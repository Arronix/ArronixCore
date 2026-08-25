namespace Arronix.Common.Lifetimes;

/// <summary>
/// Admits work while it is open, and reports when the work it admitted has finished.
/// </summary>
/// <remarks>
/// The primitive behind "stop admitting, then wait for what is already running". A counter rather than a
/// lock, because the admitted work is awaited: nothing here is thread-affine and an admission may be held
/// across a resumption on another thread.
/// </remarks>
internal sealed class QuiescenceGate
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

    /// <summary>Gets how many admitted operations are still running.</summary>
    internal int Admitted
    {
        get
        {
            lock (_gate)
            {
                return _admitted;
            }
        }
    }

    /// <summary>Admits one operation, or reports that the gate is closed.</summary>
    /// <param name="ticket">The admission, released when disposed.</param>
    /// <returns><see langword="false"/> when the gate admits no further work.</returns>
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

    /// <summary>Closes the gate, then waits for every admitted operation to finish.</summary>
    /// <returns>A task that completes when nothing admitted is still running.</returns>
    /// <remarks>Closing is part of the operation: a drain over an open gate need never end.</remarks>
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

    /// <summary>One admission. Releasing twice releases once.</summary>
    private sealed class Ticket(QuiescenceGate owner) : IDisposable
    {
        private QuiescenceGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
