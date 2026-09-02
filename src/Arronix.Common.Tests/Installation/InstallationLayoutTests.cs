using System;
using System.IO;
using Arronix.Common.Installation;

namespace Arronix.Common.Tests.Installation;

/// <summary>
/// The layout is the one description of what an installation looks like on disk.
/// </summary>
/// <remarks>
/// Every consumer of it — the composer that writes an installation, the server that runs inside one, and
/// the reset that empties one — has to agree about the same folders. These cases pin the agreement to this
/// type rather than to whoever wrote the caller.
/// </remarks>
[TestFixture]
public class InstallationLayoutTests
{
    [Test]
    public void EveryPathIsBeneathTheRoot()
    {
        var layout = InstallationLayout.At(Path.Combine(Path.GetTempPath(), "arronix-layout"));

        Assert.Multiple(() =>
        {
            Assert.That(layout.ServerFolder, Is.EqualTo(Path.Combine(layout.Root, "server")));
            Assert.That(layout.ClientFolder, Is.EqualTo(Path.Combine(layout.Root, "client")));
            Assert.That(layout.ClientStaticRoot, Is.EqualTo(Path.Combine(layout.Root, "client", "wwwroot")));
            Assert.That(layout.PackagesFolder, Is.EqualTo(Path.Combine(layout.Root, "packages")));
            Assert.That(layout.PackageStateFolder, Is.EqualTo(Path.Combine(layout.Root, "package-state")));
            Assert.That(layout.StateFolder, Is.EqualTo(Path.Combine(layout.Root, "state")));
            Assert.That(layout.StoreDataSource, Is.EqualTo(Path.Combine(layout.Root, "state", "arronix.db")));
            Assert.That(layout.ManifestFile, Is.EqualTo(Path.Combine(layout.Root, "installation.json")));
        });
    }

    [Test]
    public void ARelativeRootResolvesAgainstTheBasePathItWasGiven()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "arronix-base");

        var layout = InstallationLayout.At("..", Path.Combine(basePath, "server"));

        Assert.That(layout.Root, Is.EqualTo(Path.GetFullPath(basePath)));
    }

    [Test]
    public void ARootedRootIgnoresTheBasePath()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "arronix-absolute");

        var layout = InstallationLayout.At(absolute, Path.GetTempPath());

        Assert.That(layout.Root, Is.EqualTo(Path.GetFullPath(absolute)));
    }

    [Test]
    public void AnEmptyRootIsRefused()
        => Assert.That(() => InstallationLayout.At("  "), Throws.ArgumentException);

    [Test]
    public void APackageOccupiesExactlyOneFolderInsideThePackagesFolder()
    {
        var layout = InstallationLayout.At(Path.Combine(Path.GetTempPath(), "arronix-packages"));

        Assert.That(
            layout.PackageFolder("sample.movie.catalog"),
            Is.EqualTo(Path.Combine(layout.PackagesFolder, "sample.movie.catalog")));
    }

    /// <remarks>
    /// A package identifier reaches this type from a manifest, and a manifest is a file. An identifier that
    /// escaped the packages folder would let a composition write outside the installation it was given.
    /// </remarks>
    [TestCase("../elsewhere")]
    [TestCase("nested/deeper")]
    [TestCase("..")]
    public void APackageIdentifierThatEscapesThePackagesFolderIsRefused(string packageId)
    {
        var layout = InstallationLayout.At(Path.Combine(Path.GetTempPath(), "arronix-escape"));

        Assert.That(
            () => layout.PackageFolder(packageId),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void TheRootAndEverythingBeneathItAreRecognizedAsOwned()
    {
        var layout = InstallationLayout.At(Path.Combine(Path.GetTempPath(), "arronix-owned"));

        Assert.Multiple(() =>
        {
            Assert.That(layout.Contains(layout.Root), Is.True);
            Assert.That(layout.Contains(layout.StateFolder), Is.True);
            Assert.That(layout.Contains(Path.Combine(layout.PackagesFolder, "movies")), Is.True);
            Assert.That(layout.Contains(Path.GetTempPath()), Is.False);
            Assert.That(layout.Contains(layout.Root + "-other"), Is.False);
            Assert.That(layout.Contains("   "), Is.False);
        });
    }
}
