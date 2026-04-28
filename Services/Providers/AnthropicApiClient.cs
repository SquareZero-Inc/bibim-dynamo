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
    /// Anthropic Claude Messages API adapter. Sends the system prompt as a single
    /// text block tagged with <c>cache_control: ephemeral</c> so the static system
    /// prompt is served from prompt-cache on every call after the first within a
    /// 5-minute window. Reads <c>cache_creation_input_tokens</c> /
    /// <c>cache_read_input_tokens</c> from <c>usage</c> so caching effectiveness
    /// can be measured.
    /// </summary>
    internal sealed class AnthropicApiClient : ILlmApiClient
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";

        // Prompt caching is generally available, but the beta header is still accepted
        // and lets older API surfaces / preview models route correctly.
        private const string PromptCachingBetaHeader = "prompt-caching-2024-07-31";

        private readonly string _apiKey;

        public string ProviderName => "anthropic";
        public string ModelId { get; }

        public AnthropicApiClient(string apiKey, string modelId)
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
                var messages = new List<object>();
                foreach (var msg in history)
                {
                    messages.Add(new
                    {
                        role = msg.IsUser ? "user" : "assistant",
                        content = msg.Text
                    });
                }

                // System prompt as an array of blocks so cache_control can be attached.
                // Only mark the static system prompt as ephemeral; per-call user inputs
                // (RAG/analysis context) are passed as user messages and are not cached.
                var systemBlocks = new List<object>();
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    systemBlocks.Add(new
                    {
                        type = "text",
                        text = systemPrompt,
                        cache_control = new { type = "ephemeral" }
                    });
                }

                var body = new
                {
                    model = ModelId,
                    max_tokens = maxTokens,
                    system = systemBlocks,
                    messages = messages
                };

                string json =
#if NET48
                    JsonHelper.SerializeCamelCase(body);
#else
                    JsonSerializer.Serialize(body);
#endif

                using (var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    request.Headers.Add("x-api-key", _apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    request.Headers.Add("anthropic-beta", PromptCachingBetaHeader);

                    using (var response = await ClaudeApiClient._httpClient.SendAsync(request, cancellationToken))
                    {
                        result.HttpStatusCode = (int)response.StatusCode;

                        if (!response.IsSuccessStatusCode)
                        {
                            string errBody = await response.Content.ReadAsStringAsync();
                            Logger.Log("AnthropicApiClient", $"[API_ERROR] rid={requestId} status={(int)response.StatusCode} model={ModelId} body={ClipForLog(errBody)}");
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
                Logger.Log("AnthropicApiClient", $"[EXCEPTION] rid={requestId} type={ex.GetType().Name} msg={ClipForLog(ex.Message)}");
                result.IsSuccess = false;
                result.ErrorMessage = $"[API Error] {ex.GetType().Name}: {ex.Message}";
                return result;
            }
        }

        private void ParseResponse(string responseString, string callType, string requestId, LlmResponse result)
        {
#if NET48
            var root = JObject.Parse(responseString);
            result.StopReason = root["stop_reason"]?.ToString() ?? string.Empty;

            var usage = root["usage"];
            if (usage != null)
            {
                int inTok = usage["input_tokens"]?.Value<int>() ?? 0;
                int outTok = usage["output_tokens"]?.Value<int>() ?? 0;
                int cacheCreate = usage["cache_creation_input_tokens"]?.Value<int>() ?? 0;
                int cacheRead = usage["cache_read_input_tokens"]?.Value<int>() ?? 0;
                result.InputTokens = inTok;
                result.OutputTokens = outTok;
                result.CacheCreationTokens = cacheCreate;
                result.CacheReadTokens = cacheRead;
                TokenTracker.Track(callType, "claude", ModelId, inTok, outTok, requestId, cacheCreate, cacheRead);
            }

            var content = root["content"];
            if (content != null && content.HasValues)
            {
                var sb = new StringBuilder();
                foreach (var block in content)
                {
                    if (block["type"]?.ToString() == "text" && block["text"] != null)
                        sb.Append(block["text"].ToString());
                }
                result.Text = sb.ToString().Trim();
                result.IsSuccess = !string.IsNullOrEmpty(result.Text);
            }
#else
            using (JsonDocument doc = JsonDocument.Parse(responseString))
            {
                if (doc.RootElement.TryGetProperty("stop_reason", out var stopReasonProp))
                    result.StopReason = stopReasonProp.GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                {
                    int inTok = usageEl.TryGetProperty("input_tokens", out var inP) ? inP.GetInt32() : 0;
                    int outTok = usageEl.TryGetProperty("output_tokens", out var outP) ? outP.GetInt32() : 0;
                    int cacheCreate = usageEl.TryGetProperty("cache_creation_input_tokens", out var ccP) ? ccP.GetInt32() : 0;
                    int cacheRead = usageEl.TryGetProperty("cache_read_input_tokens", out var crP) ? crP.GetInt32() : 0;
                    result.InputTokens = inTok;
                    result.OutputTokens = outTok;
                    result.CacheCreationTokens = cacheCreate;
                    result.CacheReadTokens = cacheRead;
                    TokenTracker.Track(callType, "claude", ModelId, inTok, outTok, requestId, cacheCreate, cacheRead);
                }

                if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "text" &&
                            block.TryGetProperty("text", out var textProp))
                        {
                            sb.Append(textProp.GetString());
                        }
                    }
                    result.Text = sb.ToString().Trim();
                    result.IsSuccess = !string.IsNullOrEmpty(result.Text);
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
