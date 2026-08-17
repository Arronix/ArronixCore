using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Media;

namespace Arronix.Abstractions.Quality.Families;

/// <summary>The quality of one video file.</summary>
/// <remarks>
/// <para>
/// One family, shared by every media kind whose files are video. A stream download at 1080 lines is a
/// stream download at 1080 lines whether the work is a film or an episode, and giving each kind its own
/// copy of this taxonomy guarantees the two drift — which is exactly what the two surveyed applications
/// this replaces did to their near-identical ladders.
/// </para>
/// <para>
/// <b>Every property here is an axis and there is no second kind of quality fact.</b> Revision counts are
/// axes, defects are an axis, packaging is an axis. That invariant is what keeps the policy vocabulary to
/// four verbs and the profile editor to four lists.
/// </para>
/// <para>
/// <b>Two axes the design names are absent, and both are findings rather than omissions.</b> A distributor
/// axis wants an open vocabulary: wrapping a token in a record struct satisfies the value-type constraint
/// and still leaves the axis with no form and no members, because the derivation admits an enumeration, a
/// count, a quantity or a set of enumeration members and nothing else. A language axis wants a set over a
/// type that is a reference type today, and a set reading is constrained to enumeration members. The
/// German dual-language disc rule that a language axis would have carried is not lost — it is read from
/// <see cref="ReleaseEvidence.Languages"/> inside <see cref="VideoQualityType.Read"/>, which is where the
/// design puts it anyway; what is lost is a user's ability to <i>prefer</i> or <i>refuse</i> a language,
/// and that is recorded rather than worked around.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class VideoQuality : IQualityFacts
{
    /// <summary>Gets the master signal the file carries.</summary>
    [Axis]
    [Display(Name = "Origin", Description = "What the release was made from, and whether it carries it whole.")]
    public Evidence<VideoOrigin> Origin { get; init; }

    /// <summary>Gets how many lossy re-encodes there have been since the signal the origin names.</summary>
    [Axis(Ordering = AxisOrdering.Descending, Unit = "re-encodes")]
    [Display(Name = "Generation", Description = "How many times it has been re-encoded since.")]
    public Evidence<int> Generation { get; init; }

    /// <summary>Gets the vertical resolution.</summary>
    [Axis(Unit = "lines")]
    [Display(Name = "Resolution", Description = "Lines of vertical resolution.")]
    public Evidence<int> Resolution { get; init; }

    /// <summary>Gets the dynamic-range formats the release carries.</summary>
    /// <remarks>
    /// Set-valued, because a release genuinely carries two at once: a proprietary dynamic-metadata layer
    /// over an open dynamic-metadata base is real and increasingly common, and a single-valued reading had
    /// no member for it and no way to grow one that was not a combinatorial enumeration. Set-valued makes
    /// the axis unordered, so it cannot appear in a precedence list at all and lands in the facet tier,
    /// which is where the community's own preference for it actually lives.
    /// </remarks>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Dynamic range")]
    public EvidenceSet<DynamicRange> DynamicRange { get; init; }

    /// <summary>Gets the audio presentation.</summary>
    [Axis]
    [Display(Name = "Audio")]
    public Evidence<AudioPresentation> Audio { get; init; }

    /// <summary>Gets the video codec, ordered by compression efficiency.</summary>
    [Axis]
    [Display(Name = "Codec", Description = "Ordered by efficiency, which is not the same as quality.")]
    public Evidence<VideoCodec> Codec { get; init; }

    /// <summary>Gets the frame rate.</summary>
    [Axis(Unit = "fps")]
    [Display(Name = "Frame rate")]
    public Evidence<double> FrameRate { get; init; }

    /// <summary>Gets how the release is packaged.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Packaging", Description = "A single file, a disc image, or a disc folder.")]
    public Evidence<Packaging> Packaging { get; init; }

    /// <summary>Gets the defects the release carries.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Flaws")]
    public EvidenceSet<VideoFlaw> Flaws { get; init; }

    /// <summary>Gets how many corrections the release has been re-issued for. A first issue is zero.</summary>
    /// <remarks>
    /// Base zero, and <see cref="VideoQualityType.Read"/> subtracts. A scan reports one for a release that
    /// states nothing, two for a corrected issue and three for a second correction; one reading is chosen
    /// here so that an unread release does not rank below a release explicitly stating no correction, and
    /// so that the renderer's "corrected" word fires above zero rather than above one.
    /// </remarks>
    [Axis(Unit = "corrections")]
    [Display(Name = "Corrections", Description = "The previous issue was a worse encode of the right content.")]
    public Evidence<int> Corrections { get; init; }

    /// <summary>Gets how many times it was re-issued because the previous issue was the wrong content.</summary>
    [Axis(Unit = "corrections")]
    [Display(Name = "Mislabel fixes", Description = "The previous issue was the wrong content.")]
    public Evidence<int> Mislabels { get; init; }

    /// <summary>Gets whether the issue is a repack of the same encode.</summary>
    /// <remarks>
    /// An enumeration rather than a two-state flag, because the derivation admits no boolean axis: a
    /// boolean is not an enumeration and the table makes no exception for it.
    /// </remarks>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Repack", Description = "The same encode, packaged again.")]
    public Evidence<Repackaging> Repacked { get; init; }
}

/// <summary>The master signal a video file carries, in ascending order of what that master retains.</summary>
/// <remarks>
/// <para>
/// A member names <i>which</i> master and <i>whether the file carries that master's own bitstream</i>. The
/// second half is the decision this axis exists to record: a member exists for the untouched master
/// exactly where holding the master is a different bitrate class from holding an encode of it. The size
/// model measures that gap at roughly five-, three- and twofold for the two disc masters and for
/// broadcast, and at about a tenth of that for a streaming service — so streaming is the one master with a
/// single member, and the direct-download against re-encode step lives on the generation axis instead.
/// That is precisely the equivalence the community asserts and a rung ladder can only encode by giving two
/// rungs one weight.
/// </para>
/// <para>
/// The consequence for generation is stated rather than hidden: a bitstream member forces generation zero
/// and a rip member forces at least one. The two axes overlap on purpose, and the alternative — a
/// per-step significance weight readable across axes — is the scalar this whole model exists to delete.
/// </para>
/// <para>
/// Placing both broadcast members below the streaming member is the one judgment in this order rather
/// than a physical fact: a good terrestrial transmission carries more bits than a streaming encode, and it
/// is also interlaced, logo-burned and ad-cut, with those three recoverable as defects on the broadcast
/// side. It is a judgment, which is exactly why a policy may re-rank the axis.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum VideoOrigin
{
    /// <summary>A projection re-photographed with a camera.</summary>
    CameraCapture = 0,

    /// <summary>An unfinished edit.</summary>
    Workprint = 1,

    /// <summary>A physical film print, scanned.</summary>
    FilmPrint = 2,

    /// <summary>An over-the-air, cable or satellite transmission, re-encoded.</summary>
    Broadcast = 3,

    /// <summary>The broadcast transport stream itself, untouched.</summary>
    BroadcastBitstream = 4,

    /// <summary>A commercial streaming service's transmission, direct download and re-encode alike.</summary>
    Stream = 5,

    /// <summary>A standard-definition disc, re-encoded.</summary>
    StandardDefinitionDisc = 6,

    /// <summary>The standard-definition disc's own program stream, untouched.</summary>
    StandardDefinitionDiscBitstream = 7,

    /// <summary>A high-definition disc, re-encoded.</summary>
    HighDefinitionDisc = 8,

    /// <summary>The high-definition disc's own video bitstream, copied.</summary>
    HighDefinitionDiscBitstream = 9,
}

/// <summary>The dynamic-range format, in ascending order of range carried.</summary>
/// <remarks>
/// Read as a set, so a proprietary layer over an open base is two members rather than a missing
/// enumeration value. The declared order is retained because the facet tier scores members and a person
/// reading the list expects them ordered; it is not a precedence order, and an unordered axis cannot
/// become one.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum DynamicRange
{
    /// <summary>No extended range.</summary>
    StandardDynamicRange = 0,

    /// <summary>The broadcast-compatible extended range.</summary>
    HybridLogGamma = 1,

    /// <summary>Extended range with static metadata.</summary>
    HighDynamicRange10 = 2,

    /// <summary>Extended range with dynamic metadata.</summary>
    HighDynamicRange10Plus = 3,

    /// <summary>Proprietary dynamic metadata over a base layer an ordinary display can render.</summary>
    DolbyVisionWithFallback = 4,

    /// <summary>Proprietary dynamic metadata alone.</summary>
    DolbyVision = 5,
}

/// <summary>The audio presentation, in ascending order of what survives to the speakers.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum AudioPresentation
{
    /// <summary>Sound recorded in the room.</summary>
    RoomCapture = 0,

    /// <summary>A lossy two-channel mix.</summary>
    LossyStereo = 1,

    /// <summary>A lossy surround mix.</summary>
    LossySurround = 2,

    /// <summary>A lossy mix carrying positioned objects.</summary>
    LossyObject = 3,

    /// <summary>A lossless surround mix.</summary>
    Lossless = 4,

    /// <summary>A lossless mix carrying positioned objects.</summary>
    LosslessObject = 5,
}

/// <summary>The video codec, in ascending order of compression efficiency.</summary>
/// <remarks>
/// Efficiency, not fidelity: at an equal quality target a more efficient codec produces a smaller file of
/// the same quality, not a better one. The shipped policy therefore does not order on this axis; it exists
/// because it feeds the size model and because compatibility is a real user preference.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum VideoCodec
{
    /// <summary>The broadcast and standard-definition disc codec.</summary>
    Mpeg2 = 0,

    /// <summary>The standard-definition era's re-encode codec.</summary>
    Mpeg4Part2 = 1,

    /// <summary>The early high-definition disc codec.</summary>
    Vc1 = 2,

    /// <summary>The reference codec.</summary>
    H264 = 3,

    /// <summary>The open codec of the same generation as its successor.</summary>
    Vp9 = 4,

    /// <summary>The successor at about half the bitrate for equal quality.</summary>
    H265 = 5,

    /// <summary>The open codec at about seventy percent of that again.</summary>
    Av1 = 6,

    /// <summary>The newest standardized codec.</summary>
    H266 = 7,
}

/// <summary>Whether an issue is a repack of the same encode.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum Repackaging
{
    /// <summary>The first packaging of this encode.</summary>
    Original = 0,

    /// <summary>The same encode, packaged again.</summary>
    Repacked = 1,
}

/// <summary>How a release is packaged. Unordered: none of these is better than another.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum Packaging
{
    /// <summary>One playable file.</summary>
    SingleFile = 0,

    /// <summary>A whole disc as an image.</summary>
    DiscImage = 1,

    /// <summary>A whole disc as a directory tree.</summary>
    DiscFolder = 2,
}

/// <summary>Defects a video release may carry.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum VideoFlaw
{
    /// <summary>Enlarged from a smaller raster.</summary>
    Upscaled = 0,

    /// <summary>Interlaced rather than progressive.</summary>
    Interlaced = 1,

    /// <summary>Carrying a distribution mark burned into the picture.</summary>
    Watermarked = 2,

    /// <summary>Carrying subtitles that cannot be turned off.</summary>
    HardcodedSubtitles = 3,

    /// <summary>Carrying advertisement breaks.</summary>
    AdBreaks = 4,

    /// <summary>Carrying a transmitting network's on-screen mark.</summary>
    NetworkLogo = 5,

    /// <summary>Cropped from its original framing.</summary>
    Cropped = 6,

    /// <summary>A fragment rather than the work.</summary>
    Sample = 7,
}
