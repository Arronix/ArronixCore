// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using System;
using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Quality;

namespace Arronix.Abstractions.Tests.Quality.Support;

/// <summary>
/// A synthetic format family with the shape the video model has, so the policy can be exercised without
/// depending on a family that ships its own opinions.
/// </summary>
/// <remarks>
/// The member ranks below mirror a real ordering — a re-encoded master below the master's own bitstream —
/// because the behaviors worth pinning are relationships between those ranks, not the ranks themselves.
/// </remarks>
internal static class AxisFixtures
{
    internal static FormatFamilyId Video { get; } = FormatFamilyId.From("video");

    internal static FormatFamilyId Written { get; } = FormatFamilyId.From("written");

    internal static QualityAxisId Resolution { get; } = QualityAxisId.FromProperty("Resolution");

    internal static QualityAxisId Origin { get; } = QualityAxisId.FromProperty("Origin");

    internal static QualityAxisId Generation { get; } = QualityAxisId.FromProperty("Generation");

    internal static QualityAxisId Mislabels { get; } = QualityAxisId.FromProperty("Mislabels");

    internal static QualityAxisId Corrections { get; } = QualityAxisId.FromProperty("Corrections");

    internal static QualityAxisId DynamicRange { get; } = QualityAxisId.FromProperty("DynamicRange");

    internal static QualityAxisId Packaging { get; } = QualityAxisId.FromProperty("Packaging");

    internal static AxisValue CameraCapture { get; } = AxisValue.Member(0, "CameraCapture");

    internal static AxisValue Workprint { get; } = AxisValue.Member(1, "Workprint");

    internal static AxisValue Broadcast { get; } = AxisValue.Member(3, "Broadcast");

    internal static AxisValue BroadcastBitstream { get; } = AxisValue.Member(4, "BroadcastBitstream");

    internal static AxisValue Stream { get; } = AxisValue.Member(5, "Stream");

    internal static AxisValue HighDefinitionDisc { get; } = AxisValue.Member(8, "HighDefinitionDisc");

    internal static AxisValue HighDefinitionDiscBitstream { get; } = AxisValue.Member(9, "HighDefinitionDiscBitstream");

    internal static AxisValue SingleFile { get; } = AxisValue.Member(0, "SingleFile");

    internal static AxisValue DiscImage { get; } = AxisValue.Member(1, "DiscImage");

    internal static AxisValue DiscFolder { get; } = AxisValue.Member(2, "DiscFolder");

    internal static AxisValue StandardDynamicRange { get; } = AxisValue.Member(0, "SDR");

    internal static AxisValue HighDynamicRange10 { get; } = AxisValue.Member(2, "HDR10");

    internal static AxisValue HighDynamicRange10Plus { get; } = AxisValue.Member(3, "HDR10Plus");

    internal static AxisValue DolbyVision { get; } = AxisValue.Member(5, "DolbyVision");

    /// <summary>Gets a video-shaped quality type.</summary>
    internal static TestQualityType VideoType { get; } = new(
        Video,
        "Video",
        [
            Scalar(Resolution, "Resolution", "lines", greaterIsRicher: true),
            Ordinal(
                Origin,
                "Origin",
                [CameraCapture, Workprint, Broadcast, BroadcastBitstream, Stream, HighDefinitionDisc, HighDefinitionDiscBitstream]),
            Scalar(Generation, "Generation", "re-encodes", greaterIsRicher: false),
            Scalar(Mislabels, "Mislabel fixes", "corrections", greaterIsRicher: true),
            Scalar(Corrections, "Corrections", "corrections", greaterIsRicher: true),
            Nominal(DynamicRange, "Dynamic range", [StandardDynamicRange, HighDynamicRange10, HighDynamicRange10Plus, DolbyVision], multivalued: true),
            Nominal(Packaging, "Packaging", [SingleFile, DiscImage, DiscFolder], multivalued: false),
        ]);

    /// <summary>Gets a second family, so the cross-family guard has something to refuse.</summary>
    internal static TestQualityType WrittenType { get; } = new(
        Written,
        "Written",
        [Scalar(Resolution, "Resolution", "lines", greaterIsRicher: true)]);

    internal static QualityAxis Scalar(QualityAxisId id, string name, string unit, bool greaterIsRicher) =>
        new()
        {
            Id = id,
            Name = name,
            Form = AxisForm.Scalar,
            GreaterIsRicher = greaterIsRicher,
            Unit = unit,
        };

    internal static QualityAxis Ordinal(QualityAxisId id, string name, IReadOnlyList<AxisValue> members) =>
        new()
        {
            Id = id,
            Name = name,
            Form = AxisForm.Ordinal,
            GreaterIsRicher = true,
            Members = members,
        };

    internal static QualityAxis Nominal(
        QualityAxisId id,
        string name,
        IReadOnlyList<AxisValue> members,
        bool multivalued) =>
        new()
        {
            Id = id,
            Name = name,
            Form = AxisForm.Nominal,
            GreaterIsRicher = true,
            Members = members,
            Multivalued = multivalued,
        };

    /// <summary>Builds a point of the video family from readings stated in order.</summary>
    /// <param name="readings">The readings.</param>
    /// <returns>The point.</returns>
    internal static QualityPoint Point(params AxisReading[] readings) =>
        new() { Family = Video, Readings = readings };

    /// <summary>Builds a quantity reading at a stated source.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="magnitude">The quantity.</param>
    /// <param name="source">Where it was read.</param>
    /// <returns>The reading.</returns>
    internal static AxisReading Quantity(
        QualityAxisId axis,
        double magnitude,
        EvidenceSource source = EvidenceSource.ReleaseTitle) =>
        AxisReading.Of(axis, AxisValue.Quantity(magnitude), source);

    /// <summary>Builds a member reading at a stated source.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="member">The member.</param>
    /// <param name="source">Where it was read.</param>
    /// <returns>The reading.</returns>
    internal static AxisReading Member(
        QualityAxisId axis,
        AxisValue member,
        EvidenceSource source = EvidenceSource.ReleaseTitle) =>
        AxisReading.Of(axis, member, source);

    /// <summary>Declares the shipped video default in the vocabulary the contract actually offers.</summary>
    /// <returns>The policy.</returns>
    internal static QualityPolicy ShippedVideoDefault() => QualityPolicy.For(
        VideoType,
        policy => policy
            .Prefer(Resolution).UpTo(AxisValue.Quantity(2160)).WhenUnknownRanksLowest()
            .Prefer(Origin).WhenUnknownRanksLowest()
            .Prefer(Generation).UpTo(AxisValue.Quantity(1)).WhenUnknownAssume(AxisValue.Quantity(1))
            .Prefer(Mislabels).WhenUnknownAssume(AxisValue.Quantity(0))
            .Prefer(Corrections).WhenUnknownAssume(AxisValue.Quantity(0))
            .Facet(DynamicRange)
                .Worth(HighDynamicRange10, 10)
                .Worth(HighDynamicRange10Plus, 15)
                .Worth(DolbyVision, 15)
            .Refuse(Origin, CameraCapture, Workprint)
            .Refuse(Packaging, DiscImage, DiscFolder)
            .GoodEnoughAt(Resolution, AxisValue.Quantity(1080))
            .GoodEnoughAt(Generation, AxisValue.Quantity(1)));
}

/// <summary>A quality type that declares axes and nothing else, so a policy has something to compile against.</summary>
/// <param name="family">The family.</param>
/// <param name="name">The display name.</param>
/// <param name="axes">The declared axes.</param>
internal sealed class TestQualityType(FormatFamilyId family, string name, IReadOnlyList<QualityAxis> axes)
    : IQualityType
{
    private QualityPolicy? defaultPolicy;

    public FormatFamilyId Family { get; } = family;

    public string Name { get; } = name;

    public Type FactsType => typeof(object);

    public IReadOnlyList<QualityAxis> Axes { get; } = axes;

    public QualityPolicy DefaultPolicy => defaultPolicy ??= QualityPolicy.For(this, _ => { });

    public QualityPoint Read(ReleaseEvidence evidence) => throw new NotSupportedException();

    public QualityPoint Project(object facts) => throw new NotSupportedException();

    public string Label(QualityPoint point, QualityLabelDetail detail) =>
        string.Join(
            " ",
            point.Readings
                .Where(static reading => reading.IsKnown && reading.Values.Count > 0)
                .Select(static reading => reading.Value.Token));

    public bool TryParseLabel(string label, out QualityPoint point)
    {
        point = new QualityPoint { Family = Family, Readings = [] };

        return false;
    }

    public SizeExpectation ExpectedSize(QualityPoint point, TimeSpan duration) => SizeExpectation.NotAssessable;
}
