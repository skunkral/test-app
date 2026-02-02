using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32;
using Viana.Core.Models;
using Viana.Infrastructure.FileSystem;
using Viana.Infrastructure.Services;

namespace Viana.TestApp
{
    public partial class MainWindow : Window
    {
        private readonly ILogger _logger; // Generic logger for UI
        private readonly ILogger<ResumableDownloader> _downloaderLogger;
        private readonly ILogger<ManifestManager> _manifestLogger;
        private readonly ILogger<RollbackManager> _rollbackLogger;
        
        private readonly ResumableDownloader _downloader;
        private readonly ManifestManager _manifestManager;
        private readonly RollbackManager _rollbackManager;
        
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize default paths
            txtSavePath.Text = Path.Combine(Path.GetTempPath(), "viana_test_download.bin");
            txtActivePath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Viana", "Active");
            txtStagingPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Viana", "Staging");
            txtRollbackActivePath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Viana", "Active");

            // Setup logging
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new UILoggerProvider(this));
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            _logger = loggerFactory.CreateLogger("VianaApp");
            _downloaderLogger = loggerFactory.CreateLogger<ResumableDownloader>();
            _manifestLogger = loggerFactory.CreateLogger<ManifestManager>();
            _rollbackLogger = loggerFactory.CreateLogger<RollbackManager>();

            // Create HttpClient
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(30);

            // Initialize Services
            _downloader = new ResumableDownloader(_httpClient, _downloaderLogger);
            _manifestManager = new ManifestManager(_httpClient, _manifestLogger);
            _rollbackManager = new RollbackManager(_rollbackLogger);

            LogMessage("Application initialized. Components ready.");
        }

        #region Resumable Downloader Logic

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
            if (string.IsNullOrWhiteSpace(txtDownloadUrl.Text)) return;

            try
            {
                btnStartDownload.IsEnabled = false;
                btnCancelDownload.IsEnabled = true;
                
                // Reset progress UI
                progressBar.Value = 0;
                txtProgressPercent.Text = "0%";
                txtStatus.Text = "Starting...";

                _cancellationTokenSource = new CancellationTokenSource();
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
                            txtSpeed.Text = $"{state.BytesPerSecond.Value / 1024.0 / 1024.0:F2} MB/s";
                    });
                });

                if (chkValidateHash.IsChecked == true && !string.IsNullOrWhiteSpace(txtExpectedHash.Text))
                {
                    await _downloader.DownloadFileAsync(txtDownloadUrl.Text, txtSavePath.Text, txtExpectedHash.Text, _cancellationTokenSource.Token, progress);
                }
                else
                {
                    await _downloader.DownloadFileAsync(txtDownloadUrl.Text, txtSavePath.Text, _cancellationTokenSource.Token, progress);
                }

                System.Windows.MessageBox.Show("Download completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                LogMessage("Download Cancelled.");
            }
            catch (Exception ex)
            {
                LogMessage($"Download Failed: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStartDownload.IsEnabled = true;
                btnCancelDownload.IsEnabled = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        #endregion

        #region Manifest Manager Logic

        private void BrowseActivePath_Click(object sender, RoutedEventArgs e) => BrowseFolder(txtActivePath);

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage($"Checking for updates from: {txtManifestUrl.Text}");
                var updates = await _manifestManager.CheckForUpdatesAsync(txtManifestUrl.Text, txtActivePath.Text);
                
                gridUpdates.ItemsSource = updates;
                LogMessage($"Found {updates.Count} updates.");
            }
            catch (Exception ex)
            {
                LogMessage($"Error checking updates: {ex.Message}");
                System.Windows.MessageBox.Show($"Check failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Rollback Manager Logic

        private void BrowseStagingPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(txtStagingPath);
        private void BrowseRollbackActivePath_Click(object sender, RoutedEventArgs e) => BrowseFolder(txtRollbackActivePath);

        private void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backup = _rollbackManager.CreateBackup(txtRollbackActivePath.Text);
                LogMessage($"Backup created at: {backup}");
                System.Windows.MessageBox.Show($"Backup created: {backup}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"Backup failed: {ex.Message}");
            }
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            // Simple input dialog for backup path would be better, but for now we'll just log
            LogMessage("To test restore, please implement a Selector or manually use code. (Simplified for MVP UI)");
            // In a real test app, we'd pick a folder.
            var dialog = new OpenFileDialog 
            { 
                Title = "Select 'manifest.json' inside the Backup folder to identify it",
                Filter = "Json Files|*.json|All Files|*.*",
                ValidateNames = false,
                CheckFileExists = false,
                FileName = "Folder Selection"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var backupDir = Path.GetDirectoryName(dialog.FileName);
                try {
                     _rollbackManager.RestoreBackup(backupDir!, txtRollbackActivePath.Text);
                     LogMessage("Restore completed.");
                     System.Windows.MessageBox.Show("Restore completed.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch(Exception ex) { LogMessage($"Restore failed: {ex.Message}"); }
            }
        }

        private void PerformSwap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? service = string.IsNullOrWhiteSpace(txtServiceName.Text) ? null : txtServiceName.Text;
                _rollbackManager.PerformAtomicSwap(txtStagingPath.Text, txtRollbackActivePath.Text, service);
                LogMessage("Atomic Swap Completed Successfully.");
                System.Windows.MessageBox.Show("Swap completed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"Swap Failed: {ex.Message}");
                System.Windows.MessageBox.Show($"Swap failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helpers

        private void BrowseFolder(System.Windows.Controls.TextBox textBox)
        {
            // Using OpenFileDialog to select a "file" but acting as folder picker
            // Or just just telling user to paste path.
            // For MVP simplicity, let's just use OpenFileDialog with a dummy name
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                FileName = "Folder Selection",
                Title = "Select Folder (Click Open)"
            };
            
            if (dialog.ShowDialog() == true)
            {
                textBox.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Text = "";
        }

        public void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                txtLog.Text += $"[{timestamp}] {message}\n";
                var scrollViewer = FindScrollViewer(txtLog);
                scrollViewer?.ScrollToEnd();
            });
        }

        private System.Windows.Controls.ScrollViewer? FindScrollViewer(System.Windows.DependencyObject element)
        {
            if (element is System.Windows.Controls.ScrollViewer scrollViewer) return scrollViewer;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
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

        #endregion
    }

    // Logger Provider Integration
    public class UILoggerProvider : ILoggerProvider
    {
        private readonly MainWindow _window;
        public UILoggerProvider(MainWindow window) => _window = window;
        public ILogger CreateLogger(string categoryName) => new UILogger(_window, categoryName);
        public void Dispose() { }
    }

    public class UILogger : ILogger
    {
        private readonly MainWindow _window;
        private readonly string _categoryName;
        public UILogger(MainWindow window, string categoryName) { _window = window; _categoryName = categoryName; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var level = logLevel.ToString().ToUpper();
            // Shorten category
            var cat = _categoryName.Contains(".") ? _categoryName.Substring(_categoryName.LastIndexOf('.') + 1) : _categoryName;
            _window.LogMessage($"[{level}] [{cat}] {msg}");
        }
    }
}