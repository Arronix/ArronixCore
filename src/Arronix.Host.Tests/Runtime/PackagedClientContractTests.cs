using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using FluentAssertions.Execution;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.Support;
using Arronix.Plugins.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// What a browser may load from a running installation, proved against real staged packages.
/// </summary>
/// <remarks>
/// <para>
/// Every case installs the real staged Movies and video package folders and drives the complete Host
/// lifecycle. The catalog is then asked the same questions the HTTP surface asks it, so what is asserted
/// here is the projection a browser actually receives rather than a hand-built double of it.
/// </para>
/// <para>
/// The claims are about exactness, because that is the only thing a client can check. A content hash names
/// bytes; an assembly identity names what the runtime will bind them as; a module version identifier names
/// the build the compiler produced. Two of the three agreeing is not agreement.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class PackagedClientContractTests
{
    private const string MoviesPackage = "movies";
    private const string VideoPackage = "arronix.format.video";
    private const string MoviesContract = "Arronix.Media.Movies.dll";
    private const string VideoContract = "Arronix.Format.Video.dll";
    private const string MoviesEntryAssembly = "Arronix.Plugin.Movies.dll";

    private string _stateRoot = string.Empty;

    [SetUp]
    public void SetUp() =>
        _stateRoot = Directory.CreateTempSubdirectory("arronix-g07-client-contracts").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    /// <summary>
    /// Both installed packages declare a client facet, and each closure is stated dependency first.
    /// </summary>
    /// <remarks>
    /// The order is the load order a browser follows, and it is a property of the declared package graph
    /// rather than of the folders the loader happened to find. Movies requires the video package, so video
    /// precedes it; video requires nothing, so its closure is itself.
    /// </remarks>
    [Test]
    public async Task EachPackageStatesItsClientClosureDependencyFirst()
    {
        var root = Install(VideoPackage, MoviesPackage);
        using var services = BuildProvider(root);
        var bootstrapper = Bootstrapper(services);

        await bootstrapper.StartAsync(CancellationToken.None);

        var manifest = services.GetRequiredService<IClientContractCatalog>().Manifest();
        var withheld = services.GetRequiredService<IClientContractCatalog>().Withheld();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        withheld.Should().BeEmpty();
        manifest.Packages.Select(package => package.Id).Should().Equal(VideoPackage, MoviesPackage);

        var movies = manifest.Packages.Single(package => package.Id == MoviesPackage);
        movies.Closure.Should().Equal(
            [VideoPackage, MoviesPackage],
            "a browser loads a dependency before anything that binds to it, and the host states that order "
            + "rather than leaving a client to infer it from reference tables");
        movies.Assemblies.Select(assembly => assembly.FileName).Should().Equal(MoviesContract);

        var video = manifest.Packages.Single(package => package.Id == VideoPackage);
        video.Closure.Should().Equal(VideoPackage);
        video.Assemblies.Select(assembly => assembly.FileName).Should().Equal(VideoContract);

        manifest.ContractIdentity.Should().Be(
            typeof(Abstractions.Media.IMediaEntity).Assembly.GetName().FullName);
    }

    /// <summary>
    /// The bytes offered are the bytes the installation admitted, and every published fact about them holds.
    /// </summary>
    [Test]
    public async Task AClientFacetCarriesTheExactAdmittedBytesIdentityAndModule()
    {
        var root = Install(VideoPackage, MoviesPackage);
        using var services = BuildProvider(root);
        var bootstrapper = Bootstrapper(services);

        await bootstrapper.StartAsync(CancellationToken.None);

        var catalog = services.GetRequiredService<IClientContractCatalog>();
        var published = catalog.Manifest().Packages
            .Single(package => package.Id == MoviesPackage).Assemblies.Single();
        var opened = catalog.Open(MoviesPackage, MoviesContract, published.ContentHash);

        var staged = await File.ReadAllBytesAsync(Path.Combine(root, MoviesPackage, MoviesContract));
        var stagedHash = Convert.ToHexString(SHA256.HashData(staged));

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        published.ContentHash.Should().Be(stagedHash);
        published.AssemblyName.Should().Be("Arronix.Media.Movies");
        published.Identity.Should().Be("Arronix.Media.Movies, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        published.Length.Should().Be(staged.Length);

        opened.Outcome.Should().Be(ClientContractOutcome.Served);
        opened.Identity.Should().Be(published.Identity);
        opened.Content.ToArray().Should().Equal(
            staged,
            "the bytes a browser receives are the bytes this installation proved, not a second read of a "
            + "file the package still owns");
    }

    /// <summary>
    /// The facet is a subset, and everything outside it is unreachable from the outside.
    /// </summary>
    /// <remarks>
    /// The entry assembly is the case that matters. It sits in the same folder as the contract assembly and
    /// carries the module, the parser and the generated projections; there is no address that names it.
    /// </remarks>
    [Test]
    public async Task NothingOutsideTheDeclaredFacetIsOffered()
    {
        var root = Install(VideoPackage, MoviesPackage);
        using var services = BuildProvider(root);
        var bootstrapper = Bootstrapper(services);

        await bootstrapper.StartAsync(CancellationToken.None);

        var catalog = services.GetRequiredService<IClientContractCatalog>();
        var moviesHash = catalog.Manifest().Packages
            .Single(package => package.Id == MoviesPackage).Assemblies.Single().ContentHash;

        var entry = catalog.Open(MoviesPackage, MoviesEntryAssembly, moviesHash);
        var foreign = catalog.Open(MoviesPackage, VideoContract, moviesHash);
        var unknown = catalog.Open("books", MoviesContract, moviesHash);
        var superseded = catalog.Open(MoviesPackage, MoviesContract, new string('0', 64));

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        entry.Outcome.Should().Be(ClientContractOutcome.NotOffered);
        foreign.Outcome.Should().Be(ClientContractOutcome.NotOffered);
        unknown.Outcome.Should().Be(ClientContractOutcome.NotOffered);

        // Superseded rather than not-offered, because the file is still published at a different address and
        // re-reading the manifest is the recovery. A client told "not found" would give up instead.
        superseded.Outcome.Should().Be(ClientContractOutcome.Superseded);
        superseded.Content.Length.Should().Be(0);
    }

    /// <summary>
    /// The closure hash covers the closure, so an installation missing a dependency is a different hash.
    /// </summary>
    [Test]
    public async Task AnInstallationThatOffersLessHashesDifferently()
    {
        var both = Install(VideoPackage, MoviesPackage);
        using var withMovies = BuildProvider(both);
        var withMoviesBootstrapper = Bootstrapper(withMovies);
        await withMoviesBootstrapper.StartAsync(CancellationToken.None);
        var full = withMovies.GetRequiredService<IClientContractCatalog>().Manifest();
        await withMoviesBootstrapper.StopAsync(CancellationToken.None);

        _stateRoot = Directory.CreateTempSubdirectory("arronix-g07-client-contracts-video").FullName;
        var videoOnly = Install(VideoPackage);
        using var withoutMovies = BuildProvider(videoOnly);
        var withoutMoviesBootstrapper = Bootstrapper(withoutMovies);
        await withoutMoviesBootstrapper.StartAsync(CancellationToken.None);
        var partial = withoutMovies.GetRequiredService<IClientContractCatalog>().Manifest();
        await withoutMoviesBootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        partial.Packages.Select(package => package.Id).Should().Equal(VideoPackage);
        partial.InstallationHash.Should().NotBe(full.InstallationHash);

        // The video package's own closure did not change, so its closure hash must not have either: a hash
        // that moved with an unrelated package would make every client refetch everything.
        partial.Packages.Single().ClosureHash.Should().Be(
            full.Packages.Single(package => package.Id == VideoPackage).ClosureHash);
    }

    /// <summary>
    /// A withdrawn installation offers a browser nothing, because the catalog is a projection of what is
    /// Active rather than a second registry that has to be kept in step.
    /// </summary>
    [Test]
    public async Task AStoppedInstallationOffersNothing()
    {
        var root = Install(VideoPackage, MoviesPackage);
        using var services = BuildProvider(root);
        var bootstrapper = Bootstrapper(services);

        await bootstrapper.StartAsync(CancellationToken.None);
        var running = services.GetRequiredService<IClientContractCatalog>().Manifest();

        await bootstrapper.StopAsync(CancellationToken.None);
        var stopped = services.GetRequiredService<IClientContractCatalog>().Manifest();

        using var assertions = new AssertionScope();

        running.Packages.Should().HaveCount(2);
        stopped.Packages.Should().BeEmpty();
        stopped.ContractIdentity.Should().Be(running.ContractIdentity);
    }

    /// <summary>Installs exactly the named staged packages into one clean plugin root.</summary>
    private string Install(params string[] packages)
    {
        var root = Path.Combine(_stateRoot, "plugins");
        Directory.CreateDirectory(root);

        foreach (var package in packages)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", package);
            Directory.Exists(source).Should().BeTrue(
                $"the build must stage '{source}' before a test can install it");

            var destination = Path.Combine(root, package);
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }
        }

        return root;
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
        services.AddSingleton<ICacheProvider, PlatformServiceStub>();
        services.AddSingleton<ITelemetryEmitter, PlatformServiceStub>();
        services.AddSingleton<IEventPublisher, PlatformServiceStub>();
        services.AddSingleton<IHostRuntimeInfo, PlatformServiceStub>();
        services.AddSingleton<IOperatingSystemInfo, PlatformServiceStub>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static PluginBootstrapper Bootstrapper(ServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();
}
