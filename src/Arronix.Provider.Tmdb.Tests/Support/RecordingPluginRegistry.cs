using System;
using System.Collections.Generic;
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

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>
/// A registry that records only the two calls the TMDb module is expected to make, and refuses every
/// other contribution: a module reaching for one is doing something this pressure test did not ask for.
/// </summary>
internal sealed class RecordingPluginRegistry : IPluginRegistry
{
    public List<(Type ItemType, Type ImplementationType, ProviderDescriptor Descriptor)> Catalogers { get; } = [];

    public List<(Type ItemType, Type ImplementationType, ProviderDescriptor Descriptor)> Curators { get; } = [];

    public IPluginRegistry AddCataloger<TItem, TCataloger>(ProviderDescriptor descriptor)
        where TItem : class, IMediaItem
        where TCataloger : class, ICataloger<TItem>
    {
        Catalogers.Add((typeof(TItem), typeof(TCataloger), descriptor));
        return this;
    }

    public IPluginRegistry AddCurator<TItem, TCurator>(ProviderDescriptor descriptor)
        where TItem : class, IMediaItem
        where TCurator : class, ICurator<TItem>
    {
        Curators.Add((typeof(TItem), typeof(TCurator), descriptor));
        return this;
    }

    public IPluginRegistry AddMediaType<TType>() where TType : class, IMediaTypeDefinition, new() => throw Unused();

    public IPluginRegistry AddMediaShape(IMediaShapeProvider provider) => throw Unused();

    public IPluginRegistry AddMediaItemSource(IMediaItemSource source) => throw Unused();

    public IPluginRegistry AddReleaseMatcher(IReleaseMatcher matcher) => throw Unused();

    public IPluginRegistry AddReleaseQueryPlanner(IReleaseQueryPlanner planner) => throw Unused();

    public IPluginRegistry AddReleaseParser(IReleaseParser parser) => throw Unused();

    public IPluginRegistry AddQualityModel(IQualityModel model) => throw Unused();

    public IPluginRegistry AddImportPipeline(IImportPipeline pipeline) => throw Unused();

    public IPluginRegistry AddRenamePolicy(IRenamePolicy policy) => throw Unused();

    public IPluginRegistry AddLibraryLayout(ILibraryLayout layout) => throw Unused();

    public IPluginRegistry AddMediaIdResolver(IMediaIdResolver resolver) => throw Unused();

    public IPluginRegistry AddIndexer<TIndexer>(ProviderDescriptor descriptor) where TIndexer : class, IIndexer =>
        throw Unused();

    public IPluginRegistry AddDownloader<TDownloader>(ProviderDescriptor descriptor) where TDownloader : class, IDownloader =>
        throw Unused();

    public IPluginRegistry AddNotifier<TNotifier>(ProviderDescriptor descriptor) where TNotifier : class, INotifier =>
        throw Unused();

    public IPluginRegistry AddScheduledJob(IScheduledJob job, string schedule) => throw Unused();

    public IPluginRegistry AddHealthContributor(IHealthContributor contributor) => throw Unused();

    public IPluginRegistry AddTelemetryEnricher(ITelemetryEnricher enricher) => throw Unused();

    public IPluginRegistry AddTelemetryEventFilter(ITelemetryEventFilter filter) => throw Unused();

    public IPluginRegistry AddTelemetrySink(ITelemetrySink sink) => throw Unused();

    public IPluginRegistry AddRedactionRules(IRedactionRuleProvider provider) => throw Unused();

    public IPluginRegistry AddDiacriticFolding(IDiacriticFoldingProvider provider) => throw Unused();

    public IPluginRegistry AddLanguage<TLanguage>() where TLanguage : class, ILanguageDefinition => throw Unused();

    public IPluginRegistry AddOutboundHttpInterceptor(IOutboundHttpInterceptor interceptor) => throw Unused();

    public IPluginRegistry AddEventHandler<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent =>
        throw Unused();

    public IPluginRegistry AddIntentSurface(PluginIntentSurface surface) => throw Unused();

    private static NotSupportedException Unused() =>
        new("The TMDb module contributes only a cataloger and a curator.");
}
