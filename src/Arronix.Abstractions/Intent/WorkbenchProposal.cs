using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>A typed set of decisions proposed for one declared workbench.</summary>
/// <typeparam name="TRow">The row shape the workbench declared.</typeparam>
/// <remarks>
/// This is the semantic contract used by a typed producer and committer. The non-generic proposal below
/// is its current wire projection for clients that have not loaded the media definition assembly; it is
/// not an alternative field-bag domain model.
/// </remarks>
public sealed record WorkbenchProposal<TRow>
    where TRow : notnull
{
    /// <summary>Gets the workbench this proposal answers.</summary>
    public required string WorkbenchId { get; init; }

    /// <summary>Gets the proposed typed rows.</summary>
    public required IReadOnlyList<WorkbenchRow<TRow>> Rows { get; init; }

    /// <summary>Gets what is wrong with the proposal as a whole.</summary>
    public IReadOnlyList<ValidationFailure> Issues { get; init; } = [];
}

/// <summary>One typed proposed decision.</summary>
/// <typeparam name="TRow">The media/workflow-owned row value.</typeparam>
public sealed record WorkbenchRow<TRow>
    where TRow : notnull
{
    /// <summary>Gets the stable identity retained between proposal and commit.</summary>
    public required string RowId { get; init; }

    /// <summary>Gets the complete typed row value.</summary>
    public required TRow Value { get; init; }

    /// <summary>Gets how sure the producer is about its proposal.</summary>
    public MatchConfidence Confidence { get; init; }

    /// <summary>Gets whether the row participates unless the user excludes it.</summary>
    public bool IncludedByDefault { get; init; } = true;

    /// <summary>Gets what is wrong with this row.</summary>
    public IReadOnlyList<ValidationFailure> Issues { get; init; } = [];
}

/// <summary>The user's amended typed rows, committed together.</summary>
/// <typeparam name="TRow">The row shape the workbench declared.</typeparam>
/// <param name="WorkbenchId">The workbench being committed.</param>
/// <param name="Rows">The rows as the user left them.</param>
/// <param name="ExcludedRowIds">The rows left out of the commit.</param>
public sealed record WorkbenchCommit<TRow>(
    string WorkbenchId,
    IReadOnlyList<WorkbenchRow<TRow>> Rows,
    IReadOnlyList<string> ExcludedRowIds)
    where TRow : notnull;

/// <summary>
/// The wire projection of a set of decisions, for clients that have not loaded the typed row assembly.
/// </summary>
/// <remarks>
/// Produced from <see cref="WorkbenchProposal{TRow}"/> at the transport boundary. Domain producers and
/// committers use the generic form; this projection exists only to carry derived values over the current
/// kind-blind HTTP surface.
/// </remarks>
public sealed record WorkbenchProposal
{
    /// <summary>
    /// Gets the <see cref="WorkbenchDescriptor.WorkbenchId"/> this proposal answers.
    /// </summary>
    public required string WorkbenchId { get; init; }

    /// <summary>
    /// Gets the proposed rows, in the order they should be presented.
    /// </summary>
    public required IReadOnlyList<WorkbenchRow> Rows { get; init; }

    /// <summary>
    /// Gets what is wrong with the proposal as a whole.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Issues { get; init; } = [];
}

/// <summary>
/// The wire projection of one proposed decision.
/// </summary>
public sealed record WorkbenchRow
{
    /// <summary>
    /// Gets the identifier of this row, stable between the proposal and the commit.
    /// </summary>
    public required string RowId { get; init; }

    /// <summary>
    /// Gets the row's projected values, keyed by the column's <see cref="FieldDescriptor.FieldId"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, FieldValue> Values { get; init; }

    /// <summary>
    /// Gets how sure the extension is about its own proposal for this row. Reuses the stable confidence
    /// vocabulary, so a low-confidence proposal can be surfaced for a decision rather than applied
    /// silently.
    /// </summary>
    public MatchConfidence Confidence { get; init; }

    /// <summary>
    /// Gets a value indicating whether the row is part of the commit unless the user says otherwise.
    /// </summary>
    public bool IncludedByDefault { get; init; } = true;

    /// <summary>
    /// Gets what is wrong with this row.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Issues { get; init; } = [];
}

/// <summary>
/// The user's amended decisions, posted back to be applied together.
/// </summary>
/// <param name="WorkbenchId">The <see cref="WorkbenchDescriptor.WorkbenchId"/> being committed.</param>
/// <param name="Rows">The rows as the user left them.</param>
/// <param name="ExcludedRowIds">The rows the user removed from the commit.</param>
public sealed record WorkbenchCommit(
    string WorkbenchId,
    IReadOnlyList<WorkbenchRow> Rows,
    IReadOnlyList<string> ExcludedRowIds);
