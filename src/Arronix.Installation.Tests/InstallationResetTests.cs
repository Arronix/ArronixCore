using System;
using System.IO;
using Arronix.Common.Installation;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>
/// <see cref="InstallationReset"/> is the one place reset ownership and deletion are decided; <c>Program</c>
/// only reports what it returns. Every case here builds its own minimal fixture directly on a real temporary
/// directory and goes through real <see cref="InstallationManifest"/> validation — never a real publish and
/// never an assertion over source text.
/// </summary>
[TestFixture]
internal sealed class InstallationResetTests
{
    private string _root = string.Empty;
    private InstallationLayout _layout = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-reset-" + Guid.NewGuid().ToString("N"));
        _layout = InstallationLayout.At(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void NoInstallationRefusesWithoutDeletingAnything()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "unrelated.txt"), "keep me");

        Assert.That(
            () => InstallationReset.Execute(_layout, resetEverything: false),
            Throws.TypeOf<InstallationException>());

        Assert.That(File.Exists(Path.Combine(_root, "unrelated.txt")), Is.True);
    }

    [Test]
    public void AMissingRootRefusesWithoutCreatingAnything()
    {
        Assert.That(
            () => InstallationReset.Execute(_layout, resetEverything: false),
            Throws.TypeOf<InstallationException>());

        Assert.That(Directory.Exists(_root), Is.False);
    }

    [Test]
    public void AMalformedManifestRefusesWithoutDeletingAnything()
    {
        BuildValidInstallation();
        WriteState("state-only-file", "durable state");
        File.WriteAllText(_layout.ManifestFile, "{ this is not valid json");

        Assert.That(
            () => InstallationReset.Execute(_layout, resetEverything: false),
            Throws.TypeOf<InstallationException>());

        Assert.That(File.Exists(Path.Combine(_layout.StateFolder, "state-only-file")), Is.True);
    }

    [Test]
    public void ADriftedManifestRefusesWithoutDeletingAnything()
    {
        // The manifest itself is well-formed JSON of the right schema, but it no longer describes what is
        // actually on disk: the client index it claims to have published is gone.
        BuildValidInstallation();
        WriteState("state-only-file", "durable state");
        File.Delete(Path.Combine(_layout.ClientStaticRoot, "index.html"));

        Assert.That(
            () => InstallationReset.Execute(_layout, resetEverything: false),
            Throws.TypeOf<InstallationException>());

        Assert.That(File.Exists(Path.Combine(_layout.StateFolder, "state-only-file")), Is.True);
    }

    [Test]
    public void ANarrowResetRemovesOnlyStateAndPackageState()
    {
        BuildValidInstallation();
        WriteState("db.sqlite", "durable state");
        Directory.CreateDirectory(_layout.PackageStateFolder);
        File.WriteAllText(Path.Combine(_layout.PackageStateFolder, "scratch.txt"), "package scratch");

        var outcome = InstallationReset.Execute(_layout, resetEverything: false);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Removed, Is.EquivalentTo(new[] { _layout.StateFolder, _layout.PackageStateFolder }));
            Assert.That(outcome.Remaining, Is.Empty);
            Assert.That(Directory.Exists(_layout.StateFolder), Is.False);
            Assert.That(Directory.Exists(_layout.PackageStateFolder), Is.False);

            // Everything a narrow reset does not own survives untouched.
            Assert.That(Directory.Exists(_layout.ServerFolder), Is.True);
            Assert.That(Directory.Exists(_layout.ClientFolder), Is.True);
            Assert.That(Directory.Exists(_layout.PackagesFolder), Is.True);
            Assert.That(File.Exists(_layout.ManifestFile), Is.True);
        });
    }

    [Test]
    public void ANarrowResetToleratesEitherPathBeingAbsent()
    {
        BuildValidInstallation();

        // Neither state nor package-state exists yet; a reset before anything ever ran must not throw.
        Assert.That(Directory.Exists(_layout.StateFolder), Is.False);
        Assert.That(Directory.Exists(_layout.PackageStateFolder), Is.False);

        ResetOutcome outcome = null!;
        Assert.That(() => outcome = InstallationReset.Execute(_layout, resetEverything: false), Throws.Nothing);
        Assert.That(outcome.Removed, Is.Empty);
    }

    [Test]
    public void ResetAllDeletesOnlyTheFiniteToolOwnedEntries()
    {
        BuildValidInstallation();
        WriteState("db.sqlite", "durable state");
        Directory.CreateDirectory(_layout.PackageStateFolder);
        File.WriteAllText(Path.Combine(_layout.PackageStateFolder, "scratch.txt"), "package scratch");

        var outcome = InstallationReset.Execute(_layout, resetEverything: true);

        var expected = new[]
        {
            _layout.ServerFolder,
            _layout.ClientFolder,
            _layout.PackagesFolder,
            _layout.PackageStateFolder,
            _layout.StateFolder,
            _layout.ManifestFile,
            _layout.Root,
        };

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Removed, Is.EquivalentTo(expected));
            Assert.That(outcome.Remaining, Is.Empty);

            // The whole tree this tool ever wrote is gone, including the now-empty root.
            Assert.That(Directory.Exists(_root), Is.False);
        });
    }

    [Test]
    public void UnrelatedRootEntriesSurviveResetAllAndAreReported()
    {
        BuildValidInstallation();
        var unrelatedFile = Path.Combine(_root, "notes.txt");
        var unrelatedFolder = Path.Combine(_root, "backup");
        File.WriteAllText(unrelatedFile, "keep me");
        Directory.CreateDirectory(unrelatedFolder);
        File.WriteAllText(Path.Combine(unrelatedFolder, "inner.txt"), "keep me too");

        var outcome = InstallationReset.Execute(_layout, resetEverything: true);

        Assert.Multiple(() =>
        {
            // Everything this tool owns is still removed even though the root holds something else.
            Assert.That(outcome.Removed, Does.Contain(_layout.ServerFolder));
            Assert.That(outcome.Removed, Does.Contain(_layout.ManifestFile));

            // The root itself is never removed while an entry it does not own remains.
            Assert.That(outcome.Removed, Does.Not.Contain(_layout.Root));
            Assert.That(Directory.Exists(_root), Is.True);

            // Reported by name so an operator can see exactly what was left behind.
            Assert.That(outcome.Remaining, Is.EquivalentTo(new[] { unrelatedFile, unrelatedFolder }));
            Assert.That(File.Exists(unrelatedFile), Is.True);
            Assert.That(File.Exists(Path.Combine(unrelatedFolder, "inner.txt")), Is.True);
        });
    }

    [Test]
    public void ANarrowResetNeverRemovesTheRootOrReportsWhatRemains()
    {
        // Ownership must be proved before anything is deleted, which itself requires the server and client
        // a narrow reset never touches; the root can therefore never become empty under a narrow reset, and
        // the code never even asks whether it did — remaining is always empty and the root always survives.
        BuildValidInstallation();
        WriteState("db.sqlite", "state");

        var outcome = InstallationReset.Execute(_layout, resetEverything: false);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Remaining, Is.Empty, "a narrow reset never computes what remains in the root");
            Assert.That(Directory.Exists(_root), Is.True, "only reset --all may remove the root");
            Assert.That(Directory.Exists(_layout.ServerFolder), Is.True);
        });
    }

    [Test]
    public void TheRootIsRemovedOnlyOnceItIsProvablyEmpty()
    {
        BuildValidInstallation();

        var outcome = InstallationReset.Execute(_layout, resetEverything: true);

        Assert.That(outcome.Removed, Does.Contain(_layout.Root));
        Assert.That(Directory.Exists(_root), Is.False);
    }

    [Test]
    public void AnEscapedOwnedPathCannotBeDeleted()
    {
        BuildValidInstallation();

        var outside = Path.Combine(Path.GetTempPath(), "arronix-reset-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);

        try
        {
            Assert.That(_layout.Contains(outside), Is.False);

            // This is the exact guard InstallationReset.Execute runs against every owned target before
            // deleting it; proving it directly here shows a path outside the installation can never pass,
            // whatever future change to the owned set might otherwise compute one.
            Assert.That(
                () => InstallationReset.RequireOwned(_layout, outside),
                Throws.TypeOf<InstallationException>());

            Assert.That(Directory.Exists(outside), Is.True);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void AnInvalidRelativeOwnedPathCannotBeDeleted()
    {
        BuildValidInstallation();

        var escaping = Path.Combine(_root, "..", "arronix-reset-escaped");

        Assert.That(
            () => InstallationReset.RequireOwned(_layout, escaping),
            Throws.TypeOf<InstallationException>());
    }

    [Test]
    public void AnOwnedPathInsideTheInstallationIsAccepted()
    {
        BuildValidInstallation();

        Assert.That(() => InstallationReset.RequireOwned(_layout, _layout.StateFolder), Throws.Nothing);
        Assert.That(() => InstallationReset.RequireOwned(_layout, _layout.Root), Throws.Nothing);
    }

    private void WriteState(string fileName, string content)
    {
        Directory.CreateDirectory(_layout.StateFolder);
        File.WriteAllText(Path.Combine(_layout.StateFolder, fileName), content);
    }

    private void BuildValidInstallation()
    {
        Directory.CreateDirectory(_layout.ServerFolder);
        Directory.CreateDirectory(_layout.ClientStaticRoot);
        Directory.CreateDirectory(_layout.PackagesFolder);
        File.WriteAllBytes(Path.Combine(_layout.ServerFolder, "Arronix.Api.dll"), []);
        File.WriteAllText(Path.Combine(_layout.ClientStaticRoot, "index.html"), "<html></html>");

        var manifest = new InstallationManifest(
            InstallationManifest.CurrentSchemaVersion,
            "test-sdk",
            "server",
            "Arronix.Api.dll",
            "client/wwwroot",
            "state/arronix.db",
            "package-state",
            []);

        manifest.WriteTo(_layout);
    }
}
