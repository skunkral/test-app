using System;
using System.IO;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;

namespace Viana.Infrastructure.FileSystem
{
    /// <summary>
    /// Manages atomic directory swapping for application updates with rollback capability
    /// Implements "Deepfreeze-style" atomic operations
    /// </summary>
    public class RollbackManager
    {
        private readonly ILogger<RollbackManager> _logger;

        public RollbackManager(ILogger<RollbackManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a backup of the active directory
        /// </summary>
        /// <param name="activePath">Path to the Active directory</param>
        /// <returns>Path to the created backup directory</returns>
        public string CreateBackup(string activePath)
        {
            if (string.IsNullOrWhiteSpace(activePath))
                throw new ArgumentException("Active path cannot be null or empty", nameof(activePath));

            if (!Directory.Exists(activePath))
                throw new DirectoryNotFoundException($"Active directory not found: {activePath}");

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var parentDir = Directory.GetParent(activePath)?.FullName 
                ?? throw new InvalidOperationException("Cannot determine parent directory");

            var backupPath = Path.Combine(parentDir, $"Backup_{timestamp}");

            _logger.LogInformation("Creating backup: {ActivePath} -> {BackupPath}", activePath, backupPath);

            try
            {
                // Use Directory.Move for atomic operation
                Directory.Move(activePath, backupPath);
                _logger.LogInformation("Backup created successfully: {BackupPath}", backupPath);
                return backupPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create backup from {ActivePath}", activePath);
                throw new InvalidOperationException($"Backup creation failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Restores a backup directory to the active location
        /// </summary>
        /// <param name="backupPath">Path to the backup directory</param>
        /// <param name="activePath">Path where the Active directory should be restored</param>
        public void RestoreBackup(string backupPath, string activePath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                throw new ArgumentException("Backup path cannot be null or empty", nameof(backupPath));

            if (string.IsNullOrWhiteSpace(activePath))
                throw new ArgumentException("Active path cannot be null or empty", nameof(activePath));

            if (!Directory.Exists(backupPath))
                throw new DirectoryNotFoundException($"Backup directory not found: {backupPath}");

            _logger.LogInformation("Restoring backup: {BackupPath} -> {ActivePath}", backupPath, activePath);

            try
            {
                // Remove active directory if it exists
                if (Directory.Exists(activePath))
                {
                    _logger.LogWarning("Active directory exists, removing: {ActivePath}", activePath);
                    Directory.Delete(activePath, true);
                }

                // Restore backup
                Directory.Move(backupPath, activePath);
                _logger.LogInformation("Backup restored successfully to: {ActivePath}", activePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore backup from {BackupPath}", backupPath);
                throw new InvalidOperationException($"Backup restoration failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Performs atomic swap: Staging -> Active (with backup)
        /// Includes service stop/start and automatic rollback on failure
        /// </summary>
        /// <param name="stagingPath">Path to the Staging directory</param>
        /// <param name="activePath">Path to the Active directory</param>
        /// <param name="serviceName">Name of the service to stop/start (optional)</param>
        public void PerformAtomicSwap(string stagingPath, string activePath, string? serviceName = null)
        {
            if (string.IsNullOrWhiteSpace(stagingPath))
                throw new ArgumentException("Staging path cannot be null or empty", nameof(stagingPath));

            if (string.IsNullOrWhiteSpace(activePath))
                throw new ArgumentException("Active path cannot be null or empty", nameof(activePath));

            if (!Directory.Exists(stagingPath))
                throw new DirectoryNotFoundException($"Staging directory not found: {stagingPath}");

            _logger.LogInformation("=== Starting Atomic Swap ===");
            _logger.LogInformation("Staging: {StagingPath}", stagingPath);
            _logger.LogInformation("Active: {ActivePath}", activePath);
            _logger.LogInformation("Service: {ServiceName}", serviceName ?? "None");

            string? backupPath = null;
            bool serviceWasStopped = false;

            try
            {
                // Step 1: Stop service if specified
                if (!string.IsNullOrWhiteSpace(serviceName))
                {
                    StopService(serviceName);
                    serviceWasStopped = true;
                }

                // Step 2: Create backup (Active -> Backup_Timestamp)
                if (Directory.Exists(activePath))
                {
                    backupPath = CreateBackup(activePath);
                }
                else
                {
                    _logger.LogWarning("Active directory does not exist, skipping backup");
                }

                // Step 3: Move Staging to Active
                _logger.LogInformation("Moving Staging to Active...");
                Directory.Move(stagingPath, activePath);
                _logger.LogInformation("Staging moved to Active successfully");

                // Step 4: Start service if it was stopped
                if (serviceWasStopped && !string.IsNullOrWhiteSpace(serviceName))
                {
                    StartService(serviceName);
                }

                _logger.LogInformation("=== Atomic Swap Completed Successfully ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Atomic swap failed. Initiating rollback...");

                // Rollback: Restore backup if it was created
                if (!string.IsNullOrWhiteSpace(backupPath) && Directory.Exists(backupPath))
                {
                    try
                    {
                        _logger.LogWarning("Rolling back to backup...");
                        
                        // Remove failed active directory if it exists
                        if (Directory.Exists(activePath))
                        {
                            Directory.Delete(activePath, true);
                        }

                        RestoreBackup(backupPath, activePath);
                        _logger.LogInformation("Rollback completed successfully");

                        // Try to restart service after rollback
                        if (serviceWasStopped && !string.IsNullOrWhiteSpace(serviceName))
                        {
                            try
                            {
                                StartService(serviceName);
                            }
                            catch (Exception serviceEx)
                            {
                                _logger.LogError(serviceEx, "Failed to start service after rollback");
                            }
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogCritical(rollbackEx, "CRITICAL: Rollback failed! Manual intervention required.");
                        throw new InvalidOperationException(
                            $"Atomic swap failed and rollback also failed. Backup location: {backupPath}. " +
                            $"Original error: {ex.Message}. Rollback error: {rollbackEx.Message}", ex);
                    }
                }

                throw new InvalidOperationException($"Atomic swap failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Stops a Windows service
        /// </summary>
        private void StopService(string serviceName)
        {
            _logger.LogInformation("Stopping service: {ServiceName}", serviceName);

            try
            {
                using var service = new ServiceController(serviceName);
                
                if (service.Status == ServiceControllerStatus.Running)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    _logger.LogInformation("Service stopped: {ServiceName}", serviceName);
                }
                else
                {
                    _logger.LogInformation("Service is not running: {ServiceName} (Status: {Status})", 
                        serviceName, service.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop service: {ServiceName}", serviceName);
                throw new InvalidOperationException($"Failed to stop service '{serviceName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Starts a Windows service
        /// </summary>
        private void StartService(string serviceName)
        {
            _logger.LogInformation("Starting service: {ServiceName}", serviceName);

            try
            {
                using var service = new ServiceController(serviceName);
                
                if (service.Status != ServiceControllerStatus.Running)
                {
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    _logger.LogInformation("Service started: {ServiceName}", serviceName);
                }
                else
                {
                    _logger.LogInformation("Service is already running: {ServiceName}", serviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start service: {ServiceName}", serviceName);
                throw new InvalidOperationException($"Failed to start service '{serviceName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Cleans up old backup directories (keeps only the most recent N backups)
        /// </summary>
        /// <param name="parentPath">Parent directory containing backups</param>
        /// <param name="keepCount">Number of recent backups to keep (default: 3)</param>
        public void CleanupOldBackups(string parentPath, int keepCount = 3)
        {
            if (string.IsNullOrWhiteSpace(parentPath))
                throw new ArgumentException("Parent path cannot be null or empty", nameof(parentPath));

            if (!Directory.Exists(parentPath))
                return;

            _logger.LogInformation("Cleaning up old backups in: {ParentPath} (keeping {KeepCount})", parentPath, keepCount);

            try
            {
                var backupDirs = Directory.GetDirectories(parentPath, "Backup_*")
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.CreationTime)
                    .ToList();

                var toDelete = backupDirs.Skip(keepCount).ToList();

                foreach (var dir in toDelete)
                {
                    _logger.LogInformation("Deleting old backup: {BackupPath}", dir.FullName);
                    dir.Delete(true);
                }

                _logger.LogInformation("Cleanup complete. Deleted {Count} old backups", toDelete.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup old backups");
            }
        }
    }
}