using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Tests.Versioning;

[TestFixture]
public sealed class SemanticVersionTests
{
    [TestCase("1.2.3", 1, 2, 3, null)]
    [TestCase("0.3", 0, 3, 0, null)]
    [TestCase("2", 2, 0, 0, null)]
    [TestCase("0.3.0-beta.1", 0, 3, 0, "beta.1")]
    [TestCase("  1.0.0  ", 1, 0, 0, null)]
    [TestCase("1.0.0+build.7", 1, 0, 0, null)]
    [TestCase("1.0.0-rc.1+build.7", 1, 0, 0, "rc.1")]
    public void APartialOrDecoratedVersionWidensToTheRight(
        string text,
        int major,
        int minor,
        int patch,
        string? prerelease)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();

        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
        version.Prerelease.Should().Be(prerelease);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("1.2.3.4")]
    [TestCase("1.2.x")]
    [TestCase("v1.2.3")]
    [TestCase("1..3")]
    [TestCase("-1.2.3")]
    [TestCase("01.2.3")]
    [TestCase("1.2.3-")]
    [TestCase("1.2.3-01")]
    [TestCase("1.2.3-beta..1")]
    [TestCase("1.2.3+")]
    public void AMalformedVersionIsRefused(string? text)
        => SemanticVersion.TryParse(text, out _).Should().BeFalse();

    [Test]
    public void ParseThrowsRatherThanGuessing()
    {
        var parse = () => SemanticVersion.Parse("not-a-version");

        parse.Should().Throw<FormatException>();
    }

    [TestCase("1.0.0", "2.0.0")]
    [TestCase("2.0.0", "2.1.0")]
    [TestCase("2.1.0", "2.1.1")]
    [TestCase("1.0.0-alpha", "1.0.0")]
    [TestCase("1.0.0-alpha", "1.0.0-alpha.1")]
    [TestCase("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [TestCase("1.0.0-alpha.beta", "1.0.0-beta")]
    [TestCase("1.0.0-beta", "1.0.0-beta.2")]
    [TestCase("1.0.0-beta.2", "1.0.0-beta.11")]
    [TestCase("1.0.0-beta.11", "1.0.0-rc.1")]
    [TestCase("1.0.0-rc.1", "1.0.0")]
    public void PrecedenceFollowsTheSpecification(string lower, string higher)
    {
        var left = SemanticVersion.Parse(lower);
        var right = SemanticVersion.Parse(higher);

        (left < right).Should().BeTrue();
        (right > left).Should().BeTrue();
        (left <= right).Should().BeTrue();
        (right >= left).Should().BeTrue();
        left.CompareTo(right).Should().BeNegative();
    }

    [Test]
    public void BuildMetadataDoesNotAffectEquality()
        => SemanticVersion.Parse("1.0.0+one").Should().Be(SemanticVersion.Parse("1.0.0+two"));

    [TestCase("1.2.3", "1.2.3")]
    [TestCase("0.3", "0.3.0")]
    [TestCase("1.0.0-rc.1", "1.0.0-rc.1")]
    public void TheTextFormRoundTrips(string text, string expected)
    {
        var version = SemanticVersion.Parse(text);

        version.ToString().Should().Be(expected);
        SemanticVersion.Parse(version.ToString()).Should().Be(version);
    }

    [Test]
    public void ANegativeComponentIsRefused()
    {
        var construct = () => new SemanticVersion(1, -1, 0);

        construct.Should().Throw<ArgumentOutOfRangeException>();
    }
}
