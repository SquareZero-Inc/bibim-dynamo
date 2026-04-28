// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace BIBIM_MVP
{
    /// <summary>
    /// BYOK settings dialog: per-provider API keys (Anthropic / OpenAI / Gemini)
    /// + a single active-model radio group. Models without a key for their provider
    /// are disabled with a tooltip prompting the user to add the matching key.
    /// </summary>
    public partial class ApiKeySetupView : Window
    {
        // Sentinels shown in PasswordBoxes when a key is already saved. Never written to config.
        private const string AnthropicSentinel = "sk-ant-••••••••••••••••";
        private const string OpenAISentinel    = "sk-••••••••••••••••";
        private const string GeminiSentinel    = "AIza•••••••••••••••••";

        private string _existingAnthropicKey;
        private string _existingOpenAIKey;
        private string _existingGeminiKey;

        public ApiKeySetupView()
        {
            InitializeComponent();
            ApplyLocalization();
            PreFillValues();
            UpdateModelGating();
        }

        /// <summary>
        /// Applies localised strings to every visible label, button, and description in
        /// the dialog. The XAML carries x:Name placeholders only — no hard-coded English.
        /// </summary>
        private void ApplyLocalization()
        {
            try
            {
                Title                       = LocalizationService.Get("ApiKeySetup_WindowTitle");
                MainTitleText.Text          = LocalizationService.Get("ApiKeySetup_MainTitle");
                GuideButton.Content         = LocalizationService.Get("ApiKey_GuideLinkText");

                AnthropicSectionTitle.Text  = LocalizationService.Get("ApiKeySetup_SectionAnthropic");
                OpenAISectionTitle.Text     = LocalizationService.Get("ApiKeySetup_SectionOpenAI");
                GeminiSectionTitle.Text     = LocalizationService.Get("ApiKeySetup_SectionGemini");

                AnthropicDescPrefix.Text    = LocalizationService.Get("ApiKeySetup_AnthropicDesc");
                OpenAIDescPrefix.Text       = LocalizationService.Get("ApiKeySetup_OpenAIDesc");
                GeminiDescPrefix.Text       = LocalizationService.Get("ApiKeySetup_GeminiDesc");

                ActiveModelTitle.Text       = LocalizationService.Get("ApiKeySetup_ActiveModelTitle");
                ActiveModelDesc.Text        = LocalizationService.Get("ApiKeySetup_ActiveModelDesc");

                ModelNoteSonnet46.Text      = LocalizationService.Get("ApiKeySetup_ModelNote_Sonnet46");
                ModelNoteOpus47.Text        = LocalizationService.Get("ApiKeySetup_ModelNote_Opus47");
                ModelNoteGpt55.Text         = LocalizationService.Get("ApiKeySetup_ModelNote_Gpt55");
                ModelNoteGemini31.Text      = LocalizationService.Get("ApiKeySetup_ModelNote_Gemini31");

                // Localized speed tooltips on each model radio (icons in XAML are universal).
                ModelSonnet46.ToolTip       = LocalizationService.Get("ApiKeySetup_ModelSpeed_Fast");
                ModelOpus47.ToolTip         = LocalizationService.Get("ApiKeySetup_ModelSpeed_Medium");
                ModelGpt55.ToolTip          = LocalizationService.Get("ApiKeySetup_ModelSpeed_Medium");
                ModelGemini31.ToolTip       = LocalizationService.Get("ApiKeySetup_ModelSpeed_Slow");

                CancelButton.Content        = LocalizationService.Get("ApiKeySetup_ButtonCancel");
                SaveButton.Content          = LocalizationService.Get("ApiKeySetup_ButtonSave");
            }
            catch (Exception ex)
            {
                // If localization fails, fall back to whatever text is already present.
                Logger.LogError("ApiKeySetupView.ApplyLocalization", ex);
            }
        }

        private void GuideButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = LocalizationService.Get("ApiKey_GuideUrl");
                if (string.IsNullOrWhiteSpace(url)) return;
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log("ApiKeySetupView.GuideButton_Click", $"Failed to open guide: {ex.Message}");
            }
        }

        private void PreFillValues()
        {
            try
            {
                var config = ConfigService.GetRagConfig();

                _existingAnthropicKey = ConfigService.GetApiKeyForProvider(config, LlmApiClientFactory.ProviderAnthropic);
                _existingOpenAIKey    = ConfigService.GetApiKeyForProvider(config, LlmApiClientFactory.ProviderOpenAI);
                _existingGeminiKey    = ConfigService.GetApiKeyForProvider(config, LlmApiClientFactory.ProviderGemini);

                if (!string.IsNullOrEmpty(_existingAnthropicKey)) AnthropicKeyBox.Password = AnthropicSentinel;
                if (!string.IsNullOrEmpty(_existingOpenAIKey))    OpenAIKeyBox.Password    = OpenAISentinel;
                if (!string.IsNullOrEmpty(_existingGeminiKey))    GeminiKeyBox.Password    = GeminiSentinel;

                UpdateStatusBadges();

                // Select the saved active model, falling back to default.
                string activeModel = !string.IsNullOrEmpty(config?.ClaudeModel)
                    ? config.ClaudeModel
                    : ConfigService.DefaultModelId;
                SelectModelByTag(activeModel);
            }
            catch (Exception ex)
            {
                Logger.LogError("ApiKeySetupView.PreFillValues", ex);
            }
        }

        private void UpdateStatusBadges()
        {
            string saved = LocalizationService.Get("ApiKeySetup_BadgeSaved");
            AnthropicStatusBadge.Text = string.IsNullOrEmpty(_existingAnthropicKey) ? string.Empty : saved;
            OpenAIStatusBadge.Text    = string.IsNullOrEmpty(_existingOpenAIKey)    ? string.Empty : saved;
            GeminiStatusBadge.Text    = string.IsNullOrEmpty(_existingGeminiKey)    ? string.Empty : saved;
        }

        private void SelectModelByTag(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            foreach (var rb in AllModelRadios())
            {
                if (string.Equals(rb.Tag?.ToString(), modelId, StringComparison.Ordinal))
                {
                    rb.IsChecked = true;
                    return;
                }
            }
        }

        private string SelectedModelTag()
        {
            var rb = AllModelRadios().FirstOrDefault(r => r.IsChecked == true);
            return rb?.Tag?.ToString() ?? ConfigService.DefaultModelId;
        }

        private IEnumerable<System.Windows.Controls.RadioButton> AllModelRadios()
        {
            yield return ModelSonnet46;
            yield return ModelOpus47;
            yield return ModelGpt55;
            yield return ModelGemini31;
        }

        // ── Key change handlers — re-evaluate model gating each time ─────────

        private void AnthropicKey_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModelGating();
            HideError();
        }

        private void OpenAIKey_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModelGating();
            HideError();
        }

        private void GeminiKey_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModelGating();
            HideError();
        }

        /// <summary>
        /// A provider counts as "has key" if the user typed something OR the existing key
        /// is still represented by its sentinel. Updates each radio's IsEnabled and tooltip.
        /// </summary>
        private void UpdateModelGating()
        {
            bool hasAnthropic = HasKey(AnthropicKeyBox.Password, AnthropicSentinel, _existingAnthropicKey);
            bool hasOpenAI    = HasKey(OpenAIKeyBox.Password,    OpenAISentinel,    _existingOpenAIKey);
            bool hasGemini    = HasKey(GeminiKeyBox.Password,    GeminiSentinel,    _existingGeminiKey);

            ApplyGating(ModelSonnet46, hasAnthropic, "Anthropic");
            ApplyGating(ModelOpus47,   hasAnthropic, "Anthropic");
            ApplyGating(ModelGpt55,    hasOpenAI,    "OpenAI");
            ApplyGating(ModelGemini31, hasGemini,    "Gemini");

            // If the currently checked model just got disabled, fall back to a usable one.
            var checkedRb = AllModelRadios().FirstOrDefault(r => r.IsChecked == true);
            if (checkedRb != null && checkedRb.IsEnabled == false)
            {
                var firstEnabled = AllModelRadios().FirstOrDefault(r => r.IsEnabled);
                if (firstEnabled != null) firstEnabled.IsChecked = true;
            }
        }

        private static bool HasKey(string boxText, string sentinel, string existingKey)
        {
            if (string.IsNullOrWhiteSpace(boxText)) return false;
            if (boxText == sentinel) return !string.IsNullOrEmpty(existingKey);
            return true;
        }

        private static void ApplyGating(System.Windows.Controls.RadioButton rb, bool enabled, string providerName)
        {
            rb.IsEnabled = enabled;
            rb.ToolTip = enabled ? null : LocalizationService.Format("ApiKeySetup_LockedTooltip", providerName);
        }

        // ── Save / Cancel ────────────────────────────────────────────────────

        private void SaveButton_Click(object sender, RoutedEventArgs e) => TrySave();

        private void TrySave()
        {
            string anthropicKey = ResolveKey(AnthropicKeyBox.Password, AnthropicSentinel, _existingAnthropicKey);
            string openAIKey    = ResolveKey(OpenAIKeyBox.Password,    OpenAISentinel,    _existingOpenAIKey);
            string geminiKey    = ResolveKey(GeminiKeyBox.Password,    GeminiSentinel,    _existingGeminiKey);

            // Format checks (prefix only — actual validity is detected on the first call).
            if (!string.IsNullOrEmpty(anthropicKey) && !anthropicKey.StartsWith("sk-ant-"))
            {
                ShowError(LocalizationService.Get("ApiKey_InvalidFormat"));
                return;
            }
            if (!string.IsNullOrEmpty(openAIKey) && !openAIKey.StartsWith("sk-"))
            {
                ShowError(LocalizationService.Get("ApiKey_OpenAIInvalidFormat"));
                return;
            }
            if (!string.IsNullOrEmpty(geminiKey) && !geminiKey.StartsWith("AIza"))
            {
                ShowError(LocalizationService.Get("ApiKey_GeminiInvalidFormat"));
                return;
            }

            string activeModel = SelectedModelTag();
            string requiredProvider = LlmApiClientFactory.ResolveProviderForModel(activeModel);
            string requiredKey =
                requiredProvider == LlmApiClientFactory.ProviderOpenAI ? openAIKey :
                requiredProvider == LlmApiClientFactory.ProviderGemini ? geminiKey :
                                                                          anthropicKey;
            if (string.IsNullOrEmpty(requiredKey))
            {
                ShowError(LocalizationService.Format("ApiKey_KeyMissingForActiveModel", requiredProvider));
                return;
            }

            try
            {
                // Save each provider's key (empty string clears it).
                ConfigService.SaveApiKeyForProvider(LlmApiClientFactory.ProviderAnthropic, anthropicKey ?? string.Empty);
                ConfigService.SaveApiKeyForProvider(LlmApiClientFactory.ProviderOpenAI,    openAIKey    ?? string.Empty);
                ConfigService.SaveApiKeyForProvider(LlmApiClientFactory.ProviderGemini,    geminiKey    ?? string.Empty);
                ConfigService.SetActiveModel(activeModel);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(string.Format(LocalizationService.Get("ApiKey_SaveFailed"), ex.Message));
            }
        }

        private static string ResolveKey(string boxText, string sentinel, string existingKey)
        {
            if (string.IsNullOrWhiteSpace(boxText)) return null;
            if (boxText == sentinel) return existingKey;
            return boxText.Trim();
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

        private void HideError()
        {
            if (ErrorText != null) ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
