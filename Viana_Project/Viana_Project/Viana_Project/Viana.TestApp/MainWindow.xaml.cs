using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Viana.Core.Models;
using Viana.Infrastructure.Services;

namespace Viana.TestApp
{
    public partial class MainWindow : Window
    {
        private readonly ILogger<ResumableDownloader> _logger;
        private readonly ResumableDownloader _downloader;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize default save path
            txtSavePath.Text = Path.Combine(Path.GetTempPath(), "viana_test_download.bin");

            // Setup logging with custom logger that writes to UI
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new UILoggerProvider(this));
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            _logger = loggerFactory.CreateLogger<ResumableDownloader>();

            // Create HttpClient with SSL bypass for testing
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(30);

            _downloader = new ResumableDownloader(_httpClient, _logger);

            LogMessage("Application initialized. Ready to start download.");
        }

        private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Select Download Location",
                Filter = "All Files (*.*)|*.*",
                FileName = "download.bin"
            };

            if (dialog.ShowDialog() == true)
            {
                txtSavePath.Text = dialog.FileName;
                LogMessage($"Save location changed to: {dialog.FileName}");
            }
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDownloadUrl.Text))
            {
                MessageBox.Show("Please enter a download URL.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtSavePath.Text))
            {
                MessageBox.Show("Please select a save location.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Update UI state
                btnStartDownload.IsEnabled = false;
                btnCancelDownload.IsEnabled = true;
                txtDownloadUrl.IsEnabled = false;
                txtSavePath.IsEnabled = false;
                chkValidateHash.IsEnabled = false;
                txtExpectedHash.IsEnabled = false;

                // Reset progress
                progressBar.Value = 0;
                txtProgressPercent.Text = "0%";
                txtStatus.Text = "Starting...";
                txtDownloaded.Text = "0 MB";
                txtTotalSize.Text = "0 MB";
                txtSpeed.Text = "0 MB/s";

                LogMessage("=== Download Started ===");
                LogMessage($"URL: {txtDownloadUrl.Text}");
                LogMessage($"Destination: {txtSavePath.Text}");

                // Create cancellation token
                _cancellationTokenSource = new CancellationTokenSource();

                // Create progress reporter
                var progress = new Progress<DownloadState>(state =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = state.Progress;
                        txtProgressPercent.Text = $"{state.Progress:F1}%";
                        txtStatus.Text = state.Status.ToString();
                        txtDownloaded.Text = $"{state.DownloadedBytes / 1024.0 / 1024.0:F2} MB";
                        txtTotalSize.Text = $"{state.TotalBytes / 1024.0 / 1024.0:F2} MB";
                        
                        if (state.BytesPerSecond.HasValue)
                        {
                            txtSpeed.Text = $"{state.BytesPerSecond.Value / 1024.0 / 1024.0:F2} MB/s";
                        }

                        // Update status color based on state
                        txtStatus.Foreground = state.Status switch
                        {
                            DownloadStatus.Downloading => System.Windows.Media.Brushes.Blue,
                            DownloadStatus.Completed => System.Windows.Media.Brushes.Green,
                            DownloadStatus.Failed => System.Windows.Media.Brushes.Red,
                            DownloadStatus.Cancelled => System.Windows.Media.Brushes.Orange,
                            _ => System.Windows.Media.Brushes.Gray
                        };
                    });
                });

                // Start download
                if (chkValidateHash.IsChecked == true && !string.IsNullOrWhiteSpace(txtExpectedHash.Text))
                {
                    await _downloader.DownloadFileAsync(
                        txtDownloadUrl.Text,
                        txtSavePath.Text,
                        txtExpectedHash.Text,
                        _cancellationTokenSource.Token,
                        progress);
                }
                else
                {
                    await _downloader.DownloadFileAsync(
                        txtDownloadUrl.Text,
                        txtSavePath.Text,
                        _cancellationTokenSource.Token,
                        progress);
                }

                LogMessage("=== Download Completed Successfully ===");
                MessageBox.Show("Download completed successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                LogMessage("=== Download Cancelled by User ===");
                MessageBox.Show("Download was cancelled. Partial file has been saved and can be resumed.", 
                    "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"=== Download Failed ===");
                LogMessage($"Error: {ex.Message}");
                MessageBox.Show($"Download failed: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reset UI state
                btnStartDownload.IsEnabled = true;
                btnCancelDownload.IsEnabled = false;
                txtDownloadUrl.IsEnabled = true;
                txtSavePath.IsEnabled = true;
                chkValidateHash.IsEnabled = true;
                txtExpectedHash.IsEnabled = chkValidateHash.IsChecked == true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                LogMessage("Cancelling download...");
                _cancellationTokenSource.Cancel();
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Text = "";
            LogMessage("Log cleared.");
        }

        public void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                txtLog.Text += $"[{timestamp}] {message}\n";
                
                // Auto-scroll to bottom
                var scrollViewer = FindScrollViewer(txtLog);
                scrollViewer?.ScrollToEnd();
            });
        }

        private System.Windows.Controls.ScrollViewer? FindScrollViewer(System.Windows.DependencyObject element)
        {
            if (element is System.Windows.Controls.ScrollViewer scrollViewer)
                return scrollViewer;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _httpClient?.Dispose();
            base.OnClosed(e);
        }
    }

    // Custom logger provider that writes to the UI
    public class UILoggerProvider : ILoggerProvider
    {
        private readonly MainWindow _window;

        public UILoggerProvider(MainWindow window)
        {
            _window = window;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new UILogger(_window, categoryName);
        }

        public void Dispose() { }
    }

    public class UILogger : ILogger
    {
        private readonly MainWindow _window;
        private readonly string _categoryName;

        public UILogger(MainWindow window, string categoryName)
        {
            _window = window;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var level = logLevel switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRITICAL",
                _ => "LOG"
            };

            _window.LogMessage($"[{level}] {message}");
        }
    }
}