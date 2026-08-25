using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
public sealed class CacheProvider : ICacheNamespaceProvider, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Registration> _caches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheNamespace> _namespaces = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly ITimer? _sweep;

    /// <summary>
    /// The provider's own namespace. Every cache belongs to one — the host's unscoped caches to this — so
    /// there is no cache whose operations disposal cannot close and wait for. It also gates publication:
    /// creating a namespace and registering a cache are admitted operations, so disposal either waits for
    /// one or refuses it, and neither can land in a table that has already been swept.
    /// </summary>
    private readonly CacheNamespace _root;

    private readonly object _disposalGate = new();

    /// <summary>
    /// The completion every caller of <see cref="DisposeAsync"/> awaits. A promise rather than the core
    /// method's own task: a completed async method's task is its state machine, and this field lives as
    /// long as the provider does, so holding one would keep the last cache it touched — and with it the
    /// extension's load context — alive for the life of the process.
    /// </summary>
    private TaskCompletionSource? _disposal;

    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="CacheProvider"/> class.</summary>
    /// <param name="clock">The clock every cache measures expiry against.</param>
    /// <param name="options">The sweep configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    public CacheProvider(TimeProvider clock, IOptions<CacheOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _root = new CacheNamespace(this, "(host)", isRoot: true);

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

    /// <summary>
    /// Gets or sets a hook run inside namespace creation, after its admission is taken and before the
    /// namespace is published. The seam that makes creation-versus-disposal provable. Unset in production.
    /// </summary>
    internal Action? OnCreationAdmitted { get; set; }

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

        // Admitted, so disposal waits for this publication or refuses it outright.
        using var admission = _root.Enter();

        OnCreationAdmitted?.Invoke();

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
    /// <remarks>
    /// The blocking form of <see cref="DisposeAsync"/>, and it blocks for the same reason: a factory or a
    /// fetch that is still running is code this provider handed a cache to, and returning while it runs
    /// would report the provider disposed while it is not.
    /// </remarks>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Refuses everything new, waits for everything already admitted, and then lets go.
    /// </summary>
    /// <returns>A task that completes once no cache operation is running and nothing is held.</returns>
    /// <remarks>
    /// Every close is started before any wait, so one slow factory delays only the waiting, never the
    /// refusing: by the time the first drain is awaited, every namespace in this provider — the root
    /// included — is already turning new work away. Every caller awaits the same completion.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource promise;
        var first = false;

        lock (_disposalGate)
        {
            if (_disposal is null)
            {
                _disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                first = true;
            }

            promise = _disposal;
        }

        if (first)
        {
            _ = FulfillAsync(promise);
        }

        return new ValueTask(promise.Task);
    }

    /// <summary>Runs the disposal behind the promise, so nothing durable owns its state machine.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever disposal produces is handed to every caller through the promise rather than left on an unobserved task.")]
    private async Task FulfillAsync(TaskCompletionSource promise)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            promise.SetResult();
        }
        catch (Exception failure)
        {
            promise.SetException(failure);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref _disposed, 1);
        _sweep?.Dispose();

        var known = _namespaces.Values.ToArray();

        // Everything known is closed before anything is awaited, so refusal is immediate everywhere.
        _root.Gate.Close();

        foreach (var space in known)
        {
            space.Gate.Close();
        }

        var releases = known.Select(space => space.ReleaseAsync().AsTask()).ToList();

        // Every creation and registration that was in flight has finished or been refused after this.
        await _root.Gate.DrainAsync().ConfigureAwait(false);

        // A namespace admitted before the root closed can have been published while that drain ran. Close
        // them all first, for the same reason, and only then wait.
        var late = _namespaces.Values.Except(known).ToArray();

        foreach (var space in late)
        {
            space.Gate.Close();
        }

        releases.AddRange(late.Select(space => space.ReleaseAsync().AsTask()));

        await Task.WhenAll(releases).ConfigureAwait(false);
        await _root.ReleaseAsync().ConfigureAwait(false);

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
                (owningNamespace ?? _root).Gate));

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
                (owningNamespace ?? _root).Gate));
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
            name => new SelfRefreshingCache<TValue>(name, fetch, _clock, lifetime, (owningNamespace ?? _root).Gate));
    }

    /// <summary>Removes one cache, but only when the namespace asking for it is the one that owns it.</summary>
    internal bool TryRemoveCache(string name, CacheNamespace owner, out INamespacedCache? cache)
    {
        if (_caches.TryGetValue(name, out var registration)
            && string.Equals(registration.Shape.Namespace, owner.Scope, StringComparison.Ordinal)
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
        // The root ticket covers publication into this provider's own table, which is what disposal waits
        // for; the namespace ticket covers the namespace's own release.
        var space = owningNamespace ?? _root;
        using var publication = _root.Enter();
        using var admission = ReferenceEquals(space, _root) ? null : space.Enter();

        OnResolutionAdmitted?.Invoke();

        var name = NameOf(owner, partition, space.Scope);
        var shape = new CacheShape(
            IdentityOf(owner),
            IdentityOf(valueType),
            kind,
            expiry,
            lifetime,
            space.Scope);

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

        space.Track(name);
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
