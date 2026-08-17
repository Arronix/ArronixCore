using System.Linq.Expressions;
using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Collects what the axis attributes cannot state: the family's name, its rendering rules, its size model
/// and the policy it ships as its own opinion.
/// </summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// <para>
/// Everything here relates two or more axes, or is about the family as a whole. That is the same dividing
/// rule the typed media builder keeps, and it is why a rendering rule is not an attribute: a rule reads two
/// axes at once, a size model reads four, and a policy orders all of them.
/// </para>
/// <para>
/// A rendering rule is rewritten onto the erased point <b>here</b>, while the family is being registered,
/// rather than when a label is first asked for. A rule outside the rendering grammar is then a failure at
/// registration with the offending expression in the message, which is a failure somebody can act on, and
/// not a failure the first time a notification is rendered.
/// </para>
/// </remarks>
internal sealed class QualityTypeBuilder<TFacts> : IQualityTypeBuilder<TFacts>
    where TFacts : IQualityFacts
{
    private readonly IReadOnlyDictionary<string, DeclaredAxis> axes;
    private readonly List<CompiledLabel> labels = [];
    private readonly List<DeclaredSuffix> suffixes = [];

    /// <summary>Initializes a new instance of the <see cref="QualityTypeBuilder{TFacts}"/> class.</summary>
    /// <param name="axes">The declared axes, keyed by the property that declares each.</param>
    /// <exception cref="ArgumentNullException"><paramref name="axes"/> is <see langword="null"/>.</exception>
    internal QualityTypeBuilder(IReadOnlyDictionary<string, DeclaredAxis> axes)
    {
        ArgumentNullException.ThrowIfNull(axes);

        this.axes = axes;
    }

    /// <summary>Gets the family's display name, when it named itself.</summary>
    internal string? Name { get; private set; }

    /// <summary>Gets the rendering rules, in declared order.</summary>
    internal IReadOnlyList<CompiledLabel> Labels => labels;

    /// <summary>Gets the declared suffixes, the first of which is the standard one.</summary>
    internal IReadOnlyList<DeclaredSuffix> Suffixes => suffixes;

    /// <summary>Gets the family's size model, when it declared one.</summary>
    internal Func<TFacts, TimeSpan, SizeExpectation>? SizeModel { get; private set; }

    /// <summary>Gets the family's stated opinion, when it declared one.</summary>
    internal Action<IQualityPolicyBuilder>? Policy { get; private set; }

    /// <inheritdoc />
    public IQualityTypeBuilder<TFacts> Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;

        return this;
    }

    /// <inheritdoc />
    /// <remarks>
    /// An empty word is allowed and is not the same as no rule. A family whose evidence names no source but
    /// does name a resolution still has something true to render, and the rule that says so renders no word
    /// of its own and lets the suffix carry the whole label.
    /// </remarks>
    public IQualityTypeBuilder<TFacts> Label(Expression<Func<TFacts, bool>> when, string label)
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(label);

        labels.Add(new CompiledLabel(QualityLabelPredicate.Compile(when, axes), label));

        return this;
    }

    /// <inheritdoc />
    public IQualityTypeBuilder<TFacts> Suffix<TValue>(
        Expression<Func<TFacts, Evidence<TValue>>> axis,
        string format,
        Func<string, bool> appliesWhen)
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(appliesWhen);

        suffixes.Add(new DeclaredSuffix(QualityLabelPredicate.AxisOf(axis, axes), format, appliesWhen));

        return this;
    }

    /// <inheritdoc />
    public IQualityTypeBuilder<TFacts> Sizes(Func<TFacts, TimeSpan, SizeExpectation> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        SizeModel = model;

        return this;
    }

    /// <inheritdoc />
    public IQualityTypeBuilder<TFacts> DefaultPolicy(Action<IQualityPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Policy = configure;

        return this;
    }
}
