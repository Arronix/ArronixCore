using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Declares one set of arguments a release source will accept, and the categories it will answer for.
/// </summary>
/// <remarks>
/// <para>
/// Note what is <b>not</b> here: no media-kind reference and no protocol-level search-mode string. A
/// source declares what it can be asked; a media kind declares what it needs to ask. Eligibility is the
/// intersection of the two, and neither side ever names the other's vocabulary — which is what lets one
/// source serve a media kind that did not exist when the source was written.
/// </para>
/// <para>
/// This is the problem the surveyed aggregator solves with one adapter per target application, each
/// hand-mapping one vocabulary onto another.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record SearchProfile
{
    /// <summary>
    /// Gets the identifier of this profile within its source.
    /// </summary>
    public required string ProfileId { get; init; }

    /// <summary>
    /// Gets the argument kinds the source accepts.
    /// </summary>
    public required IReadOnlyList<SearchTerm> Terms { get; init; }

    /// <summary>
    /// Gets the categories the source will answer for under this profile.
    /// </summary>
    public required IReadOnlyList<CategoryId> Categories { get; init; }

    /// <summary>
    /// Gets how each accepted term is spelled on the wire. The source's own business, declared so the
    /// host can report what it sent without knowing the protocol.
    /// </summary>
    public IReadOnlyList<SearchTermBinding> Bindings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the source accepts unstructured text alongside the structured
    /// arguments.
    /// </summary>
    public bool SupportsRawSearch { get; init; }
}

/// <summary>
/// How one search argument is spelled on a source's wire protocol.
/// </summary>
/// <param name="Term">The argument kind.</param>
/// <param name="ComponentId">The coordinate component, when the term is an ordinal.</param>
/// <param name="WireName">The parameter name the source expects.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct SearchTermBinding(SearchTerm Term, string? ComponentId, string WireName);
