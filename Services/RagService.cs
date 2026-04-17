// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if NET48
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Result of a RAG fetch operation, including status for user notification.
    /// </summary>
    internal sealed class RagFetchResult
    {
        public string ContextText { get; set; } = "";
        public string Status { get; set; } = "none"; // hit, no_match, http_error, timeout, exception
        public string ErrorSummary { get; set; } = "";
        public bool IsSuccess => Status == "hit";
        public bool HasContext => !string.IsNullOrEmpty(ContextText);
    }

    /// <summary>
    /// Handles all Gemini-based RAG (Retrieval-Augmented Generation) operations:
    ///   - Document retrieval from the Revit API vector store
    ///   - RAG-grounded code verification
    ///   - Keyword extraction for cache-key stability
    ///
    /// Extracted from GeminiService to separate Gemini API concerns from the main
    /// code-generation orchestration.
    /// </summary>
    internal static class RagService
    {
        private const string GeminiApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };

        // Session-scoped RAG cache: hash of (store + query keywords) → result
        private static readonly Dictionary<string, RagFetchResult> _ragCache =
            new Dictionary<string, RagFetchResult>(StringComparer.Ordinal);
        private static readonly object _ragCacheLock = new object();
        private static readonly Regex _pascalCaseRegex = new Regex(@"\b([A-Z][a-z]+(?:[A-Z][a-z]+)+)\b", RegexOptions.Compiled);

        // ── Cache management ─────────────────────────────────────────────────

        /// <summary>Returns true and sets <paramref name="result"/> if the cache contains an entry for <paramref name="key"/>.</summary>
        internal static bool TryGetCache(string key, out RagFetchResult result)
        {
            lock (_ragCacheLock)
                return _ragCache.TryGetValue(key, out result);
        }

        /// <summary>Stores <paramref name="result"/> in the session-scoped RAG cache.</summary>
        internal static void SetCache(string key, RagFetchResult result)
        {
            lock (_ragCacheLock)
                _ragCache[key] = result;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds a cache key from the RAG store name and the keywords extracted
        /// from <paramref name="specificationText"/>.
        /// </summary>
        internal static string BuildCacheKey(string ragStore, string specificationText)
            => ragStore + "|" + ExtractRagKeywords(specificationText);

        /// <summary>
        /// Fetches relevant Revit API documentation from the Gemini RAG file-search store.
        /// Returns empty context (Status = no_match / http_error / timeout / exception) on failure.
        /// </summary>
        internal static async Task<RagFetchResult> FetchRelevantApiDocsAsync(
            string apiKey,
            string specificationText,
            string ragStoreName,
            string revitVersion,
            string model,
            string requestId = null,
            int timeoutSeconds = 25)
        {
            try
            {
                string queryPreview = specificationText.Length > 200
                    ? specificationText.Substring(0, 200) + "..."
                    : specificationText;
                Logger.Log("RagService", $"[RAG_QUERY] rid={requestId} store={ragStoreName} revit={revitVersion} query_preview={queryPreview.Replace("\n", " ")}");

                string requestUrl = $"{GeminiApiBaseUrl}{model}:generateContent?key={apiKey}";
                string queryPrompt = RagQueryPrompt.Build(revitVersion, specificationText);

                var contentsList = new List<object>
                {
                    new { role = "user", parts = new[] { new { text = queryPrompt } } }
                };

                var toolsConfig = new[]
                {
                    new
                    {
                        fileSearch = new
                        {
                            fileSearchStoreNames = new[] { ragStoreName }
                        }
                    }
                };

                var requestBody = new
                {
                    contents = contentsList,
                    tools = toolsConfig,
                    generationConfig = new { temperature = 0.0, maxOutputTokens = 2048 }
                };

                var jsonContent = new StringContent(
#if NET48
                    JsonHelper.SerializeCamelCase(requestBody),
#else
                    JsonSerializer.Serialize(requestBody),
#endif
                    Encoding.UTF8,
                    "application/json");

                string responseString;
                using (var ragCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    HttpResponseMessage response;
                    try
                    {
                        using (var ragRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl))
                        {
                            ragRequest.Content = jsonContent;
                            response = await _httpClient.SendAsync(ragRequest, ragCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return new RagFetchResult { Status = "timeout", ErrorSummary = $"RAG query timed out ({timeoutSeconds}s)" };
                    }
                    using (response)
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.Log("RagService", $"[RAG_ERROR] rid={requestId} http_status={response.StatusCode}");
                            return new RagFetchResult { Status = "http_error", ErrorSummary = $"HTTP {response.StatusCode}" };
                        }
                        responseString = await response.Content.ReadAsStringAsync();
                    }
                }
                TrackGeminiTokenUsage(responseString, "rag_query", model, requestId);
#if NET48
                var responseObj = JObject.Parse(responseString);
                var candidates = responseObj["candidates"];
                if (candidates != null && candidates.HasValues)
                {
                    var content = candidates[0]["content"];
                    var parts = content?["parts"];
                    if (parts != null && parts.HasValues)
                    {
                        var textBuilder = new StringBuilder();
                        foreach (var part in parts)
                        {
                            if (part["text"] != null)
                                textBuilder.Append(part["text"].ToString());
                        }
                        string result = textBuilder.ToString().Trim();
                        if (result.Contains("NO_RELEVANT_API_FOUND"))
                        {
                            Logger.Log("RagService", $"[RAG_RESULT] rid={requestId} status=no_match");
                            return new RagFetchResult { Status = "no_match" };
                        }
                        string resultPreview = result.Length > 500 ? result.Substring(0, 500) + "..." : result;
                        Logger.Log("RagService", $"[RAG_RESULT] rid={requestId} status=hit length={result.Length} preview={resultPreview.Replace("\n", " | ")}");
                        return new RagFetchResult { ContextText = result, Status = "hit" };
                    }
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        if (candidates[0].TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            var textBuilder = new StringBuilder();
                            foreach (var part in parts.EnumerateArray())
                            {
                                if (part.TryGetProperty("text", out var textProp))
                                    textBuilder.Append(textProp.GetString());
                            }
                            string result = textBuilder.ToString().Trim();
                            if (result.Contains("NO_RELEVANT_API_FOUND"))
                            {
                                Logger.Log("RagService", $"[RAG_RESULT] rid={requestId} status=no_match");
                                return new RagFetchResult { Status = "no_match" };
                            }
                            string resultPreview = result.Length > 500 ? result.Substring(0, 500) + "..." : result;
                            Logger.Log("RagService", $"[RAG_RESULT] rid={requestId} status=hit length={result.Length} preview={resultPreview.Replace("\n", " | ")}");
                            return new RagFetchResult { ContextText = result, Status = "hit" };
                        }
                    }
                }
#endif
                Logger.Log("RagService", $"[RAG_RESULT] rid={requestId} status=empty_response");
                return new RagFetchResult { Status = "no_match", ErrorSummary = "Empty response from Gemini" };
            }
            catch (Exception ex)
            {
                Logger.Log("RagService", $"[RAG_ERROR] rid={requestId} exception={ex.GetType().Name}: {ex.Message}");
                return new RagFetchResult { Status = "exception", ErrorSummary = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }

        /// <summary>
        /// Uses Gemini with RAG file-search to verify and optionally improve generated Python code.
        /// Returns the (possibly corrected) code, an outcome tag, and a detail string for logging.
        /// Falls back to the original code on any failure.
        /// </summary>
        internal static async Task<(string Code, string Outcome, string Detail)> VerifyAndFixCodeAsync(
            string apiKey,
            string pythonCode,
            string ragStoreName,
            string revitVersion,
            string dynamoVersion,
            string model,
            string requestId = null,
            int timeoutSeconds = 30)
        {
            try
            {
                string requestUrl = $"{GeminiApiBaseUrl}{model}:generateContent?key={apiKey}";
                Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=start model={model} store={ragStoreName} code_len={pythonCode?.Length ?? 0} timeout={timeoutSeconds}s");

                string verificationPrompt = RagVerificationPrompt.Build(revitVersion, dynamoVersion, pythonCode);

                var contentsList = new List<object>
                {
                    new { role = "user", parts = new[] { new { text = verificationPrompt } } }
                };

                var toolsConfig = new[]
                {
                    new
                    {
                        fileSearch = new
                        {
                            fileSearchStoreNames = new[] { ragStoreName }
                        }
                    }
                };

                var requestBody = new
                {
                    contents = contentsList,
                    tools = toolsConfig,
                    generationConfig = new { temperature = 0.0 }
                };

                var jsonContent = new StringContent(
#if NET48
                    JsonHelper.SerializeCamelCase(requestBody),
#else
                    JsonSerializer.Serialize(requestBody),
#endif
                    Encoding.UTF8,
                    "application/json");

                string responseString;
                using (var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    HttpResponseMessage response;
                    try
                    {
                        using (var verifyRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl))
                        {
                            verifyRequest.Content = jsonContent;
                            response = await _httpClient.SendAsync(verifyRequest, verifyCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=timeout ({timeoutSeconds}s)");
                        return (pythonCode, "fallback_timeout", $"timed_out_{timeoutSeconds}s");
                    }
                    using (response)
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string errorBody = await response.Content.ReadAsStringAsync();
                            Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=http_error status={(int)response.StatusCode} body={ClipForLog(errorBody)}");
                            return (pythonCode, "fallback_http_error", ((int)response.StatusCode).ToString());
                        }
                        responseString = await response.Content.ReadAsStringAsync();
                    }
                }
                TrackGeminiTokenUsage(responseString, "rag_verify", model, requestId);
#if NET48
                var responseObj = JObject.Parse(responseString);
                var candidates = responseObj["candidates"];
                if (candidates != null && candidates.HasValues)
                {
                    var content = candidates[0]["content"];
                    var parts = content?["parts"];
                    if (parts != null && parts.HasValues)
                    {
                        var textBuilder = new StringBuilder();
                        foreach (var part in parts)
                        {
                            if (part["text"] != null)
                                textBuilder.Append(part["text"].ToString());
                        }
                        string finalCode = textBuilder.ToString().Trim();
                        Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=ok output_len={finalCode.Length}");
                        return (finalCode, "ok", $"output_len:{finalCode.Length}");
                    }
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        if (candidates[0].TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            var textBuilder = new StringBuilder();
                            foreach (var part in parts.EnumerateArray())
                            {
                                if (part.TryGetProperty("text", out var textProp))
                                    textBuilder.Append(textProp.GetString());
                            }
                            string finalCode = textBuilder.ToString().Trim();
                            Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=ok output_len={finalCode.Length}");
                            return (finalCode, "ok", $"output_len:{finalCode.Length}");
                        }
                    }
                }
#endif
                Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=empty_response body={ClipForLog(responseString)}");
                return (pythonCode, "fallback_empty_response", "no_candidates_or_text");
            }
            catch (TaskCanceledException ex)
            {
                Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=timeout_or_cancel msg={ClipForLog(ex.Message)}");
                return (pythonCode, "fallback_timeout_or_cancel", "TaskCanceledException");
            }
            catch (HttpRequestException ex)
            {
                Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=http_exception msg={ClipForLog(ex.Message)}");
                return (pythonCode, "fallback_http_exception", "HttpRequestException");
            }
            catch (Exception ex)
            {
                Logger.Log("RagService", $"[VERIFY] rid={requestId} phase=exception type={ex.GetType().Name} msg={ClipForLog(ex.Message)}");
                return (pythonCode, "fallback_exception", ex.GetType().Name);
            }
        }

        /// <summary>
        /// Extracts Revit API keyword candidates from a specification text for use as a
        /// stable RAG cache key (more stable than using the full spec text verbatim).
        /// </summary>
        internal static string ExtractRagKeywords(string specificationText)
        {
            if (string.IsNullOrWhiteSpace(specificationText))
                return "";

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] revitApiPatterns = {
                "FilteredElementCollector", "Element", "Wall", "Floor", "Ceiling", "Door", "Window",
                "Room", "FamilyInstance", "FamilySymbol", "Parameter", "BuiltInParameter", "BuiltInCategory",
                "Transaction", "TransactionManager", "DocumentManager", "Document",
                "ElementId", "XYZ", "Line", "Curve", "CurveLoop", "Solid", "GeometryElement",
                "View", "ViewPlan", "ViewSection", "ViewSheet", "Schedule", "ScheduleField",
                "Level", "Grid", "ReferencePlane", "Dimension",
                "Material", "Category", "WorksetTable", "Workset",
                "ExportDWGSettings", "DWGExportOptions", "ImageExportOptions",
                "Connector", "MEPSystem", "Pipe", "Duct", "CableTray",
                "StructuralType", "StairsRun", "Railing", "Roof",
                "Area", "Length", "Volume", "UnitUtils", "UnitTypeId",
                "Selection", "Reference", "BoundingBoxXYZ", "Transform",
                "RevitLinkInstance", "ImportInstance",
                "ProtoGeometry", "Point", "Vector", "Surface", "PolySurface"
            };

            string specLower = specificationText.ToLowerInvariant();
            foreach (var pattern in revitApiPatterns)
            {
                if (specLower.Contains(pattern.ToLowerInvariant()))
                    keywords.Add(pattern);
            }

            foreach (Match m in _pascalCaseRegex.Matches(specificationText))
            {
                string word = m.Groups[1].Value;
                if (word.Length >= 4 && word.Length <= 40)
                    keywords.Add(word);
            }

            if (keywords.Count == 0)
                return specificationText.Length > 500 ? specificationText.Substring(0, 500) : specificationText;

            return "Revit API keywords for lookup: " + string.Join(", ", keywords);
        }

        /// <summary>Reads the Gemini API key from config or environment variable.</summary>
        internal static string GetGeminiApiKey()
        {
            try
            {
                var config = ConfigService.GetRagConfig();
                if (config != null && !string.IsNullOrEmpty(config.GeminiApiKey) &&
                    config.GeminiApiKey != "GEMINI_API_KEY_HERE")
                    return config.GeminiApiKey;

                string envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                if (!string.IsNullOrEmpty(envKey))
                    return envKey;

                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError("RagService.GetGeminiApiKey", ex);
                return null;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static void TrackGeminiTokenUsage(string responseString, string callType, string model, string requestId)
        {
            try
            {
#if NET48
                var responseObj = JObject.Parse(responseString);
                var usage = responseObj["usageMetadata"];
                if (usage != null)
                {
                    int inTok = usage["promptTokenCount"]?.Value<int>() ?? 0;
                    int outTok = usage["candidatesTokenCount"]?.Value<int>() ?? 0;
                    TokenTracker.Track(callType, "gemini", model, inTok, outTok, requestId);
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
                    {
                        int inTok = usage.TryGetProperty("promptTokenCount", out var inP) ? inP.GetInt32() : 0;
                        int outTok = usage.TryGetProperty("candidatesTokenCount", out var outP) ? outP.GetInt32() : 0;
                        TokenTracker.Track(callType, "gemini", model, inTok, outTok, requestId);
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Logger.Log("RagService", $"TrackGeminiTokenUsage error: {ex.Message}");
            }
        }

        private static string ClipForLog(string text, int maxLen = 180)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string normalized = text.Replace("\r", " ").Replace("\n", " ");
            return normalized.Length <= maxLen ? normalized : normalized.Substring(0, maxLen) + "...";
        }
    }
}
