using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Quality;
using FluentAssertions;

// The quality-axes model is experimental (ARX0021).
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// What the engine decides, and why.
/// </summary>
/// <remarks>
/// <para>
/// The four pairs at the top are the acceptance test for the whole model, and they are chosen because a
/// single ordered ladder cannot hold all four at once with one mechanism. A service's transmission and a
/// re-encode of it are the same thing to a viewer, and the platform must not re-download between them. A
/// disc's own bitstream and a re-encode of that disc are a fivefold bitrate cliff, and the platform must
/// take the upgrade. Those two are the same step — one lossy re-encode — and opposite preferences, so no
/// setting on a single re-encode count can express both. Separating <i>which master</i> from <i>how far
/// from it</i> is what makes both fall out of one shipped opinion with no per-title rule anywhere.
/// </para>
/// <para>
/// The second group is the rule that stops an unbounded download loop. A release title claiming a
/// resolution the held file was measured not to have is not an upgrade, however the ordering ranks it,
/// because a claim never outranks a measurement. Without it, deleting a per-kind list of sources whose
/// claims are ignored re-creates the loop rather than fixing it: only the held side is ever probed, so the
/// claim wins on every pass, forever.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class QualityAxisDecisionTests
{
    private AxisQualityEvaluator evaluator = null!;
    private IQualityType type = null!;

    [SetUp]
    public void SetUp()
    {
        var registry = QualityAxisFixtures.Registry();

        type = registry.Get(QualityAxisFixtures.Family);
        evaluator = new AxisQualityEvaluator(registry);
    }

    [Test]
    public void AStreamRipAndAStreamDownloadOfOneResolutionAreTheSameAndNeitherIsGrabbed()
    {
        var held = Read("Some.Release.2019.1080p.WEB-Rip.DDP.5.1.HEVC", source: "webrip", lines: 1080);
        var candidate = Read("Some.Release.2019.1080p.AMZN.WEB-DL.DDP.5.1.H264", source: "webdl", lines: 1080);

        evaluator.Compare(held, candidate).Should().Be(
            QualityJudgment.Same,
            "the same master and the same raster, and one re-encode of a service's own transmission is a "
            + "few percent rather than a cliff");

        evaluator.Decide(held, candidate).Verdict.Should().BeOneOf(
            GrabVerdict.NotAnUpgrade,
            GrabVerdict.AlreadyGoodEnough);
    }

    [Test]
    public void ADiscBitstreamCopyIsAboveADiscEncodeOfTheSameResolution()
    {
        var held = Read("Some.Release.2011.1080p.BluRay.x264", source: "bluray", lines: 1080);
        var candidate = Read(
            "Some.Release.2011.1080p.BluRay.REMUX.AVC",
            source: "bluray",
            lines: 1080,
            remux: true);

        evaluator.Compare(held, candidate).Should().Be(
            QualityJudgment.Better,
            "which master the file carries is the axis that separates them, and holding the master itself is "
            + "a different bitrate class from holding an encode of it");
    }

    [Test]
    public void AReEncodeOfAnExistingRipIsBelowTheRipItCameFrom()
    {
        var held = Read("Some.Release.2011.1080p.BDRip.x264", source: "bdrip", lines: 1080);
        var candidate = Read("Some.Release.2011.1080p.BRRip.x264", source: "brrip", lines: 1080);

        evaluator.Compare(held, candidate).Should().Be(
            QualityJudgment.Worse,
            "the same master and the same raster, so the re-encode count decides — and a second re-encode is "
            + "past the point where the count was tied");
    }

    [Test]
    public void AnUntouchedTransportStreamIsAboveATransmissionCapture()
    {
        var held = Read("Some.Release.2015.1080i.HDTV.DD5.1.x264", source: "hdtv", lines: 1080, codec: "x264");
        var candidate = Read(
            "Some.Release.2015.1080i.HDTV.DD5.1.MPEG2",
            source: "hdtv",
            lines: 1080,
            codec: "mpeg2");

        evaluator.Compare(held, candidate).Should().Be(
            QualityJudgment.Better,
            "a transmission carried in the codec broadcasters transmit in, with nothing saying it was "
            + "re-encoded, is the transport stream itself");
    }

    [Test]
    public void AClaimNeverOutranksAMeasurementAndTheReGrabLoopTerminates()
    {
        // The release says 1080. Nothing is held, so it is taken.
        var claimed = Read("Some.Release.2020.1080p.WEB-DL.H264", source: "webdl", lines: 1080);

        evaluator.Decide(null, claimed).Verdict.Should().Be(GrabVerdict.Grab);

        // It is imported and probed, and the probe measures 720. That is the reading that now stands.
        var measured = Read(
            "Some.Release.2020.1080p.WEB-DL.H264",
            source: "webdl",
            lines: 1080,
            probe: new MediaProbe { Height = 720, VideoCodec = "h264" });

        measured[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Resolution))].Value.Magnitude
            .Should().Be(720, "a measurement overrules a claim with no rule naming any media kind");

        // The identical release is offered again on every pass thereafter, and is declined every time.
        for (var pass = 1; pass <= 5; pass++)
        {
            var decision = evaluator.Decide(measured, claimed);

            decision.Verdict.Should().Be(
                GrabVerdict.NotAnUpgrade,
                $"pass {pass} offers a claim about a file we have already measured");

            decision.Reason.Should().Contain("we measured");
        }
    }

    [Test]
    public void TheProvenanceRuleDoesNotFireWhenBothSidesSpeakAtTheSameStrength()
    {
        var held = Read("Some.Release.2020.720p.WEB-DL.H264", source: "webdl", lines: 720);
        var candidate = Read("Some.Release.2020.1080p.WEB-DL.H264", source: "webdl", lines: 1080);

        evaluator.Decide(held, candidate).Verdict.Should().Be(
            GrabVerdict.Grab,
            "two title claims are evidence of equal standing, and refusing every claim would refuse every "
            + "upgrade the platform can ever see before it downloads one");
    }

    [Test]
    public void OnceTheHeldFileSatisfiesTheCutoffAGenuineUpgradeIsDeclined()
    {
        var held = Read("Some.Release.2011.1080p.BluRay.x264", source: "bluray", lines: 1080);
        var candidate = Read(
            "Some.Release.2011.1080p.BluRay.REMUX.AVC",
            source: "bluray",
            lines: 1080,
            remux: true);

        evaluator.Compare(held, candidate).Should().Be(QualityJudgment.Better);

        var decision = evaluator.Decide(held, candidate);

        decision.Verdict.Should().Be(
            GrabVerdict.AlreadyGoodEnough,
            "a cutoff that a better candidate could walk through would not be a cutoff at all");

        decision.Reason.Should().Contain("Bluray-1080p");
    }

    [Test]
    public void NothingHeldIsGrabbedAndSaysSo()
    {
        var candidate = Read("Some.Release.2020.2160p.WEB-DL.H265", source: "webdl", lines: 2160);

        evaluator.Decide(null, candidate).Should().Be(new GrabDecision(GrabVerdict.Grab, "Nothing held."));
    }

    [Test]
    public void ARefusedCandidateNamesTheAxisAndTheValueThatRefusedIt()
    {
        var candidate = Read("Some.Release.2013.BDISO", source: "bluray", lines: 1080, packaging: "bdiso");

        var decision = evaluator.Decide(null, candidate);

        decision.Verdict.Should().Be(GrabVerdict.Refused);
        decision.Reason.Should().Contain("Packaging").And.Contain("DiscImage");
    }

    [Test]
    public void ARefusalActsOnAStatedReadingAndNotOnAGuess()
    {
        // The same packaging, contributed by a naming heuristic rather than stated by the release. The
        // shipped refusal will not act on a guess, so the release is admitted and ranked on what it does
        // say — which is the difference between a taxonomy that loses releases silently and one that does
        // not.
        var guessed = type.Project(new VideoQualityFacts
        {
            Origin = Evidence<VideoOrigin>.From(VideoOrigin.HighDefinitionDisc, EvidenceSource.ReleaseTitle),
            Generation = Evidence<int>.From(1, EvidenceSource.ReleaseTitle),
            Resolution = Evidence<int>.From(1080, EvidenceSource.ReleaseTitle),
            Packaging = Evidence<Packaging>.From(Packaging.DiscImage, EvidenceSource.Assumed),
        });

        evaluator.Admits(guessed).IsAdmitted.Should().BeTrue();
        evaluator.Decide(null, guessed).Verdict.Should().Be(GrabVerdict.Grab);
    }

    [Test]
    public void ADecisionAlwaysNamesTheAxisThatProducedIt()
    {
        var held = Read("Some.Release.2020.720p.WEB-DL.H264", source: "webdl", lines: 720);
        var candidate = Read("Some.Release.2020.480p.WEB-DL.H264", source: "webdl", lines: 480);

        evaluator.Decide(held, candidate).Reason.Should().Contain(
            "Resolution",
            "that a reason names an axis rather than a rung is the whole usability argument: a rung number "
            + "tells a user nothing about what to change");
    }

    [Test]
    public void TwoFamiliesAreNeverRankedAgainstOneAnother()
    {
        var video = Read("Some.Release.2020.1080p.WEB-DL.H264", source: "webdl", lines: 1080);
        var foreign = video with { Family = FormatFamilyId.From("written") };

        var comparing = () => evaluator.Compare(foreign, video);

        comparing.Should().Throw<ArgumentException>().WithMessage("*never ranked against one another*");
    }

    [Test]
    public void AUserPolicyReplacesTheFamilyOpinionEntirelyAndOneChipChangesOneThing()
    {
        var held = Read("Some.Release.2019.1080p.WEB-Rip.HEVC", source: "webrip", lines: 1080);
        var candidate = Read("Some.Release.2019.1080p.WEB-DL.H264", source: "webdl", lines: 1080);

        evaluator.Compare(held, candidate).Should().Be(QualityJudgment.Same);

        // The user who does want the re-encode count to separate a service's transmission from a rip of it
        // removes the ceiling. That is one chip, and it does exactly one thing.
        evaluator.Use(QualityPolicy.For(type, policy => policy
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Resolution)))
                .UpTo(AxisValue.Quantity(2160))
                .WhenUnknownRanksLowest()
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Origin)))
                .WhenUnknownRanksLowest()
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Generation)))
                .WhenUnknownAssume(AxisValue.Quantity(1))));

        evaluator.Compare(held, candidate).Should().Be(QualityJudgment.Better);
    }

    [Test]
    public void ThePolicyDescribesItselfInWordsAndSaysWhatItSilentlyDoes()
    {
        var prose = type.DefaultPolicy.Describe();

        prose.Should().Contain("Resolution".ToLowerInvariant());
        prose.Should().Contain("Good enough at");
        prose.Should().Contain("Never take");
        prose.Should().Contain(
            "Never re-download on a claim we have already measured",
            "the provenance rule is behavior a user would otherwise have to discover");
    }

    private QualityPoint Read(
        string title,
        string? source = null,
        int? lines = null,
        string? codec = null,
        bool remux = false,
        string? packaging = null,
        MediaProbe? probe = null) =>
        evaluator.Read(
            QualityAxisFixtures.Family,
            new ReleaseEvidence
            {
                Title = title,
                SourceToken = source,
                StatedResolution = lines,
                StatedResolutionForm = ResolutionClaimForm.LineCount,
                VideoCodecToken = codec,
                IsRemux = remux,
                PackagingToken = packaging,
                Probe = probe,
            });
}
