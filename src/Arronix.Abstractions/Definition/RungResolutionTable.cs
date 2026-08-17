using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// The ordered decision table resolving tag evidence to a ladder rung.
/// </summary>
/// <remarks>
/// <para>
/// Rows are evaluated in declared order and the first row whose predicate holds wins. There is
/// deliberately no rule-selection mode here: last-occurrence semantics belong to the token scan that
/// produces the tags (<see cref="OccurrenceSelection"/>), because the rightmost token in a release name
/// varies per release while this table is fixed at authoring time — a selection mode on the table could
/// not express the difference.
/// </para>
/// <para>
/// Order is semantic and preserved byte-for-byte: pre-release before broadcast, weak signals last,
/// container fallbacks only when every row is silent.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record RungResolutionTable
{
    /// <summary>
    /// Gets the ordered rows. The first whose predicate holds wins.
    /// </summary>
    public required IReadOnlyList<RungRule> Rules { get; init; }

    /// <summary>
    /// Gets the container-extension fallbacks, consulted only when every rule is silent.
    /// </summary>
    public IReadOnlyList<ExtensionTierRule> ContainerFallbacks { get; init; } = [];

    /// <summary>
    /// Gets the tier assigned when nothing matched at all, by name.
    /// </summary>
    public required string UnknownTierId { get; init; }
}
