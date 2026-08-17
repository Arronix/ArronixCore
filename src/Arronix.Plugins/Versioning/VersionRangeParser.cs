namespace Arronix.Plugins.Versioning;

/// <summary>
/// Reads the contract-range grammar.
/// </summary>
/// <remarks>
/// The only component in the platform that parses a range. Everything else is handed a
/// <see cref="VersionRange"/> that has already been proved well-formed, which is why nothing downstream has
/// to decide what an unrecognized operator means.
/// </remarks>
public static class VersionRangeParser
{
    /// <summary>The alternation operator.</summary>
    private const string Alternation = "||";

    /// <summary>
    /// Reads a range.
    /// </summary>
    /// <param name="value">The range text, for example <c>&gt;=0.3 &lt;0.4</c>.</param>
    /// <param name="range">The range when the text was well-formed; otherwise <see langword="null"/>.</param>
    /// <param name="error">
    /// A message naming what was rejected and what to write instead, or <see langword="null"/> on success.
    /// </param>
    /// <returns><see langword="true"/> when the text was well-formed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out VersionRange? range, out string? error)
    {
        range = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A contract range must not be empty. Write an explicit lower and upper bound, for example '>=0.3 <0.4'.";
            return false;
        }

        var alternatives = value.Split(Alternation, StringSplitOptions.TrimEntries);
        var sets = new List<ComparatorSet>(alternatives.Length);

        foreach (var alternative in alternatives)
        {
            if (!TryParseComparatorSet(alternative, out var set, out error))
            {
                return false;
            }

            sets.Add(set!);
        }

        range = new VersionRange(sets, value.Trim());
        return true;
    }

    /// <summary>
    /// Reads a range.
    /// </summary>
    /// <param name="value">The range text.</param>
    /// <returns>The range.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a well-formed range.</exception>
    public static VersionRange Parse(string value)
    {
        if (!TryParse(value, out var range, out var error))
        {
            throw new FormatException(error);
        }

        return range!;
    }

    private static bool TryParseComparatorSet(string value, out ComparatorSet? set, out string? error)
    {
        set = null;
        error = null;

        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "A contract range alternative must not be empty.";
            return false;
        }

        // A hyphen range is two versions separated by a bare hyphen token. Rejecting it here, before the
        // comparator loop, produces a message about hyphen ranges rather than about a stray operand.
        foreach (var token in tokens)
        {
            if (token == "-")
            {
                error = "Hyphen ranges are not supported. Write comparisons instead, for example '>=0.3.0 <=0.4.0'.";
                return false;
            }
        }

        var comparators = new List<VersionComparator>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!TryParseComparator(token, out var comparator, out error))
            {
                return false;
            }

            comparators.Add(comparator);
        }

        set = new ComparatorSet(comparators);
        return true;
    }

    private static bool TryParseComparator(string token, out VersionComparator comparator, out string? error)
    {
        comparator = default;
        error = null;

        switch (token[0])
        {
            case '^':
                error = $"The caret operator is not supported ('{token}'). Its meaning below 1.0.0 is ambiguous; write an explicit range such as '>=0.3 <0.4'.";
                return false;
            case '~':
                error = $"The tilde operator is not supported ('{token}'). Write an explicit range such as '>=0.3 <0.4'.";
                return false;
        }

        var (op, offset) = token switch
        {
            _ when token.StartsWith(">=", StringComparison.Ordinal) => (ComparatorOperator.GreaterThanOrEqual, 2),
            _ when token.StartsWith("<=", StringComparison.Ordinal) => (ComparatorOperator.LessThanOrEqual, 2),
            _ when token.StartsWith('>') => (ComparatorOperator.GreaterThan, 1),
            _ when token.StartsWith('<') => (ComparatorOperator.LessThan, 1),
            _ when token.StartsWith('=') => (ComparatorOperator.Equal, 1),
            _ => (ComparatorOperator.Equal, 0)
        };

        var operand = token[offset..].Trim();
        if (operand.Length == 0)
        {
            error = $"'{token}' has no version to compare against.";
            return false;
        }

        if (ContainsWildcard(operand))
        {
            error = $"Wildcards are not supported ('{token}'). Write an explicit range such as '>=0.3 <0.4'.";
            return false;
        }

        if (!SemanticVersion.TryParse(operand, out var version))
        {
            error = $"'{operand}' is not a well-formed version.";
            return false;
        }

        comparator = new VersionComparator(op, version);
        return true;
    }

    /// <remarks>
    /// Only the numeric components are inspected. A prerelease identifier may legitimately contain the
    /// letter that spells a wildcard, and rejecting <c>0.3.0-x64</c> as a wildcard range would be a bug
    /// dressed as strictness.
    /// </remarks>
    private static bool ContainsWildcard(string operand)
    {
        var span = operand.AsSpan();
        var end = span.IndexOfAny('-', '+');
        if (end >= 0)
        {
            span = span[..end];
        }

        return span.IndexOfAny('*', 'x', 'X') >= 0;
    }
}
