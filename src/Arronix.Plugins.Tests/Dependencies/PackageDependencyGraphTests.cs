using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Versioning;
using static Arronix.Plugins.Tests.Dependencies.PackageGraphFixtures;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// What the dependency graph refuses, and what it says when it refuses it.
/// </summary>
/// <remarks>
/// Each fixture names the semantic it pins, so the rule can be read here rather than inferred from the
/// implementation that satisfies it.
/// </remarks>
[TestFixture]
public sealed class PackageDependencyGraphTests
{
    [Test]
    public void AnInstallationWithNothingWrongActivatesEverything()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("app", "1.0.0", "lib:>=1.0 <2.0"),
            Package("lib", "1.4.2")
        ]);

        resolution.Diagnostics.Should().BeEmpty();
        resolution.Ineligible().Should().BeEmpty();
        resolution.Activated().Should().Equal("lib", "app");
    }

    [Test]
    public void AMissingDependencyNamesThePackageTheRangeAndWhatToDo()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("app", "1.0.0", "lib:>=1.0 <2.0")
        ]);

        resolution.Activated().Should().BeEmpty();
        resolution.Ineligible().Should().Equal("app");

        var diagnostic = resolution.Of(PackageDependencyDiagnosticKind.MissingDependency).Single();
        diagnostic.Package.Value.Should().Be("app");
        diagnostic.Dependency?.Value.Should().Be("lib");
        diagnostic.Message.Should().Contain("'app'").And.Contain("'lib'").And.Contain(">=1.0 <2.0");
        diagnostic.Message.Should().Contain("no package with that identifier is installed");
    }

    [Test]
    public void AnIncompatibleVersionIsDistinguishedFromAnAbsentPackageAndReportsBothVersions()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("app", "1.0.0", "lib:>=1.0 <2.0"),
            Package("lib", "2.0.0")
        ]);

        resolution.Activated().Should().Equal("lib");
        resolution.Ineligible().Should().Equal("app");

        var diagnostic = resolution.Of(PackageDependencyDiagnosticKind.IncompatibleDependency).Single();
        diagnostic.Package.Value.Should().Be("app");
        diagnostic.Dependency?.Value.Should().Be("lib");
        diagnostic.Message.Should().Contain(">=1.0 <2.0").And.Contain("2.0.0");
        resolution.Of(PackageDependencyDiagnosticKind.MissingDependency).Should().BeEmpty();
    }

    /// <summary>
    /// The rule this fixture exists for: the range a package satisfies is whatever the one
    /// <see cref="VersionRange"/> already says it satisfies.
    /// </summary>
    [TestCase("1.0.0", ">=1.0 <2.0", true)]
    [TestCase("1.9.9", ">=1.0 <2.0", true)]
    [TestCase("2.0.0", ">=1.0 <2.0", false)]
    [TestCase("0.9.0", ">=1.0 <2.0", false)]
    [TestCase("0.3.5", ">=0.3 <0.4 || >=0.5 <0.6", true)]
    [TestCase("0.4.5", ">=0.3 <0.4 || >=0.5 <0.6", false)]
    [TestCase("1.0.0", "=1.0.0", true)]
    public void CompatibilityIsWhateverTheOneVersionRangeSaysItIs(string installed, string range, bool expected)
    {
        var version = SemanticVersion.Parse(installed);
        var parsed = VersionRangeParser.Parse(range);

        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("app", "1.0.0", $"lib:{range}"),
            Package("lib", installed)
        ]);

        parsed.IsSatisfiedBy(version).Should().Be(expected);
        resolution.Activated().Contains("app").Should().Be(expected);
    }

    [Test]
    public void AnIdentifierInstalledTwiceIsRefusedRatherThanChosenBetween()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            PackageFrom("lib", "1.0.0", "/extensions/lib-old"),
            PackageFrom("lib", "2.0.0", "/extensions/lib-new")
        ]);

        resolution.Activated().Should().BeEmpty();
        resolution.Ineligible().Should().Equal("lib");

        var diagnostic = resolution.Of(PackageDependencyDiagnosticKind.DuplicatePackage).Single();
        diagnostic.Package.Value.Should().Be("lib");
        diagnostic.Dependency.Should().BeNull();
        diagnostic.Message.Should().Contain("installed 2 times");
        diagnostic.Message.Should().Contain("1.0.0 at /extensions/lib-old");
        diagnostic.Message.Should().Contain("2.0.0 at /extensions/lib-new");
        diagnostic.Message.Should().Contain("never chooses between them");
    }

    [Test]
    public void TwoCopiesOfTheSameVersionAreStillTwoCopies()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("lib", "1.0.0"),
            Package("lib", "1.0.0")
        ]);

        resolution.Of(PackageDependencyDiagnosticKind.DuplicatePackage).Should().ContainSingle();
        resolution.Ineligible().Should().Equal("lib");
    }

    /// <summary>
    /// A dependant of a duplicated package is not told its dependency is missing, and is not quietly bound
    /// to the higher of the two copies.
    /// </summary>
    [Test]
    public void ADependantOfADuplicatedPackageIsRefusedRatherThanBoundToACopy()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("app", "1.0.0", "lib:>=1.0 <2.0"),
            Package("lib", "1.0.0"),
            Package("lib", "9.0.0")
        ]);

        resolution.Activated().Should().BeEmpty();
        resolution.Ineligible().Should().Equal("app", "lib");

        resolution.Of(PackageDependencyDiagnosticKind.MissingDependency).Should().BeEmpty();
        resolution.Of(PackageDependencyDiagnosticKind.IncompatibleDependency).Should().BeEmpty();

        var refused = resolution.Of(PackageDependencyDiagnosticKind.IneligibleDependency).Single();
        refused.Package.Value.Should().Be("app");
        refused.Dependency?.Value.Should().Be("lib");
    }

    [Test]
    public void OneDependencyStatedTwiceIsRefusedRatherThanIntersected()
    {
        var candidate = new InstalledPackage(
            PluginId.FromString("app"),
            SemanticVersion.Parse("1.0.0"),
            "/packages/app/plugin.json",
            "/packages/app",
            requirements: [Requirement("lib:>=1.0 <2.0"), Requirement("lib:>=3.0 <4.0")]);

        var resolution = PackageGraphFixtures.Resolve([candidate, Package("lib", "1.5.0")]);

        resolution.Activated().Should().Equal("lib");
        resolution.Ineligible().Should().Equal("app");

        var diagnostic = resolution.Of(PackageDependencyDiagnosticKind.DuplicateRequirement).Single();
        diagnostic.Package.Value.Should().Be("app");
        diagnostic.Dependency?.Value.Should().Be("lib");
        diagnostic.Message.Should().Contain(">=1.0 <2.0").And.Contain(">=3.0 <4.0");
        diagnostic.Message.Should().Contain("never chooses between two declared ranges");
    }

    [Test]
    public void APackageThatRequiresItselfIsACycleOfOne()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("solo", "1.0.0", "solo:>=1.0")
        ]);

        resolution.Activated().Should().BeEmpty();
        resolution.Ineligible().Should().Equal("solo");

        var diagnostic = resolution.Of(PackageDependencyDiagnosticKind.DependencyCycle).Single();
        diagnostic.Package.Value.Should().Be("solo");
        diagnostic.Path().Should().Be("solo -> solo");
        diagnostic.Message.Should().Contain("solo -> solo");
    }

    /// <summary>
    /// A package that requires itself at a version it is not is an incompatible requirement, not a cycle.
    /// The edge never resolves, so there is no edge to go round.
    /// </summary>
    [Test]
    public void APackageThatRequiresAVersionOfItselfItIsNotIsIncompatibleRatherThanCyclic()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("solo", "1.0.0", "solo:>=2.0")
        ]);

        resolution.Of(PackageDependencyDiagnosticKind.DependencyCycle).Should().BeEmpty();
        resolution.Of(PackageDependencyDiagnosticKind.IncompatibleDependency).Should().ContainSingle();
        resolution.Ineligible().Should().Equal("solo");
    }

    [Test]
    public void EveryPackageOnACycleIsToldAboutACycleThroughItself()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("ring.a", "1.0.0", "ring.b:>=1.0"),
            Package("ring.b", "1.0.0", "ring.c:>=1.0"),
            Package("ring.c", "1.0.0", "ring.a:>=1.0")
        ]);

        resolution.Activated().Should().BeEmpty();
        resolution.Ineligible().Should().Equal("ring.a", "ring.b", "ring.c");

        var cycles = resolution.Of(PackageDependencyDiagnosticKind.DependencyCycle);
        cycles.Select(static diagnostic => diagnostic.Package.Value)
            .Should().Equal("ring.a", "ring.b", "ring.c");
        cycles.Select(static diagnostic => diagnostic.Path())
            .Should().Equal(
                "ring.a -> ring.b -> ring.c -> ring.a",
                "ring.b -> ring.c -> ring.a -> ring.b",
                "ring.c -> ring.a -> ring.b -> ring.c");
    }

    /// <summary>
    /// The path is a walk over real edges, so it can be followed back into the graph.
    /// </summary>
    [Test]
    public void ACyclePathIsAWalkTheGraphActuallyContains()
    {
        var packages = new[]
        {
            Package("ring.a", "1.0.0", "ring.b:>=1.0"),
            Package("ring.b", "1.0.0", "ring.c:>=1.0"),
            Package("ring.c", "1.0.0", "ring.a:>=1.0")
        };

        var edges = packages
            .SelectMany(package => package.Requirements.Select(requirement =>
                $"{package.Id}->{requirement.PackageId}"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var diagnostic in PackageGraphFixtures
            .Resolve(packages)
            .Of(PackageDependencyDiagnosticKind.DependencyCycle))
        {
            var path = diagnostic.CyclePath;
            path.Should().HaveCountGreaterThan(1);
            path[0].Should().Be(diagnostic.Package);
            path[^1].Should().Be(diagnostic.Package);

            for (var step = 0; step < path.Count - 1; step++)
            {
                edges.Should().Contain($"{path[step]}->{path[step + 1]}");
            }
        }
    }

    /// <summary>
    /// A larger cyclic component holds cycles of different lengths through different packages; each package
    /// gets the shortest one through itself.
    /// </summary>
    [Test]
    public void EachPackageGetsTheShortestCycleThroughItself()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("a", "1.0.0", "b:>=1.0"),
            Package("b", "1.0.0", "a:>=1.0", "c:>=1.0"),
            Package("c", "1.0.0", "d:>=1.0"),
            Package("d", "1.0.0", "b:>=1.0")
        ]);

        var paths = resolution
            .Of(PackageDependencyDiagnosticKind.DependencyCycle)
            .ToDictionary(static diagnostic => diagnostic.Package.Value, static diagnostic => diagnostic.Path());

        paths.Should().HaveCount(4);
        paths["a"].Should().Be("a -> b -> a");
        paths["b"].Should().Be("b -> a -> b");
        paths["c"].Should().Be("c -> d -> b -> c");
        paths["d"].Should().Be("d -> b -> c -> d");
    }

    /// <summary>
    /// A package the caller declared unable to start is refused, and so is everything requiring it.
    /// </summary>
    /// <remarks>
    /// Not filtered out of the input, which is the distinction this fixture holds. An installed package that
    /// will never start still occupies its identifier, so a dependant of it must be told it cannot be
    /// activated rather than that nothing with that identifier was installed.
    /// </remarks>
    [Test]
    public void APackageDeclaredUnavailableIsRefusedAndTakesItsDependantsWithIt()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            DisabledPackage("core", "1.0.0"),
            Package("app", "1.0.0", "core:>=1.0 <2.0"),
            Package("island", "1.0.0")
        ]);

        resolution.Activated().Should().Equal("island");
        resolution.Ineligible().Should().Equal("app", "core");

        resolution.Of(PackageDependencyDiagnosticKind.MissingDependency).Should().BeEmpty(
            "the package is installed; it just cannot start");

        var unavailable = resolution.Of(PackageDependencyDiagnosticKind.UnavailablePackage).Single();
        unavailable.Package.Value.Should().Be("core");
        unavailable.Message.Should().Be(
            "Package 'core' is installed but cannot be activated: it is disabled by configuration.");

        var dependant = resolution.Of(PackageDependencyDiagnosticKind.IneligibleDependency).Single();
        dependant.Package.Value.Should().Be("app");
        dependant.Dependency?.Value.Should().Be("core");
        dependant.Message.Should().Contain("'core' cannot be activated: it is disabled by configuration");
        dependant.Message.Should().Contain("Resolve that, or remove the requirement");
    }

    /// <summary>
    /// A range is still checked against an unavailable package, because it is still installed.
    /// </summary>
    /// <remarks>
    /// The version it is at is a fact whether or not it can start, and an operator who re-enables it should
    /// not then discover a second, different reason it does not work.
    /// </remarks>
    [Test]
    public void AnIncompatibleRequirementOnAnUnavailablePackageIsStillReportedAsIncompatible()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            DisabledPackage("core", "1.0.0"),
            Package("app", "1.0.0", "core:>=2.0")
        ]);

        resolution.Of(PackageDependencyDiagnosticKind.IncompatibleDependency).Should().ContainSingle()
            .Which.Package.Value.Should().Be("app");
    }

    /// <summary>
    /// A transitive dependant is told which package the closure is actually broken by.
    /// </summary>
    /// <remarks>
    /// Each hop pointing only at the next one gives an operator reading a chain a row of identical messages
    /// and no fault to act on. The root is named, with its reason when it has one.
    /// </remarks>
    [Test]
    public void ATransitiveDependantIsToldWhereTheFaultInItsClosureIs()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            DisabledPackage("core", "1.0.0"),
            Package("middle", "1.0.0", "core:>=1.0"),
            Package("top", "1.0.0", "middle:>=1.0")
        ]);

        var top = resolution
            .Of(PackageDependencyDiagnosticKind.IneligibleDependency)
            .Single(diagnostic => diagnostic.Package.Value == "top");

        top.Message.Should().Contain("resolve the diagnostics reported against 'middle'");
        top.Message.Should().EndWith("The fault is in 'core' (it is disabled by configuration).");
    }

    /// <summary>
    /// Several faults in one closure are named in identifier order, not in the order they were found.
    /// </summary>
    [Test]
    public void SeveralFaultsInOneClosureAreNamedInIdentifierOrder()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("zeta", "1.0.0", "absent:>=1.0"),
            DisabledPackage("alpha", "1.0.0"),
            Package("middle", "1.0.0", "zeta:>=1.0", "alpha:>=1.0"),
            Package("top", "1.0.0", "middle:>=1.0")
        ]);

        resolution
            .Of(PackageDependencyDiagnosticKind.IneligibleDependency)
            .Single(diagnostic => diagnostic.Package.Value == "top")
            .Message
            .Should().EndWith("The fault is in 'alpha' (it is disabled by configuration), 'zeta'.");
    }

    /// <summary>
    /// Saying nothing about a package says it is an ordinary one.
    /// </summary>
    /// <remarks>
    /// Availability is a closed state rather than a reason a caller writes, so "no reason given" is not a
    /// representable third thing: a candidate is available unless a caller names the one state that says
    /// otherwise, and there is no spelling of "unavailable for no reason" to resolve.
    /// </remarks>
    [Test]
    public void ACandidateIsAvailableUnlessItsStateSaysOtherwise()
    {
        var candidate = Package("core", "1.0.0");

        candidate.Availability.Should().Be(PackageAvailability.Available);

        var resolution = PackageGraphFixtures.Resolve([candidate, Package("app", "1.0.0", "core:>=1.0")]);

        resolution.Activated().Should().Equal("core", "app");
        resolution.Diagnostics.Should().BeEmpty();
    }

    [Test]
    public void AnInvalidPackageMakesEveryTransitiveDependantIneligible()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("chain.top", "1.0.0", "chain.middle:>=1.0"),
            Package("chain.middle", "1.0.0", "chain.broken:>=1.0"),
            Package("chain.broken", "1.0.0", "absent.package:>=1.0"),
            Package("unrelated", "1.0.0")
        ]);

        resolution.Activated().Should().Equal("unrelated");
        resolution.Ineligible().Should().Equal("chain.broken", "chain.middle", "chain.top");

        resolution.Of(PackageDependencyDiagnosticKind.MissingDependency)
            .Select(static diagnostic => diagnostic.Package.Value)
            .Should().Equal("chain.broken");

        resolution.Of(PackageDependencyDiagnosticKind.IneligibleDependency)
            .Select(static diagnostic => (diagnostic.Package.Value, diagnostic.Dependency?.Value))
            .Should().Equal(("chain.middle", "chain.broken"), ("chain.top", "chain.middle"));
    }

    [Test]
    public void AnUnrelatedValidComponentSurvivesEveryFailureClassAtOnce()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("valid.base", "1.0.0"),
            Package("valid.leaf", "1.0.0", "valid.base:>=1.0 <2.0"),
            Package("gone", "1.0.0", "absent:>=1.0"),
            Package("wrong", "1.0.0", "valid.base:>=2.0"),
            Package("ring.a", "1.0.0", "ring.b:>=1.0"),
            Package("ring.b", "1.0.0", "ring.a:>=1.0"),
            Package("copied", "1.0.0"),
            Package("copied", "2.0.0")
        ]);

        resolution.Activated().Should().Equal("valid.base", "valid.leaf");
        resolution.Ineligible().Should().Equal("copied", "gone", "ring.a", "ring.b", "wrong");
    }

    /// <summary>
    /// Every refusal is explained in its own name, which is what makes "why will this not start?" a
    /// question with an answer.
    /// </summary>
    [Test]
    public void EveryIneligiblePackageCarriesADiagnosticOfItsOwn()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("valid", "1.0.0"),
            Package("gone", "1.0.0", "absent:>=1.0"),
            Package("wrong", "1.0.0", "valid:>=2.0"),
            Package("rider", "1.0.0", "gone:>=1.0"),
            Package("ring.a", "1.0.0", "ring.b:>=1.0"),
            Package("ring.b", "1.0.0", "ring.a:>=1.0"),
            Package("copied", "1.0.0"),
            Package("copied", "2.0.0")
        ]);

        var explained = resolution.Diagnostics
            .Select(static diagnostic => diagnostic.Package.Value)
            .ToHashSet(StringComparer.Ordinal);

        resolution.Ineligible().Should().OnlyContain(id => explained.Contains(id));
        resolution.Diagnostics.Should().OnlyContain(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Message));
    }

    /// <summary>
    /// The result is total: every identifier the caller supplied is accounted for exactly once.
    /// </summary>
    [Test]
    public void EveryInstalledIdentifierIsEitherOrderedOrRefused()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("valid", "1.0.0"),
            Package("gone", "1.0.0", "absent:>=1.0"),
            Package("copied", "1.0.0"),
            Package("copied", "2.0.0")
        ]);

        resolution.Activated().Concat(resolution.Ineligible())
            .Order(StringComparer.Ordinal)
            .Should().Equal("copied", "gone", "valid");
    }

    [Test]
    public void AnEmptyInstallationResolvesToNothing()
    {
        var resolution = PackageGraphFixtures.Resolve([]);

        resolution.ActivationOrder.Should().BeEmpty();
        resolution.IneligiblePackages.Should().BeEmpty();
        resolution.Diagnostics.Should().BeEmpty();
    }

    [Test]
    public void ANullCandidateIsRejectedAtTheBoundary()
    {
        var resolve = () => PackageGraphFixtures.Resolve([Package("a", "1.0.0"), null!]);

        resolve.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ANullCandidateListIsRejectedAtTheBoundary()
    {
        var resolve = () => PackageGraphFixtures.Resolve(null!);

        resolve.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ADefaultPackageIdentifierIsRejectedAtTheBoundary()
    {
        var build = () => new InstalledPackage(
            default,
            SemanticVersion.Parse("1.0.0"),
            "/packages/anonymous/plugin.json",
            "/packages/anonymous");

        build.Should().Throw<ArgumentException>();
    }
}
