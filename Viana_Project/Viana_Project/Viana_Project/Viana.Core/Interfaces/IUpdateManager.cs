using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Viana.Core.Models;

namespace Viana.Core.Interfaces
{
    /// <summary>
    /// Interface for managing application updates via manifest comparison
    /// </summary>
    public interface IUpdateManager
    {
        /// <summary>
        /// Fetches the remote manifest from the specified URL
        /// </summary>
        /// <param name="manifestUrl">URL to the manifest.json file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The parsed AppManifest</returns>
        Task<AppManifest> FetchManifestAsync(string manifestUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// Compares local files against the manifest and returns modules that need updating
        /// </summary>
        /// <param name="manifest">The remote manifest to compare against</param>
        /// <param name="localPath">Path to the local Active directory</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of modules that are missing or have mismatched hashes</returns>
        Task<List<AppModule>> GetModulesToUpdateAsync(AppManifest manifest, string localPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks for updates by fetching manifest and comparing with local files
        /// </summary>
        /// <param name="manifestUrl">URL to the manifest.json file</param>
        /// <param name="localPath">Path to the local Active directory</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of modules that need updating, or empty list if up to date</returns>
        Task<List<AppModule>> CheckForUpdatesAsync(string manifestUrl, string localPath, CancellationToken cancellationToken = default);
    }
}