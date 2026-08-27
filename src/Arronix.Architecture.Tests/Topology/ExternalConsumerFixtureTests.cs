using System.IO;
using System.Linq;
using System.Xml.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// The external media, provider and fixture-writer consumers take packages and nothing else.
/// </summary>
/// <remarks>
/// <c>eng/proofs/g07a-external-consumer.sh</c> builds and runs them, but only when somebody runs it. The
/// declarations that make its result mean anything - no project reference, no source reaching into this
/// tree, no visibility grant - are checked here so an edit that reopens one fails on the ordinary rail.
/// </remarks>
[TestFixture]
public sealed class ExternalConsumerFixtureTests
{
    private const string FixtureFolder = "eng/proofs/fixtures";

    private static readonly string[] Consumers = ["g07a-media", "g07a-provider", "../g07a-fixture"];

    /// <summary>The project files the external consumers are built from.</summary>
    private static readonly string[] Projects =
    [
        "g07a-media/Northmark.Shorts.Domain/Northmark.Shorts.Domain.csproj",
        "g07a-media/Northmark.Shorts/Northmark.Shorts.csproj",
        "g07a-provider/Northmark.Shorts.Catalog/Northmark.Shorts.Catalog.csproj",
        "../g07a-fixture/Northmark.Shorts.Fixture/Northmark.Shorts.Fixture.csproj"
    ];

    [Test]
    public void TheConsumersAreInTheWorkingTree()
    {
        Assert.That(
            Projects.Select(static project => Path($"{FixtureFolder}/{project}"))
                .Where(static path => !File.Exists(path)),
            Is.Empty,
            "a rule about a fixture that is not there would pass while checking nothing");
    }

    /// <remarks>A project reference would let either consumer compile against this tree's sources.</remarks>
    [Test]
    public void NeitherConsumerDeclaresAProjectReference()
    {
        foreach (var project in Projects)
        {
            Assert.That(
                Document(project).Descendants("ProjectReference"),
                Is.Empty,
                project);
        }
    }

    /// <remarks>
    /// The same escape hatch by another route: an item whose path climbs out of the project's own folder.
    /// </remarks>
    [Test]
    public void NeitherConsumerIncludesAFileFromOutsideItself()
    {
        foreach (var project in Projects)
        {
            var climbing = Document(project)
                .Descendants()
                .Where(static element => element.Name.LocalName is "Compile" or "Content" or "None" or "EmbeddedResource")
                .Select(static element => (string?)element.Attribute("Include") ?? string.Empty)
                .Where(static include => include.Contains("..", StringComparison.Ordinal));

            Assert.That(climbing, Is.Empty, project);
        }
    }

    /// <remarks>
    /// Every package either consumer takes is one this repository publishes for third parties. A package
    /// reference to anything else would be a dependency an external author could not have.
    /// </remarks>
    [Test]
    public void TheConsumersTakeOnlyPublishedArronixPackagesAndEachOther()
    {
        string[] permitted =
        [
            "Arronix.Abstractions", "Arronix.Format.Video", "Arronix.Sdk", "Northmark.Shorts.Domain"
        ];

        foreach (var project in Projects)
        {
            var taken = Document(project)
                .Descendants("PackageReference")
                .Select(static element => (string?)element.Attribute("Include") ?? string.Empty);

            Assert.That(taken, Is.SubsetOf(permitted), project);
        }
    }

    [Test]
    public void TheFixtureWriterTakesOnlyThePublishedExternalDomainPackage()
    {
        var taken = Packages("../g07a-fixture/Northmark.Shorts.Fixture/Northmark.Shorts.Fixture.csproj");

        Assert.That(taken, Is.EquivalentTo(new[] { "Northmark.Shorts.Domain" }));
    }

    /// <remarks>
    /// Compile-only, so the payload carries no copy of an assembly the installation admits once. The
    /// runtime asset the media package does publish is its own domain half, which is why that one is not
    /// excluded.
    /// </remarks>
    [Test]
    public void SharedContractPackagesAreExcludedFromTheOutputThatDoesNotPublishThem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Excluded("g07a-media/Northmark.Shorts/Northmark.Shorts.csproj", "Arronix.Format.Video"),
                Is.EqualTo("runtime"));
            Assert.That(
                Excluded("g07a-provider/Northmark.Shorts.Catalog/Northmark.Shorts.Catalog.csproj",
                    "Northmark.Shorts.Domain"),
                Is.EqualTo("runtime"));
            Assert.That(
                Excluded("g07a-media/Northmark.Shorts/Northmark.Shorts.csproj", "Northmark.Shorts.Domain"),
                Is.Null,
                "the media package publishes its own domain assembly");
        });
    }

    [Test]
    public void NeitherConsumerGrantsOrIsGrantedInternalsVisibility()
    {
        var granting = Consumers
            .SelectMany(consumer => Directory.EnumerateFiles(Path($"{FixtureFolder}/{consumer}"), "*", SearchOption.AllDirectories))
            .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal)
                || path.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(static path => File.ReadAllText(path).Contains("InternalsVisibleTo", StringComparison.Ordinal));

        var granted = RepositoryLayout.AllProjects
            .SelectMany(static project => RepositoryLayout.Files(project, "*.csproj"))
            .Where(static path => File.ReadAllText(path).Contains("Northmark", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(granting, Is.Empty);
            Assert.That(granted, Is.Empty);
        });
    }

    /// <remarks>
    /// Each consumer stops MSBuild's upward search at its own folder, so a property or a package version
    /// added above it cannot configure a build that is supposed to be a third party's.
    /// </remarks>
    [Test]
    public void EachConsumerConfiguresItselfAndPinsTheSameSdk()
    {
        foreach (var consumer in Consumers)
        {
            foreach (var file in new[] { "Directory.Build.props", "Directory.Packages.props", "NuGet.Config", "global.json" })
            {
                Assert.That(File.Exists(Path($"{FixtureFolder}/{consumer}/{file}")), Is.True, $"{consumer}/{file}");
            }

            Assert.That(
                File.ReadAllText(Path($"{FixtureFolder}/{consumer}/global.json")),
                Is.EqualTo(File.ReadAllText(System.IO.Path.Combine(RepositoryLayout.Root, "global.json"))),
                consumer);
        }
    }

    private static string? Excluded(string project, string package) =>
        Document(project)
            .Descendants("PackageReference")
            .Where(element => (string?)element.Attribute("Include") == package)
            .Select(static element => (string?)element.Attribute("ExcludeAssets"))
            .SingleOrDefault();

    private static IEnumerable<string> Packages(string project) =>
        Document(project)
            .Descendants("PackageReference")
            .Select(static element => (string?)element.Attribute("Include") ?? string.Empty);

    private static XDocument Document(string project) => XDocument.Load(Path($"{FixtureFolder}/{project}"));

    private static string Path(string relative) =>
        System.IO.Path.Combine(RepositoryLayout.Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
}
