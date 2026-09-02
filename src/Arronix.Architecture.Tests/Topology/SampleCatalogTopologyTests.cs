using System.IO;
using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// The sample catalog is a package like any other, and its invented data stays inside it.
/// </summary>
/// <remarks>
/// <para>
/// A sample shipped so the product can be evaluated without credentials is only worth having if it proves
/// the real path. It therefore has exactly the topology the production TMDb package has: the universal
/// contracts plus the movies media domain, no package references, no reach into Host, the loader, the
/// server or the client. If it needed anything else to work, the thing it demonstrates would not be the
/// thing an operator gets.
/// </para>
/// <para>
/// The second half is the more important one. Invented titles are the classic way a demonstration leaks
/// into a product, so no shipped platform, format or movies project may name any of them.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SampleCatalogTopologyTests
{
    /// <summary>Text that could only have come from the sample package's own content.</summary>
    private static readonly string[] SampleVocabulary =
    [
        "Harborlight",
        "Northmark Pictures",
        "Vellum Road",
        "Sixth Terrace",
        "Ardent Hollow",
        "SampleMovieCataloger",
    ];

    [Test]
    public void TheSampleCatalogDependsOnlyOnUniversalContractsAndTheMoviesDomain()
    {
        var project = ProjectFile.Load(RepositoryLayout.SampleMovieCatalog);

        Assert.Multiple(() =>
        {
            Assert.That(
                project.ProjectReferences,
                Is.EquivalentTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.MoviesDomain }));
            Assert.That(project.PackageReferences, Is.Empty);
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.MoviesExtension));
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.Host));
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.Plugins));
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.Common));
        });
    }

    [Test]
    public void TheCompiledSampleCatalogLinksOnlyUniversalContractsAndTheMoviesDomainWithinArronix()
    {
        var linked = AssemblyMetadata.ReferencedAssemblyNames(RepositoryLayout.SampleMovieCatalog)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .ToArray();

        Assert.That(linked, Is.EqualTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.MoviesDomain }));
    }

    [Test]
    public void TheSampleCatalogShipsItsOwnPackageManifest()
    {
        var manifest = Path.Combine(
            RepositoryLayout.ProjectDirectory(RepositoryLayout.SampleMovieCatalog),
            "plugin.json");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(manifest), Is.True, "a sample that is not a package proves nothing");
            Assert.That(
                File.ReadAllText(manifest),
                Does.Contain("\"id\": \"sample.movie.catalog\""));
        });
    }

    [Test]
    public void NoShippedPlatformFormatOrMoviesProjectNamesTheSampleContent()
    {
        var protectedProjects = RepositoryLayout.MediaNeutralProjects
            .Concat([
                RepositoryLayout.VideoFormat,
                RepositoryLayout.VideoFormatContributions,
                RepositoryLayout.MoviesDomain,
                RepositoryLayout.MoviesExtension,
                RepositoryLayout.TmdbProvider,
            ])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var offenders = protectedProjects
            .SelectMany(project => new[] { "*.cs", "*.razor", "*.csproj", "*.json" }
                .SelectMany(pattern => RepositoryLayout.Files(project, pattern)))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return SampleVocabulary.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .Select(RepositoryLayout.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(protectedProjects, Is.Not.Empty);
            Assert.That(
                offenders,
                Is.Empty,
                "invented sample titles belong to the sample package and nowhere else");
        });
    }

    /// <remarks>
    /// Guards the rule above: a vocabulary that appeared nowhere would make it pass by finding nothing.
    /// </remarks>
    [Test]
    public void TheSampleVocabularyIsActuallyPresentInTheSamplePackage()
    {
        var text = string.Concat(
            RepositoryLayout.Files(RepositoryLayout.SampleMovieCatalog, "*.cs").Select(File.ReadAllText));

        Assert.That(
            SampleVocabulary.Where(term => !text.Contains(term, StringComparison.Ordinal)),
            Is.Empty);
    }
}
