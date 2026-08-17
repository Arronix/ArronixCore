using Arronix.Abstractions.Quality;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The dynamic-range scan, which reports a set because a release genuinely carries a set.
/// </summary>
/// <remarks>
/// A single-slot reading of this has three bad options and no good one: drop a layer, invent a combined
/// name for every pair, or pick a winner and lose the fact that the other layer is present. The scan
/// reports what was stated, and the one place a winner is picked is labeled as the stopgap it is.
/// </remarks>
[TestFixture]
internal sealed class EvidenceDynamicRangeScannerTests
{
    [TestCase("SDR", EvidenceDynamicRangeTokens.StandardDynamicRange)]
    [TestCase("HLG", EvidenceDynamicRangeTokens.HybridLogGamma)]
    [TestCase("HDR", EvidenceDynamicRangeTokens.HighDynamicRange10)]
    [TestCase("HDR10", EvidenceDynamicRangeTokens.HighDynamicRange10)]
    [TestCase("HDR10+", EvidenceDynamicRangeTokens.HighDynamicRange10Plus)]
    [TestCase("HDR10Plus", EvidenceDynamicRangeTokens.HighDynamicRange10Plus)]
    [TestCase("DoVi", EvidenceDynamicRangeTokens.DolbyVision)]
    [TestCase("Dolby.Vision", EvidenceDynamicRangeTokens.DolbyVision)]
    public void EverySpellingFoldsOntoOneToken(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.2160p.WEB-DL.{spelling}.HEVC-GRP")
            .DynamicRangeTokens.Should().Equal(token);

    [Test]
    public void TwoStatedFormatsAreBothReportedAndNeitherIsCollapsed()
    {
        var scanned = EvidenceScanFixtures.Scan("Movie.Name.2020.2160p.WEB-DL.DV.HDR10Plus.HEVC-GRP");

        scanned.DynamicRangeTokens.Should().Equal(
            EvidenceDynamicRangeTokens.DolbyVision,
            EvidenceDynamicRangeTokens.HighDynamicRange10Plus);
    }

    [Test]
    public void AStatedPairIsNotTurnedIntoAFallbackReading()
    {
        // Whether a base layer is a usable fallback for a display that cannot render the top layer is a
        // fact about the file, not about the title. The scan states the two layers and stops.
        var scanned = EvidenceScanFixtures.Scan("Movie.Name.2020.2160p.WEB-DL.DV.HDR10.HEVC-GRP");

        scanned.DynamicRangeTokens.Should().NotContain(EvidenceDynamicRangeTokens.DolbyVisionWithFallback);
        scanned.DynamicRangeTokens.Should().HaveCount(2);
    }

    [Test]
    public void TheSingleSlotProjectionKeepsTheWidestFormat()
    {
        // Named as the stopgap it is: the evidence record carries one token where the axis is set-valued,
        // so the projection loses a layer. The scan itself does not.
        var scanned = EvidenceScanFixtures.Scan("Movie.Name.2020.2160p.WEB-DL.DV.HDR10Plus.HEVC-GRP");

        scanned.DynamicRangeTokens[0].Should().Be(EvidenceDynamicRangeTokens.DolbyVision);
        scanned.DynamicRangeTokens.Should().HaveCount(2);
    }

    [Test]
    public void ARepeatedFormatIsReportedOnce()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.2160p.WEB-DL.HDR10.HDR10.HEVC-GRP")
            .DynamicRangeTokens.Should().Equal(EvidenceDynamicRangeTokens.HighDynamicRange10);

    [Test]
    public void NothingStatedIsNothingRead()
    {
        var scanned = EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.x264-GRP");

        scanned.DynamicRangeTokens.Should().BeEmpty();
        scanned.DynamicRangeTokens.Should().BeEmpty();
    }

    [Test]
    public void TheTwoLetterDolbySpellingNeedsSomethingToSupportIt()
        => EvidenceScanFixtures.Scan("Movie Name DV").DynamicRangeTokens.Should().BeEmpty();
}
