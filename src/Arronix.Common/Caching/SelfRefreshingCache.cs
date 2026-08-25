using System.Collections.Frozen;
using System.Linq;
using Arronix.Abstractions.Caching;
using Arronix.Common.Lifetimes;

namespace Arronix.Common.Caching;

/// <summary>
/// The bulk-fetched cache behind <see cref="ICacheProvider.GetSelfRefreshingCache{TOwner, TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The cached value type.</typeparam>
/// <remarks>
/// <para>
/// The contents are one immutable snapshot replaced wholesale, so a reader sees the whole of the previous
/// fetch or the whole of the next one and never a half-applied refresh.
/// </para>
/// <para>A refresh is single-flight: concurrent callers observe one fetch.</para>
/// <para>
/// The fetch delegate is held for the life of the cache and can be extension code, so release drops it
/// with the snapshot. Every member except <see cref="Name"/> passes through the namespace's gate.
/// </para>
/// </remarks>
internal sealed class SelfRefreshingCache<TValue> : INamespacedCache, ISelfRefreshingCache<TValue>
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan? _lifetime;
    private readonly QuiescenceGate? _namespaceGate;
    private readonly object _gate = new();

    private Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>>? _fetch;
    private Snapshot _snapshot;
    private Flight? _inFlight;

    /// <summary>Initializes a new instance of the <see cref="SelfRefreshingCache{TValue}"/> class.</summary>
    /// <param name="name">The fully-qualified cache name, including the owning partition.</param>
    /// <param name="fetch">Produces the complete contents.</param>
    /// <param name="clock">The clock staleness is measured against.</param>
    /// <param name="lifetime">
    /// How long the contents stay fresh, or <see langword="null"/> for no staleness threshold: nothing goes
    /// stale on its own and the contents change only when a caller refreshes or replaces them.
    /// </param>
    /// <param name="namespaceGate">The releasable namespace this cache belongs to, when it belongs to one.</param>
    internal SelfRefreshingCache(
        string name,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeProvider clock,
        TimeSpan? lifetime,
        QuiescenceGate? namespaceGate = null)
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

    /// <summary>
    /// Gets or sets a hook run after a fetch has produced contents and before they are published. The seam
    /// that makes the publish-before-waking order provable without a scheduler race: a test holds it and
    /// observes that no caller who joined the fetch can have finished. Unset in production.
    /// </summary>
    internal Action? OnFetched { get; set; }

    /// <summary>
    /// Gets or sets a hook run as a caller reaches the staleness decision, before it takes the lock that
    /// decision and the joining of a flight share. The seam for the other order: a test holds a caller here
    /// while another publishes, and observes that the held caller then finds nothing left to fetch. Unset
    /// in production.
    /// </summary>
    internal Action? OnDeciding { get; set; }

    /// <inheritdoc />
    /// <remarks>The count of what was last fetched; staleness here is a property of the whole snapshot.</remarks>
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
    /// <remarks>A cache with no configured lifetime has nothing to measure against and is left alone.</remarks>
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

        await FetchAsync(timeToLive: null, unconditional: false, cancellationToken).ConfigureAwait(false);

        var snapshot = Volatile.Read(ref _snapshot);

        return snapshot.Items.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Cache '{Name}' holds no entry for key '{key}'.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Contents past their configured lifetime are reported absent and left where they are, so what
    /// <see cref="Count"/> reports still changes only when a sweep or a refresh changes it.
    /// </remarks>
    public TValue? Find(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var admission = Enter();

        var snapshot = Volatile.Read(ref _snapshot);

        if (_lifetime is { } lifetime && snapshot.IsExpired(_clock.GetUtcNow(), lifetime))
        {
            return default;
        }

        return snapshot.Items.TryGetValue(key, out var value) ? value : default;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var admission = Enter();
        await FetchAsync(timeToLive: null, unconditional: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RefreshIfExpiredAsync(
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        CacheLifetime.RequirePositive(timeToLive, nameof(timeToLive));

        using var admission = Enter();
        await FetchAsync(timeToLive, unconditional: false, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Replaces the contents, collapsing concurrent callers onto one fetch.</summary>
    /// <param name="timeToLive">The threshold this caller measures staleness against, when it has one.</param>
    /// <param name="unconditional">Whether to fetch whatever the contents look like.</param>
    /// <param name="cancellationToken">Abandons this caller's wait.</param>
    /// <remarks>
    /// A fetch that fails is not published, leaves the previous contents where they were, and is not left
    /// as the next caller's attempt.
    /// </remarks>
    private async Task FetchAsync(TimeSpan? timeToLive, bool unconditional, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnDeciding?.Invoke();

            Flight attempt;
            var owned = false;

            lock (_gate)
            {
                // Deciding staleness and joining a flight are one step. Read apart, a caller that paused
                // between them would start a second fetch of contents another caller had just made fresh.
                if (!unconditional && !NeedsFetch(_snapshot, timeToLive))
                {
                    return;
                }

                if (_inFlight is { } running)
                {
                    attempt = running;
                }
                else
                {
                    attempt = _inFlight = new Flight();
                    owned = true;
                }
            }

            if (owned)
            {
                await PublishAsync(attempt, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await attempt.Completed.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The caller that owned that fetch gave up; this one has not, so it tries again.
                continue;
            }
        }
    }

    /// <summary>Whether these contents oblige a fetch.</summary>
    private bool NeedsFetch(Snapshot snapshot, TimeSpan? timeToLive)
    {
        // Contents that have never been loaded are stale under any reading, threshold or no threshold.
        // Without this a cache created with no lifetime would never call its fetch delegate at all.
        if (!snapshot.HasLoaded)
        {
            return true;
        }

        // Loaded, and with no threshold to measure against, nothing goes stale on its own.
        return (timeToLive ?? _lifetime) is { } threshold && snapshot.IsExpired(_clock.GetUtcNow(), threshold);
    }

    /// <summary>Runs one flight: fetch, publish, take the flight off the record, and only then wake it.</summary>
    /// <remarks>
    /// The order is the contract. A caller who joined is told the refresh finished and goes straight on to
    /// read the contents, so waking it before publishing would let it read the contents this very fetch
    /// replaced — or, for a cache never loaded, none at all.
    /// </remarks>
    private async Task PublishAsync(Flight flight, CancellationToken cancellationToken)
    {
        Snapshot produced;

        try
        {
            produced = await RunFetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            Retire(flight);
            flight.Canceled(canceled);
            throw;
        }
        catch (Exception failure)
        {
            Retire(flight);
            flight.Failed(failure);
            throw;
        }

        OnFetched?.Invoke();

        lock (_gate)
        {
            // A release that ran while this fetch was in flight already dropped the delegate; publishing
            // now would put extension values back into a released cache.
            if (_fetch is not null)
            {
                _snapshot = produced;
            }

            Clear(flight);
        }

        flight.Landed();
    }

    /// <summary>Takes a failed flight off the record, so it is not left as the next caller's attempt.</summary>
    private void Retire(Flight flight)
    {
        lock (_gate)
        {
            Clear(flight);
        }
    }

    /// <summary>Forgets this flight, and only this one: a release may already have started another.</summary>
    private void Clear(Flight flight)
    {
        if (ReferenceEquals(_inFlight, flight))
        {
            _inFlight = null;
        }
    }

    private async Task<Snapshot> RunFetchAsync(CancellationToken cancellationToken)
    {
        var fetch = Volatile.Read(ref _fetch)
            ?? throw new ObjectDisposedException(
                Name,
                "The cache namespace this cache belongs to has been released. A namespace released with its "
                + "extension and a disposed provider both refuse every further operation.");

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
                "The cache namespace this cache belongs to has been released. A namespace released with its "
                + "extension and a disposed provider both refuse every further operation.");
        }

        return ticket;
    }

    /// <summary>
    /// One fetch-and-publish transaction, and what a caller who did not start it waits for.
    /// </summary>
    /// <remarks>
    /// What completes here is the whole transaction rather than the fetch. Joining callers are woken by it
    /// and read the contents immediately, so the fetched contents are the cache's contents by then.
    /// </remarks>
    private sealed class Flight
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when what this flight fetched is published, or faults as it failed.</summary>
        internal Task Completed => _completed.Task;

        internal void Landed() => _completed.TrySetResult();

        /// <summary>Every joining caller shares the failure, which is the one they would have had.</summary>
        /// <remarks>
        /// Observed here as well. The caller that owned the flight rethrows this failure itself and there
        /// may have been no joiner at all, so nothing else would ever look at it.
        /// </remarks>
        internal void Failed(Exception failure)
        {
            _completed.TrySetException(failure);
            _ = _completed.Task.Exception;
        }

        /// <summary>
        /// Cancellation belongs to the caller that owned the flight. A joiner sees it, tells it apart from
        /// its own, and tries again with a token that has not been cancelled.
        /// </summary>
        internal void Canceled(OperationCanceledException canceled)
            => _completed.TrySetCanceled(canceled.CancellationToken);
    }

    /// <summary>One complete set of contents and when it was loaded.</summary>
    private sealed class Snapshot(FrozenDictionary<string, TValue> items, DateTimeOffset? loadedAt)
    {
        internal static Snapshot Empty { get; } = new(
            FrozenDictionary<string, TValue>.Empty,
            loadedAt: null);

        internal FrozenDictionary<string, TValue> Items { get; } = items;

        internal DateTimeOffset? LoadedAt { get; } = loadedAt;

        /// <summary>Whether these contents have ever been loaded.</summary>
        internal bool HasLoaded => LoadedAt is not null;

        /// <summary>Contents never loaded are stale, which is what forces the first fetch.</summary>
        internal bool IsExpired(DateTimeOffset now, TimeSpan timeToLive)
            => LoadedAt is not { } at || at + timeToLive <= now;
    }
}
