using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>How two points of one family relate under one policy.</summary>
/// <remarks>
/// The axis that decided is not carried here. It is a rendering concern, and it reaches a reader through
/// <see cref="GrabDecision.Reason"/>, which is the one place a sentence about a decision is written.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum QualityJudgment
{
    /// <summary>The candidate is below the held file on the first axis that decided.</summary>
    Worse = 0,

    /// <summary>No axis in the precedence list decided, and no facet score separated them either.</summary>
    Same = 1,

    /// <summary>The candidate is above the held file on the first axis that decided.</summary>
    Better = 2,

    /// <summary>
    /// Reserved. No <see cref="AxisPreference"/> can produce this — every entry induces a total preorder
    /// — and it survives only for a bespoke family that declares an axis whose comparison is genuinely
    /// partial. <see cref="QualityPolicy.Decide"/> maps it to
    /// <see cref="GrabVerdict.EvidenceInsufficient"/>, never to a grab.
    /// </summary>
    Incomparable = 3,
}
