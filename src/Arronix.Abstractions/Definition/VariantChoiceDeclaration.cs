using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// Parameters for choosing among sibling variants of one entry.
/// </summary>
/// <remarks>
/// The features themselves are host code, published as a named feature catalog — what each feature
/// computes is imperative aggregation the declaration deliberately does not reach. A kind declares which
/// catalog it uses and tunes the features by identifier: enablement, weight, threshold. The honest
/// consequence, priced rather than hidden: a novel distance feature costs a host release, not a
/// declaration edit.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record VariantChoiceDeclaration
{
    /// <summary>
    /// Gets the host feature catalog the choice runs on. Unknown identifiers are a load failure.
    /// </summary>
    public required string FeatureCatalogId { get; init; }

    /// <summary>
    /// Gets the per-feature tuning rows, in priority order. A row naming a feature the catalog does not
    /// publish is a load failure.
    /// </summary>
    public required IReadOnlyList<FeatureParameter> Features { get; init; }
}
