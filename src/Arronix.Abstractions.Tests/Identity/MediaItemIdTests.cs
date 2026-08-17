using System;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Tests.Identity;

[TestFixture]
public class MediaItemIdTests
{
    [Test]
    public void MediaItemId_CanBeCreatedFromANumber()
    {
        var id = new MediaItemId(42);
        Assert.That(id.Value, Is.EqualTo(42L));
    }

    [Test]
    public void MediaItemId_DoesNotConvertImplicitly()
    {
        // Uniform across the identity family: crossing between the brand and the value underneath it is
        // always written out, so a bare number can never stand in for an item identifier by accident.
        Assert.That(
            typeof(MediaItemId)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, "op_Implicit", StringComparison.Ordinal)),
            Is.Empty);
    }

    [Test]
    public void MediaItemId_HoldsValuesWiderThanThirtyTwoBits()
    {
        // The column that stores it is 64-bit, and the runtime type in front of a wider column has to be
        // able to hold everything the column can.
        var id = MediaItemId.FromInt64(long.MaxValue);

        Assert.That(id.Value, Is.EqualTo(long.MaxValue));
        Assert.That(id.ToString(), Is.EqualTo(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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

    [Test]
    public void MediaItemId_FromInt64CreatesInstance()
    {
        var id = MediaItemId.FromInt64(500);
        Assert.That(id.Value, Is.EqualTo(500L));
    }

    [Test]
    public void MediaItemId_ToInt64ReturnsValue()
    {
        var id = new MediaItemId(750);
        Assert.That(id.ToInt64(), Is.EqualTo(750L));
    }
}
