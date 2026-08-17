using Arronix.Abstractions.Quality;
using FluentAssertions;

// Quality contracts are experimental; a resolution claim is stated in their vocabulary.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The raster scan: un-bucketed, form-aware, and silent when the release says nothing.
/// </summary>
/// <remarks>
/// The four things a bucketing scan destroys are asserted one at a time here, because each of them is a
/// separate downstream capability that cannot be rebuilt afterwards: an interlace marker, an intermediate
/// raster, an upscale statement, and the difference between an explicit line count and a marketing name.
/// </remarks>
[TestFixture]
internal sealed class EvidenceResolutionScannerTests
{
    [TestCase("Movie.Name.2020.480p.WEBRip.x264", 480)]
    [TestCase("Movie.Name.2020.540p.WEBRip.x264", 540)]
    [TestCase("Movie.Name.2020.576p.WEBRip.x264", 576)]
    [TestCase("Movie.Name.2020.720p.WEBRip.x264", 720)]
    [TestCase("Movie.Name.2020.1080p.WEBRip.x264", 1080)]
    [TestCase("Movie.Name.2020.1440p.WEBRip.x264", 1440)]
    [TestCase("Movie.Name.2020.2160p.WEBRip.x264", 2160)]
    [TestCase("Movie.Name.2020.4320p.WEBRip.x264", 4320)]
    public void AStatedLineCountIsReadExactlyAsStated(string title, int lines)
    {
        var evidence = EvidenceScanFixtures.Scan(title);

        evidence.StatedResolution.Should().Be(lines);
        evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.LineCount);
    }

    [Test]
    public void AnInterlaceMarkerBecomesTheScanTypeAndNotADifferentRaster()
    {
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2015.1080i.HDTV.x264");

        evidence.StatedResolution.Should().Be(1080);
        evidence.ScanType.Should().Be(ScanType.Interlaced);
    }

    [Test]
    public void AProgressiveMarkerIsAlsoRead()
        => EvidenceScanFixtures.Scan("Movie.Name.2015.1080p.HDTV.x264").ScanType
            .Should().Be(ScanType.Progressive);

    [Test]
    public void ATransformationStatesBothTheRasterAndTheEnlargement()
    {
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.4kto1080p.WEB-DL.x264-GRP");

        evidence.StatedResolution.Should().Be(1080);
        evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.LineCount);
        evidence.FlawTokens.Should().Contain(EvidenceFlawTokens.Upscaled);
    }

    [TestCase("Movie.Name.2020.[1920x1080].BluRay.x264", 1080)]
    [TestCase("Movie.Name.2020.[1280x720].BluRay.x264", 720)]
    [TestCase("Movie.Name.2020.[3840x2160].BluRay.x264", 2160)]
    public void PixelDimensionsAreARasterForm(string title, int lines)
    {
        var evidence = EvidenceScanFixtures.Scan(title);

        evidence.StatedResolution.Should().Be(lines);
        evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.Raster);
    }

    [TestCase("Movie.Name.2020.4K.BluRay.x264", 2160)]
    [TestCase("Movie.Name.2020.UHD.BluRay.x264", 2160)]
    [TestCase("Movie.Name.2020.FHD.BluRay.x264", 1080)]
    [TestCase("Movie.Name.2020.QHD.BluRay.x264", 1440)]
    [TestCase("Movie.Name.2020.8K.BluRay.x264", 4320)]
    public void AMarketingNameIsReadAndLabeledAsOne(string title, int lines)
    {
        var evidence = EvidenceScanFixtures.Scan(title);

        evidence.StatedResolution.Should().Be(lines);
        evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.MarketingName);
    }

    [Test]
    public void TheMoreSpecificFormWinsWhateverItsValue()
    {
        // A raster is more specific than a marketing name, and the marketing name claims the larger
        // number. The form decides, not the size.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.1920x1080.4K.BluRay.x264");

        evidence.StatedResolution.Should().Be(1080);
        evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.Raster);
    }

    [Test]
    public void AmongEquallySpecificClaimsTheLowestWins()
    {
        // Two line counts in one title is a title contradicting itself. The lower claim is taken because
        // the two failure directions are not symmetric: a missed claim ranks a release low and visible,
        // and an inflated claim promotes it past everything the user asked for.
        EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.720p.WEBRip.x264").StatedResolution
            .Should().Be(720);
    }

    [Test]
    public void NothingStatedIsNothingRead()
    {
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.BluRay.x264-GRP");

        evidence.StatedResolution.Should().BeNull();
        evidence.ScanType.Should().BeNull();
    }

    [Test]
    public void AContainerNeverSuppliesARasterNobodyStated()
    {
        // The scan does not round, guess a nearest rung, or let a container stand in for a raster. An
        // absent reading is representable downstream, so a guess buys nothing and costs a false claim.
        EvidenceScanFixtures.Scan("Movie.Name.2020.BluRay.x264-GRP.mkv").StatedResolution
            .Should().BeNull();
    }

    [Test]
    public void AYearIsNotARaster()
        => EvidenceScanFixtures.Scan("Movie.Name.2160.Rerelease.BluRay.x264").StatedResolution
            .Should().BeNull();
}
