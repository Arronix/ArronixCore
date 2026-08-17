using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// The naming data a kind still declares once tokens are derived from its shape.
/// </summary>
/// <remarks>
/// Tokens themselves are derived — level fields, grouping axes, identifier schemes, file facts,
/// technical facets — and the template grammar, elision, truncation, sanitization and casing engine are
/// host-owned. This section holds only what derivation cannot know: the default templates, the
/// condition rows that choose among them, the multi-unit joining styles, the folder spine and the token
/// fallbacks.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record NamingDeclaration
{
    /// <summary>
    /// Gets the declaration a kind starts from: a single-segment spine and no templates of its own.
    /// </summary>
    public static NamingDeclaration Default { get; } = new() { FolderSpine = "{root}/{folder}" };

    /// <summary>
    /// Gets the default template text per slot identifier — <c>"file"</c>, <c>"folder"</c>, per-level
    /// slots.
    /// </summary>
    public IReadOnlyDictionary<string, string> DefaultTemplates { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets the condition rows choosing among templates, in declared order.
    /// </summary>
    public IReadOnlyList<TemplateSelectionRule> Selection { get; init; } = [];

    /// <summary>
    /// Gets the styles a name joining several units may use.
    /// </summary>
    public IReadOnlyList<MultiUnitStyle> MultiUnitStyles { get; init; } = [];

    /// <summary>
    /// Gets the folder spine: the fixed skeleton of segments a library path is assembled from, with
    /// optional segments bracketed.
    /// </summary>
    public required string FolderSpine { get; init; }

    /// <summary>
    /// Gets the token fallback rows: where a token's value comes from when its primary source is empty.
    /// </summary>
    public IReadOnlyList<TokenFallbackRule> Fallbacks { get; init; } = [];
}
