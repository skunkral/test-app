using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Viana.Core.Interfaces;
using Viana.Core.Models;

namespace Viana.Infrastructure.Services
{
    /// <summary>
    /// Implements resumable file downloads with HTTP Range header support, retry logic, and hash validation
    /// </summary>
    public class ResumableDownloader : IDownloader
    {
        private readonly HttpClient _client;
        private readonly ILogger<ResumableDownloader> _logger;
        private const int BufferSize = 81920; // 80KB chunks
        private const int MaxRetries = 5;
        private const int ProgressReportThreshold = 1048576; // Report every 1MB

        public ResumableDownloader(HttpClient client, ILogger<ResumableDownloader> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Downloads a file with resumable support (no hash validation)
        /// </summary>
        public async Task DownloadFileAsync(
            string url, 
            string localPath, 
            CancellationToken cancellationToken = default,
            IProgress<DownloadState>? progress = null)
        {
            await DownloadFileInternalAsync(url, localPath, null, cancellationToken, progress);
        }

        /// <summary>
        /// Downloads a file with hash validation
        /// </summary>
        public async Task DownloadFileAsync(
            string url, 
            string localPath, 
            string expectedHash,
            CancellationToken cancellationToken = default,
            IProgress<DownloadState>? progress = null)
        {
            await DownloadFileInternalAsync(url, localPath, expectedHash, cancellationToken, progress);
        }

        /// <summary>
        /// Internal implementation of resumable download with retry logic
        /// </summary>
        private async Task DownloadFileInternalAsync(
            string url,
            string localPath,
            string? expectedHash,
            CancellationToken cancellationToken,
            IProgress<DownloadState>? progress)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            
            if (string.IsNullOrWhiteSpace(localPath))
                throw new ArgumentException("Local path cannot be null or empty", nameof(localPath));

            var fileName = Path.GetFileName(localPath);
            var partialPath = $"{localPath}.partial";
            
            _logger.LogInformation("Starting download: {Url} -> {LocalPath}", url, localPath);

            var retryCount = 0;
            var retryDelay = TimeSpan.FromSeconds(1);

            while (retryCount <= MaxRetries)
            {
                try
                {
                    await DownloadWithResumeAsync(url, localPath, partialPath, fileName, cancellationToken, progress);
                    
                    // Validate hash if provided
                    if (!string.IsNullOrWhiteSpace(expectedHash))
                    {
                        await ValidateHashAsync(localPath, expectedHash, cancellationToken);
                    }

                    _logger.LogInformation("Download completed successfully: {LocalPath}", localPath);
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Download cancelled by user: {LocalPath}", localPath);
                    throw;
                }
                catch (Exception ex) when (retryCount < MaxRetries)
                {
                    retryCount++;
                    _logger.LogWarning(ex, 
                        "Download failed (attempt {RetryCount}/{MaxRetries}). Retrying in {RetryDelay}s...", 
                        retryCount, MaxRetries, retryDelay.TotalSeconds);

                    ReportProgress(progress, fileName, 0, 0, DownloadStatus.Failed, ex.Message);

                    await Task.Delay(retryDelay, cancellationToken);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 16)); // Exponential backoff, max 16s
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Download failed after {MaxRetries} retries: {LocalPath}", MaxRetries, localPath);
                    ReportProgress(progress, fileName, 0, 0, DownloadStatus.Failed, ex.Message);
                    throw;
                }
            }
        }

        /// <summary>
        /// Performs the actual download with resume support
        /// </summary>
        private async Task DownloadWithResumeAsync(
            string url,
            string localPath,
            string partialPath,
            string fileName,
            CancellationToken cancellationToken,
            IProgress<DownloadState>? progress)
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogDebug("Created directory: {Directory}", directory);
            }

            // Check for existing partial download
            long resumePosition = 0;
            if (File.Exists(partialPath))
            {
                var fileInfo = new FileInfo(partialPath);
                resumePosition = fileInfo.Length;
                _logger.LogInformation("Resuming download from byte {ResumePosition} for {FileName}", resumePosition, fileName);
            }
            else
            {
                _logger.LogInformation("Starting fresh download for {FileName}", fileName);
            }

            // Create HTTP request with Range header
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumePosition > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumePosition, null);
            }

            // Send request
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            // Validate response
            if (resumePosition > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                _logger.LogWarning("Server does not support range requests. Starting fresh download.");
                resumePosition = 0;
                
                // Delete partial file and restart
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
                
                // Retry without range header
                await DownloadWithResumeAsync(url, localPath, partialPath, fileName, cancellationToken, progress);
                return;
            }

            response.EnsureSuccessStatusCode();

            // Get total file size
            var totalBytes = (response.Content.Headers.ContentLength ?? 0) + resumePosition;
            _logger.LogInformation("Total file size: {TotalBytes} bytes ({TotalMB:F2} MB)", totalBytes, totalBytes / 1024.0 / 1024.0);

            // Download to partial file
            var stopwatch = Stopwatch.StartNew();
            var downloadedBytes = resumePosition;
            var lastReportedBytes = resumePosition;

            var fileMode = resumePosition > 0 ? FileMode.Append : FileMode.Create;
            
            using (var fileStream = new FileStream(partialPath, fileMode, FileAccess.Write, FileShare.None, BufferSize))
            using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;

                ReportProgress(progress, fileName, totalBytes, downloadedBytes, DownloadStatus.Downloading);

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    downloadedBytes += bytesRead;

                    // Report progress periodically
                    if (downloadedBytes - lastReportedBytes >= ProgressReportThreshold || downloadedBytes == totalBytes)
                    {
                        var bytesPerSecond = stopwatch.ElapsedMilliseconds > 0 
                            ? (long)((downloadedBytes - resumePosition) / (stopwatch.ElapsedMilliseconds / 1000.0))
                            : 0;

                        ReportProgress(progress, fileName, totalBytes, downloadedBytes, DownloadStatus.Downloading, null, bytesPerSecond);
                        
                        var progressPercent = totalBytes > 0 ? (downloadedBytes * 100.0 / totalBytes) : 0;
                        _logger.LogDebug("Download progress: {Progress:F1}% ({Downloaded}/{Total} bytes, {Speed:F2} MB/s)", 
                            progressPercent, downloadedBytes, totalBytes, bytesPerSecond / 1024.0 / 1024.0);

                        lastReportedBytes = downloadedBytes;
                    }
                }
            }

            stopwatch.Stop();

            // Rename partial file to final filename
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
            File.Move(partialPath, localPath);

            var avgSpeed = stopwatch.ElapsedMilliseconds > 0 
                ? (downloadedBytes - resumePosition) / (stopwatch.ElapsedMilliseconds / 1000.0)
                : 0;

            _logger.LogInformation("Download completed in {ElapsedSeconds:F2}s. Average speed: {AvgSpeed:F2} MB/s", 
                stopwatch.Elapsed.TotalSeconds, avgSpeed / 1024.0 / 1024.0);

            ReportProgress(progress, fileName, totalBytes, downloadedBytes, DownloadStatus.Completed);
        }

        /// <summary>
        /// Validates the downloaded file hash
        /// </summary>
        private async Task ValidateHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Validating file hash for {FilePath}", filePath);

            using var sha256 = SHA256.Create();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            
            var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
            var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            var expectedHashNormalized = expectedHash.Replace("-", "").ToLowerInvariant();

            if (actualHash != expectedHashNormalized)
            {
                _logger.LogError("Hash validation failed. Expected: {ExpectedHash}, Actual: {ActualHash}", 
                    expectedHashNormalized, actualHash);
                
                // Delete invalid file
                File.Delete(filePath);
                _logger.LogWarning("Deleted invalid file: {FilePath}", filePath);

                throw new InvalidOperationException(
                    $"Hash validation failed. Expected: {expectedHashNormalized}, Actual: {actualHash}");
            }

            _logger.LogInformation("Hash validation successful: {Hash}", actualHash);
        }

        /// <summary>
        /// Reports download progress
        /// </summary>
        private void ReportProgress(
            IProgress<DownloadState>? progress, 
            string fileName, 
            long totalBytes, 
            long downloadedBytes, 
            DownloadStatus status,
            string? errorMessage = null,
            long? bytesPerSecond = null)
        {
            progress?.Report(new DownloadState
            {
                FileName = fileName,
                TotalBytes = totalBytes,
                DownloadedBytes = downloadedBytes,
                Status = status,
                ErrorMessage = errorMessage,
                BytesPerSecond = bytesPerSecond
            });
        }
    }
}