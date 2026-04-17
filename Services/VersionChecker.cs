using System;
using System.Threading.Tasks;

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
        private static readonly Lazy<VersionChecker> _lazyInstance =
            new Lazy<VersionChecker>(() => new VersionChecker());
        public static VersionChecker Instance => _lazyInstance.Value;

        private VersionChecker() { }

        /// <summary>
        /// Current app version - automatically read from assembly
        /// </summary>
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var attrs = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                    if (attrs.Length > 0)
                    {
                        var version = ((System.Reflection.AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
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

        /// <summary>

        /// </summary>
        public Task<VersionCheckResult> CheckForUpdatesAsync()
        {
            Logger.Log("VersionChecker", "OSS build — version check skipped");
            return Task.FromResult(VersionCheckResult.NoUpdateNeeded);
        }

    }
}
