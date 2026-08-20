
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One condition of a <see cref="TagPredicate"/>.
/// </summary>
/// <remarks>
/// The subject is a path into the evaluation context: a tag field (<c>"tags.SourceGroup"</c>), the
/// release categories (<c>"categories"</c>), a named guard (<c>"guard:br-disk"</c>) or a pattern capture
/// (<c>"capture:res"</c>). The vocabulary of reachable subjects is defined by the engine evaluating the
/// predicate and validated at load; an unresolvable subject refuses the definition rather than
/// evaluating to anything.
/// </remarks>
public sealed record PredicateAtom
{
    /// <summary>
    /// Gets the path naming what the atom examines.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the comparison applied to the subject.
    /// </summary>
    public required PredicateOp Op { get; init; }

    /// <summary>
    /// Gets the values the subject is compared against. Empty for operators that take none.
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the atom holds when the comparison fails rather than when it
    /// succeeds.
    /// </summary>
    public bool Negated { get; init; }
}
