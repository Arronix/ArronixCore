
using System;
using System.Collections.Generic;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;

namespace Arronix.Format.Video;

/// <summary>The observable video representation carried by a release.</summary>
/// <remarks>
/// This models the release channel and transformations after the channel artifact, not an unknowable
/// studio mastering chain. Properties are facts available to policy; they are not automatically all
/// preference axes.
/// </remarks>
public sealed record Video : IRepresentation
{
    public required VideoLineage Lineage { get; init; }

    public VideoResolution? Resolution { get; init; }

    public ReleaseRevision Revision { get; init; } = ReleaseRevision.Initial;

    public VideoStream? Stream { get; init; }

    public IReadOnlyList<AudioTrack> AudioTracks { get; init; } = [];

    public IReadOnlySet<VideoDefectId> Defects { get; init; } = new HashSet<VideoDefectId>();

    public InterpretationTrace<Video> Trace { get; init; } =
        InterpretationTrace<Video>.Empty;
}

/// <summary>Observable lineage from a release channel artifact to this publication.</summary>
public sealed record VideoLineage(
    ReleaseChannel? Channel,
    SourceCarrier? Carrier,
    AcquisitionMethod? Acquisition,
    TranscodeHistory Transcoding);

/// <summary>The public release channel the artifact came through.</summary>
public enum ReleaseChannel
{
    Exhibition,
    Broadcast,
    Streaming,
    RetailPhysical
}

/// <summary>The carrier actually acquired.</summary>
public enum SourceCarrier
{
    ProjectedImage,
    BroadcastTransport,
    HostedFile,
    OpticalDisc
}

/// <summary>How the carrier was acquired.</summary>
public enum AcquisitionMethod
{
    CameraCapture,
    DirectCopy,
    StreamCapture,
    UnknownDerivedCopy
}

/// <summary>What is known about transcodes after acquisition of the channel artifact.</summary>
/// <param name="MinimumAdditionalEncodes">The minimum number established by the release vocabulary.</param>
/// <param name="ExactAdditionalEncodes">The exact number when it is actually known.</param>
public readonly record struct TranscodeHistory(int MinimumAdditionalEncodes, int? ExactAdditionalEncodes = null)
{
    public static TranscodeHistory None { get; } = new(0, 0);
    public static TranscodeHistory AtLeastOne { get; } = new(1);
    public static TranscodeHistory Unknown { get; } = new(0);
}

/// <summary>A typed raster, ordered by vertical resolution without inventing marketing buckets.</summary>
public readonly record struct VideoResolution(int Height, int? Width = null) : IComparable<VideoResolution>
{
    public int CompareTo(VideoResolution other) => Height.CompareTo(other.Height);
}

/// <summary>The correction history stated by a release.</summary>
public readonly record struct ReleaseRevision(int Version, int Real, bool Proper, bool Repack)
    : IComparable<ReleaseRevision>
{
    public static ReleaseRevision Initial { get; } = new(1, 0, false, false);

    public int CompareTo(ReleaseRevision other)
    {
        var version = Version.CompareTo(other.Version);
        if (version != 0)
        {
            return version;
        }

        var real = Real.CompareTo(other.Real);
        return real != 0 ? real : Proper.CompareTo(other.Proper);
    }
}

/// <summary>One video stream. Codec and dynamic-range identifiers are open extension vocabularies.</summary>
public sealed record VideoStream(
    VideoCodecId? Codec,
    int? BitDepth,
    ScanMode? Scan,
    double? FrameRate,
    IReadOnlySet<DynamicRangeId> DynamicRanges);

public enum ScanMode
{
    Progressive,
    Interlaced
}

/// <summary>One audio track; releases retain every track instead of selecting a synthetic richest one.</summary>
public sealed record AudioTrack(
    AudioCodecId? Codec,
    int? Channels,
    Language? Language,
    bool? Lossless,
    bool? ObjectBased,
    string? Title = null);

public readonly record struct VideoCodecId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct AudioCodecId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct DynamicRangeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct VideoDefectId(string Value)
{
    public override string ToString() => Value;
}
