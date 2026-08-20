
using System;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;

namespace Arronix.Format.Video;

/// <summary>Video-owned default preferences contributed to one compiled media release policy.</summary>
public static class VideoReleasePolicyDefaults
{
    /// <summary>Adds video preferences without creating a second independently executing policy.</summary>
    public static void Configure<TRelease>(
        ReleasePolicyBuilder<TRelease> policy,
        Func<TRelease, Video?> representation)
        where TRelease : class, IRelease
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(representation);

        policy
            .Prefer(
                release => ResolutionOf(representation(release)),
                isKnown: static value => value.HasValue)
            .Prefer(
                release => AdditionalTranscodesOf(representation(release)),
                preferGreater: false,
                isKnown: static value => value.HasValue)
            .Prefer(
                release => RevisionOf(representation(release)),
                isKnown: static value => value.HasValue);
    }

    private static VideoResolution? ResolutionOf(Video? representation) =>
        representation?.Resolution;

    private static int? AdditionalTranscodesOf(Video? representation) =>
        representation?.Lineage.Transcoding is { } history
            && (history.ExactAdditionalEncodes.HasValue || history.MinimumAdditionalEncodes > 0)
                ? history.ExactAdditionalEncodes ?? history.MinimumAdditionalEncodes
                : null;

    private static ReleaseRevision? RevisionOf(Video? representation) =>
        representation?.Revision;
}
