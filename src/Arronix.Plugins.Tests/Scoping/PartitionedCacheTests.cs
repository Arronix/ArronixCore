using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Scoping;
using Arronix.Plugins.Tests.Support;


namespace Arronix.Plugins.Tests.Scoping;

/// <summary>
/// The namespacing decorators: cache partitions, throttling partitions, telemetry attribution, event
/// publication and folder layout.
/// </summary>
/// <remarks>
/// Each of these was documented as guaranteed behavior by a 0.2.0 contract and delivered by nothing. What
/// they have in common is that an extension cannot tell whether they are working, so the only place the
/// guarantee can be checked is here.
/// </remarks>
[TestFixture]
public sealed class PartitionedCacheTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.scoping");

    private static readonly PluginId Other = PluginId.FromString("test.other");

    [Test]
    public void ACachePartitionIsNamespacedByTheExtension()
    {
        var inner = new RecordingCacheProvider();
        var provider = new PartitionedCacheProvider(inner, Plugin, attemptId: 1);

        provider.GetCache<PartitionedCacheTests, string>("releases");

        inner.Partitions.Should().Equal($"plugin:{Plugin}:releases");
    }

    [Test]
    public void EveryCacheFlavorIsNamespacedTheSameWay()
    {
        var inner = new RecordingCacheProvider();
        var provider = new PartitionedCacheProvider(inner, Plugin, attemptId: 1);

        provider.GetCache<PartitionedCacheTests, string>("a");
        provider.GetRollingCache<PartitionedCacheTests, string>("b", TimeSpan.FromMinutes(1));
        provider.GetSelfRefreshingCache<PartitionedCacheTests, string>(
            "c",
            _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));

        inner.Partitions.Should().Equal(
            $"plugin:{Plugin}:a",
            $"plugin:{Plugin}:b",
            $"plugin:{Plugin}:c");
    }

    [Test]
    public void TwoExtensionsAskingForTheSamePartitionGetDifferentOnes()
    {
        var inner = new RecordingCacheProvider();

        new PartitionedCacheProvider(inner, Plugin, attemptId: 1).GetCache<PartitionedCacheTests, string>("shared");
        new PartitionedCacheProvider(inner, Other, attemptId: 2).GetCache<PartitionedCacheTests, string>("shared");

        inner.Partitions.Should().OnlyHaveUniqueItems(
            "one extension must be able neither to read nor to evict another's entries");
    }

    [Test]
    public void ABlankPartitionNameIsRefused()
    {
        var provider = new PartitionedCacheProvider(new RecordingCacheProvider(), Plugin, attemptId: 1);

        var resolve = () => provider.ResolvePartition("  ");

        resolve.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AThrottlingPartitionKeepsTheRemoteInTheKeyAndAddsTheExtension()
    {
        var inner = new RecordingRateLimiter();
        var limiter = new PartitionedRateLimiter(inner, Plugin);

        limiter.AttemptAcquire("indexer.example");

        inner.Partitions.Should().Equal($"{Plugin}|indexer.example");
    }

    [Test]
    public async Task EveryAcquisitionShapeComposesTheSameKey()
    {
        var inner = new RecordingRateLimiter();
        var limiter = new PartitionedRateLimiter(inner, Plugin);

        await limiter.AcquireAsync("remote.example");
        await limiter.AcquireAsync("remote.example", 3);
        limiter.AttemptAcquire("remote.example");

        inner.Partitions.Should().AllBe($"{Plugin}|remote.example");
    }

    [Test]
    public void TelemetryIsAttributedToTheExtension()
    {
        var inner = new RecordingTelemetryEmitter();
        var emitter = new PluginTelemetryEmitter(inner, Plugin);

        emitter.Emit(new TelemetryEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch, TelemetrySeverity.Info, "hello"));

        inner.Events.Should().ContainSingle();
        inner.Events[0].Tags[PluginTelemetryEmitter.PluginTag].Should().Be(Plugin.ToString());
    }

    [Test]
    public void AnExtensionCannotRelabelItsOwnTelemetryAsAnothers()
    {
        var inner = new RecordingTelemetryEmitter();
        var emitter = new PluginTelemetryEmitter(inner, Plugin);

        emitter.Emit(new TelemetryEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch, TelemetrySeverity.Info, "hello")
        {
            Tags = new Dictionary<string, string> { [PluginTelemetryEmitter.PluginTag] = Other.ToString() }
        });

        inner.Events[0].Tags[PluginTelemetryEmitter.PluginTag].Should().Be(
            Plugin.ToString(),
            "a support bundle that is misleading is worse than one that is merely incomplete");
    }

    [Test]
    public void TheExtensionsOwnTagsSurvive()
    {
        var inner = new RecordingTelemetryEmitter();
        var emitter = new PluginTelemetryEmitter(inner, Plugin);

        emitter.Emit(new TelemetryEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch, TelemetrySeverity.Info, "hello")
        {
            Tags = new Dictionary<string, string> { ["indexer"] = "acme" }
        });

        inner.Events[0].Tags["indexer"].Should().Be("acme");
    }

    [Test]
    public void APlatformEventIsAlwaysPublishableHoweverStrictTheBoundary()
    {
        var publisher = new FilteredEventPublisher(
            new RecordingEventPublisher(),
            Plugin,
            new PackageOwnership(typeof(PluginLoadContext).Assembly, []),
            invocation: null);

        publisher.MayPublish(typeof(IDomainEvent)).Should().BeTrue();
        publisher.MayPublish(typeof(ForeignEvent)).Should().BeFalse();
    }

    [Test]
    public async Task AnEventTypeFromNeitherThePlatformNorTheExtensionIsRefused()
    {
        var inner = new RecordingEventPublisher();
        var publisher = new FilteredEventPublisher(
            inner,
            Plugin,
            new PackageOwnership(typeof(PluginLoadContext).Assembly, []),
            invocation: null);

        var publish = async () => await publisher.PublishAsync(new ForeignEvent());

        await publish.Should().ThrowAsync<PluginIsolationException>();
        inner.Published.Should().BeEmpty();
    }

    [Test]
    public async Task AnEventInAContractThePackagePublishesIsItsOwnToPublishAsync()
    {
        var inner = new RecordingEventPublisher();
        var published = Emitted("Publishable.Owned.Contract");
        var publisher = new FilteredEventPublisher(
            inner,
            Plugin,
            new PackageOwnership(typeof(PluginLoadContext).Assembly, [published.Assembly]),
            invocation: null);

        await publisher.PublishAsync((IDomainEvent)Activator.CreateInstance(published.EventType)!);

        inner.Published.Should().ContainSingle("a package owns the contracts it publishes, on both sides of the bus");
    }

    [Test]
    public async Task AnEventInADependencysContractIsNotThePackagesToPublishAsync()
    {
        var inner = new RecordingEventPublisher();
        var dependency = Emitted("Publishable.Dependency.Contract");
        var publisher = new FilteredEventPublisher(
            inner,
            Plugin,
            new PackageOwnership(typeof(PluginLoadContext).Assembly, []),
            invocation: null);

        var publish = async () => await publisher.PublishAsync((IDomainEvent)Activator.CreateInstance(dependency.EventType)!);

        await publish.Should().ThrowAsync<PluginIsolationException>();
        inner.Published.Should().BeEmpty("a visible contract belongs to whoever published it");
    }

    [Test]
    public async Task APrivateAssemblySharingTheLoadContextIsNotThePackagesToPublishAsync()
    {
        var inner = new RecordingEventPublisher();
        var context = new AssemblyLoadContext("publication-ownership", isCollectible: true);
        var entry = context.LoadFromAssemblyPath(EmittedPath("Publishable.Entry.Assembly"));
        var shipped = context.LoadFromAssemblyPath(EmittedPath("Publishable.Private.Assembly"));
        var publisher = new FilteredEventPublisher(inner, Plugin, new PackageOwnership(entry, []), invocation: null);

        var publish = async () => await publisher.PublishAsync(
            (IDomainEvent)Activator.CreateInstance(shipped.GetType(EmittedEvent.TypeName)!)!);

        await publish.Should().ThrowAsync<PluginIsolationException>();
        inner.Published.Should().BeEmpty("sharing a load context is not owning");
    }

    private static (Assembly Assembly, Type EventType) Emitted(string assemblyName)
    {
        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(EmittedPath(assemblyName));
        return (assembly, assembly.GetType(EmittedEvent.TypeName)!);
    }

    private static string EmittedPath(string assemblyName)
        => EmittedEvent.Write(Path.Combine(Path.GetTempPath(), "arronix-publication", assemblyName), assemblyName);

    [Test]
    public async Task WithoutAnIsolationBoundaryOnlyThePlatformRuleApplies()
    {
        var inner = new RecordingEventPublisher();
        var publisher = new FilteredEventPublisher(inner, Plugin);

        await publisher.PublishAsync(new ForeignEvent());

        inner.Published.Should().ContainSingle(
            "an in-process module has no isolation boundary to enforce, and pretending otherwise would be theater");
    }

    [Test]
    public void TheExtensionsFoldersAreLaidOutBeneathTheConfiguredRootAndCreatedBeforeActivation()
    {
        var root = Directory.CreateTempSubdirectory("arronix-paths").FullName;

        try
        {
            var paths = PluginPaths.Beneath(Plugin, root);
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(paths.TempFolder).FullName, "stale.tmp"), "x");

            paths.Prepare();

            paths.PluginId.Should().Be(Plugin.ToString());
            Directory.Exists(paths.DataFolder).Should().BeTrue();
            Directory.Exists(paths.CacheFolder).Should().BeTrue();
            Directory.Exists(paths.TempFolder).Should().BeTrue();
            Directory.EnumerateFileSystemEntries(paths.TempFolder).Should().BeEmpty(
                "the contract says nothing written to the scratch folder survives a restart");
            paths.DataFolder.Should().StartWith(Path.Combine(root, Plugin.ToString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record ForeignEvent : IDomainEvent
    {
        public Guid EventId => Guid.Empty;

        public DateTimeOffset OccurredAt => DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }

    private sealed class RecordingRateLimiter : IRateLimiter
    {
        public List<string> Partitions { get; } = [];

        public ValueTask<IRateLimitLease> AcquireAsync(string partitionKey, CancellationToken cancellationToken = default)
        {
            Partitions.Add(partitionKey);
            return ValueTask.FromResult<IRateLimitLease>(new Lease());
        }

        public ValueTask<IRateLimitLease> AcquireAsync(
            string partitionKey,
            int permitCount,
            CancellationToken cancellationToken = default)
        {
            Partitions.Add(partitionKey);
            return ValueTask.FromResult<IRateLimitLease>(new Lease());
        }

        public IRateLimitLease AttemptAcquire(string partitionKey)
        {
            Partitions.Add(partitionKey);
            return new Lease();
        }

        private sealed class Lease : IRateLimitLease
        {
            public bool IsAcquired => true;

            public TimeSpan? RetryAfter => null;

            public void Dispose()
            {
            }
        }
    }
}
