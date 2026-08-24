using System.IO;
using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>Guards language implementations as capabilities rather than media-owned parsing data.</summary>
[TestFixture]
public sealed class LanguageCapabilityTopologyTests
{
    [Test]
    public void ReferenceLanguagesDependOnlyOnTheUniversalContracts()
    {
        var project = ProjectFile.Load(RepositoryLayout.ReferenceLanguages);

        Assert.Multiple(() =>
        {
            Assert.That(project.ProjectReferences, Is.EqualTo(new[] { RepositoryLayout.Abstractions }));
            Assert.That(project.PackageReferences, Is.Empty);
        });
    }

    /// <remarks>
    /// Both assemblies of the movies package are read. The rule is about what the movies kind declares, and
    /// splitting the package into a media domain and an isolated extension moved source across an assembly
    /// boundary without moving it out of the kind.
    /// </remarks>
    [Test]
    public void MoviesDoesNotCarryALanguageNormalizationDeclaration()
    {
        var source = string.Join(
            '\n',
            new[] { RepositoryLayout.MoviesExtension, RepositoryLayout.MoviesDomain }
                .SelectMany(project => RepositoryLayout.Files(project, "*.cs"))
                .Select(File.ReadAllText));

        Assert.That(source, Is.Not.Empty, "no movies source was read, so the rule would pass by finding nothing");
        Assert.That(source, Does.Not.Contain("Normalization = new NormalizationOptions"));
    }
}
