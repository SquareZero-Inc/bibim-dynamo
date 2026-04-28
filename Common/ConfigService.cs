// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
#if NET48
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
using System.Text.Json.Nodes;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Centralized configuration service for BIBIM. Reads settings from rag_config.json
    /// and exposes provider-aware helpers for the multi-provider LLM pipeline.
    /// </summary>
    public static class ConfigService
    {
        private static RagConfig _cachedConfig;
        private static readonly object _lock = new object();

        /// <summary>Default model id used when none is configured.</summary>
        public const string DefaultModelId = "claude-sonnet-4-6";

        // ── Available models exposed to the UI ───────────────────────────────

        /// <summary>
        /// Catalog entry for an LLM model exposed in the settings dialog.
        /// Provider is derived from the id by <see cref="LlmApiClientFactory.ResolveProviderForModel"/>.
        /// </summary>
        public sealed class AvailableModel
        {
            public string Id { get; set; }
            public string Provider { get; set; }
            public string DisplayName { get; set; }
            public string Note { get; set; }
            public bool Recommended { get; set; }
        }

        /// <summary>
        /// Models offered in the API Key Setup dialog. Order = display order.
        /// Keep IDs in sync with REVIT (`Bibim.Core/Common/ConfigService.cs`).
        /// </summary>
        public static readonly IReadOnlyList<AvailableModel> AvailableModels = new List<AvailableModel>
        {
            new AvailableModel { Id = "claude-sonnet-4-6", Provider = "anthropic", DisplayName = "Claude Sonnet 4.6", Note = "Best balance ★ Recommended", Recommended = true },
            new AvailableModel { Id = "claude-opus-4-7",   Provider = "anthropic", DisplayName = "Claude Opus 4.7",   Note = "Most capable (Apr 2026)" },
            new AvailableModel { Id = "gpt-5.5",            Provider = "openai",    DisplayName = "GPT-5.5",            Note = "OpenAI flagship" },
            new AvailableModel { Id = "gemini-3.1-pro-preview", Provider = "gemini", DisplayName = "Gemini 3.1 Pro",    Note = "Google preview" }
        };

        /// <summary>Configuration data loaded from rag_config.json.</summary>
        public class RagConfig
        {
            // RagStore / FallbackStore are still read from JSON for backward compatibility
            // with the installer and older configs, but are no longer consumed at runtime
            // — the local BM25 RAG (LocalDynamoRagService) replaced fileSearch in v1.0.2.
            public string RagStore { get; set; }
            public string FallbackStore { get; set; }
            public string RevitVersion { get; set; }
            public string DynamoVersion { get; set; }

            /// <summary>Active model id. Stored in JSON as <c>claude_model</c> for backward compat.</summary>
            public string ClaudeModel { get; set; }

            public string GeminiModel { get; set; }

            // ── API keys (loaded from config file or environment variables) ──
            // ClaudeApiKey is an alias for AnthropicApiKey kept for legacy callers.
            public string ClaudeApiKey { get; set; }
            public string AnthropicApiKey { get; set; }
            public string OpenAIApiKey { get; set; }
            public string GeminiApiKey { get; set; }

            // ── Validation pipeline feature flags ────────────────────────────
            public bool ValidationGateEnabled { get; set; }
            public bool AutoFixEnabled { get; set; }
            public int AutoFixMaxAttempts { get; set; }
            public bool EnableApiXmlHints { get; set; }
            public string ValidationRolloutPhase { get; set; }
            // VerifyStageEnabled / VerifyTimeoutSeconds / RagTimeoutSeconds removed
            // in v1.0.2 along with the Gemini verify stage and legacy fileSearch RAG.
        }

        // ── Config load / cache ───────────────────────────────────────────────

        /// <summary>Returns the cached config, loading lazily on first access.</summary>
        public static RagConfig GetRagConfig()
        {
            lock (_lock)
            {
                if (_cachedConfig != null)
                    return _cachedConfig;

                _cachedConfig = LoadRagConfig();
                return _cachedConfig;
            }
        }

        /// <summary>Drops the cached config so the next call re-reads from disk.</summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _cachedConfig = null;
            }
        }

        /// <summary>UI display tuple: (revit, dynamo, store name only, model summary).</summary>
        public static (string revitVersion, string dynamoVersion, string ragStoreName, string modelInfo) GetDisplayInfo()
        {
            var config = GetRagConfig();

            string displayStore = config.RagStore ?? string.Empty;
            if (!string.IsNullOrEmpty(displayStore) && displayStore.Contains("/"))
                displayStore = displayStore.Substring(displayStore.LastIndexOf("/") + 1);

            return (
                config.RevitVersion,
                config.DynamoVersion,
                displayStore,
                $"Active model: {config.ClaudeModel}"
            );
        }

        // ── Provider-aware helpers ────────────────────────────────────────────

        /// <summary>
        /// Returns the API key matching <paramref name="providerName"/> from the supplied
        /// config (or environment variables when the config key is empty). Returns null when
        /// no key is configured.
        /// </summary>
        public static string GetApiKeyForProvider(RagConfig config, string providerName)
        {
            if (config == null) config = GetRagConfig();
            if (string.IsNullOrEmpty(providerName)) providerName = LlmApiClientFactory.ProviderAnthropic;

            string key = null;
            switch (providerName)
            {
                case LlmApiClientFactory.ProviderOpenAI:
                    key = NullIfEmpty(config.OpenAIApiKey)
                          ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    break;
                case LlmApiClientFactory.ProviderGemini:
                    key = NullIfEmpty(config.GeminiApiKey)
                          ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                    break;
                case LlmApiClientFactory.ProviderAnthropic:
                default:
                    key = NullIfEmpty(config.AnthropicApiKey)
                          ?? NullIfEmpty(config.ClaudeApiKey)
                          ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                          ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
                    break;
            }

            return string.IsNullOrEmpty(key) || IsPlaceholder(key) ? null : key;
        }

        /// <summary>True iff an API key is configured (config or env) for the given provider.</summary>
        public static bool HasKeyForProvider(RagConfig config, string providerName)
            => !string.IsNullOrEmpty(GetApiKeyForProvider(config, providerName));

        /// <summary>
        /// Returns a masked version of the configured key for display (first 6 chars + "…")
        /// or empty string when no key is configured.
        /// </summary>
        public static string GetMaskedKeyForProvider(RagConfig config, string providerName)
        {
            string key = GetApiKeyForProvider(config, providerName);
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (key.Length <= 8) return new string('•', key.Length);
            return key.Substring(0, 6) + new string('•', 16);
        }

        /// <summary>
        /// Persists an API key for the given provider to rag_config.json.
        /// "anthropic" mirrors the value to both <c>anthropic_api_key</c> and <c>claude_api_key</c>
        /// so older readers keep working. Creates a one-time <c>rag_config.json.bak</c> backup.
        /// Caller is expected to <see cref="ClearCache"/> after a batch of saves.
        /// </summary>
        public static void SaveApiKeyForProvider(string providerName, string apiKey)
        {
            string fieldKey;
            switch (providerName)
            {
                case LlmApiClientFactory.ProviderOpenAI: fieldKey = "openai_api_key"; break;
                case LlmApiClientFactory.ProviderGemini: fieldKey = "gemini_api_key"; break;
                default: fieldKey = "anthropic_api_key"; break;
            }

            MutateConfigFile(node =>
            {
                EnsureApiKeysObject(node);
                SetApiKeyField(node, fieldKey, apiKey ?? string.Empty);
                if (providerName == LlmApiClientFactory.ProviderAnthropic)
                {
                    // Mirror to legacy field for older readers.
                    SetApiKeyField(node, "claude_api_key", apiKey ?? string.Empty);
                }
            });
        }

        /// <summary>Persists the active model id (stored under <c>claude_model</c> for backward compat).</summary>
        public static void SetActiveModel(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            MutateConfigFile(node => SetTopField(node, "claude_model", modelId));
        }

        // ── Loader ────────────────────────────────────────────────────────────

        private static RagConfig LoadRagConfig()
        {
            string store = null;
            string fallbackStore = null;
            string version = null;
            string dynamoVersion = null;
            string claudeModel = null;
            string geminiModel = null;
            string claudeApiKey = null;
            string anthropicApiKey = null;
            string openAIApiKey = null;
            string geminiApiKey = null;
            bool validationGateEnabled = true;
            bool autoFixEnabled = true;
            int autoFixMaxAttempts = 2;
            bool enableApiXmlHints = true;
            string validationRolloutPhase = "phase3";

            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(assemblyDir, "rag_config.json");

                if (!File.Exists(configPath))
                {
                    // Fall back to template so first-launch users get sensible defaults
                    // without crashing the extension.
                    string templatePath = Path.Combine(assemblyDir, "rag_config.template.json");
                    if (File.Exists(templatePath))
                    {
                        File.Copy(templatePath, configPath, overwrite: false);
                    }
                    else
                    {
                        throw new FileNotFoundException($"rag_config.json not found at: {configPath}");
                    }
                }

                string json = File.ReadAllText(configPath);

#if NET48
                var configObj = JObject.Parse(json);

                if (configObj["fallback_store"] != null)
                    fallbackStore = configObj["fallback_store"].ToString();
                if (configObj["active_store"] != null)
                {
                    string storeValue = configObj["active_store"].ToString();
                    if (!string.IsNullOrEmpty(storeValue)) store = storeValue;
                }
                if (configObj["detected_revit_version"] != null)
                {
                    string versionValue = configObj["detected_revit_version"].ToString();
                    if (!string.IsNullOrEmpty(versionValue)) version = versionValue;
                }
                if (configObj["detected_dynamo_version"] != null)
                {
                    dynamoVersion = configObj["detected_dynamo_version"].ToString();
                }
                if (configObj["claude_model"] != null)
                    claudeModel = configObj["claude_model"].ToString();
                if (configObj["gemini_model"] != null)
                    geminiModel = configObj["gemini_model"].ToString();

                if (configObj["api_keys"] != null)
                {
                    var apiKeys = configObj["api_keys"] as JObject;
                    if (apiKeys["anthropic_api_key"] != null)
                        anthropicApiKey = apiKeys["anthropic_api_key"].ToString();
                    if (apiKeys["claude_api_key"] != null)
                        claudeApiKey = apiKeys["claude_api_key"].ToString();
                    if (apiKeys["openai_api_key"] != null)
                        openAIApiKey = apiKeys["openai_api_key"].ToString();
                    if (apiKeys["gemini_api_key"] != null)
                        geminiApiKey = apiKeys["gemini_api_key"].ToString();
                }

                if (configObj["validation"] != null)
                {
                    var v = configObj["validation"] as JObject;
                    if (v["gate_enabled"] != null && bool.TryParse(v["gate_enabled"].ToString(), out var b1)) validationGateEnabled = b1;
                    if (v["auto_fix_enabled"] != null && bool.TryParse(v["auto_fix_enabled"].ToString(), out var b2)) autoFixEnabled = b2;
                    if (v["auto_fix_max_attempts"] != null && int.TryParse(v["auto_fix_max_attempts"].ToString(), out var i1)) autoFixMaxAttempts = i1;
                    if (v["enable_api_xml_hints"] != null && bool.TryParse(v["enable_api_xml_hints"].ToString(), out var b4)) enableApiXmlHints = b4;
                    if (v["rollout_phase"] != null)
                    {
                        var phase = v["rollout_phase"].ToString();
                        if (!string.IsNullOrWhiteSpace(phase)) validationRolloutPhase = phase;
                    }
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("fallback_store", out var fallbackStoreProp))
                        fallbackStore = fallbackStoreProp.GetString();
                    if (doc.RootElement.TryGetProperty("active_store", out var activeStore))
                    {
                        string storeValue = activeStore.GetString();
                        if (!string.IsNullOrEmpty(storeValue)) store = storeValue;
                    }
                    if (doc.RootElement.TryGetProperty("detected_revit_version", out var revitVer))
                    {
                        string versionValue = revitVer.GetString();
                        if (!string.IsNullOrEmpty(versionValue)) version = versionValue;
                    }
                    if (doc.RootElement.TryGetProperty("detected_dynamo_version", out var dynamoVer))
                        dynamoVersion = dynamoVer.GetString();
                    if (doc.RootElement.TryGetProperty("claude_model", out var claudeModelProp))
                        claudeModel = claudeModelProp.GetString();
                    if (doc.RootElement.TryGetProperty("gemini_model", out var geminiModelProp))
                        geminiModel = geminiModelProp.GetString();

                    if (doc.RootElement.TryGetProperty("api_keys", out var apiKeysElement))
                    {
                        if (apiKeysElement.TryGetProperty("anthropic_api_key", out var anthKeyProp))
                            anthropicApiKey = anthKeyProp.GetString();
                        if (apiKeysElement.TryGetProperty("claude_api_key", out var claudeKeyProp))
                            claudeApiKey = claudeKeyProp.GetString();
                        if (apiKeysElement.TryGetProperty("openai_api_key", out var openAIKeyProp))
                            openAIApiKey = openAIKeyProp.GetString();
                        if (apiKeysElement.TryGetProperty("gemini_api_key", out var geminiKeyProp))
                            geminiApiKey = geminiKeyProp.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("validation", out var v))
                    {
                        if (v.TryGetProperty("gate_enabled", out var gateP) &&
                            (gateP.ValueKind == JsonValueKind.True || gateP.ValueKind == JsonValueKind.False))
                            validationGateEnabled = gateP.GetBoolean();
                        if (v.TryGetProperty("auto_fix_enabled", out var afEnP) &&
                            (afEnP.ValueKind == JsonValueKind.True || afEnP.ValueKind == JsonValueKind.False))
                            autoFixEnabled = afEnP.GetBoolean();
                        if (v.TryGetProperty("auto_fix_max_attempts", out var afAtP) &&
                            afAtP.ValueKind == JsonValueKind.Number)
                            autoFixMaxAttempts = afAtP.GetInt32();
                        if (v.TryGetProperty("enable_api_xml_hints", out var xmlP) &&
                            (xmlP.ValueKind == JsonValueKind.True || xmlP.ValueKind == JsonValueKind.False))
                            enableApiXmlHints = xmlP.GetBoolean();
                        if (v.TryGetProperty("rollout_phase", out var phaseP))
                        {
                            string phase = phaseP.GetString();
                            if (!string.IsNullOrWhiteSpace(phase)) validationRolloutPhase = phase;
                        }
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load rag_config.json: {ex.Message}", ex);
            }

            // Migration: anthropic_api_key absent → fall back to legacy claude_api_key.
            if (string.IsNullOrEmpty(anthropicApiKey) && !string.IsNullOrEmpty(claudeApiKey))
                anthropicApiKey = claudeApiKey;
            // Mirror back so legacy callers reading ClaudeApiKey see the new field too.
            if (string.IsNullOrEmpty(claudeApiKey) && !string.IsNullOrEmpty(anthropicApiKey))
                claudeApiKey = anthropicApiKey;

            if (string.IsNullOrEmpty(claudeModel))
                claudeModel = DefaultModelId;

            return new RagConfig
            {
                RagStore = store ?? string.Empty,
                FallbackStore = fallbackStore ?? string.Empty,
                RevitVersion = version ?? "Unknown",
                DynamoVersion = dynamoVersion ?? "Unknown",
                ClaudeModel = claudeModel,
                GeminiModel = geminiModel ?? string.Empty,
                ClaudeApiKey = claudeApiKey,
                AnthropicApiKey = anthropicApiKey,
                OpenAIApiKey = openAIApiKey,
                GeminiApiKey = geminiApiKey,
                ValidationGateEnabled = validationGateEnabled,
                AutoFixEnabled = autoFixEnabled,
                AutoFixMaxAttempts = autoFixMaxAttempts < 0 ? 0 : autoFixMaxAttempts,
                EnableApiXmlHints = enableApiXmlHints,
                ValidationRolloutPhase = string.IsNullOrWhiteSpace(validationRolloutPhase) ? "phase3" : validationRolloutPhase
            };
        }

        // ── File mutation helpers ─────────────────────────────────────────────

        /// <summary>
        /// Atomically reads, mutates, and writes rag_config.json. Creates a one-time
        /// <c>rag_config.json.bak</c> backup before the first save.
        /// </summary>
        private static void MutateConfigFile(Action<object> mutate)
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configPath = Path.Combine(assemblyDir, "rag_config.json");
            string backupPath = configPath + ".bak";
            string templatePath = Path.Combine(assemblyDir, "rag_config.template.json");

            string json;
            if (File.Exists(configPath))
            {
                json = File.ReadAllText(configPath);
                if (!File.Exists(backupPath))
                {
                    try { File.Copy(configPath, backupPath, overwrite: false); }
                    catch (Exception ex) { Logger.Log("ConfigService", $"Backup failed: {ex.Message}"); }
                }
            }
            else if (File.Exists(templatePath))
            {
                json = File.ReadAllText(templatePath);
            }
            else
            {
                json = "{}";
            }

#if NET48
            var node = JObject.Parse(json);
            mutate(node);
            File.WriteAllText(configPath, node.ToString(Formatting.Indented));
#else
            var node = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            mutate(node);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, node.ToJsonString(options));
#endif
            ClearCache();
        }

        private static void EnsureApiKeysObject(object node)
        {
#if NET48
            var obj = (JObject)node;
            if (obj["api_keys"] == null) obj["api_keys"] = new JObject();
#else
            var obj = (JsonObject)node;
            if (obj["api_keys"] is not JsonObject) obj["api_keys"] = new JsonObject();
#endif
        }

        private static void SetApiKeyField(object node, string fieldName, string value)
        {
#if NET48
            var obj = (JObject)node;
            ((JObject)obj["api_keys"])[fieldName] = value;
#else
            var obj = (JsonObject)node;
            ((JsonObject)obj["api_keys"])[fieldName] = value;
#endif
        }

        private static void SetTopField(object node, string fieldName, string value)
        {
#if NET48
            var obj = (JObject)node;
            obj[fieldName] = value;
#else
            var obj = (JsonObject)node;
            obj[fieldName] = value;
#endif
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static bool IsPlaceholder(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            return key == "CLAUDE_API_KEY_HERE"
                || key == "ANTHROPIC_API_KEY_HERE"
                || key == "OPENAI_API_KEY_HERE"
                || key == "GEMINI_API_KEY_HERE"
                || key == "YOUR_ANTHROPIC_API_KEY_HERE";
        }
    }
}
