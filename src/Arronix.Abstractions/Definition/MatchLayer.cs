using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One key layer of the entry-resolution cascade.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MatchLayer
{
    /// <summary>
    /// Gets the layer's identifier.
    /// </summary>
    public required string LayerId { get; init; }

    /// <summary>
    /// Gets the key template over fields, grouping-axis fields and coordinate components, such as
    /// <c>"{title}"</c> or a group-synthesized <c>"{collection}{position}{title}"</c>.
    /// </summary>
    public required string KeyTemplate { get; init; }

    /// <summary>
    /// Gets the host normalizer the derived key runs through. Unknown identifiers are a load failure.
    /// </summary>
    public required string NormalizerId { get; init; }

    /// <summary>
    /// Gets the host expanders that multiply the key into accepted variants, in order.
    /// </summary>
    public IReadOnlyList<string> ExpanderIds { get; init; } = [];

    /// <summary>
    /// Gets the coordinate space preferred for unit resolution when this layer produced the match.
    /// Exists because which spelling matched can carry numbering information: an entry matched through a
    /// community alias should try that community's numbering space first, and per-alias mappings are
    /// unreachable without an on-match hint.
    /// </summary>
    public string? PreferSpaceId { get; init; }
}
