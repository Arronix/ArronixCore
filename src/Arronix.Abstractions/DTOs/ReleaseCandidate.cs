using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a release candidate from an indexer.
/// Contains information about a downloadable release that may match media content.
/// </summary>
public record ReleaseCandidate(
    ReleaseId ReleaseId,
    string Title,
    string DownloadUrl,
    string IndexerId,
    MediaKindId MediaKind,
    long Size,
    DateTime PublishDate,
    string? InfoUrl = null,
    IReadOnlyDictionary<string, string>? AdditionalData = null);
