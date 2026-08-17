using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>How an axis's values relate to one another.</summary>
/// <remarks>
/// Polarity is a fact, not a preference: on a re-encode count, more generations is strictly less retained
/// information. Whether the user <i>wants</i> more of what an axis measures is policy, and a policy may
/// invert it for the person who prefers small files.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum AxisOrdering
{
    /// <summary>A greater value is more of what the axis measures. Resolution, dynamic range.</summary>
    Ascending = 0,

    /// <summary>A greater value is less of what the axis measures. Re-encode generations.</summary>
    Descending = 1,

    /// <summary>
    /// The values do not order. A container format, a distributor, a defect: membership is a fact,
    /// "more" is not.
    /// </summary>
    Unordered = 2,
}
