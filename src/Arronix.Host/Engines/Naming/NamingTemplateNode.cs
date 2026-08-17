namespace Arronix.Host.Engines.Naming;

/// <summary>
/// One node of a compiled naming template.
/// </summary>
/// <remarks>
/// The node forms mirror the grammar of <c>docs/design/naming-and-tokens.md</c> §3.2: literal text,
/// a token reference with in-brace affixes, an optional group that renders only when a token inside it
/// resolved, a span group that renders once per bound unit, and a path separator. The tree is produced
/// once by <see cref="NamingTemplateParser"/> and rendered many times.
/// </remarks>
internal abstract record NamingTemplateNode
{
    private NamingTemplateNode()
    {
    }

    /// <summary>Literal template text, emitted as written (after sanitization of the whole component).</summary>
    internal sealed record Literal(string Text) : NamingTemplateNode;

    /// <summary>A token reference.</summary>
    internal sealed record Token(NamingTokenRef Reference) : NamingTemplateNode;

    /// <summary>An optional group: renders only when at least one token inside it resolved non-empty.</summary>
    internal sealed record Optional(IReadOnlyList<NamingTemplateNode> Children) : NamingTemplateNode;

    /// <summary>
    /// A span group: the head renders for the first unit, the tail for each further unit. With
    /// <paramref name="RangeOnly"/> the iteration collapses to the first and last units.
    /// </summary>
    internal sealed record Span(
        string? ComponentRef,
        bool RangeOnly,
        IReadOnlyList<NamingTemplateNode> Head,
        IReadOnlyList<NamingTemplateNode> Tail) : NamingTemplateNode;

    /// <summary>A path separator: the boundary between two rendered components.</summary>
    internal sealed record Separator : NamingTemplateNode
    {
        internal static Separator Instance { get; } = new();
    }
}

/// <summary>
/// A parsed <c>{…}</c> token reference: affixes, the canonical name, modifiers and the format spec.
/// </summary>
/// <remarks>
/// The affixes are the surveyed in-brace conditional idiom (<c>{ (PartNumber)}</c>): they are emitted
/// only when the token resolves non-empty, so a missing value takes its decoration with it.
/// </remarks>
internal sealed record NamingTokenRef
{
    /// <summary>Gets the token name exactly as written in the template.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the lower-cased, separator-stripped lookup key.</summary>
    public required string CanonicalName { get; init; }

    /// <summary>Gets the affix emitted before the value when the value is non-empty.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Gets the affix emitted after the value when the value is non-empty.</summary>
    public string Suffix { get; init; } = string.Empty;

    /// <summary>Gets the modifiers, applied left to right.</summary>
    public IReadOnlyList<NamingModifier> Modifiers { get; init; } = [];

    /// <summary>Gets the zero-padding width for numeric tokens, when one was written.</summary>
    public int? PadWidth { get; init; }

    /// <summary>
    /// Gets the grapheme cap for text tokens, when one was written. Positive keeps the head, negative
    /// keeps the tail. A capped token is pinned: it leaves the elastic pool (§6.5(b)).
    /// </summary>
    public int? GraphemeCap { get; init; }
}
