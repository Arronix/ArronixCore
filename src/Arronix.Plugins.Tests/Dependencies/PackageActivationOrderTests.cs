using System.Linq;
using static Arronix.Plugins.Tests.Dependencies.PackageGraphFixtures;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// The order eligible packages come out in, and the two rules that decide it.
/// </summary>
/// <remarks>
/// A dependency precedes its dependant because activation needs it to. Everything else is decided by
/// package identifier, because that is the only tie-break that is a property of the packages: order of
/// discovery, order of declaration and installed version all differ between two hosts holding the same
/// installation.
/// </remarks>
[TestFixture]
public sealed class PackageActivationOrderTests
{
    [Test]
    public void IndependentPeersComeOutInIdentifierOrderRatherThanDiscoveryOrder()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("zulu", "1.0.0"),
            Package("mike", "1.0.0"),
            Package("alpha", "1.0.0")
        ]);

        resolution.Activated().Should().Equal("alpha", "mike", "zulu");
    }

    /// <summary>
    /// The identifiers here are chosen so that identifier order and dependency order disagree completely.
    /// A resolver that only sorted, or only walked the graph, would fail one half of this.
    /// </summary>
    [Test]
    public void ADeepChainComesOutInDependencyOrderEvenWhenIdentifierOrderIsTheReverse()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("n0", "1.0.0", "n1:>=1.0"),
            Package("n1", "1.0.0", "n2:>=1.0"),
            Package("n2", "1.0.0", "n3:>=1.0"),
            Package("n3", "1.0.0", "n4:>=1.0"),
            Package("n4", "1.0.0", "n5:>=1.0"),
            Package("n5", "1.0.0", "n6:>=1.0"),
            Package("n6", "1.0.0", "n7:>=1.0"),
            Package("n7", "1.0.0")
        ]);

        resolution.Diagnostics.Should().BeEmpty();
        resolution.Activated().Should().Equal("n7", "n6", "n5", "n4", "n3", "n2", "n1", "n0");
    }

    /// <summary>
    /// A diamond: one shared dependency, two independent middles, one dependant. The middles are peers, so
    /// they are ordered by identifier; the shared dependency and the dependant are pinned by the graph.
    /// </summary>
    [Test]
    public void ADiamondPutsTheSharedDependencyFirstAndOrdersTheMiddlesByIdentifier()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("a", "1.0.0", "m:>=1.0", "b:>=1.0"),
            Package("m", "1.0.0", "z:>=1.0"),
            Package("b", "1.0.0", "z:>=1.0"),
            Package("z", "1.0.0")
        ]);

        resolution.Diagnostics.Should().BeEmpty();
        resolution.Activated().Should().Equal("z", "b", "m", "a");
    }

    [Test]
    public void ADiamondIsUnchangedWhenItsRequirementsAreDeclaredTheOtherWayRound()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("a", "1.0.0", "b:>=1.0", "m:>=1.0"),
            Package("m", "1.0.0", "z:>=1.0"),
            Package("b", "1.0.0", "z:>=1.0"),
            Package("z", "1.0.0")
        ]);

        resolution.Activated().Should().Equal("z", "b", "m", "a");
    }

    /// <summary>
    /// The property behind the two fixtures above, checked over a graph nobody chose the answer for by hand.
    /// </summary>
    [Test]
    public void EveryDependencyPrecedesEveryDependant()
    {
        var packages = new[]
        {
            Package("app.one", "1.0.0", "lib.text:>=1.0", "lib.net:>=1.0"),
            Package("app.two", "1.0.0", "lib.net:>=1.0"),
            Package("lib.net", "1.0.0", "core:>=1.0"),
            Package("lib.text", "1.0.0", "core:>=1.0"),
            Package("core", "1.0.0"),
            Package("tool", "1.0.0", "app.one:>=1.0", "app.two:>=1.0")
        };

        var resolution = PackageGraphFixtures.Resolve(packages);
        var position = resolution.ActivationOrder
            .Select(static (candidate, index) => (Id: candidate.Id.Value, Index: index))
            .ToDictionary(static entry => entry.Id, static entry => entry.Index, StringComparer.Ordinal);

        resolution.Diagnostics.Should().BeEmpty();
        position.Should().HaveCount(packages.Length);

        foreach (var package in packages)
        {
            foreach (var requirement in package.Requirements)
            {
                position[requirement.PackageId.Value]
                    .Should().BeLessThan(position[package.Id.Value]);
            }
        }
    }

    /// <summary>
    /// An ineligible package leaves the order rather than stalling it: the packages behind it in the graph
    /// still come out, in the same relative order they would have had.
    /// </summary>
    [Test]
    public void RemovingAnIneligibleBranchDoesNotDisturbTheRest()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("core", "1.0.0"),
            Package("lib.net", "1.0.0", "core:>=1.0"),
            Package("lib.text", "1.0.0", "core:>=1.0"),
            Package("broken", "1.0.0", "absent:>=1.0"),
            Package("rider", "1.0.0", "broken:>=1.0")
        ]);

        resolution.Activated().Should().Equal("core", "lib.net", "lib.text");
        resolution.Ineligible().Should().Equal("broken", "rider");
    }
}
