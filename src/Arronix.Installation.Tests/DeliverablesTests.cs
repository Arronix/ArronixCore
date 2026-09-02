using System.Linq;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>
/// Selecting a package selects its whole dependency closure, read from each candidate's own real
/// <c>plugin.json</c> in this checkout rather than a second hand-maintained graph.
/// </summary>
[TestFixture]
internal sealed class DeliverablesTests
{
    [Test]
    public void SelectingMoviesAloneAlsoInstallsTheVideoItDependsOn()
    {
        var selected = Deliverables.Select(RepositoryPaths.Root, includeSamples: false, only: ["movies"]);

        Assert.That(
            selected.Select(static p => p.Id),
            Is.EqualTo(new[] { "arronix.format.video", "movies" }),
            "the closure must preserve the declared installation order");
    }

    [Test]
    public void SelectingTmdbAloneTransitivelyInstallsMoviesAndVideo()
    {
        var selected = Deliverables.Select(RepositoryPaths.Root, includeSamples: false, only: ["tmdb"]);

        Assert.That(
            selected.Select(static p => p.Id),
            Is.EqualTo(new[] { "arronix.format.video", "movies", "tmdb" }));
    }

    [Test]
    public void SelectingVideoAloneInstallsOnlyVideo()
    {
        var selected = Deliverables.Select(RepositoryPaths.Root, includeSamples: false, only: ["arronix.format.video"]);

        Assert.That(selected.Select(static p => p.Id), Is.EqualTo(new[] { "arronix.format.video" }));
    }

    [Test]
    public void AnEmptySelectionWithSamplesIncludesEveryDeclaredPackage()
    {
        var selected = Deliverables.Select(RepositoryPaths.Root, includeSamples: true, only: []);

        Assert.That(selected, Is.EqualTo(Deliverables.Packages));
    }

    [Test]
    public void AnEmptySelectionWithoutSamplesExcludesSamplePackages()
    {
        var selected = Deliverables.Select(RepositoryPaths.Root, includeSamples: false, only: []);

        Assert.That(selected.Any(static p => p.Role == PackageRole.Sample), Is.False);
    }

    [Test]
    public void AnUnknownPackageIdentifierIsRefused()
        => Assert.That(
            () => Deliverables.Select(RepositoryPaths.Root, includeSamples: true, only: ["not-a-real-package"]),
            Throws.TypeOf<InstallationException>());

    [Test]
    public void SelectionOrderIsIndependentOfArgumentOrder()
    {
        var forward = Deliverables.Select(RepositoryPaths.Root, includeSamples: false, only: ["movies", "arronix.format.video"]);
        var backward = Deliverables.Select(RepositoryPaths.Root, includeSamples: false, only: ["arronix.format.video", "movies"]);

        Assert.That(forward.Select(static p => p.Id), Is.EqualTo(backward.Select(static p => p.Id)));
    }

    [Test]
    public void EveryDeclaredPackageProjectFileExists()
    {
        foreach (var package in Deliverables.Packages)
        {
            var projectFile = Deliverables.ProjectFile(RepositoryPaths.Root, package.ProjectName);

            Assert.That(
                System.IO.File.Exists(projectFile),
                $"'{package.Id}' names project '{package.ProjectName}', which has no project file at '{projectFile}'");
        }
    }
}
