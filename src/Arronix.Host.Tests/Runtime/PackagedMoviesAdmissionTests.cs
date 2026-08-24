using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Media;
using Arronix.Host.Runtime;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>Runs the repository-built Movies package through discovery, admission, and publication.</summary>
/// <remarks>
/// The package is the real build output, discovered from disk and driven through the complete loader and
/// host lifecycle. Constructing a registration in process and handing it to <see cref="MediaTypeBinder"/>
/// would prove that binding works and nothing about the pipeline that has to reach it, so the fixture
/// asserts that the admitted kind arrived in the extension's own isolated load context.
/// </remarks>
[TestFixture]
internal sealed class PackagedMoviesAdmissionTests
{
    private static readonly MediaKindId Movies = MediaKindId.FromString("movies");
    private static readonly PluginId MoviesPlugin = PluginId.FromString("movies");

    private string _stateRoot = string.Empty;
    private string _packagedRoot = string.Empty;
    private ServiceProvider? _provider;

    [SetUp]
    public void SetUp()
    {
        _stateRoot = Directory.CreateTempSubdirectory("arronix-packaged-movies").FullName;
        _packagedRoot = Path.Combine(AppContext.BaseDirectory, "PackagedPlugins");

        File.Exists(Path.Combine(_packagedRoot, "movies", "plugin.json")).Should().BeTrue(
            "the build must stage the real package before the test can prove its loader behavior");

        _provider = BuildProvider(_packagedRoot);
    }

    private ServiceProvider BuildProvider(string pluginRoot)
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
        services.AddSingleton<ICacheProvider, RequiredServiceStub>();
        services.AddSingleton<ITelemetryEmitter, RequiredServiceStub>();
        services.AddSingleton<IEventPublisher, RequiredServiceStub>();
        services.AddSingleton<IHostRuntimeInfo, RequiredServiceStub>();
        services.AddSingleton<IOperatingSystemInfo, RequiredServiceStub>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// Copies the staged package and rewrites one declaration key, so a negative case is proved against the
    /// real build output rather than against a fixture written to agree with the checker.
    /// </summary>
    private string RestageWithManifest(Func<string, string> rewrite)
    {
        var root = Path.Combine(_stateRoot, "restaged");
        var folder = Path.Combine(root, "movies");
        Directory.CreateDirectory(folder);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(_packagedRoot, "movies")))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
        }

        var manifest = Path.Combine(folder, "plugin.json");
        File.WriteAllText(manifest, rewrite(File.ReadAllText(manifest)));

        return root;
    }

    [TearDown]
    public void TearDown()
    {
        _provider?.Dispose();
        _provider = null;

        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    [Test]
    public async Task TheRealPackagedMoviesExtensionReachesActiveAndPublishesItsKind()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper();

        await bootstrapper.StartAsync(CancellationToken.None);

        var state = bootstrapper.States.Should().ContainSingle().Which;
        var kinds = provider.GetRequiredService<MediaKindRegistry>();
        kinds.TryGet(Movies, out var registered).Should().BeTrue();
        var publishedKinds = kinds.All.Select(kind => kind.Kind.Value).ToArray();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        state.Id.Should().Be(MoviesPlugin);
        state.State.Should().Be(
            PluginState.Active,
            "the packaged extension must survive discovery, typed admission, late agreement, token ownership and publication");
        state.ErrorCode.Should().BeNull();
        state.Defects.Should().BeEmpty();
        registered!.Plugin.Should().Be(MoviesPlugin);
        publishedKinds.Should().Equal("movies");
    }

    /// <remarks>
    /// The item type is the one the extension's own assembly declares, resolved inside the extension's
    /// collectible load context. A direct binder call in the test process could not produce that type, so
    /// reintroducing one as the acceptance path fails here rather than passing quietly.
    /// </remarks>
    [Test]
    public async Task TheAdmittedKindCameThroughTheExtensionsOwnLoadContext()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper();

        await bootstrapper.StartAsync(CancellationToken.None);

        var registered = provider.GetRequiredService<MediaKindRegistry>().Require(Movies);
        var runtime = registered.MediaType.Should().NotBeNull().And.Subject.As<IMediaTypeRuntime>();
        var loaded = provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().ContainSingle().Which;

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        loaded.LoadContext.Should().NotBeNull();
        loaded.LoadContext!.Name.Should().Be("arronix-plugin:movies");
        loaded.Admitted.Kinds.Should().Equal(Movies);

        AssemblyLoadContext.GetLoadContext(runtime.ItemType.Assembly).Should().BeSameAs(
            loaded.LoadContext,
            "the admitted kind must be the one the isolated package supplied, not one bound in process");
        runtime.ItemType.Should().NotBeSameAs(
            typeof(global::Arronix.Media.Movies.Movie),
            "the item type resolved through the plugin load context is a different runtime type from the "
            + "test project's compile-time reference to the same source");
    }

    /// <remarks>
    /// Once each, for the kind that derived them, owned by the extension that supplied that kind. The set is
    /// compared with the admitted projection's own tokens rather than with a list written here, because a
    /// list written here would be the third hand-maintained copy of a derived fact.
    /// </remarks>
    [Test]
    public async Task TheAdmittedMoviesKindOwnsExactlyItsDerivedTokens()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper();

        await bootstrapper.StartAsync(CancellationToken.None);

        var registered = provider.GetRequiredService<MediaKindRegistry>().Require(Movies);
        var derived = registered.Shape.Declaration.Tokens.Select(token => token.Name).ToArray();
        var claims = provider.GetRequiredService<TokenRegistry>().Claims;

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        derived.Should().OnlyHaveUniqueItems().And.NotBeEmpty();
        claims.Should().OnlyContain(claim => claim.Plugin == MoviesPlugin && claim.MediaKind == Movies);
        claims.Select(claim => claim.Token.Name).Should().BeEquivalentTo(derived);
        claims.Should().HaveCount(
            derived.Length,
            "every derived token is claimed exactly once, so ownership is not a cross product");
    }

    /// <remarks>
    /// The conflict is planted before loading. Host still prepares the package's complete candidate set,
    /// but the loader cannot commit its token plan, so the shared publication transaction never exposes any
    /// Host candidate. The purpose of this case is non-publication after preparation; partial-commit rollback
    /// is proved separately with a conflict at the final Host publication step.
    /// </remarks>
    [Test]
    public async Task ATokenConflictAfterHostPreparationQuarantinesWithoutPublishingThePackage()
    {
        var provider = _provider!;
        var tokens = provider.GetRequiredService<TokenRegistry>();
        var squatter = PluginId.FromString("squatter");

        tokens.TryClaimAll(
                squatter,
                [new TokenClaimRequest(Movies, [new NamingToken("{Movie Title}", "already taken", string.Empty)])],
                out _)
            .Should().BeTrue();

        await Bootstrapper().StartAsync(CancellationToken.None);

        var state = Bootstrapper().States.Should().ContainSingle().Which;

        using var assertions = new AssertionScope();
        state.State.Should().Be(PluginState.Quarantined);
        state.ErrorCode.Should().Be(CoreErrorCode.PluginTokenConflict);
        state.Defects.Should().Contain(defect => defect.Contains("{Movie Title}", StringComparison.Ordinal));

        provider.GetRequiredService<MediaKindRegistry>().TryGet(Movies, out _).Should().BeFalse(
            "a prepared candidate is not visible before the complete publication transaction commits");
        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();

        tokens.Claims.Should().ContainSingle(
            "the quarantined package keeps no token, and the claim it collided with is untouched")
            .Which.Plugin.Should().Be(squatter);
    }

    /// <remarks>
    /// The real package, restaged with one invented media kind added to its declaration. A manifest is
    /// allowed to say nothing about derivable media facts; it is not allowed to say something the admitted
    /// projection contradicts.
    /// </remarks>
    [Test]
    public async Task ADeclaredMediaKindTheAdmittedProjectionDoesNotSupplyIsRefused()
    {
        var root = RestageWithManifest(manifest => manifest.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"mediaKinds\": [\"movies\", \"westerns\"],",
            StringComparison.Ordinal));

        using var provider = BuildProvider(root);
        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        await bootstrapper.StartAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        var state = bootstrapper.States.Should().ContainSingle().Which;
        state.State.Should().Be(PluginState.Quarantined);
        state.ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);
        state.Defects.Should().Contain(defect => defect.Contains("westerns", StringComparison.Ordinal));

        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty(
            "the declaration mismatch is found before the prepared kind is ever published");
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty();
    }

    [Test]
    public async Task StoppingReleasesTheActiveKindAndItsTokenClaims()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper();

        await bootstrapper.StartAsync(CancellationToken.None);

        provider.GetRequiredService<MediaKindRegistry>().TryGet(Movies, out _).Should().BeTrue();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().NotBeEmpty();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty(
            "teardown reverses activation, and token ownership is part of what activation took");
        bootstrapper.States.Should().ContainSingle().Which.State.Should().Be(PluginState.Stopped);
    }

    private PluginBootstrapper Bootstrapper()
        => _provider!.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

    /// <summary>
    /// Satisfies loader preconditions which this package never exercises. The package still receives the
    /// loader's capability-filtered wrappers, not these instances directly.
    /// </summary>
    private sealed class RequiredServiceStub :
        ICacheProvider,
        ITelemetryEmitter,
        IEventPublisher,
        IHostRuntimeInfo,
        IOperatingSystemInfo
    {
        public DateTimeOffset StartTime { get; } = DateTimeOffset.UnixEpoch;

        public bool IsUserInteractive => false;

        public bool IsAdministrator => false;

        public bool IsWindowsService => false;

        public bool IsContainerized => false;

        public string? ExecutingApplication => null;

        public string Name => "Test";

        public string Version => "1";

        public string FullName => "Test operating system";

        public bool IsDocker => false;

        public bool IsPodman => false;

        public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw Unused();

        public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
            => throw Unused();

        public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null) => throw Unused();

        public void Emit(TelemetryEvent telemetryEvent)
        {
        }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        private static InvalidOperationException Unused() =>
            new("The packaged Movies extension must not exercise undeclared platform privileges.");
    }
}
