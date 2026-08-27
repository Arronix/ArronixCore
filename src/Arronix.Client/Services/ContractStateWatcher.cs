using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;

namespace Arronix.Client.Services;

/// <summary>
/// Reloads the installed contracts when the host says an extension changed state.
/// </summary>
/// <remarks>
/// <para>
/// Only the next pass decides that a withdrawn contract stops being served. Waiting for an operator to
/// press a button would make that an offer rather than a property of a connected tab, and would leave the
/// store growing on the one path that runs with nobody present.
/// </para>
/// <para>
/// A filter, and nothing else. It reads no manifest and owns no ordering: it asks the one reloader every
/// caller shares, so an event and an operator cannot run two overlapping transactions.
/// </para>
/// </remarks>
public sealed class ContractStateWatcher : IDisposable
{
    private readonly ContractReloader _reloader;
    private readonly EventStream _events;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractStateWatcher"/> class.
    /// </summary>
    /// <param name="reloader">The one reloader every caller shares.</param>
    /// <param name="events">The live connection carrying extension-state events.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractStateWatcher(ContractReloader reloader, EventStream events)
    {
        ArgumentNullException.ThrowIfNull(reloader);
        ArgumentNullException.ThrowIfNull(events);

        _reloader = reloader;
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
    /// The one event kind that can add, remove or re-describe an installed package. Reloading for item,
    /// job, queue, health or provider traffic would be a poll dressed as a subscription. The reload is not
    /// awaited — an event handler has no caller to return an installation to — and contains everything
    /// except an unsound process itself.
    /// </remarks>
    private void OnReceived(object? sender, EventEnvelope envelope)
    {
        if (envelope.Kind is EventKind.PluginStateChanged)
        {
            _ = _reloader.ReloadAsync();
        }
    }
}
