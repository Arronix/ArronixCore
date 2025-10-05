using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Tests.DTOs;

[TestFixture]
public class CutoffPolicyTests
{
    [Test]
    public void CutoffPolicy_MeetsCutoffReturnsTrueWhenQualityMeetsOrExceedsCutoff()
    {
        var cutoff = new QualityTier("HDTV-1080p", Rank: 10);
        var policy = new CutoffPolicy(cutoff);

        var lowerQuality = new QualityTier("HDTV-720p", Rank: 5);
        var equalQuality = new QualityTier("HDTV-1080p", Rank: 10);
        var higherQuality = new QualityTier("BluRay-1080p", Rank: 15);

        Assert.That(policy.MeetsCutoff(lowerQuality), Is.False);
        Assert.That(policy.MeetsCutoff(equalQuality), Is.True);
        Assert.That(policy.MeetsCutoff(higherQuality), Is.True);
    }

    [Test]
    public void CutoffPolicy_ShouldSearchForUpgradeRespectsEnableUpgradesSetting()
    {
        var cutoff = new QualityTier("HDTV-1080p", Rank: 10);
        var policyWithUpgrades = new CutoffPolicy(cutoff, EnableUpgrades: true);
        var policyWithoutUpgrades = new CutoffPolicy(cutoff, EnableUpgrades: false);

        var lowerQuality = new QualityTier("HDTV-720p", Rank: 5);

        Assert.That(policyWithUpgrades.ShouldSearchForUpgrade(lowerQuality), Is.True);
        Assert.That(policyWithoutUpgrades.ShouldSearchForUpgrade(lowerQuality), Is.False);
    }

    [Test]
    public void CutoffPolicy_ShouldSearchForUpgradeReturnsFalseWhenCutoffMet()
    {
        var cutoff = new QualityTier("HDTV-1080p", Rank: 10);
        var policy = new CutoffPolicy(cutoff, EnableUpgrades: true);

        var qualityAtCutoff = new QualityTier("HDTV-1080p", Rank: 10);

        Assert.That(policy.ShouldSearchForUpgrade(qualityAtCutoff), Is.False);
    }
}
