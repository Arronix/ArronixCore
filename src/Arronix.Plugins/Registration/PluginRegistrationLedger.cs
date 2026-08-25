using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Scheduling;
using Arronix.Common.Contributions;


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
/// <param name="Invoke">
/// Calls the handler with an event of that type. Captured where the type argument is still known, so
/// dispatch never rediscovers <c>HandleAsync</c> by reflection.
/// </param>
/// <param name="Ordinal">Its position in the extension's registration order.</param>
public sealed record EventHandlerRegistration(
    Type EventType,
    object Handler,
    Func<IDomainEvent, CancellationToken, Task> Invoke,
    int Ordinal);

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
    private IReadOnlyList<LedgerEntry>? _frozenEntries;
    private IReadOnlyList<ScheduledJobRegistration>? _frozenJobs;
    private IReadOnlyList<EventHandlerRegistration>? _frozenHandlers;
    private bool _frozen;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRegistrationLedger"/> class.
    /// </summary>
    /// <param name="plugin">The extension whose contributions are recorded.</param>
    public PluginRegistrationLedger(PluginId plugin) => Plugin = plugin;

    /// <summary>
    /// Gets the extension whose contributions are recorded.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>Gets the capability-scoped context supplied when registered provider types are activated.</summary>
    public IPluginContext? ActivationContext { get; internal set; }

    /// <summary>
    /// Gets this extension's licence to be called, which every Host candidate built from this ledger takes
    /// so that a runtime call into one of these objects can be waited for at teardown.
    /// </summary>
    internal IInvocationLifetime? Invocation { get; set; }

    /// <summary>
    /// Gets everything that was registered, in registration order.
    /// </summary>
    public IReadOnlyList<LedgerEntry> Entries => _frozenEntries ?? _entries.ToArray();

    /// <summary>
    /// Gets the background jobs, in registration order.
    /// </summary>
    public IReadOnlyList<ScheduledJobRegistration> ScheduledJobs => _frozenJobs ?? _jobs.ToArray();

    /// <summary>
    /// Gets the event subscriptions, in registration order.
    /// </summary>
    public IReadOnlyList<EventHandlerRegistration> EventHandlers => _frozenHandlers ?? _handlers.ToArray();

    /// <summary>
    /// Gets how many things were registered.
    /// </summary>
    public int Count => _frozenEntries?.Count ?? _entries.Count;

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
        => Entries
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
        ThrowIfFrozen();
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

    internal void RecordProvider(ProviderTypeRegistration registration, Capability capability)
    {
        Record(typeof(ProviderTypeRegistration), registration);
        _satisfied = _satisfied.Union(CapabilitySet.Of(capability));
    }

    internal void RecordLanguage(LanguageDefinitionRegistration registration)
    {
        Record(typeof(LanguageDefinitionRegistration), registration);
        _satisfied = _satisfied.Union(CapabilitySet.Of(Capability.Language));
    }

    internal void RecordScheduledJob(IScheduledJob job, string schedule)
    {
        ThrowIfFrozen();
        _jobs.Add(new ScheduledJobRegistration(job, schedule));
        Record(typeof(IScheduledJob), job);
    }

    internal void RecordEventHandler(
        Type eventType,
        object handler,
        Func<IDomainEvent, CancellationToken, Task> invoke)
    {
        ThrowIfFrozen();
        _handlers.Add(new EventHandlerRegistration(eventType, handler, invoke, _entries.Count));
        _entries.Add(new LedgerEntry(typeof(IEventHandler<>), handler, _entries.Count));
    }

    /// <summary>Freezes immutable snapshots before the load pipeline begins reading the ledger.</summary>
    internal void Freeze()
    {
        if (_frozen)
        {
            return;
        }

        _frozenEntries = Array.AsReadOnly(_entries.ToArray());
        _frozenJobs = Array.AsReadOnly(_jobs.ToArray());
        _frozenHandlers = Array.AsReadOnly(_handlers.ToArray());
        _frozen = true;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException(
                $"Extension '{Plugin}' attempted to mutate its registration ledger after configuration ended.");
        }
    }
}
