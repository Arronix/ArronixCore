using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// A group of caches the host can take back in one act.
/// </summary>
/// <remarks>
/// <para>
/// Release is a sequence, not a dictionary removal, because every step of it is load-bearing for
/// unloadability. New work is refused first; everything already admitted — including a resolution that is
/// halfway through registering a cache — is waited for; only then are the values, keys and fetch delegates
/// dropped and the exact cache objects removed. Dropping references before the wait would not make them
/// unreachable, it would only make them invisible.
/// </para>
/// <para>
/// Resolving a cache is itself an admitted operation, which is what makes creation and release
/// linearizable: a caller either gets in before the close and is then waited for, or is refused. There is
/// no third outcome in which a cache is registered into a namespace that has finished releasing.
/// </para>
/// <para>
/// Handing this object to an extension would be a mistake, so nothing does. The extension receives it
/// through the plain <see cref="ICacheProvider"/> face it has always had.
/// </para>
/// </remarks>
public sealed class CacheNamespace : ICacheProvider
{
    private readonly CacheProvider _owner;
    private readonly CacheOperationGate _gate = new();
    private readonly ConcurrentDictionary<string, byte> _caches = new(StringComparer.Ordinal);
    private readonly object _releaseGate = new();
    private Task? _release;

    internal CacheNamespace(CacheProvider owner, string name)
    {
        _owner = owner;
        Name = name;
    }

    /// <summary>Gets the group's name.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether this namespace has stopped admitting work.
    /// </summary>
    /// <remarks>
    /// True from the instant release begins rather than from the instant it finishes, because the
    /// observable consequence — every cache in the group refuses — starts at the close.
    /// </remarks>
    public bool IsReleased => _gate.IsClosed;

    /// <summary>Gets the number of caches currently held by this namespace.</summary>
    public int CacheCount => _caches.Count;

    internal CacheOperationGate Gate => _gate;

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

    /// <summary>
    /// Closes the namespace to new work, waits for the work already running, drops what its caches held,
    /// and removes them from the provider.
    /// </summary>
    /// <returns>A task that completes when nothing in this namespace is running or referenced any more.</returns>
    /// <remarks>
    /// Idempotent, and every caller awaits the same completion. A second caller returning early while the
    /// first was still draining would report the namespace released while a factory was still running in
    /// it, which is the exact claim teardown must not make.
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
        // Nothing new from here. Everything below runs against a set that can only shrink.
        _gate.Close();

        // Includes any resolution that was mid-registration when the close landed, so the set read after
        // this is the complete one.
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
