namespace Arronix.Common.Caching;

/// <summary>
/// Admits cache work while a namespace is open, and reports when the work that was admitted has finished.
/// </summary>
/// <remarks>
/// <para>
/// A cache entry produced by a plugin factory is a plugin object, and the closed generic cache instance
/// holding it is a plugin type. Removing the caches is therefore not enough on its own: a factory still
/// running holds both, and the collectible context cannot be unloaded while it does. This gate is the
/// thing that makes "the namespace is released" mean "and nothing is still producing values into it".
/// </para>
/// <para>
/// It is a counter rather than a lock because the operations it admits are awaited. Nothing here is
/// thread-affine and nothing is held across a resumption on another thread.
/// </para>
/// </remarks>
internal sealed class CacheOperationGate
{
    private readonly object _gate = new();
    private TaskCompletionSource? _drained;
    private int _admitted;
    private bool _closed;

    /// <summary>Gets a value indicating whether the gate has stopped admitting work.</summary>
    internal bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    /// <summary>Admits one operation, or reports that the namespace is closed.</summary>
    /// <param name="ticket">The admission, released when disposed.</param>
    /// <returns><see langword="false"/> when the namespace is closed to new work.</returns>
    internal bool TryEnter(out IDisposable? ticket)
    {
        lock (_gate)
        {
            if (_closed)
            {
                ticket = null;
                return false;
            }

            _admitted++;
        }

        ticket = new Ticket(this);
        return true;
    }

    /// <summary>Stops admitting work. Already-admitted work runs to completion.</summary>
    internal void Close()
    {
        TaskCompletionSource? completed = null;

        lock (_gate)
        {
            _closed = true;

            if (_admitted == 0)
            {
                completed = _drained;
                _drained = null;
            }
        }

        completed?.TrySetResult();
    }

    /// <summary>Waits for every admitted operation to finish. Closes the gate first.</summary>
    /// <returns>A task that completes when nothing is left running in this namespace.</returns>
    internal Task DrainAsync()
    {
        Close();

        lock (_gate)
        {
            if (_admitted == 0)
            {
                return Task.CompletedTask;
            }

            _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task;
        }
    }

    private void Exit()
    {
        TaskCompletionSource? completed = null;

        lock (_gate)
        {
            _admitted--;

            if (_admitted == 0 && _closed)
            {
                completed = _drained;
                _drained = null;
            }
        }

        completed?.TrySetResult();
    }

    private sealed class Ticket(CacheOperationGate owner) : IDisposable
    {
        private CacheOperationGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
