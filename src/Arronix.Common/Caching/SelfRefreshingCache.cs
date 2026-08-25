using System.Collections.Frozen;
using System.Linq;
using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// The bulk-fetched cache behind <see cref="ICacheProvider.GetSelfRefreshingCache{TOwner, TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The cached value type.</typeparam>
/// <remarks>
/// <para>
/// The contents are one immutable snapshot replaced wholesale, so a reader either sees the whole of the
/// previous fetch or the whole of the next one and never a half-applied refresh. The legacy implementation
/// this replaces mutated a shared dictionary while a fetch was in progress.
/// </para>
/// <para>
/// A refresh is single-flight. Concurrent callers observe one fetch, which is the defect
/// <see cref="ISelfRefreshingCache{TValue}.RefreshAsync"/> is documented to prevent.
/// </para>
/// <para>
/// The fetch delegate is held for the life of the cache and can be extension code closing over extension
/// state, so it is dropped by release along with the snapshot. Every public member except
/// <see cref="Name"/> passes through the owning namespace's gate for the same reason.
/// </para>
/// </remarks>
internal sealed class SelfRefreshingCache<TValue> : INamespacedCache, ISelfRefreshingCache<TValue>
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan? _lifetime;
    private readonly CacheOperationGate? _namespaceGate;
    private readonly object _gate = new();

    private Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>>? _fetch;
    private Snapshot _snapshot;
    private Task<Snapshot>? _inFlight;

    /// <summary>Initializes a new instance of the <see cref="SelfRefreshingCache{TValue}"/> class.</summary>
    /// <param name="name">The fully-qualified cache name, including the owning partition.</param>
    /// <param name="fetch">Produces the complete contents.</param>
    /// <param name="clock">The clock staleness is measured against.</param>
    /// <param name="lifetime">How long the contents stay fresh, or <see langword="null"/> for no default.</param>
    /// <param name="namespaceGate">The releasable namespace this cache belongs to, when it belongs to one.</param>
    internal SelfRefreshingCache(
        string name,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeProvider clock,
        TimeSpan? lifetime,
        CacheOperationGate? namespaceGate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(clock);
        CacheLifetime.RequirePositive(lifetime, nameof(lifetime));

        Name = name;
        _fetch = fetch;
        _clock = clock;
        _lifetime = lifetime;
        _namespaceGate = namespaceGate;
        _snapshot = Snapshot.Empty;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The count of what was last fetched. Staleness is a property of the snapshot as a whole here, so
    /// there is no per-entry expiry for a sweep to remove.
    /// </remarks>
    public int Count
    {
        get
        {
            using var admission = Enter();
            return Volatile.Read(ref _snapshot).Items.Count;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        using var admission = Enter();

        lock (_gate)
        {
            _snapshot = Snapshot.Empty;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Contents that have outlived the configured lifetime are dropped. A cache with no configured
    /// lifetime has nothing to measure against and is left alone rather than emptied.
    /// </remarks>
    public void ClearExpired()
    {
        using var admission = Enter();
        SweepCore();
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var admission = Enter();

        lock (_gate)
        {
            if (!_snapshot.Items.ContainsKey(key))
            {
                return;
            }

            var remaining = _snapshot.Items
                .Where(pair => !string.Equals(pair.Key, key, StringComparison.Ordinal))
                .ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            _snapshot = new Snapshot(remaining, _snapshot.LoadedAt);
        }
    }

    /// <inheritdoc />
    public async Task<TValue> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var admission = Enter();

        await RefreshIfExpiredCoreAsync(null, cancellationToken).ConfigureAwait(false);

        var snapshot = Volatile.Read(ref _snapshot);

        return snapshot.Items.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Cache '{Name}' holds no entry for key '{key}'.");
    }

    /// <inheritdoc />
    public TValue? Find(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var admission = Enter();
        return Volatile.Read(ref _snapshot).Items.TryGetValue(key, out var value) ? value : default;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var admission = Enter();
        await FetchAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RefreshIfExpiredAsync(
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        CacheLifetime.RequirePositive(timeToLive, nameof(timeToLive));

        using var admission = Enter();
        await RefreshIfExpiredCoreAsync(timeToLive, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Update(IReadOnlyDictionary<string, TValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var admission = Enter();
        var replacement = items.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        lock (_gate)
        {
            _snapshot = new Snapshot(replacement, _clock.GetUtcNow());
        }
    }

    /// <inheritdoc />
    public void Touch()
    {
        using var admission = Enter();

        lock (_gate)
        {
            _snapshot = new Snapshot(_snapshot.Items, _clock.GetUtcNow());
        }
    }

    /// <inheritdoc />
    public bool IsExpired(TimeSpan timeToLive)
    {
        CacheLifetime.RequirePositive(timeToLive, nameof(timeToLive));

        using var admission = Enter();
        return Volatile.Read(ref _snapshot).IsExpired(_clock.GetUtcNow(), timeToLive);
    }

    /// <inheritdoc />
    public void SweepIfAdmitted()
    {
        if (_namespaceGate is null)
        {
            SweepCore();
            return;
        }

        if (!_namespaceGate.TryEnter(out var ticket))
        {
            return;
        }

        using (ticket)
        {
            SweepCore();
        }
    }

    /// <inheritdoc />
    public void ReleaseReferences()
    {
        lock (_gate)
        {
            _snapshot = Snapshot.Empty;
            _inFlight = null;
            _fetch = null;
        }
    }

    private void SweepCore()
    {
        if (_lifetime is not { } lifetime)
        {
            return;
        }

        lock (_gate)
        {
            if (_snapshot.IsExpired(_clock.GetUtcNow(), lifetime))
            {
                _snapshot = Snapshot.Empty;
            }
        }
    }

    private async Task RefreshIfExpiredCoreAsync(TimeSpan? timeToLive, CancellationToken cancellationToken)
    {
        // A cache configured without a lifetime and asked without one has no staleness threshold at all,
        // so there is nothing to compare and nothing to refresh.
        if ((timeToLive ?? _lifetime) is not { } threshold)
        {
            return;
        }

        if (!Volatile.Read(ref _snapshot).IsExpired(_clock.GetUtcNow(), threshold))
        {
            return;
        }

        await FetchAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the contents, collapsing concurrent callers onto one fetch.
    /// </summary>
    /// <remarks>
    /// A caller that arrives while a fetch is running joins it rather than starting a second one. A fetch
    /// that fails is not published and does not become the in-flight attempt for the next caller.
    /// </remarks>
    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<Snapshot> attempt;
            var owned = false;

            lock (_gate)
            {
                if (_inFlight is { } running)
                {
                    attempt = running;
                }
                else
                {
                    attempt = RunFetchAsync(cancellationToken);
                    _inFlight = attempt;
                    owned = true;
                }
            }

            if (owned)
            {
                try
                {
                    var produced = await attempt.ConfigureAwait(false);

                    lock (_gate)
                    {
                        // A release that ran while this fetch was in flight has already dropped the fetch
                        // delegate; publishing this snapshot now would put extension values back into a
                        // cache the host has finished taking apart.
                        if (_fetch is not null)
                        {
                            _snapshot = produced;
                        }

                        _inFlight = null;
                    }

                    return;
                }
                catch
                {
                    lock (_gate)
                    {
                        _inFlight = null;
                    }

                    throw;
                }
            }

            try
            {
                await attempt.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The caller that owned that fetch gave up; this one has not, so it tries again.
                continue;
            }
        }
    }

    private async Task<Snapshot> RunFetchAsync(CancellationToken cancellationToken)
    {
        // Yielding first keeps the fetch off the caller's stack while the lock that published this task
        // is still held, so the delegate can never run under the snapshot lock.
        await Task.Yield();

        var fetch = Volatile.Read(ref _fetch)
            ?? throw new ObjectDisposedException(
                Name,
                "This cache belongs to an extension whose cache namespace has been released.");

        var fetched = await fetch(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The fetch delegate for cache '{Name}' returned no contents.");

        return new Snapshot(
            fetched.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            _clock.GetUtcNow());
    }

    private IDisposable? Enter()
    {
        if (_namespaceGate is null)
        {
            return null;
        }

        if (!_namespaceGate.TryEnter(out var ticket))
        {
            throw new ObjectDisposedException(
                Name,
                "This cache belongs to an extension whose cache namespace has been released.");
        }

        return ticket;
    }

    /// <summary>One complete set of contents and when it was loaded.</summary>
    private sealed class Snapshot(FrozenDictionary<string, TValue> items, DateTimeOffset? loadedAt)
    {
        internal static Snapshot Empty { get; } = new(
            FrozenDictionary<string, TValue>.Empty,
            loadedAt: null);

        internal FrozenDictionary<string, TValue> Items { get; } = items;

        internal DateTimeOffset? LoadedAt { get; } = loadedAt;

        /// <summary>Contents never loaded are stale by definition, which is what forces the first fetch.</summary>
        internal bool IsExpired(DateTimeOffset now, TimeSpan timeToLive)
            => LoadedAt is not { } at || at + timeToLive <= now;
    }
}
