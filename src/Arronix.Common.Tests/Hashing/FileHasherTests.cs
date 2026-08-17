using System;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Common.Hashing;

namespace Arronix.Common.Tests.Hashing;

/// <summary>
/// Covers the file hasher: that the purpose selects the primitive, that both primitives agree with the
/// framework's own implementation of them, and that reading is cancellable.
/// </summary>
[TestFixture]
public class FileHasherTests
{
    private const string Path = "/library/artwork.bin";

    private static readonly byte[] Content = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");

    private InMemoryFileSystem _fileSystem = null!;
    private FileHasher _hasher = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new InMemoryFileSystem();
        _fileSystem.Add(Path, Content);
        _hasher = new FileHasher(_fileSystem);
    }

    [Test]
    public async Task ComputeAsync_DefaultsToTheFastPrimitive()
    {
        var digest = await _hasher.ComputeAsync(Path);

        Assert.That(digest, Is.EqualTo(XxHash128.Hash(Content)));
    }

    [Test]
    public async Task ComputeAsync_UsesACryptographicPrimitiveWhenIntegrityIsAsked()
    {
        var digest = await _hasher.ComputeAsync(Path, FileHashPurpose.Integrity);

        Assert.Multiple(() =>
        {
            Assert.That(digest, Has.Length.EqualTo(32));
            Assert.That(digest, Is.EqualTo(SHA256.HashData(Content)));
        });
    }

    [Test]
    public async Task ComputeAsync_GivesDifferentDigestsForTheTwoPurposes()
    {
        var fast = await _hasher.ComputeAsync(Path);
        var cryptographic = await _hasher.ComputeAsync(Path, FileHashPurpose.Integrity);

        Assert.That(fast, Is.Not.EqualTo(cryptographic));
    }

    [Test]
    public async Task ComputeAsync_DistinguishesFilesThatDifferByOneByte()
    {
        var altered = (byte[])Content.Clone();
        altered[^1] ^= 0x01;
        _fileSystem.Add("/library/other.bin", altered);

        var first = await _hasher.ComputeAsync(Path);
        var second = await _hasher.ComputeAsync("/library/other.bin");

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public async Task ComputeAsync_ReadsAStreamFromItsCurrentPosition()
    {
        using var stream = new MemoryStream(Content, writable: false);
        stream.Position = 4;

        var digest = await _hasher.ComputeAsync(stream);

        Assert.That(digest, Is.EqualTo(XxHash128.Hash(Content.AsSpan(4))));
    }

    [Test]
    public async Task ComputeAsync_HandlesAnEmptyFile()
    {
        _fileSystem.Add("/library/empty.bin", []);

        var digest = await _hasher.ComputeAsync("/library/empty.bin");

        Assert.That(digest, Is.EqualTo(XxHash128.Hash([])));
    }

    [Test]
    public void ComputeAsync_RejectsAnEmptyPath()
    {
        Assert.That(
            async () => await _hasher.ComputeAsync("   "),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ComputeAsync_RejectsAMissingStream()
    {
        Assert.That(
            async () => await _hasher.ComputeAsync((Stream)null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void ComputeAsync_RejectsAPurposeItCannotSatisfy()
    {
        using var stream = new MemoryStream(Content, writable: false);

        Assert.That(
            async () => await _hasher.ComputeAsync(stream, (FileHashPurpose)99),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComputeAsync_ReportsAMissingFile()
    {
        Assert.That(
            async () => await _hasher.ComputeAsync("/library/absent.bin"),
            Throws.TypeOf<FileNotFoundException>());
    }

    [Test]
    public void ComputeAsync_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await _hasher.ComputeAsync(Path, FileHashPurpose.ChangeDetection, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(
                async () => await _hasher.ComputeAsync(Path, FileHashPurpose.Integrity, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        });
    }
}
