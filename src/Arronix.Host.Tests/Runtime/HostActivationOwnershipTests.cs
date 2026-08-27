using System.IO;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Plugins;
using Arronix.Host.Composition;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.DefinitionBinding;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// Who owns an object the host constructed from an extension's registration, from the instant it exists.
/// </summary>
/// <remarks>
/// A preparation that is refused half-way leaves live objects behind exactly as a successful one does. The
/// old shape disposed them in whichever catch noticed, which meant an object constructed and then refused
/// by the next line was owned by nobody until that catch ran, and a refusal that returned no attempt left
/// the package lifetime unaware of it entirely. Ownership now begins in the activation scope.
/// </remarks>
[TestFixture]
internal sealed class HostActivationOwnershipTests
{
    private static readonly PluginId Package = PluginId.FromString("ownership.fixture");

    private string _root = string.Empty;
    private ServiceProvider? _provider;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-ownership-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Plugins:RootFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Store:DataSource"] = Path.Combine(_root, "arronix.db"),
                ["Arronix:Library:RootFolders:0"] = _root,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [TearDown]
    public void TearDown()
    {
        _provider?.Dispose();
        _provider = null;

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void ARefusedPreparationLeavesEveryObjectItConstructedOwned()
    {
        var ledger = Ledger(registry => registry
            .AddLanguage<FirstLanguage>()
            .AddLanguage<RefusedLanguage>());

        var result = Prepare(ledger);

        using var assertions = new AssertionScope();
        result.IsAdmitted.Should().BeFalse("the second language duplicates the first one's code");
        ledger.HostActivated.Should().HaveCount(2, "both were constructed before the refusal");
        ledger.HostActivated.OfType<Recording>().Should().OnlyContain(one => one.DisposeCount == 0,
            "a refusal disposes nothing itself; the package lifetime owns what it constructed");
    }

    [Test]
    public async Task ThePackageLifetimeDisposesWhatARefusedPreparationConstructedExactlyOnceAsync()
    {
        var ledger = Ledger(registry => registry
            .AddLanguage<FirstLanguage>()
            .AddLanguage<RefusedLanguage>());

        Prepare(ledger).IsAdmitted.Should().BeFalse();

        // The loader builds exactly this lease in its finally block, over exactly this ledger.
        var lease = new PluginRuntimeLease(Context(), ledger, module: null);

        (await lease.DisposeAsync()).Should().BeEmpty();

        ledger.HostActivated.OfType<Recording>().Should().OnlyContain(one => one.DisposeCount == 1);
    }

    [Test]
    public async Task AHostActivatedDisposerFailureRetainsTheContextAsync()
    {
        var ledger = Ledger(registry => registry.AddLanguage<ObjectingLanguage>());

        Prepare(ledger).IsAdmitted.Should().BeTrue();

        var lease = new PluginRuntimeLease(Context(), ledger, module: null);
        var failures = await lease.DisposeAsync();

        using var assertions = new AssertionScope();
        failures.Should().HaveCount(2);
        failures[0].Should().Contain(nameof(ObjectingLanguage));
        failures[1].Should().Contain("load context").And.Contain("retained");
        lease.LoadContext.Should().NotBeNull("an object the host built for this extension may still be live");
    }

    [Test]
    public void RollingBackAPreparedAttemptRunsNoExtensionCode()
    {
        var ledger = Ledger(registry => registry.AddLanguage<FirstLanguage>());
        var result = Prepare(ledger);

        result.IsAdmitted.Should().BeTrue();
        result.Attempt!.Rollback();

        ledger.HostActivated.OfType<Recording>().Should().OnlyContain(
            one => one.DisposeCount == 0,
            "rollback runs under the publication write gate, and an extension's disposer must not run there");
    }

    private PluginAdmissionResult Prepare(PluginRegistrationLedger ledger)
        => _provider!.GetServices<IHostedService>()
            .OfType<PluginBootstrapper>()
            .Single()
            .Admission
            .Prepare(Manifest(), ledger);

    private PluginRegistrationLedger Ledger(Action<IPluginRegistry> configure)
    {
        var ledger = new PluginRegistrationLedger(Package);
        var registry = new PluginRegistry(Package, CapabilitySet.Of(Capability.Language), ledger);

        configure(registry);
        ledger.ActivationContext = new StubPluginContext(Package, registry);
        registry.Seal();
        return ledger;
    }

    private PluginLoadContext Context()
    {
        var entry = Path.Combine(_root, "Ownership.Entry.dll");
        File.WriteAllBytes(entry, []);

        return new PluginLoadContext(
            Package,
            entry,
            nativeLibraryResolver: null,
            Arronix.Plugins.Loading.PackageContractScope.Empty(Package));
    }

    private static ValidatedManifest Manifest()
    {
        var manifest = new PluginManifest
        {
            SchemaVersion = PluginManifestValidator.SupportedSchemaVersion,
            Id = Package.Value,
            Name = "Ownership fixture",
            Version = "0.1.0",
            Contracts = new ContractRequirements { Arronix = ">=0.9 <0.10" },
            EntryAssembly = "Arronix.Host.Tests.dll",
            Capabilities = ["language"],
        };

        PluginManifestValidator.TryValidate(
            new PluginCandidate(Path.Combine(Path.GetTempPath(), "arronix-ownership", "plugin.json"), manifest),
            PackageAvailability.Available,
            out var validated,
            out var defects).Should().BeTrue(string.Join(" | ", defects.Select(defect => defect.Message)));

        return validated!;
    }

    /// <summary>A language that records its own disposal.</summary>
    internal abstract class Recording : ILanguageDefinition, IDisposable
    {
        public int DisposeCount { get; private set; }

        public abstract Language Language { get; }

        public string PrepareComparison(string text) => text;

        public string PrepareQuery(string text) => text;

        public string PrepareFileName(string text) => text;

        public string PrepareSort(string text) => text;

        public virtual void Dispose() => DisposeCount++;
    }

    internal sealed class FirstLanguage : Recording
    {
        public override Language Language => Language.English;
    }

    /// <summary>The same language again, which preparation refuses after constructing it.</summary>
    internal sealed class RefusedLanguage : Recording
    {
        public override Language Language => Language.English;
    }

    /// <summary>A language whose disposer objects.</summary>
    internal sealed class ObjectingLanguage : Recording
    {
        private static readonly Language French = new("fr", "French");

        public override Language Language => French;

        public override void Dispose()
        {
            base.Dispose();
            throw new InvalidOperationException("this language will not be put away");
        }
    }
}
