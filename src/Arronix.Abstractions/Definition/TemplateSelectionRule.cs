using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One condition row choosing among naming templates.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record TemplateSelectionRule
{
    /// <summary>
    /// Gets the row's identifier, for diagnostics.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Gets the predicate selecting the row, in the definition's closed vocabulary.
    /// </summary>
    public required TagPredicate When { get; init; }

    /// <summary>
    /// Gets the template slot chosen when the row applies. Null when the row only inserts a spine
    /// segment.
    /// </summary>
    public string? TemplateId { get; init; }

    /// <summary>
    /// Gets the template slot degraded to when the chosen template references a token with no value.
    /// </summary>
    public string? FallbackTemplateId { get; init; }

    /// <summary>
    /// Gets the optional folder-spine segment the row switches on, by the segment's bracketed name.
    /// </summary>
    public string? InsertSpineSegment { get; init; }
}
