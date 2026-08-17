// Consumes the experimental definition contracts (ARX0019).
#pragma warning disable ARX0019

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Evaluates the closed predicate vocabulary over one release's context.
/// </summary>
/// <remarks>
/// A predicate is a conjunction of atoms and nothing else: no disjunction (a kind writes two rows), no
/// negation of conjunctions (atoms carry their own), no arithmetic beyond comparison. An empty
/// conjunction holds vacuously, which is what makes an unconditional row expressible. Anything the
/// vocabulary cannot say is a host strategy or a budgeted escape, never a grammar extension.
/// </remarks>
internal static class TagPredicateEvaluator
{
    /// <summary>Evaluates a predicate.</summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="context">The release's context.</param>
    /// <returns>Whether every atom holds.</returns>
    internal static bool Holds(TagPredicate predicate, ParsePredicateContext context)
    {
        foreach (var atom in predicate.All)
        {
            if (!Holds(atom, context))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates one atom's spelling at load: subject reachability, operator arity, and that guard
    /// references resolve. Returns a defect description, or null when the atom is well-formed.
    /// </summary>
    /// <param name="atom">The atom.</param>
    /// <param name="guards">The declared guard set.</param>
    /// <returns>The defect, or null.</returns>
    internal static string? Validate(PredicateAtom atom, CompiledGuardSet guards)
    {
        if (!ParsePredicateContext.IsWellFormedSubject(atom.Subject))
        {
            return $"subject '{atom.Subject}' is outside the reachable vocabulary "
                + "(tags.*, capture:*, guard:*, categories)";
        }

        if (ParsePredicateContext.IsGuardSubject(atom.Subject))
        {
            if (atom.Op != PredicateOp.GuardMatches)
            {
                return $"subject '{atom.Subject}' requires the GuardMatches operator";
            }

            var guardId = atom.Subject["guard:".Length..];

            if (!guards.Declares(guardId))
            {
                return $"guard '{guardId}' is not declared";
            }
        }
        else if (atom.Op == PredicateOp.GuardMatches)
        {
            return $"GuardMatches requires a guard: subject, not '{atom.Subject}'";
        }

        var arity = atom.Op switch
        {
            PredicateOp.Present or PredicateOp.GuardMatches => 0,
            PredicateOp.In => -1, // one or more
            _ => 1,
        };

        if (arity == 0 && atom.Values.Count != 0)
        {
            return $"operator {atom.Op} takes no values";
        }

        if (arity == 1 && atom.Values.Count != 1)
        {
            return $"operator {atom.Op} takes exactly one value";
        }

        if (arity == -1 && atom.Values.Count == 0)
        {
            return $"operator {atom.Op} takes at least one value";
        }

        return null;
    }

    private static bool Holds(PredicateAtom atom, ParsePredicateContext context)
    {
        var result = Evaluate(atom, context);
        return atom.Negated ? !result : result;
    }

    private static bool Evaluate(PredicateAtom atom, ParsePredicateContext context)
    {
        if (atom.Op == PredicateOp.GuardMatches)
        {
            return context.GuardMatches(atom.Subject);
        }

        if (ParsePredicateContext.IsCategoriesSubject(atom.Subject))
        {
            return EvaluateOverCategories(atom, context.Categories);
        }

        var present = context.TryResolve(atom.Subject, out var value);

        return atom.Op switch
        {
            PredicateOp.Present => present,
            PredicateOp.Equals => present
                && string.Equals(value, atom.Values[0], StringComparison.OrdinalIgnoreCase),
            PredicateOp.In => present
                && atom.Values.Any(v => string.Equals(value, v, StringComparison.OrdinalIgnoreCase)),
            PredicateOp.Contains => present
                && value!.Contains(atom.Values[0], StringComparison.OrdinalIgnoreCase),
            PredicateOp.GreaterOrEqual => present
                && TryCompareNumeric(value!, atom.Values[0], out var atLeast) && atLeast >= 0,
            PredicateOp.LessOrEqual => present
                && TryCompareNumeric(value!, atom.Values[0], out var atMost) && atMost <= 0,
            _ => false,
        };
    }

    private static bool EvaluateOverCategories(PredicateAtom atom, IReadOnlyList<string> categories)
    {
        return atom.Op switch
        {
            PredicateOp.Present => categories.Count > 0,
            PredicateOp.Equals => categories.Any(
                c => string.Equals(c, atom.Values[0], StringComparison.OrdinalIgnoreCase)),
            PredicateOp.In => categories.Any(
                c => atom.Values.Any(v => string.Equals(c, v, StringComparison.OrdinalIgnoreCase))),
            PredicateOp.Contains => categories.Any(
                c => c.Contains(atom.Values[0], StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static bool TryCompareNumeric(string left, string right, out int comparison)
    {
        // A non-numeric operand cannot be at least or at most anything.
        if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftValue)
            && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightValue))
        {
            comparison = leftValue.CompareTo(rightValue);
            return true;
        }

        comparison = 0;
        return false;
    }
}
