using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Viana.Core.Models;
using Viana.Infrastructure.Services;

namespace Viana.TestConsole
{
    /// <summary>
    /// Test console application to demonstrate ResumableDownloader functionality
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Debug);
            });

            var logger = loggerFactory.CreateLogger<ResumableDownloader>();

            // Create HttpClient and ResumableDownloader
            // NOTE: Using HttpClientHandler to bypass SSL validation for testing in restricted environments
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var downloader = new ResumableDownloader(httpClient, logger);

            // Test download URL (10MB test file from a public CDN)
            var testUrl = "https://speed.hetzner.de/10MB.bin";
            var downloadPath = Path.Combine(Path.GetTempPath(), "test_download.bin");

            Console.WriteLine("=== Viana ResumableDownloader Test ===");
            Console.WriteLine($"Download URL: {testUrl}");
            Console.WriteLine($"Destination: {downloadPath}");
            Console.WriteLine();

            // Progress reporter
            var progress = new Progress<DownloadState>(state =>
            {
                var speedMBps = state.BytesPerSecond.HasValue 
                    ? (state.BytesPerSecond.Value / 1024.0 / 1024.0).ToString("F2") 
                    : "N/A";

                Console.WriteLine($"[{state.Status}] {state.FileName}: {state.Progress:F1}% " +
                                  $"({state.DownloadedBytes}/{state.TotalBytes} bytes) " +
                                  $"Speed: {speedMBps} MB/s");

                if (state.Status == DownloadStatus.Failed && !string.IsNullOrEmpty(state.ErrorMessage))
                {
                    Console.WriteLine($"Error: {state.ErrorMessage}");
                }
            });

            try
            {
                // Clean up any existing files
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
                if (File.Exists($"{downloadPath}.partial"))
                    File.Delete($"{downloadPath}.partial");

                Console.WriteLine("Starting download...");
                Console.WriteLine("(You can press Ctrl+C to simulate interruption and test resume functionality)");
                Console.WriteLine();

                var cts = new CancellationTokenSource();
                
                // Handle Ctrl+C to test resume functionality
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Console.WriteLine("\n\nInterrupting download... (partial file will be preserved)");
                    cts.Cancel();
                };

                await downloader.DownloadFileAsync(testUrl, downloadPath, cts.Token, progress);

                Console.WriteLine();
                Console.WriteLine("✓ Download completed successfully!");
                
                if (File.Exists(downloadPath))
                {
                    var fileInfo = new FileInfo(downloadPath);
                    Console.WriteLine($"File size: {fileInfo.Length} bytes ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.WriteLine("Download was cancelled.");
                Console.WriteLine($"Partial file saved at: {downloadPath}.partial");
                Console.WriteLine();
                Console.WriteLine("Run the program again to resume the download!");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"✗ Download failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
