
using Arronix.Abstractions.FileSystem;

namespace Arronix.Abstractions.Tests.FileSystem;

[TestFixture]
public class PlatformPathTests
{
    [TestCase("/media/library", PlatformPathKind.Unix)]
    [TestCase(@"C:\media\library", PlatformPathKind.Windows)]
    [TestCase(@"\\server\share\media", PlatformPathKind.Windows)]
    [TestCase("library", PlatformPathKind.Unknown)]
    public void DetectsGrammarFromText(string path, PlatformPathKind expected)
    {
        var platformPath = new PlatformPath(path);

        Assert.That(platformPath.Kind, Is.EqualTo(expected));
    }

    [Test]
    public void NormalizesSeparatorsToTheGrammarSeparator()
    {
        Assert.That(new PlatformPath(@"C:/media/library").FullPath, Is.EqualTo(@"C:\media\library"));
        Assert.That(new PlatformPath("/media//library").FullPath, Is.EqualTo("/media/library"));
    }

    [Test]
    public void DefaultInstanceIsUsableAndEmpty()
    {
        var empty = PlatformPath.Empty;

        Assert.That(empty.FullPath, Is.EqualTo(string.Empty));
        Assert.That(empty.IsEmpty, Is.True);
        Assert.That(empty.IsRooted, Is.False);
        Assert.That(empty.Name, Is.EqualTo(string.Empty));
        Assert.That(empty.FileName, Is.Null);
        Assert.That(empty.Directory, Is.EqualTo(PlatformPath.Empty));
    }

    [TestCase("/media/library/file.ext", "/media/library/")]
    [TestCase("/media/library/", "/media/")]
    [TestCase("/media", "/")]
    [TestCase("/", "")]
    [TestCase(@"C:\media\file.ext", @"C:\media\")]
    [TestCase(@"C:\media", @"C:\")]
    [TestCase(@"C:\", "")]
    [TestCase(@"\\server\share\media", @"\\server\share\")]
    [TestCase(@"\\server\share", "")]
    public void ResolvesTheParentDirectory(string path, string expected)
    {
        Assert.That(new PlatformPath(path).Directory.FullPath, Is.EqualTo(expected));
    }

    [TestCase("/media/library/file.ext", "file.ext")]
    [TestCase("/media/library/", "library")]
    [TestCase("/", "/")]
    [TestCase(@"C:\media\file.ext", "file.ext")]
    [TestCase(@"C:\", @"C:\")]
    public void ResolvesTheLastSegment(string path, string expected)
    {
        Assert.That(new PlatformPath(path).Name, Is.EqualTo(expected));
    }

    [Test]
    public void TrailingSeparatorMeansDirectorySoThereIsNoFileName()
    {
        Assert.That(new PlatformPath("/media/library/file.ext").FileName, Is.EqualTo("file.ext"));
        Assert.That(new PlatformPath("/media/library/").FileName, Is.Null);
        Assert.That(new PlatformPath("/").FileName, Is.Null);
    }

    [Test]
    public void EqualityIgnoresATrailingSeparator()
    {
        var withSeparator = new PlatformPath("/media/library/");
        var withoutSeparator = new PlatformPath("/media/library");

        Assert.That(withSeparator, Is.EqualTo(withoutSeparator));
        Assert.That(withSeparator.GetHashCode(), Is.EqualTo(withoutSeparator.GetHashCode()));
    }

    [Test]
    public void WindowsPathsCompareWithoutRegardToCaseAndUnixPathsDoNot()
    {
        Assert.That(new PlatformPath(@"C:\Media"), Is.EqualTo(new PlatformPath(@"c:\media")));
        Assert.That(new PlatformPath("/Media"), Is.Not.EqualTo(new PlatformPath("/media")));
    }

    [Test]
    public void HashIsConsistentWithCaseInsensitiveEquality()
    {
        // Equality is case-insensitive only for the Windows grammar, so the hash has to be
        // case-insensitive in every grammar for equal values to hash equally.
        Assert.That(
            new PlatformPath(@"C:\Media").GetHashCode(),
            Is.EqualTo(new PlatformPath(@"c:\media").GetHashCode()));
    }

    [Test]
    public void CombineAppendsAndARootedRightHandSideWins()
    {
        Assert.That(
            (new PlatformPath("/media") + new PlatformPath("library")).FullPath,
            Is.EqualTo("/media/library"));

        Assert.That(
            (new PlatformPath("/media/") + new PlatformPath("library")).FullPath,
            Is.EqualTo("/media/library"));

        Assert.That(
            (new PlatformPath("/media") + new PlatformPath("/other")).FullPath,
            Is.EqualTo("/other"));
    }

    [Test]
    public void CombiningDifferentGrammarsIsRejectedWithAnArgumentException()
    {
        var unix = new PlatformPath("/media");
        var windows = new PlatformPath(@"C:\media");

        Assert.That(() => unix.Combine(windows), Throws.ArgumentException);
    }

    [Test]
    public void ContainsComparesWholeSegments()
    {
        var root = new PlatformPath("/media/library");

        Assert.That(root.Contains(new PlatformPath("/media/library/inner")), Is.True);
        Assert.That(root.Contains(new PlatformPath("/media/library")), Is.True);
        Assert.That(root.Contains(new PlatformPath("/media/library2")), Is.False);
        Assert.That(root.Contains(new PlatformPath("/media")), Is.False);
    }

    [Test]
    public void ContainsRequiresBothPathsToBeRooted()
    {
        Assert.That(new PlatformPath("media").Contains(new PlatformPath("media/inner")), Is.False);
    }

    [Test]
    public void AsDirectoryIsIdempotent()
    {
        var directory = new PlatformPath("/media/library").AsDirectory();

        Assert.That(directory.FullPath, Is.EqualTo("/media/library/"));
        Assert.That(directory.AsDirectory().FullPath, Is.EqualTo("/media/library/"));
    }
}
