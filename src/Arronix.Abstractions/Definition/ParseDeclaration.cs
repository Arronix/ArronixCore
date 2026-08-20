
namespace Arronix.Abstractions.Definition;

/// <summary>
/// A kind's release-title grammar: how text reads into media-owned identity, coordinates and tags.
/// </summary>
/// <remarks>
/// <para>
/// The host executes only the grammar declared here: pre-substitution rewrites, title patterns, guards,
/// coordinate extraction and token tables. Representation packages interpret their own vocabulary after
/// this media-owned title reading; the host has no shared video, audio or document scanner.
/// </para>
/// <para>
/// Declared order is semantic everywhere in this section and is preserved byte-for-byte through
/// validation: the ordered tables are the algorithm, and no engine may sort them.
/// </para>
/// </remarks>
public sealed record ParseDeclaration
{
    /// <summary>
    /// Gets the kind's parameters for the host normalization chain: leading articles, stop words,
    /// transliterations, query-text rewrites.
    /// </summary>
    public NormalizationOptions Normalization { get; init; } = NormalizationOptions.Default;

    /// <summary>
    /// Gets the pre-substitutions applied to the normalized text before the pattern list runs, in
    /// declared order.
    /// </summary>
    public IReadOnlyList<RewriteRule> PreRewrites { get; init; } = [];

    /// <summary>
    /// Gets the ordered pattern list. The first pattern whose expression matches and whose guards pass
    /// produces the reading. Order is semantic.
    /// </summary>
    public required IReadOnlyList<TitlePattern> TitlePatterns { get; init; }

    /// <summary>
    /// Gets the named guard expressions referenced by rules and patterns, declared once each.
    /// </summary>
    public IReadOnlyList<GuardPattern> Guards { get; init; } = [];

    /// <summary>
    /// Gets the per-kind token tables for facts this media type's releases spell.
    /// </summary>
    public IReadOnlyList<TokenTable> TokenTables { get; init; } = [];

    /// <summary>
    /// Gets the identifiers of the kind's budgeted per-kind code escapes.
    /// </summary>
    public IReadOnlyList<string> EscapeIds { get; init; } = [];
}
