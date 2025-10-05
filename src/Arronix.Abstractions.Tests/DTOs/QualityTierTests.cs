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
    public void QualityTier_CanHaveOptionalProperties()
    {
        var quality = new QualityTier(
            Name: "BluRay-1080p",
            Rank: 15,
            Source: "BluRay",
            Resolution: "1080p",
            Codec: "x264");

        Assert.That(quality.Source, Is.EqualTo("BluRay"));
        Assert.That(quality.Resolution, Is.EqualTo("1080p"));
        Assert.That(quality.Codec, Is.EqualTo("x264"));
    }
}
