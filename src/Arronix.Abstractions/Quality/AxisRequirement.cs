using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What makes a candidate ineligible.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record AxisRequirement
{
    /// <summary>Gets the axis.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets whether the listed values are required or refused.</summary>
    public required RequirementMode Mode { get; init; }

    /// <summary>Gets the members the requirement names. Empty when it is a bound.</summary>
    public IReadOnlyList<AxisValue> Values { get; init; } = [];

    /// <summary>Gets the least acceptable richness, when the requirement is a bound.</summary>
    public AxisValue? AtLeast { get; init; }

    /// <summary>Gets the greatest acceptable richness, when the requirement is a bound.</summary>
    public AxisValue? AtMost { get; init; }

    /// <summary>
    /// Gets what an absent reading means. Defaults to admitting, so we never refuse what we did not
    /// inspect.
    /// </summary>
    public UnknownEvidence WhenUnknown { get; init; } = UnknownEvidence.Ignore;

    /// <summary>Gets the weakest reading this requirement will act on.</summary>
    /// <remarks>
    /// <para>
    /// A refusal is irreversible from the user's side — the release is simply never offered — so refusing
    /// on a <i>guess</i> is the worst thing this model can do. A per-kind heuristic contributes at
    /// <see cref="EvidenceSource.Assumed"/>, and defaulting this to <see cref="EvidenceSource.ReleaseTitle"/>
    /// means such a heuristic can inform ranking and rendering but cannot refuse. An explicit token in the
    /// title still can.
    /// </para>
    /// <para>
    /// A reading weaker than this is treated as <i>absent</i> for this requirement, so
    /// <see cref="WhenUnknown"/> then decides what it means. With the default of
    /// <see cref="UnknownEvidence.Ignore"/> the requirement simply does not fire.
    /// </para>
    /// </remarks>
    public EvidenceSource MinimumSource { get; init; } = EvidenceSource.ReleaseTitle;

    /// <summary>Gets the user's own words for why. Generated from the requirement when absent.</summary>
    public string? Reason { get; init; }
}

/// <summary>Whether a requirement names what is acceptable or what is not.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum RequirementMode
{
    /// <summary>The candidate must match.</summary>
    Require = 0,

    /// <summary>The candidate must not match.</summary>
    Refuse = 1,
}
