using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Plugins;
using Arronix.Architecture.Tests.Repository;
using Arronix.Architecture.Tests.Support;
using Arronix.Plugins.Manifest;
using Arronix.Host.Media.Typed;
using Arronix.Plugins.Registration;

#pragma warning disable ARX0013 // Media-shape contracts are experimental; one is the negative control below.
#pragma warning disable ARX0014 // The extension model is experimental; this fixture is its enforcement test.

namespace Arronix.Architecture.Tests.Capabilities;

/// <summary>
/// Rule 10 - the capability declaration and the registrations must agree, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Least privilege is only real if it is checked both ways round. The reverse direction - an extension
/// may not register a gated contract it did not declare - is what stops a manifest being a work of
/// fiction. The forward direction - an extension may not declare a privilege it never exercises - is what
/// stops a manifest asking for everything on the off chance. Either one alone leaves an obvious hole.
/// </para>
/// <para>
/// Both are asserted here against the four real extensions, driving the real admission gate and the real
/// registration ledger. That matters: a fixture written alongside a rule agrees with it by construction,
/// while four declarations written by other people against the same rule do not. The reverse direction is
/// asserted twice over - once by the fact that configuring the extension does not throw, because the
/// registry refuses an undeclared registration at the call site, and once explicitly against the
/// capability table afterwards.
/// </para>
/// </remarks>
[TestFixture]
public class CapabilityDeclarationTests
{
    private static readonly ConcurrentDictionary<string, ConfiguredExtension> Configured = new(StringComparer.Ordinal);

    /// <summary>Gets the media extension projects, for the parameterized cases below.</summary>
    public static IEnumerable<string> MediaExtensions => RepositoryLayout.MediaExtensionProjects;

    [Test]
    public void TheCapabilityVocabularyIsWrittenDownExactlyOnce()
    {
        // Two spellings of one vocabulary, and this is the assertion that keeps them one vocabulary. A
        // capability with no wire name cannot be declared; a wire name with no capability cannot be
        // enforced. Either would be a privilege that silently does nothing.
        var declared = Enum.GetValues<Capability>();

        Assert.Multiple(() =>
        {
            foreach (var capability in declared)
            {
                var wireName = CapabilityNames.ToWireName(capability);

                Assert.That(
                    CapabilityNames.TryParse(wireName, out var round) && round == capability,
                    Is.True,
                    $"'{capability}' does not survive a round trip through its wire name '{wireName}'.");
            }
        });
    }

    [Test]
    public void TheForwardCheckAppliesToEveryCapabilityARegistrationCouldAccountFor()
    {
        // The forward check cannot cover a privilege that no registration could ever satisfy: making
        // outbound calls and reading files are privileges to USE something rather than to contribute
        // anything. That exemption is derived from the capability table rather than hard-coded, so a
        // privilege that later grows a registration form starts being checked with no other change. This
        // asserts the derived set, so the exemption stays a consequence rather than a habit.
        var exempt = Enum
            .GetValues<Capability>()
            .Where(static capability => !CapabilityMatrix.ForwardCheckableCapabilities.Contains(capability))
            .Order()
            .ToArray();

        Assert.That(
            exempt,
            Is.EqualTo(new[] { Capability.Network, Capability.Storage }),
            "The set of privileges exempt from the forward check has changed. Every addition to it is a "
            + "privilege an extension may declare and never use.");
    }

    [Test]
    public void TheAdmissionGateRefusesAContributionNoCapabilityCovers()
    {
        // The negative control for every case below. The reverse half of the check is asserted by the fact
        // that configuring a real extension does not throw - which proves nothing unless the gate can
        // throw. Same registry, same real contribution, empty grant.
        var plugin = PluginId.FromString("architecture.tests");
        var ledger = new PluginRegistrationLedger(plugin);
        var registry = new PluginRegistry(plugin, CapabilitySet.None, ledger);

        // Any shape provider is a negative control; this one belongs to a kind that has not converted to
        // the typed surface yet. Movies used to be the subject and no longer publishes a shape provider at
        // all: its structure is derived from its item type by the host.
        var refusal = Assert.Throws<PluginCapabilityException>(
            () => registry.AddMediaShape(new Arronix.Plugin.Books.BooksShape()));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Required, Is.EqualTo(Capability.MediaKind));
            Assert.That(refusal.ContractName, Is.EqualTo(nameof(Arronix.Abstractions.Shape.IMediaShapeProvider)));
            Assert.That(ledger.Count, Is.Zero, "A refused contribution must not be recorded.");
        });
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void EveryDeclaredCapabilityNameIsInTheClosedVocabulary(string projectName)
    {
        var manifest = ReadManifest(projectName);

        var unknown = manifest
            .Capabilities
            .Where(static name => !CapabilityNames.TryParse(name, out _))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            unknown,
            Is.Empty,
            $"'{projectName}' declares a privilege the platform has no enforcement for. A misspelled "
            + "privilege must be a load failure, never a silently unenforceable one.");
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void ConfiguringTheExtensionRegistersSomething(string projectName)
    {
        var extension = Configure(projectName);

        Assert.Multiple(() =>
        {
            Assert.That(
                extension.Ledger.Count,
                Is.GreaterThan(0),
                $"'{projectName}' registered nothing, so both halves of the capability check below would "
                + "pass while asserting nothing.");

            Assert.That(
                extension.Module.Id.ToString(),
                Is.EqualTo(extension.Manifest.Id),
                "The entry module's identifier and the manifest's must be the same extension.");
        });
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void EveryDeclaredCapabilityIsAccountedForByARegistration(string projectName)
    {
        var extension = Configure(projectName);

        var satisfied = extension.Ledger.TryVerifyDeclaredCapabilities(extension.Declared, out var unsatisfied);

        Assert.That(
            satisfied,
            Is.True,
            $"'{projectName}' declares "
            + string.Join(", ", unsatisfied.Select(CapabilityNames.ToWireName))
            + " and registers nothing that needs it. A privilege granted for nothing is either a mistake "
            + "in the declaration or a privilege taken on the off chance; both are worth refusing.");
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void EveryRegistrationIsCoveredByADeclaredCapability(string projectName)
    {
        var extension = Configure(projectName);

        var uncovered = extension
            .Ledger
            .Entries
            .Where(entry => !CapabilityMatrix.IsPermitted(extension.Granted, entry.Contract))
            .Select(static entry => entry.Contract.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            uncovered,
            Is.Empty,
            $"'{projectName}' contributed something its manifest does not entitle it to contribute.");
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void NoCapabilityIsHeldOnlyBecauseAnotherImpliedIt(string projectName)
    {
        var extension = Configure(projectName);

        // Implication exists so that an extension which indexes need not also spell out that indexing
        // makes outbound calls. It is not a way to acquire a contributing privilege without declaring it,
        // so anything a registration relies on must be in the manifest verbatim.
        var relied = extension
            .Ledger
            .Entries
            .Where(entry => CapabilityMatrix.RegistrationRequirements.ContainsKey(entry.Contract))
            .SelectMany(entry => CapabilityMatrix.RegistrationRequirements[entry.Contract])
            .Distinct()
            .ToArray();

        var impliedOnly = relied
            .Where(capability => !extension.Declared.Has(capability))
            .Where(capability => extension.Granted.Has(capability))
            .Select(CapabilityNames.ToWireName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(impliedOnly, Is.Empty);
    }

    private static ConfiguredExtension Configure(string projectName) =>
        Configured.GetOrAdd(projectName, static name => Build(name));

    private static ConfiguredExtension Build(string projectName)
    {
        var manifest = ReadManifest(projectName);
        var pluginId = PluginId.FromString(manifest.Id);

        var declared = CapabilitySet.Of(
            manifest.Capabilities
                .Select(static name => CapabilityNames.TryParse(name, out var capability)
                    ? capability
                    : throw new InvalidOperationException($"'{name}' is not a known capability."))
                .ToArray());

        var granted = declared.WithImplied();

        var ledger = new PluginRegistrationLedger(pluginId);
        // The real host reader, not a stand-in. A typed media kind's capability demands are only legible
        // once its configuration call has been replayed and its model derived, so a fixture that priced it
        // any other way would be checking a manifest against a guess.
        var registry = new PluginRegistry(pluginId, granted, ledger, new MediaTypeCapabilityReader());
        var telemetry = new RecordingTelemetryEmitter();
        var context = new ConfigureOnlyPluginContext(pluginId, manifest.Version, granted, registry, telemetry);

        var module = EntryModule(projectName);

        // No try/catch. The registry refuses an undeclared gated registration by throwing at the call
        // site inside the extension's own configure method, and that refusal IS the reverse half of the
        // check. Swallowing it here would turn the strongest assertion in this fixture into a comment.
        module.Configure(context);
        registry.Seal();

        return new ConfiguredExtension(projectName, manifest, declared, granted, ledger, module);
    }

    private static IPluginModule EntryModule(string projectName)
    {
        var assembly = Assembly.Load(new AssemblyName(projectName));

        var modules = assembly
            .GetExportedTypes()
            .Where(static type => typeof(IPluginModule).IsAssignableFrom(type))
            .Where(static type => type is { IsAbstract: false, IsInterface: false })
            .Order(TypeNameComparer.Instance)
            .ToArray();

        Assert.That(
            modules,
            Has.Length.EqualTo(1),
            $"'{projectName}' must expose exactly one entry module. Zero is a load failure and so is more "
            + "than one: ambiguity about which module owns an assembly is a defect, not a feature.");

        return (IPluginModule)Activator.CreateInstance(modules[0])!;
    }

    private static PluginManifest ReadManifest(string projectName)
    {
        // Read from the working tree rather than from the test output. Every extension ships its manifest
        // under the same file name, so in a shared output folder only one of the four would survive - and
        // whichever it was would be asserted about four times.
        var path = Path.Combine(RepositoryLayout.ProjectDirectory(projectName), PluginManifestReader.FileName);

        Assert.That(File.Exists(path), Is.True, $"'{projectName}' ships no {PluginManifestReader.FileName}.");

        return PluginManifestReader.ReadFile(path);
    }

    private sealed record ConfiguredExtension(
        string ProjectName,
        PluginManifest Manifest,
        CapabilitySet Declared,
        CapabilitySet Granted,
        PluginRegistrationLedger Ledger,
        IPluginModule Module);

    private sealed class TypeNameComparer : IComparer<Type>
    {
        public static TypeNameComparer Instance { get; } = new();

        public int Compare(Type? x, Type? y) =>
            string.CompareOrdinal(x?.FullName ?? string.Empty, y?.FullName ?? string.Empty);
    }
}
