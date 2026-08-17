using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// One per-file, kind-meaningful fact a format family declares.
/// </summary>
/// <remarks>
/// The declaration a field list cannot carry: the fact belongs to a file rather than an item — two files
/// of one item may differ in it — and to one kind rather than the platform, so neither the level's
/// fields nor the host-global probe vocabulary can own it. Naming-token derivation reads this row to
/// mint the corresponding template token.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record TechnicalFacet
{
    /// <summary>
    /// Gets the identifier file facts and derived tokens reference this facet by.
    /// </summary>
    public required string FacetId { get; init; }

    /// <summary>
    /// Gets the facet's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets how rendered values of the facet are cased.
    /// </summary>
    public TechnicalFacetCase TitleCase { get; init; } = TechnicalFacetCase.AsWritten;

    /// <summary>
    /// Gets the words exempt from the casing rule and rendered exactly as listed.
    /// </summary>
    public IReadOnlyList<string> CaseExceptions { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether ordinal suffixes stay lower-case under title casing, so a
    /// twenty-fifth reads naturally rather than mechanically.
    /// </summary>
    public bool OrdinalSuffixesLowerCase { get; init; }
}
