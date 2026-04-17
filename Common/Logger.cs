// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.IO;

namespace BIBIM_MVP
{
    /// <summary>
    /// Centralized logging utility for BIBIM
    /// All debug logs are written to %USERPROFILE%/bibim_debug.txt
    /// </summary>
    public static class Logger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BIBIM", "logs"
        );

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BIBIM", "logs", "bibim_debug.txt"
        );

        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB — rotate beyond this

        private static readonly object _lock = new object();

        // Always enabled for debugging port addition feature
        private static bool _enabled = true;

        /// <summary>
        /// Enable or disable logging at runtime
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Log a message with source class name
        /// </summary>
        /// <param name="source">Source class name (e.g., "SupabaseService")</param>
        /// <param name="message">Log message</param>
        public static void Log(string source, string message)
        {
            if (!_enabled) return;

            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(LogDir))
                        Directory.CreateDirectory(LogDir);

                    // Rotate when file exceeds size limit (keep one .old backup)
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSizeBytes)
                    {
                        string archivePath = LogPath + ".old";
                        if (File.Exists(archivePath))
                            File.Delete(archivePath);
                        File.Move(LogPath, archivePath);
                    }

                    File.AppendAllText(LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}]: {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Silent fail - logging should never crash the app
            }
        }

        /// <summary>
        /// Log an exception with source class name
        /// </summary>
        public static void LogError(string source, Exception ex)
        {
            Log(source, $"ERROR: {ex.Message}\n{ex.StackTrace}");
        }

        /// <summary>
        /// Clear the log file
        /// </summary>
        public static void Clear()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath))
                        File.Delete(LogPath);
                }
            }
            catch { }
        }
    }
}
