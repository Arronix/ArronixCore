using Arronix.Common.Text;

namespace Arronix.Common.Tests.Text;

/// <summary>
/// Covers the nullable parsers, and pins the fix to the fractional parser, which used to destroy any number
/// that carried both a thousands separator and a decimal point.
/// </summary>
[TestFixture]
public class NullableParseExtensionsTests
{
    [TestCase("42", 42)]
    [TestCase("-42", -42)]
    [TestCase(" 42 ", 42)]
    public void ParseInt32_ReadsAnInteger(string input, int expected)
    {
        Assert.That(input.ParseInt32(), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nonsense")]
    [TestCase("4.2")]
    [TestCase("99999999999")]
    [TestCase(null)]
    public void ParseInt32_YieldsNothingForTextThatIsNotAnInteger(string? input)
    {
        Assert.That(input.ParseInt32(), Is.Null);
    }

    [Test]
    public void ParseInt64_ReadsValuesOutsideTheThirtyTwoBitRange()
    {
        Assert.That("99999999999".ParseInt64(), Is.EqualTo(99999999999L));
    }

    [TestCase(null)]
    [TestCase("nonsense")]
    public void ParseInt64_YieldsNothingForTextThatIsNotAnInteger(string? input)
    {
        Assert.That(input.ParseInt64(), Is.Null);
    }

    [Test]
    public void ParseDouble_ReadsAGroupedNumberWithADecimalPoint()
    {
        // The regression this pins: the previous implementation replaced every comma with a dot before
        // parsing, turning "1,234.5" into "1.234.5", which parses as nothing at all. The platform therefore
        // read no value where a perfectly ordinary one was supplied, which is how the defect went unnoticed.
        Assert.That("1,234.5".ParseDouble(), Is.EqualTo(1234.5d));
    }

    [Test]
    public void ParseDouble_ReadsALargeGroupedNumber()
    {
        Assert.That("1,234,567.89".ParseDouble(), Is.EqualTo(1234567.89d));
    }

    [TestCase("1.5", 1.5d)]
    [TestCase("1,5", 1.5d)]
    [TestCase("42", 42d)]
    [TestCase("-1.5", -1.5d)]
    [TestCase("1.5e3", 1500d)]
    public void ParseDouble_AcceptsEitherDecimalSeparator(string input, double expected)
    {
        Assert.That(input.ParseDouble(), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nonsense")]
    [TestCase("1.2.3")]
    [TestCase(null)]
    public void ParseDouble_YieldsNothingForTextThatIsNotANumber(string? input)
    {
        Assert.That(input.ParseDouble(), Is.Null);
    }
}
