using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace BIBIM_MVP
{
    public partial class TopNavigationBar : System.Windows.Controls.UserControl
    {
        public event EventHandler BackRequested;

        public static readonly DependencyProperty ShowBackButtonProperty =
            DependencyProperty.Register("ShowBackButton", typeof(bool), typeof(TopNavigationBar),
                new PropertyMetadata(false));

        public bool ShowBackButton
        {
            get { return (bool)GetValue(ShowBackButtonProperty); }
            set { SetValue(ShowBackButtonProperty, value); }
        }

        public TopNavigationBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show dropdown menu on settings click
        /// </summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsButton.ContextMenu != null)
            {
                SettingsButton.ContextMenu.PlacementTarget = SettingsButton;
                SettingsButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                SettingsButton.ContextMenu.IsOpen = true;
            }
        }

        private void ApiKeySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var setupView = new ApiKeySetupView();
                var window = Window.GetWindow(this);
                if (window != null) setupView.Owner = window;
                setupView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.LogError("TopNavigationBar.ApiKeySettings_Click", ex);
            }
        }

        private void ReportBug_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/SquareZero-Inc/bibim-dynamo/issues/new?labels=bug&template=bug_report.yml");
        }

        private void SuggestFeature_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/SquareZero-Inc/bibim-dynamo/issues/new?labels=enhancement&template=feature_request.yml");
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.LogError("TopNavigationBar.OpenUrl", ex);
            }
        }

        /// <summary>
        /// 뒤로가기 버튼 클릭 - 대시보드로 돌아가기
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
