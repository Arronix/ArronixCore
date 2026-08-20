using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Tests.DTOs;

[TestFixture]
public class QualityTierTests
{
    [Test]
    public void QualityTier_CompareToWorksByRank()
    {
        var low = new QualityTier("SDTV", Rank: 1);
        var medium = new QualityTier("HDTV-720p", Rank: 5);
        var high = new QualityTier("HDTV-1080p", Rank: 10);

        Assert.That(low.CompareTo(medium), Is.LessThan(0));
        Assert.That(medium.CompareTo(high), Is.LessThan(0));
        Assert.That(high.CompareTo(low), Is.GreaterThan(0));
        Assert.That(medium.CompareTo(medium), Is.EqualTo(0));
    }

    [Test]
    public void QualityTier_EffectiveWeightDefaultsToRankAndYieldsToADeclaredWeight()
    {
        var unweighted = new QualityTier("Plain", Rank: 7);
        var grouped = new QualityTier("Grouped", Rank: 8, Weight: 7);

        Assert.Multiple(() =>
        {
            Assert.That(unweighted.EffectiveWeight, Is.EqualTo(7));
            Assert.That(grouped.EffectiveWeight, Is.EqualTo(7));
            Assert.That(unweighted.CompareTo(grouped), Is.Zero, "Equal effective weight is a quality group.");
        });
    }

}
