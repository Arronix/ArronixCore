using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What an axis's values are.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum AxisForm
{
    /// <summary>A closed set with a declared order.</summary>
    Ordinal = 0,

    /// <summary>An ordered quantity with a unit.</summary>
    Scalar = 1,

    /// <summary>
    /// A closed set with no order. Usable for requirements, for grouping, and for a facet score —
    /// never for precedence.
    /// </summary>
    Nominal = 2,
}
