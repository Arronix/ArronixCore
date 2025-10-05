using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Tests.Identity;

[TestFixture]
public class MediaKindIdTests
{
    [Test]
    public void MediaKindId_CanBeCreatedFromString()
    {
        var id = new MediaKindId("tv");
        Assert.That(id.Value, Is.EqualTo("tv"));
    }

    [Test]
    public void MediaKindId_ImplicitlyConvertsToString()
    {
        MediaKindId id = "movies";
        string value = id;
        Assert.That(value, Is.EqualTo("movies"));
    }

    [Test]
    public void MediaKindId_ImplicitlyConvertsFromString()
    {
        MediaKindId id = "music";
        Assert.That(id.Value, Is.EqualTo("music"));
    }

    [Test]
    public void MediaKindId_ToStringReturnsValue()
    {
        var id = new MediaKindId("books");
        Assert.That(id.ToString(), Is.EqualTo("books"));
    }

    [Test]
    public void MediaKindId_EqualityWorks()
    {
        var id1 = new MediaKindId("tv");
        var id2 = new MediaKindId("tv");
        var id3 = new MediaKindId("movies");

        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.Not.EqualTo(id3));
    }
}
