using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// A kind's search templates: what queries an acquisition turns into.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QueryDeclaration
{
    /// <summary>
    /// Gets the ordered query tiers per search kind. Earlier tiers are identifier and coordinate
    /// arguments; later tiers are text with aliases.
    /// </summary>
    public required IReadOnlyList<QueryTierTemplate> Tiers { get; init; }

    /// <summary>
    /// Gets the alias templates: alternative spellings of the same subject, most canonical first.
    /// </summary>
    public IReadOnlyList<AliasTemplate> Aliases { get; init; } = [];

    /// <summary>
    /// Gets how coordinates are spelled in the community's release grammar. Shared with naming's
    /// multi-unit styles by identifier.
    /// </summary>
    public required CoordinateGrammar Grammar { get; init; }

    /// <summary>
    /// Gets the result limits by search origin.
    /// </summary>
    public IReadOnlyList<OriginLimit> Limits { get; init; } = [];

    /// <summary>
    /// Gets the credited-name substitutions the community's grammar uses.
    /// </summary>
    public IReadOnlyList<CreditSubstitution> Substitutions { get; init; } = [];
}
