
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Evaluates a <see cref="TagPredicate"/> — the definition surface's closed predicate vocabulary —
/// against a subject-lookup context.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary is a conjunction of atoms; an empty conjunction holds vacuously, which is what makes
/// an unconditional row expressible (<c>Definition/TagPredicate.cs</c>). Disjunction is written as two
/// rows; the row executor supplies the first-passing-row semantics.
/// </para>
/// <para>
/// The reachable subjects are defined by the engine evaluating the predicate. For the naming engine the
/// subjects are <c>fields.&lt;id&gt;</c>, <c>options.&lt;id&gt;</c>, <c>file.&lt;facet&gt;</c>,
/// <c>units.count</c> and <c>parts.count</c>; the context is a lookup the caller assembles.
/// <see cref="PredicateOp.GuardMatches"/> is a parse-engine subject and is unresolvable here — an atom
/// using it fails closed rather than passing silently.
/// </para>
/// </remarks>
internal static class DeclarationPredicate
{
    /// <summary>
    /// Evaluates a predicate.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="lookup">
    /// Resolves a subject path to its values. Null means the subject has no value; an empty list means
    /// present with nothing to compare.
    /// </param>
    /// <returns>Whether every atom holds.</returns>
    public static bool Holds(TagPredicate predicate, Func<string, IReadOnlyList<string>?> lookup)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(lookup);

        foreach (var atom in predicate.All)
        {
            var values = lookup(atom.Subject);
            var holds = Evaluate(atom, values);

            if (atom.Negated)
            {
                holds = !holds;
            }

            if (!holds)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Evaluate(PredicateAtom atom, IReadOnlyList<string>? subject)
    {
        if (subject is null)
        {
            // An absent subject satisfies no comparison; Present is the operator that asks.
            return false;
        }

        return atom.Op switch
        {
            PredicateOp.Equals => atom.Values.Count == 1
                && subject.Any(value => string.Equals(value, atom.Values[0], StringComparison.OrdinalIgnoreCase)),
            PredicateOp.In => subject.Any(value =>
                atom.Values.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))),
            PredicateOp.Present => subject.Any(value => value.Length > 0),
            PredicateOp.GreaterOrEqual => CompareNumeric(subject, atom.Values, (left, right) => left >= right),
            PredicateOp.LessOrEqual => CompareNumeric(subject, atom.Values, (left, right) => left <= right),
            PredicateOp.Contains => atom.Values.Count == 1
                && subject.Any(value => value.Contains(atom.Values[0], StringComparison.OrdinalIgnoreCase)),

            // Guard subjects belong to the parse engine's context; here they fail closed.
            PredicateOp.GuardMatches => false,
            _ => false,
        };
    }

    private static bool CompareNumeric(
        IReadOnlyList<string> subject,
        IReadOnlyList<string> values,
        Func<double, double, bool> comparison)
    {
        if (values.Count != 1
            || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var bound))
        {
            return false;
        }

        return subject.Any(value =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && comparison(parsed, bound));
    }
}
