using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One default-resolution row: what evidence is assumed when the release stated none.
/// </summary>
/// <remarks>
/// Rows apply in declared order and every row whose predicate holds applies its assumptions; the
/// predicate is the same closed vocabulary as everywhere else in the definition, never an expression
/// string of its own.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record TierDefault
{
    /// <summary>
    /// Gets the predicate selecting the row.
    /// </summary>
    public required TagPredicate When { get; init; }

    /// <summary>
    /// Gets the resolution assumed when the release stated none. Null asserts nothing.
    /// </summary>
    public int? Resolution { get; init; }

    /// <summary>
    /// Gets the source group assumed when the release stated none. Null asserts nothing.
    /// </summary>
    public string? SourceGroup { get; init; }

    /// <summary>
    /// Gets a value indicating whether a stated resolution is ignored outright — the pre-release
    /// sources that have no resolution axis at all.
    /// </summary>
    public bool IgnoreStatedResolution { get; init; }
}
