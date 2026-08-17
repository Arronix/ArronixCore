using System.Linq;
using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// One format family's runtime quality model: the axes it declares, how it reads evidence onto them, how it
/// spells a point and how big a file at that point should be.
/// </summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// <para>
/// Built by the host from the family's authoring seam; never implemented by an extension. Every member is
/// derived from the facts type or carried verbatim from the builder, so there is no second source of truth
/// and nothing here can disagree with the facts type.
/// </para>
/// <para>
/// <b>There is no comparison on it, and that is the point.</b> This type knows everything about quality and
/// is structurally incapable of ranking it, because ranking has no answer without a policy. A family states
/// what its releases are; a user states which of those they want.
/// </para>
/// <para>
/// <b>A size model is declared over the typed facts and asked for over a point</b>, so the host rebuilds the
/// facts from the point to call it. That works because the projection is invertible — a reading carries an
/// enumeration member's own numeric value — and it is what keeps a family from having to write its size
/// model twice, once for each shape, with the two free to drift.
/// </para>
/// </remarks>
internal sealed class HostQualityType<TFacts> : IQualityType
    where TFacts : IQualityFacts
{
    private readonly Func<ReleaseEvidence, TFacts> read;
    private readonly QualityFactsProjection<TFacts> projection;
    private readonly QualityRefinementPipeline<TFacts> pipeline;
    private readonly QualityLabelRenderer renderer;
    private readonly Func<TFacts, TimeSpan, SizeExpectation>? sizes;
    private readonly Action<IQualityPolicyBuilder> policy;

    private QualityPolicy? shipped;

    /// <summary>Initializes a new instance of the <see cref="HostQualityType{TFacts}"/> class.</summary>
    /// <param name="family">The format family.</param>
    /// <param name="read">The family's reading of evidence.</param>
    /// <param name="axes">The declared axes, in declaration order.</param>
    /// <param name="builder">What the family declared beyond its axes.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="family"/> names no family.</exception>
    internal HostQualityType(
        FormatFamilyId family,
        Func<ReleaseEvidence, TFacts> read,
        IReadOnlyList<DeclaredAxis> axes,
        QualityTypeBuilder<TFacts> builder)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(builder);

        if (!family.IsNamed)
        {
            throw new ArgumentException(
                "A quality type belongs to a format family, and the family identity is the only thing that "
                + "makes two points comparable, so an unnamed one would make every point comparable with "
                + "every other.",
                nameof(family));
        }

        this.read = read;
        Family = family;
        Declared = axes;
        Axes = [.. axes.Select(static axis => axis.Axis)];
        Name = builder.Name ?? family.Value;
        projection = new QualityFactsProjection<TFacts>(family, axes);
        pipeline = new QualityRefinementPipeline<TFacts>(projection);
        renderer = new QualityLabelRenderer(family, axes, builder.Labels, builder.Suffixes);
        sizes = builder.SizeModel;
        policy = builder.Policy ?? (_ => { });
    }

    /// <inheritdoc />
    public FormatFamilyId Family { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Type FactsType => typeof(TFacts);

    /// <inheritdoc />
    public IReadOnlyList<QualityAxis> Axes { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Compiled on first use rather than in the constructor, because compiling a policy reads
    /// <see cref="Axes"/> off this instance and a constructor that hands itself out half-built is a worse
    /// problem than a lazy field.
    /// </remarks>
    public QualityPolicy DefaultPolicy => shipped ??= QualityPolicy.For(this, policy);

    /// <summary>Gets the declared axes paired with the properties they were read from.</summary>
    internal IReadOnlyList<DeclaredAxis> Declared { get; }

    /// <summary>Adds one media kind's contribution to this family's axes.</summary>
    /// <param name="refine">The contribution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="refine"/> is <see langword="null"/>.</exception>
    internal void RefinedBy(Func<TFacts, ReleaseEvidence, TFacts> refine) => pipeline.Add(refine);

    /// <inheritdoc />
    public QualityPoint Read(ReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return pipeline.Apply(read(evidence), evidence);
    }

    /// <inheritdoc />
    public QualityPoint Project(object facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return facts is TFacts typed
            ? projection.Project(typed)
            : throw new ArgumentException(
                $"'{Family}' reads '{typeof(TFacts).Name}' and was handed '{facts.GetType().Name}'.",
                nameof(facts));
    }

    /// <inheritdoc />
    public string Label(QualityPoint point, QualityLabelDetail detail) => renderer.Render(point, detail);

    /// <inheritdoc />
    public bool TryParseLabel(string label, out QualityPoint point) => renderer.TryRead(label, out point);

    /// <inheritdoc />
    public SizeExpectation ExpectedSize(QualityPoint point, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(point);

        return sizes is null ? SizeExpectation.NotAssessable : sizes(projection.Materialize(point), duration);
    }
}
