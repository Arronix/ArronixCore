using System.IO;
using System.Linq;
using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Telemetry;
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
/// When an extension's redaction rules start applying, and what happens to them if its attempt does not
/// finish publishing.
/// </summary>
/// <remarks>
/// The loader commits a Host attempt and can still fail afterwards — a dependency edge withdrawn between
/// preparation and publication is the case that exists. Everything the attempt applied has to be reversible
/// until the loader says the package is published, or a failed installation leaves the platform running
/// half of it.
/// </remarks>
[TestFixture]
internal sealed class RedactionAdmissionTests
{
    private static readonly PluginId Package = PluginId.FromString("redaction.fixture");

    private string _root = string.Empty;
    private ServiceProvider? _provider;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-redaction-admission", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Plugins:RootFolder"] = Path.Combine(_root, "extensions"),
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
    public void RulesAreNotApplyingUntilTheAttemptCommits()
    {
        var engine = _provider!.GetRequiredService<RedactionEngine>();
        var before = engine.RuleIds;

        Prepare().IsAdmitted.Should().BeTrue();

        using var assertions = new AssertionScope();
        engine.RuleIds.Should().Equal(before, "preparing compiles and reserves; it does not apply");
        engine.Redact("fixture=abc").Should().Be("fixture=abc");
    }

    [Test]
    public void AnAttemptThatPublishesAppliesItsRules()
    {
        var engine = _provider!.GetRequiredService<RedactionEngine>();
        var attempt = Prepare().Attempt!;

        attempt.TryCommit(out _, out _).Should().BeTrue();
        attempt.Confirm();

        engine.Redact("fixture=abc").Should().Be("fixture=(redacted)");
    }

    [Test]
    public void AnAttemptThatFailsAfterCommittingLeavesTheRuleSetExactlyAsItFoundIt()
    {
        var engine = _provider!.GetRequiredService<RedactionEngine>();
        var before = engine.RuleIds;
        var sample = engine.Redact("fixture=abc value=1");

        var attempt = Prepare().Attempt!;
        attempt.TryCommit(out _, out _).Should().BeTrue();

        // Exactly what the loader does when the publication step after Host admission fails.
        attempt.Rollback();

        using var assertions = new AssertionScope();
        engine.RuleIds.Should().Equal(before, "a package that did not publish contributes nothing");
        engine.Redact("fixture=abc value=1").Should().Be(sample, "and the text it produces is unchanged");
    }

    [Test]
    public void ThatSamePackageMayTryAgainWithTheSameRuleIdentifiers()
    {
        var engine = _provider!.GetRequiredService<RedactionEngine>();

        var first = Prepare().Attempt!;
        first.TryCommit(out _, out _).Should().BeTrue();
        first.Rollback();

        var second = Prepare();

        using var assertions = new AssertionScope();
        second.IsAdmitted.Should().BeTrue("the identifiers the first attempt reserved were given back");
        second.Attempt!.TryCommit(out _, out _).Should().BeTrue();
        second.Attempt.Confirm();
        engine.Redact("fixture=abc").Should().Be("fixture=(redacted)");
    }

    [Test]
    public void ARuleThatWillNotCompileRefusesTheExtensionRatherThanBeingSkipped()
    {
        var engine = _provider!.GetRequiredService<RedactionEngine>();
        var before = engine.RuleIds;

        var result = Prepare(new RedactionRule("broken", "(?<secret>[unclosed"));

        using var assertions = new AssertionScope();
        result.IsAdmitted.Should().BeFalse();
        result.Defects.Should().ContainSingle().Which.Should().Contain("does not compile");
        engine.RuleIds.Should().Equal(before);
    }

    private PluginAdmissionResult Prepare(RedactionRule? rule = null)
    {
        var ledger = new PluginRegistrationLedger(Package);
        var registry = new PluginRegistry(Package, CapabilitySet.Of(Capability.Storage), ledger);

        registry.AddRedactionRules(new FixtureRules(rule ?? new RedactionRule("fixture", "fixture=(?<secret>[a-z]+)")));
        ledger.ActivationContext = new StubPluginContext(Package, registry);
        registry.Seal();

        return _provider!.GetServices<IHostedService>()
            .OfType<PluginBootstrapper>()
            .Single()
            .Admission
            .Prepare(Manifest(), ledger);
    }

    private static ValidatedManifest Manifest()
    {
        var manifest = new PluginManifest
        {
            SchemaVersion = PluginManifestValidator.SupportedSchemaVersion,
            Id = Package.Value,
            Name = "Redaction fixture",
            Version = "0.1.0",
            Contracts = new ContractRequirements { Arronix = ">=0.9 <0.10" },
            EntryAssembly = "Arronix.Host.Tests.dll",
            Capabilities = ["storage"],
        };

        PluginManifestValidator.TryValidate(
            new PluginCandidate(Path.Combine(Path.GetTempPath(), "arronix-redaction", "plugin.json"), manifest),
            PackageAvailability.Available,
            out var validated,
            out var defects).Should().BeTrue(string.Join(" | ", defects.Select(defect => defect.Message)));

        return validated!;
    }

    private sealed class FixtureRules(RedactionRule rule) : IRedactionRuleProvider
    {
        public IReadOnlyList<RedactionRule> Rules => [rule];
    }
}
