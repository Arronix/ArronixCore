using Arronix.Abstractions.Plugins;
using Arronix.Common.Caching;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;


namespace Arronix.Plugins.Loading;

/// <summary>
/// Owns every extension-created runtime object and the collectible context which contains its code.
/// </summary>
/// <remarks>
/// <para>
/// Release is ordered, and the order is the safety property. Outstanding invocations drain first; registered
/// values are then disposed once each, by reference identity, in reverse registration order, with the module
/// last; the extension's cache namespace is taken back after that, because a disposer may legitimately still
/// read a cache; and the load context is unloaded last.
/// </para>
/// <para>
/// A context is unloaded only when nothing failed. Unloading one whose objects could not be disposed would
/// mark it for a collection that cannot happen while claiming the extension is gone, so a failure retains
/// the context and is reported instead.
/// </para>
/// </remarks>
internal sealed class PluginRuntimeLease
{
    private readonly IReadOnlyList<object> _instances;
    private readonly TokenRegistry.TokenClaimPlan? _tokenClaims;
    private readonly IPluginAdmissionAttempt? _admissionAttempt;
    private readonly ICacheNamespace? _caches;
    private readonly object _releaseGate = new();
    private PluginLoadContext? _context;
    private Task<IReadOnlyList<string>>? _release;

    internal PluginRuntimeLease(
        PluginLoadContext context,
        PluginRegistrationLedger? ledger,
        IPluginModule? module,
        TokenRegistry.TokenClaimPlan? tokenClaims = null,
        IPluginAdmissionAttempt? admissionAttempt = null,
        ICacheNamespace? caches = null,
        PluginInvocationLifetime? invocation = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var instances = new List<object>();

        if (ledger is not null)
        {
            for (var index = ledger.Entries.Count - 1; index >= 0; index--)
            {
                var instance = ledger.Entries[index].Instance;
                if (!ReferenceEquals(instance, module) && seen.Add(instance))
                {
                    instances.Add(instance);
                }
            }
        }

        if (module is not null && seen.Add(module))
        {
            instances.Add(module);
        }

        _instances = instances.AsReadOnly();
        _context = context;
        _tokenClaims = tokenClaims;
        _admissionAttempt = admissionAttempt;
        _caches = caches;

        // Supplied by the loader, which creates it before the extension is configured so that the context's
        // cache, telemetry and event wrappers all hold this exact retention authority from the first call
        // the extension makes.
        Invocation = invocation ?? new PluginInvocationLifetime(context.Plugin);
    }

    /// <summary>Gets this runtime's licence to be called, which teardown drains before disposing it.</summary>
    internal PluginInvocationLifetime Invocation { get; }

    /// <summary>Gets the collectible context while the lease is active.</summary>
    internal PluginLoadContext? LoadContext => _context;

    /// <summary>Gets the exact Host admission receipt coupled to this runtime lifetime.</summary>
    internal IPluginAdmissionAttempt? AdmissionAttempt => _admissionAttempt;

    /// <summary>Withdraws exactly the naming-token claims published by this runtime attempt.</summary>
    internal void UnpublishTokenClaims() => _tokenClaims?.Rollback();

    /// <summary>Disposes every owned value and unloads the collectible context.</summary>
    /// <returns>Every cleanup failure, after all remaining values were still attempted.</returns>
    /// <remarks>
    /// Repeated and concurrent callers await the same completion. Returning early while another caller was
    /// still draining would report an extension released while its code was still running.
    /// </remarks>
    internal ValueTask<IReadOnlyList<string>> DisposeAsync()
    {
        lock (_releaseGate)
        {
            _release ??= ReleaseAsync();
            return new ValueTask<IReadOnlyList<string>>(_release);
        }
    }

    /// <summary>Waits synchronously for cleanup when the synchronous load pipeline quarantines a plugin.</summary>
    internal IReadOnlyList<string> DisposeSynchronously()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task<IReadOnlyList<string>> ReleaseAsync()
    {
        var failures = new List<string>();
        UnpublishTokenClaims();

        // Nothing new may call into this extension, and everything that already was is finished, before any
        // of its objects are disposed. Withdrawal closed the lifetime under the publication write gate; the
        // failed-attempt path never published one, so draining here covers both.
        await Invocation.DrainAsync().ConfigureAwait(false);

        foreach (var instance in _instances)
        {
            try
            {
                if (instance is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            // Extension teardown is a containment boundary: a disposer is the package's own code, so one
            // faulty disposer is recorded and the remaining owned values must still be released. A
            // process-fatal condition raised inside one propagates instead of being filed as a cleanup note.
#pragma warning disable CA1031
            catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
            {
                failures.Add(
                    $"{instance.GetType().FullName ?? instance.GetType().Name}: {failure.Message}");
            }
        }

        // After the disposers, because a disposer may legitimately read a cache, and before the unload,
        // because the caches hold this extension's values, factory delegates and constructed generic types.
        // Contained like every other teardown step: a namespace that will not release must not abort the
        // release of the packages after this one.
        if (_caches is not null)
        {
            try
            {
                await _caches.ReleaseAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
            {
                failures.Add($"cache namespace '{_caches.Name}': {failure.Message}");
            }
        }

        // Read rather than exchanged: the field is what roots the context, and clearing it before the
        // decision below would drop the root while claiming to retain it.
        var context = Volatile.Read(ref _context);

        if (context is null)
        {
            return failures.AsReadOnly();
        }

        if (failures.Count > 0)
        {
            // Something of this extension may still be live and reachable. Unloading now would claim it is
            // gone while rooting it forever, so the context stays rooted here and the state is reported.
            failures.Add(
                "load context: retained, because objects this extension owns could not be released and "
                + "unloading would claim they are gone.");
            return failures.AsReadOnly();
        }

        try
        {
            context.Unload();
        }
        // An Unloading handler is extension code. It may report a cleanup failure, but it cannot stop later
        // extensions from being released or turn a quarantine into a Host startup failure. The context stays
        // rooted, because a handler that threw may have left something of the extension reachable.
#pragma warning disable CA1031
        catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
        {
            failures.Add($"load context: {failure.Message}");
            return failures.AsReadOnly();
        }

        // Only now: the unload was requested and nothing objected, so this lease stops rooting it.
        Interlocked.Exchange(ref _context, null);
        return failures.AsReadOnly();
    }
}
