using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Api.Tests.Vertical;

/// <summary>
/// The two facts about the server project that its own file says are asserted here: it takes no package,
/// and it holds no component file.
/// </summary>
[TestFixture]
internal sealed class ApiProjectShapeTests
{
    [Test]
    public void TheServerProjectTakesNoPackage()
    {
        var packages = XDocument.Load(ProjectFile)
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToArray();

        packages.Should().BeEmpty("the whole HTTP surface is in the shared framework");
    }

    [Test]
    public void TheServerProjectHoldsNoComponentFile()
    {
        var components = Directory
            .EnumerateFiles(ProjectFolder, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        using var assertions = new AssertionScope();
        Directory.Exists(ProjectFolder).Should().BeTrue("the assertion is worthless if it is looking nowhere");
        components.Should().BeEmpty();
    }

    private static string ProjectFolder => Path.Combine(RepositoryRoot, "src", "Arronix.Api");

    private static string ProjectFile => Path.Combine(ProjectFolder, "Arronix.Api.csproj");

    /// <summary>Walks up from the test binaries until the repository root is recognisable.</summary>
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("The repository root is not above the test binaries.");
        }
    }
}
