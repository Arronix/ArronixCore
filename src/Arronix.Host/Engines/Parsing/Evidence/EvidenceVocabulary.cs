using System.Linq;
using Arronix.Abstractions.Quality;

// Quality contracts are experimental; the vocabulary states resolution claim forms in their vocabulary.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// The release-title vocabulary the host scans, as phrases rather than as expressions.
/// </summary>
/// <remarks>
/// <para>
/// Every entry is a <i>phrase</i>: one to three normalized segments, joined by single spaces, mapped to
/// the tokens it produces. Nothing here is an alternation and nothing here is ordered — the lexer's
/// longest-phrase-first walk supplies the only precedence there is, which means "the more specific
/// spelling wins" is a property of the walk rather than a property of where a row sits in a list.
/// </para>
/// <para>
/// One phrase may produce several tokens. A disc-image spelling states both how the release is packaged
/// and what disc it came from; a screener spelling states both a signal and a burn-in. Producing two
/// tokens is how the vocabulary avoids the alternative, which is a member of one vocabulary that
/// secretly means a member of another.
/// </para>
/// </remarks>
internal static class EvidenceVocabulary
{
    /// <summary>The greatest number of adjacent segments any phrase spans.</summary>
    internal const int LongestPhrase = 3;

    /// <summary>Richness rank of an audio presentation whose bed is object-carrying and lossless.</summary>
    private const int LosslessWithObjects = 6;

    /// <summary>Richness rank of a lossless audio presentation.</summary>
    private const int Lossless = 5;

    /// <summary>Richness rank of a lossy audio presentation carrying an object layer.</summary>
    private const int LossyWithObjects = 4;

    /// <summary>Richness rank of a lossy multi-channel audio presentation.</summary>
    private const int LossySurround = 3;

    /// <summary>Richness rank of a lossy two-channel audio presentation.</summary>
    private const int LossyStereo = 2;

    /// <summary>Gets every phrase the scan recognizes, keyed by its normalized spelling.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<EvidencePhrase>> Phrases { get; } = BuildPhrases();

    /// <summary>Gets how much of the original signal each audio presentation retains.</summary>
    /// <remarks>
    /// Used only to choose between several presentations one release states at once. It is a fact about
    /// what reaches the speakers, and it is not a preference: a listener who wants the small file is
    /// expressing a policy and reads the same evidence.
    /// </remarks>
    internal static IReadOnlyDictionary<string, int> AudioRichness { get; } = BuildAudioRichness();

    /// <summary>Gets every language spelling the scan recognizes.</summary>
    internal static IReadOnlyDictionary<string, EvidenceLanguageName> LanguageNames { get; } = BuildLanguages();

    private static Dictionary<string, IReadOnlyList<EvidencePhrase>> BuildPhrases()
    {
        var map = new Dictionary<string, IReadOnlyList<EvidencePhrase>>(StringComparer.Ordinal);

        AddSources(map);
        AddRemuxSpellings(map);
        AddResolutionNames(map);
        AddVideoCodecs(map);
        AddAudioFormats(map);
        AddDynamicRanges(map);
        AddContainersAndPackaging(map);
        AddFlaws(map);
        AddLanguageMarkers(map);
        AddDistributors(map);

        return map;
    }

    private static void AddSources(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        // "UHD" is deliberately not joined to a following disc word. It is a marketing raster name in its
        // own right, a title carrying both a line count and a marketing name is the case the resolution
        // scanner's specificity rule exists for, and joining the two here would quietly settle that case
        // in the lexer where nobody could see it happen.
        var disc = EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.BluRayDisc);
        Add(map, disc, "bluray", "blu ray", "bluray disc", "bd", "uhdbd");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.BluRayRip), "bdrip", "bd rip");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.BluRayRipOfRip), "brrip", "br rip");
        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.UltraHighDefinitionDiscRip),
            "uhdbdrip");
        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.UltraHighDefinitionDiscDownConvert),
            "uhd2bd");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.HighDefinitionDvdDisc), "hddvd", "hd dvd");
        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.HighDefinitionDvdRip),
            "hddvdrip",
            "hd dvd rip",
            "hddvd rip");

        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.DvdDisc), "dvd", "dvd5", "dvd9");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.DvdRip), "dvdrip", "dvd rip");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.DvdRecordable), "dvdr");

        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.WebDownload), "webdl", "web dl", "web download");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.WebRip), "webrip", "web rip");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.Web), "web");

        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.HighDefinitionBroadcast),
            "hdtv",
            "hd tv",
            "uhdtv",
            "ahdtv",
            "hdtvrip");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.PublicDigitalBroadcast), "pdtv");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.StandardDefinitionBroadcast), "sdtv", "sd tv");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.BroadcastRip), "tvrip", "tv rip", "dvbrip");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.SatelliteRecording), "dsr", "dsrip", "satrip");

        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.CameraCapture), "cam", "camrip", "hdcam", "pdvd");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.TeleSync), "telesync", "tele sync", "hdts", "ts");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.TeleCine), "telecine", "tele cine", "hdtc", "tc");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.WorkPrint), "workprint", "work print", "wp");
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.RegionalPrint), "regional", "r5");

        // A screener states two independent things at once: which disc the signal came off, and that a
        // distribution mark is burned into the picture. Both are stated, so both are produced.
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.Screener),
                EvidencePhrase.Of(EvidenceTokenClass.Flaw, EvidenceFlawTokens.Watermarked)
            ],
            "screener",
            "scr");
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.StandardDefinitionDiscScreener),
                EvidencePhrase.Of(EvidenceTokenClass.Flaw, EvidenceFlawTokens.Watermarked)
            ],
            "dvdscr",
            "dvd scr",
            "dvdscreener");
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.HighDefinitionDiscScreener),
                EvidencePhrase.Of(EvidenceTokenClass.Flaw, EvidenceFlawTokens.Watermarked)
            ],
            "bdscr",
            "bd scr");
    }

    private static void AddRemuxSpellings(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        // A remux spelling states that the file carries a master's own bitstream. It deliberately states
        // no source: "UHDRemux" names the packaging step, not a disc the scan measured, and turning it
        // into a source claim here would be an inference dressed as a reading.
        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.Remux, "remux"),
            "remux",
            "bdremux",
            "uhdremux",
            "brremux",
            "dvdremux");
    }

    private static void AddResolutionNames(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        // Marketing names only. A bare "HD" or "SD" is deliberately absent: both occur as qualifiers
        // inside audio and source phrases ("DTS HD MA", "HD TV") and neither states a raster.
        AddResolution(map, 2160, "4k", "uhd", "ultra hd", "uhd4k");
        AddResolution(map, 4320, "8k");
        AddResolution(map, 1440, "qhd", "wqhd");
        AddResolution(map, 1080, "fhd", "fullhd", "full hd", "2k");
    }

    private static void AddVideoCodecs(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        Add(map, Codec(EvidenceVideoCodecTokens.H264), "x264", "h264", "h 264", "avc", "avc1");
        Add(map, Codec(EvidenceVideoCodecTokens.H265), "x265", "h265", "h 265", "hevc");
        Add(map, Codec(EvidenceVideoCodecTokens.H266), "x266", "h266", "h 266", "vvc");
        Add(map, Codec(EvidenceVideoCodecTokens.Av1), "av1");
        Add(map, Codec(EvidenceVideoCodecTokens.Vp9), "vp9");
        Add(map, Codec(EvidenceVideoCodecTokens.Mpeg4Part2), "xvid", "divx", "mpeg4", "mpeg 4");
        Add(map, Codec(EvidenceVideoCodecTokens.Mpeg2), "mpeg2", "mpeg 2");
        Add(map, Codec(EvidenceVideoCodecTokens.Vc1), "vc1", "vc 1");
    }

    private static void AddAudioFormats(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        Add(map, Audio(EvidenceAudioTokens.Pcm), "pcm", "lpcm");
        Add(map, Audio(EvidenceAudioTokens.TrueHdWithObjects), "truehd atmos", "true hd atmos");
        Add(map, Audio(EvidenceAudioTokens.TrueHd), "truehd", "true hd");
        Add(map, Audio(EvidenceAudioTokens.DtsWithObjects), "dtsx", "dts x");
        Add(map, Audio(EvidenceAudioTokens.DtsHdMasterAudio), "dtshdma", "dts hd ma", "dtshd ma");
        Add(map, Audio(EvidenceAudioTokens.DtsHdHighResolution), "dtshd", "dts hd", "dtshdhr");
        Add(map, Audio(EvidenceAudioTokens.Dts), "dts", "dtses");
        Add(map, Audio(EvidenceAudioTokens.Flac), "flac");
        Add(map, Audio(EvidenceAudioTokens.EnhancedAc3WithObjects), "ddp atmos", "dd+ atmos", "eac3 atmos");
        Add(map, Audio(EvidenceAudioTokens.ObjectsOnly), "atmos");
        Add(map, Audio(EvidenceAudioTokens.EnhancedAc3), "eac3", "ddp", "dd+", "ddplus");
        Add(map, Audio(EvidenceAudioTokens.Ac3), "ac3", "ac3d", "dd");
        Add(map, Audio(EvidenceAudioTokens.Aac), "aac");
        Add(map, Audio(EvidenceAudioTokens.Mp3), "mp3");
        Add(map, Audio(EvidenceAudioTokens.Mp2), "mp2");
        Add(map, Audio(EvidenceAudioTokens.Opus), "opus");
        Add(map, Audio(EvidenceAudioTokens.Vorbis), "vorbis", "ogg");
        Add(map, Audio(EvidenceAudioTokens.WindowsMediaAudio), "wma");
    }

    private static void AddDynamicRanges(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        Add(map, Range(EvidenceDynamicRangeTokens.StandardDynamicRange), "sdr");
        Add(map, Range(EvidenceDynamicRangeTokens.HybridLogGamma), "hlg");
        Add(map, Range(EvidenceDynamicRangeTokens.HighDynamicRange10Plus), "hdr10plus", "hdr10+", "hdr 10 plus");
        Add(map, Range(EvidenceDynamicRangeTokens.HighDynamicRange10), "hdr10", "hdr", "hdr 10");
        Add(map, Range(EvidenceDynamicRangeTokens.DolbyVision), "dolby vision", "dovi", "dolbyvision", "dv");
    }

    private static void AddContainersAndPackaging(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        AddContainer(map, ".mkv", "mkv");
        AddContainer(map, ".mp4", "mp4");
        AddContainer(map, ".m4v", "m4v");
        AddContainer(map, ".avi", "avi");
        AddContainer(map, ".m2ts", "m2ts");
        AddContainer(map, ".vob", "vob");
        AddContainer(map, ".wmv", "wmv");
        AddContainer(map, ".webm", "webm");
        AddContainer(map, ".mov", "mov");
        AddContainer(map, ".mk3d", "mk3d");
        AddContainer(map, ".ogm", "ogm");

        // A disc image is a container and a packaging statement at once.
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Container, ".iso"),
                EvidencePhrase.Of(EvidenceTokenClass.Packaging, EvidencePackagingTokens.DiscImage)
            ],
            "iso");
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Container, ".img"),
                EvidencePhrase.Of(EvidenceTokenClass.Packaging, EvidencePackagingTokens.DiscImage)
            ],
            "img");

        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Packaging, EvidencePackagingTokens.DiscImage),
                EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.BluRayDisc)
            ],
            "bdiso",
            "br disk",
            "brdisk",
            "bd25",
            "bd50",
            "bd66",
            "bd100",
            "complete bluray");
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Packaging, EvidencePackagingTokens.DiscImage),
                EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.DvdDisc)
            ],
            "dvdiso");

        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.Packaging, EvidencePackagingTokens.DiscFolder),
            "bdmv",
            "avchd");
        Add(
            map,
            [
                EvidencePhrase.Of(EvidenceTokenClass.Packaging, EvidencePackagingTokens.DiscFolder),
                EvidencePhrase.Of(EvidenceTokenClass.Source, EvidenceSourceTokens.DvdDisc)
            ],
            "video ts",
            "audio ts");
    }

    private static void AddFlaws(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        Add(map, Flaw(EvidenceFlawTokens.Sample), "sample");
        Add(
            map,
            Flaw(EvidenceFlawTokens.HardcodedSubtitles),
            "korsub",
            "hardsub",
            "hardsubs",
            "hardsubbed",
            "hardcoded",
            "hard sub",
            "hard subbed",
            "hard coded");
        Add(map, Flaw(EvidenceFlawTokens.Upscaled), "upscale", "upscaled", "upscaling");
        Add(map, Flaw(EvidenceFlawTokens.Watermarked), "watermark", "watermarked");
        Add(map, Flaw(EvidenceFlawTokens.Cropped), "cropped");
        Add(map, Flaw(EvidenceFlawTokens.AdBreaks), "adbreaks", "ad break", "ad breaks", "with ads");
    }

    private static void AddLanguageMarkers(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        Add(
            map,
            EvidencePhrase.Of(EvidenceTokenClass.LanguageMarker, "multiple"),
            "multi",
            "dual",
            "dualaudio",
            "dual audio",
            "dl",
            "ml");
    }

    private static void AddDistributors(IDictionary<string, IReadOnlyList<EvidencePhrase>> map)
    {
        Add(map, Service(EvidenceDistributorTokens.Amazon), "amzn", "amazon");
        Add(map, Service(EvidenceDistributorTokens.Netflix), "nf", "netflix");
        Add(map, Service(EvidenceDistributorTokens.Disney), "dsnp", "disney");
        Add(map, Service(EvidenceDistributorTokens.HboMax), "hmax");
        Add(map, Service(EvidenceDistributorTokens.Hulu), "hulu");
        Add(map, Service(EvidenceDistributorTokens.AppleTelevision), "atvp", "appletv");
        Add(map, Service(EvidenceDistributorTokens.Peacock), "pcok");
        Add(map, Service(EvidenceDistributorTokens.Paramount), "pmtp");
        Add(map, Service(EvidenceDistributorTokens.Stan), "stan");
        Add(map, Service(EvidenceDistributorTokens.Crave), "crav");
        Add(map, Service(EvidenceDistributorTokens.Iplayer), "iplayer");
        Add(map, Service(EvidenceDistributorTokens.Itv), "itv");
        Add(map, Service(EvidenceDistributorTokens.Showtime), "sho");
        Add(map, Service(EvidenceDistributorTokens.Starz), "starz");
        Add(map, Service(EvidenceDistributorTokens.Crunchyroll), "crunchyroll");
        Add(map, Service(EvidenceDistributorTokens.Funimation), "funi");
        Add(map, Service(EvidenceDistributorTokens.Pluto), "pluto");
        Add(map, Service(EvidenceDistributorTokens.Tubi), "tubi");
        Add(map, Service(EvidenceDistributorTokens.Roku), "roku");
    }

    private static Dictionary<string, int> BuildAudioRichness() =>
        new(StringComparer.Ordinal)
        {
            [EvidenceAudioTokens.TrueHdWithObjects] = LosslessWithObjects,
            [EvidenceAudioTokens.DtsWithObjects] = LosslessWithObjects,
            [EvidenceAudioTokens.Pcm] = Lossless,
            [EvidenceAudioTokens.TrueHd] = Lossless,
            [EvidenceAudioTokens.DtsHdMasterAudio] = Lossless,
            [EvidenceAudioTokens.Flac] = Lossless,
            [EvidenceAudioTokens.EnhancedAc3WithObjects] = LossyWithObjects,
            [EvidenceAudioTokens.ObjectsOnly] = LossyWithObjects,
            [EvidenceAudioTokens.DtsHdHighResolution] = LossyWithObjects,
            [EvidenceAudioTokens.EnhancedAc3] = LossySurround,
            [EvidenceAudioTokens.Ac3] = LossySurround,
            [EvidenceAudioTokens.Dts] = LossySurround,
            [EvidenceAudioTokens.Aac] = LossyStereo,
            [EvidenceAudioTokens.Opus] = LossyStereo,
            [EvidenceAudioTokens.Vorbis] = LossyStereo,
            [EvidenceAudioTokens.Mp3] = LossyStereo,
            [EvidenceAudioTokens.Mp2] = LossyStereo,
            [EvidenceAudioTokens.WindowsMediaAudio] = LossyStereo,
        };

    /// <summary>
    /// Builds the language table.
    /// </summary>
    /// <returns>The table.</returns>
    /// <remarks>
    /// Full English names are always safe. Three-letter codes are admitted only where the code is not
    /// also an ordinary English word or a common given name — <c>spa</c>, <c>fin</c>, <c>may</c>,
    /// <c>cat</c>, <c>ron</c>, <c>hun</c>, <c>pol</c>, <c>tam</c>, <c>ben</c>, <c>est</c>, <c>lit</c>,
    /// <c>tel</c>, <c>ind</c>, <c>nor</c>, <c>dan</c> and <c>rum</c> are all deliberately absent. Two-letter
    /// codes are admitted nowhere at all. The asymmetry is the reason: a missed language leaves a release
    /// unlabeled, and a false language makes a profile that refuses a dub start refusing the original.
    /// </remarks>
    private static Dictionary<string, EvidenceLanguageName> BuildLanguages()
    {
        var map = new Dictionary<string, EvidenceLanguageName>(StringComparer.Ordinal);

        AddLanguage(map, "en", "English", "english", "eng");
        AddLanguage(map, "de", "German", "german", "ger", "deu", "deutsch");
        AddLanguage(map, "fr", "French", "french", "fre", "fra", "truefrench", "vff", "vfq");
        AddLanguage(map, "es", "Spanish", "spanish", "esp", "castellano", "latino");
        AddLanguage(map, "it", "Italian", "italian", "ita");
        AddLanguage(map, "ja", "Japanese", "japanese", "jpn");
        AddLanguage(map, "ko", "Korean", "korean", "kor");
        AddLanguage(map, "zh", "Chinese", "chinese", "zho", "chs", "cht", "mandarin", "cantonese");
        AddLanguage(map, "ru", "Russian", "russian", "rus");
        AddLanguage(map, "pt", "Portuguese", "portuguese", "por", "ptbr", "brazilian");
        AddLanguage(map, "nl", "Dutch", "dutch", "nld");
        AddLanguage(map, "sv", "Swedish", "swedish", "swe");
        AddLanguage(map, "no", "Norwegian", "norwegian");
        AddLanguage(map, "da", "Danish", "danish");
        AddLanguage(map, "fi", "Finnish", "finnish");
        AddLanguage(map, "pl", "Polish", "polish");
        AddLanguage(map, "cs", "Czech", "czech", "cze");
        AddLanguage(map, "sk", "Slovak", "slovak", "slk");
        AddLanguage(map, "hu", "Hungarian", "hungarian");
        AddLanguage(map, "tr", "Turkish", "turkish", "tur");
        AddLanguage(map, "ar", "Arabic", "arabic");
        AddLanguage(map, "he", "Hebrew", "hebrew", "heb");
        AddLanguage(map, "hi", "Hindi", "hindi", "hin");
        AddLanguage(map, "th", "Thai", "thai", "tha");
        AddLanguage(map, "vi", "Vietnamese", "vietnamese", "vie");
        AddLanguage(map, "el", "Greek", "greek", "gre");
        AddLanguage(map, "ro", "Romanian", "romanian");
        AddLanguage(map, "uk", "Ukrainian", "ukrainian", "ukr");
        AddLanguage(map, "bg", "Bulgarian", "bulgarian", "bul");
        AddLanguage(map, "hr", "Croatian", "croatian", "hrv");
        AddLanguage(map, "sr", "Serbian", "serbian", "srp");
        AddLanguage(map, "sl", "Slovenian", "slovenian", "slv");
        AddLanguage(map, "et", "Estonian", "estonian");
        AddLanguage(map, "lv", "Latvian", "latvian", "lav");
        AddLanguage(map, "lt", "Lithuanian", "lithuanian");
        AddLanguage(map, "is", "Icelandic", "icelandic", "isl");
        AddLanguage(map, "ca", "Catalan", "catalan");
        AddLanguage(map, "fa", "Persian", "persian", "farsi");
        AddLanguage(map, "id", "Indonesian", "indonesian");
        AddLanguage(map, "ms", "Malay", "malay", "msa");
        AddLanguage(map, "ta", "Tamil", "tamil");
        AddLanguage(map, "te", "Telugu", "telugu");
        AddLanguage(map, "bn", "Bengali", "bengali");

        return map;
    }

    private static void AddLanguage(
        IDictionary<string, EvidenceLanguageName> map,
        string code,
        string name,
        params string[] spellings)
    {
        var entry = new EvidenceLanguageName(code, name);

        foreach (var spelling in spellings)
        {
            map[spelling] = entry;
        }
    }

    private static EvidencePhrase Codec(string value) => EvidencePhrase.Of(EvidenceTokenClass.VideoCodec, value);

    private static EvidencePhrase Audio(string value) => EvidencePhrase.Of(EvidenceTokenClass.AudioFormat, value);

    private static EvidencePhrase Range(string value) => EvidencePhrase.Of(EvidenceTokenClass.DynamicRange, value);

    private static EvidencePhrase Flaw(string value) => EvidencePhrase.Of(EvidenceTokenClass.Flaw, value);

    private static EvidencePhrase Service(string value) => EvidencePhrase.Of(EvidenceTokenClass.Distributor, value);

    private static void AddContainer(
        IDictionary<string, IReadOnlyList<EvidencePhrase>> map,
        string extension,
        string spelling) =>
        Add(map, EvidencePhrase.Of(EvidenceTokenClass.Container, extension), spelling);

    private static void AddResolution(
        IDictionary<string, IReadOnlyList<EvidencePhrase>> map,
        int lines,
        params string[] spellings) =>
        Add(
            map,
            new EvidencePhrase(EvidenceTokenClass.Resolution, "lines", lines, ResolutionClaimForm.MarketingName),
            spellings);

    private static void Add(
        IDictionary<string, IReadOnlyList<EvidencePhrase>> map,
        EvidencePhrase phrase,
        params string[] spellings) =>
        Add(map, [phrase], spellings);

    private static void Add(
        IDictionary<string, IReadOnlyList<EvidencePhrase>> map,
        IReadOnlyList<EvidencePhrase> produced,
        params string[] spellings)
    {
        foreach (var spelling in spellings)
        {
            if (spelling.Count(static character => character == ' ') + 1 > LongestPhrase)
            {
                throw new InvalidOperationException(
                    $"The phrase '{spelling}' spans more segments than the lexer looks ahead.");
            }

            map[spelling] = produced;
        }
    }
}

/// <summary>
/// One token a phrase produces.
/// </summary>
/// <param name="Class">The functional category.</param>
/// <param name="Value">The normalized value.</param>
/// <param name="Magnitude">The number the phrase states, for the classes that state one.</param>
/// <param name="Form">How a resolution claim was stated.</param>
internal readonly record struct EvidencePhrase(
    EvidenceTokenClass Class,
    string Value,
    double Magnitude,
    ResolutionClaimForm Form)
{
    /// <summary>Creates a phrase that states no number.</summary>
    /// <param name="tokenClass">The functional category.</param>
    /// <param name="value">The normalized value.</param>
    /// <returns>The phrase.</returns>
    internal static EvidencePhrase Of(EvidenceTokenClass tokenClass, string value) =>
        new(tokenClass, value, 0d, ResolutionClaimForm.LineCount);
}

/// <summary>
/// One language, as the scan names it.
/// </summary>
/// <param name="Code">The two-letter code.</param>
/// <param name="Name">The English name.</param>
internal readonly record struct EvidenceLanguageName(string Code, string Name);
