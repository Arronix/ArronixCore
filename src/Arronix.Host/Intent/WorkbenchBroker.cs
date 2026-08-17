using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Media;
using Arronix.Host.Scheduling;

// Intent, provider, wire and error contracts are experimental; a working surface names all of them.
#pragma warning disable ARX0003
#pragma warning disable ARX0013
#pragma warning disable ARX0015
#pragma warning disable ARX0016
#pragma warning disable ARX0017

namespace Arronix.Host.Intent;

/// <summary>
/// Runs the declared editable grid that is the escape hatch for work a fixed vocabulary cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Assigning several loose files to several units, with the extension proposing an assignment the user then
/// corrects, is a daily workflow in every surveyed application — not an exotic one. Expressing it as "an
/// action with a collection parameter of identifier triples" is not a user interface, and saying so is
/// better than shipping a vocabulary that cannot do the job.
/// </para>
/// <para>
/// So the extension proposes rows, a consumer renders them generically as a table, the user edits the
/// editable columns, and the edited rows come back. Nothing in the exchange names a control: a command-line
/// tool prints a table and takes row edits by number, a screen reader gets a real table with headers, and a
/// graphical consumer renders one grid component that serves every media kind.
/// </para>
/// <para>
/// The broker's own job is narrow and entirely about refusing what should not get through: that the surface
/// was declared, that the rows coming back are rows that went out, and that a commit does not carry
/// identifiers the proposal never mentioned.
/// </para>
/// </remarks>
/// <param name="kinds">The media-kind registry.</param>
/// <param name="intent">The declared surfaces.</param>
public sealed class WorkbenchBroker(IMediaKindRegistry kinds, IIntentRegistry intent)
{
    private readonly IMediaKindRegistry _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
    private readonly IIntentRegistry _intent = intent ?? throw new ArgumentNullException(nameof(intent));

    /// <summary>
    /// Asks an extension for its proposal.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">Which declared surface.</param>
    /// <param name="inputs">What the consumer collected before asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proposal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArronixException">The media kind declares no such surface, or a required input is missing.</exception>
    public async Task<WorkbenchProposal> ProposeAsync(
        MediaKindId kind,
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var descriptor = Require(kind, workbenchId);
        var registered = _kinds.Require(kind);

        var missing = descriptor.Inputs
            .Where(input => input.Required && !inputs.ContainsKey(input.ParameterId))
            .Select(input => input.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"'{descriptor.Name}' needs {string.Join(", ", missing)} before it can propose anything.");
        }

        var proposal = await registered.Items
            .ProposeAsync(workbenchId, inputs, cancellationToken)
            .ConfigureAwait(false);

        return proposal;
    }

    /// <summary>
    /// Sends the user's edited rows back to the extension.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="commit">The edited rows and the rows the user excluded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArronixException">
    /// The media kind declares no such surface, the commit carries values for a column that is not editable,
    /// or it excludes rows on a surface that does not allow exclusion.
    /// </exception>
    public async Task<ActionResult> CommitAsync(
        MediaKindId kind,
        WorkbenchCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        var descriptor = Require(kind, commit.WorkbenchId);
        var registered = _kinds.Require(kind);
        var failures = Validate(descriptor, commit);

        if (failures.Count > 0)
        {
            return new ActionResult(
                false,
                CorrelationContext.Current,
                $"'{descriptor.Name}' cannot be applied as submitted.",
                new ValidationOutcome { Failures = failures });
        }

        return await registered.Items.CommitAsync(commit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks an extension for the values a row-scoped option set offers.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">Which declared surface.</param>
    /// <param name="optionSourceId">Which option set.</param>
    /// <param name="rowId">The row the values are for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The values.</returns>
    /// <exception cref="ArronixException">The media kind declares no such surface or option set.</exception>
    /// <remarks>
    /// Row-scoped because the legal choices for one row are not the legal choices for another: which units a
    /// file may be assigned to depends on the file. A surface-wide option set would offer every choice on
    /// every row and push the filtering onto the person doing the work.
    /// </remarks>
    public async Task<IReadOnlyList<FacetValue>> OptionsAsync(
        MediaKindId kind,
        string workbenchId,
        string optionSourceId,
        string rowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);

        var descriptor = Require(kind, workbenchId);

        if (!descriptor.Columns.Any(column =>
            string.Equals(column.OptionSourceId, optionSourceId, StringComparison.Ordinal)))
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"'{descriptor.Name}' declares no option set '{optionSourceId}'.");
        }

        var registered = _kinds.Require(kind);

        // The option set is asked for through the same proposal call, scoped to one row. That keeps the
        // extension-side seam to the two methods it already implements rather than adding a third.
        var proposal = await registered.Items
            .ProposeAsync(
                workbenchId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["optionSourceId"] = optionSourceId,
                    ["rowId"] = rowId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. proposal.Rows
                .Where(row => string.Equals(row.RowId, rowId, StringComparison.Ordinal))
                .SelectMany(row => row.Values)
                .Where(value => value.Value.Text is not null)
                .Select(value => new FacetValue(value.Value.Text!, value.Key)),
        ];
    }

    [SuppressMessage(
        "Design",
        "CA1002:Do not expose generic lists",
        Justification = "The list is local and returned as a read-only interface to the one caller in this file.")]
    private static IReadOnlyList<ValidationFailure> Validate(WorkbenchDescriptor descriptor, WorkbenchCommit commit)
    {
        var failures = new List<ValidationFailure>();
        var editable = descriptor.Columns
            .Where(column => column.Editable)
            .Select(column => column.Field.FieldId)
            .ToHashSet(StringComparer.Ordinal);

        var known = descriptor.Columns
            .Select(column => column.Field.FieldId)
            .ToHashSet(StringComparer.Ordinal);

        if (!descriptor.AllowsRowExclusion && commit.ExcludedRowIds.Count > 0)
        {
            failures.Add(new ValidationFailure(
                null,
                $"'{descriptor.Name}' applies to every row; rows cannot be left out.",
                ValidationSeverity.Error,
                CoreErrorCode.InvalidConfiguration));
        }

        var rowIds = commit.Rows.Select(row => row.RowId).ToHashSet(StringComparer.Ordinal);

        foreach (var excluded in commit.ExcludedRowIds.Where(id => !rowIds.Contains(id)))
        {
            failures.Add(new ValidationFailure(
                null,
                $"Row '{excluded}' was left out but is not one of the rows submitted.",
                ValidationSeverity.Error,
                CoreErrorCode.InvalidConfiguration));
        }

        foreach (var row in commit.Rows)
        {
            foreach (var fieldId in row.Values.Keys)
            {
                if (!known.Contains(fieldId))
                {
                    failures.Add(new ValidationFailure(
                        fieldId,
                        $"'{descriptor.Name}' has no column '{fieldId}'.",
                        ValidationSeverity.Error,
                        CoreErrorCode.InvalidConfiguration));
                    continue;
                }

                // A read-only column is a projection of what the extension computed. Accepting a value for
                // one would turn a declared editable grid into an arbitrary write against extension state,
                // which is precisely the thing the declared vocabulary exists to avoid.
                if (!editable.Contains(fieldId))
                {
                    failures.Add(new ValidationFailure(
                        fieldId,
                        $"Column '{fieldId}' of '{descriptor.Name}' is not editable.",
                        ValidationSeverity.Error,
                        CoreErrorCode.InvalidConfiguration));
                }
            }
        }

        return failures;
    }

    private WorkbenchDescriptor Require(MediaKindId kind, string workbenchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbenchId);

        return _intent.FindWorkbench(kind, workbenchId)
            ?? throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                $"Media kind '{kind}' declares no working surface '{workbenchId}'.");
    }
}
