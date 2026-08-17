using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// A client that transfers releases on the platform's behalf.
/// </summary>
/// <remarks>
/// This contract differs from the surveyed applications' by exactly the one line that carried a media
/// type; every other member is character-for-character identical in all four of them.
/// <see cref="GrabRequest"/> is that line, made media-neutral.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IDownloader : IProvider
{
    /// <summary>
    /// Gets the protocol this client transfers over.
    /// </summary>
    DownloadProtocol Protocol { get; }

    /// <summary>
    /// Hands a release to the client.
    /// </summary>
    /// <param name="invocation">The definition being used, and its session.</param>
    /// <param name="request">What to transfer, and what it is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client's own identifier for the transfer.</returns>
    Task<string> DownloadAsync(
        ProviderInvocation invocation,
        GrabRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists everything the client is currently handling.
    /// </summary>
    /// <param name="invocation">The definition being asked, and its session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transfers.</returns>
    Task<IReadOnlyList<DownloadItem>> GetItemsAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a transfer from the client.
    /// </summary>
    /// <param name="invocation">The definition being used, and its session.</param>
    /// <param name="downloadId">The client's identifier for the transfer.</param>
    /// <param name="deleteData">Whether the transferred files are deleted along with the record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the transfer has been removed.</returns>
    Task RemoveItemAsync(
        ProviderInvocation invocation,
        string downloadId,
        bool deleteData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the client the platform has finished with a transfer's files.
    /// </summary>
    /// <param name="invocation">The definition being used, and its session.</param>
    /// <param name="downloadId">The client's identifier for the transfer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the client has been told.</returns>
    Task MarkImportedAsync(
        ProviderInvocation invocation,
        string downloadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports how the client is configured.
    /// </summary>
    /// <param name="invocation">The definition being asked, and its session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client's configuration as it reports it.</returns>
    Task<DownloaderInfo> GetInfoAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A request to transfer one release.
/// </summary>
/// <param name="Release">The release being transferred.</param>
/// <param name="DownloadUrl">Where the transfer artifact was fetched from.</param>
/// <param name="Title">The release's title, for the client's own display.</param>
/// <param name="MediaKind">The media kind the release is for, when it is for one.</param>
/// <param name="Units">The items the release is expected to satisfy.</param>
/// <param name="Category">The client-side category the transfer should be filed under.</param>
/// <param name="Protocol">The protocol the release is transferred over.</param>
/// <param name="Indexer">The source the release was found on, when that is known.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record GrabRequest(
    ReleaseId Release,
    Uri DownloadUrl,
    string Title,
    MediaKindId? MediaKind,
    IReadOnlyList<MediaItemRef> Units,
    string? Category,
    DownloadProtocol Protocol,
    ProviderId? Indexer);

/// <summary>
/// One transfer a client is handling.
/// </summary>
/// <param name="DownloadId">The client's identifier for the transfer.</param>
/// <param name="Title">The title the client knows it by.</param>
/// <param name="Status">How the transfer is going.</param>
/// <param name="TotalSize">The total size in bytes.</param>
/// <param name="RemainingSize">The size still to transfer, in bytes.</param>
/// <param name="RemainingTime">The client's estimate of the time left, when it offers one.</param>
/// <param name="OutputPath">Where the transferred files are, once there are any.</param>
/// <param name="Protocol">The protocol used.</param>
/// <param name="CanMoveFiles">Whether the platform may move the files rather than copy them.</param>
/// <param name="CanBeRemoved">Whether the transfer may be removed from the client.</param>
/// <param name="ErrorMessage">Why the transfer failed, when it did.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record DownloadItem(
    string DownloadId,
    string Title,
    DownloadItemStatus Status,
    long TotalSize,
    long RemainingSize,
    TimeSpan? RemainingTime,
    string? OutputPath,
    DownloadProtocol Protocol,
    bool CanMoveFiles,
    bool CanBeRemoved,
    string? ErrorMessage);

/// <summary>
/// How a transfer is going.
/// </summary>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum DownloadItemStatus
{
    /// <summary>Accepted and waiting to start.</summary>
    Queued = 0,

    /// <summary>Started and deliberately halted.</summary>
    Paused = 1,

    /// <summary>In progress.</summary>
    Downloading = 2,

    /// <summary>Finished, and the files are available.</summary>
    Completed = 3,

    /// <summary>Stopped and cannot finish.</summary>
    Failed = 4,

    /// <summary>Progressing, but the client is reporting a problem.</summary>
    Warning = 5
}

/// <summary>
/// How a transfer client is configured.
/// </summary>
/// <param name="Name">The name the client reports for itself.</param>
/// <param name="OutputRootFolders">The folders the client writes finished transfers into.</param>
/// <param name="RemovesCompleted">Whether the client removes finished transfers by itself.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record DownloaderInfo(
    string Name,
    IReadOnlyList<string> OutputRootFolders,
    bool RemovesCompleted);
