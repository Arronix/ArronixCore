
namespace Arronix.Abstractions.Definition;

/// <summary>
/// The comparisons a predicate atom may apply.
/// </summary>
/// <remarks>
/// This enum is append-only, and every addition must be justified by a concrete declaration use case in
/// the design document that grows it. A definition using a member this host does not know is refused at
/// load, and the ordinal it used is what vocabulary negotiation reports.
/// </remarks>
public enum PredicateOp
{
    /// <summary>The subject equals the single value.</summary>
    Equals = 0,

    /// <summary>The subject equals one of the values.</summary>
    In = 1,

    /// <summary>The subject has a value at all. Takes no values.</summary>
    Present = 2,

    /// <summary>The subject is numerically at least the single value.</summary>
    GreaterOrEqual = 3,

    /// <summary>The subject is numerically at most the single value.</summary>
    LessOrEqual = 4,

    /// <summary>The named guard pattern matches. The subject is a <c>guard:</c> path; takes no values.</summary>
    GuardMatches = 5,

    /// <summary>The subject contains the single value as a substring.</summary>
    Contains = 6
}
