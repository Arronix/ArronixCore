using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Host.Configuration;
using Microsoft.Extensions.Options;

// The plugin contracts are experimental; throttle keys are synthesized from the capability vocabulary.
#pragma warning disable ARX0014

namespace Arronix.Host.Scheduling;

/// <summary>
/// Holds back work until every ceiling that applies to it has room.
/// </summary>
/// <remarks>
/// <para>
/// Three limits apply at once and all of them must be free: a global ceiling on everything the host is
/// doing, the per-job ceiling the job declares for itself, and a throttle for each capability-scoped key the
/// host synthesized for the work. A piece of work that is under the global ceiling but would be the fifth
/// concurrent catalog query waits, and one that is under every throttle but would exceed the global
/// ceiling waits too.
/// </para>
/// <para>
/// Keys are acquired in ordinal order, which is what makes deadlock impossible rather than unlikely. Two
/// pieces of work holding each other's keys is the classic multi-lock failure, and a total order over the
/// keys removes it entirely — there is no cycle to form.
/// </para>
/// <para>
/// This is not rate limiting. How often the host may call one remote is the limiter's business, shared
/// across every extension hitting the same host; how much work runs at once is this. Conflating them would
/// mean two extensions using the same release source could not be throttled independently of how fast either
/// of them was allowed to call it.
/// </para>
/// </remarks>
public sealed class ConcurrencyGovernor : IDisposable
{
    /// <summary>
    /// The key every piece of work holds, so the global ceiling participates in the same ordered acquisition
    /// as every other limit rather than being a separate gate outside it.
    /// </summary>
    public const string GlobalKey = "@global";

    private const string CapabilityPrefix = "cap:";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _limits;
    private readonly int _globalLimit;
    private bool _disposed;

    /// <summary>
    /// Creates a governor from the deployment's scheduler settings.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public ConcurrencyGovernor(IOptions<SchedulerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;
        _globalLimit = settings.MaxConcurrentJobs;
        _limits = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (key, limit) in settings.ThrottleLimits)
        {
            // An operator writes "indexing"; the host synthesizes "cap:indexing". Normalizing here means the
            // configuration file does not have to encode the synthesis rule.
            var normalized = CapabilityNames.TryParse(key, out _) ? CapabilityPrefix + key : key;
            _limits[normalized] = limit;
        }
    }

    /// <summary>
    /// Synthesizes the keys a piece of work is throttled by.
    /// </summary>
    /// <param name="owner">The extension the work belongs to.</param>
    /// <param name="capabilities">That extension's granted capabilities, after implication.</param>
    /// <param name="mediaKind">The media kind the work concerns, when it concerns one.</param>
    /// <returns>The keys, including the global one, in ordinal order.</returns>
    /// <remarks>
    /// Three scopes, because three questions get asked when a deployment is overloaded: which activity is
    /// saturating it, which extension is responsible, and which library is being worked on.
    /// </remarks>
    public static IReadOnlyList<string> KeysFor(PluginId owner, CapabilitySet capabilities, string? mediaKind)
    {
        var keys = new List<string>(capabilities.Enumerate().Count() + 3) { GlobalKey, $"plugin:{owner}" };

        foreach (var capability in capabilities.Enumerate())
        {
            keys.Add(CapabilityPrefix + CapabilityNames.ToWireName(capability));
        }

        if (!string.IsNullOrWhiteSpace(mediaKind))
        {
            keys.Add($"kind:{mediaKind}");
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>
    /// Gets the ceiling for one key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The ceiling. Keys with no configured limit are unthrottled beyond the global ceiling.</returns>
    public int LimitFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return string.Equals(key, GlobalKey, StringComparison.Ordinal)
            ? _globalLimit
            : _limits.GetValueOrDefault(key, int.MaxValue);
    }

    /// <summary>
    /// Takes one slot on every key, but only if every one of them is free right now.
    /// </summary>
    /// <param name="keys">The keys, which need not be ordered by the caller.</param>
    /// <param name="lease">The lease when every key had room; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the slots were taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is what the scheduler uses, in preference to waiting. A scheduler that waited would commit to
    /// the first entry it dequeued and hold every later one behind it, so a single saturated throttle would
    /// stall work that had nothing to do with it. Declining and moving to the next candidate costs one
    /// non-blocking attempt per key and removes head-of-line blocking entirely.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The try-and-out form avoids a nullable return whose null the caller would have to interpret, and the lease must not be produced when acquisition fails.")]
    public bool TryAcquire(IReadOnlyList<string> keys, out IAsyncDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var ordered = Ordered(keys);
        var held = new List<SemaphoreSlim>(ordered.Count);

        foreach (var key in ordered)
        {
            var gate = _gates.GetOrAdd(key, GateFor);

            if (gate.Wait(0))
            {
                held.Add(gate);
                continue;
            }

            foreach (var taken in held)
            {
                taken.Release();
            }

            lease = null;
            return false;
        }

        lease = new Lease(held);
        return true;
    }

    /// <summary>
    /// Waits until every key has room, then takes one slot on each.
    /// </summary>
    /// <param name="keys">The keys, which need not be ordered by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A lease that returns the slots when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var ordered = Ordered(keys);
        var held = new List<SemaphoreSlim>(ordered.Count);

        try
        {
            foreach (var key in ordered)
            {
                var gate = _gates.GetOrAdd(key, GateFor);
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                held.Add(gate);
            }
        }
        catch
        {
            // Partial acquisition is the one state that must never persist: the keys taken so far would be
            // held by nobody and every later piece of work would wait on them forever.
            foreach (var gate in held)
            {
                gate.Release();
            }

            throw;
        }

        return new Lease(held);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }

        _gates.Clear();
    }

    // Keys with no configured ceiling are dropped rather than given an unbounded semaphore: an unbounded
    // gate is a gate that only costs allocations. Ordering the survivors is what forecloses deadlock.
    private List<string> Ordered(IReadOnlyList<string> keys)
        =>
        [
            .. keys
                .Where(key => !string.IsNullOrWhiteSpace(key) && LimitFor(key) != int.MaxValue)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    private SemaphoreSlim GateFor(string key)
    {
        var limit = LimitFor(key);
        return new SemaphoreSlim(limit, limit);
    }

    private sealed class Lease(List<SemaphoreSlim> held) : IAsyncDisposable
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;

                foreach (var gate in held)
                {
                    gate.Release();
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
