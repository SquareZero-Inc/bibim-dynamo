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
    /// OpenAI Chat Completions API adapter. Dynamo uses single-shot text in/out,
    /// so the simpler /v1/chat/completions endpoint is sufficient (no tool loop).
    ///
    /// System prompt → first message with role=system.
    /// Assistant/user history → preserved in messages[].
    /// </summary>
    internal sealed class OpenAIApiClient : ILlmApiClient
    {
        private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
        private readonly string _apiKey;

        public string ProviderName => "openai";
        public string ModelId { get; }

        public OpenAIApiClient(string apiKey, string modelId)
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
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    messages.Add(new { role = "system", content = systemPrompt });
                }

                foreach (var msg in history)
                {
                    messages.Add(new
                    {
                        role = msg.IsUser ? "user" : "assistant",
                        content = msg.Text
                    });
                }

                // GPT-5+ models use max_completion_tokens; older gpt-4 series use max_tokens.
                bool useMaxCompletionTokens = ModelId != null && (
                    ModelId.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                    ModelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                    ModelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase));

                object body = useMaxCompletionTokens
                    ? (object)new { model = ModelId, max_completion_tokens = maxTokens, messages }
                    : new { model = ModelId, max_tokens = maxTokens, messages };

                string json =
#if NET48
                    JsonHelper.SerializeCamelCase(body);
#else
                    JsonSerializer.Serialize(body);
#endif

                using (var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    request.Headers.Add("Authorization", "Bearer " + _apiKey);

                    using (var response = await ClaudeApiClient._httpClient.SendAsync(request, cancellationToken))
                    {
                        result.HttpStatusCode = (int)response.StatusCode;

                        if (!response.IsSuccessStatusCode)
                        {
                            string errBody = await response.Content.ReadAsStringAsync();
                            Logger.Log("OpenAIApiClient", $"[API_ERROR] rid={requestId} status={(int)response.StatusCode} model={ModelId} body={ClipForLog(errBody)}");
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
                Logger.Log("OpenAIApiClient", $"[EXCEPTION] rid={requestId} type={ex.GetType().Name} msg={ClipForLog(ex.Message)}");
                result.IsSuccess = false;
                result.ErrorMessage = $"[API Error] {ex.GetType().Name}: {ex.Message}";
                return result;
            }
        }

        private void ParseResponse(string responseString, string callType, string requestId, LlmResponse result)
        {
#if NET48
            var root = JObject.Parse(responseString);

            var usage = root["usage"];
            if (usage != null)
            {
                int inTok = usage["prompt_tokens"]?.Value<int>() ?? 0;
                int outTok = usage["completion_tokens"]?.Value<int>() ?? 0;
                result.InputTokens = inTok;
                result.OutputTokens = outTok;
                TokenTracker.Track(callType, "openai", ModelId, inTok, outTok, requestId);
            }

            var choices = root["choices"];
            if (choices != null && choices.HasValues)
            {
                var first = choices[0];
                string finishReason = first?["finish_reason"]?.ToString() ?? string.Empty;
                // Normalize: "length" → "max_tokens" (matches Anthropic stop_reason for caller logic)
                result.StopReason = finishReason == "length" ? "max_tokens"
                                  : finishReason == "stop"   ? "end_turn"
                                  : finishReason;

                var message = first?["message"];
                string text = message?["content"]?.ToString() ?? string.Empty;
                result.Text = text.Trim();
                result.IsSuccess = !string.IsNullOrEmpty(result.Text);
            }
#else
            using (JsonDocument doc = JsonDocument.Parse(responseString))
            {
                if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                {
                    int inTok = usageEl.TryGetProperty("prompt_tokens", out var inP) ? inP.GetInt32() : 0;
                    int outTok = usageEl.TryGetProperty("completion_tokens", out var outP) ? outP.GetInt32() : 0;
                    result.InputTokens = inTok;
                    result.OutputTokens = outTok;
                    TokenTracker.Track(callType, "openai", ModelId, inTok, outTok, requestId);
                }

                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    string finishReason = first.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? string.Empty) : string.Empty;
                    result.StopReason = finishReason == "length" ? "max_tokens"
                                      : finishReason == "stop"   ? "end_turn"
                                      : finishReason;

                    if (first.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentProp))
                    {
                        string text = contentProp.GetString() ?? string.Empty;
                        result.Text = text.Trim();
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
