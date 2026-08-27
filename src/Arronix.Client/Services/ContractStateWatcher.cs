using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;

namespace Arronix.Client.Services;

/// <summary>Reloads the installed contracts when the host says an extension changed state.</summary>
/// <remarks>
/// Only the next pass stops a withdrawn contract being served, and waiting for an operator would make that
/// an offer rather than a property of a connected tab. A filter and nothing else: it reads no manifest and
/// owns no ordering, asking the one view every caller shares.
/// </remarks>
internal sealed class ContractStateWatcher : IDisposable
{
    private readonly ContractView _view;
    private readonly EventStream _events;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractStateWatcher"/> class.
    /// </summary>
    /// <param name="view">The one view every caller shares.</param>
    /// <param name="events">The live connection carrying extension-state events.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractStateWatcher(ContractView view, EventStream events)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(events);

        _view = view;
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
    /// The one event kind that can add, remove or re-describe an installed package; reloading for the rest
    /// would be a poll. <see langword="async"/> <see langword="void"/> on purpose: the reload records every
    /// failure it contains, and discarding its task would drop the one class it contains nowhere.
    /// </remarks>
    private async void OnReceived(object? sender, EventEnvelope envelope)
    {
        if (envelope.Kind is EventKind.PluginStateChanged)
        {
            await _view.ReloadAsync();
        }
    }
}
