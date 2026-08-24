using System.Linq;
using static Arronix.Plugins.Tests.Dependencies.PackageGraphFixtures;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// One installation carrying every failure class at once, with its complete result written out.
/// </summary>
/// <remarks>
/// <para>
/// The one fixture here is deliberately brittle. It is the operator-facing output of the whole rule set, and
/// pinning it verbatim means an owner can read what a host would actually print without running one, and
/// any change to what is printed has to be made deliberately rather than noticed later.
/// </para>
/// <para>
/// It also pins the shape of the answer as a whole — which packages still start, which do not, and in what
/// order each list is written — rather than pinning seven separate assertions that could each keep passing
/// while the combination stopped making sense.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PackageDependencyReportTests
{
    [Test]
    public void OneInstallationWithEveryFailureClassReportsExactlyThis()
    {
        var resolution = PackageGraphFixtures.Resolve(
        [
            Package("valid.base", "1.0.0"),
            Package("valid.leaf", "1.0.0", "valid.base:>=1.0 <2.0"),
            Package("gone", "1.0.0", "absent:>=1.0"),
            Package("wrong", "1.0.0", "valid.base:>=2.0"),
            Package("ring.a", "1.0.0", "ring.b:>=1.0"),
            Package("ring.b", "1.0.0", "ring.a:>=1.0"),
            Package("rider", "1.0.0", "ring.a:>=1.0"),
            PackageFrom("copied", "1.0.0", "/extensions/copied-old"),
            PackageFrom("copied", "2.0.0", "/extensions/copied-new"),
            Package("user", "1.0.0", "copied:>=1.0")
        ]);

        string[] expected =
        [
            "activate valid.base 1.0.0",
            "activate valid.leaf 1.0.0",
            "refuse copied",
            "refuse gone",
            "refuse rider",
            "refuse ring.a",
            "refuse ring.b",
            "refuse user",
            "refuse wrong",
            "diagnostic DuplicatePackage | copied | - |  | Package 'copied' is installed 2 times "
                + "(1.0.0 at /extensions/copied-old, 2.0.0 at /extensions/copied-new). Remove every copy "
                + "but one: the graph never chooses between them by folder order, by version or by "
                + "discovery order.",
            "diagnostic MissingDependency | gone | absent |  | Package 'gone' requires 'absent' >=1.0, but "
                + "no package with that identifier is installed. Install it, or remove the requirement.",
            "diagnostic IneligibleDependency | rider | ring.a |  | Package 'rider' requires 'ring.a' >=1.0, "
                + "but 'ring.a' is not eligible. A package that depends on an ineligible package is itself "
                + "ineligible: resolve the diagnostics reported against 'ring.a'.",
            "diagnostic DependencyCycle | ring.a | - | ring.a -> ring.b -> ring.a | Package 'ring.a' lies "
                + "on a dependency cycle: ring.a -> ring.b -> ring.a. Dependencies must be acyclic; break "
                + "the cycle by removing one of those requirements.",
            "diagnostic DependencyCycle | ring.b | - | ring.b -> ring.a -> ring.b | Package 'ring.b' lies "
                + "on a dependency cycle: ring.b -> ring.a -> ring.b. Dependencies must be acyclic; break "
                + "the cycle by removing one of those requirements.",
            "diagnostic IneligibleDependency | user | copied |  | Package 'user' requires 'copied' >=1.0, "
                + "but 'copied' is not eligible. A package that depends on an ineligible package is itself "
                + "ineligible: resolve the diagnostics reported against 'copied'.",
            "diagnostic IncompatibleDependency | wrong | valid.base |  | Package 'wrong' requires "
                + "'valid.base' >=2.0, but the installed 'valid.base' is 1.0.0. Install a version the range "
                + "admits, or widen the range."
        ];

        resolution.Render().Should().Be(string.Concat(expected.Select(static line => line + "\n")));
    }
}
