using Arronix.Plugins.Loading;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Tests.Versioning;

/// <summary>
/// The gate that keeps a contract published for review revocable.
/// </summary>
/// <remarks>
/// An experimental contract may change in any minor release, so a range reaching past the next minor is a
/// promise the host cannot keep. These are the cases that decide whether an extension author has to pin, and
/// pinning is the whole point: it is what buys the freedom to publish a contract before its shape has
/// settled.
/// </remarks>
[TestFixture]
public sealed class ExperimentalGateTests
{
    private static readonly SemanticVersion Host = new(0, 3, 0);

    [TestCase(">=0.3 <0.4")]
    [TestCase(">=0.3.0 <0.4.0")]
    [TestCase("=0.3.0")]
    [TestCase("0.3.0")]
    [TestCase("<0.4")]
    [TestCase(">=0.2 <0.3.9")]
    public void ARangeBoundedAtOrBelowTheNextMinorIsPermitted(string range)
        => VersionRangeParser.Parse(range).SatisfiesExperimentalGate(Host).Should().BeTrue();

    [TestCase(">=0.3 <1.0")]
    [TestCase(">=0.1 <1.0")]
    [TestCase(">=0.3 <0.5")]
    [TestCase(">=0.3")]
    [TestCase(">=0.3 <0.4 || >=0.5 <0.6")]
    public void ARangeReachingPastTheNextMinorIsRefused(string range)
        => VersionRangeParser.Parse(range).SatisfiesExperimentalGate(Host).Should().BeFalse();

    [Test]
    public void TheRangeEveryFirstPartyExtensionShipsPassesAgainstThisHost()
    {
        var range = VersionRangeParser.Parse(">=0.3 <0.4");

        range.IsSatisfiedBy(PluginLoader.HostContractVersion).Should().BeTrue(
            $"the host is running contract version {PluginLoader.HostContractVersion}");
        range.SatisfiesExperimentalGate(PluginLoader.HostContractVersion).Should().BeTrue();
    }

    [Test]
    public void TheHostContractVersionIsReadFromTheContractAssembly()
        => PluginLoader.HostContractVersion.Should().BeGreaterThan(new SemanticVersion(0, 0, 0));
}
