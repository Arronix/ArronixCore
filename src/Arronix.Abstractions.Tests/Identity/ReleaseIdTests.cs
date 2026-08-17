using System;
using System.Linq;
using System.Reflection;
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
    public void ReleaseId_DoesNotConvertImplicitly()
    {
        // Uniform across the identity family: a release identifier and a media-kind identifier are both
        // strings underneath, and nothing should let one be passed where the other is expected.
        Assert.That(
            typeof(ReleaseId)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, "op_Implicit", StringComparison.Ordinal)),
            Is.Empty);
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

    [Test]
    public void ReleaseId_FromStringCreatesInstance()
    {
        var id = ReleaseId.FromString("from-string-value");
        Assert.That(id.Value, Is.EqualTo("from-string-value"));
    }

    [Test]
    public void ReleaseId_ToReleaseIdReturnsValue()
    {
        var id = new ReleaseId("to-method-value");
        Assert.That(id.ToReleaseId(), Is.EqualTo("to-method-value"));
    }
}
