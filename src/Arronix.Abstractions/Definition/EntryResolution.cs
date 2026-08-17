using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// Parameters of the generic entry-resolution cascade.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record EntryResolution
{
    /// <summary>
    /// Gets the external-identifier schemes in precedence order: when a source reports several, the
    /// earliest listed is trusted first.
    /// </summary>
    public required IReadOnlyList<string> IdentifierOrder { get; init; }

    /// <summary>
    /// Gets a value indicating whether a caller-supplied scope replaces the catalog-wide search: text
    /// that disagrees with the scoped entry is a rejection, never a match against something else.
    /// </summary>
    public bool ScopeReplacesSearch { get; init; } = true;

    /// <summary>
    /// Gets the ordered key layers. Each layer derives a lookup key from a template over fields,
    /// grouping-axis fields and coordinate components, through a named host normalizer. Layering is what
    /// stops an alternative spelling of one entry from outranking the actual title of another.
    /// </summary>
    public required IReadOnlyList<MatchLayer> Layers { get; init; }

    /// <summary>
    /// Gets the agreement rules applied after every layer and on every identifier resolution.
    /// </summary>
    public IReadOnlyList<AgreementRule> Agreements { get; init; } = [];

    /// <summary>
    /// Gets what happens when more than one entry survives the cascade.
    /// </summary>
    public AmbiguityPolicy Ambiguity { get; init; } = AmbiguityPolicy.Reject;
}
