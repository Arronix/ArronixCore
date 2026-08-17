using System;
using System.Linq;
using System.Reflection;
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
    public void MediaKindId_DoesNotConvertImplicitly()
    {
        // Uniform across the identity family: a bare string can never stand in for a media kind, so a
        // kind identifier and a level identifier are never mutually assignable through their values.
        Assert.That(
            typeof(MediaKindId)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, "op_Implicit", StringComparison.Ordinal)),
            Is.Empty);
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

    [Test]
    public void MediaKindId_FromStringCreatesInstance()
    {
        var id = MediaKindId.FromString("audio");
        Assert.That(id.Value, Is.EqualTo("audio"));
    }

    [Test]
    public void MediaKindId_ToMediaKindIdReturnsValue()
    {
        var id = new MediaKindId("podcasts");
        Assert.That(id.ToMediaKindId(), Is.EqualTo("podcasts"));
    }
}
