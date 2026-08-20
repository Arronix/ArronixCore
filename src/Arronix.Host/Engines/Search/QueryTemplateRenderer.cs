using System.Globalization;
using System.Linq;
using System.Text;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Engines.Search;

/// <summary>
/// Renders the query templates' token language: <c>{title}</c> and <c>{fieldId}</c> over an item's
/// fields, the <c>:query</c> modifier, <c>{coord:spellingId}</c> through the declared coordinate
/// grammar, and the kind's credited-name substitutions.
/// </summary>
/// <remarks>
/// A multivalued field fans one template into one rendering per element, which is how an
/// alternative-titles alias row becomes one spelling per title. Language-tagged elements — a composite
/// of text and language — carry their tag out so alias rows can be filtered by the acquisition's
/// accepted languages; an untagged element states no language and is never filtered by one.
/// </remarks>
internal static class QueryTemplateRenderer
{
    /// <summary>The token name bound to the item's title, overridable per alias spelling.</summary>
    internal const string TitleToken = "title";

    private const string CoordinatePrefix = "coord";
    private const string QueryModifier = "query";

    /// <summary>
    /// One rendered value with the language it states, when it states one.
    /// </summary>
    /// <param name="Text">The rendered text.</param>
    /// <param name="Language">The stated language, or <see langword="null"/> when none is stated.</param>
    internal readonly record struct RenderedValue(string Text, Language? Language);

    /// <summary>
    /// Renders a template into every value it takes, fanning out over multivalued tokens.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="context">The item and declaration the tokens resolve against.</param>
    /// <returns>The renderings; empty when a token resolves to nothing.</returns>
    internal static IReadOnlyList<RenderedValue> Render(string template, QueryTemplateContext context)
    {
        var results = new List<RenderedValue>();
        RenderInto(template, 0, string.Empty, null, context, results);
        return results;
    }

    /// <summary>
    /// Renders a template into one value, taking the first element of any multivalued token and
    /// rendering an absent token as nothing.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="context">The item and declaration the tokens resolve against.</param>
    /// <returns>The rendering, whitespace-collapsed.</returns>
    internal static string RenderLenient(string template, QueryTemplateContext context)
    {
        var builder = new StringBuilder(template.Length);
        var position = 0;

        while (position < template.Length)
        {
            var (literal, token, next) = NextToken(template, position);
            builder.Append(literal);
            if (token is not null)
            {
                var values = TokenValues(token, context);
                if (values.Count > 0)
                {
                    builder.Append(values[0].Text);
                }
            }

            position = next;
        }

        return CollapseWhitespace(builder.ToString());
    }

    /// <summary>
    /// Resolves a template that is exactly one bare field token to the field's own typed value, so a
    /// structured argument can carry the value rather than its spelling.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="context">The item the token resolves against.</param>
    /// <param name="value">The field's value when the shape matched and the field is present.</param>
    /// <returns><see langword="true"/> when the template was one bare present field token.</returns>
    internal static bool TryResolveBareField(
        string template,
        QueryTemplateContext context,
        out FieldValue value)
    {
        value = FieldValue.Absent(FieldValueKind.Text);
        var trimmed = template.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return false;
        }

        var inner = trimmed[1..^1];
        if (inner.Contains(':', StringComparison.Ordinal)
            || string.Equals(inner, TitleToken, StringComparison.Ordinal))
        {
            return false;
        }

        if (!context.Item.Fields.TryGetValue(inner, out var field) || field.IsAbsent)
        {
            return false;
        }

        value = field;
        return true;
    }

    private static void RenderInto(
        string template,
        int position,
        string prefix,
        Language? language,
        QueryTemplateContext context,
        List<RenderedValue> results)
    {
        while (position < template.Length)
        {
            var (literal, token, next) = NextToken(template, position);
            if (token is null)
            {
                prefix += literal;
                position = next;
                continue;
            }

            var values = TokenValues(token, context);
            if (values.Count == 0)
            {
                return;
            }

            foreach (var value in values)
            {
                RenderInto(
                    template,
                    next,
                    prefix + literal + value.Text,
                    value.Language ?? language,
                    context,
                    results);
            }

            return;
        }

        var rendered = CollapseWhitespace(prefix);
        if (rendered.Length > 0)
        {
            results.Add(new RenderedValue(rendered, language));
        }
    }

    private static (string Literal, string? Token, int Next) NextToken(string template, int position)
    {
        var open = template.IndexOf('{', position);
        if (open == -1)
        {
            return (template[position..], null, template.Length);
        }

        var close = template.IndexOf('}', open + 1);
        if (close == -1)
        {
            return (template[position..], null, template.Length);
        }

        return (template[position..open], template[(open + 1)..close], close + 1);
    }

    private static IReadOnlyList<RenderedValue> TokenValues(string token, QueryTemplateContext context)
    {
        var parts = token.Split(':');
        var name = parts[0];

        if (string.Equals(name, CoordinatePrefix, StringComparison.Ordinal))
        {
            if (parts.Length != 2)
            {
                throw new InvalidOperationException(
                    $"Coordinate token '{{{token}}}' must name exactly one spelling: '{{coord:spellingId}}'.");
            }

            var spelled = RenderCoordinate(parts[1], context);
            return spelled is null ? [] : [new RenderedValue(spelled, null)];
        }

        var applyQueryCleaning = parts.Length > 1 && string.Equals(parts[1], QueryModifier, StringComparison.Ordinal);

        var raw = string.Equals(name, TitleToken, StringComparison.Ordinal)
            ? [new RenderedValue(context.TitleOverride ?? context.Item.Title, null)]
            : FieldValues(name, context);

        return raw
            .Select(value => value with { Text = Substitute(value.Text, context.Substitutions) })
            .Select(value => applyQueryCleaning ? value with { Text = CleanForQuery(value.Text) } : value)
            .Where(value => value.Text.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<RenderedValue> FieldValues(string fieldId, QueryTemplateContext context)
    {
        if (!context.Item.Fields.TryGetValue(fieldId, out var field) || field.IsAbsent)
        {
            return [];
        }

        if (field.Items is { } items)
        {
            // A composite whose components are not themselves composites is ONE value; anything else
            // with elements — a text multivalue, a repeated composite — fans out per element.
            var isSingleComposite = field.Kind == FieldValueKind.Composite
                && !items.All(item => item.Kind == FieldValueKind.Composite);

            if (!isSingleComposite)
            {
                return items.Select(ElementValue).Where(value => value.Text.Length > 0).ToArray();
            }
        }

        var single = ElementValue(field);
        return single.Text.Length > 0 ? [single] : [];
    }

    private static RenderedValue ElementValue(FieldValue element)
    {
        if (element.Kind == FieldValueKind.Composite && element.Items is { } components)
        {
            // A language-tagged value may wrap a media-owned composite. The wrapper remains one value:
            // the first text is its query spelling and the language can live at any component depth.
            var text = FindFirstText(element);
            var language = FindLanguage(element);
            return new RenderedValue(text ?? string.Empty, language);
        }

        return new RenderedValue(FormatScalar(element), element.Language);
    }

    private static string? FindFirstText(FieldValue value)
        => value.Text ?? value.Items?
            .Select(FindFirstText)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));

    private static Language? FindLanguage(FieldValue value)
        => value.Language ?? value.Items?
            .Select(FindLanguage)
            .FirstOrDefault(static language => language is not null);

    private static string FormatScalar(FieldValue value)
    {
        if (value.Text is { Length: > 0 } text)
        {
            return text;
        }

        if (value.Number is { } number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (value.Real is { } real)
        {
            return real.ToString(CultureInfo.InvariantCulture);
        }

        if (value.Date is { } date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string? RenderCoordinate(string spellingId, QueryTemplateContext context)
    {
        var spelling = context.Grammar.Spellings.FirstOrDefault(candidate =>
            string.Equals(candidate.SpellingId, spellingId, StringComparison.Ordinal));

        if (spelling is null)
        {
            throw new InvalidOperationException(
                $"'{spellingId}' names no declared coordinate spelling.");
        }

        if (!context.Item.Coordinates.TryGet(spelling.SpaceId, out var reading)
            || reading.Value.Kind != CoordinateKind.Ordinal)
        {
            return null;
        }

        var ordinals = reading.Value.Ordinals;
        var builder = new StringBuilder(spelling.Template.Length);
        var component = 0;
        var position = 0;

        while (position < spelling.Template.Length)
        {
            var (literal, token, next) = NextToken(spelling.Template, position);
            builder.Append(literal);

            if (token is not null)
            {
                if (token.Length == 0 || token.Any(character => character != '0'))
                {
                    throw new InvalidOperationException(
                        $"Spelling '{spellingId}' contains '{{{token}}}'; a spelling slot is a run of zeros.");
                }

                if (component >= ordinals.Length)
                {
                    return null;
                }

                builder.Append(ordinals[component].ToString(CultureInfo.InvariantCulture)
                    .PadLeft(token.Length, '0'));
                component++;
            }

            position = next;
        }

        return builder.ToString();
    }

    private static string Substitute(string text, IReadOnlyList<CreditSubstitution> substitutions)
    {
        foreach (var substitution in substitutions)
        {
            if (string.Equals(text, substitution.Credit, StringComparison.OrdinalIgnoreCase))
            {
                return substitution.Substitute;
            }
        }

        return text;
    }

    private static string CleanForQuery(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            builder.Append(character is ':' or ';' or ',' or '!' or '?' or '\'' or '"' or '&' ? ' ' : character);
        }

        return CollapseWhitespace(builder.ToString());
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Everything a template token can resolve against.
/// </summary>
internal sealed record QueryTemplateContext
{
    /// <summary>
    /// Gets the item whose fields and coordinates the tokens read.
    /// </summary>
    public required ItemView Item { get; init; }

    /// <summary>
    /// Gets the spelling standing in for the title token, when a fan-out query substitutes one.
    /// </summary>
    public string? TitleOverride { get; init; }

    /// <summary>
    /// Gets the declared coordinate grammar.
    /// </summary>
    public CoordinateGrammar Grammar { get; init; } = CoordinateGrammar.None;

    /// <summary>
    /// Gets the declared credited-name substitutions.
    /// </summary>
    public IReadOnlyList<CreditSubstitution> Substitutions { get; init; } = [];
}
