namespace Arronix.Client.Contracts;

/// <summary>What a view of the installed contracts shows, and the only way to change it.</summary>
/// <remarks>
/// One transaction, one commit, one signal. Callers resume in whatever order the scheduler chooses, so an
/// older transaction can arrive last and is discarded whole rather than landing on newer state.
/// </remarks>
internal sealed class ContractView
{
    private readonly ContractReloader _reloader;
    private readonly NewestWins _commits = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractView"/> class.
    /// </summary>
    /// <param name="reloader">The one transaction every caller shares.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reloader"/> is <see langword="null"/>.</exception>
    public ContractView(ContractReloader reloader)
    {
        ArgumentNullException.ThrowIfNull(reloader);
        _reloader = reloader;
    }

    /// <summary>Occurs when this view has committed something new to show.</summary>
    /// <remarks>Raised after <see cref="Snapshot"/> is the value it announces, never before.</remarks>
    public event EventHandler? Changed;

    /// <summary>Gets what the newest committed transaction observed.</summary>
    /// <remarks>
    /// Read off the one committed value rather than kept beside it. Deciding and publishing are a single
    /// compare-and-swap, so this can only ever move forward, whatever order callers resume in.
    /// </remarks>
    public ContractReloadResult Snapshot => _commits.Current.Result;

    /// <summary>Gets every subscriber that refused the last committed signal, in order.</summary>
    /// <remarks>
    /// Attached to the commit that raised the announcement, never to whichever one happens to stand when
    /// it finishes: a refusal happens after that subscriber was shown the value, so no announcement can
    /// carry the record of its own refusals.
    /// </remarks>
    public IReadOnlyList<ContractFailure> Refused => _commits.Current.Refused;

    /// <summary>Reloads the installation and shows the result.</summary>
    /// <param name="cancellationToken">Abandons the reload.</param>
    /// <returns>A task that completes once this view has committed or discarded what the reload produced.</returns>
    public Task ReloadAsync(CancellationToken cancellationToken = default)
        => CommitAsync(_reloader.ReloadAsync(cancellationToken));

    /// <summary>Discards this browser's stored contract bytes and shows the result.</summary>
    /// <param name="cancellationToken">Abandons the discard.</param>
    /// <returns>A task that completes once this view has committed or discarded what it produced.</returns>
    public Task DiscardStoredBytesAsync(CancellationToken cancellationToken = default)
        => CommitAsync(_reloader.DiscardStoredBytesAsync(cancellationToken));

    /// <summary>Commits one sealed transaction, unless a newer one already committed.</summary>
    /// <param name="transaction">The transaction whose record to commit.</param>
    /// <returns>A task that completes once that record has committed or been discarded.</returns>
    /// <remarks>
    /// Internal rather than private so a test can complete two transactions in either order. Which record
    /// wins must not depend on the scheduler, and proving that needs the two handed over deliberately.
    /// </remarks>
    internal async Task CommitAsync(Task<ContractReloadResult> transaction)
    {
        var result = await transaction.ConfigureAwait(false);

        if (_commits.Publish(result) is not { } published)
        {
            return;
        }

        var refused = Announcement.ToEachSubscriber(Changed, this, ContractFailureStage.Changed);

        // Attached to the commit that raised this announcement, and only while it still stands: a
        // subscriber is free to start a newer transaction while it is being told.
        if (refused.Count > 0)
        {
            _commits.Attach(published, refused);
        }
    }
}
