#pragma warning disable ARX0017 // Wire contracts are experimental; the feed is made of them.

using System.Linq;
using Arronix.Abstractions.Wire;

namespace Arronix.Client.Services;

/// <summary>
/// The recent past, kept in memory so that a view opened after something happened can still show it.
/// </summary>
/// <remarks>
/// <para>
/// Live events arrive whether or not anything is listening, and a person who opens the activity view
/// after a job has finished should not be told nothing has happened. Holding a bounded window is the
/// smallest thing that fixes that without the client keeping a database.
/// </para>
/// <para>
/// Bounded on purpose. An unbounded log in a long-lived browser tab is a leak with a nice name, and the
/// server is the authority on anything older than the window anyway.
/// </para>
/// </remarks>
public sealed class ActivityLog : IDisposable
{
    private const int Capacity = 200;

    private readonly EventStream _events;
    private readonly LinkedList<EventEnvelope> _entries = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLog"/> class.
    /// </summary>
    /// <param name="events">The live connection whose events are recorded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <see langword="null"/>.</exception>
    public ActivityLog(EventStream events)
    {
        ArgumentNullException.ThrowIfNull(events);

        _events = events;
        _events.Received += OnReceived;
    }

    /// <summary>
    /// Occurs when something has been recorded.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets what has happened recently, most recent first.
    /// </summary>
    public IReadOnlyList<EventEnvelope> Entries => _entries.ToList();

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

    private void OnReceived(object? sender, EventEnvelope envelope)
    {
        _entries.AddFirst(envelope);

        while (_entries.Count > Capacity)
        {
            _entries.RemoveLast();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
