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
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;

namespace Arronix.Abstractions.Plugins;

/// <summary>
/// The only way an extension contributes anything.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no general-purpose add method, no service container and no assembly scan.
/// Registering something an extension did not declare is not a thing that can be expressed, which moves
/// enforcement from filtering afterwards to admission at the point of registration — the gate that makes
/// least privilege mechanical rather than aspirational.
/// </para>
/// <para>
/// Provider contributions are implementation types, not instances. After manifest and capability admission,
/// Host constructs them through an exact public <c>(IPluginContext)</c> constructor, or a public parameterless
/// constructor when no context is needed. Host DI is never consulted. Per-invocation configuration stays on
/// <see cref="ProviderInvocation"/> rather than mutable singleton state. The instance-taking media-engine
/// methods below are temporary compatibility seams for media kinds that have not completed typed
/// conversion.
/// </para>
/// <para>
/// The cost is stated plainly: a new seam needs a new method here, which is a host change. It is additive
/// under the stability policy, and it is the price of being able to answer "what can this extension
/// contribute?" by reading one interface.
/// </para>
/// </remarks>
public interface IPluginRegistry
{
    /// <summary>
    /// Registers a complete media kind as a pair of types. Requires <see cref="Capability.MediaKind"/>.
    /// </summary>
    /// <typeparam name="TType">The complete typed media definition.</typeparam>
    /// <returns>This registry, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The typed registration path, and the only way a media kind's structure enters the host. Nothing is
    /// passed: the definition type <i>is</i> the declaration. The host reads its typed override values,
    /// derives the shape, intent surface and naming tokens from the item type, and builds every media engine from the result —
    /// so the descriptors every engine reads have one source of truth and it is the entity.
    /// </para>
    /// <para>
    /// The per-seam registrations below remain for kinds that have not converted. Both paths feed one host
    /// pipeline, and a seam is deleted only when its last imperative implementer converts.
    /// </para>
    /// <para>
    /// A typed kind ships code, so — unlike the string-declaration surface this replaces — its assembly is
    /// <b>not</b> eligible for unload once the declaration is captured, and network privilege is not
    /// structurally ungrantable. Capability enforcement for a typed kind rests on the manifest and the
    /// loader, exactly as it does for every other extension that ships code.
    /// </para>
    /// </remarks>
    IPluginRegistry AddMediaType<TType>()
        where TType : class, IMediaTypeDefinition, new();

    /// <summary>
    /// Registers the structure of a media kind. Requires <see cref="Capability.MediaKind"/>.
    /// </summary>
    /// <param name="provider">The shape provider.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddMediaShape(IMediaShapeProvider provider);

    /// <summary>
    /// Registers the catalog of a media kind. Requires <see cref="Capability.MediaKind"/>.
    /// </summary>
    /// <param name="source">The catalog source.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddMediaItemSource(IMediaItemSource source);

    /// <summary>
    /// Registers the seam that decides which items a release or a file refers to. Requires
    /// <see cref="Capability.Matching"/>.
    /// </summary>
    /// <param name="matcher">The matcher.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddReleaseMatcher(IReleaseMatcher matcher);

    /// <summary>
    /// Registers the seam that turns an acquisition into queries. Requires
    /// <see cref="Capability.Indexing"/>.
    /// </summary>
    /// <param name="planner">The planner.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddReleaseQueryPlanner(IReleaseQueryPlanner planner);

    /// <summary>
    /// Registers release-name parsing. Requires <see cref="Capability.Parsing"/>.
    /// </summary>
    /// <param name="parser">The parser.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddReleaseParser(IReleaseParser parser);

    /// <summary>
    /// Registers quality evaluation. Requires <see cref="Capability.Quality"/>.
    /// </summary>
    /// <param name="model">The quality model.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddQualityModel(IQualityModel model);

    /// <summary>
    /// Registers the pipeline that takes files into the library. Requires
    /// <see cref="Capability.Import"/>.
    /// </summary>
    /// <param name="pipeline">The import pipeline.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddImportPipeline(IImportPipeline pipeline);

    /// <summary>
    /// Registers naming templates. Requires <see cref="Capability.Renaming"/>.
    /// </summary>
    /// <param name="policy">The rename policy.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddRenamePolicy(IRenamePolicy policy);

    /// <summary>
    /// Registers folder layout. Requires <see cref="Capability.Renaming"/>.
    /// </summary>
    /// <param name="layout">The library layout.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddLibraryLayout(ILibraryLayout layout);

    /// <summary>
    /// Registers external-identifier resolution. Requires <see cref="Capability.Metadata"/>.
    /// </summary>
    /// <param name="resolver">The identifier resolver.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddMediaIdResolver(IMediaIdResolver resolver);

    /// <summary>
    /// Registers a release source. Requires <see cref="Capability.Indexing"/>.
    /// </summary>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddIndexer<TIndexer>(ProviderDescriptor descriptor)
        where TIndexer : class, IIndexer;

    /// <summary>
    /// Registers a transfer client. Requires <see cref="Capability.Download"/>.
    /// </summary>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddDownloader<TDownloader>(ProviderDescriptor descriptor)
        where TDownloader : class, IDownloader;

    /// <summary>
    /// Registers a notification destination. Requires <see cref="Capability.Notification"/>.
    /// </summary>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddNotifier<TNotifier>(ProviderDescriptor descriptor)
        where TNotifier : class, INotifier;

    /// <summary>
    /// Registers a cataloger. Requires <see cref="Capability.Metadata"/>.
    /// </summary>
    /// <typeparam name="TCataloger">The implementation, which closed <see cref="ICataloger{TItem}"/> over its item type.</typeparam>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>This registry, for chaining.</returns>
    /// <remarks>
    /// The item type is not repeated here. It is read from the closed contract the implementation actually
    /// implements, so the pairing has one authority. The constraint makes the ordinary mistakes compiler
    /// errors; an implementation that closed no cataloger contract, or closed several, is refused here,
    /// inside the extension's own configure method.
    /// </remarks>
    IPluginRegistry AddCataloger<TCataloger>(ProviderDescriptor descriptor)
        where TCataloger : class, IClosedCataloger;

    /// <summary>
    /// Registers a curator. Requires <see cref="Capability.Curation"/>.
    /// </summary>
    /// <typeparam name="TCurator">The implementation, which closed <see cref="ICurator{TItem}"/> over its item type.</typeparam>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddCurator<TCurator>(ProviderDescriptor descriptor)
        where TCurator : class, IClosedCurator;

    /// <summary>
    /// Registers a background job. Ungated.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="schedule">
    /// When it runs: <c>manual</c>, <c>startup</c>, <c>every &lt;duration&gt;</c> or
    /// <c>daily &lt;HH:mm&gt;</c>. Parsed by the host; an unparseable schedule is a load failure.
    /// </param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddScheduledJob(IScheduledJob job, string schedule);

    /// <summary>
    /// Registers a health check. Ungated: reporting on yourself needs no privilege.
    /// </summary>
    /// <param name="contributor">The contributor. Its check identifiers are namespaced by the host.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddHealthContributor(IHealthContributor contributor);

    /// <summary>
    /// Registers telemetry enrichment. Requires <see cref="Capability.TelemetryProcessing"/>: an enricher
    /// reads and rewrites the events it is offered, and the seam offers only this extension's own.
    /// </summary>
    /// <param name="enricher">The enricher.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddTelemetryEnricher(ITelemetryEnricher enricher);

    /// <summary>
    /// Registers a telemetry filter. Requires <see cref="Capability.TelemetryProcessing"/>: a filter
    /// decides whether anyone sees an event. It is asked only about this extension's own events below
    /// <see cref="Telemetry.TelemetrySeverity.Error"/>, and never about another extension's or the host's.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddTelemetryEventFilter(ITelemetryEventFilter filter);

    /// <summary>
    /// Registers a telemetry destination. Requires <see cref="Capability.TelemetrySink"/> and an operator
    /// naming this extension in the host's trusted-sink setting, because a sink reads the whole
    /// post-redaction stream — every extension's events and the host's — and may take it anywhere.
    /// </summary>
    /// <param name="sink">The destination.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddTelemetrySink(ITelemetrySink sink);

    /// <summary>
    /// Registers redaction rules. Ungated, because it can only ever remove information; the privilege is
    /// held by whoever owns the secret being redacted.
    /// </summary>
    /// <param name="provider">The rule provider.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddRedactionRules(IRedactionRuleProvider provider);

    /// <summary>
    /// Registers diacritic folding. Requires <see cref="Capability.Parsing"/> or
    /// <see cref="Capability.Renaming"/>.
    /// </summary>
    /// <param name="provider">The folding provider.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddDiacriticFolding(IDiacriticFoldingProvider provider);

    /// <summary>
    /// Registers a language implementation type for host-owned activation. Requires
    /// <see cref="Capability.Language"/>.
    /// </summary>
    /// <typeparam name="TLanguage">The language implementation.</typeparam>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddLanguage<TLanguage>()
        where TLanguage : class, ILanguageDefinition;

    /// <summary>
    /// Registers an outbound call interceptor. Requires <see cref="Capability.Indexing"/>.
    /// </summary>
    /// <param name="interceptor">The interceptor.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddOutboundHttpInterceptor(IOutboundHttpInterceptor interceptor);

    /// <summary>
    /// Subscribes to one exact event. Ungated, and filtered: an extension sees the platform events the host
    /// admits and the ones its own package declares, never another extension's.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The exact event type. It must be a concrete closed type — an interface or abstract base would
    /// subscribe to every event derived from it — declared either by the host on its admitted list or by an
    /// assembly this package owns: its entry assembly or a contract assembly it publishes. Delivery is by
    /// that exact type, so a base subscription never receives a derived event.
    /// </typeparam>
    /// <param name="handler">The handler.</param>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="PluginCapabilityException">
    /// <typeparamref name="TEvent"/> names a class of events rather than one event.
    /// </exception>
    /// <remarks>
    /// An event type belonging to another package is refused as an isolation violation, carrying
    /// <see cref="CoreErrorCode.PluginIsolationViolation"/>.
    /// </remarks>
    IPluginRegistry AddEventHandler<TEvent>(IEventHandler<TEvent> handler)
        where TEvent : IDomainEvent;

    /// <summary>
    /// Registers what the extension declares about how its media kind is worked with. Ungated, and data
    /// only: an extension contributes declarations, never code, to any interface.
    /// </summary>
    /// <param name="surface">The declared surface.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddIntentSurface(PluginIntentSurface surface);
}
