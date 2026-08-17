using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Arronix.Abstractions.Quality.Families;

/// <summary>The community's words for a point in the video family's axis space.</summary>
/// <remarks>
/// <para>
/// Declared order is rule order and the first match wins. A label is produced from a point and is never
/// read back for a comparison: nothing that ranks, admits, decides or measures ever touches a rendered
/// string, which is the invariant that stops rungs growing back inside the renderer.
/// </para>
/// <para>
/// <b>Two rows are this design's stated position rather than an inherited one.</b> The disc-bitstream word
/// renders at every resolution, including the ones a rung ladder had nowhere to put — a release that says
/// it is a bitstream copy at 480 or 720 lines says something true, and whether the file behind it is
/// plausible is now a computation over its real size rather than a hard-coded rename. And a release whose
/// origin nothing states still renders the resolution it does state, instead of discarding a known fact to
/// say nothing at all.
/// </para>
/// <para>
/// <b>The axis model splits where physics splits; this table joins where the community joins.</b> The two
/// standard-definition disc members are separate axis values because their bitrates differ by roughly
/// threefold, and they render as one word because nobody writes two.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class VideoLabels
{
    /// <summary>The sentinel an absent origin reads as, deliberately outside the enumeration.</summary>
    /// <remarks>
    /// Load-bearing, and the one idiom in this file worth carrying to any other family: a fallback inside
    /// the enumeration would make every unread release match member zero, so every release nothing was read
    /// from would render as a camera capture.
    /// </remarks>
    private const VideoOrigin NoOrigin = (VideoOrigin)(-1);

    private const Packaging NoPackaging = (Packaging)(-1);

    private const AudioPresentation NoAudio = (AudioPresentation)(-1);

    /// <summary>The words that take a resolution suffix. Enumerated rather than described as a range.</summary>
    /// <remarks>
    /// Nothing else does — not the untouched transport stream, not standard-definition broadcast, not a
    /// disc's own program stream, not a whole-disc copy, and none of the pre-release words. Each of those
    /// names a signal whose raster is either fixed by its own standard or beside the point.
    /// </remarks>
    private static readonly string[] Suffixed = ["Remux", "Bluray", "WEBDL", "WEBRip", "HDTV"];

    /// <summary>Declares the family's rendering rules on a builder.</summary>
    /// <param name="builder">The builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static void Apply(IQualityTypeBuilder<VideoQuality> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Label(
                f => f.Packaging.Or(NoPackaging) == Packaging.DiscImage
                    || f.Packaging.Or(NoPackaging) == Packaging.DiscFolder,
                "BR-DISK")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.HighDefinitionDiscBitstream, "Remux")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.HighDefinitionDisc, "Bluray")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.Stream && f.Generation.Or(-1) == 0, "WEBDL")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.Stream && f.Generation.Or(-1) >= 1, "WEBRip")

            // A generation-absent stream reading is the common case rather than an error: a bare stream
            // token with nothing else must still render as a direct download, which is what the community
            // and every ladder do with it.
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.Stream, "WEBDL")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.BroadcastBitstream, "Raw-HD")
            .Label(
                f => f.Origin.Or(NoOrigin) == VideoOrigin.Broadcast && f.Resolution.Or(0) >= 720,
                "HDTV")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.Broadcast, "SDTV")
            .Label(
                f => f.Origin.Or(NoOrigin) == VideoOrigin.StandardDefinitionDiscBitstream
                    || f.Origin.Or(NoOrigin) == VideoOrigin.StandardDefinitionDisc,
                "DVD")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.FilmPrint, "TELECINE")
            .Label(
                f => f.Origin.Or(NoOrigin) == VideoOrigin.CameraCapture
                    && f.Audio.Or(NoAudio) >= AudioPresentation.LossyStereo,
                "TELESYNC")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.CameraCapture, "CAM")
            .Label(f => f.Origin.Or(NoOrigin) == VideoOrigin.Workprint, "WORKPRINT")

            // The truth, when the only thing anybody read was the raster. A rule list that keys every row
            // on the origin renders the word "Unknown" over a release that told us its resolution, and
            // then the suffix does not apply to that word either, so the known fact is discarded twice.
            .Label(f => !f.Origin.IsKnown && f.Resolution.IsKnown, string.Empty)
            .Label(f => true, "Unknown")

            // The first declared suffix names the standard-detail axis: every suffix over the resolution
            // is part of what a file name spells, and every suffix over any other axis is appended only at
            // full detail. The two resolution forms differ by whether a source word preceded them.
            .Suffix(f => f.Resolution, "-{0}p", word => Suffixed.Contains(word, StringComparer.Ordinal))
            .Suffix(f => f.Resolution, "{0}p", word => word.Length == 0)

            // The zero section of the format is the gate. A genuinely empty third section renders the
            // positive word for zero, so the sharp sign is required rather than decorative.
            .Suffix(f => f.Corrections, "{0:'Proper';;#}", _ => true)
            .Suffix(f => f.Mislabels, "{0:'REAL';;#}", _ => true);
    }
}
