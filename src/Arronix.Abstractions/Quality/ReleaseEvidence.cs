using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What the host's scanners and any probe found, before anything ranked it.</summary>
/// <remarks>
/// The typed members are the host-global scanning vocabulary, which is identical for every media kind and
/// therefore host-owned. <see cref="Tags"/> and <see cref="Guards"/> are the per-kind residue, and they
/// stay string-keyed because release-title parsing stays regular expressions. <b>That is the boundary:</b>
/// strings enter <see cref="IQualityType.Read"/> and typed axes come out. What changes is that they no
/// longer run all the way into a ranking table.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ReleaseEvidence
{
    /// <summary>Gets the release title as it arrived.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the normalized source token the shared scanner settled on.</summary>
    public string? SourceToken { get; init; }

    /// <summary>Gets the vertical resolution the release states, in lines, exactly as stated.</summary>
    /// <remarks>
    /// Un-bucketed. A scanner that folds an interlaced marker, an intermediate raster and an upscale token
    /// onto one number destroys the distinction before anything can reason about it; the interlace marker
    /// belongs on <see cref="ScanType"/> and the upscale on <see cref="FlawTokens"/>.
    /// </remarks>
    public int? StatedResolution { get; init; }

    /// <summary>Gets how the stated resolution was stated, for the within-source specificity rule.</summary>
    public ResolutionClaimForm StatedResolutionForm { get; init; }

    /// <summary>Gets the scan type the release states, when it states one.</summary>
    public ScanType? ScanType { get; init; }

    /// <summary>Gets the video codec token.</summary>
    public string? VideoCodecToken { get; init; }

    /// <summary>Gets the audio format token.</summary>
    public string? AudioToken { get; init; }

    /// <summary>Gets the dynamic-range formats the release states, in stated order.</summary>
    /// <remarks>
    /// A list rather than one token, because a release genuinely carries two at once: a proprietary
    /// dynamic-metadata layer over an open dynamic-metadata base is real and increasingly common, and the
    /// axis it feeds is set-valued for exactly that reason. A single slot would force the scan to pick a
    /// winner and would discard the other format before anything could read it.
    /// </remarks>
    public IReadOnlyList<string> DynamicRangeTokens { get; init; } = [];

    /// <summary>Gets whether the release states that it is a bitstream copy.</summary>
    public bool IsRemux { get; init; }

    /// <summary>Gets the stated re-issue number. One when the release states none.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets how many times the release states it corrects a mislabeled issue.</summary>
    public int RealCount { get; init; }

    /// <summary>Gets whether the release states that it is a repack of the same encode.</summary>
    public bool IsRepack { get; init; }

    /// <summary>Gets the release group.</summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>Gets the distributor token a stream capture names.</summary>
    public string? DistributorToken { get; init; }

    /// <summary>Gets the languages the release states, and how it stated them.</summary>
    public IReadOnlyList<LanguageClaim> Languages { get; init; } = [];

    /// <summary>Gets how the release is packaged, when a token or an extension says.</summary>
    public string? PackagingToken { get; init; }

    /// <summary>Gets the defect markers the release states.</summary>
    public IReadOnlySet<string> FlawTokens { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the frame rate the release states, when it states one.</summary>
    public double? StatedFrameRate { get; init; }

    /// <summary>Gets the file extension, leading dot included, when there is a file.</summary>
    /// <remarks>
    /// Load-bearing rather than a fallback: a bare line count inside a streaming container is a stream
    /// download and not a broadcast capture, and that inference is keyed on this member.
    /// </remarks>
    public string? Container { get; init; }

    /// <summary>Gets the per-kind guards that matched.</summary>
    public IReadOnlySet<string> Guards { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the per-kind tags the kind's own patterns captured.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets what a container or stream probe measured, when the file is on disk.</summary>
    public MediaProbe? Probe { get; init; }
}
