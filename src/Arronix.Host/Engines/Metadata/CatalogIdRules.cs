// The shape (ARX0013) and definition (ARX0019) contracts are experimental until 1.0.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// Executes a catalog's declared identifier rules: canonicalization and user-typed lookup forms.
/// </summary>
/// <remarks>
/// The four rule kinds are the decisions the surveyed identifier handling actually makes, ported from
/// <c>Arronix.Plugin.Movies/Providers/MoviesCataloger.cs</c> (<c>MovieIdentifiers</c>) and Radarr's
/// parsing of pasted addresses: restore a prefix and zero-pad (imdb <c>tt</c> + 7), extract from an
/// address segment (optionally discarding a slug), recognize a typed scheme prefix, and split a
/// trailing year off a text lookup within declared bounds.
/// </remarks>
internal sealed class CatalogIdRules
{
    private readonly IReadOnlyList<IdNormalization> _rules;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogIdRules"/> class.
    /// </summary>
    /// <param name="rules">The declared rules, in declared order.</param>
    /// <param name="clock">The clock bounding trailing-year splits.</param>
    public CatalogIdRules(IReadOnlyList<IdNormalization> rules, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(clock);
        _rules = rules;
        _clock = clock;
    }

    /// <summary>
    /// Canonicalizes an identifier under the scheme's prefix-and-pad rule, when one is declared.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The canonical identifier; unchanged when no rule covers the scheme.</returns>
    public ExternalId Normalize(ExternalId id)
    {
        var rule = _rules.FirstOrDefault(candidate =>
            candidate.Kind == IdRuleKind.PrefixPad
            && string.Equals(candidate.Scheme, id.Scheme, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            return id;
        }

        return ExternalId.Of(id.Scheme, NormalizeValue(rule, id.Value));
    }

    /// <summary>
    /// Recognizes an identifier in user-typed lookup text: a typed prefix form or a pasted address.
    /// </summary>
    /// <param name="text">The lookup text.</param>
    /// <param name="id">The recognized identifier.</param>
    /// <returns>Whether the text names an identifier rather than a title.</returns>
    public bool TryRecognize(string text, out ExternalId id)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();

        foreach (var rule in _rules)
        {
            switch (rule.Kind)
            {
                case IdRuleKind.TypedPrefix when rule.Scheme is { Length: > 0 }:
                    foreach (var prefix in rule.Prefixes)
                    {
                        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && trimmed.Length > prefix.Length)
                        {
                            id = Normalize(ExternalId.Of(rule.Scheme, trimmed[prefix.Length..].Trim()));
                            return true;
                        }
                    }

                    break;

                case IdRuleKind.UrlSegment when rule.Scheme is { Length: > 0 } && rule.AddressPattern is { Length: > 0 }:
                    if (TryExtractFromAddress(rule, trimmed, out var extracted))
                    {
                        id = Normalize(ExternalId.Of(rule.Scheme, extracted));
                        return true;
                    }

                    break;

                default:
                    break;
            }
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Splits a trailing year off a text lookup, within the declared bounds.
    /// </summary>
    /// <param name="text">The lookup text, e.g. <c>Arrival 2016</c>.</param>
    /// <param name="title">The text without the year.</param>
    /// <param name="year">The year, when one was split.</param>
    /// <returns>Whether a year was split.</returns>
    /// <remarks>
    /// The bounds carry the reasoning as data: a lower bound before which nothing was published, and a
    /// slack past the current year admitting announced work
    /// (<c>MoviesCataloger.cs:369-395</c>: 1870, now + 1).
    /// </remarks>
    public bool TrySplitTrailingYear(string text, out string title, out int year)
    {
        ArgumentNullException.ThrowIfNull(text);

        title = text.Trim();
        year = 0;

        var rule = _rules.FirstOrDefault(candidate => candidate.Kind == IdRuleKind.TrailingYearSplit);

        if (rule is null)
        {
            return false;
        }

        var space = title.LastIndexOf(' ');

        if (space <= 0)
        {
            return false;
        }

        var tail = title[(space + 1)..].Trim('(', ')');

        if (!int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var candidateYear))
        {
            return false;
        }

        var lower = rule.YearLowerBound ?? 1;
        var upper = _clock.GetUtcNow().Year + (rule.YearUpperBoundYearsFromNow ?? 0);

        if (candidateYear < lower || candidateYear > upper)
        {
            return false;
        }

        title = title[..space].TrimEnd();
        year = candidateYear;
        return true;
    }

    private static string NormalizeValue(IdNormalization rule, string value)
    {
        var digits = value.Trim();

        if (rule.Prefix is { Length: > 0 } prefix
            && digits.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[prefix.Length..];
        }

        if (rule.PadDigitsTo is { } width && digits.All(char.IsAsciiDigit))
        {
            digits = digits.PadLeft(width, '0');
        }

        return (rule.Prefix ?? string.Empty) + digits;
    }

    private static bool TryExtractFromAddress(IdNormalization rule, string text, out string value)
    {
        value = string.Empty;

        // The pattern spells the identifier slot as "{id}": match the literal parts around it,
        // scheme-and-host insensitive, e.g. "imdb.com/title/{id}".
        var pattern = rule.AddressPattern!;
        var slot = pattern.IndexOf("{id}", StringComparison.Ordinal);

        if (slot < 0)
        {
            return false;
        }

        var before = pattern[..slot];
        var start = text.IndexOf(before, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return false;
        }

        var rest = text[(start + before.Length)..];
        var end = rest.IndexOfAny(['/', '?', '#', ' ']);
        var segment = end < 0 ? rest : rest[..end];

        if (segment.Length == 0)
        {
            return false;
        }

        if (rule.StripSlugAfterDigits)
        {
            // "603-the-matrix" → "603": the slug after the leading digits is presentation only.
            var digits = 0;

            while (digits < segment.Length && char.IsAsciiDigit(segment[digits]))
            {
                digits++;
            }

            if (digits > 0)
            {
                segment = segment[..digits];
            }
        }

        value = segment;
        return true;
    }
}
