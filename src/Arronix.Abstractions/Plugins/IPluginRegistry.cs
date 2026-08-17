using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
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
/// Instances arrive already constructed. The extension builds its own objects from its context and the
/// host never activates an extension type through a container. That is the direct fix for a surveyed
/// pattern in which per-instance configuration is written onto a container-resolved singleton before each
/// call, which is racy under a unified host and would make capability gating racy with it.
/// </para>
/// <para>
/// The cost is stated plainly: a new seam needs a new method here, which is a host change. It is additive
/// under the stability policy, and it is the price of being able to answer "what can this extension
/// contribute?" by reading one interface.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Plugins, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IPluginRegistry
{
    /// <summary>
    /// Registers a complete media kind as a pair of types. Requires <see cref="Capability.MediaKind"/>.
    /// </summary>
    /// <typeparam name="TItem">The kind's item type: an entity whose properties and attributes are the schema.</typeparam>
    /// <typeparam name="TType">The type declaring what the item's attributes cannot.</typeparam>
    /// <returns>This registry, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The typed registration path, and the only way a media kind's structure enters the host. Nothing is
    /// passed: the two type arguments <i>are</i> the declaration. The host replays
    /// <see cref="IMediaType{TItem}.Configure"/> against its own builder, derives the shape, the intent
    /// surface and the naming tokens from the item type, and builds every media engine from the result —
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
    IPluginRegistry AddMediaType<TItem, TType>()
        where TItem : IMediaItem
        where TType : IMediaType<TItem>;

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
    /// <param name="registration">The declaration and its implementation.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddIndexer(IndexerRegistration registration);

    /// <summary>
    /// Registers a transfer client. Requires <see cref="Capability.Download"/>.
    /// </summary>
    /// <param name="registration">The declaration and its implementation.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddDownloader(DownloaderRegistration registration);

    /// <summary>
    /// Registers a notification destination. Requires <see cref="Capability.Notification"/>.
    /// </summary>
    /// <param name="registration">The declaration and its implementation.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddNotifier(NotifierRegistration registration);

    /// <summary>
    /// Registers a cataloger. Requires <see cref="Capability.Metadata"/>.
    /// </summary>
    /// <param name="registration">The declaration and its implementation.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddCataloger(CatalogerRegistration registration);

    /// <summary>
    /// Registers a curator. Requires <see cref="Capability.Curation"/>.
    /// </summary>
    /// <param name="registration">The declaration and its implementation.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddCurator(CuratorRegistration registration);

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
    /// Registers telemetry enrichment. Ungated.
    /// </summary>
    /// <param name="enricher">The enricher.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddTelemetryEnricher(ITelemetryEnricher enricher);

    /// <summary>
    /// Registers a telemetry filter. Ungated.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddTelemetryEventFilter(ITelemetryEventFilter filter);

    /// <summary>
    /// Registers a telemetry destination. Requires <see cref="Capability.TelemetrySink"/>, because
    /// receiving the platform's telemetry stream is a privilege rather than a contribution.
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
    /// Registers an outbound call interceptor. Requires <see cref="Capability.Indexing"/>.
    /// </summary>
    /// <param name="interceptor">The interceptor.</param>
    /// <returns>This registry, for chaining.</returns>
    IPluginRegistry AddOutboundHttpInterceptor(IOutboundHttpInterceptor interceptor);

    /// <summary>
    /// Subscribes to a platform event. Ungated, and filtered: an extension sees the platform's events and
    /// its own, never another extension's.
    /// </summary>
    /// <typeparam name="TEvent">The event type subscribed to.</typeparam>
    /// <param name="handler">The handler.</param>
    /// <returns>This registry, for chaining.</returns>
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
