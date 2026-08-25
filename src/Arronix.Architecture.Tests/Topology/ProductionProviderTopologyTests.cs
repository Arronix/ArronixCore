using System.IO;
using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>Guards the production provider boundary established by the first TMDb package.</summary>
[TestFixture]
public sealed class ProductionProviderTopologyTests
{
    [Test]
    public void TmdbDependsOnlyOnUniversalContractsAndTheMoviesDomain()
    {
        var project = ProjectFile.Load(RepositoryLayout.TmdbProvider);

        Assert.Multiple(() =>
        {
            Assert.That(
                project.ProjectReferences,
                Is.EquivalentTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.MoviesDomain }));
            Assert.That(project.PackageReferences, Is.Empty);
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.MoviesExtension));
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.Host));
            Assert.That(project.ProjectReferences, Does.Not.Contain(RepositoryLayout.Plugins));
        });
    }

    [Test]
    public void CompiledTmdbProviderLinksOnlyUniversalContractsAndTheMoviesDomainWithinArronix()
    {
        var linked = AssemblyMetadata.ReferencedAssemblyNames(RepositoryLayout.TmdbProvider)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .ToArray();

        Assert.That(linked, Is.EqualTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.MoviesDomain }));
    }

    [Test]
    public void TmdbVocabularyDoesNotLeakIntoPlatformFormatOrMoviesProjects()
    {
        var protectedProjects = RepositoryLayout.MediaNeutralProjects
            .Concat([
                RepositoryLayout.VideoFormat,
                RepositoryLayout.VideoFormatContributions,
                RepositoryLayout.MoviesDomain,
                RepositoryLayout.MoviesExtension,
            ])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var offenders = protectedProjects
            .SelectMany(project => new[] { "*.cs", "*.csproj", "*.json" }
                .SelectMany(pattern => RepositoryLayout.Files(project, pattern)))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("tmdb", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("themoviedb", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(RepositoryLayout.Root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "TMDb endpoints, DTOs, credentials, identity markers and names belong to its provider package");
    }
}
