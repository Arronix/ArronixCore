using System.Collections.ObjectModel;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// One thing that happened, in a form any destination can deliver.
/// </summary>
/// <remarks>
/// <para>
/// The evidence for this envelope is exact: strip the imports and two properties from the equivalent type
/// in each of the four surveyed applications and the remainder is byte-for-byte identical. The envelope
/// was never the problem — exactly two slots carried the media, and those two are
/// <see cref="Summary"/> and <see cref="Context"/> here.
/// </para>
/// <para>
/// Three rules make it work. The extension that owns the item renders <see cref="Summary"/>, so a
/// destination never interprets media context. <see cref="Context"/> is opaque to the platform and to the
/// destination, keyed by the shape's own field identifiers, so a generic destination can present labeled
/// values without knowing what any of them mean. And the platform owns its own name, so the branding
/// constants each surveyed application carries in its notification base class disappear entirely.
/// </para>
/// </remarks>
public sealed record NotificationMessage
{
    /// <summary>
    /// Gets what happened.
    /// </summary>
    public required NotificationEvent Event { get; init; }

    /// <summary>
    /// Gets when it happened.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the wider operation this belongs to.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Gets what to say, rendered by the extension that owns the subject. This is also what an activity
    /// feed presents, so there is exactly one rendering path rather than two that can disagree.
    /// </summary>
    public required RenderedSummary Summary { get; init; }

    /// <summary>
    /// Gets the media kind involved, when one was.
    /// </summary>
    public MediaKindId? MediaKind { get; init; }

    /// <summary>
    /// Gets the item involved, when one was.
    /// </summary>
    public MediaItemRef? Item { get; init; }

    /// <summary>
    /// Gets the files involved.
    /// </summary>
    public IReadOnlyList<FileSummary> Files { get; init; } = [];

    /// <summary>
    /// Gets the files that were replaced, for an upgrade.
    /// </summary>
    public IReadOnlyList<FileSummary> ReplacedFiles { get; init; } = [];

    /// <summary>
    /// Gets the quality involved, when it is meaningful. Reuses the stable quality tier.
    /// </summary>
    public QualityTier? Quality { get; init; }

    /// <summary>
    /// Gets where the release came from, for events about acquisition.
    /// </summary>
    public DownloadAttribution? Download { get; init; }

    /// <summary>
    /// Gets the health check involved, for events about health. Reuses the stable health check.
    /// </summary>
    public HealthCheck? Health { get; init; }

    /// <summary>
    /// Gets extension-owned values keyed by the shape's field identifiers. Opaque to the platform and to
    /// the destination.
    /// </summary>
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Where a release came from and how it was transferred.
/// </summary>
/// <param name="ClientName">The name of the definition that transferred it.</param>
/// <param name="Protocol">The protocol it was transferred over.</param>
/// <param name="DownloadId">The transfer client's own identifier for the transfer.</param>
/// <param name="Indexer">The source it was found on, when that is known.</param>
public sealed record DownloadAttribution(
    string ClientName,
    DownloadProtocol Protocol,
    string DownloadId,
    ProviderId? Indexer);

/// <summary>
/// One file a notification is about.
/// </summary>
/// <param name="RelativePath">The path within the library entry's folder.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Quality">The file's quality, when it was determined.</param>
/// <param name="Languages">The languages the file carries.</param>
public sealed record FileSummary(
    string RelativePath,
    long Size,
    QualityTier? Quality,
    IReadOnlyList<Language> Languages);
