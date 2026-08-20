using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>Guards the format-capability boundary independently of media-kind neutrality.</summary>
[TestFixture]
public class FormatCapabilityTopologyTests
{
    [Test]
    public void VideoDependsOnlyOnTheUniversalContracts()
    {
        var project = ProjectFile.Load(RepositoryLayout.VideoFormat);

        Assert.Multiple(() =>
        {
            Assert.That(project.ProjectReferences, Is.EqualTo(new[] { RepositoryLayout.Abstractions }));
            Assert.That(project.PackageReferences, Is.Empty);
        });
    }

    [Test]
    public void HostDoesNotReferenceAFormatCapability()
    {
        var project = ProjectFile.Load(RepositoryLayout.Host);

        Assert.That(
            project.ProjectReferences,
            Does.Not.Contain(RepositoryLayout.VideoFormat),
            "Host must execute generic algorithms without compiling a favored representation family in.");
    }
}
