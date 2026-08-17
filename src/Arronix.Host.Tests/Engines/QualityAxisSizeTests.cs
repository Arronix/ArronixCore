using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Quality.Families;
using Arronix.Host.Engines.Quality;
using FluentAssertions;

// The quality-axes model is experimental (ARX0021).
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The computed size gate: what a file at one point should weigh, and what to do when an input is missing.
/// </summary>
/// <remarks>
/// <para>
/// The cross-checks below are the model's own published derivation, re-run as assertions so that the claim
/// is checkable rather than asserted. Each row is a real release shape whose observed bitrate range is known
/// from the specifications the model is calibrated on; the computed center has to land inside it. A table of
/// megabytes per minute per rung cannot be checked this way at all, because there is nothing behind its
/// numbers to check them against.
/// </para>
/// <para>
/// The second group is the half a per-rung table never had: what happens when an input is absent, which at
/// the moment a release is offered is the common case. Two of the three failures the gate exists to catch —
/// a file far too small for what it claims and one far too large — are exactly the cases where an input is a
/// claim rather than a measurement, so the rules for absence are the interesting half of the model and not
/// an afterthought.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class QualityAxisSizeTests
{
    private const double Megabit = 1_000_000d;

    private IQualityType type = null!;

    [SetUp]
    public void SetUp() => type = QualityAxisFixtures.Registry().Get(QualityAxisFixtures.Family);

    private static IEnumerable<TestCaseData> CrossChecks()
    {
        yield return new TestCaseData(1080, 24d, VideoCodec.H264, VideoOrigin.Stream, 0, AudioPresentation.LossySurround, 4d, 8d)
            .SetName("A stream download at 1080 lines lands where published encoding ladders put it");

        yield return new TestCaseData(1080, 24d, VideoCodec.H264, VideoOrigin.HighDefinitionDiscBitstream, 0, AudioPresentation.Lossless, 25d, 38d)
            .SetName("A disc bitstream copy at 1080 lines lands inside the disc specification");

        yield return new TestCaseData(2160, 24d, VideoCodec.H265, VideoOrigin.HighDefinitionDiscBitstream, 0, AudioPresentation.LosslessObject, 50d, 80d)
            .SetName("A disc bitstream copy at 2160 lines lands inside its own specification");

        yield return new TestCaseData(1080, 24d, VideoCodec.H265, VideoOrigin.HighDefinitionDisc, 1, AudioPresentation.LossySurround, 2d, 8d)
            .SetName("A disc encode at 1080 lines lands where disc encodes are observed");

        yield return new TestCaseData(480, 30d, VideoCodec.Mpeg2, VideoOrigin.StandardDefinitionDiscBitstream, 0, AudioPresentation.LossySurround, 5d, 6.5d)
            .SetName("A standard-definition disc program stream lands on its published video rate");
    }

    [Test]
    [TestCaseSource(nameof(CrossChecks))]
    public void TheComputedCenterLandsInsideTheObservedRange(
        int lines,
        double frameRate,
        VideoCodec codec,
        VideoOrigin origin,
        int generation,
        AudioPresentation audio,
        double lowMegabits,
        double highMegabits)
    {
        var duration = TimeSpan.FromMinutes(100);

        var expectation = VideoSizeModel.Expect(
            lines,
            frameRate,
            (int)codec,
            (int)origin,
            generation,
            (int)audio,
            duration);

        var megabitsPerSecond = expectation.ExpectedBytes * 8d / duration.TotalSeconds / Megabit;

        megabitsPerSecond.Should().BeInRange(lowMegabits, highMegabits);
    }

    [Test]
    public void TheModelAnswersForCombinationsNoRungTableHasARowFor()
    {
        var expectation = VideoSizeModel.Expect(
            1440,
            60d,
            (int)VideoCodec.Av1,
            (int)VideoOrigin.Stream,
            0,
            (int)AudioPresentation.LossyObject,
            TimeSpan.FromMinutes(45));

        expectation.IsAssessable.Should().BeTrue(
            "an intermediate raster at sixty frames in a modern codec is a real release and a rung table has "
            + "no row for it, which is the whole difference between computing an answer and looking one up");

        expectation.Basis.Should().Contain("2560x1440").And.Contain("60 fps");
    }

    [Test]
    public void TheStandardDefinitionRastersAreTheDiscRastersAndNotSixteenByNine()
    {
        VideoSizeModel.WidthFor(480).Should().Be(720);
        VideoSizeModel.WidthFor(576).Should().Be(720);
        VideoSizeModel.WidthFor(1080).Should().Be(1920);
        VideoSizeModel.WidthFor(720).Should().Be(1280);
        VideoSizeModel.WidthFor(2160).Should().Be(3840);
        VideoSizeModel.WidthFor(540).Should().Be(960);
    }

    [Test]
    public void AFileFarTooSmallForWhatItClaimsFailsTheFloorAndOneFarTooLargeFailsTheCeiling()
    {
        var expectation = VideoSizeModel.Expect(
            2160,
            24d,
            (int)VideoCodec.H265,
            (int)VideoOrigin.HighDefinitionDiscBitstream,
            0,
            (int)AudioPresentation.LosslessObject,
            TimeSpan.FromMinutes(120));

        expectation.Assess(200L * 1024 * 1024).Should().Be(SizeVerdict.ImplausiblySmall);
        expectation.Assess(expectation.ExpectedBytes).Should().Be(SizeVerdict.Plausible);
        expectation.Assess(expectation.ExpectedBytes * 10).Should().Be(SizeVerdict.ImplausiblyLarge);
    }

    [Test]
    public void ABitstreamCopyAtAnUnusualResolutionIsTestedRatherThanRenamed()
    {
        // A ladder renamed a bitstream copy at 720 lines to the nearest word it had, on the grounds that
        // such a thing is not a real release. That is a claim about bitrate, and the model tests it directly
        // on the actual file: a genuine bitstream copy passes, and a group lying about a small re-encode
        // fails the floor with a reason a person can read.
        var duration = TimeSpan.FromMinutes(100);

        var expectation = VideoSizeModel.Expect(
            720,
            24d,
            (int)VideoCodec.H264,
            (int)VideoOrigin.HighDefinitionDiscBitstream,
            0,
            (int)AudioPresentation.Lossless,
            duration);

        var centerMegabits = expectation.ExpectedBytes * 8d / duration.TotalSeconds / Megabit;

        centerMegabits.Should().BeInRange(10d, 17d);

        var honest = (long)(13d * Megabit / 8d * duration.TotalSeconds);
        var lying = (long)(2d * Megabit / 8d * duration.TotalSeconds);

        expectation.Assess(honest).Should().Be(SizeVerdict.Plausible);
        expectation.Assess(lying).Should().Be(SizeVerdict.ImplausiblySmall);
    }

    [Test]
    public void AnAbsentResolutionIsUnassessableBecauseNoCenterIsDefensible()
    {
        VideoSizeModel.Expect(null, 24d, (int)VideoCodec.H264, (int)VideoOrigin.Stream, 0, null, TimeSpan.FromMinutes(90))
            .IsAssessable.Should().BeFalse();
    }

    [Test]
    public void AnAbsentOriginAndGenerationTogetherAreUnassessableAndEitherAloneIsNot()
    {
        VideoSizeModel.Expect(1080, 24d, (int)VideoCodec.H264, null, null, null, TimeSpan.FromMinutes(90))
            .IsAssessable.Should().BeFalse("the master factor spans tenfold, wider than the band itself");

        VideoSizeModel.Expect(1080, 24d, (int)VideoCodec.H264, null, 1, null, TimeSpan.FromMinutes(90))
            .IsAssessable.Should().BeTrue("a rip targets below its source whatever its source was");

        VideoSizeModel.Expect(1080, 24d, (int)VideoCodec.H264, (int)VideoOrigin.HighDefinitionDisc, null, null, TimeSpan.FromMinutes(90))
            .IsAssessable.Should().BeTrue("a rip member says its own generation is at least one");
    }

    [Test]
    public void AnUnknownDurationIsUnassessableBecauseSizeHasNoOtherTerm()
    {
        VideoSizeModel.Expect(1080, 24d, (int)VideoCodec.H264, (int)VideoOrigin.Stream, 0, null, TimeSpan.Zero)
            .IsAssessable.Should().BeFalse();
    }

    [Test]
    public void AnAbsentFrameRateAndAnAbsentCodecWidenTheBandRatherThanRefusingToAnswer()
    {
        var duration = TimeSpan.FromMinutes(100);

        var stated = VideoSizeModel.Expect(1080, 24d, (int)VideoCodec.H264, (int)VideoOrigin.Stream, 0, null, duration);
        var noFrameRate = VideoSizeModel.Expect(1080, null, (int)VideoCodec.H264, (int)VideoOrigin.Stream, 0, null, duration);
        var noCodec = VideoSizeModel.Expect(1080, 24d, null, (int)VideoOrigin.Stream, 0, null, duration);

        noFrameRate.ExpectedBytes.Should().Be(stated.ExpectedBytes, "twenty-four is the assumption");
        noFrameRate.CeilingBytes.Should().BeGreaterThan(stated.CeilingBytes);

        noCodec.ExpectedBytes.Should().Be(stated.ExpectedBytes, "the reference codec is the assumption");
        noCodec.CeilingBytes.Should().BeGreaterThan(stated.CeilingBytes);
        noCodec.FloorBytes.Should().BeLessThan(
            stated.FloorBytes,
            "assuming the reference codec for what is really a modern one over-predicts, so the floor has to "
            + "move further than the ceiling or a perfectly good small file is rejected");
    }

    [Test]
    public void AnUnassessableExpectationIsAPassAndNeverARejection()
    {
        var point = type.Project(new VideoQualityFacts());

        type.ExpectedSize(point, TimeSpan.FromMinutes(90)).Assess(1)
            .Should().Be(
                SizeVerdict.NotAssessable,
                "a release nobody can assess is not thereby implausible, and a size gate that refused what it "
                + "could not measure would be a requirement wearing a measurement's clothes");
    }

    [Test]
    public void TheGateReachesThroughTheEngineAndTheBasisExplainsItself()
    {
        var evaluator = QualityAxisFixtures.Evaluator();

        var point = evaluator.Read(
            QualityAxisFixtures.Family,
            new ReleaseEvidence
            {
                Title = "Some.Release.2011.1080p.BluRay.REMUX.AVC.TrueHD.Atmos",
                SourceToken = "bluray",
                StatedResolution = 1080,
                StatedResolutionForm = ResolutionClaimForm.LineCount,
                VideoCodecToken = "h264",
                AudioToken = "truehd-atmos",
                StatedFrameRate = 24d,
                IsRemux = true,
            });

        var duration = TimeSpan.FromMinutes(100);
        var expectation = evaluator.ExpectedSize(point, duration);

        expectation.Basis.Should()
            .Contain("1920x1080").And
            .Contain("0.100 bits per pixel").And
            .Contain("master factor 5.0").And
            .Contain("6000 kbit/s of audio");

        evaluator.AssessSize(point, expectation.ExpectedBytes, duration).Should().Be(SizeVerdict.Plausible);
        evaluator.AssessSize(point, 300L * 1024 * 1024, duration).Should().Be(SizeVerdict.ImplausiblySmall);
    }

    [Test]
    public void AFamilyDeclaringNoSizeModelSaysSoRatherThanInventingARange()
    {
        var registry = new QualityFamilyRegistry();

        registry.Add<UnmeasuredFacts, UnmeasuredType>();

        var unmeasured = registry.Get(FormatFamilyId.From("unmeasured"));
        var point = unmeasured.Project(new UnmeasuredFacts
        {
            Form = Evidence<int>.From(2, EvidenceSource.ContainerProbe),
        });

        unmeasured.ExpectedSize(point, TimeSpan.FromHours(3)).IsAssessable.Should().BeFalse();
    }
}

/// <summary>A family whose files have no size a model could predict.</summary>
internal sealed class UnmeasuredFacts : IQualityFacts
{
    /// <summary>Gets how the copy carries what it carries.</summary>
    [Axis]
    public Evidence<int> Form { get; init; }
}

/// <summary>The family declaring it.</summary>
internal sealed class UnmeasuredType : IQualityType<UnmeasuredFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("unmeasured");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<UnmeasuredFacts> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Named("Unmeasured").Label(f => true, "Copy");
    }

    /// <inheritdoc />
    public static UnmeasuredFacts Read(ReleaseEvidence evidence) => new();
}
