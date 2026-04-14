using System.Threading.Tasks;

namespace BIBIM_MVP
{
    /// <summary>
    /// Interface for version checking and update enforcement
    /// </summary>
    public interface IVersionChecker
    {
        /// <summary>
        /// Check for updates and return result with update info
        /// </summary>
        Task<VersionCheckResult> CheckForUpdatesAsync();
    }
}
