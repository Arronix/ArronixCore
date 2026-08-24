using System;
using System.Collections.Generic;
using System.Linq;

namespace Arronix.Format.Video.Tests;

/// <summary>
/// The video family definition is one object every video media type is handed.
/// </summary>
/// <remarks>
/// Which makes its contents process-global, and makes "read-only" a claim about the boundary rather than
/// about the declaration. A collection expression assigned to an <see cref="IReadOnlyList{T}"/> is an
/// array, and an array reached through a read-only interface is still an array to anyone who casts it -
/// so the vocabulary every dependant reads would be editable by any of them. These cases assert the
/// wrapper, not the interface.
/// </remarks>
[TestFixture]
public sealed class VideoFormatFamilyTests
{
    [Test]
    public void TheFamilyDefinitionNamesTheVideoFamilyAndItsExtensions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VideoFormat.Definition.Id, Is.EqualTo("video"));
            Assert.That(VideoFormat.Definition.Name, Is.EqualTo("Video"));
            Assert.That(VideoFormat.Definition.FileExtensions, Does.Contain(".mkv").And.Contain(".m2ts"));
            Assert.That(
                VideoFormat.Definition.FileExtensions.Where(static extension => !extension.StartsWith('.')),
                Is.Empty,
                "every entry is a file extension, dot included");
        });
    }

    [Test]
    public void TheSharedExtensionVocabularyCannotBeCastBackToItsArray()
    {
        var extensions = VideoFormat.Definition.FileExtensions;

        Assert.Multiple(() =>
        {
            Assert.That(extensions, Is.Not.InstanceOf<string[]>(), "an array would be editable by any caller");
            Assert.That(extensions, Is.Not.InstanceOf<List<string>>());
            Assert.That((extensions as IList<string>)?.IsReadOnly, Is.True);
        });
    }

    [Test]
    public void TheSharedExtensionVocabularyRefusesMutation()
    {
        var mutable = (IList<string>)VideoFormat.Definition.FileExtensions;

        Assert.Multiple(() =>
        {
            Assert.That(() => mutable.Add(".fake"), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => mutable.Clear(), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => mutable[0] = ".fake", Throws.TypeOf<NotSupportedException>());
        });
    }

    /// <remarks>
    /// The consequence the two cases above exist to protect. Every video media type is handed the same
    /// object, so a mutation reaching it would not be one kind's problem.
    /// </remarks>
    [Test]
    public void EveryReaderSeesTheSameUnchangedVocabulary()
    {
        var first = VideoFormat.Definition.FileExtensions;
        var second = VideoFormat.Definition.FileExtensions;

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first), "the definition is one canonical object, not a copy per read");
            Assert.That(second.ToArray(), Is.EqualTo(first.ToArray()));
        });
    }
}
