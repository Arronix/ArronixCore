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

    [Test]
    public void MoviesDoesNotCarryALanguageNormalizationDeclaration()
    {
        var source = string.Join(
            '\n',
            RepositoryLayout.Files("Arronix.Plugin.Movies", "*.cs").Select(File.ReadAllText));

        Assert.That(source, Does.Not.Contain("Normalization = new NormalizationOptions"));
    }
}
