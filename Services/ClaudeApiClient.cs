// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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
    /// Thin HTTP client for the Anthropic Claude Messages API.
    /// Responsibilities:
    ///   - Build and send the messages-API request
    ///   - Extract the text response
    ///   - Track token usage via TokenTracker
    ///   - Provide a simplified auto-fix call for the validation gate
    ///
    /// Extracted from GeminiService to isolate Claude API concerns.
    /// </summary>
    internal static class ClaudeApiClient
    {
        private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";

        internal static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Sends a conversation to Claude and returns the raw text response.
        /// Returns a user-visible error string (starting with "[API Error]") on failure.
        /// </summary>
        internal static async Task<string> CallClaudeApiAsync(
            string apiKey,
            IEnumerable<ChatMessage> history,
            string revitVersion,
            string dynamoVersion,
            string model,
            string apiDocContext = "",
            bool isCodeGeneration = false,
            string requestId = null,
            CancellationToken cancellationToken = default,
            string analysisContext = "",
            string callType = "code_generate")
        {
            var messagesList = new List<object>();

            foreach (var msg in history)
            {
                messagesList.Add(new
                {
                    role = msg.IsUser ? "user" : "assistant",
                    content = msg.Text
                });
            }

            string systemPrompt = callType == "graph_analysis"
                ? BuildAnalysisSystemPrompt(revitVersion, dynamoVersion)
                : CodeGenSystemPrompt.Build(revitVersion, dynamoVersion, isCodeGeneration);

            if (!string.IsNullOrEmpty(apiDocContext))
                systemPrompt += CodeGenSystemPrompt.AppendRagContext(apiDocContext);

            if (!string.IsNullOrEmpty(analysisContext))
                systemPrompt += CodeGenSystemPrompt.AppendAnalysisContext(analysisContext);

            var requestBody = new
            {
                model = model,
                max_tokens = 8192,
                system = systemPrompt,
                messages = messagesList
            };

            var jsonContent = new StringContent(
#if NET48
                JsonHelper.SerializeCamelCase(requestBody),
#else
                JsonSerializer.Serialize(requestBody),
#endif
                Encoding.UTF8,
                "application/json");

            using (var request = new HttpRequestMessage(HttpMethod.Post, ClaudeApiUrl))
            {
                request.Content = jsonContent;
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");

                using (var response = await _httpClient.SendAsync(request, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMsg = await response.Content.ReadAsStringAsync();
                        Logger.Log("ClaudeApiClient", $"[API_ERROR] rid={requestId} status={(int)response.StatusCode} model={model} body={ClipForLog(errorMsg)}");
                        return $"[API Error] {response.StatusCode}\nModel: {model}\n{errorMsg}";
                    }

                    var responseString = await response.Content.ReadAsStringAsync();
#if NET48
                    var responseObj = JObject.Parse(responseString);
                    var stopReason = responseObj["stop_reason"]?.ToString() ?? "";

                    var usage = responseObj["usage"];
                    if (usage != null)
                    {
                        int inTok = usage["input_tokens"]?.Value<int>() ?? 0;
                        int outTok = usage["output_tokens"]?.Value<int>() ?? 0;
                        TokenTracker.Track(callType, "claude", model, inTok, outTok, requestId);
                    }

                    var content = responseObj["content"];
                    if (content != null && content.HasValues)
                    {
                        var textBuilder = new StringBuilder();
                        foreach (var block in content)
                        {
                            if (block["type"]?.ToString() == "text" && block["text"] != null)
                                textBuilder.Append(block["text"].ToString());
                        }
                        var result = textBuilder.ToString().Trim();
                        if (string.IsNullOrEmpty(result)) return LocalizationService.Get("Spec_Error_EmptyResponse");
                        if (stopReason == "max_tokens")
                        {
                            string truncMsg = callType == "graph_analysis"
                                ? LocalizationService.Get("Analysis_ResponseTruncated")
                                : LocalizationService.Get("Code_ResponseTruncated");
                            result = truncMsg + "\n\n" + result;
                        }
                        return result;
                    }
#else
                    using (JsonDocument doc = JsonDocument.Parse(responseString))
                    {
                        string stopReason = "";
                        if (doc.RootElement.TryGetProperty("stop_reason", out var stopReasonProp))
                            stopReason = stopReasonProp.GetString() ?? "";

                        if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                        {
                            int inTok = usageEl.TryGetProperty("input_tokens", out var inP) ? inP.GetInt32() : 0;
                            int outTok = usageEl.TryGetProperty("output_tokens", out var outP) ? outP.GetInt32() : 0;
                            TokenTracker.Track(callType, "claude", model, inTok, outTok, requestId);
                        }

                        if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
                        {
                            var textBuilder = new StringBuilder();
                            foreach (var block in content.EnumerateArray())
                            {
                                if (block.TryGetProperty("type", out var typeProp) &&
                                    typeProp.GetString() == "text" &&
                                    block.TryGetProperty("text", out var textProp))
                                {
                                    textBuilder.Append(textProp.GetString());
                                }
                            }
                            var result = textBuilder.ToString().Trim();
                            if (string.IsNullOrEmpty(result)) return LocalizationService.Get("Spec_Error_EmptyResponse");
                            if (stopReason == "max_tokens")
                            {
                                string truncMsg = callType == "graph_analysis"
                                    ? LocalizationService.Get("Analysis_ResponseTruncated")
                                    : LocalizationService.Get("Code_ResponseTruncated");
                                result = truncMsg + "\n\n" + result;
                            }
                            return result;
                        }
                    }
#endif
                }
            }
            return LocalizationService.Get("Spec_Error_ParseFailed");
        }

        /// <summary>
        /// Simplified Claude call used by the validation auto-fix gate.
        /// Sends a single user <paramref name="prompt"/> with a fixed system instruction.
        /// Returns the corrected code text, or <see cref="string.Empty"/> on failure.
        /// </summary>
        internal static async Task<string> RequestValidationAutoFixAsync(
            string apiKey,
            string model,
            string prompt,
            string requestId,
            int attemptNo,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var requestBody = new
                {
                    model = model,
                    max_tokens = 8192,
                    system = "Fix only API/runtime compatibility issues in Python code. Return only code text. CRITICAL: Never introduce Element.LevelId, ElementId.IntegerValue, CurveLoop(args), or Document.PlanTopologies - these are REMOVED in Revit 2024+. Use element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM).AsElementId() instead of Element.LevelId, elementId.Value instead of IntegerValue, and CurveLoop() with Append() instead of CurveLoop(list).",
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    }
                };

                var jsonContent = new StringContent(
                    JsonHelper.SerializeCamelCase(requestBody),
                    Encoding.UTF8,
                    "application/json");

                using (var request = new HttpRequestMessage(HttpMethod.Post, ClaudeApiUrl))
                {
                    request.Content = jsonContent;
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Log("ClaudeApiClient", $"[VALIDATION] rid={requestId} autofix_http_fail attempt={attemptNo} status={response.StatusCode}");
                        return string.Empty;
                    }

                    var responseString = await response.Content.ReadAsStringAsync();
                    TrackClaudeTokenUsage(responseString, "validation_fix", model, requestId);
#if NET48
                    var responseObj = JObject.Parse(responseString);
                    var content = responseObj["content"];
                    if (content != null && content.HasValues)
                    {
                        var textBuilder = new StringBuilder();
                        foreach (var block in content)
                        {
                            if (block["type"]?.ToString() == "text" && block["text"] != null)
                                textBuilder.Append(block["text"].ToString());
                        }
                        return textBuilder.ToString().Trim();
                    }
#else
                    using (JsonDocument doc = JsonDocument.Parse(responseString))
                    {
                        if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
                        {
                            var textBuilder = new StringBuilder();
                            foreach (var block in content.EnumerateArray())
                            {
                                if (block.TryGetProperty("type", out var typeProp) &&
                                    typeProp.GetString() == "text" &&
                                    block.TryGetProperty("text", out var textProp))
                                {
                                    textBuilder.Append(textProp.GetString());
                                }
                            }
                            return textBuilder.ToString().Trim();
                        }
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ClaudeApiClient", $"[VALIDATION] rid={requestId} autofix_exception attempt={attemptNo} type={ex.GetType().Name} msg={ClipForLog(ex.Message)}");
            }

            return string.Empty;
        }

        /// <summary>Reads the Claude API key from config or environment variable.</summary>
        internal static string GetClaudeApiKey()
        {
            try
            {
                var config = ConfigService.GetRagConfig();
                if (config != null && !string.IsNullOrEmpty(config.ClaudeApiKey) &&
                    config.ClaudeApiKey != "CLAUDE_API_KEY_HERE")
                    return config.ClaudeApiKey;

                string envKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
                if (!string.IsNullOrEmpty(envKey))
                    return envKey;

                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError("ClaudeApiClient.GetClaudeApiKey", ex);
                return null;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static string BuildAnalysisSystemPrompt(string revitVersion, string dynamoVersion)
        {
            bool isIronPython = revitVersion == "2022";
            string pythonEngine = isIronPython ? "IronPython 2.7" : "CPython 3.x";
            return $"You are an expert Dynamo graph analyzer for Revit {revitVersion} + Dynamo {dynamoVersion} using {pythonEngine}. " +
                   "Analyze the graph data provided by the user and produce a structured diagnostic report as instructed in the user message. " +
                   "Output only the analysis report in the exact format requested. Do not add code generation markers such as TYPE: CODE| or TYPE: GUIDE|.";
        }

        private static void TrackClaudeTokenUsage(string responseString, string callType, string model, string requestId)
        {
            try
            {
#if NET48
                var responseObj = JObject.Parse(responseString);
                var usage = responseObj["usage"];
                if (usage != null)
                {
                    int inTok = usage["input_tokens"]?.Value<int>() ?? 0;
                    int outTok = usage["output_tokens"]?.Value<int>() ?? 0;
                    TokenTracker.Track(callType, "claude", model, inTok, outTok, requestId);
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    if (doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        int inTok = usage.TryGetProperty("input_tokens", out var inP) ? inP.GetInt32() : 0;
                        int outTok = usage.TryGetProperty("output_tokens", out var outP) ? outP.GetInt32() : 0;
                        TokenTracker.Track(callType, "claude", model, inTok, outTok, requestId);
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Logger.Log("ClaudeApiClient", $"TrackClaudeTokenUsage error: {ex.Message}");
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
