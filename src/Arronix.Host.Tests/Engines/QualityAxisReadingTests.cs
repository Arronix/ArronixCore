using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Quality;
using FluentAssertions;

// The quality-axes model is experimental (ARX0021).
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// Reading a facts type into axes, reading evidence onto them, and reconciling readings that disagree.
/// </summary>
/// <remarks>
/// The boundary under test is where strings stop. A release title and a probe go in; typed readings with
/// their provenance come out; and no ranking happens anywhere along the way, because there is nothing for
/// evidence to be collapsed to. What replaces a per-kind list of sources whose claims must be ignored is a
/// single ordering on where a reading came from, applied in one place.
/// </remarks>
[TestFixture]
internal sealed class QualityAxisReadingTests
{
    private IQualityType type = null!;

    [SetUp]
    public void SetUp() => type = QualityAxisFixtures.Registry().Get(QualityAxisFixtures.Family);

    [Test]
    public void AnAxisTakesItsIdentityFromItsPropertyAndItsProseFromItsDisplay()
    {
        var origin = type.Axes.Single(static axis => axis.Id.Value == nameof(VideoQualityFacts.Origin));

        origin.Id.Should().Be(QualityAxisId.FromProperty(nameof(VideoQualityFacts.Origin)));
        origin.Name.Should().Be("Origin");
        origin.Description.Should().NotBeNullOrWhiteSpace();
        origin.Form.Should().Be(AxisForm.Ordinal);
        origin.GreaterIsRicher.Should().BeTrue();
        origin.Members.Should().HaveCount(10);
        origin.Members[0].Token.Should().Be(nameof(VideoOrigin.CameraCapture));
        origin.Members[^1].DeclaredRank.Should().Be((int)VideoOrigin.HighDefinitionDiscBitstream);
    }

    [Test]
    public void FormAndPolarityAreDerivedFromTheTypeAndTheOneAttributeAndNowhereElse()
    {
        Axis(nameof(VideoQualityFacts.Resolution)).Form.Should().Be(AxisForm.Scalar);
        Axis(nameof(VideoQualityFacts.Resolution)).Unit.Should().Be("lines");
        Axis(nameof(VideoQualityFacts.Resolution)).Members.Should().BeEmpty();

        Axis(nameof(VideoQualityFacts.Generation)).GreaterIsRicher.Should().BeFalse(
            "more re-encodes is strictly less retained information, which is a fact and not a preference");

        Axis(nameof(VideoQualityFacts.DynamicRange)).Form.Should().Be(AxisForm.Nominal);
        Axis(nameof(VideoQualityFacts.DynamicRange)).Multivalued.Should().BeTrue();

        Axis(nameof(VideoQualityFacts.Packaging)).Form.Should().Be(AxisForm.Nominal);
        Axis(nameof(VideoQualityFacts.Packaging)).Multivalued.Should().BeFalse();
    }

    [Test]
    public void AnAxisOverAShapeTheDerivationHasNoFormForIsRefusedAtRegistration()
    {
        var registering = () => QualityTypeFactory.Create<IndecipherableFacts, IndecipherableType>();

        registering.Should().Throw<ArgumentException>()
            .WithMessage("*A boolean is none of those*");
    }

    [Test]
    public void AQuantityDeclaredUnorderedAndASetDeclaredOrderedAreBothRefused()
    {
        var unordered = () => QualityTypeFactory.Create<UnorderedQuantityFacts, UnorderedQuantityType>();
        var ordered = () => QualityTypeFactory.Create<OrderedSetFacts, OrderedSetType>();

        unordered.Should().Throw<ArgumentException>().WithMessage("*comparison undefined*");
        ordered.Should().Throw<ArgumentException>().WithMessage("*two sets that overlap*");
    }

    [Test]
    public void AFactsTypeDeclaringNoAxisIsRefusedRatherThanComparedEqualForever()
    {
        var registering = () => QualityTypeFactory.Create<SilentFacts, SilentType>();

        registering.Should().Throw<ArgumentException>().WithMessage("*declares no quality axis*");
    }

    [Test]
    public void AProjectionSurvivesTheRoundTripThroughAPointInBothDirections()
    {
        var facts = new VideoQualityFacts
        {
            Origin = Evidence<VideoOrigin>.From(VideoOrigin.Stream, EvidenceSource.ReleaseTitle),
            Generation = Evidence<int>.From(1, EvidenceSource.FileName),
            Resolution = Evidence<int>.From(1080, EvidenceSource.ContainerProbe),
            FrameRate = Evidence<double>.From(23.976, EvidenceSource.StreamProbe),
            DynamicRange = EvidenceSet<DynamicRangeFormat>.Of(
                EvidenceSource.ReleaseTitle,
                DynamicRangeFormat.DolbyVision,
                DynamicRangeFormat.HighDynamicRange10Plus),
        };

        var point = type.Project(facts);
        var expectation = type.ExpectedSize(point, TimeSpan.FromMinutes(100));

        // ExpectedSize is the one caller that has to rebuild the facts from the point, so its answer is
        // proof the projection inverts: a model reading five axes could not produce a figure otherwise.
        expectation.IsAssessable.Should().BeTrue();
        expectation.Basis.Should().Contain("1920x1080").And.Contain("23.98 fps");

        point[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Generation))].Source
            .Should().Be(EvidenceSource.FileName, "a reading carries where it came from, not just what it said");

        point[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.DynamicRange))].Values
            .Should().HaveCount(2, "a release may genuinely carry two dynamic-range formats at once");
    }

    [Test]
    public void AbsenceIsAStateAndNotAValueAtTheBottomOfTheOrder()
    {
        var point = type.Project(new VideoQualityFacts());

        foreach (var reading in point.Readings)
        {
            reading.IsKnown.Should().BeFalse();
            reading.Values.Should().BeEmpty();
        }

        var looked = type.Project(new VideoQualityFacts
        {
            Flaws = EvidenceSet<VideoFlaw>.Empty(EvidenceSource.ContainerProbe),
        });

        looked[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Flaws))].IsKnown.Should().BeTrue(
            "we looked and found nothing is a different claim from we did not look, and a policy refusing a "
            + "defect must not refuse a release it never inspected");
    }

    [Test]
    public void AMeasurementOverrulesAClaimWithNoRuleNamingAnyMediaKind()
    {
        var point = type.Read(new ReleaseEvidence
        {
            Title = "Some.Release.2020.1080p.WEB-DL.H264",
            SourceToken = "webdl",
            StatedResolution = 1080,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
            VideoCodecToken = "x264",
            Probe = new MediaProbe { Height = 720, VideoCodec = "hevc" },
        });

        var resolution = point[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Resolution))];
        var codec = point[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Codec))];

        resolution.Value.Magnitude.Should().Be(720);
        resolution.Source.Should().Be(EvidenceSource.ContainerProbe);
        codec.Value.Token.Should().Be(nameof(VideoCodec.H265));
    }

    [Test]
    public void WithinOneSourceTheMoreSpecificFormOfStatementWins()
    {
        var settled = EvidenceMerge.MostSpecific(
            EvidenceSource.ReleaseTitle,
            new EvidenceClaim<int>(2160, (int)ResolutionClaimForm.MarketingName),
            new EvidenceClaim<int>(1080, (int)ResolutionClaimForm.LineCount));

        settled.TryGet(out var lines).Should().BeTrue();
        lines.Should().Be(
            1080,
            "a release naming an explicit line count beside a marketing name has stated the line count, and "
            + "the marketing name is what the disc it came from was sold as");
    }

    [Test]
    public void AmongEquallySpecificClaimsTheLowestOneWins()
    {
        var settled = EvidenceMerge.MostSpecific(
            EvidenceSource.ReleaseTitle,
            new EvidenceClaim<int>(2160, (int)ResolutionClaimForm.LineCount),
            new EvidenceClaim<int>(1080, (int)ResolutionClaimForm.LineCount));

        settled.Or(0).Should().Be(
            1080,
            "the two failure directions are not symmetric: a missed claim leaves a release ranked low, and a "
            + "false claim promotes junk past everything the user asked for");
    }

    [Test]
    public void ARefinementMayCompleteAReadingAndMayNotOverwriteOne()
    {
        var registry = QualityAxisFixtures.Registry();

        registry.RefineWith<VideoQualityFacts, BracketConventionRefinement>();

        var type = registry.Get(QualityAxisFixtures.Family);
        var origin = QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Origin));

        var completed = type.Read(new ReleaseEvidence
        {
            Title = "[Group] Some Release - 01 [BD][720p-AAC]",
            StatedResolution = 720,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
            Guards = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bracket-disc" },
        });

        completed[origin].Value.Token.Should().Be(
            nameof(VideoOrigin.HighDefinitionDisc),
            "a kind's own naming dialect may turn absence into presence");

        completed[origin].Source.Should().Be(
            EvidenceSource.Assumed,
            "and it arrives at the weakest source there is, so it can inform ranking and cannot refuse");

        var overwritten = type.Read(new ReleaseEvidence
        {
            Title = "[Group] Some Release - 01 [BD][720p-AAC]",
            SourceToken = "webdl",
            StatedResolution = 720,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
            Guards = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bracket-disc" },
        });

        overwritten[origin].Value.Token.Should().Be(
            nameof(VideoOrigin.Stream),
            "and it may not overwrite what the family read from the release itself");
    }

    [Test]
    public void ABitstreamCopyWithNoSourceTokenIsStillADiscBitstreamCopy()
    {
        var point = type.Read(new ReleaseEvidence
        {
            Title = "Some.Release.2016.UHDRemux.2160p.HEVC.Dual.AC3.5.1",
            StatedResolution = 2160,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
            IsRemux = true,
        });

        type.Label(point, QualityLabelDetail.Standard).Should().Be("Remux-2160p");
        point[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Origin))].Source
            .Should().Be(EvidenceSource.Assumed, "it is an inference, and it says so");
    }

    [Test]
    public void AContainerIsEvidenceRatherThanAFallback()
    {
        var point = type.Read(new ReleaseEvidence
        {
            Title = "[Group] Some Release (540p) [AB649D32].mkv",
            StatedResolution = 540,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
            Container = ".mkv",
        });

        type.Label(point, QualityLabelDetail.Standard).Should().Be(
            "WEBDL-540p",
            "a bare line count inside a streaming container is a stream download and not a broadcast "
            + "capture, and the intermediate raster is what the release said");
    }

    [Test]
    public void ADualLanguageDiscEncodeWithNoRipTokenIsABitstreamCopy()
    {
        var point = type.Read(new ReleaseEvidence
        {
            Title = "Some.Release.1991.German.ML.1080p.BluRay.AVC",
            SourceToken = "bluray",
            StatedResolution = 1080,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
            Languages = [new LanguageClaim(Language.English, true)],
        });

        type.Label(point, QualityLabelDetail.Standard).Should().Be(
            "Remux-1080p",
            "the extra language track is why the release exists, and carrying it means carrying the whole "
            + "bitstream — which is a typed fact about the release and not a per-kind guard string");
    }

    [Test]
    public void AResolutionAClaimStatesAboutACameraDescribesTheCameraAndNotTheWork()
    {
        var point = type.Read(new ReleaseEvidence
        {
            Title = "Some.Release.2018.1080p.CAM",
            SourceToken = "cam",
            StatedResolution = 1080,
            StatedResolutionForm = ResolutionClaimForm.LineCount,
        });

        point[QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Resolution))].IsKnown.Should().BeFalse();
        type.Label(point, QualityLabelDetail.Standard).Should().Be("CAM");
    }

    [Test]
    public void OneFamilyRegistersOnceAndASecondDeclarationIsRefused()
    {
        var registry = QualityAxisFixtures.Registry();
        var again = () => registry.Add<VideoQualityFacts, VideoQualityType>();

        again.Should().Throw<ArgumentException>().WithMessage("*already has a quality model*");
    }

    [Test]
    public void ARefinementOfAFamilyNobodyDeclaredIsRefused()
    {
        var registry = new QualityFamilyRegistry();
        var refining = () => registry.RefineWith<VideoQualityFacts, BracketConventionRefinement>();

        refining.Should().Throw<ArgumentException>().WithMessage("*has no quality model to refine*");
    }

    private QualityAxis Axis(string property) =>
        type.Axes.Single(axis => axis.Id.Value == property);
}

/// <summary>A facts type whose axis is over a shape the derivation admits no form for.</summary>
internal sealed class IndecipherableFacts : IQualityFacts
{
    /// <summary>Gets a reading of two states with no stated order between them.</summary>
    [Axis]
    public Evidence<bool> Repacked { get; init; }
}

/// <summary>The family declaring it.</summary>
internal sealed class IndecipherableType : IQualityType<IndecipherableFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("indecipherable");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<IndecipherableFacts> builder)
    {
    }

    /// <inheritdoc />
    public static IndecipherableFacts Read(ReleaseEvidence evidence) => new();
}

/// <summary>A facts type declaring a quantity that does not order.</summary>
internal sealed class UnorderedQuantityFacts : IQualityFacts
{
    /// <summary>Gets a quantity whose comparison the declaration then removes.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    public Evidence<int> Resolution { get; init; }
}

/// <summary>The family declaring it.</summary>
internal sealed class UnorderedQuantityType : IQualityType<UnorderedQuantityFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("unordered-quantity");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<UnorderedQuantityFacts> builder)
    {
    }

    /// <inheritdoc />
    public static UnorderedQuantityFacts Read(ReleaseEvidence evidence) => new();
}

/// <summary>A facts type declaring a set that orders.</summary>
internal sealed class OrderedSetFacts : IQualityFacts
{
    /// <summary>Gets a set reading declared as though its members ranked.</summary>
    [Axis]
    public EvidenceSet<VideoFlaw> Flaws { get; init; }
}

/// <summary>The family declaring it.</summary>
internal sealed class OrderedSetType : IQualityType<OrderedSetFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("ordered-set");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<OrderedSetFacts> builder)
    {
    }

    /// <inheritdoc />
    public static OrderedSetFacts Read(ReleaseEvidence evidence) => new();
}

/// <summary>A facts type stating nothing about any file.</summary>
internal sealed class SilentFacts : IQualityFacts
{
    /// <summary>Gets a property that is not an axis, because nothing declares it one.</summary>
    public string? Note { get; init; }
}

/// <summary>The family declaring it.</summary>
internal sealed class SilentType : IQualityType<SilentFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("silent");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<SilentFacts> builder)
    {
    }

    /// <inheritdoc />
    public static SilentFacts Read(ReleaseEvidence evidence) => new();
}
