namespace Viana.Core.Models
{
    /// <summary>
    /// Represents the current state of a file download operation
    /// </summary>
    public class DownloadState
    {
        /// <summary>
        /// Name of the file being downloaded
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Total file size in bytes
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Number of bytes downloaded so far
        /// </summary>
        public long DownloadedBytes { get; set; }

        /// <summary>
        /// Current download status
        /// </summary>
        public DownloadStatus Status { get; set; }

        /// <summary>
        /// Download progress as a percentage (0-100)
        /// </summary>
        public double Progress => TotalBytes > 0 ? (DownloadedBytes * 100.0 / TotalBytes) : 0;

        /// <summary>
        /// Download speed in bytes per second (optional)
        /// </summary>
        public long? BytesPerSecond { get; set; }

        /// <summary>
        /// Error message if status is Failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Download operation status
    /// </summary>
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Completed,
        Failed,
        Cancelled
    }
}