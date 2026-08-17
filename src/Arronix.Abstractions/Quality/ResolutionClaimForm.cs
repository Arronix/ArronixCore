using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>How a release stated its vertical resolution, most specific first.</summary>
/// <remarks>
/// Specificity is an ordering over <i>forms of statement</i>, not over values. When one source says two
/// things at once — a title carrying both an explicit line count and a marketing name — the most specific
/// form wins, and among equally specific forms the lowest claim wins. The two failure directions are not
/// symmetric: a missed claim leaves a release ranked low, and a false claim promotes junk past everything
/// the user asked for.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ResolutionClaimForm
{
    /// <summary>An explicit line count.</summary>
    LineCount = 0,

    /// <summary>An explicit raster, width by height.</summary>
    Raster = 1,

    /// <summary>A marketing name.</summary>
    MarketingName = 2,

    /// <summary>Inferred from a container or a naming convention.</summary>
    Inferred = 3,
}
