using System.Linq;
using Arronix.Host.Engines.Parsing;
using FluentAssertions;

// The shape contracts are experimental (the revision axis).
#pragma warning disable ARX0013

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The host's kind-agnostic token scans, asserted against the reference behavior they were ported from
/// (Radarr's <c>QualityParser</c> scan half).
/// </summary>
[TestFixture]
internal sealed class ParseEngineTagScannerTests
{
    /// <summary>
    /// The critique's flagship case (F-2): last-ness is a property of the scan, not of any rule table.
    /// The rightmost source token wins, so the same two tokens in the two orders resolve differently —
    /// which no fixed rule-selection mode over an authored table could express.
    /// </summary>
    [Test]
    public void TheRightmostSourceTokenWins()
    {
        ReleaseTagScanner.ScanSource("Some.Film.2019.BDRip.WEB-DL").Should().Be("webdl");
        ReleaseTagScanner.ScanSource("Some.Film.2019.WEB-DL.BDRip").Should().Be("bdrip");
    }

    [TestCase("Some.Film.2019.1080p.BluRay.x264", "bluray")]
    [TestCase("Some.Film.2019.720p.HDTV.x264", "hdtv")]
    [TestCase("Some.Film.2019.WEBRip.x264", "webrip")]
    [TestCase("Some.Film.2019.DVDSCR.x264", "scr")]
    [TestCase("Some.Film.2019.WORKPRINT.x264", "wp")]
    [TestCase("Some.Film.2019.x264", null)]
    public void ScansTheSourceGroup(string title, string? expected) =>
        ReleaseTagScanner.ScanSource(title).Should().Be(expected);

    [TestCase("Some.Film.2019.1080p.BluRay", 1080)]
    [TestCase("Some.Film.2019.2160p.WEB-DL", 2160)]
    [TestCase("Some.Film.2019.848x480.DVDRip", 480)]
    [TestCase("Some.Film.UHD.BluRay", 2160)]
    [TestCase("Some.Film.[4K].BluRay", 2160)]
    [TestCase("Some.Film.2019.BluRay", 0)]
    public void ScansTheStatedResolution(string title, int expected) =>
        ReleaseTagScanner.ScanResolution(title).Should().Be(expected);

    [Test]
    public void AProperIsVersionTwo() =>
        ReleaseTagScanner.ScanRevision("Some.Film.2019.PROPER.1080p", "Some.Film.2019.PROPER.1080p")
            .Should().Be(new Abstractions.Shape.QualityRevision(2, 0, false));

    /// <summary>
    /// "REPACK2" is version three: the second repack of a release that was already at version two.
    /// </summary>
    [Test]
    public void ARepackIncrementsTheStatedVersion()
    {
        var revision = ReleaseTagScanner.ScanRevision(
            "Some.Film.2019.REPACK2.1080p", "Some.Film.2019.REPACK2.1080p");

        revision.Version.Should().Be(3);
        revision.IsRepack.Should().BeTrue();
    }

    /// <summary>
    /// REAL is a revision bump only in upper case: <c>real</c> is an ordinary English word, and a
    /// default-insensitive scan would silently flatten the two (critique F-9's fidelity case).
    /// </summary>
    [Test]
    public void RealCountsOnlyInUpperCase()
    {
        ReleaseTagScanner.ScanRevision("Some.Film.REAL.1080p", "Some.Film.REAL.1080p")
            .Real.Should().Be(1);
        ReleaseTagScanner.ScanRevision("A.real.Story.1080p", "A.real.Story.1080p")
            .Real.Should().Be(0);
    }

    [TestCase("Some.Film.2019.1080p.BluRay.Remux.AVC", true)]
    [TestCase("Some.Film.2019.1080p.BluRay.AVC", false)]
    public void ScansTheRemuxMarker(string title, bool expected) =>
        ReleaseTagScanner.Scan(title, title.Replace('_', ' ')).IsRemux.Should().Be(expected);

    [TestCase("Some.Film.2019.1080p.BluRay.x264-GROUP", "x264")]
    [TestCase("Some.Film.2019.1080p.BluRay.HEVC-GROUP", "x265")]
    public void ScansTheVideoCodec(string title, string expected) =>
        ReleaseCodecScanner.ScanVideoCodec(title).Should().Be(expected);

    [TestCase("Some.Film.2019.1080p.TrueHD.Atmos", "TrueHD")]
    [TestCase("Some.Film.2019.1080p.DDP.5.1", "EAC3")]
    public void ScansTheAudioCodec(string title, string expected) =>
        ReleaseCodecScanner.ScanAudioCodec(title).Should().Be(expected);

    /// <summary>A codec token inside a word is not a token: the boundary scan requires edges.</summary>
    [Test]
    public void ACodecTokenInsideAWordDoesNotCount() =>
        ReleaseCodecScanner.ScanVideoCodec("Avcation.2019.1080p").Should().BeNull();

    [TestCase("2019.1080p.BluRay.x264-SPARKS", "SPARKS")]
    [TestCase("[SubsPlease] 2019 1080p [ABCD1234]", "SubsPlease")]
    [TestCase("2019.1080p.BluRay.x264-EVO-Obfuscated", "EVO")]
    [TestCase("2019.1080p.WEBRip.YIFY", "YIFY")]
    [TestCase("2019.1080p.BluRay.x264", null)]
    public void ScansTheReleaseGroup(string maskedTitle, string? expected) =>
        ReleaseGroupScanner.Scan(maskedTitle).Should().Be(expected);

    /// <summary>
    /// Only a recognized container comes off the tail: a blanket strip would eat a short group name.
    /// </summary>
    [Test]
    public void StripsOnlyRecognizedExtensions()
    {
        ReleaseGroupScanner.StripRecognizedExtension("2019.1080p.BluRay.x264-GROUP.mkv")
            .Should().Be("2019.1080p.BluRay.x264-GROUP");
        ReleaseGroupScanner.StripRecognizedExtension("2019.1080p.BluRay.x264-EVO")
            .Should().Be("2019.1080p.BluRay.x264-EVO");
    }

    [Test]
    public void ScansSpelledOutLanguages()
    {
        var languages = ReleaseLanguageScanner.Scan("2019.FRENCH.1080p.BluRay");

        languages.Select(static language => language.Code).Should().Contain("fr");
    }

    /// <summary>
    /// The German dual-audio convention: <c>German.DL</c> also carries the original language, and
    /// <c>German.ML</c> the original and English. Dropping the rule makes every German dual-audio
    /// release look German-only and fail a language filter it should pass.
    /// </summary>
    [Test]
    public void GermanDualLanguageAlsoCarriesTheOriginal()
    {
        var dual = ReleaseLanguageScanner.Scan("2019.German.DL.1080p.BluRay.x264");

        dual.Select(static language => language.Code)
            .Should().BeEquivalentTo(["de", "original"]);

        var multi = ReleaseLanguageScanner.Scan("2019.German.ML.1080p.BluRay.x264");

        multi.Select(static language => language.Code)
            .Should().BeEquivalentTo(["de", "original", "en"]);
    }

    /// <summary>WEB-DL's DL is not the dual-language DL: the lookbehind keeps them apart.</summary>
    [Test]
    public void WebDlIsNotDualLanguage()
    {
        var languages = ReleaseLanguageScanner.Scan("2019.German.WEB-DL.1080p");

        languages.Select(static language => language.Code).Should().BeEquivalentTo(["de"]);
    }
}
