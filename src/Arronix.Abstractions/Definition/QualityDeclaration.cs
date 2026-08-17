using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// Quality evaluation beyond the ladder itself, which lives on the shape.
/// </summary>
/// <remarks>
/// Deliberately small: on every surveyed kind, upgrade and cutoff checks are pure functions of the
/// declared ladder once weight and revision are first-class, so what remains per kind is only the
/// default rows and two modes.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityDeclaration
{
    /// <summary>
    /// Gets the declaration of a kind whose evaluation is purely ladder-derived.
    /// </summary>
    public static QualityDeclaration LadderDerived { get; } = new();

    /// <summary>
    /// Gets the default-resolution rows applied before rung lookup, in declared order.
    /// </summary>
    public IReadOnlyList<TierDefault> Defaults { get; init; } = [];

    /// <summary>
    /// Gets where evidence between rungs lands.
    /// </summary>
    public RungFallback Fallback { get; init; } = RungFallback.RoundUp;

    /// <summary>
    /// Gets how tiers of different format families relate.
    /// </summary>
    public CrossFamilyRule CrossFamily { get; init; } = CrossFamilyRule.NeverCompare;
}
