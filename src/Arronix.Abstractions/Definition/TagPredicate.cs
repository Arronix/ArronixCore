using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// A conjunction of predicate atoms: the whole predicate holds when every atom holds.
/// </summary>
/// <param name="All">The atoms, all of which must hold.</param>
/// <remarks>
/// <para>
/// Deliberately closed and small: no disjunction (write two rows), no negation of conjunctions (atoms
/// carry their own negation), no arithmetic beyond comparison, no recursion and no user functions.
/// Anything the vocabulary cannot express is a host strategy or a budgeted escape, never a grammar
/// extension — this is the single rule that keeps the declarative surface from becoming a worse
/// programming language.
/// </para>
/// <para>
/// An empty conjunction holds vacuously, which is what makes an unconditional row expressible.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record TagPredicate(IReadOnlyList<PredicateAtom> All)
{
    /// <summary>
    /// Gets the predicate that always holds.
    /// </summary>
    public static TagPredicate Always { get; } = new([]);
}
