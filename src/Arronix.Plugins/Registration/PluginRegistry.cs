using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;
using Arronix.Plugins.Loading;

namespace Arronix.Plugins.Registration;

/// <summary>
/// The admission gate: the reverse direction of the bidirectional capability check.
/// </summary>
/// <remarks>
/// <para>
/// Every gated method asks the capability table before it records anything. That is the half of the check
/// that stops an extension registering a gated contract it never declared — without it, an extension could
/// contribute an indexer while declaring only the privilege to fold diacritics, and the manifest would be a
/// work of fiction.
/// </para>
/// <para>
/// Enforcement is admission rather than filtering. The registry never accepts something and removes it
/// later: a refused registration throws at the call site inside the extension's own configure method, which
/// quarantines the extension with a message naming the capability it should have declared. An extension is
/// never left half-configured, because a refusal aborts the whole of its configuration.
/// </para>
/// <para>
/// The registry is sealed once configuration returns. An instance that outlives the call and is written to
/// later would be a registration nothing checked the surrounding state for, so late use is refused rather
/// than tolerated.
/// </para>
/// </remarks>
public sealed class PluginRegistry : IPluginRegistry
{
    private readonly CapabilitySet _granted;
    private readonly IMediaTypeCapabilityReader? _mediaTypes;
    private readonly PackageOwnership? _ownership;
    private readonly object _gate = new();
    private bool _sealed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRegistry"/> class.
    /// </summary>
    /// <param name="plugin">The extension registering.</param>
    /// <param name="granted">The capabilities it holds, after implication.</param>
    /// <param name="ledger">Where its contributions are recorded.</param>
    /// <param name="mediaTypes">
    /// How a typed media kind is priced in capabilities. Absent only for a registry that will never see
    /// one; <see cref="AddMediaType{TType}"/> refuses rather than guessing when it is.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ledger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A registry built this way owns nothing, so every event subscription outside
    /// <see cref="PlatformEvents"/> is refused. The loader uses the ownership-bearing overload.
    /// </remarks>
    public PluginRegistry(
        PluginId plugin,
        CapabilitySet granted,
        PluginRegistrationLedger ledger,
        IMediaTypeCapabilityReader? mediaTypes = null)
        : this(plugin, granted, ledger, mediaTypes, ownership: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PluginRegistry"/> class for a loaded package.</summary>
    /// <param name="plugin">The extension registering.</param>
    /// <param name="granted">What its declaration was granted.</param>
    /// <param name="ledger">Where its registrations are recorded.</param>
    /// <param name="mediaTypes">The reader media-kind demands are read through.</param>
    /// <param name="ownership">
    /// What this package owns, which is what proves an event type is its own.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ledger"/> is <see langword="null"/>.</exception>
    internal PluginRegistry(
        PluginId plugin,
        CapabilitySet granted,
        PluginRegistrationLedger ledger,
        IMediaTypeCapabilityReader? mediaTypes,
        PackageOwnership? ownership)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        Plugin = plugin;
        _granted = granted;
        Ledger = ledger;
        _mediaTypes = mediaTypes;
        _ownership = ownership;
    }

    /// <summary>
    /// Gets the extension registering.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the record of what was registered.
    /// </summary>
    public PluginRegistrationLedger Ledger { get; }

    /// <summary>
    /// Refuses any further registration.
    /// </summary>
    /// <remarks>
    /// Called once, by the loader, when the extension's configure method returns.
    /// </remarks>
    public void Seal()
    {
        lock (_gate)
        {
            if (_sealed)
            {
                return;
            }

            Ledger.Freeze();
            _sealed = true;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The typed path holds the extension to the same bidirectional rule as the imperative one, with the
    /// kind's sections standing in for registrations. Every demand is checked before anything is recorded,
    /// so a refusal leaves no trace: the media-kind capability gates the capture itself, the required
    /// sections (parsing, matching, querying) demand their seam capabilities on every kind, and a defaulted
    /// section (quality, naming, catalog, notifications) demands one only when it differs from its default —
    /// a default section is host behavior, not a contribution.
    /// </para>
    /// <para>
    /// What is recorded satisfies exactly what was demanded, so the forward check passes for a typed-kind
    /// extension that declares precisely the capabilities its sections use, and still quarantines one that
    /// declares a capability no section accounts for.
    /// </para>
    /// <para>
    /// The demands are read through <see cref="IMediaTypeCapabilityReader"/> rather than computed here,
    /// because they are only legible after the kind's typed definition values have been compiled and that
    /// compilation is host machinery. The check itself is unchanged; only the place that can see the facts
    /// moved.
    /// </para>
    /// </remarks>
    public IPluginRegistry AddMediaType<TType>()
        where TType : class, IMediaTypeDefinition, new()
    {
        lock (_gate)
        {
            ThrowIfSealed();

            if (_mediaTypes is null)
            {
                throw new InvalidOperationException(
                    $"Extension '{Plugin}' registered the typed media kind '{typeof(TType).Name}', but this "
                    + "registry was built without a capability reader and cannot tell which capabilities the "
                    + "kind's sections demand. Registering it would grant whatever went unchecked, so it is "
                    + "refused. This is a host wiring defect, not an extension defect.");
            }

            var registration = ((IMediaTypeDefinition)new TType()).Capture();
            var requirements = _mediaTypes.Requirements(registration);

            foreach (var requirement in requirements)
            {
                if (!_granted.Has(requirement.Capability))
                {
                    throw new PluginCapabilityException(Plugin, requirement.Capability, requirement.Section);
                }
            }

            Ledger.RecordMediaType(registration, DefinitionCapabilityRules.SatisfiedBy(requirements));
            return this;
        }
    }

    /// <inheritdoc />
    public IPluginRegistry AddMediaShape(IMediaShapeProvider provider) => Admit<IMediaShapeProvider>(provider);

    /// <inheritdoc />
    public IPluginRegistry AddMediaItemSource(IMediaItemSource source) => Admit<IMediaItemSource>(source);

    /// <inheritdoc />
    public IPluginRegistry AddReleaseMatcher(IReleaseMatcher matcher) => Admit<IReleaseMatcher>(matcher);

    /// <inheritdoc />
    public IPluginRegistry AddReleaseQueryPlanner(IReleaseQueryPlanner planner) => Admit<IReleaseQueryPlanner>(planner);

    /// <inheritdoc />
    public IPluginRegistry AddReleaseParser(IReleaseParser parser) => Admit<IReleaseParser>(parser);

    /// <inheritdoc />
    public IPluginRegistry AddQualityModel(IQualityModel model) => Admit<IQualityModel>(model);

    /// <inheritdoc />
    public IPluginRegistry AddImportPipeline(IImportPipeline pipeline) => Admit<IImportPipeline>(pipeline);

    /// <inheritdoc />
    public IPluginRegistry AddRenamePolicy(IRenamePolicy policy) => Admit<IRenamePolicy>(policy);

    /// <inheritdoc />
    public IPluginRegistry AddLibraryLayout(ILibraryLayout layout) => Admit<ILibraryLayout>(layout);

    /// <inheritdoc />
    public IPluginRegistry AddMediaIdResolver(IMediaIdResolver resolver) => Admit<IMediaIdResolver>(resolver);

    /// <inheritdoc />
    public IPluginRegistry AddIndexer<TIndexer>(ProviderDescriptor descriptor)
        where TIndexer : class, IIndexer
        => AdmitProvider(ProviderTypeRegistration.For<IIndexer, TIndexer>(descriptor, ProviderFamily.Indexer), Capability.Indexing);

    /// <inheritdoc />
    public IPluginRegistry AddDownloader<TDownloader>(ProviderDescriptor descriptor)
        where TDownloader : class, IDownloader
        => AdmitProvider(ProviderTypeRegistration.For<IDownloader, TDownloader>(descriptor, ProviderFamily.Downloader), Capability.Download);

    /// <inheritdoc />
    public IPluginRegistry AddNotifier<TNotifier>(ProviderDescriptor descriptor)
        where TNotifier : class, INotifier
        => AdmitProvider(ProviderTypeRegistration.For<INotifier, TNotifier>(descriptor, ProviderFamily.Notifier), Capability.Notification);

    /// <inheritdoc />
    public IPluginRegistry AddCataloger<TCataloger>(ProviderDescriptor descriptor)
        where TCataloger : class, IClosedCataloger
        => AdmitProvider(ProviderTypeRegistration.ForCataloger<TCataloger>(descriptor), Capability.Metadata);

    /// <inheritdoc />
    public IPluginRegistry AddCurator<TCurator>(ProviderDescriptor descriptor)
        where TCurator : class, IClosedCurator
        => AdmitProvider(ProviderTypeRegistration.ForCurator<TCurator>(descriptor), Capability.Curation);

    /// <inheritdoc />
    public IPluginRegistry AddScheduledJob(IScheduledJob job, string schedule)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule);

        lock (_gate)
        {
            ThrowIfSealed();
            Ledger.RecordScheduledJob(job, schedule);
            return this;
        }
    }

    /// <inheritdoc />
    public IPluginRegistry AddHealthContributor(IHealthContributor contributor) => Admit<IHealthContributor>(contributor);

    /// <inheritdoc />
    public IPluginRegistry AddTelemetryEnricher(ITelemetryEnricher enricher) => Admit<ITelemetryEnricher>(enricher);

    /// <inheritdoc />
    public IPluginRegistry AddTelemetryEventFilter(ITelemetryEventFilter filter) => Admit<ITelemetryEventFilter>(filter);

    /// <inheritdoc />
    public IPluginRegistry AddTelemetrySink(ITelemetrySink sink) => Admit<ITelemetrySink>(sink);

    /// <inheritdoc />
    public IPluginRegistry AddRedactionRules(IRedactionRuleProvider provider) => Admit<IRedactionRuleProvider>(provider);

    /// <inheritdoc />
    public IPluginRegistry AddDiacriticFolding(IDiacriticFoldingProvider provider)
        => Admit<IDiacriticFoldingProvider>(provider);

    /// <inheritdoc />
    public IPluginRegistry AddLanguage<TLanguage>()
        where TLanguage : class, ILanguageDefinition
    {
        lock (_gate)
        {
            ThrowIfSealed();

            if (!_granted.Has(Capability.Language))
            {
                throw new PluginCapabilityException(Plugin, Capability.Language, nameof(ILanguageDefinition));
            }

            Ledger.RecordLanguage(LanguageDefinitionRegistration.For<TLanguage>());
            return this;
        }
    }

    /// <inheritdoc />
    public IPluginRegistry AddOutboundHttpInterceptor(IOutboundHttpInterceptor interceptor)
        => Admit<IOutboundHttpInterceptor>(interceptor);

    /// <inheritdoc />
    /// <remarks>
    /// A subscription names one exact event the extension may see: an event on <see cref="PlatformEvents"/>
    /// or one an assembly this package owns declares. An interface or abstract type would subscribe to every
    /// event derived from it — <c>IDomainEvent</c> to the whole bus — and unproved ownership is refused
    /// rather than assumed. The invocation delegate is captured here, while <typeparamref name="TEvent"/> is
    /// still a type argument, so dispatch calls a delegate instead of rediscovering <c>HandleAsync</c>.
    /// </remarks>
    /// <exception cref="PluginCapabilityException">
    /// The subscription names a class of events rather than one event.
    /// </exception>
    /// <exception cref="PluginIsolationException">
    /// The event belongs to another extension, or ownership cannot be established.
    /// </exception>
    public IPluginRegistry AddEventHandler<TEvent>(IEventHandler<TEvent> handler)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);

        lock (_gate)
        {
            ThrowIfSealed();
            AdmitSubscription(eventType);
            Ledger.RecordEventHandler(
                eventType,
                handler,
                (domainEvent, token) => handler.HandleAsync((TEvent)domainEvent, token));
            return this;
        }
    }

    /// <inheritdoc />
    public IPluginRegistry AddIntentSurface(PluginIntentSurface surface) => Admit<PluginIntentSurface>(surface);

    /// <summary>
    /// Checks the grant, then records the contribution.
    /// </summary>
    /// <typeparam name="TContract">The contract being registered under.</typeparam>
    /// <param name="instance">The already-constructed instance.</param>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instance"/> is <see langword="null"/>.</exception>
    /// <exception cref="PluginCapabilityException">The extension did not declare a capability covering it.</exception>
    /// <exception cref="InvalidOperationException">Configuration has already returned.</exception>
    private PluginRegistry Admit<TContract>(TContract instance)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_gate)
        {
            ThrowIfSealed();

            var contract = typeof(TContract);

            if (!CapabilityMatrix.IsPermitted(_granted, contract))
            {
                throw new PluginCapabilityException(
                    Plugin,
                    CapabilityMatrix.RequirementToReport(contract),
                    contract.Name);
            }

            Ledger.Record(contract, instance);
            return this;
        }
    }

    /// <summary>Holds one subscription to what it names, and to what this extension may see.</summary>
    private void AdmitSubscription(Type eventType)
    {
        if (eventType.IsInterface || eventType.IsAbstract)
        {
            throw new PluginCapabilityException(
                $"Extension '{Plugin}' subscribed to '{eventType.Name}', which is not a concrete event. "
                + "Subscribing to an interface or an abstract base subscribes to every event derived from "
                + "it, including facts raised by the host and by other extensions.");
        }

        if (eventType.ContainsGenericParameters)
        {
            throw new PluginCapabilityException(
                $"Extension '{Plugin}' subscribed to the open generic type '{eventType.Name}'. A "
                + "subscription names one closed event type.");
        }

        if (PlatformEvents.Admits(eventType))
        {
            return;
        }

        if (PlatformEvents.IsContractEvent(eventType))
        {
            throw new PluginIsolationException(
                $"Extension '{Plugin}' subscribed to the platform event '{eventType.Name}', which is not on "
                + "the list of platform events extensions may receive.");
        }

        if (_ownership is null)
        {
            throw new PluginIsolationException(
                $"Extension '{Plugin}' subscribed to '{eventType.Name}' and the host holds no record of what "
                + "this package owns, so the type cannot be proved to be the extension's.");
        }

        if (!_ownership.Owns(eventType))
        {
            throw new PluginIsolationException(
                $"Extension '{Plugin}' subscribed to '{eventType.Name}', which is declared by "
                + $"'{eventType.Assembly.GetName().Name}'. A package owns its entry assembly and the "
                + "contracts it publishes; an assembly it can merely see belongs to whoever published it.");
        }
    }

    private PluginRegistry AdmitProvider(ProviderTypeRegistration registration, Capability capability)
    {
        ArgumentNullException.ThrowIfNull(registration);

        lock (_gate)
        {
            ThrowIfSealed();

            if (!_granted.Has(capability))
            {
                throw new PluginCapabilityException(Plugin, capability, registration.Family.ToString());
            }

            Ledger.RecordProvider(registration, capability);
            return this;
        }
    }

    private void ThrowIfSealed()
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                $"Extension '{Plugin}' attempted to register after its configuration returned. Everything an extension contributes must be registered inside its configure method.");
        }
    }
}
