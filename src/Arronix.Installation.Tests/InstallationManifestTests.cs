using System;
using System.IO;
using Arronix.Common.Installation;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>
/// The manifest is validated against what is actually on disk, not trusted at face value. Every case here
/// builds its own minimal fixture directly, without a real publish, because these are all pure structural
/// checks over files this test writes itself.
/// </summary>
[TestFixture]
internal sealed class InstallationManifestTests
{
    private string _root = string.Empty;
    private InstallationLayout _layout = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-manifest-" + Guid.NewGuid().ToString("N"));
        _layout = InstallationLayout.At(_root);
        Directory.CreateDirectory(_layout.ServerFolder);
        Directory.CreateDirectory(_layout.ClientStaticRoot);
        Directory.CreateDirectory(_layout.PackagesFolder);
        File.WriteAllBytes(Path.Combine(_layout.ServerFolder, "Arronix.Api.dll"), []);
        File.WriteAllText(Path.Combine(_layout.ClientStaticRoot, "index.html"), "<html></html>");
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
    public void AValidManifestRoundTrips()
    {
        WritePackage("movies", []);
        (Valid() with { Packages = [Package("movies")] }).WriteTo(_layout);

        Assert.That(() => InstallationManifest.ReadFrom(_layout), Throws.Nothing);
    }

    [Test]
    public void AnUnknownSchemaVersionIsRefused()
    {
        (Valid() with { SchemaVersion = InstallationManifest.CurrentSchemaVersion + 1 }).WriteTo(_layout);

        Assert.That(() => InstallationManifest.ReadFrom(_layout), Throws.TypeOf<InstallationException>());
    }

    [TestCase("../elsewhere")]
    [TestCase("/etc/passwd")]
    [TestCase("state")]
    public void AnUnexpectedServerFolderIsRefused(string declared)
    {
        (Valid() with { ServerFolder = declared }).WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>().With.Message.Contains("server folder"));
    }

    [Test]
    public void AMissingServerEntryAssemblyIsRefused()
    {
        File.Delete(Path.Combine(_layout.ServerFolder, "Arronix.Api.dll"));
        Valid().WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>().With.Message.Contains("no '"));
    }

    [Test]
    public void AMissingClientIndexIsRefused()
    {
        File.Delete(Path.Combine(_layout.ClientStaticRoot, "index.html"));
        Valid().WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>().With.Message.Contains("no published client"));
    }

    [Test]
    public void APackageWhoseOwnManifestDeclaresADifferentIdIsRefused()
    {
        var folder = Path.Combine(_layout.PackagesFolder, "movies");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            """{ "id": "not-movies", "dependencies": [] }""");

        (Valid() with { Packages = [Package("movies")] }).WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>().With.Message.Contains("does not describe what would actually run"));
    }

    [Test]
    public void APackageDeclaringAnUnsatisfiedDependencyIsRefused()
    {
        WritePackage("movies", ["arronix.format.video"]);

        (Valid() with { Packages = [Package("movies")] }).WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>().With.Message.Contains("dependency graph is incomplete"));
    }

    [Test]
    public void APackageOnDiskThatTheManifestDoesNotDeclareIsRefused()
    {
        // A package physically installed beside the declared ones, but never named in the manifest - the
        // exact shape a manifest must never allow, because it would no longer describe what would run.
        WritePackage("movies", []);
        WritePackage("undeclared.extra", []);

        (Valid() with { Packages = [Package("movies")] }).WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>().With.Message.Contains("undeclared.extra"));
    }

    [Test]
    public void ADeclaredPackageFolderThatEscapesThePackagesFolderIsRefused()
    {
        (Valid() with
        {
            Packages =
            [
                new InstalledPackage("movies", "Movies", "0.1.0", "Arronix.Plugin.Movies", "../elsewhere", PackageRole.Product),
            ],
        }).WriteTo(_layout);

        Assert.That(
            () => InstallationManifest.ReadFrom(_layout),
            Throws.TypeOf<InstallationException>());
    }

    [Test]
    public void AMalformedManifestFileIsRefused()
    {
        File.WriteAllText(_layout.ManifestFile, "{ not json");

        Assert.That(() => InstallationManifest.ReadFrom(_layout), Throws.TypeOf<InstallationException>());
    }

    [Test]
    public void AMissingManifestIsRefused()
        => Assert.That(() => InstallationManifest.ReadFrom(_layout), Throws.TypeOf<InstallationException>());

    private void WritePackage(string id, string[] dependencies)
    {
        var folder = Path.Combine(_layout.PackagesFolder, id);
        Directory.CreateDirectory(folder);

        var dependencyJson = string.Join(
            ",",
            Array.ConvertAll(dependencies, d => $$"""{"package":"{{d}}","range":">=0.1 <0.2"}"""));

        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            $$"""{ "id": "{{id}}", "dependencies": [{{dependencyJson}}] }""");
    }

    private static InstalledPackage Package(string id)
        => new(id, id, "0.1.0", "SomeProject", "packages/" + id, PackageRole.Product);

    private InstallationManifest Valid() => new(
        InstallationManifest.CurrentSchemaVersion,
        "test-sdk",
        "server",
        "Arronix.Api.dll",
        "client/wwwroot",
        "state/arronix.db",
        "package-state",
        []);
}
