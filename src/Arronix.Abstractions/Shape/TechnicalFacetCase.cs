using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// How a technical facet's rendered values are cased.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum TechnicalFacetCase
{
    /// <summary>Values are rendered exactly as stored.</summary>
    AsWritten = 0,

    /// <summary>Values are title-cased, except for the facet's declared case exceptions.</summary>
    TitleCaseWithExceptions = 1
}
