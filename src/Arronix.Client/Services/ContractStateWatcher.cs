using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Diagnostics;

namespace Arronix.Client.Services;

/// <summary>
/// Re-reads the installed contracts, and sheds the bytes they no longer name, when the host says an
/// extension changed state.
/// </summary>
/// <remarks>
/// <para>
/// Only the loader's next pass decides that a withdrawn contract stops being served. Waiting for an
/// operator to press a button would make that an offer rather than a property of a connected tab, and would
/// leave the store growing on the one path that runs with nobody present.
/// </para>
/// <para>
/// A trigger, not a second reader: it fetches, validates and compares nothing, and calls the one method
/// that already reads the manifest, proves it whole and is serialized.
/// </para>
/// </remarks>
public sealed class ContractStateWatcher : IDisposable
{
    private readonly MediaContractLoader _contracts;
    private readonly ContractStoreJanitor _janitor;
    private readonly EventStream _events;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractStateWatcher"/> class.
    /// </summary>
    /// <param name="contracts">The loader whose next pass decides what this page may still serve.</param>
    /// <param name="janitor">Where the bytes an installation no longer names are discarded.</param>
    /// <param name="events">The live connection carrying extension-state events.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractStateWatcher(
        MediaContractLoader contracts,
        ContractStoreJanitor janitor,
        EventStream events)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(janitor);
        ArgumentNullException.ThrowIfNull(events);

        _contracts = contracts;
        _janitor = janitor;
        _events = events;
        _events.Received += OnReceived;
    }

    /// <summary>Occurs when a re-check this watcher started has finished, successfully or not.</summary>
    /// <remarks>
    /// Raised after the sweep, which the loader's own <see cref="MediaContractLoader.ReportChanged"/>
    /// precedes: a consumer that refreshed on that notification alone would read the store before this
    /// re-check had emptied it, and nothing afterwards would say so.
    /// </remarks>
    public event EventHandler? Rechecked;

    /// <summary>Gets why the last re-check failed, or <see langword="null"/> when it did not.</summary>
    /// <remarks>
    /// Nothing awaits an event handler, so a failure here has nowhere to be returned. The loader names
    /// every outcome it can describe in its own report, which makes a value here a defect in this client
    /// rather than a fact about the installation.
    /// </remarks>
    public string? LastFailure { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.Received -= OnReceived;
    }

    /// <remarks>
    /// The one event kind that can add, remove or re-describe an installed package. Re-reading for item,
    /// job, queue, health or provider traffic would be a poll dressed as a subscription.
    /// </remarks>
    private void OnReceived(object? sender, EventEnvelope envelope)
    {
        if (envelope.Kind is EventKind.PluginStateChanged)
        {
            _ = RecheckAsync();
        }
    }

    private async Task RecheckAsync()
    {
        try
        {
            var report = await _contracts.LoadAsync().ConfigureAwait(false);

            // Bytes only, against a manifest that was proved whole. Residency is untouched.
            await _janitor.SweepAsync(report).ConfigureAwait(false);
            LastFailure = null;
            Rechecked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            // An unsound process is not contained here, and nothing else is contained quietly: the same
            // notification carries a failed re-check, because nothing awaits an event handler.
            LastFailure = failure.Message;
            Rechecked?.Invoke(this, EventArgs.Empty);
        }
    }
}
