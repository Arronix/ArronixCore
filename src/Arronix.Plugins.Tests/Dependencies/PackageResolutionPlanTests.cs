using System.IO;
using System.Linq;
using System.Text;
using Arronix.Abstractions.Health;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// The resolved graph the loader acts on: which packages may be admitted, in what order, and why the rest
/// may not.
/// </summary>
/// <remarks>
/// <para>
/// The engine decides eligibility from identifiers and versions; the resolver turns its diagnostics into
/// the failure classes and member paths an operator acts on, over the same installed-package objects the
/// loader will admit. That mapping is where discovery order could get back in, so these fixtures assert the
/// whole rendered graph rather than the parts the engine already pins — the admission order with its
/// declaration paths, every refusal with its failure class, message and member paths, and the order a
/// duplicated identifier's copies are recorded in.
/// </para>
/// <para>
/// Exhaustive rather than sampled, because the claim is about every order the caller could supply.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PackageResolutionPlanTests
{
    /// <summary>Nothing switched off.</summary>
    private static IReadOnlySet<string> Enabled { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Names the identifiers an operator switched off.
    /// </summary>
    /// <param name="ids">The identifiers.</param>
    /// <returns>The configured set.</returns>
    private static IReadOnlySet<string> Disabled(params string[] ids)
        => new HashSet<string>(ids, StringComparer.Ordinal);

    /// <summary>
    /// Every failure class the plan can reach without a cycle, plus a valid pair which must survive them.
    /// </summary>
    private static IReadOnlyList<InstalledPackage> MixedInstallation =>
    [
        Entry("valid.base", "1.0.0", "base"),
        Entry("valid.leaf", "1.0.0", "leaf", "valid.base:>=1.0 <2.0"),
        Entry("gone", "1.0.0", "gone", "absent:>=1.0"),
        Entry("wrong", "1.0.0", "wrong", "valid.base:>=2.0"),
        Entry("copied", "2.0.0", "copied-new"),
        Entry("copied", "1.0.0", "copied-old"),
        Entry("user", "1.0.0", "user", "copied:>=1.0")
    ];

    private static IReadOnlyList<InstalledPackage> CyclicInstallation =>
    [
        Entry("ring.a", "1.0.0", "ring-a", "ring.b:>=1.0"),
        Entry("ring.b", "1.0.0", "ring-b", "ring.a:>=1.0"),
        Entry("rider", "1.0.0", "rider", "ring.a:>=1.0"),
        Entry("island", "1.0.0", "island")
    ];

    [Test]
    public void EveryPermutationOfAMixedInstallationPlansIdentically()
        => AssertInvariantUnderPermutation(MixedInstallation, expectedPermutations: 5040);

    [Test]
    public void EveryPermutationOfACyclicInstallationPlansIdentically()
        => AssertInvariantUnderPermutation(CyclicInstallation, expectedPermutations: 24);

    /// <summary>
    /// The tie the pure graph fixes inside one message, asserted where it becomes a sequence of results.
    /// </summary>
    /// <remarks>
    /// A duplicated identifier is recorded once per copy, so the copy order is an externally observable
    /// sequence — not an implementation detail of a diagnostic. It is the canonical order the message lists
    /// the copies in, which is a property of the copies themselves; the order they were discovered in
    /// reaches neither.
    /// </remarks>
    [Test]
    public void ADuplicatedIdentifiersCopiesAreRecordedInTheOrderItsMessageNamesThem()
    {
        var graph = Resolve(
        [
            Entry("copied", "2.0.0", "aaa-newer"),
            Entry("copied", "1.0.0", "zzz-older")
        ]);

        var refusal = graph.Refused.Should().ContainSingle().Which;

        refusal.Copies
            .Select(copy => copy.Version.ToString())
            .Should().Equal(
                ["1.0.0", "2.0.0"],
                "the copies are listed by version, not by the folder order they were found in");

        refusal.Defects.Should().ContainSingle()
            .Which.Message.Should().Contain("1.0.0 at").And.Contain("2.0.0 at");
    }

    [Test]
    public void ADependencyPrecedesItsDependantAndPeersAreInIdentifierOrder()
    {
        var graph = Resolve(
        [
            Entry("zulu", "1.0.0", "zulu", "core:>=1.0"),
            Entry("core", "1.0.0", "core"),
            Entry("alpha", "1.0.0", "alpha", "core:>=1.0")
        ]);

        graph.Refused.Should().BeEmpty();
        graph.AdmissionOrder.Select(entry => entry.Id.Value).Should().Equal("core", "alpha", "zulu");
    }

    [TestCase("absent:>=1.0", CoreErrorCode.PluginDependencyUnsatisfied, "dependencies[0].package")]
    [TestCase("core:>=2.0", CoreErrorCode.PluginDependencyUnsatisfied, "dependencies[0].range")]
    public void ADeclaredDependencyThatCannotBeMetNamesTheMemberToEdit(
        string requirement,
        CoreErrorCode expected,
        string path)
    {
        var graph = Resolve(
        [
            Entry("core", "1.0.0", "core"),
            Entry("app", "1.0.0", "app", requirement)
        ]);

        graph.AdmissionOrder.Select(entry => entry.Id.Value).Should().Equal("core");

        var refusal = graph.Refused.Should().ContainSingle().Which;
        refusal.ErrorCode.Should().Be(expected);
        refusal.Defects.Should().ContainSingle().Which.Path.Should().Be(path);
    }

    /// <remarks>
    /// The failure class distinguishes the package an operator has to fix from the one they only have to
    /// wait for. <c>app</c> is well-formed; the fault is <c>broken</c>'s, and the message says so.
    /// </remarks>
    [Test]
    public void ADependantOfAnIneligiblePackageIsRefusedAsUnavailableRatherThanUnsatisfied()
    {
        var graph = Resolve(
        [
            Entry("broken", "1.0.0", "broken", "absent:>=1.0"),
            Entry("app", "1.0.0", "app", "broken:>=1.0")
        ]);

        graph.AdmissionOrder.Should().BeEmpty();

        var refusals = graph.Refused.ToDictionary(refusal => refusal.Package.Value);
        refusals["broken"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnsatisfied);
        refusals["app"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        refusals["app"].Defects.Should().ContainSingle().Which.Message.Should().Contain("'broken' is not");
    }

    [Test]
    public void APackageOnACycleIsRefusedWithAWalkThroughItself()
    {
        var graph = Resolve(
        [
            Entry("ring.a", "1.0.0", "ring-a", "ring.b:>=1.0"),
            Entry("ring.b", "1.0.0", "ring-b", "ring.a:>=1.0")
        ]);

        graph.Refused.Should().HaveCount(2);
        graph.Refused.Should().OnlyContain(refusal => refusal.ErrorCode == CoreErrorCode.PluginDependencyCycle);
        graph.Refused[0].Defects.Should().ContainSingle()
            .Which.Message.Should().Contain("ring.a -> ring.b -> ring.a");
    }

    /// <remarks>
    /// Two installed packages claiming one identifier keeps the failure class the loader has always reported
    /// for it, so an operator who has seen it does not have to learn a second name because the check moved
    /// into the plan.
    /// </remarks>
    [Test]
    public void ADuplicatedIdentifierKeepsTheIdentityConflictFailureClass()
    {
        var graph = Resolve(
        [
            Entry("copied", "1.0.0", "old"),
            Entry("copied", "2.0.0", "new")
        ]);

        var refusal = graph.Refused.Should().ContainSingle().Which;
        refusal.ErrorCode.Should().Be(CoreErrorCode.PluginIdConflict);
        refusal.Reason.Should().Contain("More than one installed extension claims the identifier 'copied'");
        refusal.Defects.Should().ContainSingle().Which.Path.Should().Be("id");
    }

    /// <summary>
    /// A package an operator switched off is reported as switched off, not as a dependency defect.
    /// </summary>
    [Test]
    public void ADisabledPackageIsRefusedAsDisabledAndNothingElse()
    {
        var graph = Resolve([Entry("core", "1.0.0", "core")], Disabled("core"));

        graph.AdmissionOrder.Should().BeEmpty();

        var refusal = graph.Refused.Should().ContainSingle().Which;
        refusal.ErrorCode.Should().Be(CoreErrorCode.PluginDisabled);
        refusal.Reason.Should().Be("Extension 'core' is installed but disabled by configuration.");
        refusal.Defects.Should().BeEmpty(
            "the summary already says it, and there is no manifest member an operator could edit to change it");
    }

    /// <summary>
    /// The defect this fixture exists for: a package requiring one an operator switched off must not be
    /// attempted, because its dependency is never going to arrive.
    /// </summary>
    /// <remarks>
    /// And it must not be told the dependency is missing. It is installed, sitting in its own folder, and an
    /// operator sent looking for a package that is already there has been given a worse answer than none.
    /// </remarks>
    [Test]
    public void ADirectDependantOfADisabledPackageIsRefusedNamingIt()
    {
        var graph = Resolve(
        [
            Entry("core", "1.0.0", "core"),
            Entry("app", "1.0.0", "app", "core:>=1.0 <2.0"),
            Entry("unrelated", "1.0.0", "unrelated")
        ],
        Disabled("core"));

        graph.AdmissionOrder.Select(entry => entry.Id.Value).Should().Equal(
            ["unrelated"],
            "a package with nothing to do with the disabled one keeps its place");

        var refusals = graph.Refused.ToDictionary(refusal => refusal.Package.Value);
        refusals["core"].ErrorCode.Should().Be(CoreErrorCode.PluginDisabled);

        refusals["app"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        var defect = refusals["app"].Defects.Should().ContainSingle().Which;
        defect.Path.Should().Be("dependencies[0]");
        defect.Message.Should().Contain("'core' cannot be activated: it is disabled by configuration");
        defect.Message.Should().Contain("Resolve that, or remove the requirement");
        defect.Message.Should().NotContain(
            "no package with that identifier is installed",
            "the package is installed; it is switched off");
    }

    /// <summary>
    /// The whole enabled closure above a disabled package is refused, and the first message names the fault.
    /// </summary>
    [Test]
    public void EveryTransitiveDependantOfADisabledPackageIsRefusedAndToldWhereTheFaultIs()
    {
        var graph = Resolve(
        [
            Entry("core", "1.0.0", "core"),
            Entry("middle", "1.0.0", "middle", "core:>=1.0"),
            Entry("top", "1.0.0", "top", "middle:>=1.0"),
            Entry("island", "1.0.0", "island")
        ],
        Disabled("core"));

        graph.AdmissionOrder.Select(entry => entry.Id.Value).Should().Equal("island");

        var refusals = graph.Refused.ToDictionary(refusal => refusal.Package.Value);
        refusals.Keys.Should().BeEquivalentTo(["core", "middle", "top"]);
        refusals["core"].ErrorCode.Should().Be(CoreErrorCode.PluginDisabled);
        refusals["middle"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        refusals["top"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);

        refusals["top"].Defects.Should().ContainSingle()
            .Which.Message.Should().Contain(
                "The fault is in 'core' (it is disabled by configuration)",
                "a chain of identical 'not eligible' messages names nothing an operator can act on");
    }

    /// <summary>
    /// A disabled package whose own declaration is also broken keeps the disabled state, and says both.
    /// </summary>
    /// <remarks>
    /// Switching a package off does not make its broken dependency stop being broken, so the defect is still
    /// listed; it is simply not the headline, because the operator's own decision is why this package is not
    /// running today. Its dependant is refused for the dependency it cannot have, not for the fault below.
    /// </remarks>
    [Test]
    public void ADisabledPackageWithABrokenClosureKeepsTheDisabledStateAndStillReportsTheFault()
    {
        var graph = Resolve(
        [
            Entry("core", "1.0.0", "core", "absent.package:>=1.0 <2.0"),
            Entry("app", "1.0.0", "app", "core:>=1.0")
        ],
        Disabled("core"));

        graph.AdmissionOrder.Should().BeEmpty();

        var refusals = graph.Refused.ToDictionary(refusal => refusal.Package.Value);

        refusals["core"].ErrorCode.Should().Be(CoreErrorCode.PluginDisabled);
        refusals["core"].Reason.Should().Be("Extension 'core' is installed but disabled by configuration.");
        var fault = refusals["core"].Defects.Should().ContainSingle().Which;
        fault.Path.Should().Be("dependencies[0].package");
        fault.Message.Should().Contain("no package with that identifier is installed");

        refusals["app"].ErrorCode.Should().Be(CoreErrorCode.PluginDependencyUnavailable);
        refusals["app"].Defects.Should().ContainSingle()
            .Which.Message.Should().Contain("'core' cannot be activated: it is disabled by configuration");
    }

    /// <summary>
    /// Two copies of an identifier the operator also switched off keep the identity conflict.
    /// </summary>
    /// <remarks>
    /// An installation holding two copies of one identifier has a problem the operator has to fix whichever
    /// of them they meant to disable, and there is no single candidate to read a disabled package's own
    /// declaration from. The precedence is the one the loader has always had.
    /// </remarks>
    [Test]
    public void ADisabledIdentifierInstalledTwiceIsStillAnIdentityConflict()
    {
        var graph = Resolve(
        [
            Entry("copied", "1.0.0", "old"),
            Entry("copied", "2.0.0", "new")
        ],
        Disabled("copied"));

        var refusal = graph.Refused.Should().ContainSingle().Which;
        refusal.ErrorCode.Should().Be(CoreErrorCode.PluginIdConflict);
        refusal.Copies.Should().HaveCount(2);
    }

    /// <summary>
    /// A disabled identifier nobody installed changes nothing.
    /// </summary>
    [Test]
    public void DisablingAPackageThatIsNotInstalledAffectsNothing()
    {
        var graph = Resolve(
            [Entry("core", "1.0.0", "core")],
            Disabled("never.installed"));

        graph.Refused.Should().BeEmpty();
        graph.AdmissionOrder.Select(entry => entry.Id.Value).Should().Equal("core");
    }

    /// <summary>
    /// An installation carrying a disabled package, its direct and transitive dependants, an unrelated
    /// valid pair and an unrelated duplicate, planned identically under every order the caller could supply.
    /// </summary>
    /// <remarks>
    /// Configuration is an input to the plan, so it belongs under the same exhaustive comparison as the
    /// declarations: the whole rendered plan, including which refusal each package got and the text it got.
    /// </remarks>
    [Test]
    public void EveryPermutationOfAnInstallationWithDisabledPackagesPlansIdentically()
    {
        IReadOnlyList<InstalledPackage> installation =
        [
            Entry("core", "1.0.0", "core"),
            Entry("middle", "1.0.0", "middle", "core:>=1.0"),
            Entry("top", "1.0.0", "top", "middle:>=1.0"),
            Entry("island", "1.0.0", "island"),
            Entry("copied", "1.0.0", "copied-old"),
            Entry("copied", "2.0.0", "copied-new")
        ];

        AssertInvariantUnderPermutation(installation, expectedPermutations: 720, Disabled("core"));
    }

    [Test]
    public void EveryInstalledPackageIsEitherOrderedOrRefused()
    {
        var graph = Resolve(MixedInstallation, Enabled);

        graph.AdmissionOrder.Select(entry => entry.Id.Value)
            .Concat(graph.Refused.Select(refusal => refusal.Package.Value))
            .Order(StringComparer.Ordinal)
            .Should().Equal("copied", "gone", "user", "valid.base", "valid.leaf", "wrong");
    }

    [Test]
    public void AnEmptyInstallationPlansToNothing()
    {
        var graph = Resolve([], Enabled);

        graph.AdmissionOrder.Should().BeEmpty();
        graph.Refused.Should().BeEmpty();
    }

    private static void AssertInvariantUnderPermutation(
        IReadOnlyList<InstalledPackage> installation,
        int expectedPermutations,
        IReadOnlySet<string>? disabled = null)
    {
        var switchedOff = disabled ?? Enabled;
        var baseline = Render(Resolve(installation, switchedOff));
        var seen = 0;

        foreach (var order in PackageGraphFixtures.Permutations(installation))
        {
            Render(Resolve(order, switchedOff)).Should().Be(baseline);
            seen++;
        }

        seen.Should().Be(expectedPermutations);
        baseline.Should().NotBeEmpty();
    }

    /// <summary>
    /// Renders everything the loader can observe about a plan, so a comparison is of the whole answer.
    /// </summary>
    /// <remarks>
    /// The declaration paths are in here deliberately. They are the one thing about a package that is a
    /// property of where it was installed, so a fixture that left them out could not tell a canonical
    /// sequence from one the file-system walk happened to produce.
    /// </remarks>
    private static string Render(ResolvedPackageGraph graph)
    {
        var text = new StringBuilder();

        foreach (var entry in graph.AdmissionOrder)
        {
            text.Append("activate ")
                .Append(entry.Id)
                .Append(' ')
                .Append(entry.Version)
                .Append(' ')
                .Append(entry.Source)
                .Append('\n');
        }

        foreach (var refusal in graph.Refused)
        {
            text.Append("refuse ")
                .Append(refusal.Package)
                .Append(" | ")
                .Append(refusal.ErrorCode)
                .Append(" | ")
                .Append(refusal.Reason)
                .Append('\n');

            foreach (var defect in refusal.Defects)
            {
                text.Append("  defect ").Append(defect).Append('\n');
            }

            foreach (var copy in refusal.Copies)
            {
                text.Append("  copy ").Append(copy.Source).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Builds one installed package, exactly as the loader would hold it after validation.
    /// </summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="folder">The folder name it was installed under.</param>
    /// <param name="dependencies">Its dependencies, each written <c>package:range</c>.</param>
    /// <returns>The entry.</returns>
    /// <remarks>
    /// The declaration goes through the real validator rather than being constructed proved, so a fixture
    /// cannot state a dependency the manifest format would have refused.
    /// </remarks>
    private static InstalledPackage Entry(
        string id,
        string version,
        string folder,
        params string[] dependencies)
    {
        var manifest = new PluginManifest
        {
            SchemaVersion = PluginManifestValidator.SupportedSchemaVersion,
            Id = id,
            Name = id,
            Version = version,
            Contracts = new ContractRequirements { Arronix = ">=0.9 <0.10" },
            EntryAssembly = "Example.dll",
            Capabilities = ["parsing"],
            Dependencies = [.. dependencies.Select(Declaration)]
        };

        var path = Path.Combine(Path.GetTempPath(), "arronix-plan", folder, PluginManifestReader.FileName);

        PluginManifestValidator
            .TryValidate(
                new PluginCandidate(path, manifest),
                PackageAvailability.Available,
                out var validated,
                out var defects)
            .Should().BeTrue(string.Join("; ", defects));

        return validated!.Package;
    }

    /// <summary>
    /// Runs the one production resolver over installed packages.
    /// </summary>
    /// <param name="installed">The installed packages.</param>
    /// <param name="disabled">The identifiers an operator switched off, applied to the snapshots.</param>
    /// <returns>The resolved graph.</returns>
    private static ResolvedPackageGraph Resolve(
        IReadOnlyList<InstalledPackage> installed,
        IReadOnlySet<string>? disabled = null)
        => new PackageDependencyResolver().Resolve(
        [
            .. installed.Select(package => disabled?.Contains(package.Id.Value) == true
                ? SwitchedOff(package)
                : package),
        ]);

    /// <summary>
    /// Rebuilds one installed package with the state an operator's configuration produces.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>The same declaration, switched off.</returns>
    /// <remarks>
    /// Production applies availability once, where the canonical snapshot is created, so nothing downstream
    /// holds two objects for one installed copy. These fixtures start from an already-built package, so they
    /// rebuild it here rather than asking the platform for a mutator it must not have.
    /// </remarks>
    private static InstalledPackage SwitchedOff(InstalledPackage package)
        => new(
            package.Id,
            package.Version,
            package.Source,
            package.Folder,
            package.EntryAssemblyFileName,
            package.ContractAssemblies,
            package.Requirements,
            PackageAvailability.DisabledByConfiguration);

    private static PackageDependencyDeclaration Declaration(string text)
    {
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        return new PackageDependencyDeclaration
        {
            Package = text[..separator],
            Range = text[(separator + 1)..]
        };
    }
}
