using System.Globalization;
using System.Linq;
using System.Text;
using Arronix.Common.Naming;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// The options one render runs under.
/// </summary>
internal sealed record RenderOptions
{
    /// <summary>Gets the default options: 255-byte components, the surveyed substitutions, a one-grapheme ellipsis.</summary>
    public static RenderOptions Default { get; } = new();

    /// <summary>Gets the substitution table applied to token values.</summary>
    public NameSubstitutions Substitutions { get; init; } = NameSubstitutions.Default;

    /// <summary>Gets the UTF-8 byte budget for a single path component. 255 on ext4/APFS/NTFS/SMB.</summary>
    public int MaxComponentBytes { get; init; } = 255;

    /// <summary>Gets the marker inserted where elastic text was cut. One grapheme, three UTF-8 bytes.</summary>
    public string Ellipsis { get; init; } = "…";
}

/// <summary>
/// Renders a compiled template against bindings and materializes legal path components.
/// </summary>
/// <remarks>
/// <para>
/// Rendering is a <b>fragment stream</b>, not a string, until the last step
/// (<c>docs/design/naming-and-tokens.md</c> §6.1). Each fragment knows whether it is literal template
/// text, a token value, or engine-inserted (the ellipsis) — which is what lets separator collapsing skip
/// inserted text and deletes the surveyed <c>{{ellipsis}}</c> sentinel dance
/// (Sonarr <c>src/NzbDrone.Core/Organizer/FileNameBuilder.cs:191,218,1069,1224</c>).
/// </para>
/// <para>
/// The truncation ladder is §6.5(b), generalized from Sonarr's hard-coded elastic episode title
/// (<c>FileNameBuilder.cs:205-237,1142</c>): budgets are UTF-8 bytes; droppable tokens vanish first,
/// deepest level first; elastic tokens then shrink, deepest first; a still-over component is cut whole.
/// An explicit <c>:N</c> pins a token — capped and out of the elastic pool.
/// </para>
/// </remarks>
internal sealed class TemplateRenderer
{
    private static readonly char[] SeparatorCharacters = ['-', ' ', '.', '_'];

    private readonly RenderOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateRenderer"/> class.
    /// </summary>
    /// <param name="options">The render options.</param>
    public TemplateRenderer(RenderOptions? options = null) => _options = options ?? RenderOptions.Default;

    /// <summary>
    /// Renders a template into path components, one per separator-delimited segment.
    /// </summary>
    /// <param name="template">The compiled template. Must be valid.</param>
    /// <param name="bindings">The resolved token values.</param>
    /// <returns>The components, each sanitized and within the byte budget.</returns>
    /// <exception cref="ArgumentException">The template has parse errors.</exception>
    public IReadOnlyList<string> RenderComponents(CompiledNamingTemplate template, NamingTokenBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(bindings);

        if (!template.IsValid)
        {
            throw new ArgumentException(
                $"The template '{template.Text}' has errors: {string.Join(" ", template.Errors)}",
                nameof(template));
        }

        var components = new List<string>();
        var fragments = new List<Fragment>();

        foreach (var node in template.Nodes)
        {
            if (node is NamingTemplateNode.Separator)
            {
                components.Add(Materialize(fragments));
                fragments.Clear();
                continue;
            }

            Write(node, fragments, bindings, unitIndex: 0);
        }

        components.Add(Materialize(fragments));
        return components;
    }

    /// <summary>
    /// Renders a template into one component, treating any separator as literal segment glue.
    /// </summary>
    /// <param name="template">The compiled template.</param>
    /// <param name="bindings">The resolved token values.</param>
    /// <returns>The single joined component.</returns>
    public string RenderComponent(CompiledNamingTemplate template, NamingTokenBindings bindings)
    {
        var components = RenderComponents(template, bindings);

        return components.Count == 1
            ? components[0]
            : string.Join(' ', components.Where(component => component.Length > 0));
    }

    private void Write(
        NamingTemplateNode node,
        List<Fragment> into,
        NamingTokenBindings bindings,
        int unitIndex)
    {
        switch (node)
        {
            case NamingTemplateNode.Literal literal:
                into.Add(new Fragment(literal.Text, FragmentKind.Literal, null));
                break;

            case NamingTemplateNode.Token token:
                WriteToken(token.Reference, into, bindings, unitIndex);
                break;

            case NamingTemplateNode.Optional optional:
                var children = new List<Fragment>();

                foreach (var child in optional.Children)
                {
                    Write(child, children, bindings, unitIndex);
                }

                // An optional group renders iff at least one token inside it resolved non-empty (§3.3).
                if (children.Any(fragment => fragment.Kind == FragmentKind.TokenValue && fragment.Text.Length > 0))
                {
                    into.AddRange(children);
                }

                break;

            case NamingTemplateNode.Span span:
                WriteSpan(span, into, bindings);
                break;

            default:
                break;
        }
    }

    private void WriteSpan(NamingTemplateNode.Span span, List<Fragment> into, NamingTokenBindings bindings)
    {
        var units = bindings.UnitCount;

        foreach (var child in span.Head)
        {
            Write(child, into, bindings, unitIndex: 0);
        }

        if (units <= 1)
        {
            // With one unit the tail never renders and the group is indistinguishable from plain
            // text (§3.4 rule 3).
            return;
        }

        if (span.RangeOnly)
        {
            // Range iterates first and last only — Sonarr's FormatRangeNumberTokens
            // (src/NzbDrone.Core/Organizer/FileNameBuilder.cs:940-950).
            foreach (var child in span.Tail)
            {
                Write(child, into, bindings, unitIndex: units - 1);
            }

            return;
        }

        for (var unit = 1; unit < units; unit++)
        {
            foreach (var child in span.Tail)
            {
                Write(child, into, bindings, unit);
            }
        }
    }

    private void WriteToken(
        NamingTokenRef reference,
        List<Fragment> into,
        NamingTokenBindings bindings,
        int unitIndex)
    {
        bindings.TryGet(reference.CanonicalName, out var binding);

        var raw = binding is null || binding.Values.Count == 0
            ? string.Empty
            : binding.Values[Math.Min(unitIndex, binding.Values.Count - 1)];

        foreach (var modifier in reference.Modifiers)
        {
            raw = NamingModifiers.Apply(raw, modifier, binding?.Year);
        }

        if (reference.PadWidth is { } width
            && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            raw = number.ToString(new string('0', width), CultureInfo.InvariantCulture);
        }

        if (reference.GraphemeCap is { } cap)
        {
            raw = GraphemeText.CapGraphemes(raw, cap);
        }

        var value = CleanValue(raw);

        if (value.Length == 0)
        {
            // A token that rendered empty takes its affixes with it (§6.1 "drop affixes").
            return;
        }

        // An explicit cap pins the token: it leaves the elastic pool (§6.5(b)).
        var elasticity = reference.GraphemeCap is null
            ? binding?.Elasticity ?? TokenElasticity.Rigid
            : TokenElasticity.Rigid;

        var meta = new TokenMeta(reference.CanonicalName, elasticity, binding?.Depth ?? 0);

        if (reference.Prefix.Length > 0)
        {
            into.Add(new Fragment(reference.Prefix, FragmentKind.Literal, meta));
        }

        into.Add(new Fragment(value, FragmentKind.TokenValue, meta));

        if (reference.Suffix.Length > 0)
        {
            into.Add(new Fragment(reference.Suffix, FragmentKind.Literal, meta));
        }
    }

    private string CleanValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var substituted = _options.Substitutions.Apply(raw);
        var sanitized = TokenSanitizer.SanitizeComponent(substituted);

        // SanitizeComponent answers for whole components and never returns empty; a token value that
        // sanitized down to nothing must render as nothing so its affixes drop.
        return string.Equals(sanitized, TokenSanitizer.EmptyNamePlaceholder, StringComparison.Ordinal)
            && !substituted.Contains(TokenSanitizer.EmptyNamePlaceholder, StringComparison.Ordinal)
            ? string.Empty
            : sanitized;
    }

    private string Materialize(List<Fragment> fragments)
    {
        Truncate(fragments);

        var builder = new StringBuilder();

        foreach (var fragment in fragments)
        {
            if (fragment.Kind == FragmentKind.Inserted)
            {
                // Inserted text is exempt from collapsing by construction — the reason no ellipsis
                // sentinel exists (§6.5(f)).
                builder.Append(fragment.Text);
                continue;
            }

            var text = fragment.Text;

            // Collapse a separator run spanning the boundary: the run's first character wins.
            if (builder.Length > 0 && text.Length > 0
                && IsSeparator(builder[^1]) && builder[^1] == text[0])
            {
                text = text.TrimStart(builder[^1]);
            }

            builder.Append(text);
        }

        var component = CollapseRuns(builder.ToString()).Trim(SeparatorCharacters);

        if (component.Length == 0)
        {
            return string.Empty;
        }

        // The whole-component backstop, grapheme-safe and extension-aware.
        component = TokenSanitizer.TruncateComponent(component, _options.MaxComponentBytes);

        // The reserved-device-name rule, per component (§6.1's last fix before joining).
        return TokenSanitizer.IsReservedName(component)
            ? TokenSanitizer.SanitizeComponent(component)
            : component;
    }

    private void Truncate(List<Fragment> fragments)
    {
        var budget = _options.MaxComponentBytes;

        if (TotalBytes(fragments) <= budget)
        {
            return;
        }

        // Pass 1: drop droppable tokens with their affixes, deepest first, in reverse template order.
        var droppable = fragments
            .Where(fragment => fragment.Token is { Elasticity: TokenElasticity.Droppable })
            .Select(fragment => fragment.Token!)
            .Distinct()
            .OrderByDescending(meta => meta.Depth)
            .ToList();

        foreach (var victim in droppable)
        {
            fragments.RemoveAll(fragment => victim.Equals(fragment.Token));

            if (TotalBytes(fragments) <= budget)
            {
                return;
            }
        }

        // Pass 2: shrink elastic tokens, deepest first, each absorbing the remaining overshoot.
        var elastic = fragments
            .Where(fragment => fragment.Kind == FragmentKind.TokenValue
                && fragment.Token is { Elasticity: TokenElasticity.Elastic })
            .Select(fragment => fragment.Token!)
            .Distinct()
            .OrderByDescending(meta => meta.Depth)
            .ToList();

        var ellipsisBytes = GraphemeText.ByteCount(_options.Ellipsis);

        foreach (var meta in elastic)
        {
            var overshoot = TotalBytes(fragments) - budget;

            if (overshoot <= 0)
            {
                return;
            }

            // Re-find the value fragment: earlier passes may have moved or removed it.
            var index = fragments.FindIndex(fragment =>
                fragment.Kind == FragmentKind.TokenValue && meta.Equals(fragment.Token));

            if (index < 0)
            {
                continue;
            }

            var fragment = fragments[index];
            var valueBytes = GraphemeText.ByteCount(fragment.Text);
            var valueBudget = valueBytes - overshoot - ellipsisBytes;

            if (valueBudget <= 0)
            {
                // Nothing legible would remain: the token drops entirely, affixes included.
                fragments.RemoveAll(candidate => meta.Equals(candidate.Token));
                continue;
            }

            var shortened = GraphemeText.TrimToByteBudget(fragment.Text, valueBudget);
            fragments[index] = fragment with { Text = shortened };
            fragments.Insert(index + 1, new Fragment(_options.Ellipsis, FragmentKind.Inserted, fragment.Token));
        }

        // Anything still over is cut whole by Materialize's TruncateComponent backstop.
    }

    private static int TotalBytes(List<Fragment> fragments)
    {
        var total = 0;

        foreach (var fragment in fragments)
        {
            total += GraphemeText.ByteCount(fragment.Text);
        }

        return total;
    }

    private static bool IsSeparator(char symbol) => Array.IndexOf(SeparatorCharacters, symbol) >= 0;

    private static string CollapseRuns(string value)
    {
        // The surveyed cleanup collapses runs of one repeated separator ([- ._])\1+ — Sonarr
        // FileNameCleanupRegex (src/NzbDrone.Core/Organizer/FileNameBuilder.cs:74). Runs of mixed
        // separators are kept: " - " is deliberate template text.
        var builder = new StringBuilder(value.Length);

        foreach (var symbol in value)
        {
            if (builder.Length > 0 && IsSeparator(symbol) && builder[^1] == symbol && symbol != ' ')
            {
                continue;
            }

            if (builder.Length > 0 && symbol == ' ' && builder[^1] == ' ')
            {
                continue;
            }

            builder.Append(symbol);
        }

        return builder.ToString();
    }

    private enum FragmentKind
    {
        Literal = 0,
        TokenValue = 1,
        Inserted = 2,
    }

    private sealed record TokenMeta(string CanonicalName, TokenElasticity Elasticity, int Depth);

    private sealed record Fragment(string Text, FragmentKind Kind, TokenMeta? Token);
}
