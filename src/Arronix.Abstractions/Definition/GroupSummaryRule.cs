using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// How groups on one grouping axis are summarized.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record GroupSummaryRule
{
    /// <summary>
    /// Gets the grouping axis the rule serves.
    /// </summary>
    public required string AxisId { get; init; }

    /// <summary>
    /// Gets the headline template over the axis's declared fields.
    /// </summary>
    public required string HeadlineTemplate { get; init; }

    /// <summary>
    /// Gets the summary field rows, in render order.
    /// </summary>
    public IReadOnlyList<SummaryFieldRule> Fields { get; init; } = [];

}
