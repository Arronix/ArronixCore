using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Quality;
using FluentAssertions;

// The quality-axes model is experimental (ARX0021).
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// Rendering a point in the community's vocabulary, and reading one of those renderings back.
/// </summary>
/// <remarks>
/// <para>
/// The stance under test is the design's, and it is a change from what a rung ladder ships: <b>render what
/// the point says</b>. A bitstream copy at 480 lines renders as a bitstream copy at 480 lines, because that
/// is what the release states and what the file is; a ladder renamed it to the nearest rung it had a word
/// for, which was not a correction but a taxonomy with nowhere to put the truth. The claim behind the
/// rename — that such a file is implausible — is now a computation over the real file, which is a better
/// mechanism than a hard-coded rename that also silences the honest case.
/// </para>
/// <para>
/// The round-trip asserted here is <b>label to point to label</b>, and it is the only one that can hold: a
/// label is a rendering and a rendering is lossy, so point-to-label-to-point cannot be an identity and is
/// not claimed to be.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class QualityAxisLabelTests
{
    private IQualityType type = null!;

    [SetUp]
    public void SetUp() => type = QualityAxisFixtures.Registry().Get(QualityAxisFixtures.Family);

    private static IEnumerable<TestCaseData> Renderings()
    {
        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.Stream),
                Generation = Read(0),
                Resolution = Read(1080),
            },
            "WEBDL-1080p").SetName("A stream at generation zero is a stream download");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.Stream),
                Generation = Read(1),
                Resolution = Read(1080),
            },
            "WEBRip-1080p").SetName("A stream re-encoded once is a stream rip");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.Stream),
                Resolution = Read(720),
            },
            "WEBDL-720p").SetName("A stream whose generation nothing states still renders");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.HighDefinitionDisc),
                Generation = Read(1),
                Resolution = Read(1080),
            },
            "Bluray-1080p").SetName("A disc encode is the disc word");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.BroadcastBitstream),
                Generation = Read(0),
                Resolution = Read(1080),
            },
            "Raw-HD").SetName("An untouched transport stream takes no resolution suffix");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.StandardDefinitionDiscBitstream),
                Generation = Read(0),
                Resolution = Read(480),
            },
            "DVD").SetName("Both standard-definition disc members render one word");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.StandardDefinitionDisc),
                Generation = Read(1),
                Resolution = Read(480),
            },
            "DVD").SetName("A standard-definition disc encode renders the same word as its bitstream");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.Broadcast),
                Generation = Read(1),
                Resolution = Read(1080),
            },
            "HDTV-1080p").SetName("A high-definition transmission is the broadcast word");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.Broadcast),
                Generation = Read(1),
                Resolution = Read(480),
            },
            "SDTV").SetName("A standard-definition transmission takes no suffix");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.CameraCapture),
                Audio = Read(AudioPresentation.LossyStereo),
            },
            "TELESYNC").SetName("A re-photographed projection with a line feed is named for its audio");

        yield return new TestCaseData(
            new VideoQualityFacts { Origin = Read(VideoOrigin.CameraCapture) },
            "CAM").SetName("A re-photographed projection with room sound is the camera word");

        yield return new TestCaseData(
            new VideoQualityFacts { Packaging = Read(Packaging.DiscImage), Resolution = Read(1080) },
            "BR-DISK").SetName("Packaging outranks every other word");

        yield return new TestCaseData(
            new VideoQualityFacts(),
            "Unknown").SetName("A point stating nothing renders the word for nothing");
    }

    private static IEnumerable<TestCaseData> TruthfulButNovel()
    {
        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.HighDefinitionDiscBitstream),
                Generation = Read(0),
                Resolution = Read(480),
            },
            "Remux-480p").SetName("A disc bitstream copy at 480 lines says so");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.HighDefinitionDiscBitstream),
                Generation = Read(0),
                Resolution = Read(720),
            },
            "Remux-720p").SetName("A disc bitstream copy at 720 lines says so");

        yield return new TestCaseData(
            new VideoQualityFacts
            {
                Origin = Read(VideoOrigin.Stream),
                Generation = Read(0),
                Resolution = Read(540),
            },
            "WEBDL-540p").SetName("An intermediate raster is not rounded to a raster somebody tabulated");

        yield return new TestCaseData(
            new VideoQualityFacts { Resolution = Read(2160) },
            "2160p").SetName("A resolution with no stated origin renders the resolution rather than nothing");

        yield return new TestCaseData(
            new VideoQualityFacts { Resolution = Read(1440) },
            "1440p").SetName("A raster no ladder has a rung for renders as itself");
    }

    [Test]
    [TestCaseSource(nameof(Renderings))]
    [TestCaseSource(nameof(TruthfulButNovel))]
    public void RendersThePointRatherThanTheNearestRung(VideoQualityFacts facts, string expected) =>
        type.Label(type.Project(facts), QualityLabelDetail.Standard).Should().Be(expected);

    [Test]
    [TestCaseSource(nameof(Renderings))]
    [TestCaseSource(nameof(TruthfulButNovel))]
    public void EveryLabelItRendersItReadsBack(VideoQualityFacts facts, string expected)
    {
        type.TryParseLabel(expected, out var point).Should().BeTrue(
            "a label this renderer produced is a label it must understand");

        type.Label(point, QualityLabelDetail.Standard).Should().Be(expected);
    }

    [Test]
    public void TheFullLabelSpellsACorrectionAndTheStandardOneDoesNot()
    {
        var point = type.Project(new VideoQualityFacts
        {
            Origin = Read(VideoOrigin.Stream),
            Generation = Read(0),
            Resolution = Read(1080),
            Corrections = Read(1),
        });

        type.Label(point, QualityLabelDetail.Standard).Should().Be("WEBDL-1080p");
        type.Label(point, QualityLabelDetail.Full).Should().Be("WEBDL-1080p Proper");
    }

    [Test]
    public void AFirstIssueSpellsNoCorrectionAtAll()
    {
        var point = type.Project(new VideoQualityFacts
        {
            Origin = Read(VideoOrigin.Stream),
            Generation = Read(0),
            Resolution = Read(1080),
            Corrections = Read(0),
            Mislabels = Read(0),
        });

        type.Label(point, QualityLabelDetail.Full).Should().Be(
            "WEBDL-1080p",
            "a release stating that nothing has been corrected states nothing, and an empty part is elided");
    }

    [Test]
    public void BothCorrectionCountsSpellInTheOrderTheyWereDeclared()
    {
        var point = type.Project(new VideoQualityFacts
        {
            Origin = Read(VideoOrigin.HighDefinitionDisc),
            Generation = Read(1),
            Resolution = Read(2160),
            Corrections = Read(2),
            Mislabels = Read(1),
        });

        type.Label(point, QualityLabelDetail.Full).Should().Be("Bluray-2160p Proper REAL");
    }

    [Test]
    public void AFullLabelReadsBackToAFullLabel()
    {
        type.TryParseLabel("Bluray-2160p Proper REAL", out var point).Should().BeTrue();

        type.Label(point, QualityLabelDetail.Full).Should().Be("Bluray-2160p Proper REAL");
        type.Label(point, QualityLabelDetail.Standard).Should().Be("Bluray-2160p");
        type.Label(point, QualityLabelDetail.Source).Should().Be("Bluray");
    }

    [Test]
    public void ALabelThisRendererCannotProduceIsRefusedRatherThanGuessedAt()
    {
        type.TryParseLabel("Bluray-1080p Remux", out _).Should().BeFalse(
            "another family may spell a bitstream copy that way and this one does not, and a parse that "
            + "cannot be re-rendered is a parse that invented something");

        type.TryParseLabel("HD-DVD", out _).Should().BeFalse();
        type.TryParseLabel("   ", out _).Should().BeFalse();
    }

    [Test]
    public void TheDiagnosticViewSpellsEveryKnownAxisAndNoAbsentOne()
    {
        var point = type.Project(new VideoQualityFacts
        {
            Origin = Read(VideoOrigin.Stream),
            Generation = Read(0),
            Resolution = Read(1080),
            Audio = Read(AudioPresentation.LossyObject),
        });

        var rendered = type.Label(point, QualityLabelDetail.Diagnostic);

        rendered.Should().Contain("Stream").And.Contain("1080 lines").And.Contain("LossyObject");
        rendered.Should().NotContain("Repacked", "an axis nothing spoke to is not spelled as anything");
    }

    [Test]
    public void ARenderingRuleOutsideTheGrammarFailsWhenTheFamilyIsRegisteredAndNamesTheExpression()
    {
        var registering = () => QualityTypeFactory.Create<VideoQualityFacts, UngrammaticalLabelType>();

        registering.Should().Throw<ArgumentException>()
            .WithMessage("*outside the rendering grammar*");
    }

    private static Evidence<TValue> Read<TValue>(TValue value)
        where TValue : struct =>
        Evidence<TValue>.From(value, EvidenceSource.ReleaseTitle);
}

/// <summary>A family whose rendering rule reaches outside the grammar, so that the refusal is asserted.</summary>
internal sealed class UngrammaticalLabelType : IQualityType<VideoQualityFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("ungrammatical");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<VideoQualityFacts> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Label(f => f.Origin.ToString()!.Length > 3, "Nonsense");
    }

    /// <inheritdoc />
    public static VideoQualityFacts Read(ReleaseEvidence evidence) => new();
}
