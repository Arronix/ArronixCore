using System.Linq.Expressions;
using System.Reflection;

namespace Arronix.Abstractions.Releases;

/// <summary>How an interpreted property was established, without imposing a universal trust order.</summary>
public enum ObservationKind
{
    /// <summary>A name, manifest or provider explicitly claimed the value.</summary>
    Claimed = 0,

    /// <summary>A probe measured the artifact itself.</summary>
    Measured = 1,

    /// <summary>A recognizer inferred the value from other observations.</summary>
    Inferred = 2
}

/// <summary>One interpretation step for one typed property.</summary>
/// <param name="PropertyPath">The property path derived from a checked expression.</param>
/// <param name="Kind">How the conclusion was reached.</param>
/// <param name="Contributor">The recognizer, probe or provider that contributed it.</param>
/// <param name="Raw">The source spelling or reading, when useful for diagnostics.</param>
public sealed record InterpretationObservation(
    string PropertyPath,
    ObservationKind Kind,
    string Contributor,
    string? Raw = null);

/// <summary>A sidecar history for a typed interpreted subject.</summary>
/// <typeparam name="TSubject">The typed representation or release being explained.</typeparam>
/// <remarks>
/// The subject retains ordinary properties and nullability retains absence. This trace explains how a
/// conclusion was reached without wrapping every value or teaching policy that all measurements outrank
/// all claims across different artifacts.
/// </remarks>
public sealed record InterpretationTrace<TSubject>
    where TSubject : class
{
    /// <summary>An empty trace.</summary>
    public static InterpretationTrace<TSubject> Empty { get; } = new();

    /// <summary>Gets the recorded steps in interpretation order.</summary>
    public IReadOnlyList<InterpretationObservation> Observations { get; init; } = [];

    /// <summary>Returns a trace with one expression-checked property observation appended.</summary>
    public InterpretationTrace<TSubject> Add<TValue>(
        Expression<Func<TSubject, TValue>> property,
        ObservationKind kind,
        string contributor,
        string? raw = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(contributor);

        return this with
        {
            Observations = [.. Observations, new InterpretationObservation(Path(property.Body), kind, contributor, raw)]
        };
    }

    private static string Path(Expression expression)
    {
        var names = new Stack<string>();
        var cursor = expression;

        while (cursor is MemberExpression { Member: PropertyInfo property } member)
        {
            names.Push(property.Name);
            cursor = member.Expression!;
        }

        if (cursor is not ParameterExpression || names.Count == 0)
        {
            throw new ArgumentException("An interpretation observation must name a property path.", nameof(expression));
        }

        return string.Join('.', names);
    }
}
