// Consumes the experimental definition contracts (ARX0019).
#pragma warning disable ARX0019

using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// A kind's declared guard expressions, compiled once at load.
/// </summary>
/// <remarks>
/// Each guard chooses its input — raw or normalized — and its case sensitivity, exactly as the surveyed
/// sources make those choices: <c>REAL</c> is a revision marker only in upper case, and a bracketed
/// literal is only visible in the raw text. Declared expressions compile with a timeout, and a guard
/// whose expression cannot finish reads as not matching rather than failing the parse.
/// </remarks>
internal sealed class CompiledGuardSet
{
    private readonly Dictionary<string, (Regex Expression, GuardInput Input)> _guards;

    internal CompiledGuardSet(IReadOnlyList<GuardPattern> declared)
    {
        _guards = new Dictionary<string, (Regex, GuardInput)>(declared.Count, StringComparer.Ordinal);

        foreach (var guard in declared)
        {
            if (string.IsNullOrWhiteSpace(guard.GuardId))
            {
                throw new ArgumentException("A guard pattern declares an empty identifier.", nameof(declared));
            }

            if (_guards.ContainsKey(guard.GuardId))
            {
                throw new ArgumentException(
                    $"Guard '{guard.GuardId}' is declared more than once.", nameof(declared));
            }

            var options = RegexOptions.CultureInvariant;

            if (!guard.CaseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            Regex expression;

            try
            {
                expression = new Regex(
                    guard.Regex,
                    options,
                    TimeSpan.FromMilliseconds(ReleaseTokenVocabulary.MatchTimeoutMilliseconds));
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException(
                    $"Guard '{guard.GuardId}' declares an invalid expression: {inner.Message}",
                    nameof(declared),
                    inner);
            }

            _guards[guard.GuardId] = (expression, guard.Input);
        }
    }

    /// <summary>Gets every declared guard identifier, in declaration order.</summary>
    internal IReadOnlyCollection<string> Ids => _guards.Keys;

    /// <summary>Determines whether a guard identifier is declared.</summary>
    /// <param name="guardId">The identifier.</param>
    /// <returns>Whether it resolves.</returns>
    internal bool Declares(string guardId) => _guards.ContainsKey(guardId);

    /// <summary>Evaluates one guard against the two text forms.</summary>
    /// <param name="guardId">The guard.</param>
    /// <param name="rawText">The raw text.</param>
    /// <param name="normalizedText">The working text.</param>
    /// <returns>Whether the guard matches its chosen input.</returns>
    internal bool Matches(string guardId, string rawText, string normalizedText)
    {
        var (expression, input) = _guards[guardId];
        var text = input == GuardInput.Raw ? rawText : normalizedText;

        try
        {
            return expression.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // A guard that cannot finish over one hostile title asserts nothing about it.
            return false;
        }
    }
}
