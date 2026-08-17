using System.IO;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Scoping;

#pragma warning disable ARX0005 // File-system contracts are experimental; these tests exercise the decorator.
#pragma warning disable ARX0014 // The extension model is experimental; these tests exercise it.

namespace Arronix.Plugins.Tests.Scoping;

/// <summary>
/// The confinement the file-system contract has always documented and nothing delivered until now.
/// </summary>
[TestFixture]
public sealed class ScopedFileSystemTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.storage");

    private string _root = string.Empty;
    private string _granted = string.Empty;
    private string _sibling = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("arronix-scope").FullName;
        _granted = Path.Combine(_root, "library");
        _sibling = Path.Combine(_root, "library-backup");
        Directory.CreateDirectory(_granted);
        Directory.CreateDirectory(_sibling);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ScopedFileSystem Create() => new(new ThrowingFileSystem(), Plugin, [_granted]);

    [Test]
    public void APathInsideAGrantedRootIsAllowedThrough()
    {
        var scoped = Create();

        scoped.IsWithinGrant(Path.Combine(_granted, "a", "b.mkv")).Should().BeTrue();
        scoped.IsWithinGrant(_granted).Should().BeTrue();
    }

    [Test]
    public void ASiblingWhoseNameMerelyStartsTheSameIsNotInsideTheGrant()
        => Create().IsWithinGrant(Path.Combine(_sibling, "a.mkv")).Should().BeFalse(
            "matching a prefix without a separator would leak a grant to every folder sharing its opening characters");

    [Test]
    public void ARelativeSegmentCannotWalkOutOfTheGrant()
        => Create().IsWithinGrant(Path.Combine(_granted, "..", "library-backup", "a.mkv")).Should().BeFalse();

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankPathIsOutsideEveryGrant(string? path)
        => Create().IsWithinGrant(path).Should().BeFalse();

    [Test]
    public void AnEmptyGrantConfinesTheExtensionToNothing()
    {
        var scoped = new ScopedFileSystem(new ThrowingFileSystem(), Plugin, []);

        scoped.GrantedRoots.Should().BeEmpty();
        scoped.IsWithinGrant(_granted).Should().BeFalse(
            "a privilege nobody configured grants nothing, which is the correct default");
    }

    [Test]
    public void EveryReadingMemberRefusesAPathOutsideTheGrant()
    {
        var scoped = Create();
        var outside = Path.Combine(_sibling, "a.mkv");

        ShouldRefuse(() => scoped.FileExists(outside));
        ShouldRefuse(() => scoped.FolderExists(outside));
        ShouldRefuse(() => scoped.GetFileSize(outside));
        ShouldRefuse(() => scoped.GetAvailableSpace(outside));
        ShouldRefuse(() => scoped.GetTotalSize(outside));
        ShouldRefuse(() => scoped.GetLastWriteTimeUtc(outside));
        ShouldRefuse(() => scoped.EnumerateFiles(outside));
        ShouldRefuse(() => scoped.EnumerateDirectories(outside));
        ShouldRefuse(() => scoped.OpenRead(outside));
        ShouldRefuse(() => scoped.IsFileLocked(outside));
        ShouldRefuse(() => scoped.GetMount(outside));
    }

    [Test]
    public void EveryWritingMemberRefusesAPathOutsideTheGrant()
    {
        var scoped = Create();
        var outside = Path.Combine(_sibling, "a.mkv");
        var inside = Path.Combine(_granted, "a.mkv");

        ShouldRefuse(() => scoped.OpenWrite(outside));
        ShouldRefuse(() => scoped.EnsureFolder(outside));
        ShouldRefuse(() => scoped.DeleteFile(outside));
        ShouldRefuse(() => scoped.SetLastWriteTimeUtc(outside, DateTimeOffset.UnixEpoch));
        ShouldRefuse(() => scoped.TryCreateHardLink(outside, inside));
        ShouldRefuse(() => scoped.TryCreateHardLink(inside, outside));
    }

    [Test]
    public void BothEndsOfATransferAreChecked()
    {
        var scoped = Create();
        var outside = Path.Combine(_sibling, "a.mkv");
        var inside = Path.Combine(_granted, "a.mkv");

        ShouldRefuse(() => scoped.CopyFileAsync(inside, outside));
        ShouldRefuse(() => scoped.CopyFileAsync(outside, inside));
        ShouldRefuse(() => scoped.MoveFileAsync(inside, outside));
        ShouldRefuse(() => scoped.MoveFileAsync(outside, inside));
    }

    [Test]
    public void APathInsideTheGrantReachesTheUnderlyingFileSystem()
    {
        var scoped = Create();

        var reach = () => scoped.FileExists(Path.Combine(_granted, "a.mkv"));

        reach.Should().Throw<NotSupportedException>("the decorator's job is to allow it through, not to answer it");
    }

    [Test]
    public void TheRefusalNamesTheExtensionAndThePath()
    {
        var scoped = Create();
        var outside = Path.Combine(_sibling, "a.mkv");

        var reach = () => scoped.FileExists(outside);

        reach.Should().Throw<UnauthorizedAccessException>()
            .Which.Message.Should().Contain(Plugin.ToString()).And.Contain(outside);
    }

    [Test]
    public void AMissingInnerFileSystemIsRefusedAtConstruction()
    {
        var construct = () => new ScopedFileSystem(null!, Plugin, []);

        construct.Should().Throw<ArgumentNullException>();
    }

    private static void ShouldRefuse(Action call)
        => call.Should().Throw<UnauthorizedAccessException>();

    /// <summary>
    /// Answers nothing. Reaching it at all is what the tests assert on, so it makes that observable.
    /// </summary>
    private sealed class ThrowingFileSystem : IFileSystem
    {
        public bool FileExists(string path) => throw new NotSupportedException();

        public bool FolderExists(string path) => throw new NotSupportedException();

        public Task<bool> IsFolderWritableAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public long? GetAvailableSpace(string path) => throw new NotSupportedException();

        public long? GetTotalSize(string path) => throw new NotSupportedException();

        public long GetFileSize(string path) => throw new NotSupportedException();

        public DateTimeOffset GetLastWriteTimeUtc(string path) => throw new NotSupportedException();

        public void SetLastWriteTimeUtc(string path, DateTimeOffset lastWriteTimeUtc)
            => throw new NotSupportedException();

        public IEnumerable<string> EnumerateFiles(string path, bool recursive = false)
            => throw new NotSupportedException();

        public IEnumerable<string> EnumerateDirectories(string path) => throw new NotSupportedException();

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Stream OpenRead(string path) => throw new NotSupportedException();

        public Stream OpenWrite(string path) => throw new NotSupportedException();

        public Task SaveStreamAsync(Stream source, string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void EnsureFolder(string path) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();

        public Task CopyFileAsync(
            string sourcePath,
            string destinationPath,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MoveFileAsync(
            string sourcePath,
            string destinationPath,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool TryCreateHardLink(string sourcePath, string linkPath) => throw new NotSupportedException();

        public bool IsFileLocked(string path) => throw new NotSupportedException();

        public IStorageMount? GetMount(string path) => throw new NotSupportedException();
    }
}
