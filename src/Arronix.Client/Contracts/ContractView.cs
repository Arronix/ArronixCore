using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>What a view of the installed contracts shows, kept current as reloads happen.</summary>
/// <remarks>
/// Two notifications arrive per reload — the loader's read, then the reloader once it has swept — and each
/// refresh reads the store behind an await. Awaits do not complete in the order they started, so only the
/// newest refresh commits. A refresh nobody awaits records what it can rather than faulting a task nobody
/// observes; an unsound process is contained nowhere.
/// </remarks>
public sealed class ContractView : IDisposable
{
    private readonly MediaContractLoader _contracts;
    private readonly ContractStore _store;
    private readonly ContractReloader _reloader;
    private readonly NewestWins _refreshes = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractView"/> class.
    /// </summary>
    /// <param name="contracts">The loader whose report this view shows.</param>
    /// <param name="store">This browser's contract store, whose held addresses this view counts.</param>
    /// <param name="reloader">The one reloader every caller shares.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractView(MediaContractLoader contracts, ContractStore store, ContractReloader reloader)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reloader);

        _contracts = contracts;
        _store = store;
        _reloader = reloader;

        _contracts.ReportChanged += OnChanged;
        _reloader.Completed += OnChanged;
    }

    /// <summary>Occurs when this view has committed something new to show.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the last report this view committed, or <see langword="null"/> before the first.</summary>
    public ContractLoadReport? Report { get; private set; }

    /// <summary>Gets the content hashes this browser was holding when that report was committed.</summary>
    public IReadOnlyList<string> StoredKeys { get; private set; } = [];

    /// <summary>Gets why the last refresh failed, or <see langword="null"/> when it did not.</summary>
    public string? LastFailure { get; private set; }

    /// <summary>Gets why the last reload failed, or <see langword="null"/> when it did not.</summary>
    public string? LastReloadFailure => _reloader.LastFailure;

    /// <summary>Reloads the installation and shows the result.</summary>
    /// <param name="cancellationToken">Abandons the reload.</param>
    /// <returns>A task that completes once this view has committed what the reload produced.</returns>
    /// <remarks>The reload's own notification already refreshes; this one is awaitable.</remarks>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloader.ReloadAsync(cancellationToken).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>Re-reads what there is to show and commits it, unless a later refresh overtook this one.</summary>
    /// <returns>A task that completes when this refresh has committed or been discarded.</returns>
    /// <remarks>
    /// One observation, published as one. A report read at commit time beside keys read before an await
    /// describes two moments, and an overtaken refresh's failure would replace one already answered.
    /// </remarks>
    public async Task RefreshAsync()
    {
        var request = _refreshes.Request();
        IReadOnlyList<string> keys = [];
        ContractLoadReport? report = null;
        string? failure = null;

        try
        {
            keys = await _store.KeysAsync().ConfigureAwait(false);
            report = _contracts.Report;
        }
        catch (Exception thrown) when (!ProcessFailure.IsFatal(thrown))
        {
            failure = thrown.Message;
        }

        if (!_refreshes.IsCurrent(request))
        {
            return;
        }

        if (failure is null)
        {
            Report = report;
            StoredKeys = keys;
        }

        LastFailure = failure;

        if (Announcement.ToEachSubscriber(Changed, this) is { } refused)
        {
            LastFailure = refused;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _contracts.ReportChanged -= OnChanged;
        _reloader.Completed -= OnChanged;
    }

    /// <remarks>
    /// <see langword="async"/> <see langword="void"/> on purpose: it observes the refresh rather than
    /// dropping its task, so an unsound process is rethrown on the captured context instead of vanishing.
    /// </remarks>
    private async void OnChanged(object? sender, EventArgs e) => await RefreshAsync();
}
