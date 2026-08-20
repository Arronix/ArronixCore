using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;


namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// One kind's parse declaration, compiled and cross-checked at load.
/// </summary>
/// <remarks>
/// <para>
/// Parse, don't validate, applied to the declaration itself: every cross-reference is resolved here or
/// the definition is refused with a message naming the row — a capture binding naming a group its own
/// expression does not declare or a guard reference naming an undeclared guard. Nothing is deferred
/// to first use, because first use is some release at three in the morning.
/// </para>
/// <para>
/// Declared order is preserved byte-for-byte: the ordered tables are the algorithm, and no engine may
/// sort them.
/// </para>
/// </remarks>
internal sealed class CompiledParseDeclaration
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    internal CompiledParseDeclaration(MediaKindModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var parsing = model.Parsing
            ?? throw new ArgumentException("The legacy declarative parser requires a parse declaration.", nameof(model));

        Guards = new CompiledGuardSet(parsing.Guards);

        PreRewrites = [.. parsing.PreRewrites.Select(static rule => new CompiledRewrite(
            rule,
            CompileDeclared(rule.Regex, $"pre-rewrite '{rule.Regex}'")))];

        TitlePatterns = [.. parsing.TitlePatterns.Select(pattern => CompilePattern(pattern, Guards))];

        TokenTables = [.. parsing.TokenTables.Select(static table => CompileTable(table))];

    }

    /// <summary>Gets the compiled guard set.</summary>
    internal CompiledGuardSet Guards { get; }

    /// <summary>Gets the compiled pre-substitutions, in declared order.</summary>
    internal IReadOnlyList<CompiledRewrite> PreRewrites { get; }

    /// <summary>Gets the compiled title patterns, in declared order.</summary>
    internal IReadOnlyList<CompiledTitlePattern> TitlePatterns { get; }

    /// <summary>Gets the compiled token tables, in declared order.</summary>
    internal IReadOnlyList<CompiledTokenTable> TokenTables { get; }

    private static Regex CompileDeclared(string pattern, string what, bool ignoreCase = true)
    {
        var options = RegexOptions.CultureInvariant;

        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(
                pattern,
                options,
                MatchTimeout);
        }
        catch (ArgumentException inner)
        {
            throw new ArgumentException(
                $"The declared expression of {what} does not compile: {inner.Message}",
                nameof(pattern),
                inner);
        }
    }

    private static CompiledTitlePattern CompilePattern(TitlePattern pattern, CompiledGuardSet guards)
    {
        var expression = CompileDeclared(pattern.Regex, $"title pattern '{pattern.PatternId}'");
        var groupNames = new HashSet<string>(expression.GetGroupNames(), StringComparer.Ordinal);

        foreach (var capture in pattern.Captures)
        {
            if (!groupNames.Contains(capture.GroupName))
            {
                throw new ArgumentException(
                    $"Title pattern '{pattern.PatternId}' binds capture group '{capture.GroupName}', "
                    + "which its expression does not declare.",
                    nameof(pattern));
            }

            var defect = capture.Target switch
            {
                CaptureTarget.CoordinateComponent when
                    string.IsNullOrEmpty(capture.SpaceId) || string.IsNullOrEmpty(capture.ComponentId) =>
                    "a coordinate capture names both a space and a component",
                CaptureTarget.ExternalId when string.IsNullOrEmpty(capture.Key) =>
                    "an external-identifier capture names its scheme in Key",
                CaptureTarget.Tag when string.IsNullOrEmpty(capture.Key) =>
                    "a tag capture names its tag key in Key",
                _ => null,
            };

            if (defect is not null)
            {
                throw new ArgumentException(
                    $"Title pattern '{pattern.PatternId}', capture '{capture.GroupName}': {defect}.",
                    nameof(pattern));
            }
        }

        foreach (var guardRef in pattern.Guards)
        {
            if (!guards.Declares(guardRef.GuardId))
            {
                throw new ArgumentException(
                    $"Title pattern '{pattern.PatternId}' references guard '{guardRef.GuardId}', "
                    + "which is not declared.",
                    nameof(pattern));
            }
        }

        if (pattern.Expansion is { } expansion)
        {
            if (!groupNames.Contains(expansion.FromGroup) || !groupNames.Contains(expansion.ToGroup))
            {
                throw new ArgumentException(
                    $"Title pattern '{pattern.PatternId}' expands groups "
                    + $"'{expansion.FromGroup}'..'{expansion.ToGroup}', which its expression does not declare.",
                    nameof(pattern));
            }

            if (expansion.MaxSpan < 1)
            {
                throw new ArgumentException(
                    $"Title pattern '{pattern.PatternId}' declares a non-positive range cap.",
                    nameof(pattern));
            }
        }

        return new CompiledTitlePattern(pattern, expression);
    }

    private static CompiledTokenTable CompileTable(TokenTable table)
    {
        var rows = new List<CompiledTokenRow>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Tag))
            {
                throw new ArgumentException(
                    $"Token table '{table.TableId}' declares a row with an empty tag key.",
                    nameof(table));
            }

            var expression = CompileDeclared(
                row.Pattern, $"token table '{table.TableId}', tag '{row.Tag}'");

            rows.Add(new CompiledTokenRow(row, expression, TokenConstraint.Parse(row.Constraint, table.TableId)));
        }

        return new CompiledTokenTable(table, rows);
    }

}

/// <summary>One compiled pre-substitution.</summary>
/// <param name="Declaration">The declared rule.</param>
/// <param name="Expression">Its compiled expression.</param>
internal sealed record CompiledRewrite(RewriteRule Declaration, Regex Expression);

/// <summary>One compiled title pattern.</summary>
/// <param name="Declaration">The declared pattern.</param>
/// <param name="Expression">Its compiled expression.</param>
internal sealed record CompiledTitlePattern(TitlePattern Declaration, Regex Expression);

/// <summary>One compiled token table.</summary>
/// <param name="Declaration">The declared table.</param>
/// <param name="Rows">Its compiled rows, in declared order.</param>
internal sealed record CompiledTokenTable(TokenTable Declaration, IReadOnlyList<CompiledTokenRow> Rows);

/// <summary>One compiled token-table row.</summary>
/// <param name="Declaration">The declared row.</param>
/// <param name="Expression">Its compiled expression.</param>
/// <param name="Constraint">Its parsed validity constraint.</param>
internal sealed record CompiledTokenRow(TokenRow Declaration, Regex Expression, TokenConstraint Constraint);

/// <summary>
/// The engine's token-row constraint vocabulary: none, <c>numeric</c>, or <c>length min..max</c>.
/// </summary>
/// <remarks>
/// Deliberately tiny and validated at load; an unknown constraint spelling refuses the definition. The
/// two members exist because the surveyed identifier conventions need exactly them: an embedded numeric
/// identifier, and an identifier whose length range is its checksum.
/// </remarks>
internal sealed record TokenConstraint(bool RequireNumeric, int MinLength, int MaxLength)
{
    /// <summary>Gets the constraint that accepts everything.</summary>
    internal static TokenConstraint None { get; } = new(false, 0, int.MaxValue);

    /// <summary>Parses a declared constraint spelling.</summary>
    /// <param name="spelling">The spelling, or null.</param>
    /// <param name="tableId">The declaring table, for the refusal message.</param>
    /// <returns>The constraint.</returns>
    internal static TokenConstraint Parse(string? spelling, string tableId)
    {
        if (string.IsNullOrWhiteSpace(spelling))
        {
            return None;
        }

        if (string.Equals(spelling, "numeric", StringComparison.Ordinal))
        {
            return new TokenConstraint(true, 0, int.MaxValue);
        }

        const string LengthPrefix = "length ";

        if (spelling.StartsWith(LengthPrefix, StringComparison.Ordinal))
        {
            var range = spelling[LengthPrefix.Length..].Split("..", StringSplitOptions.TrimEntries);

            if (range.Length == 2
                && int.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
                && int.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
                && min >= 0
                && max >= min)
            {
                return new TokenConstraint(false, min, max);
            }
        }

        throw new ArgumentException(
            $"Token table '{tableId}' declares constraint '{spelling}', which is outside the engine's "
            + "constraint vocabulary (numeric | length min..max).",
            nameof(spelling));
    }

    /// <summary>Determines whether a captured value satisfies the constraint.</summary>
    /// <param name="value">The captured text.</param>
    /// <returns>Whether it satisfies.</returns>
    internal bool Accepts(string value)
    {
        if (value.Length < MinLength || value.Length > MaxLength)
        {
            return false;
        }

        return !RequireNumeric
            || long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }
}
