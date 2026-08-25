using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;

namespace Arronix.Host.Media;

/// <summary>
/// Runs a media kind's item-source operations under the contributing extension's lease.
/// </summary>
/// <remarks>
/// <para>
/// The item source is extension code, so nothing outside the host may hold it. Callers name the kind and
/// the operation; this takes the ticket, keeps it for the whole call including everything the call awaits,
/// and releases it afterwards. A kind that has been withdrawn answers <see langword="null"/> rather than
/// running against an object teardown is disposing.
/// </para>
/// <para>
/// What comes back is re-materialized into host-owned collections <em>inside</em> the leased scope, so a
/// lazy or extension-defined collection is never enumerated, and never held, after the ticket is released.
/// </para>
/// </remarks>
public sealed class MediaItemBroker
{
    private readonly MediaKindRegistry _kinds;

    /// <summary>Initializes a new instance of the <see cref="MediaItemBroker"/> class.</summary>
    /// <param name="kinds">The registry the kind and its ticket come from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kinds"/> is <see langword="null"/>.</exception>
    public MediaItemBroker(MediaKindRegistry kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds;
    }

    /// <summary>Reads one page of items.</summary>
    /// <param name="kind">The kind whose item source is asked.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or <see langword="null"/> when the kind is no longer published.</returns>
    public Task<ItemPage?> QueryAsync(
        MediaKindId kind,
        ItemQuery query,
        CancellationToken cancellationToken = default)
        => RunAsync(
            kind,
            async (source, token) => PluginBoundary.Snapshot(
                await source.QueryAsync(query, token).ConfigureAwait(false)),
            cancellationToken);

    /// <summary>Reads one item.</summary>
    /// <param name="kind">The kind whose item source is asked.</param>
    /// <param name="reference">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when it or its kind is absent.</returns>
    /// <remarks>
    /// The two absences are one answer here on purpose: a caller asking for an item of a kind that has just
    /// been withdrawn wants the same 'not found' it would get for a removed item.
    /// </remarks>
    public Task<ItemView?> GetAsync(
        MediaKindId kind,
        MediaItemRef reference,
        CancellationToken cancellationToken = default)
        => RunAsync(
            kind,
            async (source, token) =>
            {
                var item = await source.GetAsync(reference, token).ConfigureAwait(false);
                return item is null ? null : PluginBoundary.Snapshot(item);
            },
            cancellationToken);

    /// <summary>Asks a workbench for a proposal.</summary>
    /// <param name="kind">The kind whose item source is asked.</param>
    /// <param name="workbenchId">The workbench.</param>
    /// <param name="inputs">The inputs it declared.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proposal, or <see langword="null"/> when the kind is no longer published.</returns>
    public Task<WorkbenchProposal?> ProposeAsync(
        MediaKindId kind,
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
        => RunAsync(
            kind,
            async (source, token) => PluginBoundary.Snapshot(
                await source.ProposeAsync(workbenchId, inputs, token).ConfigureAwait(false)),
            cancellationToken);

    /// <summary>Commits a workbench's rows.</summary>
    /// <param name="kind">The kind whose item source is asked.</param>
    /// <param name="commit">What to commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result, or <see langword="null"/> when the kind is no longer published.</returns>
    public Task<ActionResult?> CommitAsync(
        MediaKindId kind,
        WorkbenchCommit commit,
        CancellationToken cancellationToken = default)
        => RunAsync(
            kind,
            async (source, token) => PluginBoundary.Snapshot(
                await source.CommitAsync(commit, token).ConfigureAwait(false)),
            cancellationToken);

    /// <summary>
    /// Runs one host-owned operation against a kind's item source under a single ticket.
    /// </summary>
    /// <typeparam name="TResult">What the operation produces.</typeparam>
    /// <param name="kind">The kind whose item source is asked.</param>
    /// <param name="operation">The operation. It may make several calls; all share the one ticket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation's result.</returns>
    /// <exception cref="ArronixException">The kind is no longer published.</exception>
    /// <remarks>
    /// For a host caller whose decision spans more than one read and is an invariant rather than a query: a
    /// kind that disappears mid-decision is refused, never quietly allowed.
    /// </remarks>
    internal async ValueTask<TResult> RequireItemsAsync<TResult>(
        MediaKindId kind,
        Func<IMediaItemSource, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (!_kinds.TryLease(kind, out var leased))
        {
            throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                $"Media kind '{kind}' was withdrawn while an operation that depends on it was running.");
        }

        using var held = leased;
        return await operation(held.Value.Items, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult?> RunAsync<TResult>(
        MediaKindId kind,
        Func<IMediaItemSource, CancellationToken, Task<TResult?>> operation,
        CancellationToken cancellationToken)
        where TResult : class
    {
        if (!_kinds.TryLease(kind, out var leased))
        {
            return null;
        }

        using var held = leased;
        return await operation(held.Value.Items, cancellationToken).ConfigureAwait(false);
    }
}
