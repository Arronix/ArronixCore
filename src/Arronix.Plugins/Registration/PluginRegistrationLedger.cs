using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;

#pragma warning disable ARX0004 // Event contracts are experimental; this assembly records registrations of them.
#pragma warning disable ARX0014 // The extension model is experimental; this assembly implements it.
#pragma warning disable ARX0020 // The typed media surface is experimental; this assembly records registrations of it.

namespace Arronix.Plugins.Registration;

/// <summary>
/// One thing an extension contributed.
/// </summary>
/// <param name="Contract">The contract it was registered under.</param>
/// <param name="Instance">The already-constructed instance.</param>
/// <param name="Ordinal">Its position in the extension's registration order.</param>
public readonly record struct LedgerEntry(Type Contract, object Instance, int Ordinal);

/// <summary>
/// A background job and when it runs.
/// </summary>
/// <param name="Job">The job.</param>
/// <param name="Schedule">The schedule text, parsed by the scheduler rather than here.</param>
public sealed record ScheduledJobRegistration(IScheduledJob Job, string Schedule);

/// <summary>
/// A subscription to a platform event.
/// </summary>
/// <param name="EventType">The event type subscribed to.</param>
/// <param name="Handler">The handler, typed as an object because the ledger is not generic.</param>
public sealed record EventHandlerRegistration(Type EventType, object Handler);

/// <summary>
/// Everything one extension registered, in the order it registered it.
/// </summary>
/// <remarks>
/// <para>
/// The ledger is what makes the forward half of the capability check possible. Without a record of what was
/// registered there is no way to ask whether a declared capability was ever used, and a capability nobody
/// exercises is either a mistake in the declaration or a privilege granted for nothing — both worth
/// refusing.
/// </para>
/// <para>
/// Registration order is preserved because the order an extension declares its contributions in is the
/// order it means them to apply. A ledger that returned a set would silently reorder a policy chain.
/// </para>
/// </remarks>
public sealed class PluginRegistrationLedger
{
    private readonly List<LedgerEntry> _entries = [];
    private readonly List<ScheduledJobRegistration> _jobs = [];
    private readonly List<EventHandlerRegistration> _handlers = [];
    private CapabilitySet _satisfied = CapabilitySet.None;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRegistrationLedger"/> class.
    /// </summary>
    /// <param name="plugin">The extension whose contributions are recorded.</param>
    public PluginRegistrationLedger(PluginId plugin) => Plugin = plugin;

    /// <summary>
    /// Gets the extension whose contributions are recorded.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets everything that was registered, in registration order.
    /// </summary>
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>
    /// Gets the background jobs, in registration order.
    /// </summary>
    public IReadOnlyList<ScheduledJobRegistration> ScheduledJobs => _jobs;

    /// <summary>
    /// Gets the event subscriptions, in registration order.
    /// </summary>
    public IReadOnlyList<EventHandlerRegistration> EventHandlers => _handlers;

    /// <summary>
    /// Gets how many things were registered.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the capabilities the recorded registrations account for.
    /// </summary>
    /// <remarks>
    /// A contract that accepts any of several capabilities marks all of them accounted for. The alternative
    /// — guessing which one the extension meant — would report a false unsatisfied capability for a
    /// declaration that is in fact honest.
    /// </remarks>
    public CapabilitySet SatisfiedCapabilities => _satisfied;

    /// <summary>
    /// Gets everything registered under one contract, in registration order.
    /// </summary>
    /// <typeparam name="TContract">The contract.</typeparam>
    /// <returns>The instances.</returns>
    public IReadOnlyList<TContract> Registered<TContract>()
        where TContract : class
        => _entries
            .Where(entry => entry.Contract == typeof(TContract))
            .Select(entry => (TContract)entry.Instance)
            .ToArray();

    /// <summary>
    /// Gets the single thing registered under one contract.
    /// </summary>
    /// <typeparam name="TContract">The contract.</typeparam>
    /// <returns>The instance, or <see langword="null"/> when nothing was registered under it.</returns>
    /// <exception cref="InvalidOperationException">More than one instance was registered under it.</exception>
    public TContract? Single<TContract>()
        where TContract : class
    {
        var registered = Registered<TContract>();

        return registered.Count switch
        {
            0 => null,
            1 => registered[0],
            _ => throw new InvalidOperationException(
                $"Extension '{Plugin}' registered {registered.Count} instances of '{typeof(TContract).Name}' where at most one is meaningful.")
        };
    }

    /// <summary>
    /// Determines whether every forward-checkable capability an extension declared was actually used.
    /// </summary>
    /// <param name="declared">The capabilities exactly as declared, before implication.</param>
    /// <param name="unsatisfied">The declared capabilities nothing accounted for.</param>
    /// <returns><see langword="true"/> when every checkable declaration was used.</returns>
    /// <remarks>
    /// Only capabilities that some registration <i>could</i> account for are checked. Making outbound calls
    /// and reading files are privileges to use something rather than to contribute anything, so there is no
    /// registration that could ever satisfy them and requiring one would make the check a lie.
    /// </remarks>
    public bool TryVerifyDeclaredCapabilities(CapabilitySet declared, out IReadOnlyList<Capability> unsatisfied)
    {
        var missing = new List<Capability>();

        foreach (var capability in declared.Enumerate())
        {
            if (CapabilityMatrix.ForwardCheckableCapabilities.Contains(capability) && !_satisfied.Has(capability))
            {
                missing.Add(capability);
            }
        }

        unsatisfied = missing;
        return missing.Count == 0;
    }

    internal void Record(Type contract, object instance)
    {
        _entries.Add(new LedgerEntry(contract, instance, _entries.Count));

        if (CapabilityMatrix.RegistrationRequirements.TryGetValue(contract, out var required))
        {
            _satisfied = _satisfied.Union(CapabilitySet.Of(required));
        }
    }

    /// <summary>
    /// Records a captured typed media kind, together with the capabilities its sections account for.
    /// </summary>
    /// <param name="registration">The captured registration.</param>
    /// <param name="satisfied">
    /// The capabilities the kind's sections satisfy, computed by
    /// <see cref="DefinitionCapabilityRules.SatisfiedBy(IReadOnlyList{DefinitionSectionRequirement})"/>. A
    /// media kind is one entry in the ledger but many contributions in substance — its parsing section is
    /// the release parser, its querying section the query planner — and the forward check must see all of
    /// them accounted for, not only the media-kind gate the entry itself sits behind.
    /// </param>
    internal void RecordMediaType(IMediaTypeRegistration registration, CapabilitySet satisfied)
    {
        Record(typeof(IMediaTypeRegistration), registration);
        _satisfied = _satisfied.Union(satisfied);
    }

    internal void RecordScheduledJob(IScheduledJob job, string schedule)
    {
        _jobs.Add(new ScheduledJobRegistration(job, schedule));
        Record(typeof(IScheduledJob), job);
    }

    internal void RecordEventHandler(Type eventType, object handler)
    {
        _handlers.Add(new EventHandlerRegistration(eventType, handler));
        _entries.Add(new LedgerEntry(typeof(IEventHandler<>), handler, _entries.Count));
    }
}
