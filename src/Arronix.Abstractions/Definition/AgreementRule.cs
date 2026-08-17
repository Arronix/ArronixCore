using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One agreement a candidate entry must satisfy before a title-based match is accepted.
/// </summary>
/// <remarks>
/// The defense against a plausible key landing on the wrong entry: a reading's stated evidence must
/// agree with the entry it resolved to. Absence is configurable because a missing statement is common
/// and harmless where a contradicting one is neither.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record AgreementRule
{
    /// <summary>
    /// Gets the rule's identifier, for diagnostics.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Gets the path to the reading-side value, such as <c>"reading.TitleYear"</c>.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the entry-side paths the subject may agree with; agreement with any one satisfies the rule.
    /// </summary>
    public required IReadOnlyList<string> AgreesWith { get; init; }

    /// <summary>
    /// Gets a value indicating whether an absent subject counts as agreement.
    /// </summary>
    public bool AbsentAgrees { get; init; }

    /// <summary>
    /// Gets the least value at which the subject is treated as a real statement rather than noise.
    /// </summary>
    public double? MinimumValue { get; init; }
}
