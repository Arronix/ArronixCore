namespace Arronix.Plugins.Registry;

/// <summary>
/// Makes one extension-publication boundary observable as one change across plugin and host registries.
/// </summary>
/// <remarks>
/// Candidate derivation and extension activation happen before this gate is entered. The write lease is
/// reserved for publishing already-built values, so third-party code never runs while readers are stopped.
/// Registries take read leases only long enough to copy or locate their own state and return snapshots.
/// </remarks>
public sealed class PluginPublicationGate
{
    private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.SupportsRecursion);

    /// <summary>Stops publication while a registry takes a stable snapshot.</summary>
    /// <returns>A lease which releases the read lock when disposed.</returns>
    public IDisposable EnterRead()
    {
        _gate.EnterReadLock();
        return new Lease(_gate, write: false);
    }

    /// <summary>Stops readers and other publishers while one complete attempt is published.</summary>
    /// <returns>A lease which releases the write lock when disposed.</returns>
    public IDisposable EnterWrite()
    {
        _gate.EnterWriteLock();
        return new Lease(_gate, write: true);
    }

    private sealed class Lease(ReaderWriterLockSlim gate, bool write) : IDisposable
    {
        private ReaderWriterLockSlim? _gate = gate;

        public void Dispose()
        {
            var held = Interlocked.Exchange(ref _gate, null);
            if (held is null)
            {
                return;
            }

            if (write)
            {
                held.ExitWriteLock();
            }
            else
            {
                held.ExitReadLock();
            }
        }
    }
}
