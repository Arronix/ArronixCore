
namespace Arronix.Abstractions.Definition;

/// <summary>
/// What entry resolution does when more than one entry survives the cascade.
/// </summary>
public enum AmbiguityPolicy
{
    /// <summary>The match is rejected with a reason naming the contenders.</summary>
    Reject = 0,

    /// <summary>The entry whose year evidence agrees best wins; a residual tie is rejected.</summary>
    TiebreakByYear = 1
}
