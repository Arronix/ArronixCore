using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Caching;
using Arronix.Common.Lifetimes;

namespace Arronix.Common.Caching;

/// <summary>
/// A group of caches the host can take back in one act.
/// </summary>
/// <remarks>
/// <para>
/// Release is a sequence: refuse new work, wait for everything already admitted, then drop the values and
/// delegates and remove the exact cache objects. Dropping references before the wait would make them
/// invisible rather than unreachable.
/// </para>
/// <para>
/// Resolving a cache is itself an admitted operation, so creation and release are linearizable: a caller
/// gets in before the close and is waited for, or is refused.
/// </para>
/// <para>An extension never sees this type; it holds the plain <see cref="ICacheProvider"/> face.</para>
/// </remarks>
internal sealed class CacheNamespace : ICacheNamespace
{
    private readonly CacheProvider _owner;
    private readonly QuiescenceGate _gate = new();
    private readonly ConcurrentDictionary<string, byte> _caches = new(StringComparer.Ordinal);
    private readonly object _releaseGate = new();
    private Task? _release;

    internal CacheNamespace(CacheProvider owner, string name)
    {
        _owner = owner;
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>True from the instant release begins, which is when its caches start refusing.</remarks>
    public bool IsReleased => _gate.IsClosed;

    /// <summary>Gets the number of caches currently held by this namespace.</summary>
    internal int CacheCount => _caches.Count;

    internal QuiescenceGate Gate => _gate;

    /// <inheritdoc />
    public ICache<TValue> GetCache<TOwner, TValue>(string partition)
        => _owner.GetCache<TOwner, TValue>(partition, this);

    /// <inheritdoc />
    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
        => _owner.GetRollingCache<TOwner, TValue>(partition, defaultLifetime, this);

    /// <inheritdoc />
    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null)
        => _owner.GetSelfRefreshingCache<TOwner, TValue>(partition, fetch, lifetime, this);

    /// <inheritdoc />
    /// <remarks>
    /// A second caller awaits the same completion rather than returning early, which would report the
    /// namespace released while a factory was still running in it.
    /// </remarks>
    public ValueTask ReleaseAsync()
    {
        lock (_releaseGate)
        {
            _release ??= ReleaseCoreAsync();
            return new ValueTask(_release);
        }
    }

    /// <summary>Admits one operation against this namespace, or refuses because it is being released.</summary>
    internal IDisposable Enter()
    {
        if (!_gate.TryEnter(out var ticket))
        {
            throw new ObjectDisposedException(
                Name,
                "This cache namespace has been released and hands out no further caches.");
        }

        return ticket!;
    }

    /// <summary>Records that a cache now belongs to this namespace. Called under an admission.</summary>
    internal void Track(string cacheName) => _caches[cacheName] = 0;

    private async Task ReleaseCoreAsync()
    {
        _gate.Close();

        // Waits for any resolution that was mid-registration when the close landed, so the set read below
        // is the complete one.
        await _gate.DrainAsync().ConfigureAwait(false);

        foreach (var name in _caches.Keys.ToArray())
        {
            if (_owner.TryRemoveCache(name, this, out var cache))
            {
                cache!.ReleaseReferences();
            }
        }

        _caches.Clear();

        // Last, so that the name cannot be reused while anything of the previous namespace is still held.
        _owner.ForgetNamespace(this);
    }
}
