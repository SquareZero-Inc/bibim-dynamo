// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BIBIM_MVP
{
    /// <summary>
    /// Windows 시스템 트레이 알림을 표시하는 헬퍼 클래스.
    /// .NET 4.8과 .NET 8 모두에서 동작하며, Windows 10/11 호환.
    /// </summary>
    public static class NotificationHelper
    {
        private static NotifyIcon _notifyIcon;
        private static System.Windows.Forms.Timer _disposeTimer;
        private static Icon _bibimIcon;

        /// <summary>
        /// BIBIM 아이콘을 로드합니다. 실패 시 시스템 아이콘 반환.
        /// </summary>
        private static Icon GetBibimIcon()
        {
            if (_bibimIcon != null) return _bibimIcon;

            try
            {
                // DLL과 같은 폴더에서 아이콘 파일 찾기
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var assemblyDir = Path.GetDirectoryName(assemblyLocation);
                var iconPath = Path.Combine(assemblyDir, "bibim-icon-white.ico");

                if (File.Exists(iconPath))
                {
                    _bibimIcon = new Icon(iconPath);
                    return _bibimIcon;
                }
            }
            catch { }

            // 폴백: 시스템 아이콘
            return SystemIcons.Information;
        }

        /// <summary>
        /// 시스템 트레이에 풍선 알림을 표시합니다.
        /// </summary>
        /// <param name="title">알림 제목</param>
        /// <param name="message">알림 메시지</param>
        /// <param name="durationMs">표시 시간 (밀리초, 기본 3000)</param>
        /// <param name="icon">알림 아이콘 타입</param>
        public static void ShowBalloonTip(
            string title, 
            string message, 
            int durationMs = 3000, 
            ToolTipIcon icon = ToolTipIcon.Info)
        {
            try
            {
                // 이전 알림이 있으면 정리
                CleanupPrevious();

                _notifyIcon = new NotifyIcon
                {
                    Visible = true,
                    Icon = GetBibimIcon(),
                    BalloonTipTitle = title,
                    BalloonTipText = message,
                    BalloonTipIcon = icon
                };

                _notifyIcon.ShowBalloonTip(durationMs);

                // 일정 시간 후 리소스 정리
                _disposeTimer = new System.Windows.Forms.Timer { Interval = durationMs + 1000 };
                _disposeTimer.Tick += (s, e) =>
                {
                    CleanupPrevious();
                };
                _disposeTimer.Start();
            }
            catch
            {
                // 알림 실패 시 무시 (크리티컬하지 않음)
            }
        }

        /// <summary>
        /// BIBIM AI 응답 완료 알림을 표시합니다.
        /// </summary>
        /// <param name="isCodeGenerated">코드가 생성되었는지 여부</param>
        public static void ShowResponseNotification(bool isCodeGenerated = false)
        {
            string title = LocalizationService.Get("Common_BibimAi");
            string message = isCodeGenerated 
                ? LocalizationService.Get("Notification_CodeGenerated")
                : LocalizationService.Get("Notification_ResponseArrived");

            ShowBalloonTip(title, message);
        }

        private static void CleanupPrevious()
        {
            try
            {
                if (_disposeTimer != null)
                {
                    _disposeTimer.Stop();
                    _disposeTimer.Dispose();
                    _disposeTimer = null;
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
            catch { }
        }
    }
}
