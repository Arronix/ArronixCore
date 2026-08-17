using System.Formats.Tar;
using System.IO;
using System.Threading.Tasks;

namespace Arronix.Common.Tests.Archives;

/// <summary>
/// Proves that an archive cannot write outside the folder it is extracted into.
/// </summary>
/// <remarks>
/// <para>
/// The implementation this replaces combined each entry's recorded name with the destination folder and
/// wrote, with no check at all. An entry named <c>../../something</c> therefore escaped the destination and
/// overwrote whatever the process could reach — a self-update or an extension package, both of which arrive
/// over the network, could rewrite the host's own binaries or configuration. This fixture exists so that the
/// defect cannot reappear: every test here fails against that behavior.
/// </para>
/// <para>
/// Each test asserts two things — that the extraction is refused, and that nothing was written outside the
/// destination. The second assertion is the one that matters: an extractor that threw only after writing the
/// escaping file would still have been exploited.
/// </para>
/// </remarks>
[TestFixture]
public class ZipSlipTests : ArchiveFixture
{
    [Test]
    public void ExtractAsync_RefusesAZipEntryThatClimbsOutOfTheDestination()
    {
        var archive = WriteZip("traversal.zip", "../escaped.txt");

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, Destination),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(Scratch, "escaped.txt")), Is.False);
        });
    }

    [Test]
    public void ExtractAsync_RefusesAZipEntryThatClimbsSeveralLevels()
    {
        // The destination is nested so that the climb lands inside the scratch folder, where the test can
        // see whether anything was written and the teardown can still clean it up.
        var nested = Path.Combine(Scratch, "one", "two", "three");
        Directory.CreateDirectory(nested);

        var archive = WriteZip("deep-traversal.zip", "../../../escaped.txt");

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, nested),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(Scratch, "escaped.txt")), Is.False);
        });
    }

    /// <summary>
    /// Archives written on Windows record a backslash as the separator. An extractor that only understands
    /// the forward slash treats <c>..\..\escaped.txt</c> as one oddly named file on Linux — and as a
    /// traversal on Windows, where it is not checked at all.
    /// </summary>
    [Test]
    public void ExtractAsync_RefusesAZipEntryThatClimbsUsingBackslashes()
    {
        var archive = WriteZip("backslash-traversal.zip", "..\\..\\escaped.txt");

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, Destination),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(Scratch, "escaped.txt")), Is.False);
        });
    }

    /// <summary>
    /// A destination check written as a plain string prefix comparison lets an entry escape into a sibling
    /// folder whose name merely starts with the destination's.
    /// </summary>
    [Test]
    public void ExtractAsync_RefusesAZipEntryThatEscapesIntoASiblingWithAMatchingPrefix()
    {
        var sibling = Destination + "-elsewhere";
        Directory.CreateDirectory(sibling);

        var archive = WriteZip("sibling.zip", "../destination-elsewhere/escaped.txt");

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, Destination),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(sibling, "escaped.txt")), Is.False);
        });
    }

    [Test]
    public void ExtractAsync_RefusesAZipEntryWithAnAbsolutePath()
    {
        var absolute = Path.Combine(Scratch, "absolute.txt").Replace('\\', '/');
        var archive = WriteZip("absolute.zip", absolute);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, Destination),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(Scratch, "absolute.txt")), Is.False);
        });
    }

    /// <summary>
    /// A traversal entry aborts the whole extraction, leaving nothing behind. An extractor that unpacked the
    /// legitimate entries first and only then refused would have half-applied an update it had already
    /// decided not to trust.
    /// </summary>
    [Test]
    public void ExtractAsync_WritesNothingAtAllWhenAnyEntryWouldEscape()
    {
        var archive = WriteZip("mixed.zip", "legitimate.txt", "../escaped.txt");

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, Destination),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(Destination, "legitimate.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(Scratch, "escaped.txt")), Is.False);
        });
    }

    [Test]
    public void ExtractAsync_RefusesATarEntryThatClimbsOutOfTheDestination()
    {
        var archive = WriteGzippedTar(
            "traversal.tar.gz",
            (TarEntryType.RegularFile, "../escaped.txt"));

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(archive, Destination),
                Throws.InstanceOf<IOException>());

            Assert.That(File.Exists(Path.Combine(Scratch, "escaped.txt")), Is.False);
        });
    }

    /// <summary>
    /// A symbolic link is the same escape by another route: the link itself lands inside the destination,
    /// but it points anywhere, and the next write through it goes wherever it points.
    /// </summary>
    [Test]
    public async Task ExtractAsync_DoesNotRecreateASymbolicLinkFromATarball()
    {
        var archive = WriteGzippedTar(
            "linked.tar.gz",
            (TarEntryType.RegularFile, "real.txt"),
            (TarEntryType.SymbolicLink, "link-to-hosts"));

        await Service.ExtractAsync(archive, Destination);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(Destination, "real.txt")), Is.True);
            Assert.That(Path.Exists(Path.Combine(Destination, "link-to-hosts")), Is.False);
        });
    }

    [Test]
    public async Task ExtractAsync_StillUnpacksAnHonestArchive()
    {
        var archive = WriteZip("honest.zip", "readme.txt", "nested/", "nested/inner.txt");

        await Service.ExtractAsync(archive, Destination);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(Destination, "readme.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(Destination, "nested", "inner.txt")), Is.True);
        });
    }

    [Test]
    public async Task ExtractAsync_CreatesTheDestinationWhenItDoesNotExist()
    {
        var archive = WriteZip("honest.zip", "readme.txt");
        var fresh = Path.Combine(Scratch, "not-yet-there");

        await Service.ExtractAsync(archive, fresh);

        Assert.That(File.Exists(Path.Combine(fresh, "readme.txt")), Is.True);
    }
}
