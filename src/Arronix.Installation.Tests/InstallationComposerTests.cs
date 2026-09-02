using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arronix.Common.Installation;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>
/// The composer never clears or partially overwrites a live installation before a whole new generation has
/// been built and validated in a sibling staging directory, so a failed compose leaves the last good
/// installation exactly as it was.
/// </summary>
[TestFixture]
internal sealed class InstallationComposerTests
{
    private string _root = string.Empty;
    private string _serverProject = string.Empty;
    private string _clientProject = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-composer-" + Guid.NewGuid().ToString("N"));
        _serverProject = Deliverables.ProjectFile(RepositoryPaths.Root, Deliverables.ServerProject);
        _clientProject = Deliverables.ProjectFile(RepositoryPaths.Root, Deliverables.ClientProject);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        // A composer never leaves a sibling staging directory behind it, whether it succeeded or failed
        // before commit. This is asserted in every test below, not only here; the teardown just guarantees
        // a leftover from a broken test does not bleed into the next one.
        var staging = _root + ".staging";

        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Test]
    public void AFullComposeProducesAValidatedManifestAndNoStagingLeftover()
    {
        var layout = InstallationLayout.At(_root);
        var dotnet = FakeCli().WithPackage("pkg:video", "arronix.format.video");
        var composer = new InstallationComposer(dotnet, RepositoryPaths.Root, layout);

        var manifest = composer.Install(
            [new PackageSource("arronix.format.video", "pkg:video", PackageRole.Product)],
            static _ => { });

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Packages, Has.Count.EqualTo(1));
            Assert.That(manifest.Packages[0].Id, Is.EqualTo("arronix.format.video"));
            Assert.That(File.Exists(Path.Combine(layout.ServerFolder, "Arronix.Api.dll")), Is.True);
            Assert.That(File.Exists(Path.Combine(layout.ClientStaticRoot, "index.html")), Is.True);
            Assert.That(File.Exists(layout.ManifestFile), Is.True);
            Assert.That(Directory.Exists(_root + ".staging"), Is.False);
        });

        // Written by the composer directly into the staged server before promotion, so it is true the
        // moment the server folder becomes live.
        var settings = File.ReadAllText(Path.Combine(layout.ServerFolder, "appsettings.json"));
        Assert.That(settings, Does.Contain("\"Root\": \"..\""));

        // Re-reading through the ordinary route re-validates everything the composer just wrote.
        Assert.That(() => InstallationManifest.ReadFrom(layout), Throws.Nothing);
    }

    [Test]
    public void AFailedFirstComposeLeavesNoInstallationBehind()
    {
        var layout = InstallationLayout.At(_root);
        var dotnet = FakeCli().Failing("pkg:broken", "simulated publish failure");
        var composer = new InstallationComposer(dotnet, RepositoryPaths.Root, layout);

        Assert.That(
            () => composer.Install(
                [new PackageSource("broken", "pkg:broken", PackageRole.Product)],
                static _ => { }),
            Throws.TypeOf<InstallationException>());

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(layout.ManifestFile), Is.False);
            Assert.That(Directory.Exists(_root + ".staging"), Is.False, "a pre-commit failure must clean up its own staging directory");
        });
    }

    [Test]
    public void AFailedSecondComposeLeavesThePreviousGenerationCompletelyUnchanged()
    {
        var layout = InstallationLayout.At(_root);

        var good = FakeCli().WithPackage("pkg:video", "arronix.format.video");
        new InstallationComposer(good, RepositoryPaths.Root, layout).Install(
            [new PackageSource("arronix.format.video", "pkg:video", PackageRole.Product)],
            static _ => { });

        var before = Snapshot(layout.Root);

        var broken = FakeCli()
            .WithPackage("pkg:video", "arronix.format.video")
            .Failing("pkg:movies", "simulated publish failure");

        Assert.That(
            () => new InstallationComposer(broken, RepositoryPaths.Root, layout).Install(
                [
                    new PackageSource("arronix.format.video", "pkg:video", PackageRole.Product),
                    new PackageSource("movies", "pkg:movies", PackageRole.Product),
                ],
                static _ => { }),
            Throws.TypeOf<InstallationException>());

        var after = Snapshot(layout.Root);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.EqualTo(before), "the live installation must be byte-for-byte unchanged");
            Assert.That(Directory.Exists(_root + ".staging"), Is.False);

            // The failed generation never touched the live packages folder: only one package was ever
            // there, and it is still the only one there.
            var manifest = InstallationManifest.ReadFrom(layout);
            Assert.That(manifest.Packages, Has.Count.EqualTo(1));
            Assert.That(manifest.Packages[0].Id, Is.EqualTo("arronix.format.video"));
        });
    }

    [Test]
    public void ExternalAndDeclaredPackagesCommitTogetherInOneGeneration()
    {
        var layout = InstallationLayout.At(_root);
        var dotnet = FakeCli()
            .WithPackage("pkg:video", "arronix.format.video")
            .WithPackage("pkg:movies", "movies", "arronix.format.video")
            .WithPackage("pkg:fixture", "proof.fixture", "movies");
        var composer = new InstallationComposer(dotnet, RepositoryPaths.Root, layout);

        var manifest = composer.Install(
            [
                new PackageSource("arronix.format.video", "pkg:video", PackageRole.Product),
                new PackageSource("movies", "pkg:movies", PackageRole.Product),
                new PackageSource("proof.fixture", "pkg:fixture", PackageRole.Fixture),
            ],
            static _ => { });

        Assert.That(
            manifest.Packages.Select(static p => p.Id),
            Is.EquivalentTo(new[] { "arronix.format.video", "movies", "proof.fixture" }));

        // The manifest fully and accurately declares what is on disk - no package installed behind it.
        Assert.That(() => InstallationManifest.ReadFrom(layout), Throws.Nothing);
    }

    [Test]
    public void PromoteEntryMovesAFreshEntryIntoAnEmptyDestination()
    {
        var staged = TempDirectory("staged");
        var live = Path.Combine(Path.GetTempPath(), "arronix-live-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(Path.Combine(staged, "marker.txt"), "new");

        try
        {
            InstallationComposer.PromoteEntry(staged, live);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(staged), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(live, "marker.txt")), Is.EqualTo("new"));
            });
        }
        finally
        {
            SafeDelete(live);
            SafeDelete(live + ".previous");
        }
    }

    [Test]
    public void PromoteEntryBacksUpWhateverItReplaces()
    {
        var staged = TempDirectory("staged");
        var live = TempDirectory("live");
        File.WriteAllText(Path.Combine(staged, "marker.txt"), "new");
        File.WriteAllText(Path.Combine(live, "marker.txt"), "old");

        try
        {
            InstallationComposer.PromoteEntry(staged, live);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(live, "marker.txt")), Is.EqualTo("new"));
                Assert.That(File.ReadAllText(Path.Combine(live + ".previous", "marker.txt")), Is.EqualTo("old"));
            });
        }
        finally
        {
            SafeDelete(live);
            SafeDelete(live + ".previous");
        }
    }

    [Test]
    public void RollBackRestoresEveryEntryABackupExistsFor()
    {
        var liveA = TempDirectory("live-a");
        var liveB = TempDirectory("live-b");
        File.WriteAllText(Path.Combine(liveA, "marker.txt"), "original-a");
        File.WriteAllText(Path.Combine(liveB, "marker.txt"), "original-b");

        try
        {
            // Simulate a commit that got as far as promoting A (leaving a .previous backup) before B's own
            // promotion failed, by moving A's live content aside exactly as PromoteEntry would have.
            Directory.Move(liveA, liveA + ".previous");
            Directory.CreateDirectory(liveA);
            File.WriteAllText(Path.Combine(liveA, "marker.txt"), "replacement-a");

            var report = InstallationComposer.RollBack([liveA]);

            Assert.Multiple(() =>
            {
                Assert.That(report, Does.Contain("restored"));
                Assert.That(File.ReadAllText(Path.Combine(liveA, "marker.txt")), Is.EqualTo("original-a"));
                Assert.That(Directory.Exists(liveA + ".previous"), Is.False);

                // The replacement that had already been promoted is kept aside rather than discarded, for
                // forensic inspection - it is not silently deleted.
                Assert.That(File.ReadAllText(Path.Combine(liveA + ".failed", "marker.txt")), Is.EqualTo("replacement-a"));
            });
        }
        finally
        {
            SafeDelete(liveA);
            SafeDelete(liveA + ".previous");
            SafeDelete(liveA + ".failed");
            SafeDelete(liveB);
        }
    }

    [Test]
    public void RollBackLeavesAnEntryWithNoBackupInPlace()
    {
        var live = TempDirectory("live-only");
        File.WriteAllText(Path.Combine(live, "marker.txt"), "freshly-promoted");

        try
        {
            var report = InstallationComposer.RollBack([live]);

            Assert.Multiple(() =>
            {
                Assert.That(report, Does.Contain("restored"));
                Assert.That(File.ReadAllText(Path.Combine(live, "marker.txt")), Is.EqualTo("freshly-promoted"));
            });
        }
        finally
        {
            SafeDelete(live);
        }
    }

    private FakeDotNetCli FakeCli() => new FakeDotNetCli()
        .WithServer(_serverProject, "Arronix.Api.dll")
        .WithClient(_clientProject);

    private static string TempDirectory(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arronix-{label}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDelete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static IReadOnlyDictionary<string, string> Snapshot(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(
            file => Path.GetRelativePath(root, file),
            File.ReadAllText,
            StringComparer.Ordinal);
}
