using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>One axis, as the host and client see it.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityAxis
{
    /// <summary>Gets the axis's identifier, derived from the property name.</summary>
    public required QualityAxisId Id { get; init; }

    /// <summary>Gets the display name, from <c>[Display]</c> or the property name split on case.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the description, from <c>[Display]</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the axis's form.</summary>
    public required AxisForm Form { get; init; }

    /// <summary>Gets whether a greater value means more of what the axis measures.</summary>
    public required bool GreaterIsRicher { get; init; }

    /// <summary>Gets whether a reading may hold several members at once.</summary>
    public bool Multivalued { get; init; }

    /// <summary>Gets the unit a quantity is expressed in. Null for a closed axis.</summary>
    public string? Unit { get; init; }

    /// <summary>Gets the members of a closed axis, in declared order. Empty for a quantity.</summary>
    /// <remarks>
    /// Declared order is the family's <i>claim</i> about fidelity, not the user's preference. A policy
    /// may re-rank it, which is what makes a contested pair a setting instead of an argument.
    /// </remarks>
    public IReadOnlyList<AxisValue> Members { get; init; } = [];

    /// <summary>
    /// Gets the richness of a value on this axis: the number every bound, ceiling, floor and comparison
    /// in a policy speaks in.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The richness, or <see cref="double.NegativeInfinity"/> when the value is absent.</returns>
    /// <remarks>
    /// Richness, not magnitude, is what a policy orders on, so that a bound reads the same direction on
    /// every axis: on a <see cref="AxisOrdering.Descending"/> axis "at least this rich" is "at most this
    /// many", and a person configuring one never has to remember which axes count downwards.
    /// </remarks>
    internal double RichnessOf(AxisValue value) =>
        !value.IsKnown ? double.NegativeInfinity
        : GreaterIsRicher ? value.Ordinate
        : -value.Ordinate;
}
