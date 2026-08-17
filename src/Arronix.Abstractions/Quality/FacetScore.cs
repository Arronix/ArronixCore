using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What one facet is worth.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FacetScore
{
    /// <summary>Gets the axis. Must not also appear in <see cref="QualityPolicy.Precedence"/>.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>
    /// Gets what each member is worth, in [-100, 100]. An unlisted member is worth zero.
    /// </summary>
    /// <remarks>
    /// Looked up by the point a value names rather than by its spelling, so a policy a user composed and a
    /// family's declared members do not have to agree on a word.
    /// </remarks>
    public required IReadOnlyDictionary<AxisValue, int> Points { get; init; }

    /// <summary>Gets what an absent reading is worth. Zero, and not configurable.</summary>
    /// <remarks>
    /// Deliberately fixed. A configurable unknown-score is the one thing that would let a facet punish
    /// releases for silence, which is how a bonus becomes a refusal.
    /// </remarks>
    public static int WhenUnknown => 0;

    /// <summary>Gets what one value is worth on this facet.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The points, or zero when the value is not listed.</returns>
    public int PointsFor(AxisValue value)
    {
        if (!value.IsKnown)
        {
            return WhenUnknown;
        }

        foreach (var entry in Points)
        {
            if (entry.Key.Names(value))
            {
                return entry.Value;
            }
        }

        return 0;
    }
}
