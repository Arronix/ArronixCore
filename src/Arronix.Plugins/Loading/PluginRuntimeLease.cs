using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;


namespace Arronix.Plugins.Loading;

/// <summary>
/// Owns every extension-created runtime object and the collectible context which contains its code.
/// </summary>
/// <remarks>
/// Registered values are disposed once each, by reference identity, in reverse registration order; the
/// module follows them and load-context unload is attempted last. Asynchronous disposal is preferred when
/// an object implements both disposal contracts. The synchronous loader waits for this sequence on
/// quarantine; Host shutdown awaits it without blocking.
/// </remarks>
internal sealed class PluginRuntimeLease
{
    private readonly IReadOnlyList<object> _instances;
    private readonly TokenRegistry.TokenClaimPlan? _tokenClaims;
    private readonly IPluginAdmissionAttempt? _admissionAttempt;
    private PluginLoadContext? _context;
    private int _disposed;

    internal PluginRuntimeLease(
        PluginLoadContext context,
        PluginRegistrationLedger? ledger,
        IPluginModule? module,
        TokenRegistry.TokenClaimPlan? tokenClaims = null,
        IPluginAdmissionAttempt? admissionAttempt = null)
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
    }

    /// <summary>Gets the collectible context while the lease is active.</summary>
    internal PluginLoadContext? LoadContext => _context;

    /// <summary>Gets the exact Host admission receipt coupled to this runtime lifetime.</summary>
    internal IPluginAdmissionAttempt? AdmissionAttempt => _admissionAttempt;

    /// <summary>Withdraws exactly the naming-token claims published by this runtime attempt.</summary>
    internal void UnpublishTokenClaims() => _tokenClaims?.Rollback();

    /// <summary>Disposes every owned value and unloads the collectible context.</summary>
    /// <returns>Every cleanup failure, after all remaining values were still attempted.</returns>
    internal async ValueTask<IReadOnlyList<string>> DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return [];
        }

        var failures = new List<string>();
        UnpublishTokenClaims();

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
// Extension teardown is a containment boundary: a disposer is the package's own code, so one faulty
// disposer is recorded and the remaining owned values and load context must still be released. It is the
// package-code rule rather than the file-boundary one, because a disposer may throw any type at all — but
// it is still a rule, so a process-fatal condition raised inside one propagates instead of being filed as
// a cleanup note.
#pragma warning disable CA1031
            catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
            {
                failures.Add(
                    $"{instance.GetType().FullName ?? instance.GetType().Name}: {failure.Message}");
            }
        }

        var context = Interlocked.Exchange(ref _context, null);
        if (context is not null)
        {
            try
            {
                context.Unload();
            }
// An Unloading handler is extension code. It is allowed to report a cleanup failure, but it cannot stop
// later extensions from being released or turn a quarantine into a Host startup failure. The same limit
// applies as above: a handler that exhausts the process is not a cleanup note.
#pragma warning disable CA1031
            catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
            {
                failures.Add($"load context: {failure.Message}");
            }
        }

        return failures.AsReadOnly();
    }

    /// <summary>Waits synchronously for cleanup when the synchronous load pipeline quarantines a plugin.</summary>
    internal IReadOnlyList<string> DisposeSynchronously()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
