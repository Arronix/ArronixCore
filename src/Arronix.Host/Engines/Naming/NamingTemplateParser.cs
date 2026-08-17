using System.Globalization;
using System.Linq;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Parses naming template text into a validated node tree.
/// </summary>
/// <remarks>
/// <para>
/// A hand-written recursive-descent parser rather than a regex, because the <c>&lt;…&gt;</c> optional
/// groups and <c>{span}</c> groups nest and a nesting language is not a regular one
/// (<c>docs/design/naming-and-tokens.md</c> §3.1 — the four surveyed applications each hand a different
/// regex the same job, and two of them cannot even escape a brace).
/// </para>
/// <para>
/// Grammar accepted here, per §3.2: <c>{{</c>/<c>}}</c> escapes; <c>{affix Name:spec affix}</c> tokens;
/// <c>&lt;…&gt;</c> optional groups; <c>{span}…{|}…{/span}</c> span groups with a <c>range</c> option and
/// an optional <c>space.component</c> reference; <c>/</c> as the path separator. <c>&lt;</c> and
/// <c>&gt;</c> are grammar only — <see cref="Arronix.Common.Naming.TokenSanitizer"/> strips them from
/// every name, so they can never be literal output and need no escape form.
/// </para>
/// </remarks>
internal static class NamingTemplateParser
{
    private const string SpanOpen = "span";
    private const string SpanTail = "|";
    private const string SpanClose = "/span";

    /// <summary>
    /// Parses a template.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <returns>The compilation: nodes when clean, errors otherwise.</returns>
    public static CompiledNamingTemplate Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var errors = new List<string>();
        var position = 0;
        var nodes = ParseSegments(template, ref position, errors, insideOptional: false, insideSpan: false, stopMarkers: [], out _);

        if (position < template.Length)
        {
            errors.Add($"Unexpected '{template[position]}' at position {position.ToString(CultureInfo.InvariantCulture)}.");
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectTokens(nodes, referenced);

        return new CompiledNamingTemplate
        {
            Text = template,
            Nodes = nodes,
            ReferencedTokens = referenced,
            Errors = errors,
        };
    }

    /// <summary>
    /// Lower-cases a token name and strips every separator, producing the lookup and collision key.
    /// </summary>
    /// <param name="tokenName">The name as written.</param>
    /// <returns>The canonical form.</returns>
    /// <remarks>
    /// This is the surveyed token equality comparer promoted to a grammar rule (§3.2): the same fold
    /// makes <c>{Series Title}</c>, <c>{series.title}</c> and <c>{SERIES_TITLE}</c> one token.
    /// </remarks>
    public static string Canonicalize(string tokenName)
    {
        ArgumentNullException.ThrowIfNull(tokenName);

        Span<char> buffer = stackalloc char[tokenName.Length];
        var length = 0;

        foreach (var symbol in tokenName)
        {
            if (char.IsLetterOrDigit(symbol))
            {
                buffer[length++] = char.ToLowerInvariant(symbol);
            }
        }

        return new string(buffer[..length]);
    }

    private static List<NamingTemplateNode> ParseSegments(
        string text,
        ref int position,
        List<string> errors,
        bool insideOptional,
        bool insideSpan,
        IReadOnlyList<string> stopMarkers,
        out string? stoppedAt)
    {
        stoppedAt = null;
        var nodes = new List<NamingTemplateNode>();
        var literal = new System.Text.StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                nodes.Add(new NamingTemplateNode.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        while (position < text.Length)
        {
            var symbol = text[position];

            switch (symbol)
            {
                case '{' when position + 1 < text.Length && text[position + 1] == '{':
                    literal.Append('{');
                    position += 2;
                    continue;

                case '}' when position + 1 < text.Length && text[position + 1] == '}':
                    literal.Append('}');
                    position += 2;
                    continue;

                case '{':
                    var braceBody = ReadBraced(text, ref position, errors);

                    if (braceBody is null)
                    {
                        FlushLiteral();
                        return nodes;
                    }

                    var marker = stopMarkers.FirstOrDefault(candidate => IsMarker(braceBody, candidate));

                    if (marker is not null)
                    {
                        stoppedAt = marker;
                        FlushLiteral();
                        return nodes;
                    }

                    if (IsMarker(braceBody, SpanClose) || IsMarker(braceBody, SpanTail))
                    {
                        errors.Add($"'{{{braceBody}}}' has no matching '{{span}}'.");
                        continue;
                    }

                    FlushLiteral();

                    if (braceBody.StartsWith(SpanOpen, StringComparison.OrdinalIgnoreCase)
                        && (braceBody.Length == SpanOpen.Length || !char.IsLetterOrDigit(braceBody[SpanOpen.Length])))
                    {
                        if (insideSpan)
                        {
                            // No surveyed case needs a nested span, and the semantics of a cross-product
                            // of two axes in one name are not obvious enough to guess (§3.4 rule 7).
                            errors.Add("A span group cannot contain another span group.");
                        }

                        nodes.Add(ParseSpan(braceBody, text, ref position, errors));
                        continue;
                    }

                    nodes.Add(new NamingTemplateNode.Token(ParseTokenRef(braceBody, errors)));
                    continue;

                case '}':
                    errors.Add($"Unbalanced '}}' at position {position.ToString(CultureInfo.InvariantCulture)}.");
                    position++;
                    continue;

                case '<':
                    FlushLiteral();
                    position++;
                    var children = ParseSegments(text, ref position, errors, insideOptional: true, insideSpan, stopMarkers: [], out _);

                    if (position >= text.Length || text[position] != '>')
                    {
                        errors.Add("An optional group's '<' has no matching '>'.");
                    }
                    else
                    {
                        position++;
                    }

                    if (!children.Any(child => child is NamingTemplateNode.Token or NamingTemplateNode.Optional))
                    {
                        // Unconditional literal text wearing a conditional's clothes (§4.3, ARN0022's
                        // condition, held as an error here because a slot template is plugin-authored).
                        errors.Add("An optional group must contain at least one token.");
                    }

                    nodes.Add(new NamingTemplateNode.Optional(children));
                    continue;

                case '>':
                    if (insideOptional)
                    {
                        FlushLiteral();
                        return nodes;
                    }

                    errors.Add($"Unbalanced '>' at position {position.ToString(CultureInfo.InvariantCulture)}.");
                    position++;
                    continue;

                case '/':
                case '\\':
                    FlushLiteral();
                    nodes.Add(NamingTemplateNode.Separator.Instance);
                    position++;
                    continue;

                default:
                    literal.Append(symbol);
                    position++;
                    continue;
            }
        }

        FlushLiteral();
        return nodes;
    }

    private static bool IsMarker(string braceBody, string marker) =>
        string.Equals(braceBody.Trim(), marker, StringComparison.OrdinalIgnoreCase);

    private static string? ReadBraced(string text, ref int position, List<string> errors)
    {
        // position sits on '{'.
        var close = text.IndexOf('}', position + 1);

        if (close < 0)
        {
            errors.Add($"Unbalanced '{{' at position {position.ToString(CultureInfo.InvariantCulture)}.");
            position = text.Length;
            return null;
        }

        var body = text[(position + 1)..close];
        position = close + 1;
        return body;
    }

    private static NamingTemplateNode.Span ParseSpan(
        string spanHeader,
        string text,
        ref int position,
        List<string> errors)
    {
        string? componentRef = null;
        var rangeOnly = false;

        var options = spanHeader[SpanOpen.Length..].Trim();

        if (options.StartsWith(':'))
        {
            var rest = options[1..];
            var space = rest.IndexOf(' ');
            componentRef = space < 0 ? rest : rest[..space];
            options = space < 0 ? string.Empty : rest[(space + 1)..];
        }

        foreach (var option in options.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(option, "range", StringComparison.OrdinalIgnoreCase))
            {
                rangeOnly = true;
            }
            else
            {
                errors.Add($"Unknown span option '{option}'.");
            }
        }

        var head = ParseSegments(
            text, ref position, errors, insideOptional: false, insideSpan: true,
            stopMarkers: [SpanTail, SpanClose], out var headStop);

        if (headStop is null)
        {
            errors.Add("A '{span}' group has no matching '{/span}'.");
            return new NamingTemplateNode.Span(componentRef, rangeOnly, head, []);
        }

        if (string.Equals(headStop, SpanClose, StringComparison.OrdinalIgnoreCase))
        {
            return new NamingTemplateNode.Span(componentRef, rangeOnly, head, []);
        }

        var tail = ParseSegments(
            text, ref position, errors, insideOptional: false, insideSpan: true,
            stopMarkers: [SpanClose], out var tailStop);

        if (tailStop is null)
        {
            errors.Add("A '{span}' group has no matching '{/span}'.");
        }

        return new NamingTemplateNode.Span(componentRef, rangeOnly, head, tail);
    }

    private static NamingTokenRef ParseTokenRef(string body, List<string> errors)
    {
        if (body.Contains('{', StringComparison.Ordinal))
        {
            // The surveyed tag-wrapper idiom ({tmdb-{Movie TmdbId}}) is not part of the grammar; the
            // optional group '<tmdb-{Movie TmdbId}>' says the same thing and validates.
            errors.Add($"'{{{body}}}' nests a brace. Wrap conditional text in '<…>' instead.");
        }

        // Split off the format/modifier spec first: affixes never contain ':'.
        var colon = body.IndexOf(':', StringComparison.Ordinal);
        var namePart = colon < 0 ? body : body[..colon];
        var specPart = colon < 0 ? null : body[(colon + 1)..];

        // The in-brace affixes: leading and trailing runs of separator/bracket characters.
        var start = 0;

        while (start < namePart.Length && IsAffixCharacter(namePart[start]))
        {
            start++;
        }

        var end = namePart.Length;

        while (end > start && IsAffixCharacter(namePart[end - 1]))
        {
            end--;
        }

        var prefix = namePart[..start];
        var name = namePart[start..end];
        var suffix = namePart[end..];

        if (name.Length == 0)
        {
            errors.Add($"'{{{body}}}' names no token.");
        }

        var modifiers = new List<NamingModifier>();
        int? padWidth = null;
        int? graphemeCap = null;

        if (!string.IsNullOrEmpty(specPart))
        {
            foreach (var piece in specPart.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                if (piece.Length > 0 && piece.All(character => character == '0'))
                {
                    padWidth = piece.Length;
                }
                else if (int.TryParse(piece, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var cap)
                    && cap != 0)
                {
                    graphemeCap = cap;
                }
                else if (NamingModifiers.TryParse(piece, out var modifier))
                {
                    if (modifiers.Contains(modifier))
                    {
                        errors.Add($"Modifier '{piece}' is applied twice to '{{{name}}}'.");
                    }

                    modifiers.Add(modifier);
                }
                else
                {
                    errors.Add($"'{piece}' is not a modifier, a padding width or a length for '{{{name}}}'.");
                }
            }
        }

        return new NamingTokenRef
        {
            Name = name.Trim(),
            CanonicalName = Canonicalize(name),
            Prefix = prefix,
            Suffix = suffix,
            Modifiers = modifiers,
            PadWidth = padWidth,
            GraphemeCap = graphemeCap,
        };
    }

    private static bool IsAffixCharacter(char symbol) =>
        symbol is '-' or ' ' or '.' or '_' or '[' or ']' or '(' or ')';

    private static void CollectTokens(IReadOnlyList<NamingTemplateNode> nodes, HashSet<string> into)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case NamingTemplateNode.Token token:
                    into.Add(token.Reference.CanonicalName);
                    break;
                case NamingTemplateNode.Optional optional:
                    CollectTokens(optional.Children, into);
                    break;
                case NamingTemplateNode.Span span:
                    CollectTokens(span.Head, into);
                    CollectTokens(span.Tail, into);
                    break;
                default:
                    break;
            }
        }
    }
}

/// <summary>
/// A parsed, validated template: immutable and safe to cache per (template, shape) pair.
/// </summary>
internal sealed record CompiledNamingTemplate
{
    /// <summary>Gets the template text as the user wrote it.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the node tree.</summary>
    public required IReadOnlyList<NamingTemplateNode> Nodes { get; init; }

    /// <summary>Gets the canonical names of every token the template reads.</summary>
    public required IReadOnlySet<string> ReferencedTokens { get; init; }

    /// <summary>Gets the parse errors. The template is renderable only when this is empty.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>Gets a value indicating whether the template parsed cleanly.</summary>
    public bool IsValid => Errors.Count == 0;
}
