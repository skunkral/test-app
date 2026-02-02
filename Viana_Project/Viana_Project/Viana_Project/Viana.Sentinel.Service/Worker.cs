using Viana.Core.Interfaces;
using Viana.Infrastructure.FileSystem;
using Viana.Core.Models;

namespace Viana.Sentinel.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IDownloader _downloader;
    private readonly IUpdateManager _updateManager;
    private readonly RollbackManager _rollbackManager;
    
    // Configuration placeholders (TODO: Move to appsettings.json)
    // Using a local file URL for testing default
    private const string ManifestUrl = "http://localhost:5000/manifest.json"; 
    private const string AppBaseUrl = "http://localhost:5000/modules/";
    private readonly string _basePath;
    private readonly string _activePath;
    private readonly string _stagingPath;
    private const string ServiceName = "VianaApp"; // Service to control during update

    public Worker(
        ILogger<Worker> logger,
        IDownloader downloader,
        IUpdateManager updateManager,
        RollbackManager rollbackManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
        _rollbackManager = rollbackManager ?? throw new ArgumentNullException(nameof(rollbackManager));

        // Setup paths (AppData/Viana)
        _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Viana");
        _activePath = Path.Combine(_basePath, "Active");
        _stagingPath = Path.Combine(_basePath, "Staging");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Viana Sentinel Service started.");
        _logger.LogInformation("Monitoring paths - Active: {Active}, Staging: {Staging}", _activePath, _stagingPath);

        // Ensure directories exist
        Directory.CreateDirectory(_activePath);

        // Initial delay to let system settle
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Checking for updates at: {time}", DateTimeOffset.Now);

                // Step 1: Check for updates
                // Note: using dry-run check first would be better, but for MVP we get the list directly
                // Logic: If manifest fetch fails, catch exception and retry next loop
                List<AppModule> updates;
                AppManifest manifest;
                
                try 
                {
                    manifest = await _updateManager.FetchManifestAsync(ManifestUrl, stoppingToken);
                    updates = await _updateManager.GetModulesToUpdateAsync(manifest, _activePath, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check for updates. Will retry in 5 minutes.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }

                if (updates.Count == 0)
                {
                    _logger.LogInformation("System is up to date.");
                }
                else
                {
                    _logger.LogInformation("Found {Count} modules to update. Starting update process...", updates.Count);

                    await PerformUpdateAsync(updates, manifest, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in update loop");
            }

            // Wait 5 minutes before next check
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task PerformUpdateAsync(List<AppModule> updates, AppManifest manifest, CancellationToken stoppingToken)
    {
        try
        {
            // Step 2: Prepare Staging
            // Clean/Recreate Staging
            if (Directory.Exists(_stagingPath))
            {
                Directory.Delete(_stagingPath, true);
            }
            Directory.CreateDirectory(_stagingPath);

            // Copy existing Active files to Staging (to minimize downloads)
            // This is a "Delta Update" strategy base
            CopyDirectory(_activePath, _stagingPath);

            // Download new/changed files to Staging
            foreach (var module in updates)
            {
                var downloadUrl = !string.IsNullOrEmpty(module.Url) 
                    ? module.Url 
                    : $"{AppBaseUrl.TrimEnd('/')}/{module.Name}";
                
                var localPath = Path.Combine(_stagingPath, module.FilePath);
                
                _logger.LogInformation("Downloading module: {Name} ({Size} bytes)", module.Name, module.Size);

                await _downloader.DownloadFileAsync(
                    downloadUrl,
                    localPath,
                    module.Hash, // Validate hash
                    stoppingToken
                );
            }

            _logger.LogInformation("All files downloaded and verified in Staging.");

            // Step 3: Atomic Swap
            // Note: For MVP testing, passing null as service name to avoid failing if service doesn't exist
            // In production, valid ServiceName is required
            _rollbackManager.PerformAtomicSwap(_stagingPath, _activePath, null); // Pass ServiceName in prod

            _logger.LogInformation("Update applied successfully! New version: {Version}", manifest.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update process failed. Rolling back changes.");
            // Cleanup Staging on failure
            if (Directory.Exists(_stagingPath))
            {
                try { Directory.Delete(_stagingPath, true); } catch { }
            }
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            Directory.CreateDirectory(newDestinationDir);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }
}
