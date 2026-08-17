using Arronix.Abstractions.Definition;
using Arronix.Host.Engines.Parsing;
using FluentAssertions;

// The definition contracts are experimental (ARX0019).
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The declared rung table over real release titles: the answers are Radarr's
/// (<c>QualityParser.ParseQualityName</c>), produced here by ordered rows instead of a branch cascade.
/// </summary>
[TestFixture]
internal sealed class ParseEngineRungResolutionTests
{
    private static readonly DeclarativeReleaseParser Parser = ParseEngineFixtures.Parser();

    private static string RungOf(string title)
    {
        var parsed = Parser.Parse(title);

        parsed.Should().NotBeNull(because: $"'{title}' is a parseable release title");
        return parsed!.Quality!;
    }

    // The reference cases, spelled as the surveyed parser answers them.
    [TestCase("Some.Film.2019.1080p.BluRay.x264-GROUP", "Bluray-1080p")]
    [TestCase("Some.Film.2019.2160p.BluRay.Remux.HDR-GROUP", "Remux-2160p")]
    [TestCase("Some.Film.2019.1080p.BluRay.Remux.AVC-GROUP", "Remux-1080p")]
    [TestCase("Some.Film.2019.720p.BluRay.x264-GROUP", "Bluray-720p")]
    [TestCase("Some.Film.2019.576p.BluRay.x264-GROUP", "Bluray-576p")]
    [TestCase("Some.Film.2019.480p.BluRay.x264-GROUP", "Bluray-480p")]
    [TestCase("Some.Film.2019.BluRay.Xvid-GROUP", "Bluray-480p")]
    [TestCase("Some.Film.2019.2160p.WEB-DL.x265-GROUP", "WEBDL-2160p")]
    [TestCase("Some.Film.2019.1080p.WEB-DL.x264-GROUP", "WEBDL-1080p")]
    [TestCase("Some.Film.2019.720p.WEB-DL.x264-GROUP", "WEBDL-720p")]
    [TestCase("Some.Film.2019.WEB-DL.x264-GROUP", "WEBDL-480p")]
    [TestCase("Some.Film.2019.1080p.WEBRip.x264-GROUP", "WEBRip-1080p")]
    [TestCase("Some.Film.2019.720p.HDTV.x264-GROUP", "HDTV-720p")]
    [TestCase("Some.Film.2019.1080p.HDTV.x264-GROUP", "HDTV-1080p")]
    [TestCase("Some.Film.2019.HDTV.x264-GROUP", "SDTV")]
    [TestCase("Some.Film.2019.1080p.BDRip.x264-GROUP", "Bluray-1080p")]
    [TestCase("Some.Film.2019.BRRip.x264-GROUP", "Bluray-480p")]
    [TestCase("Some.Film.2019.DVDSCR.x264-GROUP", "DVDSCR")]
    [TestCase("Some.Film.2019.CAM.x264-GROUP", "CAM")]
    [TestCase("Some.Film.2019.TELECINE.x264-GROUP", "TELECINE")]
    [TestCase("Some.Film.2019.WORKPRINT.x264-GROUP", "WORKPRINT")]
    [TestCase("Some.Film.2019.DVDRip.x264-GROUP", "DVD")]
    public void ResolvesTheSurveyedRungs(string title, string expected) =>
        RungOf(title).Should().Be(expected);

    /// <summary>
    /// The rightmost source token wins the scan, so the rung follows the scan: the same two tokens in
    /// the two orders land on different rungs (critique F-2, resolved at the scan).
    /// </summary>
    [Test]
    public void TheRungFollowsTheRightmostSourceToken()
    {
        RungOf("Some.Film.2019.1080p.BDRip.WEB-DL.x264").Should().Be("WEBDL-1080p");
        RungOf("Some.Film.2019.1080p.WEB-DL.BDRip.x264").Should().Be("Bluray-1080p");
    }

    /// <summary>An MPEG-2 broadcast capture is an untouched stream, not an HDTV rip.</summary>
    [Test]
    public void AnMpeg2BroadcastIsARawStream() =>
        RungOf("Some.Film.2019.1080i.HDTV.MPEG-2.DD5.1-GROUP").Should().Be("Raw-HD");

    /// <summary>A remux that named a disc but no resolution is 1080p, never 720p.</summary>
    [Test]
    public void ARemuxWithoutAResolutionIs1080p() =>
        RungOf("Some.Film.2019.BluRay.Remux.AVC.DTS-GROUP").Should().Be("Remux-1080p");

    /// <summary>A resolution with no source lands on the broadcast rung: never the top of the ladder.</summary>
    [TestCase("Some.Film.2019.2160p.x265-GROUP", "HDTV-2160p")]
    [TestCase("Some.Film.2019.1080p.x264-GROUP", "HDTV-1080p")]
    [TestCase("Some.Film.2019.720p.x264-GROUP", "HDTV-720p")]
    public void AResolutionAloneLandsOnTheBroadcastRung(string title, string expected) =>
        RungOf(title).Should().Be(expected);

    /// <summary>x264 with nothing else is standard definition by convention.</summary>
    [Test]
    public void WeakSignalsResolveLast()
    {
        RungOf("Some.Film.2019.x264-GROUP").Should().Be("SDTV");
        RungOf("Some.Film.2019.bluray1080p.x264-GROUP").Should().Be("Bluray-1080p");
    }

    /// <summary>
    /// The tier-plus-carried-resolution composition (critique F-10.2): the surveyed rung keeps its
    /// identity while adopting whatever resolution the release stated.
    /// </summary>
    [Test]
    public void ACarryingRungAdoptsTheStatedResolution()
    {
        var parsed = Parser.Parse("Some.Film.2019.1080p.HDTS.x264-GROUP");

        parsed!.Quality.Should().Be("TELESYNC");
        parsed.Resolution.Should().Be("1080p");
    }

    /// <summary>
    /// The container fallback is consulted only when every row is silent, and its wildcard row means
    /// "every other recognized media container", never "every dot-suffix".
    /// </summary>
    [Test]
    public void TheContainerFallbackSpeaksOnlyWhenEveryRowIsSilent()
    {
        RungOf("Some.Film.2019.mkv").Should().Be("WEBDL-720p");
        RungOf("Some.Film.2019.avi").Should().Be("SDTV");
        RungOf("Some.Film.2019").Should().Be("Unknown");
    }

    /// <summary>
    /// Default rows assume evidence the release did not state and never override what it did state.
    /// </summary>
    [Test]
    public void ADefaultRowFillsAbsenceOnly()
    {
        var quality = new QualityDeclaration
        {
            Defaults =
            [
                // A resolution with no source is assumed to be a stream download.
                new TierDefault
                {
                    When = new TagPredicate([ParseEngineFixtures.NoSource(), ParseEngineFixtures.ResAny()]),
                    SourceGroup = "webdl",
                },
            ],
        };

        var parser = ParseEngineFixtures.Parser(quality: quality);

        // Without the default row this title resolves to the broadcast rung (res-alone-1080).
        parser.Parse("Some.Film.2019.1080p.x264-GROUP")!.Quality.Should().Be("WEBDL-1080p");

        // A stated source is never overridden by the assumption.
        parser.Parse("Some.Film.2019.1080p.HDTV.x264-GROUP")!.Quality.Should().Be("HDTV-1080p");
    }

    /// <summary>Pre-release sources have no resolution axis; a stated resolution is ignored outright.</summary>
    [Test]
    public void AnIgnoreResolutionRowClearsTheStatedResolution()
    {
        var quality = new QualityDeclaration
        {
            Defaults =
            [
                new TierDefault
                {
                    When = new TagPredicate([ParseEngineFixtures.SrcIn("cam", "ts", "tc", "wp")]),
                    IgnoreStatedResolution = true,
                },
            ],
        };

        var parser = ParseEngineFixtures.Parser(quality: quality);
        var parsed = parser.Parse("Some.Film.2019.1080p.HDCAM.x264-GROUP");

        parsed!.Quality.Should().Be("CAM");
        parsed.Resolution.Should().BeNull();
    }
}
