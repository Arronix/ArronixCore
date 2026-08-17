#pragma warning disable ARX0001 // Caching contracts are experimental; the recorder only has to name the type.
#pragma warning disable ARX0002 // Diagnostics contracts are experimental; likewise.
#pragma warning disable ARX0004 // Event contracts are experimental; likewise.
#pragma warning disable ARX0005 // File-system contracts are experimental; likewise.
#pragma warning disable ARX0006 // Health contracts are experimental; likewise.
#pragma warning disable ARX0007 // Hosting contracts are experimental; likewise.
#pragma warning disable ARX0008 // HTTP contracts are experimental; likewise.
#pragma warning disable ARX0009 // Naming contracts are experimental; likewise.
#pragma warning disable ARX0010 // Serialization contracts are experimental; likewise.
#pragma warning disable ARX0011 // Telemetry contracts are experimental; the recorder captures them.
#pragma warning disable ARX0012 // Throttling contracts are experimental; likewise.
#pragma warning disable ARX0013 // Shape contracts are experimental; these tests cover an implementation.
#pragma warning disable ARX0014 // Extension contracts are experimental; these tests cover an implementation.
#pragma warning disable ARX0015 // Provider contracts are experimental; these tests cover an implementation.
#pragma warning disable ARX0016 // Intent contracts are experimental; these tests cover an implementation.
#pragma warning disable ARX0020 // The typed media surface is experimental; the recorder only has to name the type.

using System;
using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;
using Arronix.Plugin.Tv.Providers;

namespace Arronix.Plugin.Tv.Tests.Shape;

/// <summary>
/// Asserts what the module registers, and that it registers nothing it did not declare.
/// </summary>
/// <remarks>
/// The forward half of the capability check — "every declared capability has a matching registration" — is
/// the host's, and it needs an extension that actually holds up its end. These tests are the extension-side
/// mirror: every seam handed to the registry maps to a capability the manifest declares, and every
/// capability the manifest declares is backed by at least one registration.
/// </remarks>
[TestFixture]
public sealed class ModuleRegistrationTests
{
    private RecordingRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new RecordingRegistry();
        new TvPluginModule().Configure(new RecordingContext(_registry));
    }

    [Test]
    public void TheModuleIdentifiesItselfAsTheManifestDoes()
        => Assert.That(new TvPluginModule().Id.Value, Is.EqualTo(TvIds.PluginIdValue));

    [Test]
    public void EverySeamThisMediaKindNeedsIsRegisteredExactlyOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.Shapes, Has.Count.EqualTo(1));
            Assert.That(_registry.ItemSources, Has.Count.EqualTo(1));
            Assert.That(_registry.Matchers, Has.Count.EqualTo(1));
            Assert.That(_registry.QueryPlanners, Has.Count.EqualTo(1));
            Assert.That(_registry.Parsers, Has.Count.EqualTo(1));
            Assert.That(_registry.QualityModels, Has.Count.EqualTo(1));
            Assert.That(_registry.RenamePolicies, Has.Count.EqualTo(1));
            Assert.That(_registry.LibraryLayouts, Has.Count.EqualTo(1));
            Assert.That(_registry.IdResolvers, Has.Count.EqualTo(1));
            Assert.That(_registry.IntentSurfaces, Has.Count.EqualTo(1));
            Assert.That(_registry.Indexers, Has.Count.EqualTo(1));
            Assert.That(_registry.Catalogers, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void NothingOutsideTheDeclaredCapabilitiesIsRegistered()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.ImportPipelines, Is.Empty, "the import capability is not declared");
            Assert.That(_registry.Downloaders, Is.Empty, "the download capability is not declared");
            Assert.That(_registry.Notifiers, Is.Empty, "the notification capability is not declared");
            Assert.That(_registry.Curators, Is.Empty, "the curation capability is not declared");
            Assert.That(_registry.TelemetrySinks, Is.Empty, "the telemetry-sink capability is not declared");
            Assert.That(_registry.ScheduledJobs, Is.Empty);
        });
    }

    [Test]
    public void EverySeamAgreesOnTheMediaKind()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.Shapes.Single().Shape.Kind, Is.EqualTo(TvIds.MediaKind));
            Assert.That(_registry.ItemSources.Single().MediaKind, Is.EqualTo(TvIds.MediaKind));
            Assert.That(_registry.Matchers.Single().MediaKind, Is.EqualTo(TvIds.MediaKind));
            Assert.That(_registry.QueryPlanners.Single().MediaKind, Is.EqualTo(TvIds.MediaKind));
            Assert.That(_registry.Parsers.Single().MediaKind, Is.EqualTo(TvIds.MediaKind));
            Assert.That(_registry.QualityModels.Single().MediaKind, Is.EqualTo(TvIds.MediaKind));
            Assert.That(_registry.IntentSurfaces.Single().MediaKind, Is.EqualTo(TvIds.MediaKind));
        });
    }

    [Test]
    public void ProviderIdentifiersAreQualifiedByTheExtensionIdentifier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                _registry.Indexers.Single().Provider.Id.Plugin.Value,
                Is.EqualTo(TvIds.PluginIdValue));
            Assert.That(
                _registry.Catalogers.Single().Provider.Id.Plugin.Value,
                Is.EqualTo(TvIds.PluginIdValue));
            Assert.That(
                _registry.Indexers.Single().Provider.Id.Value,
                Is.EqualTo($"{TvIds.PluginIdValue}:{TvIndexer.LocalId}"));
        });
    }

    [Test]
    public void EveryProviderSecretIsDeclaredAsOne()
    {
        var descriptors = _registry.Indexers
            .Select(registration => registration.Descriptor)
            .Concat(_registry.Catalogers.Select(registration => registration.Descriptor))
            .ToList();

        var apiKey = descriptors
            .SelectMany(descriptor => descriptor.Settings)
            .Single(field => field.FieldId == TvIndexer.ApiKeySetting);

        Assert.Multiple(() =>
        {
            Assert.That(apiKey.Sensitivity, Is.EqualTo(SettingSensitivity.Secret));

            foreach (var descriptor in descriptors)
            {
                var fieldIds = descriptor.Settings.Select(field => field.FieldId).ToList();

                Assert.That(
                    fieldIds.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(fieldIds.Count),
                    $"'{descriptor.LocalId}' declares a settings field twice");
            }
        });
    }

    [Test]
    public void EveryIntentSurfaceReferenceResolvesAgainstTheShape()
    {
        var shape = _registry.Shapes.Single().Shape;
        var intent = _registry.IntentSurfaces.Single();
        var levelIds = shape.Levels.Select(level => level.Id).ToList();
        var fieldIds = shape.Levels
            .SelectMany(level => level.Fields.Select(field => field.FieldId))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var axis in intent.BrowseAxes)
            {
                if (axis.LevelId is { } levelId)
                {
                    Assert.That(levelIds, Does.Contain(levelId), $"browse axis '{axis.AxisId}'");
                }

                if (axis.FieldId is { } fieldId)
                {
                    Assert.That(fieldIds, Does.Contain(fieldId), $"browse axis '{axis.AxisId}'");
                }

                if (axis.SequenceAxisId is { } sequenceAxisId)
                {
                    Assert.That(
                        shape.Levels.SelectMany(level => level.SequenceAxes).Select(sequence => sequence.AxisId),
                        Does.Contain(sequenceAxisId));
                }
            }

            foreach (var sort in intent.Sorts)
            {
                Assert.That(fieldIds, Does.Contain(sort.FieldId), $"sort '{sort.Name}'");
            }

            foreach (var filter in intent.Filters)
            {
                Assert.That(fieldIds, Does.Contain(filter.FieldId), $"filter '{filter.Name}'");
            }

            foreach (var state in intent.States)
            {
                Assert.That(fieldIds, Does.Contain(state.SourceFieldId), $"state '{state.StateId}'");
            }

            foreach (var action in intent.Actions.Where(action => action.TargetLevelId is not null))
            {
                Assert.That(levelIds, Does.Contain(action.TargetLevelId!.Value));
            }

            foreach (var surface in intent.ExternalSurfaces)
            {
                Assert.That(levelIds, Does.Contain(surface.LevelId));
            }
        });
    }

    [Test]
    public void EveryDeclaredStateIsReachableFromAFieldChoice()
    {
        var shape = _registry.Shapes.Single().Shape;
        var intent = _registry.IntentSurfaces.Single();

        foreach (var state in intent.States)
        {
            var field = shape.Levels
                .SelectMany(level => level.Fields)
                .Single(candidate => candidate.FieldId == state.SourceFieldId);

            Assert.That(
                field.Choices.Select(choice => choice.Value),
                Does.Contain(state.StateId),
                $"state '{state.StateId}' names a value its source field cannot hold");
        }
    }

    [Test]
    public void EveryWorkbenchColumnHasADistinctIdentifier()
    {
        foreach (var workbench in _registry.IntentSurfaces.Single().Workbenches)
        {
            var ids = workbench.Columns.Select(column => column.Field.FieldId).ToList();

            Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Count));
        }
    }

    /// <summary>Captures everything handed to the registration surface.</summary>
    private sealed class RecordingRegistry : IPluginRegistry
    {
        public List<IMediaShapeProvider> Shapes { get; } = [];

        public List<IMediaItemSource> ItemSources { get; } = [];

        public List<IReleaseMatcher> Matchers { get; } = [];

        public List<IReleaseQueryPlanner> QueryPlanners { get; } = [];

        public List<IReleaseParser> Parsers { get; } = [];

        public List<IQualityModel> QualityModels { get; } = [];

        public List<IImportPipeline> ImportPipelines { get; } = [];

        public List<IRenamePolicy> RenamePolicies { get; } = [];

        public List<ILibraryLayout> LibraryLayouts { get; } = [];

        public List<IMediaIdResolver> IdResolvers { get; } = [];

        public List<IndexerRegistration> Indexers { get; } = [];

        public List<DownloaderRegistration> Downloaders { get; } = [];

        public List<NotifierRegistration> Notifiers { get; } = [];

        public List<CatalogerRegistration> Catalogers { get; } = [];

        public List<CuratorRegistration> Curators { get; } = [];

        public List<IScheduledJob> ScheduledJobs { get; } = [];

        public List<IHealthContributor> HealthContributors { get; } = [];

        public List<ITelemetrySink> TelemetrySinks { get; } = [];

        public List<PluginIntentSurface> IntentSurfaces { get; } = [];

        public List<IMediaTypeRegistration> MediaKinds { get; } = [];

        public IPluginRegistry AddMediaType<TItem, TType>()
            where TItem : IMediaItem
            where TType : IMediaType<TItem>
        {
            MediaKinds.Add(MediaTypeRegistration.For<TItem, TType>());
            return this;
        }

        public IPluginRegistry AddMediaShape(IMediaShapeProvider provider)
        {
            Shapes.Add(provider);
            return this;
        }

        public IPluginRegistry AddMediaItemSource(IMediaItemSource source)
        {
            ItemSources.Add(source);
            return this;
        }

        public IPluginRegistry AddReleaseMatcher(IReleaseMatcher matcher)
        {
            Matchers.Add(matcher);
            return this;
        }

        public IPluginRegistry AddReleaseQueryPlanner(IReleaseQueryPlanner planner)
        {
            QueryPlanners.Add(planner);
            return this;
        }

        public IPluginRegistry AddReleaseParser(IReleaseParser parser)
        {
            Parsers.Add(parser);
            return this;
        }

        public IPluginRegistry AddQualityModel(IQualityModel model)
        {
            QualityModels.Add(model);
            return this;
        }

        public IPluginRegistry AddImportPipeline(IImportPipeline pipeline)
        {
            ImportPipelines.Add(pipeline);
            return this;
        }

        public IPluginRegistry AddRenamePolicy(IRenamePolicy policy)
        {
            RenamePolicies.Add(policy);
            return this;
        }

        public IPluginRegistry AddLibraryLayout(ILibraryLayout layout)
        {
            LibraryLayouts.Add(layout);
            return this;
        }

        public IPluginRegistry AddMediaIdResolver(IMediaIdResolver resolver)
        {
            IdResolvers.Add(resolver);
            return this;
        }

        public IPluginRegistry AddIndexer(IndexerRegistration registration)
        {
            Indexers.Add(registration);
            return this;
        }

        public IPluginRegistry AddDownloader(DownloaderRegistration registration)
        {
            Downloaders.Add(registration);
            return this;
        }

        public IPluginRegistry AddNotifier(NotifierRegistration registration)
        {
            Notifiers.Add(registration);
            return this;
        }

        public IPluginRegistry AddCataloger(CatalogerRegistration registration)
        {
            Catalogers.Add(registration);
            return this;
        }

        public IPluginRegistry AddCurator(CuratorRegistration registration)
        {
            Curators.Add(registration);
            return this;
        }

        public IPluginRegistry AddScheduledJob(IScheduledJob job, string schedule)
        {
            ScheduledJobs.Add(job);
            return this;
        }

        public IPluginRegistry AddHealthContributor(IHealthContributor contributor)
        {
            HealthContributors.Add(contributor);
            return this;
        }

        public IPluginRegistry AddTelemetryEnricher(ITelemetryEnricher enricher) => this;

        public IPluginRegistry AddTelemetryEventFilter(ITelemetryEventFilter filter) => this;

        public IPluginRegistry AddTelemetrySink(ITelemetrySink sink)
        {
            TelemetrySinks.Add(sink);
            return this;
        }

        public IPluginRegistry AddRedactionRules(IRedactionRuleProvider provider) => this;

        public IPluginRegistry AddDiacriticFolding(IDiacriticFoldingProvider provider) => this;

        public IPluginRegistry AddOutboundHttpInterceptor(IOutboundHttpInterceptor interceptor) => this;

        public IPluginRegistry AddEventHandler<TEvent>(IEventHandler<TEvent> handler)
            where TEvent : IDomainEvent
            => this;

        public IPluginRegistry AddIntentSurface(PluginIntentSurface surface)
        {
            IntentSurfaces.Add(surface);
            return this;
        }
    }

    /// <summary>Supplies only what a media extension's <c>Configure</c> is allowed to touch.</summary>
    private sealed class RecordingContext(IPluginRegistry registry) : IPluginContext
    {
        public PluginId PluginId { get; } = PluginId.FromString(TvIds.PluginIdValue);

        public string PluginVersion => "0.1.0";

        public string HostContractVersion => "0.3.0";

        public CapabilitySet Capabilities { get; } = CapabilitySet.Of(
            Capability.MediaKind,
            Capability.Parsing,
            Capability.Matching,
            Capability.Indexing,
            Capability.Quality,
            Capability.Renaming,
            Capability.Metadata);

        public IPluginRegistry Registry { get; } = registry;

        public ITelemetryEmitter Telemetry { get; } = new NullEmitter();

        public TimeProvider Clock => TimeProvider.System;

        public IPluginPaths Paths => throw new NotSupportedException();

        public ICacheProvider Cache => throw new NotSupportedException();

        public IJsonSerializer Json => throw new NotSupportedException();

        public IEventPublisher Events => throw new NotSupportedException();

        public IHostRuntimeInfo Runtime => throw new NotSupportedException();

        public IOperatingSystemInfo OperatingSystem => throw new NotSupportedException();

        public bool TryGetHttp(out IHttpGateway? gateway)
        {
            gateway = null;
            return false;
        }

        public bool TryGetRateLimiter(out IRateLimiter? limiter)
        {
            limiter = null;
            return false;
        }

        public bool TryGetCertificatePolicy(out ICertificateValidationPolicy? policy)
        {
            policy = null;
            return false;
        }

        public bool TryGetFileSystem(out IFileSystem? fileSystem)
        {
            fileSystem = null;
            return false;
        }

        public bool TryGetFileTransfer(out IFileTransferService? transfer)
        {
            transfer = null;
            return false;
        }

        public IHttpGateway RequireHttp() => throw new NotSupportedException();

        public IFileSystem RequireFileSystem() => throw new NotSupportedException();

        public IFileTransferService RequireFileTransfer() => throw new NotSupportedException();
    }

    private sealed class NullEmitter : ITelemetryEmitter
    {
        public void Emit(TelemetryEvent telemetryEvent)
        {
        }
    }
}
