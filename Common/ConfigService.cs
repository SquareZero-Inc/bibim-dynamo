using System;
using System.IO;
using System.Reflection;
#if NET48
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Centralized configuration service for BIBIM
    /// Reads settings from rag_config.json
    /// </summary>
    public static class ConfigService
    {
        private static RagConfig _cachedConfig;
        private static readonly object _lock = new object();

        /// <summary>
        /// RAG configuration data
        /// </summary>
        public class RagConfig
        {
            public string RagStore { get; set; }
            public string FallbackStore { get; set; }
            public string RevitVersion { get; set; }
            public string DynamoVersion { get; set; }
            public string ClaudeModel { get; set; }
            public string GeminiModel { get; set; }
            
            // API Keys (loaded from config file or environment variables)
            public string ClaudeApiKey { get; set; }
            public string GeminiApiKey { get; set; }

            // Validation pipeline feature flags
            public bool ValidationGateEnabled { get; set; }
            public bool AutoFixEnabled { get; set; }
            public int AutoFixMaxAttempts { get; set; }
            public bool VerifyStageEnabled { get; set; }
            public int VerifyTimeoutSeconds { get; set; }
            public int RagTimeoutSeconds { get; set; }
            public bool EnableApiXmlHints { get; set; }
            public string ValidationRolloutPhase { get; set; }
        }

        /// <summary>
        /// Get the full RAG configuration from rag_config.json
        /// </summary>
        /// <returns>RagConfig with all settings</returns>
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

        /// <summary>
        /// Clear cached configuration (useful for testing or config reload)
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _cachedConfig = null;
            }
        }

        /// <summary>
        /// Get current config info for display in UI
        /// </summary>
        /// <returns>(revitVersion, dynamoVersion, ragStoreName, modelInfo)</returns>
        public static (string revitVersion, string dynamoVersion, string ragStoreName, string modelInfo) GetDisplayInfo()
        {
            var config = GetRagConfig();
            
            // Extract just the store name for display
            string displayStore = config.RagStore;
            if (config.RagStore.Contains("/"))
            {
                displayStore = config.RagStore.Substring(config.RagStore.LastIndexOf("/") + 1);
            }
            
            return (
                config.RevitVersion,
                config.DynamoVersion,
                displayStore,
                $"Claude: {config.ClaudeModel} / Gemini RAG: {config.GeminiModel}"
            );
        }

        private static RagConfig LoadRagConfig()
        {
            string store = null;
            string fallbackStore = null;
            string version = null;
            string dynamoVersion = null;
            string claudeModel = null;
            string geminiModel = null;
            string claudeApiKey = null;
            string geminiApiKey = null;
            bool validationGateEnabled = true;
            bool autoFixEnabled = true;
            int autoFixMaxAttempts = 2;
            bool verifyStageEnabled = false;
            int verifyTimeoutSeconds = 30;
            int ragTimeoutSeconds = 25;
            bool enableApiXmlHints = true;
            string validationRolloutPhase = "phase3";

            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(assemblyDir, "rag_config.json");

                if (!File.Exists(configPath))
                {
                    throw new FileNotFoundException($"rag_config.json not found at: {configPath}");
                }

                string json = File.ReadAllText(configPath);

#if NET48
                var configObj = JObject.Parse(json);

                // Load fallback store (separate from primary store)
                if (configObj["fallback_store"] != null)
                    fallbackStore = configObj["fallback_store"].ToString();

                // Override with active values if present
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
                
                // Load API keys if present
                if (configObj["api_keys"] != null)
                {
                    var apiKeys = configObj["api_keys"] as JObject;
                    if (apiKeys["claude_api_key"] != null)
                        claudeApiKey = apiKeys["claude_api_key"].ToString();
                    if (apiKeys["gemini_api_key"] != null)
                        geminiApiKey = apiKeys["gemini_api_key"].ToString();
                }

                if (configObj["validation"] != null)
                {
                    var validationObj = configObj["validation"] as JObject;
                    if (validationObj["gate_enabled"] != null)
                    {
                        bool parsed;
                        if (bool.TryParse(validationObj["gate_enabled"].ToString(), out parsed))
                            validationGateEnabled = parsed;
                    }
                    if (validationObj["auto_fix_enabled"] != null)
                    {
                        bool parsed;
                        if (bool.TryParse(validationObj["auto_fix_enabled"].ToString(), out parsed))
                            autoFixEnabled = parsed;
                    }
                    if (validationObj["auto_fix_max_attempts"] != null)
                    {
                        int parsed;
                        if (int.TryParse(validationObj["auto_fix_max_attempts"].ToString(), out parsed))
                            autoFixMaxAttempts = parsed;
                    }
                    if (validationObj["verify_stage_enabled"] != null)
                    {
                        bool parsed;
                        if (bool.TryParse(validationObj["verify_stage_enabled"].ToString(), out parsed))
                            verifyStageEnabled = parsed;
                    }
                    if (validationObj["verify_timeout_seconds"] != null)
                    {
                        int parsed;
                        if (int.TryParse(validationObj["verify_timeout_seconds"].ToString(), out parsed))
                            verifyTimeoutSeconds = parsed;
                    }
                    if (validationObj["rag_timeout_seconds"] != null)
                    {
                        int parsed;
                        if (int.TryParse(validationObj["rag_timeout_seconds"].ToString(), out parsed))
                            ragTimeoutSeconds = parsed;
                    }
                    if (validationObj["enable_api_xml_hints"] != null)
                    {
                        bool parsed;
                        if (bool.TryParse(validationObj["enable_api_xml_hints"].ToString(), out parsed))
                            enableApiXmlHints = parsed;
                    }
                    if (validationObj["rollout_phase"] != null)
                    {
                        var phase = validationObj["rollout_phase"].ToString();
                        if (!string.IsNullOrWhiteSpace(phase))
                            validationRolloutPhase = phase;
                    }
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    // Load fallback store (separate from primary store)
                    if (doc.RootElement.TryGetProperty("fallback_store", out var fallbackStoreProp))
                        fallbackStore = fallbackStoreProp.GetString();

                    // Override with active values if present
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
                    
                    // Load API keys if present
                    if (doc.RootElement.TryGetProperty("api_keys", out var apiKeysElement))
                    {
                        if (apiKeysElement.TryGetProperty("claude_api_key", out var claudeKeyProp))
                            claudeApiKey = claudeKeyProp.GetString();
                        if (apiKeysElement.TryGetProperty("gemini_api_key", out var geminiKeyProp))
                            geminiApiKey = geminiKeyProp.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("validation", out var validationElement))
                    {
                        if (validationElement.TryGetProperty("gate_enabled", out var gateEnabledProp) &&
                            (gateEnabledProp.ValueKind == JsonValueKind.True || gateEnabledProp.ValueKind == JsonValueKind.False))
                        {
                            validationGateEnabled = gateEnabledProp.GetBoolean();
                        }

                        if (validationElement.TryGetProperty("auto_fix_enabled", out var autoFixEnabledProp) &&
                            (autoFixEnabledProp.ValueKind == JsonValueKind.True || autoFixEnabledProp.ValueKind == JsonValueKind.False))
                        {
                            autoFixEnabled = autoFixEnabledProp.GetBoolean();
                        }

                        if (validationElement.TryGetProperty("auto_fix_max_attempts", out var autoFixAttemptsProp) &&
                            autoFixAttemptsProp.ValueKind == JsonValueKind.Number)
                        {
                            autoFixMaxAttempts = autoFixAttemptsProp.GetInt32();
                        }

                        if (validationElement.TryGetProperty("verify_stage_enabled", out var verifyStageProp) &&
                            (verifyStageProp.ValueKind == JsonValueKind.True || verifyStageProp.ValueKind == JsonValueKind.False))
                        {
                            verifyStageEnabled = verifyStageProp.GetBoolean();
                        }

                        if (validationElement.TryGetProperty("verify_timeout_seconds", out var verifyTimeoutProp) &&
                            verifyTimeoutProp.ValueKind == JsonValueKind.Number)
                        {
                            verifyTimeoutSeconds = verifyTimeoutProp.GetInt32();
                        }

                        if (validationElement.TryGetProperty("rag_timeout_seconds", out var ragTimeoutProp) &&
                            ragTimeoutProp.ValueKind == JsonValueKind.Number)
                        {
                            ragTimeoutSeconds = ragTimeoutProp.GetInt32();
                        }

                        if (validationElement.TryGetProperty("enable_api_xml_hints", out var xmlHintsProp) &&
                            (xmlHintsProp.ValueKind == JsonValueKind.True || xmlHintsProp.ValueKind == JsonValueKind.False))
                        {
                            enableApiXmlHints = xmlHintsProp.GetBoolean();
                        }

                        if (validationElement.TryGetProperty("rollout_phase", out var rolloutPhaseProp))
                        {
                            string phase = rolloutPhaseProp.GetString();
                            if (!string.IsNullOrWhiteSpace(phase))
                                validationRolloutPhase = phase;
                        }
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load rag_config.json: {ex.Message}", ex);
            }

            // OSS BYOK: only claude_api_key is required (validated at call site via UI).
            // store, version, gemini_model are optional — RAG is skipped when absent.
            if (string.IsNullOrEmpty(claudeModel))
                claudeModel = "claude-sonnet-4-6";

            return new RagConfig
            {
                RagStore = store ?? string.Empty,
                FallbackStore = fallbackStore ?? string.Empty,
                RevitVersion = version ?? "Unknown",
                DynamoVersion = dynamoVersion ?? "Unknown",
                ClaudeModel = claudeModel,
                GeminiModel = geminiModel ?? string.Empty,
                ClaudeApiKey = claudeApiKey,
                GeminiApiKey = geminiApiKey,
                ValidationGateEnabled = validationGateEnabled,
                AutoFixEnabled = autoFixEnabled,
                AutoFixMaxAttempts = autoFixMaxAttempts < 0 ? 0 : autoFixMaxAttempts,
                VerifyStageEnabled = verifyStageEnabled,
                VerifyTimeoutSeconds = verifyTimeoutSeconds < 10 ? 10 : verifyTimeoutSeconds,
                RagTimeoutSeconds = ragTimeoutSeconds < 5 ? 5 : ragTimeoutSeconds,
                EnableApiXmlHints = enableApiXmlHints,
                ValidationRolloutPhase = string.IsNullOrWhiteSpace(validationRolloutPhase) ? "phase3" : validationRolloutPhase
            };
        }
    }
}
