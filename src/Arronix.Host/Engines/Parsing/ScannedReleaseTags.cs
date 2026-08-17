// The revision axis is an experimental shape contract until 1.0.
#pragma warning disable ARX0013

using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// The tag evidence the host's kind-agnostic scanners read out of one release title.
/// </summary>
/// <remarks>
/// This is the single bag every declared rung table consumes. The shared vocabulary lives once, host-side,
/// because a source or codec token means the same thing to every kind; a kind extends recognition only
/// through its declared token tables, whose rows land in <see cref="Extra"/> and are reachable through the
/// same <c>tags.*</c> predicate subjects as the built-in members.
/// </remarks>
internal sealed record ScannedReleaseTags
{
    /// <summary>Gets the empty bag.</summary>
    internal static ScannedReleaseTags Empty { get; } = new();

    /// <summary>
    /// Gets the source group the title named, in the host vocabulary the source scan's named groups
    /// spell: <c>bluray</c>, <c>webdl</c>, <c>webrip</c>, <c>hdtv</c>, <c>bdrip</c>, <c>brrip</c>,
    /// <c>dvdr</c>, <c>dvd</c>, <c>dsr</c>, <c>regional</c>, <c>scr</c>, <c>ts</c>, <c>tc</c>,
    /// <c>cam</c>, <c>wp</c>, <c>pdtv</c>, <c>sdtv</c>, <c>tvrip</c>. Null when the title named none.
    /// When the title names several, the rightmost occurrence in the text wins — a per-release fact no
    /// fixed rule table could express, which is why it is a property of the scan.
    /// </summary>
    public string? SourceGroup { get; init; }

    /// <summary>
    /// Gets the resolution the title literally stated, as a pixel height. Zero when it stated none.
    /// </summary>
    public int StatedResolution { get; init; }

    /// <summary>
    /// Gets a value indicating whether the title claims a lossless remux of its source.
    /// </summary>
    public bool IsRemux { get; init; }

    /// <summary>Gets the video codec token, or null.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Gets the audio codec token, or null.</summary>
    public string? AudioCodec { get; init; }

    /// <summary>Gets the release group, or null.</summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>Gets how many times the release was re-issued, and why.</summary>
    public QualityRevision Revision { get; init; } = QualityRevision.Initial;

    /// <summary>Gets the languages the title names. Empty means "not stated", never "none".</summary>
    public IReadOnlyList<Language> Languages { get; init; } = [];

    /// <summary>
    /// Gets the tags a kind's declared token tables added, keyed by the tag key each row names.
    /// </summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } = EmptyExtra;

    private static readonly IReadOnlyDictionary<string, string> EmptyExtra =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
