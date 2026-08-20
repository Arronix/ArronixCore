
namespace Arronix.Abstractions.Definition;

/// <summary>
/// The identifier normalizations a catalog declaration may invoke.
/// </summary>
public enum IdRuleKind
{
    /// <summary>Canonicalize by restoring a prefix and zero-padding the numeric part.</summary>
    PrefixPad = 0,

    /// <summary>Extract the identifier from a pasted address matching the declared pattern.</summary>
    UrlSegment = 1,

    /// <summary>Recognize a user-typed prefix form such as a scheme name and a colon.</summary>
    TypedPrefix = 2,

    /// <summary>Split a trailing year off a typed lookup, within the declared bounds.</summary>
    TrailingYearSplit = 3
}
