using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One row of the rung-resolution table.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record RungRule
{
    /// <summary>
    /// Gets the row's stable identifier, for diagnostics and corpus coverage.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Gets the closed-vocabulary predicate over tag fields, categories, guards and captures that
    /// selects this row.
    /// </summary>
    public required TagPredicate When { get; init; }

    /// <summary>
    /// Gets the ladder tier the row resolves to, by name.
    /// </summary>
    public required string TierId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stated resolution read from the text is carried onto the
    /// resolved tier. Exists because a surveyed rung keeps its identity while adopting whatever
    /// resolution the release stated — a pure tier identifier could not compose the two.
    /// </summary>
    public bool CarryStatedResolution { get; init; }
}
