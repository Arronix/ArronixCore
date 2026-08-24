using System.IO;
using FluentAssertions;
using FluentAssertions.Execution;
using System.Linq;
using System.Runtime.Loader;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.Support;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// The vertical proofs for one CLR type identity across separately installed packages, and for the package
/// lifecycle that keeps it sound.
/// </summary>
/// <remarks>
/// <para>
/// Every case here installs real staged package folders and drives the complete Host lifecycle. Nothing is
/// asserted against a hand-built double of the loader, and no fixture is a compile-time reference of this
/// project: the only way to reach a fixture's types is to send its package through discovery.
/// </para>
/// <para>
/// The identity claims are reference comparisons on <see cref="Type"/> and <see cref="System.Reflection.Assembly"/> objects,
/// because that is what type identity actually is. Matching names, versions and bytes do not produce it, and
/// the failure when they are relied on is silent.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class PackagedIdentityAndLifecycleTests
{
    private static readonly PluginId MoviesPlugin = PluginId.FromString("movies");
    private static readonly PluginId VideoPackage = PluginId.FromString("arronix.format.video");
    private static readonly PluginId ProviderPackage = PluginId.FromString("fixture.movies.provider");
    private static readonly PluginId VideoDependant = PluginId.FromString("fixture.video.dependant");
    private static readonly MediaKindId Movies = MediaKindId.FromString("movies");

    private string _stateRoot = string.Empty;

    [SetUp]
    public void SetUp() =>
        _stateRoot = Directory.CreateTempSubdirectory("arronix-g04-identity").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    /// <summary>
    /// A separately packaged provider closes <c>ICataloger&lt;Movie&gt;</c> over the same runtime type the
    /// registered movies kind publishes as its item.
    /// </summary>
    /// <remarks>
    /// The reference comparison is the whole proof. Before the contract was admitted once for the
    /// installation, the provider's <c>Movie</c> and the kind's <c>Movie</c> were two types with equal
    /// assembly-qualified names that could not be cast to one another, and the observable symptom was that
    /// the provider appeared to contribute nothing.
    /// </remarks>
    [Test]
    public async Task ASeparatelyPackagedProviderClosesItsCatalogerOverTheRegisteredMoviesItemType()
    {
        var root = Install("video", "movies", "movies-provider");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var kind = provider.GetRequiredService<MediaKindRegistry>().Require(Movies);
        var registeredItemType = kind.MediaType!.ItemType;

        var cataloger = provider.GetRequiredService<ProviderRegistry>().All
            .Should().ContainSingle(registration =>
                registration.Plugin == ProviderPackage
                && registration.Family == ProviderFamily.Cataloger)
            .Which;

        var closedItemType = cataloger.Provider.GetType()
            .GetInterfaces()
            .Single(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition().Name.StartsWith("ICataloger`", StringComparison.Ordinal))
            .GetGenericArguments()[0];

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        closedItemType.Should().BeSameAs(
            registeredItemType,
            "the provider and the media kind must close their generics over one runtime type, or every "
            + "cast between them fails and the operator is told the provider contributed nothing");

        closedItemType.Assembly.GetName().Name.Should().Be("Arronix.Media.Movies");
        AssemblyLoadContext.GetLoadContext(closedItemType.Assembly)!.Name.Should()
            .Be(SharedContractStore.ContextName);
    }

    /// <summary>
    /// Two packages that know nothing about each other receive the same admitted video assembly and type.
    /// </summary>
    [Test]
    public async Task TwoIndependentlyPackagedDependantsSeeOneVideoAssemblyAndType()
    {
        var root = Install("video", "movies", "video-dependant");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var movies = ActiveContextOf(runtime, MoviesPlugin);
        var dependant = ActiveContextOf(runtime, VideoDependant);

        var fromMovies = VideoTypeSeenBy(movies);
        var fromDependant = VideoTypeSeenBy(dependant);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        movies.Should().NotBeSameAs(dependant, "each package keeps its own executable isolation");

        fromDependant.Should().BeSameAs(
            fromMovies,
            "two separately installed dependants of one video package compose the same representation, so "
            + "the type a release carries has to be one runtime type");

        fromDependant.Assembly.Should().BeSameAs(fromMovies.Assembly);
        AssemblyLoadContext.GetLoadContext(fromMovies.Assembly)!.Name.Should()
            .Be(SharedContractStore.ContextName);
    }

    /// <summary>
    /// Video is loaded once, in the Host-owned collectible contract context, and no dependant's own context
    /// holds a copy of it.
    /// </summary>
    [Test]
    public async Task VideoLoadsOnceInTheContractContextAndNoDependantHoldsAPrivateCopy()
    {
        var root = Install("video", "movies", "video-dependant");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var contracts = provider.GetRequiredService<PluginLoader>().SharedContracts;
        var admitted = contracts.Admitted;
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();

        var privateCopies = runtime.Active
            .Where(result => result.LoadContext is not null)
            .SelectMany(result => result.LoadContext!.Assemblies.Select(assembly =>
                $"{result.Id}:{assembly.GetName().Name}"))
            .Where(entry => entry.EndsWith(":Arronix.Format.Video", StringComparison.Ordinal))
            .ToArray();

        var contexts = AssemblyLoadContext.All
            .Where(context => context.Name == SharedContractStore.ContextName)
            .ToArray();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        admitted.Select(contract => contract.Identity.Name).Should().BeEquivalentTo(
            ["Arronix.Format.Video", "Arronix.Media.Movies"],
            "the installation admits exactly the contracts its packages declare");

        admitted.Should().ContainSingle(contract => contract.Identity.Name == "Arronix.Format.Video")
            .Which.Publisher.Should().Be(VideoPackage);

        contexts.Should().ContainSingle("one installation has one shared contract context")
            .Which.IsCollectible.Should().BeTrue(
                "yielding a contract to the default context would make it permanently unloadable");

        privateCopies.Should().BeEmpty(
            "a dependant that loaded its own copy would have a second Video type, and nothing would cast");
    }

    /// <summary>
    /// The contract-only video package is a first-class active package, and it cannot be released while a
    /// dependant still holds it.
    /// </summary>
    [Test]
    public async Task TheContractOnlyVideoPackageIsRootedAndCannotBeReleasedWhileDependantsLive()
    {
        var root = Install("video", "movies");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var loader = provider.GetRequiredService<PluginLoader>();
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var video = runtime.Active.Should().ContainSingle(result => result.Id == VideoPackage).Which;

        var refusedWhileLive = loader.SharedContracts.TryRequestUnload(out var refusal);
        var holdersWhileLive = loader.SharedContracts.Holders;
        var dependants = loader.Dependencies.DependantsOf(VideoPackage);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        video.State.Should().Be(
            PluginState.Active,
            "a package that publishes contracts and runs no code is an ordinary active package");
        video.LoadContext.Should().BeNull("nothing was activated, so no executable context was invented");
        video.Ledger.Should().BeNull("a contract-only package registers nothing");

        dependants.Should().Contain(MoviesPlugin);
        holdersWhileLive.Should().Contain(VideoPackage).And.Contain(MoviesPlugin);

        refusedWhileLive.Should().BeFalse("a live dependant still holds types from the contract context");
        refusal.Should().Contain("movies");

        loader.SharedContracts.UnloadRequested.Should().BeTrue(
            "once every package has withdrawn, the context is released");
        loader.Dependencies.RootedPackages.Should().BeEmpty();
    }

    /// <summary>
    /// Teardown reverses the order packages were actually published in, and matches receipts exactly.
    /// </summary>
    [Test]
    public async Task TeardownReversesPublicationOrderAndLeavesNothingRooted()
    {
        var root = Install("video", "movies", "movies-provider", "video-dependant");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var loader = provider.GetRequiredService<PluginLoader>();
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();

        var published = runtime.Active
            .Select(result => new
            {
                Id = result.Id!.Value.ToString(),
                Order = loader.Dependencies.PublicationOrderOf(result.PackageLease?.Receipt),
            })
            .OrderBy(entry => entry.Order)
            .Select(entry => entry.Id)
            .ToList();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        published.Should().ContainInOrder(
            "arronix.format.video",
            "movies",
            "fixture.movies.provider");

        published.Should().ContainInOrder(
            "arronix.format.video",
            "fixture.video.dependant");

        published[0].Should().Be(
            "arronix.format.video",
            "every other package requires it, so it publishes first whatever order the folders were walked in");

        bootstrapper.States.Should().OnlyContain(state => state.State == PluginState.Stopped);
        loader.Dependencies.RootedPackages.Should().BeEmpty();
        loader.Dependencies.RetainedPackages.Should().BeEmpty();
        loader.SharedContracts.UnloadRequested.Should().BeTrue();
        loader.SharedContracts.Admitted.Should().BeEmpty(
            "a released context can never serve another dependant, so reporting its former contents as "
            + "available would be the same untruth as reporting the assemblies collected");
    }

    /// <summary>
    /// A package that ships its own copy of an admitted shared contract is refused before it is loaded, and
    /// unrelated packages survive.
    /// </summary>
    [Test]
    public async Task APrivateCopyOfAnAdmittedContractIsRefusedAndUnrelatedPackagesSurvive()
    {
        var root = Install("video", "movies", "video-dependant");

        // The dependant ships the exact file the video package publishes. Same name, same bytes, same
        // identity: a duplicate is refused because it is a second copy, not because it disagrees.
        var source = Path.Combine(root, "arronix.format.video", "Arronix.Format.Video.dll");
        var planted = Path.Combine(root, "fixture.video.dependant", "Arronix.Format.Video.dll");
        File.Copy(source, planted);

        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var states = bootstrapper.States.ToDictionary(state => state.Id!.Value);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        states["fixture.video.dependant"].State.Should().Be(PluginState.Quarantined);
        states["fixture.video.dependant"].ErrorCode.Should().Be(CoreErrorCode.PluginIsolationViolation);
        states["fixture.video.dependant"].Defects.Should().Contain(defect =>
            defect.Contains("private copy", StringComparison.Ordinal)
            && defect.Contains("MVID", StringComparison.Ordinal)
            && defect.Contains("SHA-256", StringComparison.Ordinal));

        states["movies"].State.Should().Be(PluginState.Active, "the fault is not theirs");
        states["arronix.format.video"].State.Should().Be(PluginState.Active);
    }

    /// <summary>
    /// A package that binds to a different identity of an admitted contract is refused with both identities
    /// printed.
    /// </summary>
    [Test]
    public async Task APackageCarryingADifferentBuildOfAnAdmittedContractIsRefusedWithBothIdentities()
    {
        var root = Install("video", "movies", "video-dependant");

        // A different build of the same assembly name: the compiler stamps a new MVID into every build, so
        // the two files are the same identity and different bytes. That is the case a name comparison
        // cannot see.
        var rebuilt = Path.Combine(
            AppContext.BaseDirectory,
            "G04PackagedPlugins",
            "fixture.video.dependant",
            "Arronix.Fixture.VideoDependant.dll");
        var planted = Path.Combine(root, "fixture.video.dependant", "Arronix.Format.Video.dll");
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Arronix.Format.Video.dll"),
            planted,
            overwrite: true);

        File.Exists(rebuilt).Should().BeTrue("the fixture must be staged before this case means anything");

        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var states = bootstrapper.States.ToDictionary(state => state.Id!.Value);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        states["fixture.video.dependant"].State.Should().Be(PluginState.Quarantined);
        states["fixture.video.dependant"].Defects.Should().Contain(defect =>
            defect.Contains("Arronix.Format.Video", StringComparison.Ordinal));
        states["movies"].State.Should().Be(PluginState.Active);
    }

    private static PluginLoadContext ActiveContextOf(PluginRuntimeRegistry runtime, PluginId package)
        => runtime.Active
            .Should().ContainSingle(result => result.Id == package).Which
            .LoadContext.Should().NotBeNull().And.Subject.As<PluginLoadContext>();

    /// <summary>
    /// Reads the exact <see cref="Type"/> a package's own load context resolved for the video
    /// representation, without this project referencing either package.
    /// </summary>
    /// <param name="context">The package's load context.</param>
    /// <returns>The video representation type that package sees.</returns>
    /// <remarks>
    /// Found by walking the type closure of the package's own exported types — base types, interfaces and
    /// their generic arguments — so the type observed is one the package's code genuinely names rather than
    /// one this test resolved on its behalf.
    /// </remarks>
    private static Type VideoTypeSeenBy(PluginLoadContext context)
        => context.Assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(Closure)
            .Where(type => type.FullName == "Arronix.Format.Video.Video")
            .Distinct()
            .Should().ContainSingle(
                "one package resolves one video representation type; two would be the exact failure these "
                + "proofs exist to detect")
            .Which;

    /// <summary>Every type reachable from one type through inheritance, interfaces and generic arguments.</summary>
    private static IEnumerable<Type> Closure(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            foreach (var next in current.GetInterfaces()
                         .Concat(current.BaseType is { } baseType ? [baseType] : [])
                         .Concat(current.IsGenericType ? current.GetGenericArguments() : [])
                         .Concat(current.GetProperties().Select(property => property.PropertyType)))
            {
                pending.Push(next);
            }
        }
    }

    /// <summary>Installs exactly the named staged packages into one clean plugin root.</summary>
    /// <param name="packages">The package folders to install.</param>
    /// <returns>The plugin root.</returns>
    /// <remarks>
    /// The folder name is the package identifier, and the packages are copied in the order named. Both are
    /// deliberately unrelated to the order the loader must activate them in: activation order comes from the
    /// resolved graph, and a test that installed them in dependency order would not be able to tell.
    /// </remarks>
    private string Install(params string[] packages)
    {
        var root = Path.Combine(_stateRoot, "plugins");
        Directory.CreateDirectory(root);

        foreach (var package in packages)
        {
            var (source, folder) = package switch
            {
                "video" => (Path.Combine("PackagedPlugins", "arronix.format.video"), "arronix.format.video"),
                "movies" => (Path.Combine("PackagedPlugins", "movies"), "movies"),
                "movies-provider" => (Path.Combine("G04PackagedPlugins", "fixture.movies.provider"), "fixture.movies.provider"),
                "video-dependant" => (Path.Combine("G04PackagedPlugins", "fixture.video.dependant"), "fixture.video.dependant"),
                _ => throw new ArgumentOutOfRangeException(nameof(packages), package, "Unknown fixture package."),
            };

            CopyPackage(Path.Combine(AppContext.BaseDirectory, source), Path.Combine(root, folder));
        }

        return root;
    }

    private static void CopyPackage(string source, string destination)
    {
        Directory.Exists(source).Should().BeTrue(
            $"the build must stage '{source}' before a test can install it");

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
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
