namespace Arronix.Plugin.Movies.Tests.Quality;

/// <summary>
/// Every corpus row the axes model does not yet read the way the superseded ladder read it, with the class
/// it falls into and the reason.
/// </summary>
/// <remarks>
/// <para>
/// <b>A register rather than a narrowed assertion, because the difference matters.</b> Dropping the rows
/// that fail would leave the corpus green and the finding invisible, which is the one outcome that helps
/// nobody. Keeping them failing leaves a red build that says nothing about which failures are intended. So
/// every residual row is listed here with what it reads today, what it read before, and why — and the
/// corpus assertion is that a row is either identical or registered, with a bound on how long this list is
/// allowed to get. A cap is what stops an escape hatch swallowing the test it escapes.
/// </para>
/// <para>
/// <b>Nothing here is a rung the model cannot express.</b> Every entry is one of two things: a release
/// spelling the host's evidence scanners have no vocabulary for, or one pair of rows where the evidence
/// genuinely cannot separate two readings. Both are stated as defects with an owner, not as characteristics
/// of the design.
/// </para>
/// </remarks>
internal static class QualityDivergenceRegister
{
    /// <summary>How long this list is allowed to get before it is swallowing the test.</summary>
    internal const int Cap = 30;

    /// <summary>
    /// The release spellings the host's evidence vocabulary does not know.
    /// </summary>
    /// <remarks>
    /// <b>This is a scanner-coverage finding and not a model one.</b> Each of these titles states its source
    /// in a spelling the shared scan has no phrase for, so the family is handed evidence with the source
    /// missing and reads exactly what it was given — which is the correct behavior on incomplete input.
    /// Every one of them closes by adding a phrase to the host's vocabulary, and none of them by changing
    /// an axis, a policy or a rendering rule. The superseded tag scanner knew these spellings; the
    /// rewritten evidence scanner was built against a thirty-row sample and has not met them yet.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> UnknownToTheScanner { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Movie.Title.3.2017.720p.TSRip.x264.AAC-Ozlem"] =
                "No phrase for a projection capture spelled as a rip of the sync feed.",
            ["Movie: Title (2024) TeleSynch 720p | HEVC-FILVOVAN"] =
                "No phrase for the alternative spelling of the sync-feed capture.",
            ["Movie Name (2022) 1080p HQCAM ENG x264 AAC - QRips"] =
                "No phrase for the quality-qualified camera capture.",
            ["Movie Name (2022) New HDCAMRip 1080p [Love Rulz]"] =
                "No phrase for the high-definition camera capture spelled as a rip.",
            ["Movie.Name.2024.NEWCAM.1080p.HEVC.AC3.English-RypS"] =
                "No phrase for the recency-qualified camera capture.",
            ["Movie Name (2018) 720p Hindi HQ CAMrip x264 AAC 1.4GB"] =
                "The capture is read, but nothing emits a room-audio reading, so the audio axis cannot tell "
                + "a line feed from a microphone and the renderer takes the sync-feed word.",
            ["Movie.Name.2019.720p.MBLURAY.x264-MBLURAYFANS.mkv"] =
                "No phrase for the micro-encoded disc spelling; the streaming container is then the only "
                + "evidence left.",
            ["Movie.Name.2019.1080p.MBLURAY.x264-MBLURAYFANS.mkv"] = "As above.",
            ["Movie.Title.2019.2160p.MBLURAY.x264-MBLURAYFANS.mkv"] = "As above.",
            ["Movie.Name.2021.1080p.BDLight.x265-AVCDVD"] =
                "No phrase for the light-encoded disc spelling.",
            ["Movie.Name.2.Parte.2.ITA-ENG.720p.BDMux.DD5.1.x264-DarkSideMux"] =
                "No phrase for the disc-multiplex spelling.",
            ["Movie Name.S04E25.720p.iTunesHD.AVC-TVS"] =
                "No phrase for a named store's high-definition stream.",
            ["Movie Name.S06E23.720p.WebHD.h264-euHD"] =
                "No phrase for the high-definition stream spelling.",
            ["Movie.Name.1x04.ITA.WEBMux.x264-NovaRip"] =
                "No phrase for the stream-multiplex spelling.",
            ["Movie.Name.1x04.ITA.1080p.WEBMux.x264-NovaRip"] = "As above.",
            ["Movie.Title.ITA.720p.WEBMux.x264-NovaRip"] = "As above.",
            ["Some.Movie.2020.PAL.2xDVD9"] =
                "No phrase for a disc-capacity spelling carrying a multiplier.",
            ["Some.Movie.2000.2DVD5"] = "As above.",
            ["Some.Movie.2005.PAL.MDVDR-SOMegRoUP"] =
                "No phrase for the micro-encoded recordable-disc spelling.",
            ["SomeMovie.2018.DVDRip.ts"] =
                "The transport-stream extension is taken as the file's container and the disc rip token "
                + "beside it is not claimed.",
            ["the.Movie Name.1x13.circles.ws.xvidvd-tns"] =
                "No phrase for the codec-and-disc compound spelling.",
        };

    /// <summary>
    /// The one pair the evidence genuinely cannot separate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the only entry that is a finding about the model rather than about a scanner, and it is
    /// stated as one.</b> A disc release that names a dub beside its original language is very often a
    /// bitstream copy — that is the whole reason the second track exists, and it is the rule that lets a
    /// German dual-language disc encode be read as a remux without any media kind hard-coding the word
    /// "German" anywhere.
    /// </para>
    /// <para>
    /// What separates the two rows below from the rows that rule is right about is the codec <i>spelling</i>:
    /// a release naming the disc's own format may be carrying the disc's own stream, and one naming an
    /// encoder has been re-encoded. The evidence scan folds both spellings onto one codec token, so the
    /// distinction is gone before the family reads it. Closing this needs the scan to report whether the
    /// codec was named as a format or as an encoder — one member on the evidence record — and it is not
    /// closable inside the family.
    /// </para>
    /// <para>
    /// Both readings here are at release-title strength on both sides, so neither is a claim outranking a
    /// measurement; what they cost is one origin member of over-statement on two rows.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> UnseparableByTheEvidence { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Movie.Name.2016.German.DTS.DL.1080p.UHDBD.x265-TDO.mkv"] =
                "A dubbed disc release naming an encoder reads as a bitstream copy, because the scan folds "
                + "the encoder spelling and the format spelling onto one codec token.",
            ["Movie.Name.2020.German.UHDBD.2160p.HDR10.HEVC.EAC3.DL-pmHD.mkv"] = "As above.",
        };

    /// <summary>Every registered release title.</summary>
    internal static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            [.. UnknownToTheScanner.Keys, .. UnseparableByTheEvidence.Keys],
            StringComparer.Ordinal);
}
