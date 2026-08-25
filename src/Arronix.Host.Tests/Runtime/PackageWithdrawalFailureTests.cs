using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.Support;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Versioning;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// What the platform does when a package's code cannot be released.
/// </summary>
/// <remarks>
/// <para>
/// The safe answer and the tidy answer differ here, and the platform takes the safe one. An unload request
/// that reported a failure is not proof the code is gone, so the package goes on holding everything it
/// depends on, its identifier stays occupied, the installation's contract context is not released, and
/// shutdown is reported incomplete — including on a repeated stop, where an empty active set would otherwise
/// look like a clean finish.
/// </para>
/// <para>
/// The fixture's unloading handler throws for one declared version, so the failure arrives through the same
/// path a hostile or merely buggy extension would use rather than through a seam invented for the test.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class PackageWithdrawalFailureTests
{
    private const string ThrowingUnloadVersion = "0.1.1";

    private static readonly PluginId Fixture = PluginId.FromString("g02.admission.fixture");
    private static readonly PluginId VideoPackage = PluginId.FromString("arronix.format.video");

    private string _stateRoot = string.Empty;

    [SetUp]
    public void SetUp() =>
        _stateRoot = Directory.CreateTempSubdirectory("arronix-withdrawal-failure").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    [Test]
    public async Task ACleanClosureStopReleasesEverythingAndReportsShutdownComplete()
    {
        var root = Install(dependantVersion: "0.1.0");
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);
        var loader = provider.GetRequiredService<PluginLoader>();

        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        bootstrapper.ShutdownState.Should().Be(PluginBootstrapper.LifecycleState.Stopped);
        loader.Dependencies.RootedPackages.Should().BeEmpty();
        loader.Dependencies.RetainedPackages.Should().BeEmpty();
        loader.SharedContracts.UnloadRequested.Should().BeTrue(
            "the contract context is the last thing holding extension types, so a clean stop releases it");
    }

    [Test]
    public async Task ADependantWhoseUnloadFailedGoesOnHoldingItsDependencyAndTheContractContext()
    {
        var root = Install(dependantVersion: ThrowingUnloadVersion);
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);
        var loader = provider.GetRequiredService<PluginLoader>();
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();

        await bootstrapper.StartAsync(CancellationToken.None);

        runtime.Active.Select(result => result.Id).Should().BeEquivalentTo([Fixture, VideoPackage]);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        runtime.TryGet(Fixture, out var dependant).Should().BeTrue();
        dependant!.State.Should().Be(
            PluginState.Stopped,
            "the dependant is hidden and stopped even though its context refused to unload");

        loader.Dependencies.DependantsOf(VideoPackage).Should().Equal(
            [Fixture],
            "an unload that reported a failure is not proof the code is gone, so the pin stays");

        loader.Dependencies.RetainedPackages.Should().Contain(Fixture);
        bootstrapper.ShutdownState.Should().Be(PluginBootstrapper.LifecycleState.StopIncomplete);

        loader.SharedContracts.Holders.Should().Contain(
            Fixture,
            "a package whose code may still be resident goes on holding the contracts it can reach");
        loader.SharedContracts.UnloadRequested.Should().BeFalse();

        runtime.Active.Select(result => result.Id).Should().Equal(
            [VideoPackage],
            "the dependency stays active because something may still be using it");
    }

    /// <remarks>
    /// The case an empty active set makes easy to get wrong. A package is recorded stopped before its
    /// instances are disposed, so the moment its release fails it is absent from the active set while its
    /// receipt, identifier and pins are all still held. A second pass must not talk itself into a clean
    /// finish from that.
    /// </remarks>
    [Test]
    public async Task ARepeatedStopDoesNotTalkItselfIntoAFinishedShutdown()
    {
        var root = Install(dependantVersion: ThrowingUnloadVersion);
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);
        var loader = provider.GetRequiredService<PluginLoader>();

        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.StopAsync(CancellationToken.None);
        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        bootstrapper.ShutdownState.Should().Be(PluginBootstrapper.LifecycleState.StopIncomplete);
        loader.Dependencies.RetainedPackages.Should().Contain(Fixture);
        loader.SharedContracts.UnloadRequested.Should().BeFalse(
            "nothing changed between the two passes, so nothing new can be claimed");
    }

    /// <remarks>
    /// The retention claim, measured rather than described. The stopped result is deliberately free of
    /// references — that is what makes it safe to publish — so if the dependency registry rooted only the
    /// receipt, the lifetime holding this package's load context and instances would be collectible while
    /// the platform reported them possibly resident.
    /// </remarks>
    [Test]
    public async Task AFailedWithdrawalGoesOnHoldingTheExactLifetimeItCouldNotReleaseAsync()
    {
        var root = Install(dependantVersion: ThrowingUnloadVersion);
        using var provider = BuildProvider(root);
        var bootstrapper = Bootstrapper(provider);
        var loader = provider.GetRequiredService<PluginLoader>();
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();

        await bootstrapper.StartAsync(CancellationToken.None);

        var held = Handles(runtime);
        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();

        runtime.TryGet(Fixture, out var stopped).Should().BeTrue();
        stopped!.PackageLease.Should().BeNull("a stopped result carries no reference to what it could not release");
        stopped.LoadContext.Should().BeNull();

        loader.Dependencies.RetainedPackages.Should().Contain(Fixture);
        Reachable(held.Lifetime).Should().BeTrue("the registry roots the lifetime that still owns the code");
        Reachable(held.Context).Should().BeTrue("and the context it could not unload is under it");

        // The other half of the same claim, asked of the live registry this withdrawal left behind: the
        // identifier is refused before a replacement reads a byte or runs a line. What that refusal does to
        // a whole load pass has its own proof in the dependency registry's tests, which reach it from a
        // second installation rather than from a second pass over this one.
        loader.Dependencies.TryPinDependencies(Replacement(), out var refusal).Should().BeFalse();
        refusal.Should().ContainSingle().Which.Should().Contain("retained");
    }

    /// <summary>A second installation attempt of the same identifier, prepared but not yet pinned.</summary>
    private PackageAdmissionReceipt Replacement()
        => new(
            new InstalledPackage(
                Fixture,
                SemanticVersion.Parse("0.2.0"),
                Path.Combine(_stateRoot, "replacement", "plugin.json"),
                Path.Combine(_stateRoot, "replacement")),
            []);

    /// <summary>Weak handles to the exact lifetime and context this package is active on.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Lifetime, WeakReference Context) Handles(PluginRuntimeRegistry runtime)
    {
        runtime.TryGet(Fixture, out var active).Should().BeTrue();
        return (new WeakReference(active!.PackageLease), new WeakReference(active.LoadContext));
    }

    private static bool Reachable(WeakReference held)
    {
        for (var attempt = 0; attempt < 12 && held.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return held.IsAlive;
    }

    /// <summary>
    /// Installs the packaged fixture as a dependant of the installed video package.
    /// </summary>
    /// <param name="dependantVersion">The version the fixture declares, which selects its behavior.</param>
    /// <returns>The plugin root.</returns>
    private string Install(string dependantVersion)
    {
        var root = Path.Combine(_stateRoot, "plugins");
        var fixture = Path.Combine(root, "a-fixture");

        CopyPackage(
            Path.Combine(AppContext.BaseDirectory, "G02PackagedPlugins", Fixture.Value),
            fixture);
        CopyPackage(
            Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", VideoPackage.Value),
            Path.Combine(root, "z-video"));

        var manifestPath = Path.Combine(fixture, "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["version"] = dependantVersion;
        manifest["dependencies"] = new JsonArray(
            new JsonObject { ["package"] = VideoPackage.Value, ["range"] = ">=0.1 <0.2" });

        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

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
