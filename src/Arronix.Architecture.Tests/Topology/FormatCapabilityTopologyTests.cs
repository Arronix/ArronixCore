using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>Guards the format-capability boundary independently of media-kind neutrality.</summary>
/// <remarks>
/// The video package is two assemblies with two release cadences. `Arronix.Format.Video` is the domain
/// surface: the representation and quality facts a release carries, shared once per installation, and what
/// a second video media kind closes its own typed releases over. `Arronix.Format.Video.Contributions` holds
/// what video contributes to a dependant's compiled behavior - the release-term vocabulary, the
/// file-extension family definition and the policy defaults - all of which grow as recognition work lands,
/// and none of which anything outside a media definition consumes.
/// </remarks>
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
    public void VideoContributionsDependOnlyOnTheUniversalContractsAndTheVideoDomain()
    {
        var project = ProjectFile.Load(RepositoryLayout.VideoFormatContributions);

        Assert.Multiple(() =>
        {
            Assert.That(
                project.ProjectReferences,
                Is.EqualTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.VideoFormat }));
            Assert.That(project.PackageReferences, Is.Empty);
        });
    }

    /// <remarks>
    /// The direction is the invariant. Contributions uses the shared types; if the shared assembly ever used
    /// Contributions, resolving the domain name would pull the churning half in with it and the split would
    /// buy nothing. It is also the property that decides what a browser client could be given.
    /// </remarks>
    [Test]
    public void TheVideoDomainDoesNotReferenceItsContributions()
    {
        var project = ProjectFile.Load(RepositoryLayout.VideoFormat);

        Assert.That(
            project.ProjectReferences,
            Does.Not.Contain(RepositoryLayout.VideoFormatContributions),
            "the reference between the two halves of a package runs one way only.");
    }

    /// <remarks>
    /// The reason the representation types are their own assembly rather than a folder. Two media kinds
    /// close their typed releases over <c>Video</c>; when they are separately installed packages, that is
    /// only sound if both resolve one copy, and being one assembly is the precondition for that.
    /// </remarks>
    [Test]
    public void TwoMediaExtensionsCompileAgainstTheOneVideoDomainAssembly()
    {
        var dependants = RepositoryLayout.MediaExtensionProjects
            .Select(ProjectFile.Load)
            .Where(static project => project.ProjectReferences.Contains(
                RepositoryLayout.VideoFormat,
                StringComparer.Ordinal))
            .Select(static project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            dependants,
            Has.Length.GreaterThanOrEqualTo(2),
            "Movies and Television are the two dependants this gate is proved against. If either stops "
            + "referencing the video domain assembly, the shared-identity claim is about one package.");
    }

    [Test]
    public void HostDoesNotReferenceAFormatCapability()
    {
        var project = ProjectFile.Load(RepositoryLayout.Host);

        Assert.Multiple(() =>
        {
            Assert.That(
                project.ProjectReferences,
                Does.Not.Contain(RepositoryLayout.VideoFormat),
                "Host must execute generic algorithms without compiling a favored representation family in.");
            Assert.That(
                project.ProjectReferences,
                Does.Not.Contain(RepositoryLayout.VideoFormatContributions),
                "and it takes neither the vocabulary nor the policy defaults that come with one.");
        });
    }
}
