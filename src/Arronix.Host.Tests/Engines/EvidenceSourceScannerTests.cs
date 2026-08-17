using Arronix.Abstractions.Quality;
using FluentAssertions.Execution;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The source scan: a vocabulary of signals, with no ranking anywhere in it.
/// </summary>
/// <remarks>
/// The scanner is structurally incapable of saying that one source beats another — it returns a token out
/// of a flat vocabulary — and that is the whole point. What a disc bitstream is worth relative to a stream
/// capture is a question about somebody's preference, and the defect this design replaces is precisely a
/// data table that answered it on everyone's behalf.
/// </remarks>
[TestFixture]
internal sealed class EvidenceSourceScannerTests
{
    [TestCase("BluRay", EvidenceSourceTokens.BluRayDisc)]
    [TestCase("Blu-Ray", EvidenceSourceTokens.BluRayDisc)]
    [TestCase("BDRip", EvidenceSourceTokens.BluRayRip)]
    [TestCase("BRRip", EvidenceSourceTokens.BluRayRipOfRip)]
    [TestCase("UHDBDRip", EvidenceSourceTokens.UltraHighDefinitionDiscRip)]
    [TestCase("UHD2BD", EvidenceSourceTokens.UltraHighDefinitionDiscDownConvert)]
    [TestCase("HDDVD", EvidenceSourceTokens.HighDefinitionDvdDisc)]
    [TestCase("HDDVDRip", EvidenceSourceTokens.HighDefinitionDvdRip)]
    [TestCase("DVD", EvidenceSourceTokens.DvdDisc)]
    [TestCase("DVDRip", EvidenceSourceTokens.DvdRip)]
    [TestCase("WEB-DL", EvidenceSourceTokens.WebDownload)]
    [TestCase("WEBDL", EvidenceSourceTokens.WebDownload)]
    [TestCase("WEBRip", EvidenceSourceTokens.WebRip)]
    [TestCase("WEB-Rip", EvidenceSourceTokens.WebRip)]
    [TestCase("HDTV", EvidenceSourceTokens.HighDefinitionBroadcast)]
    [TestCase("PDTV", EvidenceSourceTokens.PublicDigitalBroadcast)]
    [TestCase("SDTV", EvidenceSourceTokens.StandardDefinitionBroadcast)]
    [TestCase("TVRip", EvidenceSourceTokens.BroadcastRip)]
    [TestCase("DSR", EvidenceSourceTokens.SatelliteRecording)]
    [TestCase("CAM", EvidenceSourceTokens.CameraCapture)]
    [TestCase("HDCAM", EvidenceSourceTokens.CameraCapture)]
    [TestCase("TELESYNC", EvidenceSourceTokens.TeleSync)]
    [TestCase("HDTS", EvidenceSourceTokens.TeleSync)]
    [TestCase("TELECINE", EvidenceSourceTokens.TeleCine)]
    [TestCase("DVDSCR", EvidenceSourceTokens.StandardDefinitionDiscScreener)]
    [TestCase("BDSCR", EvidenceSourceTokens.HighDefinitionDiscScreener)]
    [TestCase("WORKPRINT", EvidenceSourceTokens.WorkPrint)]
    public void EverySourceSpellingFoldsOntoOneToken(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.1080p.{spelling}.x264-GRP").SourceToken
            .Should().Be(token);

    [Test]
    public void TheMoreSpecificClaimWins()
    {
        // "HD DVD Rip" spans three segments and "DVD" spans one. The longer spelling states more.
        EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.HD.DVD.Rip.x264-GRP").SourceToken
            .Should().Be(EvidenceSourceTokens.HighDefinitionDvdRip);
    }

    [Test]
    public void TwoEquallySpecificClaimsThatDisagreeCancel()
    {
        // A title naming two unrelated sources at the same strength is contradicting itself, and a wrong
        // source promotes a release past everything the user asked for. A missing one merely leaves it
        // visible and unranked.
        EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.HDTV.BluRay.x264-GRP").SourceToken
            .Should().BeNull();
    }

    [Test]
    public void ARepeatedClaimIsNotADisagreement()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.BluRay.BluRay.x264-GRP").SourceToken
            .Should().Be(EvidenceSourceTokens.BluRayDisc);

    [Test]
    public void ABitstreamClaimIsStatedIndependentlyOfAnySource()
    {
        // A bitstream spelling with no source beside it is a real and common arrangement, and the scan's
        // job is to report that the title said one thing and not the other. What the combination is worth
        // is a reading, not a scan.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.2160p.UHDRemux.HEVC-GRP");

        using (new AssertionScope())
        {
            evidence.IsRemux.Should().BeTrue();
            evidence.SourceToken.Should().BeNull();
        }
    }

    [Test]
    public void ABitstreamClaimBesideASourceLeavesTheSourceAlone()
    {
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.2160p.BluRay.REMUX.HEVC-GRP");

        using (new AssertionScope())
        {
            evidence.IsRemux.Should().BeTrue();
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
        }
    }

    [Test]
    public void AScreenerStatesBothItsDiscAndItsBurnIn()
    {
        // Modelling a screener as its actual signal plus a defect is more useful than pretending it is
        // worse than a broadcast capture: a disc screener really is a high-fidelity signal with a mark on
        // it, and the two facts are independently actionable.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.BDSCR.1080p.x264-GRP");

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.HighDefinitionDiscScreener);
            evidence.FlawTokens.Should().Contain(EvidenceFlawTokens.Watermarked);
        }
    }

    [Test]
    public void ATwoLetterSourceAbbreviationAtTheEndOfATitleIsNotClaimed()
        => EvidenceScanFixtures.Scan("Movie Name 2020 BD").SourceToken.Should().BeNull();

    [Test]
    public void NothingStatedIsNothingClaimed()
        => EvidenceScanFixtures.Scan("Movie.Name.2020-GRP").SourceToken.Should().BeNull();
}
