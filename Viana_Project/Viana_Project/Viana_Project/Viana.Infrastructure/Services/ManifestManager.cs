using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Viana.Core.Interfaces;
using Viana.Core.Models;

namespace Viana.Infrastructure.Services
{
    /// <summary>
    /// Manages application updates by comparing local files with remote manifest
    /// Uses MD5 hashing for file comparison (Minecraft-style update detection)
    /// </summary>
    public class ManifestManager : IUpdateManager
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ManifestManager> _logger;

        public ManifestManager(HttpClient httpClient, ILogger<ManifestManager> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Fetches the remote manifest from the specified URL
        /// </summary>
        public async Task<AppManifest> FetchManifestAsync(string manifestUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
                throw new ArgumentException("Manifest URL cannot be null or empty", nameof(manifestUrl));

            _logger.LogInformation("Fetching manifest from: {ManifestUrl}", manifestUrl);

            try
            {
                var response = await _httpClient.GetAsync(manifestUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                var manifest = JsonSerializer.Deserialize<AppManifest>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest == null)
                    throw new InvalidOperationException("Failed to deserialize manifest");

                _logger.LogInformation("Manifest fetched successfully. Version: {Version}, Modules: {ModuleCount}", 
                    manifest.Version, manifest.Modules?.Count ?? 0);

                return manifest;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to fetch manifest from {ManifestUrl}", manifestUrl);
                throw new InvalidOperationException($"Failed to fetch manifest: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse manifest JSON");
                throw new InvalidOperationException($"Invalid manifest format: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Compares local files against the manifest and returns modules that need updating
        /// </summary>
        public async Task<List<AppModule>> GetModulesToUpdateAsync(
            AppManifest manifest, 
            string localPath, 
            CancellationToken cancellationToken = default)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            if (string.IsNullOrWhiteSpace(localPath))
                throw new ArgumentException("Local path cannot be null or empty", nameof(localPath));

            if (!Directory.Exists(localPath))
            {
                _logger.LogWarning("Local path does not exist: {LocalPath}. All modules will be downloaded.", localPath);
                return manifest.Modules ?? new List<AppModule>();
            }

            _logger.LogInformation("Scanning local directory: {LocalPath}", localPath);

            var modulesToUpdate = new List<AppModule>();

            if (manifest.Modules == null || manifest.Modules.Count == 0)
            {
                _logger.LogWarning("Manifest contains no modules");
                return modulesToUpdate;
            }

            foreach (var module in manifest.Modules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localFilePath = Path.Combine(localPath, module.FilePath);

                // Check if file exists
                if (!File.Exists(localFilePath))
                {
                    _logger.LogInformation("Module missing: {ModuleName} at {FilePath}", module.Name, module.FilePath);
                    modulesToUpdate.Add(module);
                    continue;
                }

                // Calculate MD5 hash of local file
                var localHash = await CalculateMD5HashAsync(localFilePath, cancellationToken);
                var remoteHash = module.Hash.Replace("-", "").ToLowerInvariant();

                if (localHash != remoteHash)
                {
                    _logger.LogInformation("Module hash mismatch: {ModuleName}. Local: {LocalHash}, Remote: {RemoteHash}", 
                        module.Name, localHash, remoteHash);
                    modulesToUpdate.Add(module);
                }
                else
                {
                    _logger.LogDebug("Module up to date: {ModuleName}", module.Name);
                }
            }

            _logger.LogInformation("Update scan complete. Modules to update: {UpdateCount}/{TotalCount}", 
                modulesToUpdate.Count, manifest.Modules.Count);

            return modulesToUpdate;
        }

        /// <summary>
        /// Checks for updates by fetching manifest and comparing with local files
        /// </summary>
        public async Task<List<AppModule>> CheckForUpdatesAsync(
            string manifestUrl, 
            string localPath, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("=== Starting Update Check ===");
            _logger.LogInformation("Manifest URL: {ManifestUrl}", manifestUrl);
            _logger.LogInformation("Local Path: {LocalPath}", localPath);

            var manifest = await FetchManifestAsync(manifestUrl, cancellationToken);
            var modulesToUpdate = await GetModulesToUpdateAsync(manifest, localPath, cancellationToken);

            if (modulesToUpdate.Count == 0)
            {
                _logger.LogInformation("=== System is up to date ===");
            }
            else
            {
                _logger.LogInformation("=== Updates Available ===");
                foreach (var module in modulesToUpdate)
                {
                    _logger.LogInformation("  - {ModuleName} ({FilePath})", module.Name, module.FilePath);
                }
            }

            return modulesToUpdate;
        }

        /// <summary>
        /// Calculates MD5 hash of a file
        /// </summary>
        private async Task<string> CalculateMD5HashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var md5 = MD5.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            
            var hashBytes = await md5.ComputeHashAsync(stream, cancellationToken);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}