using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// The normalized source vocabulary a release-title scan settles on.
/// </summary>
/// <remarks>
/// <para>
/// A release title spells one signal a dozen ways. The scanners fold every spelling onto exactly one of
/// these constants, and nothing downstream ever sees a spelling. The constants are declared rather than
/// written as literals at both ends because they are a vocabulary shared between the scan that produces
/// them and the reading that interprets them: a typo in a literal on either side is a silent miss, and a
/// typo in a constant is a build failure.
/// </para>
/// <para>
/// A member names a <i>signal</i> and never a rank. Whether a disc bitstream outranks a stream capture is
/// a question about preference, and nothing in this file is allowed to have an opinion about it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceSourceTokens
{
    /// <summary>A Blu-ray or UHD Blu-ray, with nothing said about re-encoding.</summary>
    public const string BluRayDisc = "bluray";

    /// <summary>A Blu-ray re-encoded to a target bitrate.</summary>
    public const string BluRayRip = "bdrip";

    /// <summary>A re-encode of an existing Blu-ray rip.</summary>
    public const string BluRayRipOfRip = "brrip";

    /// <summary>A UHD Blu-ray re-encoded, stated by a compound token.</summary>
    public const string UltraHighDefinitionDiscRip = "uhdbdrip";

    /// <summary>A UHD Blu-ray re-encoded down to a smaller raster, stated by a compound token.</summary>
    public const string UltraHighDefinitionDiscDownConvert = "uhd2bd";

    /// <summary>An HD DVD.</summary>
    public const string HighDefinitionDvdDisc = "hddvd";

    /// <summary>An HD DVD re-encoded.</summary>
    public const string HighDefinitionDvdRip = "hddvdrip";

    /// <summary>A DVD, with nothing said about re-encoding.</summary>
    public const string DvdDisc = "dvd";

    /// <summary>A DVD re-encoded.</summary>
    public const string DvdRip = "dvdrip";

    /// <summary>A recordable DVD copy.</summary>
    public const string DvdRecordable = "dvdr";

    /// <summary>A streaming service's transmission, remuxed rather than re-encoded.</summary>
    public const string WebDownload = "webdl";

    /// <summary>A streaming service's transmission, re-encoded.</summary>
    public const string WebRip = "webrip";

    /// <summary>A streaming service's transmission, with nothing said about re-encoding.</summary>
    public const string Web = "web";

    /// <summary>A high-definition broadcast capture.</summary>
    public const string HighDefinitionBroadcast = "hdtv";

    /// <summary>A standard-definition digital broadcast capture.</summary>
    public const string PublicDigitalBroadcast = "pdtv";

    /// <summary>A standard-definition broadcast capture.</summary>
    public const string StandardDefinitionBroadcast = "sdtv";

    /// <summary>A broadcast capture whose transmission path is not stated.</summary>
    public const string BroadcastRip = "tvrip";

    /// <summary>A digital satellite recording.</summary>
    public const string SatelliteRecording = "dsr";

    /// <summary>A projection re-photographed with a camera, with room audio.</summary>
    public const string CameraCapture = "cam";

    /// <summary>A projection re-photographed with a camera, with a line audio feed.</summary>
    public const string TeleSync = "telesync";

    /// <summary>A physical film print, scanned.</summary>
    public const string TeleCine = "telecine";

    /// <summary>A distribution copy sent for review, watermarked.</summary>
    public const string Screener = "screener";

    /// <summary>A standard-definition disc screener.</summary>
    public const string StandardDefinitionDiscScreener = "dvdscr";

    /// <summary>A high-definition disc screener.</summary>
    public const string HighDefinitionDiscScreener = "bdscr";

    /// <summary>An unfinished edit.</summary>
    public const string WorkPrint = "workprint";

    /// <summary>A regionally released print, re-encoded.</summary>
    public const string RegionalPrint = "regional";
}

/// <summary>
/// The normalized video-codec vocabulary.
/// </summary>
/// <remarks>
/// Efficiency is what these members are ordered by wherever they are ordered, and efficiency is not
/// fidelity: at an equal quality target a more efficient codec produces a smaller file of the same
/// quality, not a better one. This file states no order at all; the order belongs to whoever declares
/// the axis.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceVideoCodecTokens
{
    /// <summary>MPEG-2 part 2, the broadcast and DVD codec.</summary>
    public const string Mpeg2 = "mpeg2";

    /// <summary>MPEG-4 part 2, spelled DivX or Xvid in the field.</summary>
    public const string Mpeg4Part2 = "mpeg4-part2";

    /// <summary>SMPTE VC-1.</summary>
    public const string Vc1 = "vc1";

    /// <summary>H.264 / MPEG-4 AVC.</summary>
    public const string H264 = "h264";

    /// <summary>Google VP9.</summary>
    public const string Vp9 = "vp9";

    /// <summary>H.265 / HEVC.</summary>
    public const string H265 = "h265";

    /// <summary>AOMedia AV1.</summary>
    public const string Av1 = "av1";

    /// <summary>H.266 / VVC.</summary>
    public const string H266 = "h266";
}

/// <summary>
/// The normalized audio-presentation vocabulary.
/// </summary>
/// <remarks>
/// One release states several of these at once — a lossy core beside a lossless extension, or a stereo
/// commentary beside a surround program — and the evidence record carries a single token, so the scan
/// reports the richest presentation it found. The ranking that decides which is richest is a physical
/// fact about what survives to the speakers, not a preference.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceAudioTokens
{
    /// <summary>Uncompressed linear samples.</summary>
    public const string Pcm = "pcm";

    /// <summary>Dolby TrueHD carrying an object layer.</summary>
    public const string TrueHdWithObjects = "truehd-atmos";

    /// <summary>Dolby TrueHD.</summary>
    public const string TrueHd = "truehd";

    /// <summary>DTS:X, an object presentation over a lossless bed.</summary>
    public const string DtsWithObjects = "dtsx";

    /// <summary>DTS-HD Master Audio.</summary>
    public const string DtsHdMasterAudio = "dts-hd-ma";

    /// <summary>DTS-HD High Resolution.</summary>
    public const string DtsHdHighResolution = "dts-hd";

    /// <summary>DTS core.</summary>
    public const string Dts = "dts";

    /// <summary>Free lossless audio codec.</summary>
    public const string Flac = "flac";

    /// <summary>Enhanced AC-3 carrying an object layer.</summary>
    public const string EnhancedAc3WithObjects = "eac3-atmos";

    /// <summary>An object layer whose bed codec the release does not state.</summary>
    public const string ObjectsOnly = "atmos";

    /// <summary>Enhanced AC-3, spelled DD+ or DDP in the field.</summary>
    public const string EnhancedAc3 = "eac3";

    /// <summary>AC-3, spelled DD in the field.</summary>
    public const string Ac3 = "ac3";

    /// <summary>Advanced audio coding.</summary>
    public const string Aac = "aac";

    /// <summary>MPEG-1 audio layer III.</summary>
    public const string Mp3 = "mp3";

    /// <summary>MPEG-1 audio layer II.</summary>
    public const string Mp2 = "mp2";

    /// <summary>Opus.</summary>
    public const string Opus = "opus";

    /// <summary>Vorbis.</summary>
    public const string Vorbis = "vorbis";

    /// <summary>Windows Media Audio.</summary>
    public const string WindowsMediaAudio = "wma";
}

/// <summary>
/// The normalized dynamic-range vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// A release genuinely carries more than one of these at a time — a Dolby Vision layer over an HDR10+
/// base is real and increasingly common — so the scan reports a set and never collapses two stated
/// formats into one invented member.
/// </para>
/// <para>
/// <see cref="DolbyVisionWithFallback"/> is declared and never scanned. No release title spells it
/// distinctly: what a title states is the two layers, and whether the base layer is a usable fallback is
/// a fact about the file that a probe answers. It is declared here so the vocabulary is complete for
/// whoever reads it onto an axis, and its absence from the scan is the honest report.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceDynamicRangeTokens
{
    /// <summary>Standard dynamic range, stated explicitly.</summary>
    public const string StandardDynamicRange = "sdr";

    /// <summary>Hybrid log-gamma.</summary>
    public const string HybridLogGamma = "hlg";

    /// <summary>HDR10, static metadata.</summary>
    public const string HighDynamicRange10 = "hdr10";

    /// <summary>HDR10+, dynamic metadata.</summary>
    public const string HighDynamicRange10Plus = "hdr10plus";

    /// <summary>Dolby Vision.</summary>
    public const string DolbyVision = "dolby-vision";

    /// <summary>Dolby Vision over a base layer a non-Dolby display can render. Never scanned.</summary>
    public const string DolbyVisionWithFallback = "dolby-vision-with-fallback";
}

/// <summary>
/// The normalized defect vocabulary.
/// </summary>
/// <remarks>
/// A defect is something the file carries that the viewer did not ask for, and it is deliberately kept
/// apart from the signal the file descends from: a disc screener is a high-fidelity signal with a
/// burn-in, and saying so is more useful than pretending it is worse than a broadcast capture.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceFlawTokens
{
    /// <summary>The raster was enlarged from a smaller one.</summary>
    public const string Upscaled = "upscaled";

    /// <summary>A short extract rather than the whole program.</summary>
    public const string Sample = "sample";

    /// <summary>Subtitles burned into the picture.</summary>
    public const string HardcodedSubtitles = "hardcoded-subtitles";

    /// <summary>A distribution mark burned into the picture.</summary>
    public const string Watermarked = "watermarked";

    /// <summary>Advertising breaks left in.</summary>
    public const string AdBreaks = "ad-breaks";

    /// <summary>A transmitting network's mark burned into the picture. Never scanned.</summary>
    public const string NetworkLogo = "network-logo";

    /// <summary>Picture removed from the edges.</summary>
    public const string Cropped = "cropped";
}

/// <summary>
/// The normalized packaging vocabulary.
/// </summary>
/// <remarks>
/// None of these is better than another and this file says so by declaring them flat. What a user makes
/// of a whole-disc copy is a policy question, and the ladder this model replaces got that wrong in its
/// own file's own comment.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidencePackagingTokens
{
    /// <summary>One playable file.</summary>
    public const string SingleFile = "single-file";

    /// <summary>A whole disc as an image.</summary>
    public const string DiscImage = "disc-image";

    /// <summary>A whole disc as a directory tree.</summary>
    public const string DiscFolder = "disc-folder";
}

/// <summary>
/// The normalized streaming-distributor vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// A distributor code is claimed only beside a stream-class source token. The codes are two to four
/// letters and collide with initialisms, group names and audio qualifiers, and a false distributor claim
/// is worse than a missing one: a preference for one service silently starts matching releases from
/// another.
/// </para>
/// <para>
/// Two well-known codes are deliberately absent. <c>MA</c> is not claimed because it is how DTS-HD
/// Master Audio is spelled in the field, which appears in the corpus beside a genuine stream token.
/// <c>RED</c> is not claimed because it is an ordinary word.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class EvidenceDistributorTokens
{
    /// <summary>Amazon.</summary>
    public const string Amazon = "amzn";

    /// <summary>Netflix.</summary>
    public const string Netflix = "nf";

    /// <summary>Disney+.</summary>
    public const string Disney = "dsnp";

    /// <summary>HBO Max.</summary>
    public const string HboMax = "hmax";

    /// <summary>Hulu.</summary>
    public const string Hulu = "hulu";

    /// <summary>Apple TV+.</summary>
    public const string AppleTelevision = "atvp";

    /// <summary>Peacock.</summary>
    public const string Peacock = "pcok";

    /// <summary>Paramount+.</summary>
    public const string Paramount = "pmtp";

    /// <summary>Stan.</summary>
    public const string Stan = "stan";

    /// <summary>Crave.</summary>
    public const string Crave = "crav";

    /// <summary>BBC iPlayer.</summary>
    public const string Iplayer = "iplayer";

    /// <summary>ITV.</summary>
    public const string Itv = "itv";

    /// <summary>Showtime.</summary>
    public const string Showtime = "sho";

    /// <summary>Starz.</summary>
    public const string Starz = "starz";

    /// <summary>Crunchyroll.</summary>
    public const string Crunchyroll = "crunchyroll";

    /// <summary>Funimation.</summary>
    public const string Funimation = "funi";

    /// <summary>Pluto TV.</summary>
    public const string Pluto = "pluto";

    /// <summary>Tubi.</summary>
    public const string Tubi = "tubi";

    /// <summary>The Roku Channel.</summary>
    public const string Roku = "roku";
}
