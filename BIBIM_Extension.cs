// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Wpf.Extensions;

namespace BIBIM_MVP
{
    public class BIBIM_Extension : IViewExtension
    {
        public string UniqueId => "BIBIM_MVP_GUID_2025";
        public string Name => "BIBIM Chatbot";

        private System.Windows.Controls.MenuItem _menuItem;
        private ViewLoadedParams _viewLoadedParams;

        public void Startup(ViewStartupParams p)
        {
            Log("Startup called");
            AppLanguage.Initialize();
            LocalizationService.Initialize(AppLanguage.Current);

            try
            {
                if (!ServiceContainer.IsInitialized)
                {
                    ServiceContainer.Initialize();
                    Log("DI Container initialized");
                }
            }
            catch (Exception ex)
            {
                Log($"DI Container initialization failed: {ex.Message}");
            }
        }

        public void Loaded(ViewLoadedParams p)
        {
            Log("Loaded called");
            _viewLoadedParams = p;

            try
            {
                _menuItem = new System.Windows.Controls.MenuItem { Header = LocalizationService.Get("Extension_OpenChatMenu") };
                _menuItem.Click += async (sender, args) =>
                {
                    try
                    {
                        Log("Menu item clicked");

                        // Version check (OSS: always passes — no Supabase DB)
                        var versionResult = await VersionChecker.Instance.CheckForUpdatesAsync();
                        if (versionResult.UpdateRequired && versionResult.IsMandatory)
                        {
                            Log($"Mandatory update required: {versionResult.CurrentVersion} -> {versionResult.LatestVersion}");
                            System.Windows.MessageBox.Show(
                                $"Update required: {versionResult.LatestVersion}",
                                "BIBIM Update",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return;
                        }

                        // BYOK: check API key
                        string apiKey = ClaudeApiClient.GetClaudeApiKey();
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            Log("Claude API key not found. Showing ApiKeySetupView.");
                            var setupView = new ApiKeySetupView();
                            if (p.DynamoWindow != null) setupView.Owner = p.DynamoWindow;
                            setupView.ShowDialog();

                            apiKey = ClaudeApiClient.GetClaudeApiKey();
                            if (string.IsNullOrEmpty(apiKey))
                            {
                                Log("API key not provided. Aborting.");
                                return;
                            }
                        }

                        Log("API key confirmed. Opening ChatWorkspace.");
                        ShowChatWorkspace(p);
                    }
                    catch (Exception ex)
                    {
                        Log("Click Error: " + ex.ToString());
                        System.Windows.MessageBox.Show(
                            LocalizationService.Format("Extension_LoadError", ex.Message, ex.StackTrace),
                            "BIBIM Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };
                p.AddExtensionMenuItem(_menuItem);
                Log("Menu item added successfully");
            }
            catch (Exception ex)
            {
                Log("Loaded Error: " + ex.ToString());
                System.Windows.MessageBox.Show(
                    LocalizationService.Format("Extension_InitError", ex.Message),
                    "BIBIM Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowChatWorkspace(ViewLoadedParams p)
        {
            try
            {
                Log("Creating ChatWorkspaceViewModel...");
                var viewModel = new ChatWorkspaceViewModel(p);
                Log("Creating ChatWorkspace...");
                var workspace = new ChatWorkspace(viewModel, p);
                Log("Creating Window...");

                var window = new Window
                {
                    Content = workspace,
                    Width = 455,
                    Height = 780,
                    Title = LocalizationService.Get("Extension_WindowTitle"),
                    Owner = p.DynamoWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Icon = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/BIBIM_MVP;component/Assets/Icons/bibim-icon-white.ico"))
                };

                window.Show();
                Log("Window.Show() completed");
            }
            catch (Exception ex)
            {
                Log($"ShowChatWorkspace Error: {ex}");
                System.Windows.MessageBox.Show(
                    LocalizationService.Format("Extension_WindowOpenError", ex.Message),
                    LocalizationService.Get("Common_ErrorTitle"));
            }
        }

        /// <summary>
        /// Shows an update notification dialog. Returns true if the user chose to download.
        /// For mandatory updates, shows blocking text; for optional, offers dismiss.
        /// </summary>
        private static bool ShowUpdateDialog(VersionCheckResult result, Window owner)
        {
            string title = result.IsMandatory
                ? LocalizationService.Get("Update_WindowTitle")
                : "BIBIM — " + LocalizationService.Get("Update_LatestVersion") + result.LatestVersion;

            string notes = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                ? LocalizationService.Get("Update_DefaultReleaseNotes")
                : result.ReleaseNotes;
            // Strip [MANDATORY] marker from displayed release notes
            notes = Regex.Replace(notes, @"\[MANDATORY\]\s*", "", RegexOptions.IgnoreCase).Trim();

            string body = LocalizationService.Get("Update_CurrentVersion") + result.CurrentVersion + "
"
                        + LocalizationService.Get("Update_LatestVersion") + result.LatestVersion + "

"
                        + (result.IsMandatory ? LocalizationService.Get("Update_Message") + "

" : "")
                        + LocalizationService.Get("Update_ReleaseNotes") + ":
" + notes;

            var buttons = result.IsMandatory ? MessageBoxButton.OK : MessageBoxButton.YesNo;
            var icon = result.IsMandatory ? MessageBoxImage.Warning : MessageBoxImage.Information;

            // Append download prompt for non-mandatory
            if (!result.IsMandatory)
                body += "

" + LocalizationService.Get("Update_ButtonInstall") + "?";

            var mbResult = System.Windows.MessageBox.Show(body, title, buttons, icon);
            return result.IsMandatory || mbResult == MessageBoxResult.Yes;
        }

        public void Shutdown() => Log("Shutdown called");
        public void Dispose() => Log("Dispose called");

        private void Log(string message) => Logger.Log("BIBIM_Extension", message);
    }
}
