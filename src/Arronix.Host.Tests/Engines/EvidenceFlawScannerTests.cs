using Arronix.Abstractions.Quality;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The defect scan.
/// </summary>
/// <remarks>
/// A defect is something the file carries that the viewer did not ask for, and it is kept apart from the
/// signal the file descends from. That separation is what lets a screener be described truthfully — a
/// high-fidelity signal with a mark burned into it — instead of being demoted below a broadcast capture
/// to express the mark.
/// </remarks>
[TestFixture]
internal sealed class EvidenceFlawScannerTests
{
    [TestCase("KORSUB", EvidenceFlawTokens.HardcodedSubtitles)]
    [TestCase("HARDSUB", EvidenceFlawTokens.HardcodedSubtitles)]
    [TestCase("HARDCODED", EvidenceFlawTokens.HardcodedSubtitles)]
    [TestCase("Hard.Coded", EvidenceFlawTokens.HardcodedSubtitles)]
    [TestCase("SAMPLE", EvidenceFlawTokens.Sample)]
    [TestCase("UPSCALED", EvidenceFlawTokens.Upscaled)]
    [TestCase("WATERMARKED", EvidenceFlawTokens.Watermarked)]
    [TestCase("CROPPED", EvidenceFlawTokens.Cropped)]
    [TestCase("AD.BREAKS", EvidenceFlawTokens.AdBreaks)]
    [TestCase("With.Ads", EvidenceFlawTokens.AdBreaks)]
    public void ADefectSpellingIsRead(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.1080p.WEBRip.{spelling}.x264-GRP")
            .FlawTokens.Should().Contain(token);

    [Test]
    public void AnUpscaleStatedAsATransformationIsStillAnUpscale()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.4kto1080p.WEB-DL.x264-GRP")
            .FlawTokens.Should().Contain(EvidenceFlawTokens.Upscaled);

    [Test]
    public void AScreenerCarriesItsBurnIn()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.DVDSCR.XviD-GRP")
            .FlawTokens.Should().Contain(EvidenceFlawTokens.Watermarked);

    [Test]
    public void InterlacingIsNotADefectHereBecauseItIsAScanType()
    {
        // It arrives where it was actually stated — on the raster claim — rather than being duplicated
        // into a defect the title never spelled.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2015.1080i.HDTV.x264");

        evidence.FlawTokens.Should().BeEmpty();
        evidence.ScanType.Should().NotBeNull();
    }

    [Test]
    public void SoftSubtitlesAreNotADefect()
        => EvidenceScanFixtures.Scan(EvidenceScanFixtures.BracketedStreamMarker).FlawTokens
            .Should().BeEmpty();

    [Test]
    public void AnUncroppedFrameIsNotACroppedOne()
    {
        // "Open Matte" means more picture, not less. Nothing in the vocabulary claims it, which is the
        // correct amount of opinion for a scanner to have about it.
        EvidenceScanFixtures.Scan(EvidenceScanFixtures.BroadcastTransportStream).FlawTokens
            .Should().BeEmpty();
    }

    [Test]
    public void NothingStatedIsNothingRead()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.x264-GRP").FlawTokens
            .Should().BeEmpty();
}
