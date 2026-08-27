using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Providers;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.Support;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>Loads the production TMDb package against the independently installed Movies package.</summary>
[TestFixture]
internal sealed class PackagedTmdbProviderTests
{
    private static readonly MediaKindId Movies = MediaKindId.FromString("movies");
    private static readonly PluginId Tmdb = PluginId.FromString("tmdb");

    private string _stateRoot = string.Empty;

    [SetUp]
    public void SetUp() =>
        _stateRoot = Directory.CreateTempSubdirectory("arronix-g05-tmdb").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    [Test]
    public async Task TheProductionPackageActivatesBothBindingsAndMaterializesTheInstalledMovieType()
    {
        var root = Install(includeMovies: true, includeTmdb: true);
        var gateway = new TmdbGateway();
        using var provider = BuildProvider(root, gateway);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var kind = provider.GetRequiredService<MediaKindRegistry>().Require(Movies);
        var itemType = kind.MediaType!.ItemType;
        var tmdbProviders = provider.GetRequiredService<ProviderRegistry>().All
            .Where(registration => registration.Plugin == Tmdb)
            .ToArray();
        var cataloger = tmdbProviders.Single(registration => registration.Family == ProviderFamily.Cataloger);
        var curator = tmdbProviders.Single(registration => registration.Family == ProviderFamily.Curator);
        var definitions = provider.GetRequiredService<ProviderDefinitionStore>();
        var definition = await definitions.AddAsync(
            new ProviderDefinition
            {
                Id = 0,
                Provider = cataloger.Id,
                Family = ProviderFamily.Cataloger,
                Name = "TMDb integration proof",
                Settings = new Dictionary<string, string> { ["readAccessToken"] = "integration-token" },
                MediaKinds = [Movies],
            });
        var curatorDefinition = await definitions.AddAsync(
            new ProviderDefinition
            {
                Id = 0,
                Provider = curator.Id,
                Family = ProviderFamily.Curator,
                Name = "TMDb popular integration proof",
                Settings = new Dictionary<string, string> { ["readAccessToken"] = "integration-token" },
                MediaKinds = [Movies],
            });
        var dispatcher = provider.GetRequiredService<CatalogDispatcher>();
        var identity = provider.GetRequiredService<CatalogIdentity>();
        var itemLevel = kind.MediaType!.Shape.Levels[0].Id;
        var beforeTakingIn = await FetchAsync(dispatcher, kind.MediaType, itemType);
        var namedWhileReading = identity.TryFind(Movies, itemLevel, ExternalId.Of("tmdb", "603"), out _);
        var first = TakeIn(dispatcher, kind.MediaType, itemType, beforeTakingIn.Value);
        var repeated = await FetchAsync(dispatcher, kind.MediaType, itemType);
        var second = TakeIn(dispatcher, kind.MediaType, itemType, repeated.Value);
        var curated = await FetchCuratedListAsync(
            curator.Provider,
            provider.GetRequiredService<ProviderTestService>().Invocation(curatorDefinition));
        var curatedReferences = (IReadOnlyList<CuratedReference>)curated.GetType()
            .GetProperty("Items")!.GetValue(curated)!;
        var resolvedCuration = await ResolveAsync(dispatcher, kind.MediaType, itemType, curated);
        var materializedCuration = resolvedCuration
            .Select(candidate => TakeIn(dispatcher, kind.MediaType, itemType, candidate.Value))
            .ToArray();
        var parsed = kind.Parser!.Parse("The Matrix 1999 {tmdb-603}");
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>().Active
            .Single(result => result.Id == Tmdb);
        var state = bootstrapper.States.Single(result => result.Id == Tmdb);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        state.State.Should().Be(PluginState.Active);
        state.Defects.Should().BeEmpty();
        tmdbProviders.Should().HaveCount(2);
        cataloger.CatalogScheme.Should().Be("tmdb");
        cataloger.MediaItemType.Should().BeSameAs(itemType);
        curator.MediaItemType.Should().BeSameAs(itemType);
        definition.Provider.Should().Be(cataloger.Id);

        beforeTakingIn.Item.GetType().Should().BeSameAs(itemType);
        ((IMediaItem)beforeTakingIn.Item).Title.Should().Be("The Matrix");
        beforeTakingIn.CatalogId.Should().Be(ExternalId.Of("tmdb", "603"));
        beforeTakingIn.Held.Should().BeNull("a catalog fetch of an unheld record names nothing");
        namedWhileReading.Should().BeFalse("and a real provider fetch names nothing locally");
        first.Reference.Id.Value.Should().Be(1, "Host assigned the first identity, and the fetch spent none");
        repeated.Held.Should().Be(first.Reference, "a second fetch reports the reference it is now held under");
        second.Reference.Should().Be(first.Reference, "repeating one take-in is one local item");
        identity.TryFind(Movies, itemLevel, ExternalId.Of("tmdb", "603"), out var bound).Should().BeTrue(
            "the control: the same lookup does find the record once it is materialized");
        bound.Should().Be(first.Reference, "one record, one durable identity");
        itemType.GetProperty("Key").Should().BeNull("durable identity never crosses the provider contract");

        curatedReferences.Should().ContainSingle().Which.Should().Be(
            new CuratedReference(ExternalId.Of("tmdb", "603")));
        resolvedCuration.Should().ContainSingle();
        resolvedCuration[0].Held.Should().Be(first.Reference,
            "the curator's reference is resolved through the owning cataloger, not treated as an item");
        materializedCuration.Should().ContainSingle();
        materializedCuration[0].Reference.Should().Be(first.Reference,
            "so materializing it converges on the record already held rather than naming a second one");
        materializedCuration[0].CuratedEntryId.Should().BeNull();

        parsed.Should().NotBeNull();
        parsed!.AdditionalMetadata.Should().Contain("parse.externalId.tmdb", "603",
            "the Movie parser consumes marker readings supplied by the installed cataloger");

        runtime.LoadContext.Should().NotBeNull();
        runtime.LoadContext!.Name.Should().Be("arronix-plugin:tmdb");
        runtime.LoadContext.Assemblies.Select(assembly => assembly.GetName().Name).Should()
            .Contain("Arronix.Provider.Tmdb")
            .And.NotContain("Arronix.Media.Movies");
        AssemblyLoadContext.GetLoadContext(itemType.Assembly)!.Name.Should().Be(SharedContractStore.ContextName);

        gateway.Requests.Should().HaveCount(4);
        gateway.Requests.Should().OnlyContain(request =>
            request.Headers.GetValues("Authorization").Single() == "Bearer integration-token"
            && request.Headers.UserAgent!.Contains("(+tmdb)", StringComparison.Ordinal)
            && request.RateLimitKey == "tmdb|api.themoviedb.org");
        gateway.Requests.Count(request => request.Url.AbsolutePath.EndsWith("/movie/603", StringComparison.Ordinal))
            .Should().Be(3);
        gateway.Requests.Count(request => request.Url.AbsolutePath.EndsWith("/movie/popular", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Test]
    public async Task MissingMoviesQuarantinesOnlyTheTmdbBindingPackage()
    {
        var root = Install(includeMovies: false, includeTmdb: true);
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var states = bootstrapper.States.ToDictionary(state => state.Id!.Value);
        var providers = provider.GetRequiredService<ProviderRegistry>().All.ToArray();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        states["tmdb"].State.Should().Be(PluginState.Quarantined);
        states["tmdb"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnsatisfied);
        states["tmdb"].Defects.Should().Contain(defect =>
            defect.Contains("dependencies[0].package", StringComparison.Ordinal)
            && defect.Contains("movies", StringComparison.Ordinal));
        states["arronix.format.video"].State.Should().Be(PluginState.Active,
            "the unrelated installed package remains usable");
        providers.Should().NotContain(registration => registration.Plugin == Tmdb);
    }

    [Test]
    public async Task AnIncompatibleMoviesVersionQuarantinesTmdbWithoutQuarantiningMovies()
    {
        var root = Install(includeMovies: true, includeTmdb: true);
        RewriteVersion(root, "movies", "9.9.9");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var states = bootstrapper.States.ToDictionary(state => state.Id!.Value);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        states["tmdb"].State.Should().Be(PluginState.Quarantined);
        states["tmdb"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnsatisfied);
        states["tmdb"].Defects.Should().Contain(defect =>
            defect.Contains("dependencies[0].range", StringComparison.Ordinal)
            && defect.Contains("9.9.9", StringComparison.Ordinal));
        states["movies"].State.Should().Be(PluginState.Active,
            "the media package is sound; only TMDb's declared compatibility range rejects it");
    }

    private string Install(bool includeMovies, bool includeTmdb)
    {
        var root = Path.Combine(_stateRoot, "plugins");
        Directory.CreateDirectory(root);

        CopyPackage(
            Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "arronix.format.video"),
            Path.Combine(root, "arronix.format.video"));

        if (includeMovies)
        {
            CopyPackage(
                Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "movies"),
                Path.Combine(root, "movies"));
        }

        if (includeTmdb)
        {
            CopyPackage(
                Path.Combine(AppContext.BaseDirectory, "G05PackagedPlugins", "tmdb"),
                Path.Combine(root, "tmdb"));
        }

        return root;
    }

    private static void CopyPackage(string source, string destination)
    {
        Directory.Exists(source).Should().BeTrue(
            $"the build must stage '{source}' before this test installs it");
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static void RewriteVersion(string root, string package, string version)
    {
        var path = Path.Combine(root, package, "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        manifest["version"] = version;
        File.WriteAllText(path, manifest.ToJsonString());
    }

    private ServiceProvider BuildProvider(string pluginRoot, IHttpGateway? gateway = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = pluginRoot,
                ["Arronix:Plugins:RootFolder"] = pluginRoot,
                ["Arronix:Plugins:StateFolder"] = Path.Combine(_stateRoot, "state"),
                ["Arronix:Library:RootFolders:0"] = Path.Combine(_stateRoot, "library"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);
        services.AddSingleton<ICacheProvider, PlatformServiceStub>();
        services.AddSingleton<ITelemetryEmitter, PlatformServiceStub>();
        services.AddSingleton<IEventPublisher, PlatformServiceStub>();
        services.AddSingleton<IHostRuntimeInfo, PlatformServiceStub>();
        services.AddSingleton<IOperatingSystemInfo, PlatformServiceStub>();

        if (gateway is not null)
        {
            services.AddSingleton(gateway);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static PluginBootstrapper Bootstrapper(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

    private static async Task<CandidateObservation> FetchAsync(
        CatalogDispatcher dispatcher,
        IMediaTypeRuntime kind,
        Type itemType)
    {
        var open = typeof(CatalogDispatcher).GetMethods()
            .Single(method => method.Name == nameof(CatalogDispatcher.FetchAsync)
                && method.IsGenericMethodDefinition);
        var task = (Task)open.MakeGenericMethod(itemType).Invoke(
            dispatcher,
            [kind, ExternalId.Of("tmdb", "603"), CancellationToken.None])!;

        await task.ConfigureAwait(false);

        var fetch = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var outcome = fetch.GetType().GetProperty("Outcome")!.GetValue(fetch)!;

        outcome.ToString().Should().Be("Found", "the fake TMDb gateway holds the record");
        return ObserveCandidate(fetch.GetType().GetProperty("Candidate")!.GetValue(fetch)!);
    }

    private static MaterializedObservation TakeIn(
        CatalogDispatcher dispatcher,
        IMediaTypeRuntime kind,
        Type itemType,
        object candidate)
    {
        // Host-internal: assignment is not an operation the platform offers while the library row it
        // belongs in a transaction with does not exist.
        var open = typeof(CatalogDispatcher)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == nameof(CatalogDispatcher.Materialize));

        return Observe(open.MakeGenericMethod(itemType).Invoke(dispatcher, [kind, candidate])!);
    }

    private static async Task<object> FetchCuratedListAsync(
        IProvider curator,
        ProviderInvocation invocation)
    {
        var task = (Task)curator.GetType().GetMethod("FetchAsync")!.Invoke(
            curator,
            [invocation, CancellationToken.None])!;

        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<IReadOnlyList<CandidateObservation>> ResolveAsync(
        CatalogDispatcher dispatcher,
        IMediaTypeRuntime kind,
        Type itemType,
        object curatedList)
    {
        var open = typeof(CatalogDispatcher).GetMethods()
            .Single(method => method.Name == nameof(CatalogDispatcher.ResolveAsync)
                && method.IsGenericMethodDefinition);
        var task = (Task)open.MakeGenericMethod(itemType).Invoke(
            dispatcher,
            [kind, curatedList, CancellationToken.None])!;

        await task.ConfigureAwait(false);

        var answer = task.GetType().GetProperty("Result")!.GetValue(task)!;

        return ((IEnumerable)answer.GetType().GetProperty("Candidates")!.GetValue(answer)!)
            .Cast<object>()
            .Select(ObserveCandidate)
            .ToArray();
    }

    /// <remarks>
    /// Reflection is deliberate here: the test has no compile-time reference to the package's Movie type,
    /// so the generic calls close over the exact type admitted from the installed Movies package.
    /// </remarks>
    private static MaterializedObservation Observe(object materialized)
    {
        var type = materialized.GetType();

        return new MaterializedObservation(
            (MediaItemRef)type.GetProperty("Reference")!.GetValue(materialized)!,
            (ExternalId)type.GetProperty("CatalogId")!.GetValue(materialized)!,
            type.GetProperty("Item")!.GetValue(materialized)!,
            (CuratedEntryId?)type.GetProperty("CuratedEntryId")!.GetValue(materialized));
    }

    private static CandidateObservation ObserveCandidate(object candidate)
    {
        var type = candidate.GetType();

        return new CandidateObservation(
            candidate,
            (ExternalId)type.GetProperty("CatalogId")!.GetValue(candidate)!,
            type.GetProperty("Item")!.GetValue(candidate)!,
            (MediaItemRef?)type.GetProperty("Held")!.GetValue(candidate),
            (CuratedEntryId?)type.GetProperty("CuratedEntryId")!.GetValue(candidate));
    }

    private sealed record MaterializedObservation(
        MediaItemRef Reference,
        ExternalId CatalogId,
        object Item,
        CuratedEntryId? CuratedEntryId);

    /// <summary>One candidate, read reflectively, plus the candidate itself to hand back.</summary>
    private sealed record CandidateObservation(
        object Value,
        ExternalId CatalogId,
        object Item,
        MediaItemRef? Held,
        CuratedEntryId? CuratedEntryId);

    private sealed class TmdbGateway : IHttpGateway
    {
        public List<OutboundHttpRequest> Requests { get; } = [];

        public Task<OutboundHttpResponse> ExecuteAsync(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            if (request.Url.AbsolutePath.EndsWith("/movie/popular", StringComparison.Ordinal))
            {
                const string Popular = """
                    { "page": 1, "results": [{ "id": 603, "title": "The Matrix" }], "total_pages": 1, "total_results": 1 }
                    """;

                return Response(request, Popular);
            }

            if (!request.Url.AbsolutePath.EndsWith("/movie/603", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected TMDb request: {request.Url.AbsolutePath}");
            }

            const string Json = """
                {
                  "id": 603,
                  "title": "The Matrix",
                  "release_date": "1999-03-30",
                  "runtime": 136,
                  "external_ids": { "imdb_id": "tt0133093" },
                  "release_dates": {
                    "results": [{
                      "iso_3166_1": "US",
                      "release_dates": [{ "release_date": "1999-03-31T00:00:00Z", "type": 3 }]
                    }]
                  }
                }
                """;

            return Response(request, Json);
        }

        private static Task<OutboundHttpResponse> Response(OutboundHttpRequest request, string json) =>
            Task.FromResult(new OutboundHttpResponse(
                request,
                new HttpHeaderCollection(),
                HttpStatusCode.OK,
                Encoding.UTF8.GetBytes(json)));

        public Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The TMDb provider consumes the buffered response itself.");

        public Task<OutboundHttpResponse> DownloadAsync(
            OutboundHttpRequest request,
            Stream destination,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The TMDb provider does not download through this proof.");
    }
}
