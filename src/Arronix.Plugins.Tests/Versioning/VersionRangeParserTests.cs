using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Tests.Versioning;

[TestFixture]
public sealed class VersionRangeParserTests
{
    [TestCase(">=0.3", "0.3.0", true)]
    [TestCase(">=0.3", "0.2.9", false)]
    [TestCase(">=0.3 <0.4", "0.3.7", true)]
    [TestCase(">=0.3 <0.4", "0.4.0", false)]
    [TestCase(">0.3.0", "0.3.0", false)]
    [TestCase(">0.3.0", "0.3.1", true)]
    [TestCase("<=0.3.0", "0.3.0", true)]
    [TestCase("<=0.3.0", "0.3.1", false)]
    [TestCase("=0.3.0", "0.3.0", true)]
    [TestCase("0.3.0", "0.3.0", true)]
    [TestCase("0.3.0", "0.3.1", false)]
    [TestCase(">=0.3 <0.4 || >=0.5 <0.6", "0.5.2", true)]
    [TestCase(">=0.3 <0.4 || >=0.5 <0.6", "0.4.2", false)]
    [TestCase(">=0.3   <0.4", "0.3.1", true)]
    public void TheGrammarMeansWhatItSays(string range, string version, bool expected)
    {
        VersionRangeParser.TryParse(range, out var parsed, out var error).Should().BeTrue(error);

        parsed!.IsSatisfiedBy(SemanticVersion.Parse(version)).Should().Be(expected);
    }

    [TestCase("^0.3.0", "caret")]
    [TestCase("~0.3.0", "tilde")]
    [TestCase("0.3.x", "Wildcards")]
    [TestCase("0.3.*", "Wildcards")]
    [TestCase("*", "Wildcards")]
    [TestCase("0.3.0 - 0.4.0", "Hyphen")]
    public void TheRejectedFormsAreRejectedWithAnActionableMessage(string range, string expectedFragment)
    {
        VersionRangeParser.TryParse(range, out var parsed, out var error).Should().BeFalse();

        parsed.Should().BeNull();
        error.Should().Contain(expectedFragment);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(">=")]
    [TestCase(">=not-a-version")]
    [TestCase(">=0.3 ||")]
    public void AnUnparseableRangeIsARefusalRatherThanAGuess(string? range)
    {
        VersionRangeParser.TryParse(range, out var parsed, out var error).Should().BeFalse();

        parsed.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void ParseThrowsCarryingTheSameMessage()
    {
        var parse = () => VersionRangeParser.Parse("^1.0.0");

        parse.Should().Throw<FormatException>().WithMessage("*caret*");
    }

    [TestCase(">=0.3 <0.4", "0.4.0")]
    [TestCase("<0.9.0", "0.9.0")]
    [TestCase("<=0.3.5", "0.3.6")]
    [TestCase("=0.3.5", "0.3.6")]
    [TestCase(">=0.3 <0.4 || >=0.5 <0.6", "0.6.0")]
    public void AnUpperBoundIsReportedSoundly(string range, string expected)
    {
        var parsed = VersionRangeParser.Parse(range);

        parsed.UpperBoundExclusive.Should().Be(SemanticVersion.Parse(expected));
    }

    [TestCase(">=0.3")]
    [TestCase(">0.0.1")]
    [TestCase(">=0.3 <0.4 || >=0.5")]
    public void AnUnboundedRangeReportsNoUpperBound(string range)
        => VersionRangeParser.Parse(range).UpperBoundExclusive.Should().BeNull();

    [Test]
    public void TheRangeKeepsTheTextItWasWrittenAs()
        => VersionRangeParser.Parse("  >=0.3 <0.4  ").Text.Should().Be(">=0.3 <0.4");
}
