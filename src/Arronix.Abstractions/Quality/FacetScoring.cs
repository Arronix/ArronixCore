using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>A bounded additive score over axes the precedence list does not order.</summary>
/// <remarks>
/// <para>
/// The legitimate successor to token scoring. Two properties make it safe where a global weight was not:
/// it is consulted <b>only</b> on a core tie, so it can never overturn an ordered judgment; and it is
/// <b>bounded</b>, so it cannot be inflated into a shadow precedence list.
/// </para>
/// <para>
/// Scoring is not refusing. A facet may carry negative points — mildly disliking a format is a real
/// preference — but no number of negative points makes a candidate ineligible. That is
/// <see cref="AxisRequirement"/>, and keeping the two apart is what deletes the magic rejection value.
/// </para>
/// <para>
/// Acyclicity does not come from the bound: it comes from the score being a <i>function of one point</i>,
/// so the composite of the core preorder and this one is a lexicographic product of two total preorders.
/// The bound is there so the policy stays finite to render and cannot grow into a second, competing model.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FacetScoring
{
    /// <summary>Gets the scoring that never separates anything.</summary>
    public static FacetScoring None { get; } = new();

    /// <summary>Gets the greatest magnitude one facet may contribute.</summary>
    public static int PointsPerFacet => 100;

    /// <summary>Gets the greatest number of facets a policy may declare.</summary>
    public static int MaximumFacets => 10;

    /// <summary>Gets the declared facets.</summary>
    public IReadOnlyList<FacetScore> Scores { get; init; } = [];

    /// <summary>Scores one point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>
    /// The score, always within plus or minus <see cref="PointsPerFacet"/> times
    /// <see cref="MaximumFacets"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is null.</exception>
    /// <remarks>
    /// A set-valued facet takes its <b>greatest</b> member, never the sum: a release is not better for
    /// stating its dynamic range twice, and summing would make a release carrying two flavors of one thing
    /// outrank everything for a reason nobody intended.
    /// </remarks>
    public int Of(QualityPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var total = 0;

        foreach (var score in Scores)
        {
            var reading = point[score.Axis];

            if (!reading.IsKnown || reading.Values.Count == 0)
            {
                total += FacetScore.WhenUnknown;

                continue;
            }

            var best = int.MinValue;

            foreach (var value in reading.Values)
            {
                var points = score.PointsFor(value);

                if (points > best)
                {
                    best = points;
                }
            }

            total += best == int.MinValue ? FacetScore.WhenUnknown : best;
        }

        return total;
    }
}
