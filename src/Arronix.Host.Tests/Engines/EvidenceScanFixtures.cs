using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Parsing.Evidence;

// Quality contracts are experimental; the scan produces one of them.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The release shapes the quality-axes design hand-mapped, and the scan over them.
/// </summary>
/// <remarks>
/// <para>
/// The design document maps thirty release shapes by hand and records, for each, what the model is
/// supposed to read out of it. Those thirty are the closest thing the scanners have to an acceptance
/// test, so they are transcribed here as named constants rather than inlined: a row is referred to by
/// name from several fixtures, and a shape that appears twice under two spellings is a shape that will
/// eventually disagree with itself.
/// </para>
/// <para>
/// The names carry the row number from the design's table so a failure points at the paragraph that
/// decided it. Work titles are the sanitized placeholders the design itself uses.
/// </para>
/// </remarks>
internal static class EvidenceScanFixtures
{
    /// <summary>Row 1 — an interlaced disc bitstream below high definition.</summary>
    internal const string InterlacedDiscBitstream = "Movie.Name.2011.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR";

    /// <summary>Row 2 — a disc bitstream at 720 lines.</summary>
    internal const string DiscBitstreamAt720 = "Movie.Hunter.2018.720p.Blu-ray.Remux.AVC.FLAC.2.0-SiCFoI";

    /// <summary>Row 3 — a dual-language disc encode, which is what makes it a bitstream copy.</summary>
    internal const string DualLanguageDiscEncode =
        "This.Wonderful.Movie.1991.German.ML.1080p.BluRay.AVC-GeRMaNSCeNEGRoUP";

    /// <summary>Row 4 — the codec the previous vocabulary could not see.</summary>
    internal const string VeryVersatileCodec = "Movie.Title.1956.German.DL.2160p.HDR.UHDBDRip.h266-GROUP";

    /// <summary>Row 5 — a marketing raster name and no source at all.</summary>
    internal const string MarketingRasterOnly = "[NOGRP][国漫][诛仙][Movie Title 2022][19][HEVC][GB][4K]";

    /// <summary>Row 6 — a bitstream claim with no source token beside it.</summary>
    internal const string OrphanBitstreamClaim = "Movie.Title.2016.T1.UHDRemux.2160p.HEVC.Dual.AC3.5.1-TrueHD.5.1.Sub";

    /// <summary>Row 7 — the same, spelled as a bracketed compound, with a file extension.</summary>
    internal const string OrphanBitstreamBracketed =
        "Movie Name (2021) [Remux-2160p x265 HDR 10-BIT DTS-HD MA 7.1]-FraMeSToR.mkv";

    /// <summary>Row 8 — a broadcast transport stream, told apart by its codec.</summary>
    internal const string BroadcastTransportStream = "Movie.Title.2015.Open.Matte.1080i.HDTV.DD5.1.MPEG2";

    /// <summary>Row 9 — an intermediate raster a bucketing scan would have destroyed.</summary>
    internal const string IntermediateRaster = "[SubsPlease] Movie Title (540p) [AB649D32].mkv";

    /// <summary>Row 10 — a bracketed stream marker beside a container marker.</summary>
    internal const string BracketedStreamMarker =
        "[HorribleSubs] Movie Title! 2018 [Web][MKV][h264][480p][AAC 2.0][Softsubs]";

    /// <summary>Row 11 — a two-letter disc abbreviation that is not at the end of the title.</summary>
    internal const string BracketedDiscAbbreviation = "[FFF] Movie Name - 01 [BD][720p-AAC][0601BED4]";

    /// <summary>Row 12 — the raster stated as pixel dimensions.</summary>
    internal const string PixelDimensions = "[Coalgirls]_Movie!!_01_(1920x1080_Blu-ray_FLAC)_[8370CB8F].mkv";

    /// <summary>Row 13 — a re-issue number and a source welded to a raster.</summary>
    internal const string CompactSourceAndRaster = "[coldhell] Movie v2 [BD1080p][5A45EABE].mkv";

    /// <summary>Row 14 — an explicit whole-disc statement.</summary>
    internal const string StatedDiscImage = "Movie.Title.2013.BDISO";

    /// <summary>Row 15 — a title a disc heuristic used to refuse, wrongly.</summary>
    internal const string NotADiscImage = "Movie Title 2005 1080p USA Blu-ray AVC DTS-HD MA 5.1-PTP";

    /// <summary>Row 16 — a work title a disc heuristic used to refuse, more wrongly.</summary>
    internal const string WorkTitleNotADiscImage = "The German 2021 Bluray AVC";

    /// <summary>Row 17 — a dual-language marker that must not promote, because a rip token is present.</summary>
    internal const string DualLanguageDiscRip = "Movie.Title.2019.German.DL.1080p.HDR.UHDBDRip.AV1-GROUP";

    /// <summary>Row 18 — a compound down-conversion token.</summary>
    internal const string DiscDownConversion = "Movie.Title.2012.German.DL.1080p.UHD2BD.x264-QfG";

    /// <summary>Row 19 — a line count and a marketing name in one title.</summary>
    internal const string TwoRasterClaims = "Movie Name 2005 1080p UHD BluRay DD+7.1 x264-LoRD.mkv";

    /// <summary>Row 20 — a high-definition disc format the vocabulary must still know.</summary>
    internal const string HighDefinitionDvdRip = "Movie.Title.2005.1080p.HDDVDRip.x264";

    /// <summary>Row 21 — a distributor code beside a stream source.</summary>
    internal const string DistributorBesideStream = "Movie.Title.2019.1080p.AMZN.WEB-Rip.DDP.5.1.HEVC";

    /// <summary>Row 22 — two dynamic-range formats at once.</summary>
    internal const string TwoDynamicRanges =
        "Movie.Name.2024.German.AC3D.DL.2160p.Hybrid.WEB.DV.HDR10Plus.HEVC-GROUP";

    /// <summary>Row 23 — a self-describing several-languages marker.</summary>
    internal const string SeveralLanguagesMarker = "Movie.Title.2020.MULTi.1080p.WEB.H264-ALLDAYiN (S:285/L:11)";

    /// <summary>Row 24 — a bare stream word that a raster supports.</summary>
    internal const string SupportedBareStreamWord = "Movie.Name.2020.1080p.AMZN.WEB";

    /// <summary>Row 25 — a release-selection marker that is not quality.</summary>
    internal const string SelectionMarkerIsNotQuality = "Movie.Name.S04E01.iNTERNAL.1080p.WEBRip.x264-QRUS";

    /// <summary>Row 26 — a broadcast source split across two segments by separators.</summary>
    internal const string SeparatedBroadcastSource = "Some Movie 2018 2160p_HD_TV";

    /// <summary>Row 27 — a second repack, which is the third issue.</summary>
    internal const string SecondRepack = "Movie.Title.2018.REPACK2.720p.HDTV.x264-aAF";

    /// <summary>Row 28 — subtitles burned into the picture.</summary>
    internal const string BurnedInSubtitles = "Movie.Title.2016.1080p.KORSUB.WEBRip.x264.AAC2.0-RADARR";

    /// <summary>Row 29 — a codec alone, with the positive inference deliberately declined.</summary>
    internal const string CodecAlone = "Movie.2008.X264-DIMENSION";

    /// <summary>Row 30 — a work title containing an ordinary word the vocabulary also uses.</summary>
    internal const string WorkTitleContainingVocabulary = "Movie.Title.S01E01.The.Web.MT-dd";

    /// <summary>Scans a title with nothing else known about it.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The evidence.</returns>
    internal static ReleaseEvidence Scan(string title) =>
        ReleaseEvidenceScanner.Scan(new EvidenceScanRequest { Title = title });

}
