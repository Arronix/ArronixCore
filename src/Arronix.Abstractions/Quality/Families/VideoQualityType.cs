using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality.Families;

/// <summary>The video format family's quality model: how evidence becomes typed axis readings.</summary>
/// <remarks>
/// <para>
/// This type replaces a hundred-and-eight declaration rows whose entire job was to collapse evidence into
/// one of thirty names. The <i>ranking</i> cascade genuinely disappears — there is nothing for evidence to
/// be collapsed <i>to</i>, so nothing here decides what is better than what, and this type is structurally
/// incapable of comparing two files. The <i>inference</i> cascade does not disappear: deciding that a
/// re-encode of an existing rip is two generations deep and a rip of the disc is one is still a
/// token-keyed decision. What changed is that it is ordinary code, its order is local to one function per
/// axis, and it is testable without anybody naming a rung.
/// </para>
/// <para>
/// It lives in the contract assembly beside the framework rather than inside a media extension because
/// quality is a property of a <i>file</i> and a file belongs to a <i>format family</i>. Two kinds whose
/// files are video share this one model and therefore cannot drift; a kind whose releases carry a naming
/// dialect of its own contributes an <see cref="IQualityRefinement{TFacts}"/> rather than a second copy of
/// the taxonomy.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class VideoQualityType : IQualityType<VideoQuality>
{
    /// <summary>The stream containers whose presence is itself evidence of a stream download.</summary>
    /// <remarks>
    /// Evidence and not a fallback. A bare line count inside one of these is a stream download and not a
    /// broadcast capture, which is a positive reading about a real release convention; guessing a
    /// <i>resolution</i> from a container, which is the thing a container fallback table did, is a
    /// different and much worse move and is not made anywhere here.
    /// </remarks>
    private static readonly string[] StreamContainers = [".mkv", ".mk3d", ".mp4", ".m4v", ".webm"];

    private static readonly string[] DiscImageContainers = [".iso", ".img"];

    private static readonly string[] ProgramStreamContainers = [".vob", ".ifo"];

    private static readonly string[] DiscTransportContainers = [".m2ts", ".mts"];

    /// <inheritdoc />
    public static FormatFamilyId Family => FormatFamilyId.From("video");

    /// <inheritdoc />
    public static void Configure(IQualityTypeBuilder<VideoQuality> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Named("Video");
        VideoLabels.Apply(builder);
        builder.Sizes(ExpectedSize).DefaultPolicy(VideoDefaults.Shipped);
    }

    /// <inheritdoc />
    public static VideoQuality Read(ReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var codec = CodecOf(evidence);
        var origin = OriginOf(evidence, codec);

        return new VideoQuality
        {
            Origin = origin,
            Generation = GenerationOf(evidence, origin, codec),
            Resolution = ResolutionOf(evidence, origin),
            DynamicRange = DynamicRangeOf(evidence),
            Audio = AudioOf(evidence),
            Codec = codec,
            FrameRate = FrameRateOf(evidence),
            Packaging = PackagingOf(evidence),
            Flaws = FlawsOf(evidence),

            // A scan reports one for a release stating no correction, so the count a person means is one
            // less than the number a scan reports.
            Corrections = Evidence<int>.From(Math.Max(0, evidence.Version - 1), EvidenceSource.ReleaseTitle),
            Mislabels = Evidence<int>.From(evidence.RealCount, EvidenceSource.ReleaseTitle),
            Repacked = Evidence<Repackaging>.From(
                evidence.IsRepack ? Repackaging.Repacked : Repackaging.Original,
                EvidenceSource.ReleaseTitle),
        };
    }

    /// <summary>Reads which master signal the file carries.</summary>
    /// <remarks>
    /// <para>
    /// Four inferences live here that a rung table spent thirty rows on, and each is one line because the
    /// facts it reads are typed.
    /// </para>
    /// <para>
    /// A bitstream copy of a high-definition disc is what the community calls a remux. A <i>dual-language</i>
    /// disc encode with no rip token is one too — the second audio track is the entire reason the release
    /// exists, and it never spells the word — which is why the language claims are read here rather than
    /// through a media kind's own guard string. A transmission carried in the codec broadcasters transmit
    /// in, with nothing saying it was re-encoded, is the transport stream itself. And a bitstream copy with
    /// no source token at all is still a disc bitstream copy by construction, because that is the only
    /// thing a bitstream copy can be.
    /// </para>
    /// </remarks>
    private static Evidence<VideoOrigin> OriginOf(ReleaseEvidence evidence, Evidence<VideoCodec> codec)
    {
        var stated = Normalize(evidence.SourceToken) switch
        {
            EvidenceSourceTokens.BluRayDisc
                or EvidenceSourceTokens.BluRayRip
                or EvidenceSourceTokens.BluRayRipOfRip
                or EvidenceSourceTokens.UltraHighDefinitionDiscRip
                or EvidenceSourceTokens.UltraHighDefinitionDiscDownConvert
                or EvidenceSourceTokens.HighDefinitionDvdDisc
                or EvidenceSourceTokens.HighDefinitionDvdRip
                or EvidenceSourceTokens.HighDefinitionDiscScreener => VideoOrigin.HighDefinitionDisc,

            EvidenceSourceTokens.WebDownload
                or EvidenceSourceTokens.WebRip
                or EvidenceSourceTokens.Web => VideoOrigin.Stream,

            EvidenceSourceTokens.HighDefinitionBroadcast
                or EvidenceSourceTokens.PublicDigitalBroadcast
                or EvidenceSourceTokens.StandardDefinitionBroadcast
                or EvidenceSourceTokens.BroadcastRip
                or EvidenceSourceTokens.SatelliteRecording => VideoOrigin.Broadcast,

            EvidenceSourceTokens.DvdDisc
                or EvidenceSourceTokens.DvdRecordable => VideoOrigin.StandardDefinitionDiscBitstream,

            EvidenceSourceTokens.DvdRip
                or EvidenceSourceTokens.StandardDefinitionDiscScreener
                or EvidenceSourceTokens.RegionalPrint => VideoOrigin.StandardDefinitionDisc,

            EvidenceSourceTokens.CameraCapture
                or EvidenceSourceTokens.TeleSync => VideoOrigin.CameraCapture,

            EvidenceSourceTokens.TeleCine => VideoOrigin.FilmPrint,
            EvidenceSourceTokens.WorkPrint => VideoOrigin.Workprint,

            // A distribution copy sent for review names no master of its own. What it contributes is a
            // burn-in, which the defect axis carries, and the origin stays whatever else the title said.
            _ => (VideoOrigin?)null,
        };

        if (stated is { } source)
        {
            var bitstream = source == VideoOrigin.HighDefinitionDisc
                && (evidence.IsRemux || (StatesADubbedDiscEncode(evidence) && !StatesRip(evidence)));

            var transport = source == VideoOrigin.Broadcast
                && codec.TryGet(out var transmitted)
                && transmitted == VideoCodec.Mpeg2;

            return Evidence<VideoOrigin>.From(
                bitstream ? VideoOrigin.HighDefinitionDiscBitstream
                    : transport ? VideoOrigin.BroadcastBitstream
                    : source,
                EvidenceSource.ReleaseTitle);
        }

        if (evidence.IsRemux)
        {
            return Evidence<VideoOrigin>.From(VideoOrigin.HighDefinitionDiscBitstream, EvidenceSource.Assumed);
        }

        return Container(evidence) switch
        {
            var c when Holds(StreamContainers, c) =>
                Evidence<VideoOrigin>.From(VideoOrigin.Stream, EvidenceSource.Assumed),
            var c when Holds(ProgramStreamContainers, c) =>
                Evidence<VideoOrigin>.From(VideoOrigin.StandardDefinitionDiscBitstream, EvidenceSource.Assumed),
            var c when Holds(DiscTransportContainers, c) =>
                Evidence<VideoOrigin>.From(VideoOrigin.HighDefinitionDisc, EvidenceSource.Assumed),
            _ => Evidence<VideoOrigin>.None,
        };
    }

    /// <summary>Reads how many lossy re-encodes there have been since the origin's signal.</summary>
    /// <remarks>
    /// A bitstream member fixes the count at zero by construction. Everything else is a token the scan
    /// settled on, and the one distinction worth stating is between a rip of a disc and a re-encode of an
    /// existing rip: a ladder treats those two tokens identically, and the difference between them is a
    /// real drop that this axis is left live to catch.
    /// </remarks>
    private static Evidence<int> GenerationOf(
        ReleaseEvidence evidence,
        Evidence<VideoOrigin> origin,
        Evidence<VideoCodec> codec)
    {
        if (origin.TryGet(out var carried)
            && carried is VideoOrigin.HighDefinitionDiscBitstream
                or VideoOrigin.StandardDefinitionDiscBitstream
                or VideoOrigin.BroadcastBitstream)
        {
            return Evidence<int>.From(0, origin.Source);
        }

        var stated = Normalize(evidence.SourceToken) switch
        {
            EvidenceSourceTokens.BluRayRipOfRip => 2,

            EvidenceSourceTokens.WebDownload or EvidenceSourceTokens.Web => 0,

            EvidenceSourceTokens.BluRayDisc
                or EvidenceSourceTokens.BluRayRip
                or EvidenceSourceTokens.UltraHighDefinitionDiscRip
                or EvidenceSourceTokens.UltraHighDefinitionDiscDownConvert
                or EvidenceSourceTokens.HighDefinitionDvdDisc
                or EvidenceSourceTokens.HighDefinitionDvdRip
                or EvidenceSourceTokens.HighDefinitionDiscScreener
                or EvidenceSourceTokens.StandardDefinitionDiscScreener
                or EvidenceSourceTokens.DvdRip
                or EvidenceSourceTokens.RegionalPrint
                or EvidenceSourceTokens.WebRip
                or EvidenceSourceTokens.HighDefinitionBroadcast
                or EvidenceSourceTokens.PublicDigitalBroadcast
                or EvidenceSourceTokens.StandardDefinitionBroadcast
                or EvidenceSourceTokens.BroadcastRip
                or EvidenceSourceTokens.SatelliteRecording
                or EvidenceSourceTokens.CameraCapture
                or EvidenceSourceTokens.TeleSync
                or EvidenceSourceTokens.TeleCine => 1,

            _ => (int?)null,
        };

        if (stated is { } count)
        {
            return Evidence<int>.From(count, EvidenceSource.ReleaseTitle);
        }

        // The standard-definition era's re-encode codec is never what a master is carried in, so a release
        // stating it has been re-encoded at least once whatever else it left out.
        if (codec.TryGet(out var encoder) && encoder == VideoCodec.Mpeg4Part2)
        {
            return Evidence<int>.From(1, EvidenceSource.Assumed);
        }

        return Holds(StreamContainers, Container(evidence))
            ? Evidence<int>.From(0, EvidenceSource.Assumed)
            : Evidence<int>.None;
    }

    /// <summary>Reads the vertical resolution.</summary>
    /// <remarks>
    /// A camera recording of a projection states the camera's raster, not the work's, so a stated
    /// resolution says nothing about the copy and is dropped — which is the whole of what a per-kind list
    /// of "sources whose stated resolution must be ignored" was doing. A measurement, if there is one,
    /// still counts, because a measurement of the file is never a claim about the shoot.
    /// </remarks>
    private static Evidence<int> ResolutionOf(ReleaseEvidence evidence, Evidence<VideoOrigin> origin)
    {
        var shotThroughACamera = origin.TryGet(out var carried) && carried == VideoOrigin.CameraCapture;

        var claimed = shotThroughACamera || evidence.StatedResolution is not { } lines
            ? Evidence<int>.None
            : EvidenceMerge.MostSpecific(
                EvidenceSource.ReleaseTitle,
                new EvidenceClaim<int>(lines, (int)evidence.StatedResolutionForm));

        var measured = evidence.Probe?.Height is { } height
            ? Evidence<int>.From(height, EvidenceSource.ContainerProbe)
            : Evidence<int>.None;

        return EvidenceMerge.Stronger(claimed, measured);
    }

    private static Evidence<VideoCodec> CodecOf(ReleaseEvidence evidence)
    {
        var claimed = Normalize(evidence.VideoCodecToken) switch
        {
            EvidenceVideoCodecTokens.Mpeg2 => Named(VideoCodec.Mpeg2),
            EvidenceVideoCodecTokens.Mpeg4Part2 => Named(VideoCodec.Mpeg4Part2),
            EvidenceVideoCodecTokens.Vc1 => Named(VideoCodec.Vc1),
            EvidenceVideoCodecTokens.H264 => Named(VideoCodec.H264),
            EvidenceVideoCodecTokens.Vp9 => Named(VideoCodec.Vp9),
            EvidenceVideoCodecTokens.H265 => Named(VideoCodec.H265),
            EvidenceVideoCodecTokens.Av1 => Named(VideoCodec.Av1),
            EvidenceVideoCodecTokens.H266 => Named(VideoCodec.H266),
            _ => Evidence<VideoCodec>.None,
        };

        // A container names its codec in its own spelling rather than the scene's, and it is a measurement
        // rather than a claim, so it wins whatever the title said.
        var measured = Normalize(evidence.Probe?.VideoCodec) switch
        {
            "mpeg2video" or "mpeg2" => Measured(VideoCodec.Mpeg2),
            "mpeg4" or "msmpeg4v3" => Measured(VideoCodec.Mpeg4Part2),
            "vc1" => Measured(VideoCodec.Vc1),
            "h264" or "avc" => Measured(VideoCodec.H264),
            "vp9" => Measured(VideoCodec.Vp9),
            "hevc" or "h265" => Measured(VideoCodec.H265),
            "av1" => Measured(VideoCodec.Av1),
            "vvc" or "h266" => Measured(VideoCodec.H266),
            _ => Evidence<VideoCodec>.None,
        };

        return EvidenceMerge.Stronger(claimed, measured);

        static Evidence<VideoCodec> Named(VideoCodec codec) =>
            Evidence<VideoCodec>.From(codec, EvidenceSource.ReleaseTitle);

        static Evidence<VideoCodec> Measured(VideoCodec codec) =>
            Evidence<VideoCodec>.From(codec, EvidenceSource.ContainerProbe);
    }

    /// <summary>Reads what survives to the speakers.</summary>
    /// <remarks>
    /// The one call worth stating: a high-resolution variant of the surround codec is <i>lossy</i>, whatever
    /// its name suggests, and only the master-audio variant is not. Reading the name rather than the format
    /// is how a lossy stream ends up ranked with a disc's own soundtrack.
    /// </remarks>
    private static Evidence<AudioPresentation> AudioOf(ReleaseEvidence evidence) =>
        Normalize(evidence.AudioToken) switch
        {
            EvidenceAudioTokens.TrueHdWithObjects
                or EvidenceAudioTokens.DtsWithObjects => Heard(AudioPresentation.LosslessObject),

            EvidenceAudioTokens.Pcm
                or EvidenceAudioTokens.TrueHd
                or EvidenceAudioTokens.DtsHdMasterAudio
                or EvidenceAudioTokens.Flac => Heard(AudioPresentation.Lossless),

            EvidenceAudioTokens.EnhancedAc3WithObjects
                or EvidenceAudioTokens.ObjectsOnly => Heard(AudioPresentation.LossyObject),

            EvidenceAudioTokens.DtsHdHighResolution
                or EvidenceAudioTokens.Dts
                or EvidenceAudioTokens.EnhancedAc3
                or EvidenceAudioTokens.Ac3 => Heard(AudioPresentation.LossySurround),

            EvidenceAudioTokens.Aac
                or EvidenceAudioTokens.Mp3
                or EvidenceAudioTokens.Mp2
                or EvidenceAudioTokens.Opus
                or EvidenceAudioTokens.Vorbis
                or EvidenceAudioTokens.WindowsMediaAudio => Heard(AudioPresentation.LossyStereo),

            _ => Evidence<AudioPresentation>.None,
        };

    private static EvidenceSet<DynamicRange> DynamicRangeOf(ReleaseEvidence evidence)
    {
        if (evidence.DynamicRangeTokens.Count == 0)
        {
            return EvidenceSet<DynamicRange>.None;
        }

        var members = new List<DynamicRange>(evidence.DynamicRangeTokens.Count);

        foreach (var token in evidence.DynamicRangeTokens)
        {
            var member = Normalize(token) switch
            {
                EvidenceDynamicRangeTokens.StandardDynamicRange => DynamicRange.StandardDynamicRange,
                EvidenceDynamicRangeTokens.HybridLogGamma => DynamicRange.HybridLogGamma,
                EvidenceDynamicRangeTokens.HighDynamicRange10 => DynamicRange.HighDynamicRange10,
                EvidenceDynamicRangeTokens.HighDynamicRange10Plus => DynamicRange.HighDynamicRange10Plus,
                EvidenceDynamicRangeTokens.DolbyVisionWithFallback => DynamicRange.DolbyVisionWithFallback,
                EvidenceDynamicRangeTokens.DolbyVision => DynamicRange.DolbyVision,
                _ => (DynamicRange?)null,
            };

            if (member is { } named && !members.Contains(named))
            {
                members.Add(named);
            }
        }

        return members.Count == 0
            ? EvidenceSet<DynamicRange>.None
            : EvidenceSet<DynamicRange>.Of(EvidenceSource.ReleaseTitle, [.. members]);
    }

    private static Evidence<double> FrameRateOf(ReleaseEvidence evidence) =>
        EvidenceMerge.Stronger(
            evidence.StatedFrameRate is { } stated
                ? Evidence<double>.From(stated, EvidenceSource.ReleaseTitle)
                : Evidence<double>.None,
            evidence.Probe?.FrameRate is { } measured
                ? Evidence<double>.From(measured, EvidenceSource.ContainerProbe)
                : Evidence<double>.None);

    private static Evidence<Packaging> PackagingOf(ReleaseEvidence evidence)
    {
        var claimed = Normalize(evidence.PackagingToken) switch
        {
            EvidencePackagingTokens.DiscImage => Stated(Packaging.DiscImage),
            EvidencePackagingTokens.DiscFolder => Stated(Packaging.DiscFolder),
            EvidencePackagingTokens.SingleFile => Stated(Packaging.SingleFile),
            _ => Evidence<Packaging>.None,
        };

        var container = Container(evidence);

        var inferred = Holds(DiscImageContainers, container) ? Guessed(Packaging.DiscImage)
            : Holds(StreamContainers, container) ? Guessed(Packaging.SingleFile)
            : Evidence<Packaging>.None;

        return EvidenceMerge.Stronger(inferred, claimed);

        static Evidence<Packaging> Stated(Packaging packaging) =>
            Evidence<Packaging>.From(packaging, EvidenceSource.ReleaseTitle);

        static Evidence<Packaging> Guessed(Packaging packaging) =>
            Evidence<Packaging>.From(packaging, EvidenceSource.Assumed);
    }

    /// <summary>Reads the defects the release states.</summary>
    /// <remarks>
    /// A screener is a high-fidelity signal with a burn-in, and saying so is more useful than pretending it
    /// is worse than a broadcast capture — so its origin stays its origin and the burn-in arrives here.
    /// </remarks>
    private static EvidenceSet<VideoFlaw> FlawsOf(ReleaseEvidence evidence)
    {
        var screener = Normalize(evidence.SourceToken)
            is EvidenceSourceTokens.Screener
            or EvidenceSourceTokens.StandardDefinitionDiscScreener
            or EvidenceSourceTokens.HighDefinitionDiscScreener;

        if (evidence.FlawTokens.Count == 0 && evidence.ScanType is null && !screener)
        {
            return EvidenceSet<VideoFlaw>.None;
        }

        var found = new List<VideoFlaw>();

        foreach (var token in evidence.FlawTokens)
        {
            var flaw = Normalize(token) switch
            {
                EvidenceFlawTokens.Upscaled => VideoFlaw.Upscaled,
                EvidenceFlawTokens.Sample => VideoFlaw.Sample,
                EvidenceFlawTokens.HardcodedSubtitles => VideoFlaw.HardcodedSubtitles,
                EvidenceFlawTokens.Watermarked => VideoFlaw.Watermarked,
                EvidenceFlawTokens.AdBreaks => VideoFlaw.AdBreaks,
                EvidenceFlawTokens.NetworkLogo => VideoFlaw.NetworkLogo,
                EvidenceFlawTokens.Cropped => VideoFlaw.Cropped,
                _ => (VideoFlaw?)null,
            };

            if (flaw is { } named && !found.Contains(named))
            {
                found.Add(named);
            }
        }

        if (screener && !found.Contains(VideoFlaw.Watermarked))
        {
            found.Add(VideoFlaw.Watermarked);
        }

        if (evidence.ScanType is Quality.ScanType.Interlaced && !found.Contains(VideoFlaw.Interlaced))
        {
            found.Add(VideoFlaw.Interlaced);
        }

        return EvidenceSet<VideoFlaw>.Of(EvidenceSource.ReleaseTitle, [.. found]);
    }

    private static SizeExpectation ExpectedSize(VideoQuality facts, TimeSpan duration) =>
        VideoSizeModel.Expect(
            facts.Resolution.TryGet(out var lines) ? lines : null,
            facts.FrameRate.TryGet(out var rate) ? rate : null,
            facts.Codec.TryGet(out var codec) ? (int)codec : null,
            facts.Origin.TryGet(out var origin) ? (int)origin : null,
            facts.Generation.TryGet(out var generation) ? generation : null,
            facts.Audio.TryGet(out var audio) ? (int)audio : null,
            duration);

    private static Evidence<AudioPresentation> Heard(AudioPresentation presentation) =>
        Evidence<AudioPresentation>.From(presentation, EvidenceSource.ReleaseTitle);

    /// <summary>Reads whether the release states a named dub beside the language it was made in.</summary>
    /// <remarks>
    /// <para>
    /// A dual-language marker on its own is not enough, and the difference is the whole reason this rule
    /// exists. A release that names a language <i>and</i> carries a several-languages marker is stating that
    /// it holds a dub beside the original track — and carrying two full audio tracks is what a bitstream
    /// copy of a disc does and what an encode targeted at a bitrate does not. A bare marker with no named
    /// language says only that more than one language is present, which is true of most discs.
    /// </para>
    /// <para>
    /// <b>What this rule cannot separate, stated rather than hidden.</b> A disc release that names its
    /// <i>encoder</i> has been re-encoded and one that names the disc's own <i>codec format</i> may not have
    /// been — which is the discriminator that would finish this rule off. The scan folds both spellings onto
    /// one codec token, so the distinction is gone before this is read, and a dubbed disc encode that states
    /// an encoder still reads as a bitstream copy here.
    /// </para>
    /// </remarks>
    private static bool StatesADubbedDiscEncode(ReleaseEvidence evidence)
    {
        var marked = false;
        var named = false;

        foreach (var claim in evidence.Languages)
        {
            marked |= claim.IsDualLanguageMarker;
            named |= !claim.IsDualLanguageMarker;
        }

        return marked && named;
    }

    private static bool StatesRip(ReleaseEvidence evidence) =>
        Normalize(evidence.SourceToken)
            is EvidenceSourceTokens.BluRayRip
            or EvidenceSourceTokens.BluRayRipOfRip
            or EvidenceSourceTokens.UltraHighDefinitionDiscRip
            or EvidenceSourceTokens.UltraHighDefinitionDiscDownConvert
            or EvidenceSourceTokens.HighDefinitionDvdRip
            or EvidenceSourceTokens.DvdRip
            or EvidenceSourceTokens.WebRip;

    private static string? Container(ReleaseEvidence evidence) => Normalize(evidence.Container);

    private static bool Holds(string[] containers, string? container) =>
        container is not null && Array.IndexOf(containers, container) >= 0;

    private static string? Normalize(string? token) =>
        string.IsNullOrEmpty(token) ? null : token.ToLowerInvariant();
}
