using System;
using System.IO;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>
/// A supplied <c>--root</c> is never self-authorizing. Every case here uses an isolated temporary directory
/// standing in as the repository, never a real path in this checkout or on the machine, because the guard is
/// exercised for every command, not only destructive ones.
/// </summary>
[TestFixture]
internal sealed class InstallationRootGuardTests
{
    private string _repository = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _repository = Path.Combine(Path.GetTempPath(), "arronix-fake-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repository);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_repository))
        {
            Directory.Delete(_repository, recursive: true);
        }
    }

    [Test]
    public void TheRepositoryItselfIsRefused()
        => Assert.That(
            () => InstallationRootGuard.EnsureSafe(_repository, _repository),
            Throws.TypeOf<InstallationException>());

    [Test]
    public void AnAncestorOfTheRepositoryIsRefused()
    {
        var ancestor = Path.GetDirectoryName(_repository)!;

        Assert.That(
            () => InstallationRootGuard.EnsureSafe(ancestor, _repository),
            Throws.TypeOf<InstallationException>());
    }

    [Test]
    public void AnArbitraryRepositoryDescendantIsRefused()
    {
        var sourceLike = Path.Combine(_repository, "src", "Arronix.Api");
        Directory.CreateDirectory(sourceLike);

        Assert.That(
            () => InstallationRootGuard.EnsureSafe(sourceLike, _repository),
            Throws.TypeOf<InstallationException>().With.Message.Contains("source tree"));
    }

    [Test]
    public void TheArtifactsScratchAreaIsAllowed()
    {
        var artifacts = Path.Combine(_repository, "artifacts", "installation");

        Assert.That(() => InstallationRootGuard.EnsureSafe(artifacts, _repository), Throws.Nothing);
    }

    [Test]
    public void ArtifactsSubdirectoriesAreAllowed()
    {
        var artifacts = Path.Combine(_repository, "artifacts", "g07b-browser-proof", "installation");

        Assert.That(() => InstallationRootGuard.EnsureSafe(artifacts, _repository), Throws.Nothing);
    }

    [Test]
    public void ARootOutsideTheRepositoryEntirelyIsAllowed()
    {
        var outside = Path.Combine(Path.GetTempPath(), "arronix-outside-" + Guid.NewGuid().ToString("N"));

        Assert.That(() => InstallationRootGuard.EnsureSafe(outside, _repository), Throws.Nothing);
    }

    [Test]
    public void TheUserProfileDirectoryIsRefused()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.That(
            () => InstallationRootGuard.EnsureSafe(home, _repository),
            Throws.TypeOf<InstallationException>());
    }

    [Test]
    public void ADriveOrFilesystemRootIsRefused()
    {
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.That(
            () => InstallationRootGuard.EnsureSafe(filesystemRoot, _repository),
            Throws.TypeOf<InstallationException>());
    }

    [Test]
    public void ASymbolicLinkAwayFromWhereItAppearsIsRefused()
    {
        var real = Path.Combine(Path.GetTempPath(), "arronix-real-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(Path.GetTempPath(), "arronix-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(real);

        var symbolicLink = Directory.CreateSymbolicLink(link, real);

        try
        {
            Assert.That(
                () => InstallationRootGuard.EnsureSafe(link, _repository),
                Throws.TypeOf<InstallationException>().With.Message.Contains("symbolic link"));
        }
        finally
        {
            // Deletes the link itself, not the real target it points at: .NET does not traverse a
            // directory symlink when the link's own path is what is being deleted.
            symbolicLink.Delete();
            Directory.Delete(real, recursive: true);
        }
    }

    [Test]
    public void ANonExistentRootWithNoLinkInItsPathIsAllowed()
    {
        var notYetCreated = Path.Combine(
            Path.GetTempPath(),
            "arronix-not-yet-" + Guid.NewGuid().ToString("N"),
            "installation");

        Assert.That(() => InstallationRootGuard.EnsureSafe(notYetCreated, _repository), Throws.Nothing);
    }
}
