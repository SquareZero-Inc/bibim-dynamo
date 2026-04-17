// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BIBIM_MVP
{
    /// <summary>
    /// Represents a single conversation history entry
    /// </summary>
    public class HistoryEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string RevitVersion { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string AiResponse { get; set; } = string.Empty;
        public string PythonCode { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title))
                    return Title;
                string summary = UserPrompt.Length > 40
                    ? UserPrompt.Substring(0, 40) + "..."
                    : UserPrompt;
                return summary.Replace("\n", " ").Replace("\r", "");
            }
        }

        public string VersionTag => $"[Revit {RevitVersion}]";
    }

    /// <summary>
    /// OSS: History is managed locally via LocalSessionManager / ConversationContextManager.
    /// </summary>
    public static class HistoryManager
    {
        public static Task<List<HistoryEntry>> LoadAllHistoryAsync()
            => Task.FromResult(new List<HistoryEntry>());

        public static List<HistoryEntry> LoadAllHistory()
            => new List<HistoryEntry>();
    }
}
