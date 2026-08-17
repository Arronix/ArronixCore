using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Quality.Families;
using Arronix.Host.Engines.Quality;

// The whole quality-axes model is experimental, and so is the display prose it reads.
#pragma warning disable ARX0020
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// A video format family, declared the way a real one will be, so the host's quality engine is exercised
/// against a family somebody wrote rather than against a mock of one.
/// </summary>
/// <remarks>
/// <para>
/// This is a fixture and not the shipped family: the standard families belong to the contract assembly, and
/// nothing here is referenced by anything but these tests. It is written faithfully anyway — the same axes,
/// the same rendering table, the same shipped opinion — because a fixture that agrees with the engine by
/// construction proves nothing, and because these assertions are then re-pointable at the real family
/// without rewriting a single expectation.
/// </para>
/// <para>
/// Two axes the design names are missing, and both are findings rather than omissions. A distributor axis
/// wants an open vocabulary, which the derivation admits no shape for: wrapping a string in a record struct
/// satisfies the type constraint and still leaves the axis with no form and no members. A language axis
/// wants a set of a type that is a reference type today, and a set reading is constrained to enumeration
/// members. Both are recorded for whoever lands the shipped family.
/// </para>
/// </remarks>
internal static class QualityAxisFixtures
{
    /// <summary>Gets the family every fixture point belongs to.</summary>
    internal static FormatFamilyId Family => VideoQualityType.Family;

    /// <summary>Builds a registry holding the fixture family.</summary>
    /// <returns>The registry.</returns>
    internal static QualityFamilyRegistry Registry()
    {
        var registry = new QualityFamilyRegistry();

        registry.Add<VideoQualityFacts, VideoQualityType>();

        return registry;
    }

    /// <summary>Builds an evaluator over the fixture family.</summary>
    /// <returns>The evaluator.</returns>
    internal static AxisQualityEvaluator Evaluator() => new(Registry());

    /// <summary>Names one of the fixture family's axes.</summary>
    /// <param name="property">The property that declares it.</param>
    /// <returns>The identity.</returns>
    internal static QualityAxisId Axis(string property) => QualityAxisId.FromProperty(property);

    /// <summary>Spells one member of a closed axis.</summary>
    /// <typeparam name="TMember">The member type.</typeparam>
    /// <param name="member">The member.</param>
    /// <returns>The value.</returns>
    internal static AxisValue Member<TMember>(TMember member)
        where TMember : struct, Enum =>
        AxisValue.Member(Convert.ToInt32(member, CultureInfo.InvariantCulture), member.ToString());
}

/// <summary>The master signal a video file carries, ascending by what that master retains.</summary>
internal enum VideoOrigin
{
    /// <summary>A projection re-photographed with a camera.</summary>
    CameraCapture = 0,

    /// <summary>An unfinished edit.</summary>
    Workprint = 1,

    /// <summary>A physical film print, scanned.</summary>
    FilmPrint = 2,

    /// <summary>A transmission, re-encoded.</summary>
    Broadcast = 3,

    /// <summary>The transport stream itself, untouched.</summary>
    BroadcastBitstream = 4,

    /// <summary>A commercial streaming service's transmission.</summary>
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

/// <summary>The dynamic-range format, ascending by range carried.</summary>
internal enum DynamicRangeFormat
{
    /// <summary>No extended range.</summary>
    StandardDynamicRange = 0,

    /// <summary>The broadcast-compatible extended range.</summary>
    HybridLogGamma = 1,

    /// <summary>Extended range with static metadata.</summary>
    HighDynamicRange10 = 2,

    /// <summary>Extended range with dynamic metadata.</summary>
    HighDynamicRange10Plus = 3,

    /// <summary>Proprietary dynamic metadata with a compatible base layer.</summary>
    DolbyVisionWithFallback = 4,

    /// <summary>Proprietary dynamic metadata alone.</summary>
    DolbyVision = 5,
}

/// <summary>The audio presentation, ascending by what survives to the speakers.</summary>
internal enum AudioPresentation
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

/// <summary>The video codec, ascending by compression efficiency.</summary>
internal enum VideoCodec
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
internal enum Repackaging
{
    /// <summary>The first packaging of this encode.</summary>
    Original = 0,

    /// <summary>The same encode, packaged again.</summary>
    Repacked = 1,
}

/// <summary>How a release is packaged. None of these is better than another.</summary>
internal enum Packaging
{
    /// <summary>One playable file.</summary>
    SingleFile = 0,

    /// <summary>A disc image.</summary>
    DiscImage = 1,

    /// <summary>A disc folder structure.</summary>
    DiscFolder = 2,
}

/// <summary>Defects a video release may carry.</summary>
internal enum VideoFlaw
{
    /// <summary>Enlarged from a smaller raster.</summary>
    Upscaled = 0,

    /// <summary>Interlaced rather than progressive.</summary>
    Interlaced = 1,

    /// <summary>Carrying a burnt-in mark.</summary>
    Watermarked = 2,

    /// <summary>Carrying subtitles that cannot be turned off.</summary>
    HardcodedSubtitles = 3,

    /// <summary>Carrying advertisement breaks.</summary>
    AdBreaks = 4,

    /// <summary>Carrying a broadcaster's on-screen mark.</summary>
    NetworkLogo = 5,

    /// <summary>Cropped from its original framing.</summary>
    Cropped = 6,

    /// <summary>A fragment rather than the work.</summary>
    Sample = 7,
}

/// <summary>The quality of one video file.</summary>
internal sealed class VideoQualityFacts : IQualityFacts
{
    /// <summary>Gets the master signal the file carries.</summary>
    [Axis]
    [Display(Name = "Origin", Description = "What the release was made from, and whether it carries it whole.")]
    public Evidence<VideoOrigin> Origin { get; init; }

    /// <summary>Gets how many lossy re-encodes there have been since that signal.</summary>
    [Axis(Ordering = AxisOrdering.Descending, Unit = "re-encodes")]
    [Display(Name = "Generation", Description = "How many times it has been re-encoded since.")]
    public Evidence<int> Generation { get; init; }

    /// <summary>Gets the vertical resolution.</summary>
    [Axis(Unit = "lines")]
    [Display(Name = "Resolution", Description = "Lines of vertical resolution.")]
    public Evidence<int> Resolution { get; init; }

    /// <summary>Gets the dynamic-range formats the release carries.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Dynamic range")]
    public EvidenceSet<DynamicRangeFormat> DynamicRange { get; init; }

    /// <summary>Gets the audio presentation.</summary>
    [Axis]
    [Display(Name = "Audio")]
    public Evidence<AudioPresentation> Audio { get; init; }

    /// <summary>Gets the video codec, ordered by efficiency.</summary>
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
    [Axis(Unit = "corrections")]
    [Display(Name = "Corrections", Description = "The previous issue was a worse encode.")]
    public Evidence<int> Corrections { get; init; }

    /// <summary>Gets how many times it has been re-issued because the previous issue was the wrong content.</summary>
    [Axis(Unit = "corrections")]
    [Display(Name = "Mislabel fixes", Description = "The previous issue was the wrong content.")]
    public Evidence<int> Mislabels { get; init; }

    /// <summary>Gets whether the issue is a repack of the same encode.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Repack", Description = "The same encode, packaged again.")]
    public Evidence<Repackaging> Repacked { get; init; }
}

/// <summary>The video family's quality model.</summary>
internal sealed class VideoQualityType : IQualityType<VideoQualityFacts>
{
    private const VideoOrigin UnstatedOrigin = (VideoOrigin)(-1);
    private const Packaging UnstatedPackaging = (Packaging)(-1);
    private const AudioPresentation UnstatedAudio = (AudioPresentation)(-1);

    private static readonly string[] Suffixed = ["Remux", "Bluray", "WEBDL", "WEBRip", "HDTV"];

    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("video");

    /// <inheritdoc />
    /// <remarks>
    /// The rendering table is ordered and the first match wins. Two things in it are the design's stated
    /// position rather than an inherited one: the disc-bitstream word renders at every resolution, including
    /// the ones a rung ladder had nowhere to put, because the point is true and the size model is what tests
    /// whether the file behind it is; and a release whose origin nothing states still renders the resolution
    /// it does state, rather than discarding a known fact to say nothing.
    /// </remarks>
    public static void Configure(IQualityTypeBuilder<VideoQualityFacts> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Named("Video")
            .Label(
                f => f.Packaging.Or(UnstatedPackaging) == Packaging.DiscImage
                    || f.Packaging.Or(UnstatedPackaging) == Packaging.DiscFolder,
                "BR-DISK")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.HighDefinitionDiscBitstream, "Remux")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.HighDefinitionDisc, "Bluray")
            .Label(
                f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.Stream && f.Generation.Or(-1) == 0,
                "WEBDL")
            .Label(
                f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.Stream && f.Generation.Or(-1) >= 1,
                "WEBRip")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.Stream, "WEBDL")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.BroadcastBitstream, "Raw-HD")
            .Label(
                f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.Broadcast && f.Resolution.Or(0) >= 720,
                "HDTV")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.Broadcast, "SDTV")
            .Label(
                f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.StandardDefinitionDiscBitstream
                    || f.Origin.Or(UnstatedOrigin) == VideoOrigin.StandardDefinitionDisc,
                "DVD")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.FilmPrint, "TELECINE")
            .Label(
                f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.CameraCapture
                    && f.Audio.Or(UnstatedAudio) >= AudioPresentation.LossyStereo,
                "TELESYNC")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.CameraCapture, "CAM")
            .Label(f => f.Origin.Or(UnstatedOrigin) == VideoOrigin.Workprint, "WORKPRINT")
            .Label(f => !f.Origin.IsKnown && f.Resolution.IsKnown, string.Empty)
            .Label(f => true, "Unknown")
            .Suffix(f => f.Resolution, "-{0}p", word => Suffixed.Contains(word, StringComparer.Ordinal))
            .Suffix(f => f.Resolution, "{0}p", word => word.Length == 0)
            .Suffix(f => f.Corrections, "{0:'Proper';;#}", _ => true)
            .Suffix(f => f.Mislabels, "{0:'REAL';;#}", _ => true)
            .Sizes(ExpectedSize)
            .DefaultPolicy(Shipped);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordinary code, one function per axis. What disappears against a rung table is the <i>ranking</i>
    /// cascade — there is nothing for evidence to be collapsed to, so nothing here decides what is better
    /// than what. What does not disappear is the <i>inference</i> cascade, which is still a token-keyed
    /// decision about what a release is; it is smaller, its order is local to one function, and it is
    /// testable without naming a rung.
    /// </remarks>
    public static VideoQualityFacts Read(ReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var codec = CodecOf(evidence);
        var origin = OriginOf(evidence, codec);
        var generation = GenerationOf(evidence, origin);

        return new VideoQualityFacts
        {
            Origin = origin,
            Generation = generation,
            Resolution = ResolutionOf(evidence, origin),
            Codec = codec,
            Audio = AudioOf(evidence),
            DynamicRange = DynamicRangeOf(evidence),
            FrameRate = evidence.StatedFrameRate is { } rate
                ? Evidence<double>.From(rate, EvidenceSource.ReleaseTitle)
                : evidence.Probe?.FrameRate is { } measured
                    ? Evidence<double>.From(measured, EvidenceSource.ContainerProbe)
                    : Evidence<double>.None,
            Packaging = PackagingOf(evidence),
            Flaws = FlawsOf(evidence),
            Corrections = Evidence<int>.From(Math.Max(0, evidence.Version - 1), EvidenceSource.ReleaseTitle),
            Mislabels = Evidence<int>.From(evidence.RealCount, EvidenceSource.ReleaseTitle),
            Repacked = Evidence<Repackaging>.From(
                evidence.IsRepack ? Repackaging.Repacked : Repackaging.Original,
                EvidenceSource.ReleaseTitle),
        };
    }

    /// <summary>The opinion this family ships, which a user's own profile replaces entirely.</summary>
    /// <param name="policy">The builder.</param>
    /// <remarks>
    /// Resolution first because it is the one dimension a viewer notices from across the room and the one
    /// every release states, capped where consumer displays stop. Origin second because that is where the
    /// master-against-rip cliff lives, which is what makes a disc bitstream an upgrade over a disc encode
    /// while leaving a service's transmission and a re-encode of it equal. Generation third and tied at one,
    /// which is that equality stated as data rather than as an omission, and which leaves the axis live for
    /// the one step that is a real drop. Then the two correction counts, wrong content before worse encode.
    /// </remarks>
    private static void Shipped(IQualityPolicyBuilder policy)
    {
        policy
            .Prefer(QualityAxisId.FromProperty(nameof(VideoQualityFacts.Resolution)))
                .UpTo(AxisValue.Quantity(2160))
                .WhenUnknownRanksLowest()
            .Prefer(QualityAxisId.FromProperty(nameof(VideoQualityFacts.Origin)))
                .WhenUnknownRanksLowest()
            .Prefer(QualityAxisId.FromProperty(nameof(VideoQualityFacts.Generation)))
                .UpTo(AxisValue.Quantity(1))
                .WhenUnknownAssume(AxisValue.Quantity(1))
            .Prefer(QualityAxisId.FromProperty(nameof(VideoQualityFacts.Mislabels)))
                .WhenUnknownAssume(AxisValue.Quantity(0))
            .Prefer(QualityAxisId.FromProperty(nameof(VideoQualityFacts.Corrections)))
                .WhenUnknownAssume(AxisValue.Quantity(0))
            .Facet(QualityAxisId.FromProperty(nameof(VideoQualityFacts.DynamicRange)))
                .Worth(QualityAxisFixtures.Member(DynamicRangeFormat.HybridLogGamma), 5)
                .Worth(QualityAxisFixtures.Member(DynamicRangeFormat.HighDynamicRange10), 10)
                .Worth(QualityAxisFixtures.Member(DynamicRangeFormat.HighDynamicRange10Plus), 15)
                .Worth(QualityAxisFixtures.Member(DynamicRangeFormat.DolbyVisionWithFallback), 15)
                .Worth(QualityAxisFixtures.Member(DynamicRangeFormat.DolbyVision), 15)
            .Refuse(
                QualityAxisId.FromProperty(nameof(VideoQualityFacts.Origin)),
                QualityAxisFixtures.Member(VideoOrigin.CameraCapture),
                QualityAxisFixtures.Member(VideoOrigin.Workprint))
            .Refuse(
                QualityAxisId.FromProperty(nameof(VideoQualityFacts.Packaging)),
                QualityAxisFixtures.Member(Packaging.DiscImage),
                QualityAxisFixtures.Member(Packaging.DiscFolder))
            .GoodEnoughAt(
                QualityAxisId.FromProperty(nameof(VideoQualityFacts.Resolution)),
                AxisValue.Quantity(1080))
            .GoodEnoughAt(
                QualityAxisId.FromProperty(nameof(VideoQualityFacts.Generation)),
                AxisValue.Quantity(1));
    }

    private static SizeExpectation ExpectedSize(VideoQualityFacts facts, TimeSpan duration) =>
        VideoSizeModel.Expect(
            facts.Resolution.TryGet(out var lines) ? lines : null,
            facts.FrameRate.TryGet(out var rate) ? rate : null,
            facts.Codec.TryGet(out var codec) ? (int)codec : null,
            facts.Origin.TryGet(out var origin) ? (int)origin : null,
            facts.Generation.TryGet(out var generation) ? generation : null,
            facts.Audio.TryGet(out var audio) ? (int)audio : null,
            duration);

    private static Evidence<VideoOrigin> OriginOf(ReleaseEvidence evidence, Evidence<VideoCodec> codec)
    {
        var stated = evidence.SourceToken?.ToLowerInvariant() switch
        {
            "bluray" or "bdrip" or "brrip" or "uhdbdrip" => VideoOrigin.HighDefinitionDisc,
            "web" or "webdl" or "webrip" => VideoOrigin.Stream,
            "hdtv" or "sdtv" or "pdtv" => VideoOrigin.Broadcast,
            "dvd" => VideoOrigin.StandardDefinitionDiscBitstream,
            "dvdrip" => VideoOrigin.StandardDefinitionDisc,
            "cam" or "telesync" => VideoOrigin.CameraCapture,
            "telecine" => VideoOrigin.FilmPrint,
            "workprint" => VideoOrigin.Workprint,
            _ => (VideoOrigin?)null,
        };

        if (stated is { } source)
        {
            // A bitstream copy of a disc is what the community calls a remux, and a dual-language disc
            // encode with no rip token is one too: the extra track is why it exists.
            var bitstream = source == VideoOrigin.HighDefinitionDisc
                && (evidence.IsRemux || (IsDualLanguage(evidence) && !IsRip(evidence)));

            // A transmission carried in the codec broadcasters transmit in, with nothing saying it was
            // re-encoded, is the transport stream itself.
            var transport = source == VideoOrigin.Broadcast
                && codec.TryGet(out var transmitted)
                && transmitted == VideoCodec.Mpeg2;

            return Evidence<VideoOrigin>.From(
                bitstream ? VideoOrigin.HighDefinitionDiscBitstream
                : transport ? VideoOrigin.BroadcastBitstream
                : source,
                EvidenceSource.ReleaseTitle);
        }

        // A bitstream copy with no source token is still a disc bitstream copy by construction, which is
        // the only thing a bitstream copy can be.
        if (evidence.IsRemux)
        {
            return Evidence<VideoOrigin>.From(
                VideoOrigin.HighDefinitionDiscBitstream,
                EvidenceSource.Assumed);
        }

        // A container is evidence and not a fallback: a bare line count inside a streaming container is a
        // stream download and not a broadcast capture, and a program-stream container is a disc.
        return evidence.Container?.ToLowerInvariant() switch
        {
            ".mkv" or ".mp4" => Evidence<VideoOrigin>.From(VideoOrigin.Stream, EvidenceSource.Assumed),
            ".vob" or ".ifo" => Evidence<VideoOrigin>.From(
                VideoOrigin.StandardDefinitionDiscBitstream,
                EvidenceSource.Assumed),
            _ => Evidence<VideoOrigin>.None,
        };
    }

    private static Evidence<int> GenerationOf(ReleaseEvidence evidence, Evidence<VideoOrigin> origin)
    {
        if (origin.TryGet(out var carried)
            && carried is VideoOrigin.HighDefinitionDiscBitstream
                or VideoOrigin.StandardDefinitionDiscBitstream
                or VideoOrigin.BroadcastBitstream)
        {
            return Evidence<int>.From(0, origin.Source);
        }

        return evidence.SourceToken?.ToLowerInvariant() switch
        {
            "brrip" => Evidence<int>.From(2, EvidenceSource.ReleaseTitle),
            "bdrip" or "uhdbdrip" or "bluray" or "dvdrip" or "webrip" or "hdtv" or "sdtv" or "pdtv" =>
                Evidence<int>.From(1, EvidenceSource.ReleaseTitle),
            "web" or "webdl" => Evidence<int>.From(0, EvidenceSource.ReleaseTitle),
            _ => evidence.Container?.ToLowerInvariant() is ".mkv" or ".mp4"
                ? Evidence<int>.From(0, EvidenceSource.Assumed)
                : Evidence<int>.None,
        };
    }

    private static Evidence<int> ResolutionOf(ReleaseEvidence evidence, Evidence<VideoOrigin> origin)
    {
        // A camera recording of a projection states the camera's raster, not the film's, so a stated
        // resolution says nothing about the copy. The measurement, if there is one, still does.
        var claimed = origin.Or(UnstatedOrigin) == VideoOrigin.CameraCapture || evidence.StatedResolution is null
            ? Evidence<int>.None
            : EvidenceMerge.MostSpecific(
                EvidenceSource.ReleaseTitle,
                new EvidenceClaim<int>(evidence.StatedResolution.Value, (int)evidence.StatedResolutionForm));

        var measured = evidence.Probe?.Height is { } height
            ? Evidence<int>.From(height, EvidenceSource.ContainerProbe)
            : Evidence<int>.None;

        return EvidenceMerge.Stronger(claimed, measured);
    }

    private static Evidence<VideoCodec> CodecOf(ReleaseEvidence evidence)
    {
        var claimed = evidence.VideoCodecToken?.ToLowerInvariant() switch
        {
            "mpeg2" => Evidence<VideoCodec>.From(VideoCodec.Mpeg2, EvidenceSource.ReleaseTitle),
            "xvid" or "divx" => Evidence<VideoCodec>.From(VideoCodec.Mpeg4Part2, EvidenceSource.ReleaseTitle),
            "vc1" => Evidence<VideoCodec>.From(VideoCodec.Vc1, EvidenceSource.ReleaseTitle),
            "h264" or "x264" => Evidence<VideoCodec>.From(VideoCodec.H264, EvidenceSource.ReleaseTitle),
            "vp9" => Evidence<VideoCodec>.From(VideoCodec.Vp9, EvidenceSource.ReleaseTitle),
            "h265" or "x265" or "hevc" => Evidence<VideoCodec>.From(VideoCodec.H265, EvidenceSource.ReleaseTitle),
            "av1" => Evidence<VideoCodec>.From(VideoCodec.Av1, EvidenceSource.ReleaseTitle),
            "h266" or "vvc" => Evidence<VideoCodec>.From(VideoCodec.H266, EvidenceSource.ReleaseTitle),
            _ => Evidence<VideoCodec>.None,
        };

        var measured = evidence.Probe?.VideoCodec?.ToLowerInvariant() switch
        {
            "mpeg2video" => Evidence<VideoCodec>.From(VideoCodec.Mpeg2, EvidenceSource.ContainerProbe),
            "h264" => Evidence<VideoCodec>.From(VideoCodec.H264, EvidenceSource.ContainerProbe),
            "hevc" => Evidence<VideoCodec>.From(VideoCodec.H265, EvidenceSource.ContainerProbe),
            "av1" => Evidence<VideoCodec>.From(VideoCodec.Av1, EvidenceSource.ContainerProbe),
            _ => Evidence<VideoCodec>.None,
        };

        return EvidenceMerge.Stronger(claimed, measured);
    }

    private static Evidence<AudioPresentation> AudioOf(ReleaseEvidence evidence) =>
        evidence.AudioToken?.ToLowerInvariant() switch
        {
            "truehd-atmos" or "dts-x" => Evidence<AudioPresentation>.From(
                AudioPresentation.LosslessObject,
                EvidenceSource.ReleaseTitle),
            "truehd" or "dts-hd" or "flac" => Evidence<AudioPresentation>.From(
                AudioPresentation.Lossless,
                EvidenceSource.ReleaseTitle),
            "ddp-atmos" or "eac3-atmos" => Evidence<AudioPresentation>.From(
                AudioPresentation.LossyObject,
                EvidenceSource.ReleaseTitle),
            "ac3" or "ddp" or "eac3" or "dts" => Evidence<AudioPresentation>.From(
                AudioPresentation.LossySurround,
                EvidenceSource.ReleaseTitle),
            "aac" or "mp3" => Evidence<AudioPresentation>.From(
                AudioPresentation.LossyStereo,
                EvidenceSource.ReleaseTitle),
            _ => Evidence<AudioPresentation>.None,
        };

    private static EvidenceSet<DynamicRangeFormat> DynamicRangeOf(ReleaseEvidence evidence)
    {
        if (evidence.DynamicRangeTokens.Count == 0)
        {
            return EvidenceSet<DynamicRangeFormat>.None;
        }

        var members = evidence.DynamicRangeTokens
            .Select(static token => token.ToLowerInvariant())
            .Select(static part => part switch
            {
                "hlg" => DynamicRangeFormat.HybridLogGamma,
                "hdr" or "hdr10" => DynamicRangeFormat.HighDynamicRange10,
                "hdr10plus" => DynamicRangeFormat.HighDynamicRange10Plus,
                "dolby-vision-with-fallback" => DynamicRangeFormat.DolbyVisionWithFallback,
                "dolby-vision" => DynamicRangeFormat.DolbyVision,
                _ => DynamicRangeFormat.StandardDynamicRange,
            })
            .ToArray();

        return EvidenceSet<DynamicRangeFormat>.Of(EvidenceSource.ReleaseTitle, members);
    }

    private static Evidence<Packaging> PackagingOf(ReleaseEvidence evidence)
    {
        var claimed = evidence.PackagingToken?.ToLowerInvariant() switch
        {
            "bdiso" or "iso" => Evidence<Packaging>.From(Packaging.DiscImage, EvidenceSource.ReleaseTitle),
            "bdmv" or "video_ts" => Evidence<Packaging>.From(Packaging.DiscFolder, EvidenceSource.ReleaseTitle),
            _ => Evidence<Packaging>.None,
        };

        var inferred = evidence.Container?.ToLowerInvariant() switch
        {
            ".iso" or ".img" => Evidence<Packaging>.From(Packaging.DiscImage, EvidenceSource.Assumed),
            ".mkv" or ".mp4" => Evidence<Packaging>.From(Packaging.SingleFile, EvidenceSource.Assumed),
            _ => Evidence<Packaging>.None,
        };

        return EvidenceMerge.Stronger(inferred, claimed);
    }

    private static EvidenceSet<VideoFlaw> FlawsOf(ReleaseEvidence evidence)
    {
        if (evidence.FlawTokens.Count == 0 && evidence.ScanType is null)
        {
            return EvidenceSet<VideoFlaw>.None;
        }

        var found = new List<VideoFlaw>();

        foreach (var token in evidence.FlawTokens)
        {
            var flaw = token.ToLowerInvariant() switch
            {
                "upscaled" => VideoFlaw.Upscaled,
                "watermarked" or "screener" => VideoFlaw.Watermarked,
                "hardsub" or "korsub" => VideoFlaw.HardcodedSubtitles,
                "sample" => VideoFlaw.Sample,
                "adbreaks" => VideoFlaw.AdBreaks,
                "logo" => VideoFlaw.NetworkLogo,
                "cropped" => VideoFlaw.Cropped,
                _ => (VideoFlaw?)null,
            };

            if (flaw is { } named)
            {
                found.Add(named);
            }
        }

        if (evidence.ScanType is Abstractions.Quality.ScanType.Interlaced)
        {
            found.Add(VideoFlaw.Interlaced);
        }

        return EvidenceSet<VideoFlaw>.Of(EvidenceSource.ReleaseTitle, [.. found]);
    }

    private static bool IsDualLanguage(ReleaseEvidence evidence)
    {
        foreach (var claim in evidence.Languages)
        {
            if (claim.IsDualLanguageMarker)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRip(ReleaseEvidence evidence) =>
        evidence.SourceToken?.ToLowerInvariant() is "bdrip" or "brrip" or "uhdbdrip" or "dvdrip" or "webrip";
}

/// <summary>
/// One media kind's contribution to the video family: bracketed naming conventions its own community wrote,
/// which are not video facts and do not belong in a family every kind shares.
/// </summary>
internal sealed class BracketConventionRefinement : IQualityRefinement<VideoQualityFacts>
{
    /// <inheritdoc />
    public static FormatFamilyId Family => VideoQualityType.Family;

    /// <inheritdoc />
    /// <remarks>
    /// Everything here contributes at the weakest source there is, which is what it deserves: these are
    /// guesses about a naming habit. The host discards anything that would overwrite a stronger reading, and
    /// a requirement's minimum source keeps a guess from refusing a release outright.
    /// </remarks>
    public static VideoQualityFacts Refine(VideoQualityFacts read, ReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Guards.Contains("bracket-disc"))
        {
            return new VideoQualityFacts
            {
                Origin = EvidenceMerge.Stronger(
                    read.Origin,
                    Evidence<VideoOrigin>.From(VideoOrigin.HighDefinitionDisc, EvidenceSource.Assumed)),
                Generation = read.Generation,
                Resolution = read.Resolution,
                DynamicRange = read.DynamicRange,
                Audio = read.Audio,
                Codec = read.Codec,
                FrameRate = read.FrameRate,
                Packaging = read.Packaging,
                Flaws = read.Flaws,
                Corrections = read.Corrections,
                Mislabels = read.Mislabels,
                Repacked = read.Repacked,
            };
        }

        return read;
    }
}
