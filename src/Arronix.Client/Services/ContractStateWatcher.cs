using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;

namespace Arronix.Client.Services;

/// <summary>
/// Re-reads the installed contracts when the host says an extension changed state.
/// </summary>
/// <remarks>
/// <para>
/// A browser cannot unload an assembly, so a tab holding a withdrawn contract keeps holding it. What it
/// must not do is keep serving it, and only the loader's next pass decides that. Waiting for an operator to
/// press a button would make refusing a withdrawn contract an offer rather than a property of a connected
/// tab.
/// </para>
/// <para>
/// A trigger, not a second reader: it fetches, validates and compares nothing. It calls the one method that
/// already reads the manifest, already proves it whole and is already serialized, so this client has one
/// description of an installation and one place that verifies it.
/// </para>
/// </remarks>
public sealed class ContractStateWatcher : IDisposable
{
    private readonly MediaContractLoader _contracts;
    private readonly EventStream _events;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractStateWatcher"/> class.
    /// </summary>
    /// <param name="contracts">The loader whose next pass decides what this page may still serve.</param>
    /// <param name="events">The live connection carrying extension-state events.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractStateWatcher(MediaContractLoader contracts, EventStream events)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(events);

        _contracts = contracts;
        _events = events;
        _events.Received += OnReceived;
    }

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
    /// The one event kind that can add, remove or re-describe an installed package. Re-reading for any of
    /// the others — item, job, queue, health and provider traffic — would be a poll dressed as a
    /// subscription.
    /// </remarks>
    private void OnReceived(object? sender, EventEnvelope envelope)
    {
        if (envelope.Kind is EventKind.PluginStateChanged)
        {
            _ = RecheckAsync();
        }
    }

    /// <remarks>
    /// Not awaited by anything: an event handler has no caller to return an installation to. The loader
    /// records every outcome it can describe in its own report, and a failure it could not describe is not
    /// worth taking a page down for.
    /// </remarks>
    private async Task RecheckAsync()
    {
        try
        {
            await _contracts.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Contained here, and reported through ContractLoadReport wherever the loader could name it.
        }
    }
}
