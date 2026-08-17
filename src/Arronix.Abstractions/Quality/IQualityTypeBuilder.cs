using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// Declares what the axis attributes cannot: the family's identity, its labels, its size model and its
/// stated default policy.
/// </summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// The same dividing rule as the media-type builder: an attribute states a fact about one axis in
/// isolation; everything here relates two or more axes, or is about the family as a whole. A label rule
/// reads two axes at once; a size model reads four; a default policy orders all of them.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityTypeBuilder<TFacts>
    where TFacts : IQualityFacts
{
    /// <summary>Names the family.</summary>
    /// <param name="name">The display name.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityTypeBuilder<TFacts> Named(string name);

    /// <summary>Declares one rendering rule. Declared order is the rule order; the first match wins.</summary>
    /// <param name="when">What must hold.</param>
    /// <param name="label">The community's word for it.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// An <see cref="Expression{TDelegate}"/> rather than a delegate: the rule is authored against the
    /// typed facts and evaluated against an erased <see cref="QualityPoint"/>, and a compiled delegate
    /// cannot be rewritten onto the point.
    /// </remarks>
    IQualityTypeBuilder<TFacts> Label(Expression<Func<TFacts, bool>> when, string label);

    /// <summary>Declares which axis the standard label suffixes with, and how.</summary>
    /// <typeparam name="TValue">The axis's value type.</typeparam>
    /// <param name="axis">The axis, as a property reference.</param>
    /// <param name="format">How the value is spelled.</param>
    /// <param name="appliesWhen">Which labels take the suffix.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityTypeBuilder<TFacts> Suffix<TValue>(
        Expression<Func<TFacts, Evidence<TValue>>> axis,
        string format,
        Func<string, bool> appliesWhen)
        where TValue : struct;

    /// <summary>Declares the family's expected-size model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityTypeBuilder<TFacts> Sizes(Func<TFacts, TimeSpan, SizeExpectation> model);

    /// <summary>Declares the policy shipped as this family's stated opinion.</summary>
    /// <param name="configure">The declaration.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The one place in the whole model where a preference is written by anybody but the user, and it is a
    /// <i>default</i>: a profile that replaces it owes nothing to it.
    /// </remarks>
    IQualityTypeBuilder<TFacts> DefaultPolicy(Action<IQualityPolicyBuilder> configure);
}
