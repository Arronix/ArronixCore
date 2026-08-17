using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// How a kind's parsed readings resolve to catalog entries and units.
/// </summary>
/// <remarks>
/// Matching is a family of host strategies with declared parameters, not one vocabulary: the surveyed
/// kinds split between layered key lookup, per-release-kind coordinate resolution and assignment over
/// feature distances, and one operator chain for all of them would be an over-claim. This declaration
/// parameterizes the family; the strategies themselves are host code.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MatchDeclaration
{
    /// <summary>
    /// Gets the entry-resolution cascade parameters: identifier order, key layers, agreements,
    /// ambiguity policy.
    /// </summary>
    public required EntryResolution Entry { get; init; }

    /// <summary>
    /// Gets the per-release-kind unit resolution rules: which spaces, in what order, under what gates.
    /// </summary>
    public required IReadOnlyList<UnitResolutionRule> Units { get; init; }

    /// <summary>
    /// Gets the rows mapping match basis and provenance to a confidence.
    /// </summary>
    public required IReadOnlyList<ConfidenceRule> Confidence { get; init; }

    /// <summary>
    /// Gets the variant-choice parameters, when the shape has a variant level. Null otherwise.
    /// </summary>
    public VariantChoiceDeclaration? Variant { get; init; }
}
