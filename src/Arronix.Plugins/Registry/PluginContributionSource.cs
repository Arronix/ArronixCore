using System.Linq;
using System.Runtime.Loader;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Contributions;
using Arronix.Plugins.Loading;

namespace Arronix.Plugins.Registry;

/// <summary>
/// Projects the Active extension registry into leased contributions for the platform's dispatch paths.
/// </summary>
/// <remarks>
/// Selection happens under the publication read gate and every selected runtime is leased before the gate
/// is released, so a runtime cannot be withdrawn between being chosen and being called. Withdrawal closes a
/// runtime and removes its published result under one write lease, so an Active result whose lifetime
/// refuses a lease is a broken invariant and is reported as one.
/// </remarks>
internal sealed class PluginContributionSource : IPluginContributionSource
{
    private static readonly System.Reflection.Assembly ContractAssembly = typeof(IDomainEvent).Assembly;

    private readonly PluginRuntimeRegistry _registry;

    internal PluginContributionSource(PluginRuntimeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public IContributionLease<TContract> Acquire<TContract>()
        where TContract : class
        => Select<TContract, object?>(
            static (result, _) => result.Ledger is not { } ledger
                ? []
                :
                [
                    .. ledger.Entries
                        .Where(entry => entry.Contract == typeof(TContract))
                        .Select(entry => (entry.Ordinal, (TContract)entry.Instance)),
                ],
            state: null);

    /// <inheritdoc />
    public IContributionLease<EventHandlerContribution> AcquireEventHandlers(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return Select<EventHandlerContribution, Type>(
            static (result, published) =>
            {
                if (result.Ledger is not { } ledger || !MaySubscribe(result, published))
                {
                    return [];
                }

                return
                [
                    .. ledger.EventHandlers
                        .Where(handler => handler.EventType.IsAssignableFrom(published))
                        .Select(handler => (
                            handler.Ordinal,
                            new EventHandlerContribution(handler.Handler, handler.EventType, handler.Invoke))),
                ];
            },
            eventType);
    }

    /// <inheritdoc />
    public bool TryAcquireOwner(Type type, out PluginId owner, out IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(type);

        var context = AssemblyLoadContext.GetLoadContext(type.Assembly);

        if (context is null || ReferenceEquals(context, AssemblyLoadContext.Default))
        {
            owner = default;
            lease = null;
            return false;
        }

        using (_registry.PublicationGate.EnterRead())
        {
            foreach (var result in _registry.Active)
            {
                if (!ReferenceEquals(result.LoadContext, context) || result.RuntimeLease is not { } runtime)
                {
                    continue;
                }

                owner = RequireOwner(result);
                lease = RequireLease(result, runtime);
                return true;
            }
        }

        owner = default;
        lease = null;
        return false;
    }

    /// <summary>
    /// The subscription boundary: an extension sees platform-contract events and its own, never another's.
    /// </summary>
    private static bool MaySubscribe(PluginLoadResult result, Type eventType)
        => eventType.Assembly == ContractAssembly
            || result.LoadContext is null
            || ReferenceEquals(AssemblyLoadContext.GetLoadContext(eventType.Assembly), result.LoadContext);

    private static PluginId RequireOwner(PluginLoadResult result)
        => result.Id
            ?? throw new InvalidOperationException(
                $"The active extension at '{result.Source}' contributes without carrying an identifier.");

    /// <summary>
    /// Takes the lease an Active runtime must be able to give.
    /// </summary>
    /// <remarks>
    /// Withdrawal closes the lifetime and replaces the published result inside one publication write lease,
    /// so no reader can see one without the other. A refusal here means something closed a live runtime
    /// outside that transition, which would let teardown unload code a caller is about to invoke.
    /// </remarks>
    private static IDisposable RequireLease(PluginLoadResult result, PluginRuntimeLease runtime)
    {
        if (runtime.Invocation.TryEnter(out var lease))
        {
            return lease!;
        }

        throw new InvalidOperationException(
            $"Extension '{result.Id?.ToString() ?? result.Source}' is published as Active while its "
            + "invocation lifetime is closed. Closing and unpublishing are one transition under the "
            + "publication write gate, so this is a lifecycle defect rather than an ordinary race.");
    }

    /// <summary>
    /// Selects from every active runtime under the read gate, leasing each runtime that contributes.
    /// </summary>
    private CompositeLease<TValue> Select<TValue, TState>(
        Func<PluginLoadResult, TState, IReadOnlyList<(int Ordinal, TValue Value)>> select,
        TState state)
    {
        var contributions = new List<PluginContribution<TValue>>();
        var leases = new List<IDisposable>();

        try
        {
            using (_registry.PublicationGate.EnterRead())
            {
                // Active is ordered by identifier then by folder, and each extension's own entries carry
                // their registration ordinal, so dispatch order is a property of the installation rather
                // than of whichever thread got here first.
                foreach (var result in _registry.Active)
                {
                    var found = select(result, state);

                    if (found.Count == 0)
                    {
                        continue;
                    }

                    // A package with no executable half has no runtime to lease and nothing to contribute.
                    if (result.RuntimeLease is not { } runtime)
                    {
                        continue;
                    }

                    var owner = RequireOwner(result);
                    leases.Add(RequireLease(result, runtime));

                    contributions.AddRange(
                        found
                            .OrderBy(entry => entry.Ordinal)
                            .Select(entry => new PluginContribution<TValue>(owner, entry.Ordinal, entry.Value)));
                }
            }
        }
        catch
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }

            throw;
        }

        return new CompositeLease<TValue>(contributions, leases);
    }

    private sealed class CompositeLease<TValue>(
        IReadOnlyList<PluginContribution<TValue>> contributions,
        IReadOnlyList<IDisposable> leases) : IContributionLease<TValue>
    {
        private int _disposed;

        public IReadOnlyList<PluginContribution<TValue>> Contributions { get; } = contributions;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }
    }
}
