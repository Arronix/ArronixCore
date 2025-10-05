using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Tests.Identity;

[TestFixture]
public class MediaItemIdTests
{
    [Test]
    public void MediaItemId_CanBeCreatedFromInt()
    {
        var id = new MediaItemId(42);
        Assert.That(id.Value, Is.EqualTo(42));
    }

    [Test]
    public void MediaItemId_ImplicitlyConvertsToInt()
    {
        MediaItemId id = 100;
        int value = id;
        Assert.That(value, Is.EqualTo(100));
    }

    [Test]
    public void MediaItemId_ImplicitlyConvertsFromInt()
    {
        MediaItemId id = 200;
        Assert.That(id.Value, Is.EqualTo(200));
    }

    [Test]
    public void MediaItemId_ToStringReturnsValue()
    {
        var id = new MediaItemId(999);
        Assert.That(id.ToString(), Is.EqualTo("999"));
    }

    [Test]
    public void MediaItemId_EqualityWorks()
    {
        var id1 = new MediaItemId(1);
        var id2 = new MediaItemId(1);
        var id3 = new MediaItemId(2);

        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.Not.EqualTo(id3));
    }
}
