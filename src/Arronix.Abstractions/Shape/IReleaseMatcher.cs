using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Decides which items a piece of text refers to.
/// </summary>
/// <remarks>
/// <para>
/// The single most media-specific step in the platform, and the one the host never attempts: the
/// surveyed implementations range from a switchboard over several numbering schemes to an optimal
/// assignment algorithm, and they have nothing in common but their signature.
/// </para>
/// <para>
/// One method serves both the acquisition path and the import path. They differ only in
/// <see cref="MatchRequest.Source"/>, which generalizes the provenance flag the surveyed matchers thread
/// through by hand, and getting that flag wrong is what makes an alternative numbering scheme silently
/// apply to a file it should not.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IReleaseMatcher
{
    /// <summary>
    /// Gets the media kind this matcher serves.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Resolves text to the items it refers to.
    /// </summary>
    /// <param name="request">What to match, and what is already known about it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items matched, with the matcher's confidence in them.</returns>
    Task<MatchOutcome> MatchAsync(MatchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// What to match, and everything already known about it.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MatchRequest
{
    /// <summary>
    /// Gets the media kind the match is being made within.
    /// </summary>
    public required MediaKindId MediaKind { get; init; }

    /// <summary>
    /// Gets the raw text: a release name, a file name or a folder name.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the stable parser's output, when a parser is registered for the kind.
    /// </summary>
    public ParsedRelease? Parsed { get; init; }

    /// <summary>
    /// Gets the coordinates already read from the text or from a file's embedded metadata.
    /// </summary>
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;

    /// <summary>
    /// Gets the library entry the caller believes the text belongs to, when it knows.
    /// </summary>
    public MediaItemRef? Scope { get; init; }

    /// <summary>
    /// Gets where the text came from.
    /// </summary>
    public required MatchSource Source { get; init; }

    /// <summary>
    /// Gets the external identifiers the caller already resolved.
    /// </summary>
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];
}

/// <summary>
/// Where the text being matched came from.
/// </summary>
/// <remarks>
/// Provenance changes the answer. A numbering scheme that is only valid in release names must not be
/// applied to a file name, and a matcher cannot tell the two apart from the text alone.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum MatchSource
{
    /// <summary>A release name, as published by a release source.</summary>
    ReleaseName = 0,

    /// <summary>A file name on disk.</summary>
    FileName = 1,

    /// <summary>A folder name on disk.</summary>
    FolderName = 2,

    /// <summary>The title a transfer client reports for an item it is handling.</summary>
    DownloaderTitle = 3,

    /// <summary>Metadata embedded in a file.</summary>
    Tags = 4
}

/// <summary>
/// The items a piece of text was found to refer to.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MatchOutcome
{
    /// <summary>
    /// Gets the items matched. Empty means the text refers to nothing in the library.
    /// </summary>
    public required IReadOnlyList<MediaItemRef> Units { get; init; }

    /// <summary>
    /// Gets how sure the matcher is. Reuses the stable confidence vocabulary.
    /// </summary>
    public required MatchConfidence Confidence { get; init; }

    /// <summary>
    /// Gets what the match was made on. Categorical, and the value downstream import decisions branch
    /// on; confidence says how sure, this says why.
    /// </summary>
    public MatchBasis Basis { get; init; } = MatchBasis.None;

    /// <summary>
    /// Gets the coordinates the matcher read or derived while matching.
    /// </summary>
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;

    /// <summary>
    /// Gets why nothing was matched, when nothing was.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Gets anything the matcher wants an operator to know about a match it nonetheless made.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
