using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.DTOs;

/// <summary>An indexer's raw listing for an artifact that can be fetched.</summary>
/// <remarks>
/// This is provider transport data, not a semantic release and not a selection judgment. A media kind
/// interprets <see cref="Title"/> into its own <c>TRelease</c>; the common engine then matches that release
/// to a typed target and applies the compiled release policy.
/// </remarks>
public sealed record ReleaseListing(
    ReleaseId ReleaseId,
    string Title,
    Uri DownloadUrl,
    string IndexerId,
    MediaKindId MediaKind,
    long Size,
    DateTime PublishDate,
    Uri? InfoUrl = null,
    IReadOnlyDictionary<string, string>? AdditionalData = null);
