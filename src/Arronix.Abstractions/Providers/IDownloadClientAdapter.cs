using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Adapter for download clients (e.g., qBittorrent, SABnzbd, Transmission).
/// Handles communication with download clients to send and monitor downloads.
/// </summary>
public interface IDownloadClientAdapter
{
    /// <summary>
    /// Gets the unique identifier for this download client.
    /// </summary>
    string ClientId { get; }

    /// <summary>
    /// Gets the human-readable name of this download client.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the protocol this client handles (e.g., "torrent", "usenet").
    /// </summary>
    string Protocol { get; }

    /// <summary>
    /// Sends a release to the download client.
    /// </summary>
    /// <param name="releaseId">The release identifier.</param>
    /// <param name="downloadUrl">The download URL or magnet link.</param>
    /// <param name="category">Optional category for organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download client's identifier for this download.</returns>
    Task<string> SendDownloadAsync(
        ReleaseId releaseId,
        string downloadUrl,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a download.
    /// </summary>
    /// <param name="downloadClientId">The download client's identifier for the download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download status, or null if not found.</returns>
    Task<DownloadStatus?> GetDownloadStatusAsync(
        string downloadClientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the download client.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the connection is successful; otherwise, false.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the status of a download.
/// </summary>
public record DownloadStatus(
    string ClientId,
    string Status,
    double Progress,
    long TotalSize,
    long DownloadedSize,
    string? ErrorMessage);
