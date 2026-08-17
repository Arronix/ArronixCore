using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>One axis's place in a preference.</summary>
/// <remarks>
/// Every entry induces a <b>total preorder</b> on points, which is what makes the lexicographic core
/// transitive and acyclic. Each control below is monotone or order-reversing into a totally ordered set:
/// <see cref="Ceiling"/> and <see cref="Floor"/> are clamp maps, <see cref="Ranking"/> is a monotone
/// quotient onto ordered groups, <see cref="PreferRicher"/> composes with order reversal, and
/// <see cref="WhenUnknown"/> maps an absent reading to a fixed element.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record AxisPreference
{
    /// <summary>Gets the axis.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets whether richer is preferred. Inverting is legitimate for a small-files profile.</summary>
    public bool PreferRicher { get; init; } = true;

    /// <summary>
    /// Gets the value above which the axis stops mattering. A candidate above it compares equal to it.
    /// </summary>
    /// <remarks>
    /// Capping, not refusing. "I do not need more than this" and "my player cannot open more than this"
    /// are different intents; the second is an <see cref="AxisRequirement"/>. A ceiling is stated in
    /// richness, so on a descending axis a ceiling of one re-encode makes untouched and once-encoded
    /// compare equal while a second re-encode still loses.
    /// </remarks>
    public AxisValue? Ceiling { get; init; }

    /// <summary>Gets the value below which the axis stops mattering. Stated in richness, like the ceiling.</summary>
    public AxisValue? Floor { get; init; }

    /// <summary>
    /// Gets a replacement ranking for a closed axis: groups worst first, members inside one group tied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One mechanism for two jobs. Re-ordering a contested pair is moving a chip; declaring two members
    /// equivalent is dropping two chips into one row. Empty means the family's declared order stands.
    /// </para>
    /// <para>
    /// A ranking <b>replaces</b> the declared order, so it must name every member of the axis exactly
    /// once — a partial ranking is refused when the policy compiles rather than silently collapsing every
    /// unnamed member into one tie. Group order is richness order directly: the axis's own polarity has
    /// already been spent by the act of dragging the members into the order the user wants.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<AxisValue>> Ranking { get; init; } = [];

    /// <summary>Gets what an absent reading means on this axis.</summary>
    /// <remarks>
    /// <see cref="PreferenceUnknown"/>, not <see cref="UnknownEvidence"/>. A preference may only place an
    /// absent reading somewhere in the order; it may not skip the axis, which breaks transitivity, and it
    /// may not refuse, which is a requirement wearing a preference's clothes. The restriction is enforced
    /// by the type rather than by an analyzer, because here it is cheap to make structural.
    /// </remarks>
    public PreferenceUnknown WhenUnknown { get; init; } = PreferenceUnknown.Lowest;
}
