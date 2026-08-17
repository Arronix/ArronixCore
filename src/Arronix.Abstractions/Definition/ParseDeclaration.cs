using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// A kind's release models: how a release title reads into coordinates, and how token evidence resolves
/// to a ladder rung.
/// </summary>
/// <remarks>
/// <para>
/// The host's kind-agnostic layer — junk rejection, reversal, extension strip, site prefixes, the shared
/// source/resolution/codec/audio/revision/group/hash/language scanners, canonicalization — runs once per
/// release before any kind sees the text, and is host code with host data, not a declaration. This
/// section is exactly the per-kind residue: the title guess, coordinate extraction into declared spaces,
/// unit fan-out, pre-substitution rewrites and rung resolution.
/// </para>
/// <para>
/// Declared order is semantic everywhere in this section and is preserved byte-for-byte through
/// validation: the ordered tables are the algorithm, and no engine may sort them.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
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
    /// Gets the per-kind token tables layered over the host scanners: tokens the shared vocabulary does
    /// not know but this kind's releases spell.
    /// </summary>
    public IReadOnlyList<TokenTable> TokenTables { get; init; } = [];

    /// <summary>
    /// Gets the ordered decision table resolving tag evidence to a ladder rung, when the kind's families
    /// still rank their files by a ladder.
    /// </summary>
    /// <remarks>
    /// Absent for a kind whose families declare an axis-based quality model instead. The table's entire
    /// job is to collapse evidence into one of a fixed set of rung names — a lossy projection performed
    /// before anything can reason about the evidence — and every row of it is a small ranking decision
    /// sitting inside a declaration that is supposed to be about reading text. Where there is nothing to
    /// collapse evidence <i>to</i>, there is no table.
    /// </remarks>
    public RungResolutionTable? RungResolution { get; init; }

    /// <summary>
    /// Gets the identifiers of the kind's budgeted per-kind code escapes. Registering any reclassifies
    /// the plugin as hybrid, visibly; each escape must name the corpus cases the tables cannot express.
    /// </summary>
    public IReadOnlyList<string> EscapeIds { get; init; } = [];
}
