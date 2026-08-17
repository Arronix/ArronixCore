using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One row of a kind's ordered title-pattern list.
/// </summary>
/// <remarks>
/// The first pattern whose expression matches and whose guards pass claims the release. A capture that
/// names an undeclared coordinate component, an unknown guard, or a source the pattern cannot fire on is
/// a load failure — never a silent no-op.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record TitlePattern
{
    /// <summary>
    /// Gets the pattern's stable identifier, which flows to the reading it produces so corpus coverage
    /// can be reported per row.
    /// </summary>
    public required string PatternId { get; init; }

    /// <summary>
    /// Gets the regular expression, with named groups for every capture.
    /// </summary>
    public required string Regex { get; init; }

    /// <summary>
    /// Gets the text provenances this pattern may fire on. Empty means every provenance.
    /// </summary>
    public IReadOnlyList<MatchSource> Sources { get; init; } = [];

    /// <summary>
    /// Gets the named-group bindings: which output each capture lands in.
    /// </summary>
    public required IReadOnlyList<CaptureBinding> Captures { get; init; }

    /// <summary>
    /// Gets the guard references that must hold — or must not — for the pattern to claim the match.
    /// </summary>
    public IReadOnlyList<GuardRef> Guards { get; init; } = [];

    /// <summary>
    /// Gets how a multi-capture or range match fans out into units, when it does.
    /// </summary>
    public RangeExpansion? Expansion { get; init; }

    /// <summary>
    /// Gets how much of the hierarchy the pattern's reading claims.
    /// </summary>
    public AcquisitionScope? Scope { get; init; }
}
