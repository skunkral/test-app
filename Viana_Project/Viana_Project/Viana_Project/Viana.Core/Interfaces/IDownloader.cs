using System;
using System.Threading;
using System.Threading.Tasks;
using Viana.Core.Models;

namespace Viana.Core.Interfaces
{
    /// <summary>
    /// Interface for resumable file download operations
    /// </summary>
    public interface IDownloader
    {
        /// <summary>
        /// Downloads a file from the specified URL to the local path with resumable support
        /// </summary>
        /// <param name="url">Source URL to download from</param>
        /// <param name="localPath">Destination file path</param>
        /// <param name="cancellationToken">Cancellation token to abort the download</param>
        /// <param name="progress">Optional progress reporter for download state updates</param>
        /// <returns>Task representing the async download operation</returns>
        Task DownloadFileAsync(
            string url, 
            string localPath, 
            CancellationToken cancellationToken = default,
            IProgress<DownloadState>? progress = null);

        /// <summary>
        /// Downloads a file with hash validation
        /// </summary>
        /// <param name="url">Source URL to download from</param>
        /// <param name="localPath">Destination file path</param>
        /// <param name="expectedHash">Expected SHA256 hash for validation</param>
        /// <param name="cancellationToken">Cancellation token to abort the download</param>
        /// <param name="progress">Optional progress reporter for download state updates</param>
        /// <returns>Task representing the async download operation</returns>
        /// <exception cref="InvalidOperationException">Thrown when hash validation fails</exception>
        Task DownloadFileAsync(
            string url, 
            string localPath, 
            string expectedHash,
            CancellationToken cancellationToken = default,
            IProgress<DownloadState>? progress = null);
    }
}