// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BIBIM_MVP
{
    /// <summary>
    /// Result of version check operation
    /// </summary>
    public class VersionCheckResult
    {
        public bool UpdateRequired { get; set; }
        public bool IsMandatory { get; set; }
        public string LatestVersion { get; set; }
        public string CurrentVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public string ErrorMessage { get; set; }

        public static VersionCheckResult NoUpdateNeeded => new VersionCheckResult
        {
            UpdateRequired = false,
            IsMandatory = false
        };

        public static VersionCheckResult Error(string message) => new VersionCheckResult
        {
            UpdateRequired = false,
            IsMandatory = false,
            ErrorMessage = message
        };
    }

    /// <summary>
    /// Checks GitHub Releases (SquareZero-Inc/bibim-dynamo) for a newer version.
    /// Mandatory update: release body contains "[MANDATORY]".
    /// Asset selection: picks the KO or EN installer based on AppLanguage.IsEnglish.
    /// </summary>
    public class VersionChecker : IVersionChecker
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/SquareZero-Inc/bibim-dynamo/releases/latest";

        private static readonly Lazy<VersionChecker> _lazyInstance =
            new Lazy<VersionChecker>(() => new VersionChecker());
        public static VersionChecker Instance => _lazyInstance.Value;

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private VersionChecker() { }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("BIBIM-AI-Dynamo", CurrentVersion));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        /// <summary>
        /// Current app version — automatically read from assembly.
        /// </summary>
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var attrs = assembly.GetCustomAttributes(
                        typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                    if (attrs.Length > 0)
                    {
                        var version = ((System.Reflection.AssemblyInformationalVersionAttribute)attrs[0])
                            .InformationalVersion;
                        var plusIndex = version.IndexOf('+');
                        return plusIndex > 0 ? version.Substring(0, plusIndex) : version;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("VersionChecker.CurrentVersion", $"Failed to read assembly version: {ex.Message}");
                }
                return "0.0.0";
            }
        }

        public async Task<VersionCheckResult> CheckForUpdatesAsync()
        {
            string currentRaw = CurrentVersion; // e.g. "1.0.0-en" or "1.0.0-kr"
            int dashIdx = currentRaw.IndexOf('-');
            string currentVersion = dashIdx >= 0 ? currentRaw.Substring(0, dashIdx) : currentRaw;

            try
            {
                string json = await _httpClient.GetStringAsync(ReleasesApiUrl).ConfigureAwait(false);
                var release = JObject.Parse(json);

                string tagName = release["tag_name"]?.ToString() ?? "";
                string latestVersion = tagName.TrimStart('v', 'V');
                string body = release["body"]?.ToString() ?? "";
                bool isMandatory = body.IndexOf("[MANDATORY]", StringComparison.OrdinalIgnoreCase) >= 0;

                if (CompareVersions(latestVersion, currentVersion) <= 0)
                {
                    Logger.Log("VersionChecker", "Up to date (" + currentVersion + ")");
                    return VersionCheckResult.NoUpdateNeeded;
                }

                // Find matching asset: _EN_ for English build, _KO_ for Korean build
                string langToken = AppLanguage.IsEnglish ? "_EN_" : "_KO_";
                string downloadUrl = null;
                var assets = release["assets"] as JArray;
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        string name = asset["name"]?.ToString() ?? "";
                        if (name.IndexOf(langToken, StringComparison.OrdinalIgnoreCase) >= 0
                            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }

                // Fallback: use release HTML page if no matching asset found
                if (string.IsNullOrEmpty(downloadUrl))
                    downloadUrl = release["html_url"]?.ToString() ?? "";

                Logger.Log("VersionChecker",
                    "Update available: " + currentVersion + " -> " + latestVersion
                    + ", mandatory=" + isMandatory);

                return new VersionCheckResult
                {
                    UpdateRequired = true,
                    IsMandatory = isMandatory,
                    LatestVersion = latestVersion,
                    CurrentVersion = currentVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = body
                };
            }
            catch (Exception ex)
            {
                Logger.Log("VersionChecker", "Version check failed: " + ex.Message);
                return VersionCheckResult.Error(ex.Message);
            }
        }

        public static int CompareVersions(string v1, string v2)
        {
            if (string.IsNullOrEmpty(v1) && string.IsNullOrEmpty(v2)) return 0;
            if (string.IsNullOrEmpty(v1)) return -1;
            if (string.IsNullOrEmpty(v2)) return 1;

            v1 = v1.TrimStart('v', 'V');
            v2 = v2.TrimStart('v', 'V');

            var parts1 = v1.Split('.');
            var parts2 = v2.Split('.');
            int max = Math.Max(parts1.Length, parts2.Length);

            for (int i = 0; i < max; i++)
            {
                int p1 = i < parts1.Length && int.TryParse(parts1[i], out int a) ? a : 0;
                int p2 = i < parts2.Length && int.TryParse(parts2[i], out int b) ? b : 0;
                if (p1 < p2) return -1;
                if (p1 > p2) return 1;
            }
            return 0;
        }
    }
}
