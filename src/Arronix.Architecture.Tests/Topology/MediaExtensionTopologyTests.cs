using System.Linq;
using System.Reflection;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Rules 1 and 8 - the enforcement topology of a media extension.
/// </summary>
/// <remarks>
/// <para>
/// This is the single most important rule in the platform, and it is asserted twice over: once against
/// what the project file declares, and once against what the compiled assembly actually links. The two
/// can disagree - a package that arrives transitively, a reference added by a target rather than by the
/// author - and it is the second form that a loaded extension is judged by at run time.
/// </para>
/// <para>
/// An extension contributes behavior, never interface implementation. That is why the same fixture also
/// refuses markup files and rendering packages: a plugin that could ship a component would decide how it
/// is presented, and the whole point of the intent model is that it cannot.
/// </para>
/// </remarks>
[TestFixture]
public class MediaExtensionTopologyTests
{
    private static readonly string[] RenderingPackageMarkers =
    [
        "Blazor",
        "Razor",
        "Microsoft.AspNetCore",
        "Components"
    ];

    private static readonly string[] MarkupPatterns = ["*.razor", "*.razor.cs", "*.razor.css", "*.cshtml"];

    /// <summary>Gets the media extension projects, for the parameterized cases below.</summary>
    public static IEnumerable<string> MediaExtensions => RepositoryLayout.MediaExtensionProjects;

    [Test]
    public void TheWorkingTreeContainsTheFourReferenceMediaExtensions()
    {
        // Guards every other case in this fixture. A parameterized test over an empty discovery set
        // reports success while asserting nothing, which is the failure mode a governance suite must not
        // have.
        Assert.That(
            RepositoryLayout.MediaExtensionProjects,
            Has.Count.GreaterThanOrEqualTo(4),
            "Discovery of 'src/" + RepositoryLayout.ExtensionPrefix + "*' found fewer projects than the "
            + "milestone ships, so the extension-topology rules below would be checking nothing.");
    }

    /// <param name="projectName">The extension under test.</param>
    /// <remarks>
    /// The permitted set is the universal contracts, the domain assembly of any format package the
    /// extension composes, and its own media domain - the assembly it publishes for others to pair with.
    /// A format package's executable half is deliberately absent: everything a declaration needs from a
    /// format is domain semantics, so referencing the executable half would only copy an independently
    /// updatable assembly into this package's payload.
    /// It may not reference another kind's media domain - <see cref="PackageFacetTopologyTests"/> holds
    /// that line - and it may still reference no platform assembly at all.
    /// </remarks>
    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionReferencesOnlyContractsAndTypedFormatAssemblies(string projectName)
    {
        var project = ProjectFile.Load(projectName);
        var permitted = new[]
        {
            RepositoryLayout.Abstractions,
            RepositoryLayout.VideoFormat,
        }.Concat(new[] { RepositoryLayout.MediaDomainOf(projectName) }.OfType<string>()).ToArray();

        Assert.That(
            project.RuntimeProjectReferences,
            Is.SubsetOf(permitted),
            $"'{projectName}' may reference contracts, typed format assemblies and its own media domain, "
            + "but not the platform library, loader, runtime or HTTP surface.");

        Assert.That(project.RuntimeProjectReferences, Does.Contain(RepositoryLayout.Abstractions));
        Assert.That(
            project.AnalyzerProjectReferences,
            Is.SubsetOf(new[] { RepositoryLayout.Generators }),
            $"'{projectName}' may use the Arronix compile-time generator, but an analyzer must not become "
            + "a runtime plugin dependency.");
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionDeclaresNoPackageReference(string projectName)
    {
        var project = ProjectFile.Load(projectName);

        Assert.That(
            project.PackageReferences,
            Is.Empty,
            $"'{projectName}' must take no package at all. Everything an extension may reach is on its "
            + "context, and a package taken here is a second copy of a dependency the host already owns.");
    }

    /// <summary>
    /// The binary half of rule 1.
    /// </summary>
    /// <remarks>
    /// Neither half subsumes the other, which is why both are here. The compiler emits an assembly
    /// reference only for an assembly a type is actually used from, so a declared-but-unused project
    /// reference is invisible here and is caught by the declaration check above; and a reference that
    /// arrives through a target rather than through an author is invisible there and is caught here.
    /// </remarks>
    /// <param name="projectName">The extension under test.</param>
    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionLinksNoPlatformAssembly(string projectName)
    {
        var assembly = LoadExtensionAssembly(projectName);
        var permitted = new[]
        {
            RepositoryLayout.Abstractions,
            RepositoryLayout.VideoFormat,
        }.Concat(new[] { RepositoryLayout.MediaDomainOf(projectName) }.OfType<string>()).ToArray();

        var linked = assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            linked,
            Is.SubsetOf(permitted),
            $"The compiled '{projectName}' links a platform assembly. The declaration and the binary have "
            + "to agree, because the loader's reference-graph check judges the binary.");
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionContainsNoMarkupFile(string projectName)
    {
        var markup = MarkupPatterns
            .SelectMany(pattern => RepositoryLayout.Files(projectName, pattern))
            .Select(RepositoryLayout.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            markup,
            Is.Empty,
            $"'{projectName}' ships an interface implementation. An extension declares intent - what may be "
            + "done, what may be shown, what may be edited - and the front end decides how.");
    }

    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionNamesNoRenderingPackage(string projectName)
    {
        var project = ProjectFile.Load(projectName);

        var rendering = project
            .PackageReferences
            .Where(package => RenderingPackageMarkers.Any(
                marker => package.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.That(rendering, Is.Empty, $"'{projectName}' declares a rendering package.");
    }

    private static Assembly LoadExtensionAssembly(string projectName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(projectName));
        }
        catch (Exception failure) when (failure is System.IO.FileNotFoundException or BadImageFormatException)
        {
            Assert.Fail(
                $"'{projectName}' could not be loaded from the test output: {failure.Message}. This fixture "
                + "references every media extension precisely so the binary form of the rule is checkable.");
            throw;
        }
    }
}
