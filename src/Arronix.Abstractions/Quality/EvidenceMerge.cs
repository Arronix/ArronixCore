using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// The two rules for reconciling readings that disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Between sources, the stronger one wins.</b> A resolution a release title claims and a resolution a
/// container probe measured are not the same evidence, and the ordering on
/// <see cref="EvidenceSource"/> is what replaces a per-kind list of sources whose claims have to be ignored:
/// a capture whose title says one thing and whose stream measures another resolves in one place, with no
/// rule naming any kind.
/// </para>
/// <para>
/// <b>Within one source, the most specific claim wins, and among equally specific claims the lowest one
/// wins.</b> The common case is not two sources disagreeing but one source saying two things at once — an
/// explicit line count beside a marketing name — and a reading holds exactly one value with nowhere to put
/// a tie-break. Specificity is an ordering over <i>forms of statement</i>, which each axis states for
/// itself; this type takes it as a number so that the rule is written once.
/// </para>
/// <para>
/// The lowest-claim tie-break is not symmetric hedging. A missed claim leaves a release ranked low; a false
/// claim promotes junk past everything the user asked for, and only one of those is recoverable.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceMerge
{
    /// <summary>Keeps whichever of two readings is better attested.</summary>
    /// <typeparam name="TValue">The axis's value type.</typeparam>
    /// <param name="held">What is already read.</param>
    /// <param name="contribution">What is offered.</param>
    /// <returns>The reading that survives.</returns>
    /// <remarks>
    /// A contribution takes effect only when it turns absence into presence or arrives at a strictly
    /// stronger source. An equal source loses, deliberately: two claims of equal standing are not a reason
    /// to overwrite the one already read, and the within-source rule is where an equal-standing conflict is
    /// settled.
    /// </remarks>
    public static Evidence<TValue> Stronger<TValue>(Evidence<TValue> held, Evidence<TValue> contribution)
        where TValue : struct =>
        !contribution.IsKnown ? held
        : !held.IsKnown || contribution.Source > held.Source ? contribution
        : held;

    /// <summary>Keeps whichever of two set readings is better attested.</summary>
    /// <typeparam name="TValue">The axis's member type.</typeparam>
    /// <param name="held">What is already read.</param>
    /// <param name="contribution">What is offered.</param>
    /// <returns>The reading that survives.</returns>
    public static EvidenceSet<TValue> Stronger<TValue>(
        EvidenceSet<TValue> held,
        EvidenceSet<TValue> contribution)
        where TValue : struct, Enum =>
        !contribution.IsKnown ? held
        : !held.IsKnown || contribution.Source > held.Source ? contribution
        : held;

    /// <summary>Keeps whichever of two erased readings is better attested.</summary>
    /// <param name="held">What is already read.</param>
    /// <param name="contribution">What is offered.</param>
    /// <returns>The reading that survives.</returns>
    /// <remarks>
    /// The same rule as the typed overloads, applied where a reading has already lost its type: this is what
    /// the host applies to a per-kind refinement, so that a heuristic can complete a family's reading and
    /// can never overwrite a measurement.
    /// </remarks>
    public static AxisReading Stronger(AxisReading held, AxisReading contribution)
    {
        var offered = contribution is { IsKnown: true, Values.Count: > 0 };
        var present = held is { IsKnown: true, Values.Count: > 0 };

        return !offered ? held
            : !present || contribution.Source > held.Source ? contribution
            : held;
    }

    /// <summary>Settles several claims made at one source.</summary>
    /// <typeparam name="TValue">The axis's value type.</typeparam>
    /// <param name="source">The source every claim was read from.</param>
    /// <param name="claims">The claims.</param>
    /// <returns>The reading, or the absent reading when nothing was claimed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="claims"/> is <see langword="null"/>.</exception>
    public static Evidence<TValue> MostSpecific<TValue>(
        EvidenceSource source,
        params EvidenceClaim<TValue>[] claims)
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(claims);

        var settled = false;
        var value = default(TValue);
        var specificity = int.MaxValue;

        foreach (var claim in claims)
        {
            if (claim.Specificity > specificity)
            {
                continue;
            }

            if (claim.Specificity < specificity || Comparer<TValue>.Default.Compare(claim.Value, value) < 0)
            {
                value = claim.Value;
                specificity = claim.Specificity;
                settled = true;
            }
        }

        return settled ? Evidence<TValue>.From(value, source) : Evidence<TValue>.None;
    }
}

/// <summary>One claim about one axis, and how specifically it was stated.</summary>
/// <typeparam name="TValue">The axis's value type.</typeparam>
/// <param name="Value">The value claimed.</param>
/// <param name="Specificity">
/// How specific the form of the statement was, lower being more specific. An axis states its own scale: an
/// explicit line count is more specific than an explicit raster, which is more specific than a marketing
/// name, which is more specific than an inference from a container.
/// </param>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct EvidenceClaim<TValue>(TValue Value, int Specificity)
    where TValue : struct;
