using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Runs a family's reading, then each media kind's contribution to it, and enforces the one rule that makes
/// the second safe.
/// </summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// <para>
/// The family reads what every kind's releases say; a kind refines that with what only <i>its</i> releases
/// say. Both are needed and they are not peers: a family reads typed evidence, and a kind's contribution is
/// a dialect heuristic over naming conventions its own community wrote. So the host enforces the asymmetry
/// rather than trusting it — <b>a refinement may turn absence into presence or raise a reading's source,
/// and nothing else.</b> A contribution that arrives at the same source or weaker is discarded.
/// </para>
/// <para>
/// The enforcement is per axis and after every refinement, not once at the end, so a second refinement sees
/// what the first was actually allowed to contribute rather than what it asked to. That costs a round trip
/// through the point for each refinement, which is free because the projection is invertible and there is
/// normally one refinement per kind.
/// </para>
/// <para>
/// What this buys downstream is the reason it exists: a heuristic contributes at
/// <see cref="EvidenceSource.Assumed"/>, so it can inform ranking and rendering while
/// <see cref="AxisRequirement.MinimumSource"/> keeps it from refusing anything. A guess can complete a
/// picture; it cannot make a release disappear.
/// </para>
/// </remarks>
internal sealed class QualityRefinementPipeline<TFacts>
    where TFacts : IQualityFacts
{
    private readonly QualityFactsProjection<TFacts> projection;
    private readonly List<Func<TFacts, ReleaseEvidence, TFacts>> refinements = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="QualityRefinementPipeline{TFacts}"/> class.
    /// </summary>
    /// <param name="projection">The family's projection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    internal QualityRefinementPipeline(QualityFactsProjection<TFacts> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        this.projection = projection;
    }

    /// <summary>Gets how many kinds contribute to this family.</summary>
    internal int Count => refinements.Count;

    /// <summary>Adds one media kind's contribution.</summary>
    /// <param name="refine">The contribution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="refine"/> is <see langword="null"/>.</exception>
    internal void Add(Func<TFacts, ReleaseEvidence, TFacts> refine)
    {
        ArgumentNullException.ThrowIfNull(refine);

        refinements.Add(refine);
    }

    /// <summary>Applies every contribution to what the family read.</summary>
    /// <param name="read">What the family read.</param>
    /// <param name="evidence">The evidence the family saw.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> or <paramref name="evidence"/> is null.</exception>
    internal QualityPoint Apply(TFacts read, ReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(evidence);

        var current = read;
        var point = projection.Project(current);

        foreach (var refine in refinements)
        {
            point = Guard(point, projection.Project(refine(current, evidence)));
            current = projection.Materialize(point);
        }

        return point;
    }

    private static QualityPoint Guard(QualityPoint read, QualityPoint refined)
    {
        var readings = new AxisReading[read.Readings.Count];

        for (var index = 0; index < readings.Length; index++)
        {
            var held = read.Readings[index];

            readings[index] = EvidenceMerge.Stronger(held, refined[held.Axis]);
        }

        return read with { Readings = readings };
    }
}
