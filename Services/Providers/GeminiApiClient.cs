// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET48
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Google Gemini API adapter for single-shot text generation
    /// (POST /v1beta/models/{model}:generateContent?key=...).
    ///
    /// System prompt → top-level <c>systemInstruction</c>.
    /// History → <c>contents[]</c> with role mapping (user → "user", assistant → "model").
    /// </summary>
    internal sealed class GeminiApiClient : ILlmApiClient
    {
        private const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
        private readonly string _apiKey;

        public string ProviderName => "gemini";
        public string ModelId { get; }

        public GeminiApiClient(string apiKey, string modelId)
        {
            _apiKey = apiKey;
            ModelId = modelId;
        }

        public async Task<LlmResponse> SendMessageAsync(
            IEnumerable<ChatMessage> history,
            string systemPrompt,
            int maxTokens,
            string requestId,
            string callType,
            CancellationToken cancellationToken)
        {
            var result = new LlmResponse();
            try
            {
                // Gemini contents[] uses role: "user" / "model" (not "assistant").
                // Each entry has a parts[] with text segments.
                var contents = new List<object>();
                foreach (var msg in history)
                {
                    contents.Add(new
                    {
                        role = msg.IsUser ? "user" : "model",
                        parts = new[] { new { text = msg.Text ?? string.Empty } }
                    });
                }

                object body;
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    body = new
                    {
                        systemInstruction = new
                        {
                            parts = new[] { new { text = systemPrompt } }
                        },
                        contents = contents,
                        generationConfig = new { maxOutputTokens = maxTokens }
                    };
                }
                else
                {
                    body = new
                    {
                        contents = contents,
                        generationConfig = new { maxOutputTokens = maxTokens }
                    };
                }

                string json =
#if NET48
                    JsonHelper.SerializeCamelCase(body);
#else
                    JsonSerializer.Serialize(body);
#endif

                string requestUrl = $"{ApiBaseUrl}{ModelId}:generateContent?key={_apiKey}";

                using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var response = await ClaudeApiClient._httpClient.SendAsync(request, cancellationToken))
                    {
                        result.HttpStatusCode = (int)response.StatusCode;

                        if (!response.IsSuccessStatusCode)
                        {
                            string errBody = await response.Content.ReadAsStringAsync();
                            Logger.Log("GeminiApiClient", $"[API_ERROR] rid={requestId} status={(int)response.StatusCode} model={ModelId} body={ClipForLog(errBody)}");
                            result.IsSuccess = false;
                            result.ErrorMessage = $"[API Error] {response.StatusCode}\nModel: {ModelId}\n{errBody}";
                            return result;
                        }

                        string responseString = await response.Content.ReadAsStringAsync();
                        ParseResponse(responseString, callType, requestId, result);
                        return result;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log("GeminiApiClient", $"[EXCEPTION] rid={requestId} type={ex.GetType().Name} msg={ClipForLog(ex.Message)}");
                result.IsSuccess = false;
                result.ErrorMessage = $"[API Error] {ex.GetType().Name}: {ex.Message}";
                return result;
            }
        }

        private void ParseResponse(string responseString, string callType, string requestId, LlmResponse result)
        {
#if NET48
            var root = JObject.Parse(responseString);

            var usage = root["usageMetadata"];
            if (usage != null)
            {
                int inTok = usage["promptTokenCount"]?.Value<int>() ?? 0;
                int outTok = usage["candidatesTokenCount"]?.Value<int>() ?? 0;
                result.InputTokens = inTok;
                result.OutputTokens = outTok;
                TokenTracker.Track(callType, "gemini", ModelId, inTok, outTok, requestId);
            }

            var candidates = root["candidates"];
            if (candidates != null && candidates.HasValues)
            {
                var first = candidates[0];
                string finishReason = first?["finishReason"]?.ToString() ?? string.Empty;
                result.StopReason = finishReason == "MAX_TOKENS" ? "max_tokens"
                                  : finishReason == "STOP"      ? "end_turn"
                                  : finishReason;

                var content = first?["content"];
                var parts = content?["parts"];
                if (parts != null && parts.HasValues)
                {
                    var sb = new StringBuilder();
                    foreach (var part in parts)
                    {
                        if (part["text"] != null)
                            sb.Append(part["text"].ToString());
                    }
                    result.Text = sb.ToString().Trim();
                    result.IsSuccess = !string.IsNullOrEmpty(result.Text);
                }
            }
#else
            using (JsonDocument doc = JsonDocument.Parse(responseString))
            {
                if (doc.RootElement.TryGetProperty("usageMetadata", out var usageEl))
                {
                    int inTok = usageEl.TryGetProperty("promptTokenCount", out var inP) ? inP.GetInt32() : 0;
                    int outTok = usageEl.TryGetProperty("candidatesTokenCount", out var outP) ? outP.GetInt32() : 0;
                    result.InputTokens = inTok;
                    result.OutputTokens = outTok;
                    TokenTracker.Track(callType, "gemini", ModelId, inTok, outTok, requestId);
                }

                if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var first = candidates[0];

                    if (first.TryGetProperty("finishReason", out var frProp))
                    {
                        string finishReason = frProp.GetString() ?? string.Empty;
                        result.StopReason = finishReason == "MAX_TOKENS" ? "max_tokens"
                                          : finishReason == "STOP"      ? "end_turn"
                                          : finishReason;
                    }

                    if (first.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textProp))
                                sb.Append(textProp.GetString());
                        }
                        result.Text = sb.ToString().Trim();
                        result.IsSuccess = !string.IsNullOrEmpty(result.Text);
                    }
                }
            }
#endif
        }

        private static string ClipForLog(string text, int maxLen = 180)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string normalized = text.Replace("\r", " ").Replace("\n", " ");
            return normalized.Length <= maxLen ? normalized : normalized.Substring(0, maxLen) + "...";
        }
    }
}
