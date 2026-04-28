// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BIBIM_MVP
{
    /// <summary>
    /// Thin orchestrator for the Dynamo single-shot LLM call path.
    /// Builds a fully static system prompt (cacheable via cache_control), then routes
    /// the request through <see cref="LlmApiClientFactory"/> to the matching provider.
    ///
    /// Cache strategy: per-call dynamic context (RAG docs / prior analysis) is injected
    /// into the most recent user message rather than concatenated to the system prompt,
    /// so the system prompt prefix stays identical across calls and prompt-caching hits
    /// on every call after the first within a 5-minute window.
    /// </summary>
    internal static class ClaudeApiClient
    {
        // Per-call max_tokens budgets sized to observed real outputs:
        //   spec / autofix: short structured edits (~600-2000 tokens)
        //   codegen:        full Python script (~2500-4000 tokens)
        //   analysis:       diagnosis report (~1500-3000 tokens)
        // Cap is generous enough to absorb verbose runs without truncation, but tight
        // enough to prevent runaway max_tokens responses on misbehaving models.
        public const int MaxTokensSpec = 2048;
        public const int MaxTokensAutoFix = 2048;
        public const int MaxTokensCodegen = 4096;
        public const int MaxTokensAnalysis = 3072;

        /// <summary>
        /// Shared HttpClient used by every provider adapter. One instance avoids
        /// socket exhaustion across the many short-lived calls in the pipeline.
        /// </summary>
        internal static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Sends a conversation to the active LLM and returns the raw text response.
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
            // System prompt is purely static (depends only on revit/dynamo version + flag),
            // so the prompt-cache hit covers the entire system block.
            string systemPrompt = callType == "graph_analysis"
                ? BuildAnalysisSystemPrompt(revitVersion, dynamoVersion)
                : CodeGenSystemPrompt.Build(revitVersion, dynamoVersion, isCodeGeneration);

            // Per-call dynamic context goes into the latest user message, keeping the
            // system prompt cache-stable.
            var augmentedHistory = AugmentLastUserMessage(history, apiDocContext, analysisContext);

            int budget = callType == "graph_analysis" ? MaxTokensAnalysis : MaxTokensCodegen;

            var client = LlmApiClientFactory.Create(apiKey, model);
            var response = await client.SendMessageAsync(augmentedHistory, systemPrompt, budget, requestId, callType, cancellationToken);

            if (!response.IsSuccess)
            {
                if (!string.IsNullOrEmpty(response.ErrorMessage))
                    return response.ErrorMessage;
                return LocalizationService.Get("Spec_Error_EmptyResponse");
            }

            if (response.StopReason == "max_tokens")
            {
                string truncMsg = callType == "graph_analysis"
                    ? LocalizationService.Get("Analysis_ResponseTruncated")
                    : LocalizationService.Get("Code_ResponseTruncated");
                return truncMsg + "\n\n" + response.Text;
            }

            return response.Text;
        }

        /// <summary>
        /// Auto-fix call used by the validation gate. Reuses the codegen system prompt
        /// so the cached prefix is shared with the original generation call (no separate
        /// cache slot for autofix). Fix-specific instructions and the failing code are
        /// built into <paramref name="prompt"/> by <see cref="AutoFixRequestBuilder"/>.
        /// </summary>
        internal static async Task<string> RequestValidationAutoFixAsync(
            string apiKey,
            string model,
            string prompt,
            string revitVersion,
            string dynamoVersion,
            string requestId,
            int attemptNo,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string systemPrompt = CodeGenSystemPrompt.Build(revitVersion, dynamoVersion, isCodeGeneration: true);

                var history = new List<ChatMessage>
                {
                    new ChatMessage { IsUser = true, Text = prompt }
                };

                var client = LlmApiClientFactory.Create(apiKey, model);
                var response = await client.SendMessageAsync(history, systemPrompt, MaxTokensAutoFix, requestId, "validation_fix", cancellationToken);

                if (!response.IsSuccess)
                {
                    Logger.Log("ClaudeApiClient", $"[VALIDATION] rid={requestId} autofix_fail attempt={attemptNo} status={response.HttpStatusCode}");
                    return string.Empty;
                }

                return response.Text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log("ClaudeApiClient", $"[VALIDATION] rid={requestId} autofix_exception attempt={attemptNo} type={ex.GetType().Name} msg={ClipForLog(ex.Message)}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Returns the API key that matches the currently configured active model.
        /// Resolves provider from the model id, then reads the matching key from
        /// config (or environment). Returns null when no key is configured.
        /// </summary>
        internal static string GetClaudeApiKey()
        {
            try
            {
                var config = ConfigService.GetRagConfig();
                string activeModel = !string.IsNullOrEmpty(config?.ClaudeModel)
                    ? config.ClaudeModel
                    : "claude-sonnet-4-6";
                string provider = LlmApiClientFactory.ResolveProviderForModel(activeModel);
                return ConfigService.GetApiKeyForProvider(config, provider);
            }
            catch (Exception ex)
            {
                Logger.LogError("ClaudeApiClient.GetClaudeApiKey", ex);
                return null;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a copy of <paramref name="history"/> where the last user message has
        /// the per-call API-doc and analysis context prepended (as separate sections).
        /// Returns the original sequence unchanged when both contexts are empty.
        /// </summary>
        private static IEnumerable<ChatMessage> AugmentLastUserMessage(
            IEnumerable<ChatMessage> history,
            string apiDocContext,
            string analysisContext)
        {
            bool hasApiDoc = !string.IsNullOrEmpty(apiDocContext);
            bool hasAnalysis = !string.IsNullOrEmpty(analysisContext);
            if (!hasApiDoc && !hasAnalysis) return history;

            var list = new List<ChatMessage>();
            foreach (var m in history) list.Add(m);

            int lastUserIdx = -1;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsUser)
                {
                    lastUserIdx = i;
                    break;
                }
            }
            if (lastUserIdx < 0) return list;

            var sb = new StringBuilder();
            if (hasApiDoc)
            {
                sb.AppendLine("[Revit API documentation context]");
                sb.AppendLine(apiDocContext);
                sb.AppendLine();
            }
            if (hasAnalysis)
            {
                sb.AppendLine("[Previous Graph Analysis context]");
                sb.AppendLine(analysisContext);
                sb.AppendLine();
            }
            sb.AppendLine("[User request]");
            sb.Append(list[lastUserIdx].Text);

            list[lastUserIdx] = new ChatMessage
            {
                IsUser = true,
                Text = sb.ToString()
            };
            return list;
        }

        private static string BuildAnalysisSystemPrompt(string revitVersion, string dynamoVersion)
        {
            bool isIronPython = revitVersion == "2022";
            string pythonEngine = isIronPython ? "IronPython 2.7" : "CPython 3.x";
            return $"You are an expert Dynamo graph analyzer for Revit {revitVersion} + Dynamo {dynamoVersion} using {pythonEngine}. " +
                   "Analyze the graph data provided by the user and produce a structured diagnostic report as instructed in the user message. " +
                   "Output only the analysis report in the exact format requested. Do not add code generation markers such as TYPE: CODE| or TYPE: GUIDE|.";
        }

        private static string ClipForLog(string text, int maxLen = 180)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string normalized = text.Replace("\r", " ").Replace("\n", " ");
            return normalized.Length <= maxLen ? normalized : normalized.Substring(0, maxLen) + "...";
        }
    }
}
