using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// The set of decisions an extension proposes, for a user to amend and commit.
/// </summary>
/// <remarks>
/// Produced by the extension, presented generically, edited by the user, posted back. The proposal is
/// where the extension's domain knowledge goes — one surveyed application computes it with an optimal
/// assignment algorithm — and the platform contributes only the amend-and-commit cycle.
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
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
/// One proposed decision.
/// </summary>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record WorkbenchRow
{
    /// <summary>
    /// Gets the identifier of this row, stable between the proposal and the commit.
    /// </summary>
    public required string RowId { get; init; }

    /// <summary>
    /// Gets the row's values, keyed by the column's <see cref="FieldDescriptor.FieldId"/>.
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
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record WorkbenchCommit(
    string WorkbenchId,
    IReadOnlyList<WorkbenchRow> Rows,
    IReadOnlyList<string> ExcludedRowIds);
