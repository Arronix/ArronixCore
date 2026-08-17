using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What a container or stream probe measured. Measurements, never claims.</summary>
/// <remarks>
/// Every member here arrives as <see cref="EvidenceSource.ContainerProbe"/> or
/// <see cref="EvidenceSource.StreamProbe"/> and therefore overrules anything a release title said, with
/// no per-kind rule. That source ordering is the whole of what a per-kind "ignore the stated resolution
/// for these sources" list was working around.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MediaProbe
{
    /// <summary>Gets the measured frame width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the measured frame height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Gets the measured frame rate.</summary>
    public double? FrameRate { get; init; }

    /// <summary>Gets whether the video stream is interlaced.</summary>
    public bool? Interlaced { get; init; }

    /// <summary>Gets the codec the container declares.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Gets the audio codec the container declares.</summary>
    public string? AudioCodec { get; init; }

    /// <summary>Gets the audio channel count.</summary>
    public int? AudioChannels { get; init; }

    /// <summary>Gets the measured audio bitrate, in kilobits per second.</summary>
    public int? AudioBitrate { get; init; }

    /// <summary>Gets the audio sample depth, in bits.</summary>
    public int? SampleDepth { get; init; }

    /// <summary>Gets the audio sample rate, in kilohertz.</summary>
    public double? SampleRate { get; init; }

    /// <summary>Gets the transfer characteristics the container declares.</summary>
    public string? TransferCharacteristics { get; init; }

    /// <summary>Gets the duration the container declares.</summary>
    public TimeSpan? Duration { get; init; }
}
