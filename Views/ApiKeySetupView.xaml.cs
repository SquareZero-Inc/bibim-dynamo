// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
#if NET48
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
using System.Text.Json.Nodes;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// BYOK settings dialog: Claude + Gemini API keys and model selection.
    /// Saves to rag_config.json and invalidates ConfigService cache.
    /// </summary>
    public partial class ApiKeySetupView : Window
    {
        // Sentinel shown in PasswordBox when key already exists — never written to config
        private const string KeyPlaceholder = "sk-ant-••••••••••••••••";
        private const string GeminiPlaceholder = "AIza•••••••••••••••••••";
        private string _existingClaudeKey;
        private string _existingGeminiKey;

        public ApiKeySetupView()
        {
            InitializeComponent();
            PreFillValues();
        }

        private void PreFillValues()
        {
            try
            {
                _existingClaudeKey = ClaudeApiClient.GetClaudeApiKey();
                if (!string.IsNullOrEmpty(_existingClaudeKey))
                {
                    ApiKeyBox.Password = KeyPlaceholder;
                    ClaudeModelCombo.IsEnabled = true;
                }

                var config = ConfigService.GetRagConfig();
                if (config == null) return;

                _existingGeminiKey = config.GeminiApiKey;
                if (!string.IsNullOrEmpty(_existingGeminiKey))
                {
                    GeminiApiKeyBox.Password = GeminiPlaceholder;
                    GeminiModelCombo.IsEnabled = true;
                }

                if (!string.IsNullOrEmpty(config.ClaudeModel))
                    SelectComboByTag(ClaudeModelCombo, config.ClaudeModel);

                if (!string.IsNullOrEmpty(config.GeminiModel))
                    SelectComboByTag(GeminiModelCombo, config.GeminiModel);
            }
            catch (Exception ex)
            {
                Logger.LogError("ApiKeySetupView.PreFillValues", ex);
            }
        }

        private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tagValue)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
            {
                if (item.Tag?.ToString() == tagValue)
                {
                    item.IsSelected = true;
                    return;
                }
            }
        }

        private static string GetSelectedTag(System.Windows.Controls.ComboBox combo, string fallback)
        {
            return (combo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? fallback;
        }

        private void ClaudeKey_Changed(object sender, RoutedEventArgs e)
        {
            if (ClaudeModelCombo != null)
                ClaudeModelCombo.IsEnabled = ApiKeyBox.Password.Length > 0;
            if (ErrorText != null)
                ErrorText.Visibility = Visibility.Collapsed;
        }

        private void GeminiKey_Changed(object sender, RoutedEventArgs e)
        {
            if (GeminiModelCombo != null)
                GeminiModelCombo.IsEnabled = GeminiApiKeyBox.Password.Length > 0;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e) => TrySave();

        private void TrySave()
        {
            string claudeKey = ApiKeyBox.Password?.Trim();
            // Resolve sentinel: user did not change the existing key
            if (claudeKey == KeyPlaceholder) claudeKey = _existingClaudeKey;
            if (string.IsNullOrEmpty(claudeKey))
            {
                ShowError(LocalizationService.Get("ApiKey_ClaudeKeyRequired"));
                return;
            }
            if (!claudeKey.StartsWith("sk-ant-"))
            {
                ShowError(LocalizationService.Get("ApiKey_InvalidFormat"));
                return;
            }

            string geminiKey = GeminiApiKeyBox.Password?.Trim();
            if (geminiKey == GeminiPlaceholder) geminiKey = _existingGeminiKey;
            if (geminiKey == GeminiPlaceholder) geminiKey = _existingGeminiKey;
            string claudeModel = GetSelectedTag(ClaudeModelCombo, "claude-sonnet-4-6");
            string geminiModel = GetSelectedTag(GeminiModelCombo, "gemini-2.5-flash-lite");

            try
            {
                SaveSettings(claudeKey, geminiKey, claudeModel, geminiModel);
                ConfigService.ClearCache();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(string.Format(LocalizationService.Get("ApiKey_SaveFailed"), ex.Message));
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log("ApiKeySetupView.Hyperlink_RequestNavigate", $"Failed to open URL: {ex.Message}");
            }
            e.Handled = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private static void SaveSettings(string claudeApiKey, string geminiApiKey, string claudeModel, string geminiModel)
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configPath = Path.Combine(assemblyDir, "rag_config.json");

            string json;
            if (File.Exists(configPath))
                json = File.ReadAllText(configPath);
            else
            {
                string templatePath = Path.Combine(assemblyDir, "rag_config.template.json");
                json = File.Exists(templatePath) ? File.ReadAllText(templatePath) : "{}";
            }

#if NET48
            var obj = JObject.Parse(json);
            if (obj["api_keys"] == null)
                obj["api_keys"] = new JObject();
            obj["api_keys"]["claude_api_key"] = claudeApiKey;
            obj["api_keys"]["gemini_api_key"] = geminiApiKey ?? "";
            obj["claude_model"] = claudeModel;
            obj["gemini_model"] = geminiModel;
            File.WriteAllText(configPath, obj.ToString(Newtonsoft.Json.Formatting.Indented));
#else
            var node = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            if (node["api_keys"] is not JsonObject apiKeys)
            {
                apiKeys = new JsonObject();
                node["api_keys"] = apiKeys;
            }
            apiKeys["claude_api_key"] = claudeApiKey;
            apiKeys["gemini_api_key"] = geminiApiKey ?? "";
            node["claude_model"] = claudeModel;
            node["gemini_model"] = geminiModel;
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, node.ToJsonString(options));
#endif
        }
    }
}
