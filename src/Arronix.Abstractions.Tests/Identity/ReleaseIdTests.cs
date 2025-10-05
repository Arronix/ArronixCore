using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Tests.Identity;

[TestFixture]
public class ReleaseIdTests
{
    [Test]
    public void ReleaseId_CanBeCreatedFromString()
    {
        var id = new ReleaseId("abc123");
        Assert.That(id.Value, Is.EqualTo("abc123"));
    }

    [Test]
    public void ReleaseId_ImplicitlyConvertsToString()
    {
        ReleaseId id = "guid-value";
        string value = id;
        Assert.That(value, Is.EqualTo("guid-value"));
    }

    [Test]
    public void ReleaseId_ImplicitlyConvertsFromString()
    {
        ReleaseId id = "hash-value";
        Assert.That(id.Value, Is.EqualTo("hash-value"));
    }

    [Test]
    public void ReleaseId_ToStringReturnsValue()
    {
        var id = new ReleaseId("test-release-id");
        Assert.That(id.ToString(), Is.EqualTo("test-release-id"));
    }

    [Test]
    public void ReleaseId_EqualityWorks()
    {
        var id1 = new ReleaseId("id1");
        var id2 = new ReleaseId("id1");
        var id3 = new ReleaseId("id2");

        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.Not.EqualTo(id3));
    }
}
