using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>When a held file is good enough to stop looking.</summary>
/// <remarks>
/// <para>
/// Conjunctive over axes, which is what a person means by "good enough": <i>at least this many lines and
/// at most one re-encode</i>. Naming one rung of a ladder instead forces the user to accept a whole
/// cross-product cell, and gives them no way to say "this resolution from anywhere is fine".
/// </para>
/// <para>
/// A predicate with no floors states nothing to satisfy and is therefore <b>never satisfied</b>, which is
/// what <see cref="Never"/> is. That is the right default: a policy that declares no cutoff should keep
/// looking, not stop immediately.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CutoffPredicate
{
    /// <summary>Gets the predicate that is never satisfied, so upgrades never stop.</summary>
    public static CutoffPredicate Never { get; } = new();

    /// <summary>Gets the per-axis floors, all of which must hold.</summary>
    public IReadOnlyList<AxisFloor> Floors { get; init; } = [];

    /// <summary>Gets whether upgrades are searched for at all below the cutoff.</summary>
    /// <remarks>
    /// Read by whatever schedules a search, not by <see cref="QualityPolicy.Decide"/>: it says whether to
    /// go looking, and a decision the user asked for explicitly still gets an honest answer.
    /// </remarks>
    public bool UpgradesEnabled { get; init; } = true;

    /// <summary>
    /// Gets the declared axes, so a floor can be read in richness rather than in raw magnitude.
    /// </summary>
    /// <remarks>
    /// Supplied by <see cref="QualityPolicy.For"/> when it compiles the policy. A predicate built by hand
    /// carries none, and its floors then read as though every axis were ascending.
    /// </remarks>
    internal IReadOnlyDictionary<QualityAxisId, QualityAxis> Declared { get; init; } =
        new Dictionary<QualityAxisId, QualityAxis>();

    /// <summary>Tests a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when there is at least one floor and every floor holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is null.</exception>
    public bool IsSatisfiedBy(QualityPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (Floors.Count == 0)
        {
            return false;
        }

        foreach (var floor in Floors)
        {
            var axis = AxisOf(floor.Axis);
            var reading = point[floor.Axis];

            if (!TryRichness(axis, reading, floor.WhenUnknown, out var richness, out var vacuous))
            {
                return false;
            }

            if (vacuous)
            {
                continue;
            }

            if (richness < axis.RichnessOf(floor.AtLeast))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the richness a reading contributes, applying the absent-reading mode.
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <param name="reading">The reading.</param>
    /// <param name="whenUnknown">What an absent reading means.</param>
    /// <param name="richness">Receives the richness.</param>
    /// <param name="vacuous">Receives whether the test does not apply at all.</param>
    /// <returns><see langword="false"/> when the absent reading refuses outright.</returns>
    internal static bool TryRichness(
        QualityAxis axis,
        AxisReading reading,
        UnknownEvidence whenUnknown,
        out double richness,
        out bool vacuous)
    {
        vacuous = false;
        richness = double.NegativeInfinity;

        if (reading.IsKnown && reading.Values.Count > 0)
        {
            foreach (var value in reading.Values)
            {
                var candidate = axis.RichnessOf(value);

                if (candidate > richness)
                {
                    richness = candidate;
                }
            }

            return true;
        }

        switch (whenUnknown.Mode)
        {
            case UnknownEvidenceMode.Ignore:
                vacuous = true;

                return true;

            case UnknownEvidenceMode.Refuse:
                return false;

            case UnknownEvidenceMode.Assume:
                richness = axis.RichnessOf(whenUnknown.Assumption);

                return true;

            default:
                richness = double.NegativeInfinity;

                return true;
        }
    }

    private QualityAxis AxisOf(QualityAxisId id) =>
        Declared.TryGetValue(id, out var axis)
            ? axis
            : new QualityAxis { Id = id, Name = id.ToString(), Form = AxisForm.Scalar, GreaterIsRicher = true };
}
