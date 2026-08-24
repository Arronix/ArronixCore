using FluentAssertions.Execution;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Versioning;


namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// The order admission runs in, and what the lifecycle refuses to accept as an already-resolved graph.
/// </summary>
/// <remarks>
/// This type resolves nothing. What it owns is the order, and the preconditions a resolver has to have met
/// before the order is meaningful: one entry per identifier, no requirement on something that was not
/// admitted, and no cycle. Those are defects in a resolver rather than in an installation, so they throw
/// here instead of quarantining an extension for someone else's mistake.
/// </remarks>
[TestFixture]
public sealed class ResolvedPackageGraphTests
{
    private static readonly VersionRange Any = VersionRangeParser.Parse(">=1.0");

    /// <remarks>
    /// An installation whose packages require nothing of each other resolves to identifier order. It is what
    /// the one resolver answers rather than a shortcut the graph offers, because a host with no resolution
    /// authority must not be able to assume this shape.
    /// </remarks>
    [Test]
    public void AnEdgelessInstallationIsAdmittedInPackageIdentifierOrder()
    {
        var graph = new PackageDependencyResolver()
            .Resolve([Installed("zulu"), Installed("alpha"), Installed("mike")]);

        Order(graph).Should().Equal("alpha", "mike", "zulu");
        graph.Refused.Should().BeEmpty();
    }

    [Test]
    public void APackageFollowsEverythingItRequires()
    {
        var graph = new ResolvedPackageGraph(
        [
            Package("app", ("core", "1.0.0"), ("ui", "1.0.0")),
            Package("ui", ("core", "1.0.0")),
            Package("core"),
        ]);

        Order(graph).Should().Equal("core", "ui", "app");
    }

    [Test]
    public void TiesAreBrokenByIdentifierRatherThanByHowTheResolverListedThem()
    {
        var graph = new ResolvedPackageGraph(
        [
            Package("app", ("core", "1.0.0")),
            Package("zulu"),
            Package("core"),
        ]);

        // The ordinally smallest valid order, taken one position at a time: 'core' first because 'app' is
        // not yet admissible, then 'app' the moment it becomes admissible, and 'zulu' last. Choosing per
        // position rather than sorting the whole sequence is what makes the answer a property of the graph
        // instead of a property of the traversal.
        Order(graph).Should().Equal("core", "app", "zulu");
    }

    [Test]
    public void TheOrderIsIdenticalUnderEveryPermutationOfTheResolverInput()
    {
        var packages = new[]
        {
            Package("app", ("core", "1.0.0"), ("ui", "1.0.0")),
            Package("ui", ("core", "1.0.0")),
            Package("core"),
            Package("tools", ("core", "1.0.0")),
            Package("solo"),
        };

        var orders = Permutations(packages)
            .Select(permutation => string.Join(",", Order(new ResolvedPackageGraph([.. permutation]))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        orders.Should().ContainSingle(
            "an installation's admission order is a property of what is installed, not of how the disk was walked")
            .Which.Should().Be("core,solo,tools,ui,app");
    }

    [Test]
    public void RefusalsAreReportedInIdentifierOrderAndKeepTheirDiagnosis()
    {
        var graph = new ResolvedPackageGraph(
            [Package("core")],
            [
                new PackageResolutionRefusal(
                    PluginId.FromString("later"),
                    CoreErrorCode.PluginContractMismatch,
                    "later requires core >=2.0, and the installed core is 1.0.0."),
                new PackageResolutionRefusal(
                    PluginId.FromString("earlier"),
                    CoreErrorCode.PluginIdConflict,
                    "earlier is installed twice."),
            ]);

        graph.Refused.Select(refusal => refusal.Package.Value).Should().Equal("earlier", "later");
        graph.TryGetRefusal(PluginId.FromString("later"), out var refusal).Should().BeTrue();
        refusal!.ErrorCode.Should().Be(CoreErrorCode.PluginContractMismatch);
        graph.TryGet(PluginId.FromString("later"), out _).Should().BeFalse();
    }

    [Test]
    public void ACycleAmongAdmittedPackagesIsARefusedGraphRatherThanAnAdmissionOrder()
    {
        var build = () => new ResolvedPackageGraph(
        [
            Package("left", ("right", "1.0.0")),
            Package("right", ("left", "1.0.0")),
        ]);

        build.Should().Throw<ArgumentException>().WithMessage("*cycle*left, right*");
    }

    [Test]
    public void ARequirementOnAPackageTheGraphDoesNotAdmitIsARefusedGraph()
    {
        // A dependant of an unresolvable dependency is refused by the resolver. Admitting it with a
        // dangling edge would leave the lifecycle to discover mid-load that there is nothing to pin.
        var build = () => new ResolvedPackageGraph([Package("app", ("core", "1.0.0"))]);

        build.Should().Throw<ArgumentException>().WithMessage("*requires 'core'*does not admit*");
    }

    [Test]
    public void OneIdentifierAdmittedTwiceIsARefusedGraph()
    {
        var build = () => new ResolvedPackageGraph([Package("core"), Package("core")]);

        build.Should().Throw<ArgumentException>().WithMessage("*more than once*");
    }

    [Test]
    public void APackageAdmittedAndRefusedAtOnceIsARefusedGraph()
    {
        var build = () => new ResolvedPackageGraph(
            [Package("core")],
            [new PackageResolutionRefusal(PluginId.FromString("core"), CoreErrorCode.PluginLoadFailure, "no")]);

        build.Should().Throw<ArgumentException>().WithMessage("*both admissible and refused*");
    }

    [Test]
    public void ADeclarationThatRequiresItselfOrRepeatsADependencyIsRefusedByTheResolverNotTheModel()
    {
        var itself = Package("core", ("core", "1.0.0"));
        var twice = Package("app", ("core", "1.0.0"), ("core", "2.0.0"));

        var resolver = new PackageDependencyResolver();

        using var assertions = new AssertionScope();

        // The canonical model keeps what the author wrote, including a repetition, because a constructor
        // that deduplicated the list would destroy the evidence before the check ran.
        twice.Requirements.Should().HaveCount(2);

        resolver.Resolve([itself]).Refused.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(CoreErrorCode.PluginDependencyCycle);

        resolver.Resolve([twice, Installed("core")]).Refused
            .Should().ContainSingle(refusal => refusal.Package.Value == "app")
            .Which.Defects.Should().ContainSingle()
            .Which.Message.Should().Contain("states a requirement on 'core' 2 times");
    }

    /// <remarks>
    /// Declared, never inferred, and proved a bare file name at the boundary. Repetition is refused by
    /// manifest validation, which can name the member at fault, so the canonical model checks the shape a
    /// caller could otherwise construct directly.
    /// </remarks>
    [Test]
    public void ADeclaredContractAssemblyIsCarriedAsDeclaredAndNeverInferred()
    {
        var package = new InstalledPackage(
            PluginId.FromString("core"),
            SemanticVersion.Parse("1.0.0"),
            "/extensions/core/plugin.json",
            "/extensions/core",
            contractAssemblies: ["Contoso.Core.Contracts.dll"]);

        var blank = () => new InstalledPackage(
            PluginId.FromString("core"),
            SemanticVersion.Parse("1.0.0"),
            "/extensions/core/plugin.json",
            "/extensions/core",
            contractAssemblies: [" "]);

        var escaping = () => new InstalledPackage(
            PluginId.FromString("core"),
            SemanticVersion.Parse("1.0.0"),
            "/extensions/core/plugin.json",
            "/extensions/core",
            contractAssemblies: ["../elsewhere/Contoso.Core.Contracts.dll"]);

        package.ContractAssemblies.Should().Equal("Contoso.Core.Contracts.dll");
        blank.Should().Throw<ArgumentException>();
        escaping.Should().Throw<ArgumentException>();
    }

    /// <remarks>
    /// A caller cannot cast a published collection back to the list behind it and edit what the
    /// installation was proved to be.
    /// </remarks>
    [Test]
    public void AProvedPackagePublishesCollectionsNothingCanCastBackAndEdit()
    {
        var package = new InstalledPackage(
            PluginId.FromString("core"),
            SemanticVersion.Parse("1.0.0"),
            "/extensions/core/plugin.json",
            "/extensions/core",
            contractAssemblies: ["Contoso.Core.Contracts.dll"],
            requirements: [new PackageRequirement(PluginId.FromString("lib"), Any)]);

        (package.ContractAssemblies as ICollection<string>)!.IsReadOnly.Should().BeTrue();
        ((object)package.ContractAssemblies).Should().NotBeAssignableTo<string[]>();
        ((object)package.Requirements).Should().NotBeAssignableTo<PackageRequirement[]>();

        var edit = () => ((IList<string>)package.ContractAssemblies).Add("Sneaked.dll");
        edit.Should().Throw<NotSupportedException>();
    }

    /// <remarks>
    /// An undefined availability value is refused rather than treated as some other kind of unavailable.
    /// </remarks>
    [Test]
    public void AnUndefinedAvailabilityStateIsRefused()
    {
        var construct = () => new InstalledPackage(
            PluginId.FromString("core"),
            SemanticVersion.Parse("1.0.0"),
            "/extensions/core/plugin.json",
            "/extensions/core",
            availability: (PackageAvailability)42);

        construct.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static InstalledPackage Installed(string id, string version = "1.0.0")
        => new(
            PluginId.FromString(id),
            SemanticVersion.Parse(version),
            $"/extensions/{id}/plugin.json",
            $"/extensions/{id}");

    private static InstalledPackage Package(string id, params (string Dependency, string Resolved)[] requirements)
        => new(
            PluginId.FromString(id),
            SemanticVersion.Parse("1.0.0"),
            $"/extensions/{id}/plugin.json",
            $"/extensions/{id}",
            requirements:
            [
                .. requirements.Select(requirement => new PackageRequirement(
                    PluginId.FromString(requirement.Dependency),
                    Any)),
            ]);

    private static IReadOnlyList<string> Order(ResolvedPackageGraph graph)
        => [.. graph.AdmissionOrder.Select(package => package.Id.Value)];

    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var head = items[index];
            var rest = items.Where((_, position) => position != index).ToArray();

            foreach (var tail in Permutations(rest))
            {
                yield return [head, .. tail];
            }
        }
    }
}
