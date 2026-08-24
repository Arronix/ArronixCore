using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Versioning;
using static Arronix.Plugins.Tests.Dependencies.PackageGraphFixtures;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// The result does not depend on the order the caller happened to enumerate the installation in.
/// </summary>
/// <remarks>
/// <para>
/// This is the fixture the whole engine exists to satisfy. Discovery order is a property of a file system
/// walk, not of an installation, and a resolver that lets it through produces an installation that differs
/// between two hosts holding the same packages — and a support report that cannot be reproduced from the
/// list of what is installed.
/// </para>
/// <para>
/// Exhaustive rather than sampled, and comparing the complete rendering rather than one field, because the
/// claim being made is about every permutation and about the whole result.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PackageDependencyPermutationTests
{
    /// <summary>
    /// Every failure class that can be decided without a cycle, in one installation: a valid pair, an
    /// identifier installed twice, a dependant of it, an absent dependency and an incompatible one.
    /// </summary>
    private static IReadOnlyList<InstalledPackage> MixedInstallation =>
    [
        Package("base", "1.0.0"),
        Package("dep", "1.0.0", "base:>=1.0 <2.0"),
        Package("lib", "1.0.0"),
        Package("lib", "2.0.0"),
        Package("user", "1.0.0", "lib:>=1.0"),
        Package("gone", "1.0.0", "absent:>=1.0"),
        Package("wrong", "1.0.0", "base:>=2.0")
    ];

    /// <summary>
    /// A cycle, a package riding on the cycle, and an unrelated pair that must survive both.
    /// </summary>
    private static IReadOnlyList<InstalledPackage> CyclicInstallation =>
    [
        Package("ring.a", "1.0.0", "ring.b:>=1.0"),
        Package("ring.b", "1.0.0", "ring.c:>=1.0"),
        Package("ring.c", "1.0.0", "ring.a:>=1.0"),
        Package("rider", "1.0.0", "ring.a:>=1.0"),
        Package("island", "1.0.0"),
        Package("isle", "1.0.0", "island:>=1.0")
    ];

    /// <summary>
    /// An unavailable package, its direct and transitive dependants, an unrelated pair and a duplicate.
    /// </summary>
    private static IReadOnlyList<InstalledPackage> UnavailableInstallation =>
    [
        DisabledPackage("core", "1.0.0"),
        Package("middle", "1.0.0", "core:>=1.0"),
        Package("top", "1.0.0", "middle:>=1.0"),
        Package("island", "1.0.0"),
        Package("isle", "1.0.0", "island:>=1.0"),
        Package("wrong", "1.0.0", "core:>=2.0")
    ];

    /// <summary>
    /// Declared unavailability reaches the result the same way every other input does: not at all through
    /// the order it arrived in.
    /// </summary>
    [Test]
    public void EveryPermutationOfAnInstallationWithAnUnavailablePackageResolvesIdentically()
    {
        AssertInvariantUnderPermutation(UnavailableInstallation, expectedPermutations: 720);
    }

    [Test]
    public void EveryPermutationOfAMixedInstallationResolvesIdentically()
    {
        AssertInvariantUnderPermutation(MixedInstallation, expectedPermutations: 5040);
    }

    [Test]
    public void EveryPermutationOfACyclicInstallationResolvesIdentically()
    {
        AssertInvariantUnderPermutation(CyclicInstallation, expectedPermutations: 720);
    }

    /// <summary>
    /// The order a package declares its own requirements in is a property of its file, not of the graph.
    /// </summary>
    [Test]
    public void EveryPermutationOfOnePackagesRequirementsResolvesIdentically()
    {
        var others = new[]
        {
            Package("w", "1.0.0"),
            Package("x", "1.0.0"),
            Package("y", "1.0.0"),
            Package("z", "1.0.0")
        };

        string[] requirements = ["w:>=1.0", "x:>=1.0", "y:>=1.0", "z:>=1.0"];

        string? expected = null;
        var seen = 0;

        foreach (var order in Permutations(requirements))
        {
            var candidate = new InstalledPackage(
                PluginId.FromString("app"),
                SemanticVersion.Parse("1.0.0"),
                "/packages/app/plugin.json",
                "/packages/app",
                requirements: [.. order.Select(Requirement)]);

            var rendered = PackageGraphFixtures.Resolve([candidate, .. others]).Render();

            expected ??= rendered;
            rendered.Should().Be(expected, "requirement declaration order must not reach the result");
            seen++;
        }

        seen.Should().Be(24);
        expected.Should().Contain("activate w 1.0.0");
    }

    /// <summary>
    /// Two spellings of "this copy came from nowhere" cannot both exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the tie a review found on the candidate branch. Two copies shared a version, one stated no
    /// origin and the other a blank one — the same statement in two spellings, which the sort key could not
    /// tell apart and the rendered text could: an absent origin printed the version alone and a blank one
    /// printed it followed by a dangling "at". A stable sort left the order of the two, and therefore the
    /// operator-facing message, decided by whichever copy the file-system walk reached first.
    /// </para>
    /// <para>
    /// The canonical model closes it one step earlier by refusing a package with no folder at all: an
    /// installed copy was found somewhere, and a model that could not say where would be describing
    /// something that is not installed. The state is unrepresentable rather than normalized, so no
    /// comparison anywhere has to agree about which spelling wins.
    /// </para>
    /// </remarks>
    /// <param name="folder">A folder spelling that says nothing.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ACopyThatCannotSayWhereItCameFromIsRefused(string? folder)
    {
        var construct = () => PackageWithFolder("copied", "1.0.0", folder);

        construct.Should().Throw<ArgumentException>()
            .WithMessage(
                "*folder*",
                "an installed copy was found somewhere, and two spellings of saying otherwise would let "
                + "discovery order decide the text of a duplicate diagnostic");
    }

    /// <summary>
    /// The same tie where the folders are real and different: the order is theirs, not the caller's.
    /// </summary>
    /// <remarks>
    /// The sort key and the rendered text are one value, so two copies that tie on version are ordered by
    /// exactly the text the message lists them under. A coarser key would leave ties the text could still
    /// tell apart, and the winner of one would be whichever copy was discovered first.
    /// </remarks>
    [Test]
    public void CopiesSharingAVersionAreListedByTheTextTheyPrintBy()
    {
        IReadOnlyList<InstalledPackage> installation =
        [
            PackageWithFolder("copied", "1.0.0", "/extensions/zulu"),
            PackageWithFolder("copied", "1.0.0", "/extensions/alpha"),
            PackageWithFolder("copied", "1.0.0", "/extensions/mike")
        ];

        AssertInvariantUnderPermutation(installation, expectedPermutations: 6);

        PackageGraphFixtures.Resolve(installation)
            .Of(PackageDependencyDiagnosticKind.DuplicatePackage)
            .Single()
            .Message
            .Should().Contain(
                "(1.0.0 at /extensions/alpha, 1.0.0 at /extensions/mike, 1.0.0 at /extensions/zulu)");
    }

    /// <summary>
    /// The exhaustive comparison, and the check that it really was exhaustive. A permutation generator that
    /// silently produced one ordering would make every fixture here pass while proving nothing.
    /// </summary>
    private static void AssertInvariantUnderPermutation(
        IReadOnlyList<InstalledPackage> installation,
        int expectedPermutations)
    {
        var baseline = PackageGraphFixtures.Resolve(installation).Render();
        var seen = 0;

        foreach (var order in Permutations(installation))
        {
            PackageGraphFixtures.Resolve(order).Render().Should().Be(baseline);
            seen++;
        }

        seen.Should().Be(expectedPermutations);
        baseline.Should().NotBeEmpty();
    }
}
