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

        /// <summary>
        /// Cached result of the startup version check. Null if no update needed or not yet checked.
        /// </summary>
        internal static VersionCheckResult LastVersionCheckResult { get; private set; }

        /// <summary>
        /// Fired on a background thread when the startup version check finds a newer version.
        /// Subscribers must be thread-safe (dispatch to UI thread as needed).
        /// </summary>
        internal static event Action<VersionCheckResult> VersionCheckCompleted;

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

            // Fire-and-forget version check at startup
            Task.Run(async () =>
            {
                try
                {
                    var result = await VersionChecker.Instance.CheckForUpdatesAsync();
                    if (result.UpdateRequired)
                    {
                        LastVersionCheckResult = result;
                        Log($"Update available: {result.LatestVersion} (mandatory={result.IsMandatory})");
                        VersionCheckCompleted?.Invoke(result);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Version check failed (non-fatal): {ex.Message}");
                }
            });

            try
            {
                _menuItem = new System.Windows.Controls.MenuItem { Header = LocalizationService.Get("Extension_OpenChatMenu") };
                _menuItem.Click += (sender, args) =>
                {
                    try
                    {
                        Log("Menu item clicked");

                        // Use cached result — startup check already ran in background
                        if (LastVersionCheckResult?.UpdateRequired == true && LastVersionCheckResult.IsMandatory)
                        {
                            Log($"Mandatory update required: {LastVersionCheckResult.CurrentVersion} -> {LastVersionCheckResult.LatestVersion}");
                            System.Windows.MessageBox.Show(
                                $"Update required: {LastVersionCheckResult.LatestVersion}",
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

        public void Shutdown() => Log("Shutdown called");
        public void Dispose() => Log("Dispose called");

        private void Log(string message) => Logger.Log("BIBIM_Extension", message);
    }
}
