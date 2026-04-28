// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;

namespace BIBIM_MVP
{
    /// <summary>
    /// Resolves a model id to a provider key and creates the matching ILlmApiClient.
    /// Provider is derived from the model id prefix — there is no separate
    /// "selected_provider" field in config, mirroring the REVIT design.
    /// </summary>
    public static class LlmApiClientFactory
    {
        public const string ProviderAnthropic = "anthropic";
        public const string ProviderOpenAI    = "openai";
        public const string ProviderGemini    = "gemini";

        /// <summary>
        /// Maps a model id to its owning provider key.
        /// Defaults to "anthropic" when the prefix is unknown so legacy configs keep working.
        /// </summary>
        public static string ResolveProviderForModel(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return ProviderAnthropic;

            if (modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                return ProviderAnthropic;
            if (modelId.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) ||
                modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase))
                return ProviderOpenAI;
            if (modelId.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                return ProviderGemini;

            return ProviderAnthropic;
        }

        /// <summary>
        /// Creates the matching ILlmApiClient for <paramref name="modelId"/> using <paramref name="apiKey"/>.
        /// </summary>
        internal static ILlmApiClient Create(string apiKey, string modelId)
        {
            string provider = ResolveProviderForModel(modelId);
            return Create(provider, apiKey, modelId);
        }

        /// <summary>
        /// Creates the matching ILlmApiClient for an explicit provider key.
        /// </summary>
        internal static ILlmApiClient Create(string providerName, string apiKey, string modelId)
        {
            switch (providerName)
            {
                case ProviderOpenAI:
                    return new OpenAIApiClient(apiKey, modelId);
                case ProviderGemini:
                    return new GeminiApiClient(apiKey, modelId);
                case ProviderAnthropic:
                default:
                    return new AnthropicApiClient(apiKey, modelId);
            }
        }
    }
}
