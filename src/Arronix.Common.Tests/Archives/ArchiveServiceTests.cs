using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Common.Archives;

namespace Arronix.Common.Tests.Archives;

/// <summary>
/// Covers packing and unpacking: format detection, the round trip, and the failures a caller has to be able
/// to tell apart.
/// </summary>
[TestFixture]
public class ArchiveServiceTests : ArchiveFixture
{
    [Test]
    public void Constructor_RejectsAMissingLogger()
    {
        Assert.That(() => new ArchiveService(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public async Task CreateZipAsync_StoresEachFileUnderItsOwnNameWithNoFolders()
    {
        var first = WriteScratchFile("config/settings.json", "{}");
        var second = WriteScratchFile("logs/today.txt", "hello");
        var archivePath = Path.Combine(Scratch, "backup.zip");

        await Service.CreateZipAsync(archivePath, [first, second]);

        using var archive = ZipFile.OpenRead(archivePath);

        Assert.That(
            archive.Entries.Select(static entry => entry.FullName).ToArray(),
            Is.EquivalentTo(new[] { "settings.json", "today.txt" }));
    }

    [Test]
    public async Task CreateZipAsync_RoundTripsContentThroughExtraction()
    {
        var source = WriteScratchFile("notes.txt", "the original content");
        var archivePath = Path.Combine(Scratch, "backup.zip");

        await Service.CreateZipAsync(archivePath, [source]);
        await Service.ExtractAsync(archivePath, Destination);

        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(Destination, "notes.txt")),
            Is.EqualTo("the original content"));
    }

    /// <summary>
    /// Flattening the paths means two files from different folders can want the same entry name. Storing
    /// both would leave an archive whose second entry silently shadows the first when it is unpacked.
    /// </summary>
    [Test]
    public void CreateZipAsync_RefusesTwoFilesThatWouldClaimOneEntryName()
    {
        var first = WriteScratchFile("a/settings.json", "1");
        var second = WriteScratchFile("b/settings.json", "2");
        var archivePath = Path.Combine(Scratch, "clash.zip");

        Assert.That(
            async () => await Service.CreateZipAsync(archivePath, [first, second]),
            Throws.InstanceOf<IOException>());
    }

    [Test]
    public void CreateZipAsync_RejectsMissingArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.CreateZipAsync(" ", []),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await Service.CreateZipAsync(Path.Combine(Scratch, "x.zip"), null!),
                Throws.TypeOf<ArgumentNullException>());
        });
    }

    [Test]
    public void CreateZipAsync_ObservesCancellation()
    {
        var source = WriteScratchFile("notes.txt", "content");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await Service.CreateZipAsync(
                Path.Combine(Scratch, "canceled.zip"),
                [source],
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ExtractAsync_UnpacksAGzippedTarball()
    {
        var archive = WriteGzippedTar(
            "package.tar.gz",
            (TarEntryType.Directory, "inner"),
            (TarEntryType.RegularFile, "inner/file.txt"));

        await Service.ExtractAsync(archive, Destination);

        Assert.That(File.Exists(Path.Combine(Destination, "inner", "file.txt")), Is.True);
    }

    [Test]
    public async Task ExtractAsync_CreatesAZeroLengthFileRatherThanSkippingIt()
    {
        var archive = WriteGzippedTarWithEmptyFile("empty.tar.gz", "marker");

        await Service.ExtractAsync(archive, Destination);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(Destination, "marker")), Is.True);
            Assert.That(new FileInfo(Path.Combine(Destination, "marker")).Length, Is.Zero);
        });
    }

    [Test]
    public async Task ExtractAsync_CreatesFoldersAnEntryImpliesButDoesNotDeclare()
    {
        var archive = WriteZip("implied.zip", "one/two/three.txt");

        await Service.ExtractAsync(archive, Destination);

        Assert.That(File.Exists(Path.Combine(Destination, "one", "two", "three.txt")), Is.True);
    }

    [Test]
    public async Task ExtractAsync_ReplacesAFileAlreadyInTheDestination()
    {
        var archive = WriteZip("overwrite.zip", "readme.txt");
        await File.WriteAllTextAsync(Path.Combine(Destination, "readme.txt"), "stale");

        await Service.ExtractAsync(archive, Destination);

        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(Destination, "readme.txt")),
            Is.EqualTo("content of readme.txt"));
    }

    /// <summary>
    /// The implementation this replaces treated anything that was not a zip as a tarball, so a truncated
    /// download or an error page saved under the expected name failed part-way through unpacking over a
    /// running installation instead of failing where it was fetched.
    /// </summary>
    [TestCase("update.rar")]
    [TestCase("update.7z")]
    [TestCase("update")]
    [TestCase("update.bin")]
    public void ExtractAsync_RefusesAFormatItDoesNotRead(string fileName)
    {
        var path = WriteScratchFile(fileName, "not an archive");

        Assert.That(
            async () => await Service.ExtractAsync(path, Destination),
            Throws.TypeOf<NotSupportedException>());
    }

    [TestCase("package.zip")]
    [TestCase("package.tar")]
    [TestCase("package.tar.gz")]
    [TestCase("package.tgz")]
    [TestCase("PACKAGE.TGZ")]
    public void ExtractAsync_ReportsAMissingArchiveRatherThanARejectedFormat(string fileName)
    {
        Assert.That(
            async () => await Service.ExtractAsync(Path.Combine(Scratch, fileName), Destination),
            Throws.TypeOf<FileNotFoundException>());
    }

    [Test]
    public void ExtractAsync_ReportsADamagedArchive()
    {
        var path = WriteScratchFile("damaged.zip", "this is not a zip file at all");

        Assert.That(
            async () => await Service.ExtractAsync(path, Destination),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void ExtractAsync_RejectsMissingArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await Service.ExtractAsync(" ", Destination),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await Service.ExtractAsync(Path.Combine(Scratch, "x.zip"), " "),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void ExtractAsync_ObservesCancellation()
    {
        var archive = WriteZip("honest.zip", "readme.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await Service.ExtractAsync(archive, Destination, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private string WriteScratchFile(string relativePath, string content)
    {
        var path = Path.Combine(Scratch, relativePath);
        var folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, content);

        return path;
    }
}
