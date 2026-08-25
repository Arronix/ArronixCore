using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.ExceptionServices;
using Arronix.Abstractions.Caching;
using Arronix.Common.Lifetimes;

namespace Arronix.Common.Caching;

/// <summary>
/// The keyed cache behind <see cref="ICacheProvider.GetCache{TOwner, TValue}"/> and
/// <see cref="ICacheProvider.GetRollingCache{TOwner, TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The cached value type.</typeparam>
/// <remarks>
/// <para>
/// A miss is single-flight per key: concurrent callers observe one factory invocation and share its
/// outcome, and a factory that throws or is canceled produces no entry and poisons no later attempt.
/// </para>
/// <para>
/// Every member except <see cref="Name"/> passes through the owning namespace's gate, so a released
/// namespace refuses reads, writes, enumeration and removal alike.
/// </para>
/// </remarks>
internal sealed class ExpiringCache<TValue> : INamespacedCache, ICache<TValue>
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Flight> _inFlight = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly CacheExpiry _expiry;
    private readonly TimeSpan? _defaultLifetime;
    private readonly QuiescenceGate? _namespaceGate;

    /// <summary>Initializes a new instance of the <see cref="ExpiringCache{TValue}"/> class.</summary>
    /// <param name="name">The fully-qualified cache name, including the owning partition.</param>
    /// <param name="clock">The clock expiry is measured against.</param>
    /// <param name="expiry">Whether the lifetime runs from storage or from the last read.</param>
    /// <param name="defaultLifetime">
    /// The lifetime applied to entries stored without one, or <see langword="null"/> for an entry that
    /// never expires on its own.
    /// </param>
    /// <param name="namespaceGate">
    /// The releasable namespace this cache belongs to, when it belongs to one.
    /// </param>
    internal ExpiringCache(
        string name,
        TimeProvider clock,
        CacheExpiry expiry,
        TimeSpan? defaultLifetime,
        QuiescenceGate? namespaceGate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clock);
        CacheLifetime.RequirePositive(defaultLifetime, nameof(defaultLifetime));

        if (expiry == CacheExpiry.Rolling && defaultLifetime is null)
        {
            throw new ArgumentNullException(
                nameof(defaultLifetime),
                "A rolling cache measures its lifetime from the last read and therefore must have one.");
        }

        Name = name;
        _clock = clock;
        _expiry = expiry;
        _defaultLifetime = defaultLifetime;
        _namespaceGate = namespaceGate;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            using var admission = Enter();
            return _entries.Count;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Enumerating hands out each live value, so on a rolling cache it is a successful read and restarts
    /// those entries' lifetimes. The renewal is a reference-identity swap, so no cached value's own
    /// equality runs.
    /// </remarks>
    public IReadOnlyCollection<TValue> Values
    {
        get
        {
            using var admission = Enter();

            var now = _clock.GetUtcNow();
            var live = new List<TValue>(_entries.Count);

            foreach (var pair in _entries)
            {
                if (pair.Value.IsExpired(now))
                {
                    continue;
                }

                if (_expiry == CacheExpiry.Rolling && pair.Value.Lifetime is { } lifetime)
                {
                    _entries.TryUpdate(pair.Key, pair.Value.RenewedTo(now + lifetime), pair.Value);
                }

                live.Add(pair.Value.Value);
            }

            return live;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        using var admission = Enter();
        _entries.Clear();
    }

    /// <inheritdoc />
    public void ClearExpired()
    {
        using var admission = Enter();
        Sweep();
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var admission = Enter();
        _entries.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public void Set(string key, TValue value, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        CacheLifetime.RequirePositive(lifetime, nameof(lifetime));

        using var admission = Enter();
        _entries[key] = NewEntry(value, lifetime);
    }

    /// <inheritdoc />
    public TValue? Find(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var admission = Enter();
        return TryReadLive(key, out var value) ? value : default;
    }

    /// <inheritdoc />
    public TValue Get(string key, Func<TValue> valueFactory, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(valueFactory);
        CacheLifetime.RequirePositive(lifetime, nameof(lifetime));

        using var admission = Enter();

        while (true)
        {
            if (TryReadLive(key, out var live))
            {
                return live!;
            }

            var flight = new Flight();
            var joined = _inFlight.GetOrAdd(key, flight);

            if (!ReferenceEquals(joined, flight))
            {
                // Blocking is the point of the synchronous overload; the alternative is every caller
                // running the same factory.
                var observed = joined.Task.GetAwaiter().GetResult();

                observed.Failure?.Throw();

                if (!observed.Canceled)
                {
                    return observed.Value!;
                }

                continue;
            }

            try
            {
                var produced = valueFactory();
                _entries[key] = NewEntry(produced, lifetime);
                flight.Complete(produced);
                return produced;
            }
            catch (Exception failure)
            {
                // The dispatch info carries the original throw site to everyone who joined, so a shared
                // failure is not re-stacked where one unrelated caller happened to observe it.
                flight.Fail(ExceptionDispatchInfo.Capture(failure));
                throw;
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<string, Flight>(key, flight));
            }
        }
    }

    /// <inheritdoc />
    public async Task<TValue> GetAsync(
        string key,
        Func<CancellationToken, Task<TValue>> valueFactory,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(valueFactory);
        CacheLifetime.RequirePositive(lifetime, nameof(lifetime));

        using var admission = Enter();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryReadLive(key, out var live))
            {
                return live!;
            }

            var flight = new Flight();
            var joined = _inFlight.GetOrAdd(key, flight);

            if (!ReferenceEquals(joined, flight))
            {
                var observed = await joined.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                observed.Failure?.Throw();

                if (!observed.Canceled)
                {
                    return observed.Value!;
                }

                // That attempt's owner gave up; this caller has not, so it starts its own rather than
                // inheriting an unrelated cancellation.
                continue;
            }

            try
            {
                var produced = await valueFactory(cancellationToken).ConfigureAwait(false);
                _entries[key] = NewEntry(produced, lifetime);
                flight.Complete(produced);
                return produced;
            }
            catch (OperationCanceledException)
            {
                flight.Cancel();
                throw;
            }
            catch (Exception failure)
            {
                flight.Fail(ExceptionDispatchInfo.Capture(failure));
                throw;
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<string, Flight>(key, flight));
            }
        }
    }

    /// <inheritdoc />
    public void SweepIfAdmitted()
    {
        if (_namespaceGate is null)
        {
            Sweep();
            return;
        }

        if (!_namespaceGate.TryEnter(out var ticket))
        {
            return;
        }

        using (ticket)
        {
            Sweep();
        }
    }

    /// <inheritdoc />
    public void ReleaseReferences()
    {
        _entries.Clear();

        // Nothing is in flight by here — the gate drained first — but a completed flight still holds the
        // value it produced, so the table is emptied rather than assumed empty.
        _inFlight.Clear();
    }

    private void Sweep()
    {
        var now = _clock.GetUtcNow();
        var entries = (ICollection<KeyValuePair<string, Entry>>)_entries;

        foreach (var pair in _entries.Where(pair => pair.Value.IsExpired(now)).ToArray())
        {
            entries.Remove(pair);
        }
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

    private Entry NewEntry(TValue value, TimeSpan? lifetime)
    {
        var effective = lifetime ?? _defaultLifetime;
        return new Entry(value, effective is { } span ? _clock.GetUtcNow() + span : null, effective);
    }

    /// <summary>Reads a live value, restarting a rolling lifetime on the way out.</summary>
    private bool TryReadLive(string key, out TValue? value)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            value = default;
            return false;
        }

        var now = _clock.GetUtcNow();

        if (entry.IsExpired(now))
        {
            // By exact pair, so a value stored by another thread between the read and the removal survives.
            ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(key, entry));
            value = default;
            return false;
        }

        if (_expiry == CacheExpiry.Rolling && entry.Lifetime is { } lifetime)
        {
            _entries.TryUpdate(key, entry.RenewedTo(now + lifetime), entry);
        }

        value = entry.Value;
        return true;
    }

    /// <summary>One stored value, its expiry instant, and the lifetime a rolling read restarts.</summary>
    /// <remarks>
    /// Reference equality rather than a record, so the dictionary's compare-and-swap operations do not run
    /// the cached value's own equality.
    /// </remarks>
    private sealed class Entry(TValue value, DateTimeOffset? expiresAt, TimeSpan? lifetime)
    {
        internal TValue Value { get; } = value;

        internal TimeSpan? Lifetime { get; } = lifetime;

        private DateTimeOffset? ExpiresAt { get; } = expiresAt;

        internal bool IsExpired(DateTimeOffset now) => ExpiresAt is { } at && at <= now;

        internal Entry RenewedTo(DateTimeOffset renewed) => new(Value, renewed, Lifetime);
    }

    /// <summary>What one caller's attempt at a key produced, shared with everyone who joined it.</summary>
    private readonly record struct FlightOutcome(TValue? Value, ExceptionDispatchInfo? Failure, bool Canceled);

    /// <summary>One in-progress attempt at a key.</summary>
    private sealed class Flight
    {
        private readonly TaskCompletionSource<FlightOutcome> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<FlightOutcome> Task => _completion.Task;

        internal void Complete(TValue value) => _completion.TrySetResult(new FlightOutcome(value, null, false));

        internal void Fail(ExceptionDispatchInfo failure)
            => _completion.TrySetResult(new FlightOutcome(default, failure, false));

        internal void Cancel() => _completion.TrySetResult(new FlightOutcome(default, null, true));
    }
}
