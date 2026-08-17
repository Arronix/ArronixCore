using Arronix.Common.Text;

namespace Arronix.Common.Tests.Text;

/// <summary>
/// Covers byte-size formatting and conversion, and pins the correction that gives this type its reason to
/// exist: the arithmetic is binary, so the units must say so.
/// </summary>
[TestFixture]
public class ByteSizeExtensionsTests
{
    [TestCase(0L, "0 B")]
    [TestCase(1000L, "1,000.0 B")]
    [TestCase(1024L, "1.0 KiB")]
    [TestCase(1000000L, "976.6 KiB")]
    [TestCase(377487360L, "360.0 MiB")]
    [TestCase(1255864686L, "1.2 GiB")]
    [TestCase(-1024L, "-1.0 KiB")]
    [TestCase(-1000000L, "-976.6 KiB")]
    [TestCase(-377487360L, "-360.0 MiB")]
    [TestCase(-1255864686L, "-1.2 GiB")]
    public void ToBinarySizeString_FormatsInTheLargestUnitThatLeavesAWholePart(long bytes, string expected)
    {
        Assert.That(bytes.ToBinarySizeString(), Is.EqualTo(expected));
    }

    [TestCase(1024L, "KiB")]
    [TestCase(1048576L, "MiB")]
    [TestCase(1073741824L, "GiB")]
    [TestCase(1099511627776L, "TiB")]
    public void ToBinarySizeString_LabelsBinaryMultiplesWithBinaryUnits(long bytes, string expectedUnit)
    {
        // The regression this pins: the previous implementation divided by 1024 and then labeled the
        // result KB, MB and GB, which are multiples of 1000. A value it reported as "1.0 GB" was 1073741824
        // bytes — 7.4% more than the operator who wrote the limit asked for.
        Assert.That(bytes.ToBinarySizeString(), Is.EqualTo("1.0 " + expectedUnit));
    }

    [Test]
    public void ToBinarySizeString_FormatsTheMostNegativeValueExactly()
    {
        // Has no positive counterpart, so it is taken as an unsigned magnitude rather than approximated by
        // the largest positive value as it used to be.
        Assert.That(long.MinValue.ToBinarySizeString(), Is.EqualTo("-8.0 EiB"));
    }

    [Test]
    public void ToBinarySizeString_UsesTheInvariantCultureRegardlessOfTheHost()
    {
        Assert.That(1536L.ToBinarySizeString(), Is.EqualTo("1.5 KiB"));
    }

    [Test]
    public void Mebibytes_ConvertsInBinaryMultiples()
    {
        Assert.That(1.Mebibytes(), Is.EqualTo(1048576L));
        Assert.That(100.Mebibytes(), Is.EqualTo(104857600L));
        Assert.That(1.5d.Mebibytes(), Is.EqualTo(1572864L));
    }

    [Test]
    public void Gibibytes_ConvertsInBinaryMultiples()
    {
        Assert.That(1.Gibibytes(), Is.EqualTo(1073741824L));
        Assert.That(2.5d.Gibibytes(), Is.EqualTo(2684354560L));
    }
}
