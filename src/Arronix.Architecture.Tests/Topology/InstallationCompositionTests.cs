using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// There is one route from this repository to a running installation, and it installs the product.
/// </summary>
/// <remarks>
/// <para>
/// The composer declares its deliverables rather than discovering them, because six projects in this
/// repository carry a package manifest without being product: three are loader fixtures owned by test
/// suites, and three are media extensions still on the legacy imperative seams. A globbed composer would
/// install all six and present test infrastructure as the product.
/// </para>
/// <para>
/// A declared list can go stale, so these rules hold it to the working tree: every project it names exists,
/// ships a manifest, and declares the identifier the composer installs it under; and none of them is a test
/// or fixture project. The composer itself stays outside the platform — nothing that runs references it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class InstallationCompositionTests
{
    private static readonly Regex DeclaredPackage = new(
        """new\("(?<id>[^"]+)", "(?<project>[^"]+)", PackageRole\.(?<role>\w+)\)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>Gets the packages the composer declares, read from its own source.</summary>
    public static IEnumerable<TestCaseData> DeclaredPackages => Declared()
        .Select(entry => new TestCaseData(entry.Id, entry.Project).SetArgDisplayNames(entry.Id, entry.Project));

    [Test]
    public void TheDeclaredDeliverableSetWasActuallyRead()
    {
        var declared = Declared();

        Assert.Multiple(() =>
        {
            Assert.That(declared, Is.Not.Empty, "the composer's deliverable list could not be read");
            Assert.That(
                declared.Select(static entry => entry.Id),
                Does.Contain("movies"),
                "a deliverable set without the reference media extension is not this repository's product");
        });
    }

    [TestCaseSource(nameof(DeclaredPackages))]
    public void EveryDeclaredPackageIsAProjectThatShipsThatExactManifest(string packageId, string projectName)
    {
        var manifest = Path.Combine(RepositoryLayout.ProjectDirectory(projectName), "plugin.json");

        Assert.Multiple(() =>
        {
            Assert.That(
                RepositoryLayout.ProjectExists(projectName),
                Is.True,
                $"the composer installs '{projectName}', which is not a project here");
            Assert.That(
                File.Exists(manifest),
                Is.True,
                $"'{projectName}' is installed as a package but ships no manifest");
            Assert.That(
                File.ReadAllText(manifest),
                Does.Contain($"\"id\": \"{packageId}\""),
                $"'{projectName}' is installed as '{packageId}' but does not declare that identifier");
        });
    }

    /// <remarks>
    /// The distinction this repository keeps everywhere else: a fixture proves a loader rule and is owned by
    /// the suite that wrote it. Installing one would make test infrastructure part of what an evaluator sees.
    /// </remarks>
    [Test]
    public void NoTestOrFixtureProjectIsInstalledAsProduct()
    {
        var offenders = Declared()
            .Select(static entry => entry.Project)
            .Where(static project =>
                project.EndsWith(".Tests", StringComparison.Ordinal)
                || project.Contains(".Tests.", StringComparison.Ordinal)
                || project.Contains("Fixture", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void TheComposerNamesTheRealServerAndClientProjects()
    {
        var text = ComposerSource("Deliverables.cs");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain($"ServerProject = \"{RepositoryLayout.Api}\""));
            Assert.That(text, Does.Contain($"ClientProject = \"{RepositoryLayout.Client}\""));
        });
    }

    /// <remarks>
    /// The composer is a tool, not a layer. Anything in the running platform that referenced it would make
    /// the product depend on the thing that installs it. Its own test project is the one deliberate
    /// exception: proving the composer's behaviour needs a reference to it, and a test project is never part
    /// of what runs.
    /// </remarks>
    [Test]
    public void NothingInTheRunningPlatformReferencesTheComposer()
    {
        var offenders = RepositoryLayout.AllProjects
            .Where(static project => !string.Equals(
                project,
                RepositoryLayout.InstallationComposer,
                StringComparison.Ordinal))
            .Where(static project => !string.Equals(
                project,
                RepositoryLayout.InstallationComposer + ".Tests",
                StringComparison.Ordinal))
            .Where(static project => ProjectFile.Load(project).ProjectReferences
                .Contains(RepositoryLayout.InstallationComposer, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    /// <remarks>
    /// The composer takes the platform's layout type rather than restating those folder names, which is the
    /// whole reason a server and its installer can agree without anybody keeping two lists in step.
    /// </remarks>
    [Test]
    public void TheComposerTakesTheSharedInstallationLayout()
    {
        var project = ProjectFile.Load(RepositoryLayout.InstallationComposer);

        Assert.Multiple(() =>
        {
            Assert.That(project.ProjectReferences, Is.EqualTo(new[] { RepositoryLayout.Common }));
            Assert.That(
                ComposerSource("InstallationComposer.cs"),
                Does.Contain("using Arronix.Common.Installation;"));
        });
    }

    private static IReadOnlyList<(string Id, string Project)> Declared() => DeclaredPackage
        .Matches(ComposerSource("Deliverables.cs"))
        .Select(static match => (match.Groups["id"].Value, match.Groups["project"].Value))
        .ToArray();

    private static string ComposerSource(string fileName) => File.ReadAllText(Path.Combine(
        RepositoryLayout.ProjectDirectory(RepositoryLayout.InstallationComposer),
        fileName));
}
