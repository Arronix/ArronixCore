using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Caching;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Caching;

/// <summary>
/// The platform's cache provider: the one place a cache comes from, and the one place they are swept.
/// </summary>
/// <remarks>
/// <para>
/// A cache is named by its releasable namespace, its owning type and its partition, and carries a shape:
/// the value type, whether the lifetime runs from storage or from the last read, and the default lifetime.
/// Two callers asking for one name with different shapes are disagreeing about a cache rather than sharing
/// one, so the second is refused instead of being handed entries that expire by rules it did not ask for.
/// </para>
/// <para>
/// Both types are recorded as text, including their assembly identity. The provider outlives the
/// extensions that use it, and a <see cref="Type"/> in a long-lived dictionary keeps its whole load context
/// alive — the leak the releasable namespace exists to prevent. Assembly identity is part of the name as
/// well as the shape, so two types that share a full name do not collapse onto one entry and then refuse
/// each other.
/// </para>
/// <para>
/// Expired entries are never returned, so the sweep is about memory rather than correctness. It runs on the
/// injected clock's timer.
/// </para>
/// </remarks>
public sealed class CacheProvider : ICacheNamespaceProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, Registration> _caches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheNamespace> _namespaces = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly ITimer? _sweep;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="CacheProvider"/> class.</summary>
    /// <param name="clock">The clock every cache measures expiry against.</param>
    /// <param name="options">The sweep configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    public CacheProvider(TimeProvider clock, IOptions<CacheOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;

        var interval = options?.Value.SweepInterval ?? CacheOptions.DefaultSweepInterval;

        if (interval > TimeSpan.Zero)
        {
            _sweep = clock.CreateTimer(static state => ((CacheProvider)state!).SweepExpired(), this, interval, interval);
        }
    }

    /// <summary>Gets the names of every cache currently held.</summary>
    public IReadOnlyCollection<string> CacheNames => [.. _caches.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gets or sets a hook run inside cache resolution, after the namespace admission is taken and before
    /// the cache is registered.
    /// </summary>
    /// <remarks>
    /// The seam that makes the creation-versus-release ordering provable without a scheduler race: a test
    /// begins a release from inside the hook and observes that the release cannot complete while this
    /// resolution holds its admission. Unset in production.
    /// </remarks>
    internal Action? OnResolutionAdmitted { get; set; }

    /// <inheritdoc />
    public ICache<TValue> GetCache<TOwner, TValue>(string partition)
        => GetCache<TOwner, TValue>(partition, owningNamespace: null);

    /// <inheritdoc />
    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
        => GetRollingCache<TOwner, TValue>(partition, defaultLifetime, owningNamespace: null);

    /// <inheritdoc />
    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null)
        => GetSelfRefreshingCache<TOwner, TValue>(partition, fetch, lifetime, owningNamespace: null);

    /// <inheritdoc />
    public ICacheNamespace CreateNamespace(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var created = new CacheNamespace(this, name);

        if (!_namespaces.TryAdd(name, created))
        {
            throw new InvalidOperationException(
                $"A live cache namespace named '{name}' already exists. Release it before opening another.");
        }

        return created;
    }

    /// <summary>
    /// Removes entries whose lifetime has elapsed, in every cache this provider holds.
    /// </summary>
    /// <remarks>
    /// Runs on a timer and so can land at any point in a release. Each cache sweeps only if its namespace
    /// still admits work, and never refuses: an exception out of a timer callback is a failure nobody sees.
    /// </remarks>
    public void SweepExpired()
    {
        foreach (var registration in _caches.Values)
        {
            registration.Cache.SweepIfAdmitted();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _sweep?.Dispose();
        _caches.Clear();
        _namespaces.Clear();
    }

    internal ICache<TValue> GetCache<TOwner, TValue>(string partition, CacheNamespace? owningNamespace)
        => (ICache<TValue>)Resolve(
            typeof(TOwner),
            partition,
            typeof(TValue),
            CacheKind.Keyed,
            CacheExpiry.Fixed,
            lifetime: null,
            owningNamespace,
            name => new ExpiringCache<TValue>(
                name,
                _clock,
                CacheExpiry.Fixed,
                defaultLifetime: null,
                owningNamespace?.Gate));

    internal ICache<TValue> GetRollingCache<TOwner, TValue>(
        string partition,
        TimeSpan defaultLifetime,
        CacheNamespace? owningNamespace)
    {
        CacheLifetime.RequirePositive(defaultLifetime, nameof(defaultLifetime));

        return (ICache<TValue>)Resolve(
            typeof(TOwner),
            partition,
            typeof(TValue),
            CacheKind.Keyed,
            CacheExpiry.Rolling,
            defaultLifetime,
            owningNamespace,
            name => new ExpiringCache<TValue>(
                name,
                _clock,
                CacheExpiry.Rolling,
                defaultLifetime,
                owningNamespace?.Gate));
    }

    internal ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime,
        CacheNamespace? owningNamespace)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        CacheLifetime.RequirePositive(lifetime, nameof(lifetime));

        return (ISelfRefreshingCache<TValue>)Resolve(
            typeof(TOwner),
            partition,
            typeof(TValue),
            CacheKind.SelfRefreshing,
            CacheExpiry.Fixed,
            lifetime,
            owningNamespace,
            name => new SelfRefreshingCache<TValue>(name, fetch, _clock, lifetime, owningNamespace?.Gate));
    }

    /// <summary>Removes one cache, but only when the namespace asking for it is the one that owns it.</summary>
    internal bool TryRemoveCache(string name, CacheNamespace owner, out INamespacedCache? cache)
    {
        if (_caches.TryGetValue(name, out var registration)
            && string.Equals(registration.Shape.Namespace, owner.Name, StringComparison.Ordinal)
            && ((ICollection<KeyValuePair<string, Registration>>)_caches).Remove(
                new KeyValuePair<string, Registration>(name, registration)))
        {
            cache = registration.Cache;
            return true;
        }

        cache = null;
        return false;
    }

    /// <summary>Forgets a namespace, but only when it is still the live one under that name.</summary>
    internal void ForgetNamespace(CacheNamespace released)
        => ((ICollection<KeyValuePair<string, CacheNamespace>>)_namespaces).Remove(
            new KeyValuePair<string, CacheNamespace>(released.Name, released));

    /// <summary>
    /// The name a cache is known by: its namespace, its owning type's exact identity, and its partition.
    /// </summary>
    /// <remarks>
    /// The same identity the shape records, so two owners the shape would tell apart never collapse onto
    /// one entry and then refuse each other.
    /// </remarks>
    internal static string NameOf(Type owner, string partition, string? owningNamespace = null)
    {
        var scope = owningNamespace is null ? string.Empty : owningNamespace + "/";
        return string.Create(CultureInfo.InvariantCulture, $"{scope}{IdentityOf(owner)}:{partition}");
    }

    /// <summary>The text identity of a type, for a record that outlives the type's load context.</summary>
    private static string IdentityOf(Type type)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{type.FullName ?? type.Name}, {type.Assembly.FullName ?? type.Assembly.GetName().Name}");

    private ICache Resolve(
        Type owner,
        string partition,
        Type valueType,
        CacheKind kind,
        CacheExpiry expiry,
        TimeSpan? lifetime,
        CacheNamespace? owningNamespace,
        Func<string, INamespacedCache> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Resolution is an admitted operation, so a release either waits for this registration or refuses
        // it. Without the admission a cache can be added to a namespace whose release already read its
        // contents.
        using var admission = owningNamespace?.Enter();

        OnResolutionAdmitted?.Invoke();

        var name = NameOf(owner, partition, owningNamespace?.Name);
        var shape = new CacheShape(
            IdentityOf(owner),
            IdentityOf(valueType),
            kind,
            expiry,
            lifetime,
            owningNamespace?.Name);

        var registration = _caches.GetOrAdd(
            name,
            static (key, state) => new Registration(state.Shape, state.Create(key)),
            (Shape: shape, Create: create));

        if (registration.Shape != shape)
        {
            throw new InvalidOperationException(
                $"Cache '{name}' is already held as {registration.Shape.Describe()} and cannot also be used "
                + $"as {shape.Describe()}. One owner and partition names one cache.");
        }

        owningNamespace?.Track(name);
        return registration.Cache;
    }

    /// <summary>Which of the two cache contracts a registration serves.</summary>
    private enum CacheKind
    {
        Keyed = 0,
        SelfRefreshing = 1
    }

    /// <summary>The shape two callers must agree on before they share a cache.</summary>
    private readonly record struct CacheShape(
        string OwnerIdentity,
        string ValueIdentity,
        CacheKind Kind,
        CacheExpiry Expiry,
        TimeSpan? Lifetime,
        string? Namespace)
    {
        internal string Describe()
        {
            var lifetime = Lifetime is { } span ? span.ToString() : "no lifetime";
            var owner = Namespace is { } name ? $"namespace '{name}'" : "unscoped";
            return $"a {Expiry} {Kind} cache of {ValueIdentity} ({lifetime}, {owner})";
        }
    }

    private sealed record Registration(CacheShape Shape, INamespacedCache Cache);
}
