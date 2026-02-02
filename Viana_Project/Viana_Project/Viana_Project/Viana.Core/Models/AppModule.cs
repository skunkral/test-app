using System.Text.Json.Serialization;

namespace Viana.Core.Models
{
    /// <summary>
    /// Represents a single module/file in the application manifest
    /// </summary>
    public class AppModule
    {
        /// <summary>
        /// Name of the module (e.g., "CoreEngine.dll")
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Relative file path within the application directory
        /// </summary>
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// SHA256 hash of the file for integrity verification
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// File size in bytes
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// Download URL for this module (optional, may be constructed from base URL)
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
