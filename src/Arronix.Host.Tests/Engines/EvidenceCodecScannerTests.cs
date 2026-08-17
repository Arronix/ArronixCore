using Arronix.Abstractions.Quality;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The codec and audio scans.
/// </summary>
/// <remarks>
/// The video vocabulary reaching H.266 is asserted first because it is the case that failed before: a
/// codec the scan does not know reads as no codec at all, and a codec is not a curiosity — it feeds the
/// expected-size model and it is one of the two things people most often write a rule about.
/// </remarks>
[TestFixture]
internal sealed class EvidenceCodecScannerTests
{
    [TestCase("x264", EvidenceVideoCodecTokens.H264)]
    [TestCase("h264", EvidenceVideoCodecTokens.H264)]
    [TestCase("H.264", EvidenceVideoCodecTokens.H264)]
    [TestCase("AVC", EvidenceVideoCodecTokens.H264)]
    [TestCase("x265", EvidenceVideoCodecTokens.H265)]
    [TestCase("HEVC", EvidenceVideoCodecTokens.H265)]
    [TestCase("H.265", EvidenceVideoCodecTokens.H265)]
    [TestCase("h266", EvidenceVideoCodecTokens.H266)]
    [TestCase("VVC", EvidenceVideoCodecTokens.H266)]
    [TestCase("x266", EvidenceVideoCodecTokens.H266)]
    [TestCase("AV1", EvidenceVideoCodecTokens.Av1)]
    [TestCase("VP9", EvidenceVideoCodecTokens.Vp9)]
    [TestCase("XviD", EvidenceVideoCodecTokens.Mpeg4Part2)]
    [TestCase("DivX", EvidenceVideoCodecTokens.Mpeg4Part2)]
    [TestCase("MPEG2", EvidenceVideoCodecTokens.Mpeg2)]
    [TestCase("VC1", EvidenceVideoCodecTokens.Vc1)]
    public void EveryVideoCodecSpellingFoldsOntoOneToken(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.1080p.BluRay.{spelling}-GRP").VideoCodecToken
            .Should().Be(token);

    [TestCase("TrueHD.Atmos", EvidenceAudioTokens.TrueHdWithObjects)]
    [TestCase("TrueHD", EvidenceAudioTokens.TrueHd)]
    [TestCase("DTS-X", EvidenceAudioTokens.DtsWithObjects)]
    [TestCase("DTS-HD.MA", EvidenceAudioTokens.DtsHdMasterAudio)]
    [TestCase("DTS-HD", EvidenceAudioTokens.DtsHdHighResolution)]
    [TestCase("DTS", EvidenceAudioTokens.Dts)]
    [TestCase("FLAC", EvidenceAudioTokens.Flac)]
    [TestCase("LPCM", EvidenceAudioTokens.Pcm)]
    [TestCase("DDP", EvidenceAudioTokens.EnhancedAc3)]
    [TestCase("DD+", EvidenceAudioTokens.EnhancedAc3)]
    [TestCase("EAC3", EvidenceAudioTokens.EnhancedAc3)]
    [TestCase("AC3", EvidenceAudioTokens.Ac3)]
    [TestCase("AAC", EvidenceAudioTokens.Aac)]
    [TestCase("MP3", EvidenceAudioTokens.Mp3)]
    [TestCase("Opus", EvidenceAudioTokens.Opus)]
    public void EveryAudioSpellingFoldsOntoOneToken(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.1080p.BluRay.{spelling}.x264-GRP").AudioToken
            .Should().Be(token);

    [Test]
    public void TheRichestPresentationWinsWhenAReleaseCarriesSeveral()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.BluRay.AC3.5.1-TrueHD.5.1.x264").AudioToken
            .Should().Be(EvidenceAudioTokens.TrueHd);

    [Test]
    public void ARichnessComparisonIsAboutTheTracksAndNotAboutTaste()
    {
        // A lossless track beside a lossy one is a fact about what reaches the speakers. Choosing the
        // richest loses nothing a reader could have wanted, because a listener who prefers the small file
        // is stating a policy over the same evidence.
        EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.BluRay.DTS-HD.MA.7.1.AAC.2.0.x264").AudioToken
            .Should().Be(EvidenceAudioTokens.DtsHdMasterAudio);
    }

    [Test]
    public void TwoDisagreeingCodecClaimsCancel()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.BluRay.x264.HEVC-GRP").VideoCodecToken
            .Should().BeNull();

    [Test]
    public void AStatedFrameRateIsRead()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.60fps.WEB-DL.x264-GRP").StatedFrameRate
            .Should().Be(60d);

    [Test]
    public void AFractionalFrameRateIsNotReadAndIsNotHalfRead()
    {
        // The period that separates the whole part from the fraction is the same character that separates
        // segments, so the fraction arrives on its own. Reading it as a rate would claim 976 frames per
        // second; the honest answer is that the release stated a rate this scan cannot spell.
        EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.23.976fps.WEB-DL.x264-GRP").StatedFrameRate
            .Should().BeNull();
    }

    [Test]
    public void NoFrameRateIsInventedWhenNoneIsStated()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.x264-GRP").StatedFrameRate
            .Should().BeNull();
}
