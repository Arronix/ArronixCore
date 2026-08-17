using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Arronix.Common.Tests;

/// <summary>
/// Guards the physical shape of the platform assembly: its identity, its single project reference and the
/// approved package set, and the one-way dependency on the contract assembly.
/// </summary>
[TestFixture]
public class ProjectShapeTests
{
    private const string CommonAssemblyName = "Arronix.Common";
    private const string AbstractionsAssemblyName = "Arronix.Abstractions";
    private const string SolutionFileName = "Arronix.sln";

    private static readonly string[] ApprovedPackages =
    [
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Options.DataAnnotations",
        "System.IO.Hashing",
        "System.Threading.RateLimiting",
    ];

    [Test]
    public void PlatformAssembly_LoadsUnderItsOwnName()
    {
        var assembly = LoadAssembly(CommonAssemblyName);

        Assert.That(assembly.GetName().Name, Is.EqualTo(CommonAssemblyName));
    }

    [Test]
    public void PlatformAssembly_DeclaresExactlyOneProjectReferenceOnTheContractAssembly()
    {
        var references = ReadProjectReferences(CommonAssemblyName);

        Assert.That(references, Is.EqualTo(new[] { AbstractionsAssemblyName }));
    }

    [Test]
    public void PlatformAssembly_DeclaresOnlyTheApprovedPackageSet()
    {
        var packages = ReadPackageReferences(CommonAssemblyName);

        Assert.That(packages, Is.EqualTo(ApprovedPackages));
    }

    [Test]
    public void PlatformAssembly_PinsNoPackageVersionsLocally()
    {
        var project = LoadProjectFile(CommonAssemblyName);

        var pinned = project
            .Descendants("PackageReference")
            .Where(static element => element.Attribute("Version") is not null)
            .Select(static element => (string?)element.Attribute("Include"))
            .ToArray();

        Assert.That(pinned, Is.Empty, "Package versions belong in the central Directory.Packages.props file.");
    }

    [Test]
    public void ContractAssembly_TakesNoPackageDependencies()
    {
        var packages = ReadPackageReferences(AbstractionsAssemblyName);

        Assert.That(packages, Is.Empty);
    }

    [Test]
    public void ContractAssembly_DeclaresNoReferenceBackToThePlatformAssembly()
    {
        var references = ReadProjectReferences(AbstractionsAssemblyName);

        Assert.That(references, Does.Not.Contain(CommonAssemblyName));
    }

    [Test]
    public void ContractAssembly_LinksNothingFromThePlatformAssembly()
    {
        var assembly = LoadAssembly(AbstractionsAssemblyName);

        var linked = assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ToArray();

        Assert.That(linked, Does.Not.Contain(CommonAssemblyName));
    }

    private static Assembly LoadAssembly(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not locate '{SolutionFileName}' above '{AppContext.BaseDirectory}'.");
        }

        return directory.FullName;
    }

    private static XDocument LoadProjectFile(string projectName)
    {
        var path = Path.Combine(RepositoryRoot(), "src", projectName, projectName + ".csproj");

        return XDocument.Load(path);
    }

    private static string[] ReadPackageReferences(string projectName) =>
        LoadProjectFile(projectName)
            .Descendants("PackageReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => !string.IsNullOrEmpty(include))
            .Select(static include => include!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] ReadProjectReferences(string projectName) =>
        LoadProjectFile(projectName)
            .Descendants("ProjectReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => !string.IsNullOrEmpty(include))
            .Select(static include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
