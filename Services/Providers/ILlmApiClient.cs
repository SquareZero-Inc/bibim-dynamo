// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BIBIM_MVP
{
    /// <summary>
    /// Provider-agnostic LLM HTTP client used by Dynamo's single-shot text in/text out flows
    /// (spec generation, code generation, graph analysis, validation auto-fix).
    ///
    /// Implementations adapt the canonical inputs (chat history + system prompt) to each
    /// provider's wire format and normalize the response back into <see cref="LlmResponse"/>.
    ///
    /// Concrete implementations: AnthropicApiClient, OpenAIApiClient, GeminiApiClient.
    /// </summary>
    public interface ILlmApiClient
    {
        /// <summary>Lower-case provider key: "anthropic" / "openai" / "gemini".</summary>
        string ProviderName { get; }

        /// <summary>Provider-specific model id (e.g. "claude-sonnet-4-6", "gpt-5.5", "gemini-3.1-pro-preview").</summary>
        string ModelId { get; }

        /// <summary>
        /// Sends a conversation to the provider and returns the normalized response.
        /// On HTTP / parse failures, returns an <see cref="LlmResponse"/> with
        /// <c>IsSuccess=false</c> and a user-facing <c>ErrorMessage</c>.
        /// </summary>
        Task<LlmResponse> SendMessageAsync(
            IEnumerable<ChatMessage> history,
            string systemPrompt,
            int maxTokens,
            string requestId,
            string callType,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Normalized LLM response — common shape across all providers.
    /// Token counts are best-effort: 0 if the provider does not return usage metadata.
    /// </summary>
    public sealed class LlmResponse
    {
        /// <summary>Concatenated text output. Empty string on failure.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>True iff the call returned 200 + parseable text content.</summary>
        public bool IsSuccess { get; set; }

        /// <summary>User-facing error string (already localized when known). Empty on success.</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>HTTP status code when applicable; 0 when the call did not reach the server.</summary>
        public int HttpStatusCode { get; set; }

        /// <summary>
        /// Provider-normalized stop reason: "end_turn" / "max_tokens" / "tool_use" / "" (unknown).
        /// Used by callers to render a "response truncated" notice.
        /// </summary>
        public string StopReason { get; set; } = string.Empty;

        /// <summary>Input/prompt token count (best effort; 0 if unavailable).</summary>
        public int InputTokens { get; set; }

        /// <summary>Output/completion token count (best effort; 0 if unavailable).</summary>
        public int OutputTokens { get; set; }

        /// <summary>
        /// Tokens written to a new prompt-cache slot on this call (Anthropic
        /// <c>usage.cache_creation_input_tokens</c>). 0 when caching is unsupported,
        /// disabled, or no cache miss occurred.
        /// </summary>
        public int CacheCreationTokens { get; set; }

        /// <summary>
        /// Tokens served from prompt-cache on this call (Anthropic
        /// <c>usage.cache_read_input_tokens</c>). High values mean caching is working.
        /// </summary>
        public int CacheReadTokens { get; set; }
    }
}
