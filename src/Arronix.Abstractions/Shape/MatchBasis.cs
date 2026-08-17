using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// What a match was made on. Categorical, and orthogonal to how confident the matcher is.
/// </summary>
/// <remarks>
/// The load-bearing distinction downstream decisions branch on is not the five-point confidence scale
/// but the category of evidence: an identifier match and an exact-title match may both be near-certain
/// and are still treated differently by import policy. The surveyed matchers all carry this and could
/// not report it through the contract until now.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum MatchBasis
{
    /// <summary>Nothing was matched, so there is no basis to report.</summary>
    None = 0,

    /// <summary>An external identifier resolved directly to the item.</summary>
    Identifier = 1,

    /// <summary>A title agreed and a declared agreement rule (such as a year) corroborated it.</summary>
    TitleWithYear = 2,

    /// <summary>A title agreed with no corroborating agreement rule.</summary>
    TitleOnly = 3,

    /// <summary>Coordinates addressed the item within an already-resolved entry.</summary>
    Coordinate = 4,

    /// <summary>The caller scoped the match to the entry and the text did not contradict it.</summary>
    Scope = 5,

    /// <summary>A person chose the item.</summary>
    Manual = 6
}
