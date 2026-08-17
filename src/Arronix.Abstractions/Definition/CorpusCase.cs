using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One release-text case the definition ships as its own parity fixture.
/// </summary>
/// <remarks>
/// A host upgrade changes engine behavior for every kind at once, so a definition states the readings it
/// depends on and the host's parity gate keeps them green across engine upgrades. Expectations are
/// asserted only where stated; a case may pin the pattern, the tier, the title, or any combination.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CorpusCase
{
    /// <summary>
    /// Gets the case's identifier.
    /// </summary>
    public required string CaseId { get; init; }

    /// <summary>
    /// Gets the release text as it would arrive.
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// Gets the provenance the text is presented under.
    /// </summary>
    public MatchSource Source { get; init; } = MatchSource.ReleaseName;

    /// <summary>
    /// Gets the pattern expected to claim the text, by identifier, when the case pins one.
    /// </summary>
    public string? ExpectedPatternId { get; init; }

    /// <summary>
    /// Gets the ladder tier expected to resolve, by name, when the case pins one.
    /// </summary>
    public string? ExpectedTierId { get; init; }

    /// <summary>
    /// Gets the title text expected to be read, when the case pins one.
    /// </summary>
    public string? ExpectedTitle { get; init; }
}
