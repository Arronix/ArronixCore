using System;
using System.Collections.Generic;

namespace Arronix.Format.Video.Contributions;

/// <summary>Channel-relative meanings of common video release terms.</summary>
/// <remarks>
/// Each token contributes only what it actually establishes. In particular, WEBRip and HDTV do not claim
/// an exact encode count, and none of these rows speculates about the studio master upstream of the public
/// channel artifact.
/// </remarks>
public static class VideoReleaseVocabulary
{
    private static readonly IReadOnlyDictionary<string, VideoLineage> Meanings =
        new Dictionary<string, VideoLineage>(StringComparer.OrdinalIgnoreCase)
        {
            ["bluray-remux"] = new(
                ReleaseChannel.RetailPhysical,
                SourceCarrier.OpticalDisc,
                AcquisitionMethod.DirectCopy,
                TranscodeHistory.None),
            ["web-dl"] = new(
                ReleaseChannel.Streaming,
                SourceCarrier.HostedFile,
                AcquisitionMethod.DirectCopy,
                TranscodeHistory.None),
            ["bdrip"] = new(
                ReleaseChannel.RetailPhysical,
                SourceCarrier.OpticalDisc,
                AcquisitionMethod.UnknownDerivedCopy,
                TranscodeHistory.AtLeastOne),
            ["webrip"] = new(
                ReleaseChannel.Streaming,
                SourceCarrier.HostedFile,
                AcquisitionMethod.UnknownDerivedCopy,
                TranscodeHistory.AtLeastOne),
            ["cam"] = new(
                ReleaseChannel.Exhibition,
                SourceCarrier.ProjectedImage,
                AcquisitionMethod.CameraCapture,
                TranscodeHistory.Unknown),
            ["ts"] = new(
                ReleaseChannel.Exhibition,
                SourceCarrier.ProjectedImage,
                AcquisitionMethod.CameraCapture,
                TranscodeHistory.Unknown),
            ["hdtv"] = new(
                ReleaseChannel.Broadcast,
                SourceCarrier.BroadcastTransport,
                AcquisitionMethod.UnknownDerivedCopy,
                TranscodeHistory.Unknown),
        };

    public static bool TryReadLineage(string token, out VideoLineage lineage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Meanings.TryGetValue(token, out lineage!);
    }
}
