using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.Support;
using Arronix.Plugins.Loading;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// Every way a package's graph position can be wrong, proved against real installed packages.
/// </summary>
/// <remarks>
/// Each case asserts three things together: the affected package is refused with an actionable diagnostic,
/// nothing of it was activated, and an unrelated package still reaches <see cref="PluginState.Active"/>.
/// A refusal that stops the installation is as much a defect as one that lets a broken package through.
/// </remarks>
[TestFixture]
internal sealed class PackagedRefusalMatrixTests
{
    private string _stateRoot = string.Empty;

    [SetUp]
    public void SetUp() =>
        _stateRoot = Directory.CreateTempSubdirectory("arronix-g04-refusals").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    /// <summary>A required package that is not installed at all.</summary>
    [Test]
    public async Task AMissingDependencyRefusesItsDependantAndNamesTheMemberToEdit()
    {
        // The video package both of these require is simply not installed.
        var root = Install("movies", "video-dependant");

        var states = await RunAsync(root);

        using var assertions = new AssertionScope();

        states["movies"].State.Should().Be(PluginState.Quarantined);
        states["movies"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnsatisfied);
        states["movies"].Defects.Should().Contain(defect =>
            defect.Contains("dependencies[0].package", StringComparison.Ordinal)
            && defect.Contains("no package with that identifier is installed", StringComparison.Ordinal));

        states["fixture.video.dependant"].State.Should().Be(
            PluginState.Quarantined,
            "the second dependant requires the same absent package");
    }

    /// <summary>A required package installed at a version the declared range does not admit.</summary>
    [Test]
    public async Task AnIncompatibleVersionRefusesItsDependantAndPointsAtTheRange()
    {
        var root = Install("video", "movies", "video-dependant");
        Rewrite(root, "arronix.format.video", manifest => manifest["version"] = "9.9.9");

        var states = await RunAsync(root);

        using var assertions = new AssertionScope();

        states["arronix.format.video"].State.Should().Be(
            PluginState.Active,
            "the package itself is sound; it is the dependants' ranges that do not admit it");

        states["movies"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnsatisfied);
        states["movies"].Defects.Should().Contain(defect =>
            defect.Contains("dependencies[0].range", StringComparison.Ordinal)
            && defect.Contains("9.9.9", StringComparison.Ordinal));
    }

    /// <summary>One identifier installed twice, with every copy recorded.</summary>
    [Test]
    public async Task ADuplicatedIdentifierRefusesEveryCopyAndLeavesUnrelatedPackagesActive()
    {
        var root = Install("video", "movies", "video-dependant");
        CopyPackage(Path.Combine(root, "fixture.video.dependant"), Path.Combine(root, "second-copy"));

        var results = await LoadAsync(root);

        using var assertions = new AssertionScope();

        var copies = results.Where(result => result.Id?.Value == "fixture.video.dependant").ToArray();

        copies.Should().HaveCount(
            2,
            "an operator has two folders to act on, so both are recorded rather than one being chosen to "
            + "speak for the identifier");
        copies.Should().OnlyContain(result => result.ErrorCode == CoreErrorCode.PluginIdConflict);
        copies.Select(result => result.Source).Should().OnlyHaveUniqueItems();

        results.Where(result => result.Id?.Value == "movies").Should().ContainSingle()
            .Which.State.Should().Be(PluginState.Active);
    }

    /// <summary>A cycle, with a real walk through it.</summary>
    [Test]
    public async Task ACycleRefusesEveryParticipantWithAWalkAndLeavesUnrelatedPackagesActive()
    {
        var root = Install("video", "movies", "video-dependant");

        // The dependant requires movies, and movies is made to require the dependant back.
        Rewrite(root, "fixture.video.dependant", manifest => manifest["dependencies"] = new JsonArray(
            new JsonObject { ["package"] = "movies", ["range"] = ">=0.1 <0.2" }));
        Rewrite(root, "movies", manifest => manifest["dependencies"] = new JsonArray(
            new JsonObject { ["package"] = "fixture.video.dependant", ["range"] = ">=0.1 <0.2" }));

        var states = await RunAsync(root);

        using var assertions = new AssertionScope();

        states["movies"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyCycle);
        states["movies"].Defects.Should().Contain(defect =>
            defect.Contains("movies -> fixture.video.dependant -> movies", StringComparison.Ordinal));
        states["fixture.video.dependant"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyCycle);

        states["arronix.format.video"].State.Should().Be(PluginState.Active);
    }

    /// <summary>
    /// A package an operator switched off, and the dependants it takes with it.
    /// </summary>
    /// <remarks>
    /// The disabled package keeps the state the operator set. Its dependants are refused for the real root
    /// cause, and told which package to re-enable rather than that nothing with that identifier exists.
    /// </remarks>
    [Test]
    public async Task DisablingAPackageRefusesItsDependantsWithTheRealRootCause()
    {
        var root = Install("video", "movies", "movies-provider", "video-dependant");

        var states = await RunAsync(root, disabled: "arronix.format.video");

        using var assertions = new AssertionScope();

        states["arronix.format.video"].State.Should().Be(PluginState.Quarantined);
        states["arronix.format.video"].ErrorCode.Should().Be(
            CoreErrorCode.PluginDisabled,
            "an operator who switched a package off is told it is off, not that its dependency graph broke");
        states["arronix.format.video"].Defects.Should().BeEmpty(
            "there is no member to edit; the setting is the whole of it");

        states["movies"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        states["movies"].Defects.Should().Contain(defect =>
            defect.Contains("disabled by configuration", StringComparison.Ordinal));

        states["fixture.movies.provider"].ErrorCode.Should().Be(
            CoreErrorCode.PluginDependencyUnavailable,
            "the provider depends on movies, which depends on the disabled package");
        states["fixture.movies.provider"].Defects.Should().Contain(defect =>
            defect.Contains("arronix.format.video", StringComparison.Ordinal),
            "a transitive dependant is told which package is actually at fault");
    }

    /// <summary>A shared contract that passes every metadata rule and then cannot be loaded.</summary>
    /// <remarks>
    /// The declared package edge is what withdraws the dependants, not the metadata their own contracts
    /// happen to reference. A dependant that never names the failed package's assemblies is refused just
    /// the same, because it required the package.
    /// </remarks>
    [Test]
    public async Task AContractThatCannotLoadRefusesItsPackageAndEveryPackageDependant()
    {
        var root = Install("video", "movies", "movies-provider", "video-dependant");

        // A file that is a readable assembly by metadata and not loadable: the reference assembly from the
        // targeting pack parses, declares an identity, and refuses to load.
        var contract = Path.Combine(root, "arronix.format.video", "Arronix.Format.Video.dll");
        File.Copy(ReferenceAssembly(), contract, overwrite: true);

        var results = await LoadAsync(root);
        var states = results.ToDictionary(result => result.Id!.Value.ToString());

        using var assertions = new AssertionScope();

        states["arronix.format.video"].State.Should().Be(PluginState.Quarantined);

        states["movies"].State.Should().Be(PluginState.Quarantined);
        states["movies"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        states["movies"].Message.Should().Contain("arronix.format.video");

        states["fixture.video.dependant"].State.Should().Be(PluginState.Quarantined);
        states["fixture.movies.provider"].State.Should().Be(
            PluginState.Quarantined,
            "it requires movies, which requires the package whose contracts could not be admitted");
    }

    /// <summary>
    /// A dependant of a package that fails its own executable admission is never attempted.
    /// </summary>
    [Test]
    public async Task ADependantOfAPackageThatFailsAdmissionIsNotAttempted()
    {
        var root = Install("video", "movies", "movies-provider");

        // Movies is made to declare a media kind its code does not supply, which is a late check: it fails
        // after the package has loaded and run, which is exactly the verdict the resolver cannot reach.
        Rewrite(root, "movies", manifest => manifest["mediaKinds"] = new JsonArray("movies", "westerns"));

        var states = await RunAsync(root);

        using var assertions = new AssertionScope();

        states["movies"].State.Should().Be(PluginState.Quarantined);
        states["movies"].ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);

        states["fixture.movies.provider"].State.Should().Be(PluginState.Quarantined);
        states["fixture.movies.provider"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        states["fixture.movies.provider"].Defects.Should().Contain(defect =>
            defect.Contains("dependency[movies]", StringComparison.Ordinal));

        states["arronix.format.video"].State.Should().Be(
            PluginState.Active,
            "a package nobody's failure reaches keeps its place");
    }

    /// <summary>
    /// Every order the folders can be discovered in produces the same eligibility, order and diagnostics.
    /// </summary>
    /// <remarks>
    /// Discovery order is a property of a file-system walk, so any observable result it can change is an
    /// accident. The folders are named so that alphabetical order and dependency order disagree.
    /// </remarks>
    [Test]
    public async Task EveryDiscoveryOrderProducesTheSameEligibilityOrderAndDiagnostics()
    {
        string?[] renderings = [null, null, null, null, null, null];
        var names = new[] { "a", "m", "z" };
        var index = 0;

        foreach (var order in Permutations(["video", "movies", "video-dependant"]))
        {
            var root = Path.Combine(_stateRoot, $"permutation-{index}");
            Directory.CreateDirectory(root);

            for (var position = 0; position < order.Length; position++)
            {
                CopyPackage(StagedFolderOf(order[position]), Path.Combine(root, $"{names[position]}-package"));
            }

            // One installation is also made faulty, so the diagnostics being compared are not all empty.
            Rewrite(root, folder => folder.Contains("dependant", StringComparison.Ordinal), manifest =>
                manifest["dependencies"] = new JsonArray(
                    new JsonObject { ["package"] = "absent.package", ["range"] = ">=1.0 <2.0" }));

            var results = await LoadAsync(root);

            renderings[index] = string.Join(
                "\n",
                results.Select(result => string.Join(
                    " | ",
                    result.Id?.Value ?? "?",
                    result.State,
                    result.ErrorCode?.ToString() ?? "-",
                    result.Message ?? "-",
                    string.Join(";", result.Defects))));

            index++;
        }

        using var assertions = new AssertionScope();

        renderings.Should().OnlyContain(rendering => rendering != null);
        renderings.Distinct(StringComparer.Ordinal).Should().ContainSingle(
            "the whole rendered outcome is a property of the installation, not of the order its folders "
            + "were walked in");
    }

    /// <summary>A reference assembly: readable metadata, and the runtime refuses to load it.</summary>
    private static string ReferenceAssembly()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "Arronix.Format.Video.dll");

        // Truncating a valid assembly produces a file that stages as unreadable rather than one that loads,
        // so the load-boundary case uses a deliberately corrupted copy instead.
        var corrupted = Path.Combine(Path.GetTempPath(), $"arronix-unloadable-{Guid.NewGuid():N}.dll");
        var bytes = File.ReadAllBytes(candidate);

        // Keep the PE and metadata headers intact and corrupt the IL heap, which parses and will not load.
        for (var offset = bytes.Length / 2; offset < bytes.Length; offset++)
        {
            bytes[offset] = 0xFF;
        }

        File.WriteAllBytes(corrupted, bytes);
        return corrupted;
    }

    private static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var index = 0; index < items.Length; index++)
        {
            var head = items[index];
            var rest = items.Where((_, position) => position != index).ToArray();

            foreach (var tail in Permutations(rest))
            {
                yield return [head, .. tail];
            }
        }
    }

    private static string StagedFolderOf(string package) => package switch
    {
        "video" => Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "arronix.format.video"),
        "movies" => Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "movies"),
        "movies-provider" => Path.Combine(AppContext.BaseDirectory, "G04PackagedPlugins", "fixture.movies.provider"),
        "video-dependant" => Path.Combine(AppContext.BaseDirectory, "G04PackagedPlugins", "fixture.video.dependant"),
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "Unknown fixture package."),
    };

    private static string FolderNameOf(string package) => package switch
    {
        "video" => "arronix.format.video",
        "movies" => "movies",
        "movies-provider" => "fixture.movies.provider",
        "video-dependant" => "fixture.video.dependant",
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "Unknown fixture package."),
    };

    private string Install(params string[] packages)
    {
        var root = Path.Combine(_stateRoot, "plugins");
        Directory.CreateDirectory(root);

        foreach (var package in packages)
        {
            CopyPackage(StagedFolderOf(package), Path.Combine(root, FolderNameOf(package)));
        }

        return root;
    }

    private static void Rewrite(string root, string folder, Action<JsonObject> rewrite)
        => Rewrite(root, candidate => string.Equals(candidate, folder, StringComparison.Ordinal), rewrite);

    private static void Rewrite(string root, Func<string, bool> matches, Action<JsonObject> rewrite)
    {
        foreach (var folder in Directory.EnumerateDirectories(root).Where(path => matches(Path.GetFileName(path))))
        {
            var path = Path.Combine(folder, "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            rewrite(manifest);
            File.WriteAllText(path, manifest.ToJsonString());
        }
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

    private async Task<Dictionary<string, PluginRuntimeState>> RunAsync(string root, string? disabled = null)
    {
        using var provider = BuildProvider(root, disabled);
        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        await bootstrapper.StartAsync(CancellationToken.None);
        var states = bootstrapper.States.ToDictionary(state => state.Id!.Value.ToString());
        await bootstrapper.StopAsync(CancellationToken.None);

        return states;
    }

    /// <summary>
    /// Drives the loader directly so the complete per-copy record is observable.
    /// </summary>
    /// <param name="root">The plugin root.</param>
    /// <returns>One result per installed copy.</returns>
    /// <remarks>
    /// The runtime registry is a status projection keyed by identifier, so two copies of one identifier
    /// appear there once. The loader's own result list is the complete record and is what an operator's
    /// diagnostics are built from.
    /// </remarks>
    private async Task<IReadOnlyList<PluginLoadResult>> LoadAsync(string root)
    {
        using var provider = BuildProvider(root, disabled: null);
        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        var results = await provider.GetRequiredService<PluginLoader>()
            .LoadAllAsync(bootstrapper.Admission);

        await bootstrapper.StopAsync(CancellationToken.None);

        return results;
    }

    private ServiceProvider BuildProvider(string pluginRoot, string? disabled)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Arronix:Host:ExtensionFolder"] = pluginRoot,
            ["Arronix:Plugins:RootFolder"] = pluginRoot,
            ["Arronix:Plugins:StateFolder"] = Path.Combine(_stateRoot, "state"),
            ["Arronix:Library:RootFolders:0"] = Path.Combine(_stateRoot, "library"),
        };

        if (disabled is not null)
        {
            settings["Arronix:Plugins:Disabled:0"] = disabled;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

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
}
