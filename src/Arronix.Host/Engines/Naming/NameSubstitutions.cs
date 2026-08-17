using Arronix.Common.Naming;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// One character substitution applied to token values before sanitization.
/// </summary>
/// <param name="Match">The text to replace. Longest match wins.</param>
/// <param name="Replacement">What it becomes. Must itself survive sanitization unchanged.</param>
internal readonly record struct SubstitutionRule(string Match, string Replacement);

/// <summary>
/// A validated substitution table, replacing the surveyed <c>ColonReplacementFormat</c> enum plus its
/// parallel <c>BadCharacters</c>/<c>GoodCharacters</c> arrays.
/// </summary>
/// <remarks>
/// The defaults reproduce the surveyed behavior exactly (<c>docs/design/naming-and-tokens.md</c> §6.3;
/// Sonarr <c>src/NzbDrone.Core/Organizer/FileNameBuilder.cs:1160-1200</c>): the two-rule "smart colon"
/// (<c>": " → " - "</c> then <c>":" → "-"</c>), path separators to <c>+</c>, <c>?</c> to <c>!</c>, and
/// deletion for everything else illegal — which <see cref="TokenSanitizer.SanitizeComponent"/> already
/// does, so deletion needs no rule here.
/// </remarks>
internal sealed class NameSubstitutions
{
    private readonly IReadOnlyList<SubstitutionRule> _rules;

    private NameSubstitutions(IReadOnlyList<SubstitutionRule> rules) => _rules = rules;

    /// <summary>
    /// Gets the surveyed default table.
    /// </summary>
    public static NameSubstitutions Default { get; } = new(
    [
        new SubstitutionRule(": ", " - "),
        new SubstitutionRule(":", "-"),
        new SubstitutionRule("/", "+"),
        new SubstitutionRule("\\", "+"),
        new SubstitutionRule("?", "!"),
    ]);

    /// <summary>
    /// Builds a table, refusing any replacement that sanitization would alter.
    /// </summary>
    /// <param name="rules">The rules, longest match applied first.</param>
    /// <param name="substitutions">The table, when every rule is legal.</param>
    /// <param name="offendingRule">The first illegal rule, otherwise.</param>
    /// <returns>Whether the table is usable.</returns>
    /// <remarks>
    /// A replacement that is not already a legal name fragment would silently reintroduce what the
    /// sanitizer removes — a user mapping <c>":" → "/"</c> would be creating directories.
    /// </remarks>
    public static bool TryCreate(
        IReadOnlyList<SubstitutionRule> rules,
        out NameSubstitutions substitutions,
        out SubstitutionRule offendingRule)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules)
        {
            if (rule.Replacement.Length != 0
                && !string.Equals(TokenSanitizer.SanitizeComponent(rule.Replacement), rule.Replacement.Trim(), StringComparison.Ordinal))
            {
                substitutions = Default;
                offendingRule = rule;
                return false;
            }
        }

        var ordered = new List<SubstitutionRule>(rules);
        ordered.Sort((left, right) => right.Match.Length.CompareTo(left.Match.Length));

        substitutions = new NameSubstitutions(ordered);
        offendingRule = default;
        return true;
    }

    /// <summary>
    /// Applies the table to a token value, longest match first, single pass.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The substituted value.</returns>
    public string Apply(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (var rule in _rules)
        {
            value = value.Replace(rule.Match, rule.Replacement, StringComparison.Ordinal);
        }

        return value;
    }
}
