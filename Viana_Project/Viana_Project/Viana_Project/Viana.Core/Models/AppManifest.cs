using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Viana.Core.Models
{
    /// <summary>
    /// Represents the application manifest (manifest.json) containing version and module information
    /// </summary>
    public class AppManifest
    {
        /// <summary>
        /// Application version (e.g., "1.2.3")
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// List of application modules/files with their metadata
        /// </summary>
        [JsonPropertyName("modules")]
        public List<AppModule> Modules { get; set; } = new();

        /// <summary>
        /// Base URL for downloading modules (optional)
        /// </summary>
        [JsonPropertyName("baseUrl")]
        public string? BaseUrl { get; set; }

        /// <summary>
        /// Timestamp when the manifest was generated
        /// </summary>
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
    }
}