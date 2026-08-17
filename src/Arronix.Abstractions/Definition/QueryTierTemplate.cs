using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One tier of a kind's query plan, as a template.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QueryTierTemplate
{
    /// <summary>
    /// Gets the tier's identifier.
    /// </summary>
    public required string TierId { get; init; }

    /// <summary>
    /// Gets the search kind the tier implements. Unknown identifiers are a load failure.
    /// </summary>
    public required string SearchKindId { get; init; }

    /// <summary>
    /// Gets the tier's position in the plan; lower runs first.
    /// </summary>
    public int Order { get; init; } = 1;

    /// <summary>
    /// Gets the origins the tier applies to. Empty means every origin.
    /// </summary>
    public IReadOnlyList<SearchOrigin> Origins { get; init; } = [];

    /// <summary>
    /// Gets the structured arguments, as templates over fields and identifiers.
    /// </summary>
    public IReadOnlyList<QueryArgument> Arguments { get; init; } = [];

    /// <summary>
    /// Gets the free-text template. Empty is legitimate for a sweep that names nothing.
    /// </summary>
    public string FreeTextTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Gets the fields that must have values for the tier to plan at all — the rule that keeps a bare
    /// title from becoming the worst query a kind can make.
    /// </summary>
    public IReadOnlyList<string> RequiredFields { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether one query is planned per alias spelling rather than one query
    /// carrying many.
    /// </summary>
    public bool FanOutPerAlias { get; init; }

    /// <summary>
    /// Gets a value indicating whether the planned queries carry the alias spellings for sources that
    /// use them.
    /// </summary>
    public bool CarryAliases { get; init; }
}
