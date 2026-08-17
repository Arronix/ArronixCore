#pragma warning disable ARX0021 // Quality contracts are experimental; these tests exercise the axes model.

using System.Linq;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Quality.Families;
using Arronix.Plugin.Movies.Definition;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Quality;

/// <summary>
/// What the shared video family reads off a movie release, axis by axis.
/// </summary>
/// <remarks>
/// <para>
/// These are the assertions the rung fixture used to make, said properly. A rung name bundled several
/// independent facts into one string and then asserted the string, so a case that read the source right and
/// the resolution wrong looked identical to one that read both wrong. Here each fact is its own assertion
/// and a failure names the axis.
/// </para>
/// <para>
/// The evidence is scanned by the host's own scanners, so what is exercised is the whole boundary: strings
/// enter the family's reading and typed axes come out. The per-kind guards are supplied explicitly, because
/// they are exactly the input the refinement seam exists to carry and stating them in the test is what
/// makes a dialect rule visible rather than incidental.
/// </para>
/// </remarks>
[TestFixture]
public class AxisReadingTests
{
    [TestCase("Movie.Title.2016.REMUX.1080p.BluRay.AVC.DTS-HD.MA.5.1-iFT", VideoOrigin.HighDefinitionDiscBitstream)]
    [TestCase("Movie.Title.S02E13.1080p.BluRay.x264-AVCDVD", VideoOrigin.HighDefinitionDisc)]
    [TestCase("Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG", VideoOrigin.Stream)]
    [TestCase("Movie.Name.S04E01.iNTERNAL.1080p.WEBRip.x264-QRUS", VideoOrigin.Stream)]
    [TestCase("Movie.Name.S08E05.720p.HDTV.X264-DIMENSION", VideoOrigin.Broadcast)]
    [TestCase("Movie.Title.2015.Open.Matte.1080i.HDTV.DD5.1.MPEG2", VideoOrigin.BroadcastBitstream)]
    [TestCase("Some.Movie.S03E06.DVDRip.XviD-WiDE", VideoOrigin.StandardDefinitionDisc)]
    [TestCase("Movie Name 2018 NEW PROPER 720p HD-CAM X264 HQ-CPG", VideoOrigin.CameraCapture)]
    public void ReadsTheMasterSignalTheReleaseNames(string releaseTitle, VideoOrigin expected)
        => Assert.That(Read(releaseTitle).Origin.Or((VideoOrigin)(-1)), Is.EqualTo(expected));

    /// <summary>
    /// The step a rung ladder needs two unrelated mechanisms to state: a bitstream copy is generation zero
    /// by construction, a rip of the disc is one, and a re-encode of an existing rip is two — which a rung
    /// table treats identically to a rip because it has nowhere to put the difference.
    /// </summary>
    [TestCase("Movie.Title.2016.REMUX.1080p.BluRay.AVC.DTS-HD.MA.5.1-iFT", 0)]
    [TestCase("Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG", 0)]
    [TestCase("Movie.Name.2004.576p.BDRip.x264-HANDJOB", 1)]
    [TestCase("Movie.Name.S04E01.iNTERNAL.1080p.WEBRip.x264-QRUS", 1)]
    [TestCase("Movie.Title.2014.1080p.BRRip.x264-GROUP", 2)]
    public void CountsReEncodesSinceThatSignal(string releaseTitle, int expected)
        => Assert.That(Read(releaseTitle).Generation.Or(-1), Is.EqualTo(expected));

    /// <summary>
    /// <b>Un-bucketed, which is the point.</b> A scan that folds an intermediate raster onto the nearest
    /// familiar one destroys the distinction before anything can reason about it, and the resolution is the
    /// first axis the shipped policy orders on.
    /// </summary>
    [TestCase("[SubsPlease] Movie Title (540p) [AB649D32].mkv", 540)]
    [TestCase("Movie.Title.2013.960p.WEB-DL.AAC2.0.H.264-squalor", 960)]
    [TestCase("Movie.Name.2011.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR", 480)]
    [TestCase("[Coalgirls]_Movie!!_01_(1920x1080_Blu-ray_FLAC)_[8370CB8F].mkv", 1080)]
    [TestCase("Movie Name 2005 1080p UHD BluRay DD+7.1 x264-LoRD.mkv", 1080)]
    public void ReadsTheResolutionExactlyAsStated(string releaseTitle, int expected)
        => Assert.That(Read(releaseTitle).Resolution.Or(0), Is.EqualTo(expected));

    /// <summary>
    /// A camera recording of a projection states the camera's raster, not the film's, so the claim says
    /// nothing about the copy and is dropped. This is the whole of what a per-kind list of "sources whose
    /// stated resolution must be ignored" was doing, and it needs no list.
    /// </summary>
    [Test]
    public void DropsAResolutionAProjectionCaptureClaims()
    {
        var read = Read("Movie Name 2018 NEW PROPER 1080p HD-CAM X264 HQ-CPG");

        Assert.Multiple(() =>
        {
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.CameraCapture));
            Assert.That(read.Resolution.IsKnown, Is.False);
        });
    }

    /// <summary>
    /// <b>The German dual-language remux, read from a typed axis instead of a movie identifier.</b> A
    /// German dual-language disc encode is a remux and never spells the word, which used to need a
    /// per-kind guard whose expression was a movie string sitting in a decision table. It is now a rule the
    /// shared family states over the language claims the host's scanner already produced.
    /// </summary>
    [Test]
    public void ReadsADualLanguageDiscEncodeAsABitstreamCopyWithoutAPerKindGuard()
    {
        var read = Read("This.Wonderful.Movie.1991.German.ML.1080p.BluRay.AVC-GeRMaNSCeNEGRoUP");

        Assert.Multiple(() =>
        {
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.HighDefinitionDiscBitstream));
            Assert.That(
                MoviesDeclaration.Parsing.Guards.Select(static guard => guard.GuardId),
                Does.Not.Contain("german-remux"));
        });
    }

    /// <summary>
    /// A rip token in the same title stops the dual-language marker promoting anything: the release states
    /// outright that it was re-encoded, and a marker cannot outrank a statement.
    /// </summary>
    [Test]
    public void DoesNotPromoteADualLanguageRipToABitstreamCopy()
    {
        var read = Read("Movie.Title.2019.German.DL.1080p.HDR.UHDBDRip.AV1-GROUP");

        Assert.Multiple(() =>
        {
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.HighDefinitionDisc));
            Assert.That(read.Generation.Or(-1), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A broadcast capture carried in the codec broadcasters transmit in has not been re-encoded, so it is
    /// the transport stream itself. The per-kind codec guard that used to say so is gone; the codec is
    /// typed evidence now.
    /// </summary>
    [Test]
    public void ReadsAnUnreencodedBroadcastAsItsOwnTransportStream()
    {
        var read = Read("Movie.Title.2009.1080i.HDTV.AAC2.0.MPEG2-PepelefuF");

        Assert.Multiple(() =>
        {
            Assert.That(read.Codec.Or((VideoCodec)(-1)), Is.EqualTo(VideoCodec.Mpeg2));
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.BroadcastBitstream));
            Assert.That(read.Generation.Or(-1), Is.Zero);
            Assert.That(read.Flaws.Has(VideoFlaw.Interlaced), Is.True);
            Assert.That(
                MoviesDeclaration.Parsing.Guards.Select(static guard => guard.GuardId),
                Does.Not.Contain("mpeg2"));
        });
    }

    /// <summary>
    /// A bitstream copy with no source token at all is still a disc bitstream copy, because that is the
    /// only thing a bitstream copy can be. Four rung rows used to say this; it is one line and it arrives
    /// at the weakest provenance there is, so a measurement still overrules it.
    /// </summary>
    [TestCase("Movie.Title.2016.T1.UHDRemux.2160p.HEVC.Dual.AC3.5.1-TrueHD.5.1.Sub")]
    [TestCase("Movie Name (2021) [Remux-2160p x265 HDR 10-BIT DTS-HD MA 7.1]-FraMeSToR.mkv")]
    public void InfersADiscBitstreamFromABitstreamTokenAlone(string releaseTitle)
    {
        var read = Read(releaseTitle);

        Assert.Multiple(() =>
        {
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.HighDefinitionDiscBitstream));
            Assert.That(read.Origin.Source, Is.EqualTo(EvidenceSource.Assumed));
            Assert.That(read.Resolution.Or(0), Is.EqualTo(2160));
        });
    }

    /// <summary>
    /// <b>Set-valued, because a release genuinely carries two at once.</b> A single slot would force the
    /// scan to pick a winner and discard the other format before anything could read it.
    /// </summary>
    [Test]
    public void ReadsBothDynamicRangeFormatsAReleaseStates()
    {
        var read = Read("Movie.Name.2024.German.AC3D.DL.2160p.Hybrid.WEB.DV.HDR10Plus.HEVC-GROUP");

        Assert.Multiple(() =>
        {
            Assert.That(read.DynamicRange.Has(DynamicRange.DolbyVision), Is.True);
            Assert.That(read.DynamicRange.Has(DynamicRange.HighDynamicRange10Plus), Is.True);
        });
    }

    /// <summary>
    /// Base zero, so a first issue is nothing and a corrected issue is one. A scan reports one for a
    /// release stating no correction, and the count a person means is one less than that.
    /// </summary>
    [TestCase("Movie.Title.2018.720p.HDTV.x264-aAF", 0, 0)]
    [TestCase("Movie.Title.2018.PROPER.720p.HDTV.x264-aAF", 1, 0)]
    [TestCase("Movie.Title.2018.REPACK2.720p.HDTV.x264-aAF", 2, 0)]
    [TestCase("Movie.Title.2018.REAL.PROPER.720p.HDTV.x264-aAF", 1, 1)]
    public void CountsCorrectionsFromZero(string releaseTitle, int corrections, int mislabels)
    {
        var read = Read(releaseTitle);

        Assert.Multiple(() =>
        {
            Assert.That(read.Corrections.Or(-1), Is.EqualTo(corrections));
            Assert.That(read.Mislabels.Or(-1), Is.EqualTo(mislabels));
        });
    }

    /// <summary>
    /// A screener is a high-fidelity signal with a burn-in, and saying so is more useful than pretending it
    /// is worse than a broadcast capture. Its origin stays its origin and the burn-in rides the defect axis.
    /// </summary>
    [Test]
    public void ReadsAScreenerAsItsOwnSourcePlusABurnIn()
    {
        var read = Read("Movie.Title.2011.DVDSCR.XviD-GROUP");

        Assert.Multiple(() =>
        {
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.StandardDefinitionDisc));
            Assert.That(read.Flaws.Has(VideoFlaw.Watermarked), Is.True);
        });
    }

    /// <summary>
    /// <b>The refinement seam, doing exactly what it is for.</b> A bracketed disc marker is a naming habit
    /// of one community, so the family knows nothing about it and this kind contributes the reading — at
    /// the weakest provenance there is, which is what stops a guess overwriting a measurement and what
    /// stops it refusing anything. The evidence is built directly here rather than scanned, because what is
    /// under test is the seam and not which spellings a scanner happens to cover.
    /// </summary>
    [Test]
    public void LetsThisKindContributeItsOwnBracketedConventions()
    {
        var silent = new ReleaseEvidence { Title = "[FFF] Movie Name - 01 [720p-AAC][0601BED4]" };
        var dialect = silent with { Guards = Guards(MoviesVideoRefinement.AnimeDiscGuard) };

        var plain = VideoQualityType.Read(silent);
        var refined = MoviesVideoRefinement.Refine(VideoQualityType.Read(dialect), dialect);

        Assert.Multiple(() =>
        {
            Assert.That(
                plain.Origin.IsKnown,
                Is.False,
                "The shared family knows nothing about a bracketed disc marker, and should not.");
            Assert.That(refined.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.HighDefinitionDisc));
            Assert.That(refined.Origin.Source, Is.EqualTo(EvidenceSource.Assumed));
            Assert.That(refined.Generation.Or(-1), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// <b>The strengthen-only rule, which is what makes the seam safe.</b> A refinement contributes at the
    /// weakest provenance, so it can complete what the family left absent and can never overrule what the
    /// family read — here the family reads a stream from the release's own token and the kind's disc
    /// convention, fired deliberately on the same release, changes nothing.
    /// </summary>
    [Test]
    public void NeverLetsAKindsGuessOverruleWhatTheFamilyRead()
    {
        var evidence = new ReleaseEvidence
        {
            Title = "[HorribleSubs] Movie Title! 2018 [Web][MKV][h264][480p][AAC 2.0][Softsubs]",
            SourceToken = EvidenceSourceTokens.Web,
            StatedResolution = 480,
            Guards = Guards(MoviesVideoRefinement.AnimeDiscGuard),
        };

        var read = MoviesVideoRefinement.Refine(VideoQualityType.Read(evidence), evidence);

        Assert.Multiple(() =>
        {
            Assert.That(read.Origin.Or((VideoOrigin)(-1)), Is.EqualTo(VideoOrigin.Stream));
            Assert.That(read.Origin.Source, Is.EqualTo(EvidenceSource.ReleaseTitle));
            Assert.That(read.Resolution.Or(0), Is.EqualTo(480));
        });
    }

    private static IReadOnlySet<string> Guards(params string[] guardIds) =>
        new HashSet<string>(guardIds, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <b>A measurement is not a claim.</b> A title claiming one raster over a file that measures another
    /// resolves in one place, with no rule naming any media kind — which is what an ordered provenance is
    /// for.
    /// </summary>
    [Test]
    public void LetsAMeasurementOverruleWhatTheTitleClaimed()
    {
        var claimed = Read("Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG");

        var measured = VideoQualityType.Read(new ReleaseEvidence
        {
            Title = "Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG",
            SourceToken = EvidenceSourceTokens.WebDownload,
            StatedResolution = 1080,
            Probe = new MediaProbe { Height = 720 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(claimed.Resolution.Or(0), Is.EqualTo(1080));
            Assert.That(claimed.Resolution.Source, Is.EqualTo(EvidenceSource.ReleaseTitle));
            Assert.That(measured.Resolution.Or(0), Is.EqualTo(720));
            Assert.That(measured.Resolution.Source, Is.EqualTo(EvidenceSource.ContainerProbe));
        });
    }

    /// <summary>
    /// The whole-disc probe fires on titles carrying no disc token at all, so what it contributes is a
    /// guess. It can inform a reading and a rendering; it cannot refuse a release, and that is enforced by
    /// the requirement's own minimum provenance rather than by anybody remembering.
    /// </summary>
    [Test]
    public void ContributesTheWholeDiscProbeAsAGuessAndNeverAsARefusal()
    {
        var guessed = Read("Movie Title 2005 1080p USA Blu-ray AVC DTS-HD MA 5.1-PTP");
        var stated = Read("Movie.Title.2013.BDISO");

        var point = MoviesDeclaration.Quality.Project(guessed);
        var explicitPoint = MoviesDeclaration.Quality.Project(stated);

        Assert.Multiple(() =>
        {
            Assert.That(guessed.Packaging.Or((Packaging)(-1)), Is.EqualTo(Packaging.DiscImage));
            Assert.That(guessed.Packaging.Source, Is.EqualTo(EvidenceSource.Assumed));
            Assert.That(
                MoviesDeclaration.Policy.Admits(point).IsAdmitted,
                Is.True,
                "A heuristic that fires on a title stating no disc must never refuse the release.");
            Assert.That(
                MoviesDeclaration.Policy.Admits(explicitPoint).IsAdmitted,
                Is.False,
                "An explicit disc-image token still refuses.");
        });
    }

    /// <summary>Reads one release title the way the host reads it, guards and all.</summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <returns>The refined facts.</returns>
    internal static VideoQuality Read(string releaseTitle) =>
        MoviesVideoRefinement.Refine(
            VideoQualityType.Read(Evidence(releaseTitle, MatchedGuards(releaseTitle))),
            Evidence(releaseTitle, MatchedGuards(releaseTitle)));

    private static ReleaseEvidence Evidence(string releaseTitle, IReadOnlySet<string> guards) =>
        MoviesGuards.Scan(releaseTitle, guards);

    private static IReadOnlySet<string> MatchedGuards(string releaseTitle) =>
        MoviesGuards.Matching(releaseTitle);
}
