#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended declarer.
#pragma warning disable ARX0019 // Definition contracts are experimental; a media extension is their intended declarer.

using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>
/// The parity cases this definition ships with itself: real release names, each pinned to the quality or
/// the title it must read as.
/// </summary>
/// <remarks>
/// <para>
/// Every case below is a release name that broke something once. They are the highest-value asset the
/// surveyed application has, and they are ported rather than paraphrased — a case rewritten to suit an
/// implementation stops being evidence.
/// </para>
/// <para>
/// They ship <i>on the definition</i> rather than only in this extension's test project because a host
/// upgrade changes engine behavior for every kind at once. A definition states the readings it depends on
/// and the host's parity gate keeps them green across engine upgrades; a fixture that lives only beside
/// the extension proves nothing about the engine that will execute it.
/// </para>
/// <para>
/// Cases naming a season and an episode are kept verbatim from the surveyed corpus. They are not movie
/// releases and they are not read as such: the quality scan reads tokens out of any text, which is
/// precisely the evidence that it is not movie semantics. Under the axes model that stops being a defect
/// in the corpus — a quality expectation does not go through title parsing at all, so a quality case and a
/// title case are simply two different assets, and the sixty rows a movie title pattern declines are
/// asserted in full against the shared video family.
/// </para>
/// <para>
/// <b>The quality column states a rendering rather than a rung, and nineteen rows changed with it.</b> A
/// rung name was chosen from a fixed list of thirty; a rendering is derived from what was actually read, so
/// where the two differ the row now states what the release says instead of the nearest row that existed.
/// Eight rows carry a raster or a bitstream claim a ladder had nowhere to put — 360, 540 and 960 lines, and
/// a disc bitstream copy below 1080. Seven drop a guess: a disc token with no stated resolution no longer
/// invents one, and a broadcast token with no stated resolution reads the same way whether it was spelled
/// with a separator or without, which is what the surveyed rows already said for every unspaced spelling.
/// Four render both standard-definition disc members with the community's single word for them.
/// </para>
/// </remarks>
public static class MoviesCorpus
{
    /// <summary>Builds the corpus.</summary>
    /// <returns>The cases, quality first and title second.</returns>
    public static IReadOnlyList<CorpusCase> Build() =>
        [.. QualityCases(), .. SeparatorCases(), .. TitleCases()];

    private static IReadOnlyList<CorpusCase> QualityCases() =>
    [
        Tier("q001", "Movie.Title.3.2017.720p.TSRip.x264.AAC-Ozlem", "TELESYNC"),
        Tier("q002", "Movie: Title (2024) TeleSynch 720p | HEVC-FILVOVAN", "TELESYNC"),
        Tier("q003", "Movie Name 2018 NEW PROPER 720p HD-CAM X264 HQ-CPG", "CAM"),
        Tier("q004", "Movie Name (2022) 1080p HQCAM ENG x264 AAC - QRips", "CAM"),
        Tier("q005", "Movie Name (2018) 720p Hindi HQ CAMrip x264 AAC 1.4GB", "CAM"),
        Tier("q006", "Movie Name (2022) New HDCAMRip 1080p [Love Rulz]", "CAM"),
        Tier("q007", "Movie.Name.2024.NEWCAM.1080p.HEVC.AC3.English-RypS", "CAM"),
        Tier("q008", "Movie Name S02E01 HDTV XviD 2HD", "SDTV"),
        Tier("q009", "Movie Name S05E11 PROPER HDTV XviD 2HD", "SDTV"),
        Tier("q010", "Movie.Name.2011.S02E01.WS.PDTV.x264-TLA", "SDTV"),
        Tier("q011", "Movie Name S01E04 DSR x264 2HD", "SDTV"),
        Tier("q012", "Movie Name S11E03 has no periods or extension HDTV", "SDTV"),
        Tier("q013", "Movie Name.S10E27.WS.DSR.XviD-2HD", "SDTV"),
        Tier("q014", "Movie Name.S03.TVRip.XviD-NOGRP", "SDTV"),
        Tier("q015", "Some.Movie.S03E06.DVDRip.XviD-WiDE", "DVD"),
        Tier("q016", "Some.Movie.S03E06.DVD.Rip.XviD-WiDE", "DVD"),
        Tier("q017", "the.Movie Name.1x13.circles.ws.xvidvd-tns", "DVD"),
        Tier("q018", "The.Third.Movie Name.2008.DVDRip.360p.H264 iPod -20-40", "DVD"),
        Tier("q019", "SomeMovie.2018.DVDRip.ts", "DVD"),
        Tier("q020", "Some.Movie.Magic.Rainbow.2007.DVD5.NTSC", "DVD"),
        Tier("q021", "Some.Movie.Magic.Rainbow.2007.DVD9.NTSC", "DVD"),
        Tier("q022", "Some.Movie.Magic.Rainbow.2007.DVDR.NTSC", "DVD"),
        Tier("q023", "Some.Movie.Magic.Rainbow.2007.DVD-R.NTSC", "DVD"),
        Tier("q024", "Some.Movie.2020.PAL.2xDVD9", "DVD"),
        Tier("q025", "Some.Movie.2000.2DVD5", "DVD"),
        Tier("q026", "Some.Movie.2005.PAL.MDVDR-SOMegRoUP", "DVD"),
        Tier("q027", "Movie.Name.S01E10.The.Leviathan.480p.WEB-DL.x264-mSD", "WEBDL-480p"),
        Tier("q028", "Movie.Name.S02E04.480p.WEB.DL.nSD.x264-NhaNc3", "WEBDL-480p"),
        Tier("q029", "[HorribleSubs] Movie Title! 2018 [Web][MKV][h264][480p][AAC 2.0][Softsubs (HorribleSubs)]", "WEBDL-480p"),
        Tier("q030", "[SubsPlease] Movie Title (540p) [AB649D32].mkv", "WEBDL-540p"),
        Tier("q031", "[Erai-raws] Movie Title [540p][Multiple Subtitle].mkv", "WEBDL-540p"),
        Tier("q032", "Movie.Name.1x04.ITA.WEBMux.x264-NovaRip", "WEBRip-480p"),
        Tier("q033", "Movie.Name (BD)(640x480(RAW) (BATCH 1) (1-13)", "Bluray-480p"),
        Tier("q034", "Movie.Name.S01E05.480p.BluRay.DD5.1.x264-HiSD", "Bluray-480p"),
        Tier("q035", "Movie.Name.S03E01-06.DUAL.BDRip.AC3.-HELLYWOOD", "Bluray"),
        Tier("q036", "Movie.Name.2011.LIMITED.BluRay.360p.H264-20-40", "Bluray-360p"),
        Tier("q037", "Movie.Name.2011.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR", "Remux-480p"),
        Tier("q038", "Movie.Name.2004.576p.BDRip.x264-HANDJOB", "Bluray-576p"),
        Tier("q039", "Movie.Title.S01E05.576p.BluRay.DD5.1.x264-HiSD", "Bluray-576p"),
        Tier("q040", "Movie Name - S01E01 - Title [HDTV-720p]", "HDTV-720p"),
        Tier("q041", "Movie.Name S04E87 REPACK 720p HDTV x264 aAF", "HDTV-720p"),
        Tier("q042", "Movie.Name - S22E03 - MoneyBART - HD TV.mkv", "SDTV"),
        Tier("q043", "Movie.Name.S08E05.720p.HDTV.X264-DIMENSION", "HDTV-720p"),
        Tier("q044", "Movie.Name.S01E08.Tourmaline.Nepal.720p.HDTV.x264-DHD", "HDTV-720p"),
        Tier("q045", "Movie.Name.US.S12E17.HR.WS.PDTV.X264-DIMENSION", "HDTV-720p"),
        Tier("q046", "Movie Name.S07E01.ARE.YOU.1080P.HDTV.X264-QCF", "HDTV-1080p"),
        Tier("q047", "Movie Name.S07E01.ARE.YOU.1080P.HDTV.proper.X264-QCF", "HDTV-1080p"),
        Tier("q048", "Movie Name - S01E01 - Title [HDTV-1080p]", "HDTV-1080p"),
        Tier("q049", "Movie.Name.2020.1080i.HDTV.DD5.1.H.264-NOGRP", "HDTV-1080p"),
        Tier("q050", "[NOGRP][国漫][诛仙][Movie Title 2022][19][HEVC][GB][4K]", "2160p"),
        Tier("q051", "Movie Name S01E04 Mexicos Death Train 720p WEB DL", "WEBDL-720p"),
        Tier("q052", "Movie Name S02E21 720p WEB DL DD5 1 H 264", "WEBDL-720p"),
        Tier("q053", "Movie Name - S11E06 - D-Yikes! - 720p WEB-DL.mkv", "WEBDL-720p"),
        Tier("q054", "Some.Movie.S02E15.720p.WEB-DL.DD5.1.H.264-SURFER", "WEBDL-720p"),
        Tier("q055", "Movie Name S04E22 720p WEB-DL DD5.1 H264-EbP.mkv", "WEBDL-720p"),
        Tier("q056", "Movie Name.S04E25.720p.iTunesHD.AVC-TVS", "WEBDL-720p"),
        Tier("q057", "Movie Name.S06E23.720p.WebHD.h264-euHD", "WEBDL-720p"),
        Tier("q058", "Movie Name.2016.03.14.720p.WEB.x264-spamTV", "WEBDL-720p"),
        Tier("q059", "Movie.Title.2013.960p.WEB-DL.AAC2.0.H.264-squalor", "WEBDL-960p"),
        Tier("q060", "Movie.Title.2021.DP.WEB.720p.DDP.5.1.H.264.PLEX", "WEBDL-720p"),
        Tier("q061", "Movie.Title.ITA.720p.WEBMux.x264-NovaRip", "WEBRip-720p"),
        Tier("q062", "Movie Name.S04E01.720p.WEBRip.AAC2.0.x264-NFRiP", "WEBRip-720p"),
        Tier("q063", "Movie Name S09E03 1080p WEB DL DD5 1 H264 NFHD", "WEBDL-1080p"),
        Tier("q064", "Movie.Name.S08E01.1080p.WEB-DL.proper.AAC2.0.H.264", "WEBDL-1080p"),
        Tier("q065", "Movie Name S10E03 1080p WEB DL DD5 1 H 264 REPACK NFHD", "WEBDL-1080p"),
        Tier("q066", "Movie.Name.Baby.S01E02.Night.2.[WEBDL-1080p].mkv", "WEBDL-1080p"),
        Tier("q067", "Movie.Name.2016.03.14.1080p.WEB.h264-spamTV", "WEBDL-1080p"),
        Tier("q068", "Series Title S06E08 1080p WEB h264-EXCLUSIVE", "WEBDL-1080p"),
        Tier("q069", "The.Movie.Name.2017.1080p.WEB-DL.DD5.1.H.264.Remux.-NTb", "WEBDL-1080p"),
        Tier("q070", "Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG", "WEBDL-1080p"),
        Tier("q071", "Movie.Name.2020.1080p.AMZN.WEB...", "WEBDL-1080p"),
        Tier("q072", "Movie Title - 2020 1080p Viva MKV WEB", "WEBDL-1080p"),
        Tier("q073", "Movie.Title.2020.MULTi.1080p.WEB.H264-ALLDAYiN (S:285/L:11)", "WEBDL-1080p"),
        Tier("q074", "Movie Title (2020) MULTi WEB 1080p x264-JiHEFF (S:317/L:28)", "WEBDL-1080p"),
        Tier("q075", "Movie.Titles.2020.1080p.NF.WEB.DD2.0.x264-SNEAkY", "WEBDL-1080p"),
        Tier("q076", "The.Movie.2022.NORDiC.1080p.DV.HDR.WEB.H 265-NiDHUG", "WEBDL-1080p"),
        Tier("q077", "Movie Title 2018 [WEB 1080p HEVC Opus] [Netaro]", "WEBDL-1080p"),
        Tier("q078", "Movie.Title.2024.German.Dubbed.DL.AAC.1080p.WEB.AVC-GROUP", "WEBDL-1080p"),
        Tier("q079", "Movie.Name.S04E01.iNTERNAL.1080p.WEBRip.x264-QRUS", "WEBRip-1080p"),
        Tier("q080", "Movie.Name.1x04.ITA.1080p.WEBMux.x264-NovaRip", "WEBRip-1080p"),
        Tier("q081", "Movie.Title.2019.1080p.AMZN.WEB-Rip.DDP.5.1.HEVC", "WEBRip-1080p"),
        Tier("q082", "Movie.Name.2016.03.14.2160p.WEB.x264-spamTV", "WEBDL-2160p"),
        Tier("q083", "Movie.Name.2016.03.14.2160p.WEB.PROPER.h264-spamTV", "WEBDL-2160p"),
        Tier("q084", "[HorribleSubs] Movie Title! 2018 [Web][MKV][h264][2160p][AAC 2.0][Softsubs (HorribleSubs)]", "WEBDL-2160p"),
        Tier("q085", "Movie Name 2020 WEB-DL 4K H265 10bit HDR DDP5.1 Atmos-PTerWEB", "WEBDL-2160p"),
        Tier("q086", "The.Movie.2022.NORDiC.2160p.DV.HDR.WEB.H.265-NiDHUG", "WEBDL-2160p"),
        Tier("q087", "Movie.Name.2024.German.Dubbed.DL.AAC.2160p.DV.HDR.WEB.HEVC-GROUP", "WEBDL-2160p"),
        Tier("q088", "Movie.Name.2024.German.AC3D.DL.2160p.Hybrid.WEB.DV.HDR10Plus.HEVC-GROUP", "WEBDL-2160p"),
        Tier("q089", "Movie Name S01E01.2160P AMZN WEBRIP DD2.0 HI10P X264-TROLLUHD", "WEBRip-2160p"),
        Tier("q090", "Movie.Name.S01E01.2160p.AMZN.WEBRip.DD2.0.Hi10p.X264-TrollUHD", "WEBRip-2160p"),
        Tier("q091", "Movie.Name.S03E01-06.DUAL.Bluray.AC3.-HELLYWOOD.avi", "Bluray"),
        Tier("q092", "Movie Name - S01E03 - Come Fly With Me - 720p BluRay.mkv", "Bluray-720p"),
        Tier("q093", "Movie.Name.S01E02.Chained.Heat.[Bluray720p].mkv", "Bluray-720p"),
        Tier("q094", "[FFF] Movie Name - 01 [BD][720p-AAC][0601BED4]", "Bluray-720p"),
        Tier("q095", "[coldhell] Movie v3 [BD720p][03192D4C]", "Bluray-720p"),
        Tier("q096", "Movie.Name.S03E01-06.DUAL.720p.Blu-ray.AC3.-HELLYWOOD.avi", "Bluray-720p"),
        Tier("q097", "Movie.Name.S01E01.33.720p.HDDVD.x264-SiNNERS.mkv", "Bluray-720p"),
        Tier("q098", "Movie.Name.S01E07.RERIP.720p.BluRay.x264-DEMAND", "Bluray-720p"),
        Tier("q099", "Movie.Name.2019.720p.MBLURAY.x264-MBLURAYFANS.mkv", "Bluray-720p"),
        Tier("q100", "Movie.Name.2.Parte.2.ITA-ENG.720p.BDMux.DD5.1.x264-DarkSideMux", "Bluray-720p"),
        Tier("q101", "Movie.Hunter.2018.720p.Blu-ray.Remux.AVC.FLAC.2.0-SiCFoI", "Remux-720p"),
        Tier("q102", "Movie.1993.720p.BluRay.REMUX.AVC.FLAC.2.0-BLURANiUM", "Remux-720p"),
        Tier("q103", "Movie Title - S01E03 - Come Fly With Me - 1080p BluRay.mkv", "Bluray-1080p"),
        Tier("q104", "Movie.Title.S02E13.1080p.BluRay.x264-AVCDVD", "Bluray-1080p"),
        Tier("q105", "Movie.S01E02.Chained.Heat.[Bluray1080p].mkv", "Bluray-1080p"),
        Tier("q106", "[coldhell] Movie v2 [BD1080p][5A45EABE].mkv", "Bluray-1080p"),
        Tier("q107", "[Coalgirls]_Movie!!_01_(1920x1080_Blu-ray_FLAC)_[8370CB8F].mkv", "Bluray-1080p"),
        Tier("q108", "Movie.Name.2019.1080p.MBLURAY.x264-MBLURAYFANS.mkv", "Bluray-1080p"),
        Tier("q109", "Movie Name 2005 1080p UHD BluRay DD+7.1 x264-LoRD.mkv", "Bluray-1080p"),
        Tier("q110", "Movie.Name.2016.German.DTS.DL.1080p.UHDBD.x265-TDO.mkv", "Bluray-1080p"),
        Tier("q111", "Movie.Name.2021.1080p.BDLight.x265-AVCDVD", "Bluray-1080p"),
        Tier("q112", "Movie.Title.2012.German.DL.1080p.UHD2BD.x264-QfG", "Bluray-1080p"),
        Tier("q113", "Movie.Title.2005.1080p.HDDVDRip.x264", "Bluray-1080p"),
        Tier("q114", "Movie.Title.2019.German.DL.1080p.HDR.UHDBDRip.AV1-GROUP", "Bluray-1080p"),
        Tier("q115", "Movie.Title.1993.Uncut.German.DL.1080p.HDR.UHDBDRip.h265-GROUP", "Bluray-1080p"),
        Tier("q116", "Movie.S01E02.Chained.Heat.[Bluray2160p].mkv", "Bluray-2160p"),
        Tier("q117", "[coldhell] Movie v2 [BD2160p][5A45EABE].mkv", "Bluray-2160p"),
        Tier("q118", "[Coalgirls]_Movie!!_01_(3840x2160_Blu-ray_FLAC)_[8370CB8F].mkv", "Bluray-2160p"),
        Tier("q119", "Movie.Title.2019.2160p.MBLURAY.x264-MBLURAYFANS.mkv", "Bluray-2160p"),
        Tier("q120", "Movie.Name.2020.German.UHDBD.2160p.HDR10.HEVC.EAC3.DL-pmHD.mkv", "Bluray-2160p"),
        Tier("q121", "Movie.Title.2014.2160p.UHD.BluRay.X265-IAMABLE.mkv", "Bluray-2160p"),
        Tier("q122", "Movie.Title.2014.2160p.BDRip.AAC.7.1.HDR10.x265.10bit-Markll", "Bluray-2160p"),
        Tier("q123", "Movie.Title.1956.German.DL.2160p.HDR.UHDBDRip.h266-GROUP", "Bluray-2160p"),
        Tier("q124", "Movie.Title.2021.4K.HDR.2160P.UHDBDRip.HEVC-10bit.GROUP", "Bluray-2160p"),
        Tier("q125", "Movie.Title.2016.REMUX.1080p.BluRay.AVC.DTS-HD.MA.5.1-iFT", "Remux-1080p"),
        Tier("q126", "Movie.Name.2008.REMUX.1080p.Bluray.AVC.DTS-HR.MA.5.1-LEGi0N", "Remux-1080p"),
        Tier("q127", "Movie.Name.2008.BDREMUX.1080p.Bluray.AVC.DTS-HR.MA.5.1-LEGi0N", "Remux-1080p"),
        Tier("q128", "Movie.Title.M.2008.USA.BluRay.Remux.1080p.MPEG-2.DD.5.1-TDD", "Remux-1080p"),
        Tier("q129", "Movie.Title.2018.1080p.BluRay.REMUX.MPEG-2.DTS-HD.MA.5.1-EPSiLON", "Remux-1080p"),
        Tier("q130", "Movie.Title.II.2003.4K.BluRay.Remux.1080p.AVC.DTS-HD.MA.5.1-BMF", "Remux-1080p"),
        Tier("q131", "Movie Title 2022 (BDRemux 1080p HEVC FLAC) [Netaro]", "Remux-1080p"),
        Tier("q132", "[Vodes] Movie Title - Other Title (2020) [BDRemux 1080p HEVC Dual-Audio]", "Remux-1080p"),
        Tier("q133", "This.Wonderful.Movie.1991.German.ML.1080p.BluRay.AVC-GeRMaNSCeNEGRoUP", "Remux-1080p"),
        Tier("q134", "Movie.Name.2011.1080p.DD.2.0.AVC.REMUX-FraMeSToR", "Remux-1080p"),
        Tier("q135", "Movie Name 2018 1080p BluRay Hybrid-REMUX AVC TRUEHD 5.1 Dual Audio-ZR-", "Remux-1080p"),
        Tier("q136", "Movie.Title.2016.REMUX.2160p.BluRay.AVC.DTS-HD.MA.5.1-iFT", "Remux-2160p"),
        Tier("q137", "Movie.Name.2008.REMUX.2160p.Bluray.AVC.DTS-HR.MA.5.1-LEGi0N", "Remux-2160p"),
        Tier("q138", "Movie.Title.1980.2160p.UHD.BluRay.Remux.HDR.HEVC.DTS-HD.MA.5.1-PmP.mkv", "Remux-2160p"),
        Tier("q139", "Movie.Title.2016.T1.UHDRemux.2160p.HEVC.Dual.AC3.5.1-TrueHD.5.1.Sub", "Remux-2160p"),
        Tier("q140", "Movie.Name.2020.German.UHDBD.2160p.HDR10.HEVC.EAC3.DL.Remux-pmHD.mkv", "Remux-2160p"),
        Tier("q141", "Movie Name (2021) [Remux-2160p x265 HDR 10-BIT DTS-HD MA 7.1]-FraMeSToR.mkv", "Remux-2160p"),
        Tier("q142", "This.Wonderful.Movie.1991.German.ML.2160p.BluRay.HEVC-GeRMaNSCeNEGRoUP", "Remux-2160p"),
        Tier("q143", "Movie.Name.2011.2160p.DD.2.0.AVC.REMUX-FraMeSToR", "Remux-2160p"),
        Tier("q144", "Movie.Name.2018.2160p.BluRay.Hybrid-REMUX.AVC.TRUEHD.5.1.Dual.Audio-ZR-", "Remux-2160p"),
        Tier("q145", "Movie.Title.2013.BDISO", "BR-DISK"),
        Tier("q146", "Movie.Title.2005.MULTi.COMPLETE.BLURAY-VLS", "BR-DISK"),
        Tier("q147", "Movie Name (2012) Bluray ISO [USENET-TURK]", "BR-DISK"),
        Tier("q148", "Movie Name.1993..BD25.ISO", "BR-DISK"),
        Tier("q149", "Movie.Title.2012.Bluray.1080p.3D.AVC.DTS-HD.MA.5.1.iso", "BR-DISK"),
        Tier("q150", "Movie.Title.1996.Bluray.ISO", "BR-DISK"),
        Tier("q151", "Random.Title.2010.1080p.HD.DVD.AVC.DDP.5.1-GRouP", "BR-DISK"),
        Tier("q152", "Movie Title 2005 1080p USA Blu-ray AVC DTS-HD MA 5.1-PTP", "BR-DISK"),
        Tier("q153", "Movie Title 1976 2160p UHD Blu-ray DTS-HD MA 5.1 DV HDR HEVC-UNTOUCHED", "BR-DISK"),
        Tier("q154", "BD25.Movie.Title.1994.1080p.DTS-HD", "BR-DISK"),
        Tier("q155", "Movie.Title.1997.1080p.NL.BD-50", "BR-DISK"),
        Tier("q156", "Movie Title 2009 3D BD 2009 UNTOUCHED", "BR-DISK"),
        Tier("q157", "Movie.Title.1982.1080p.HD.DVD.VC-1.DD+.5.1", "BR-DISK"),
        Tier("q158", "Movie.Title.2008.1080i.XXX.Blu-ray.MPEG-2.LPCM2.0.ISO", "BR-DISK"),
        Tier("q159", "Movie.Title.2008.BONUS.GERMAN.SUBBED.COMPLETE.BLURAY", "BR-DISK"),
        Tier("q160", "The German 2021 Bluray AVC", "BR-DISK"),
        Tier("q161", "Movie.Title.2008.US.Directors.Cut.UHD.BD66.Blu-ray", "BR-DISK"),
        Tier("q162", "Movie.2009.Blu.ray.AVC.DTS.HD.MA.5.1", "BR-DISK"),
        Tier("q163", "[BD]Movie.Title.2008.2023.1080p.COMPLETE.BLURAY-RlsGrp", "BR-DISK"),
        Tier("q164", "Movie.Title.2015.Open.Matte.1080i.HDTV.DD5.1.MPEG2", "Raw-HD"),
        Tier("q165", "Movie.Title.2009.1080i.HDTV.AAC2.0.MPEG2-PepelefuF", "Raw-HD"),
        Tier("q166", "Some.Movie.S02E15", "Unknown"),
        Tier("q167", "Movie Name - 11x11 - Quickie", "Unknown"),
        Tier("q168", "Movie.Title.S01E01.The.Web.MT-dd", "Unknown"),
    ];

    /// <summary>
    /// The generic spellings a user types into a profile, in each of the four separators a release
    /// name uses. The surveyed application runs the same cross-product, and it is the cheapest way to
    /// prove that separator normalization happens before the source scan rather than after it.
    /// </summary>
    private static IReadOnlyList<CorpusCase> SeparatorCases() =>
    [
        Tier("s001", "Some Movie 2018 SD-TV", "SDTV"),
        Tier("s002", "Some Movie 2018 SD.TV", "SDTV"),
        Tier("s003", "Some Movie 2018 SD TV", "SDTV"),
        Tier("s004", "Some Movie 2018 SD_TV", "SDTV"),
        Tier("s005", "Some Movie 2018 480p-WEB-DL", "WEBDL-480p"),
        Tier("s006", "Some Movie 2018 480p.WEB-DL", "WEBDL-480p"),
        Tier("s007", "Some Movie 2018 480p WEB-DL", "WEBDL-480p"),
        Tier("s008", "Some Movie 2018 480p_WEB-DL", "WEBDL-480p"),
        Tier("s009", "Some Movie 2018 HD-TV", "SDTV"),
        Tier("s010", "Some Movie 2018 HD.TV", "SDTV"),
        Tier("s011", "Some Movie 2018 HD TV", "SDTV"),
        Tier("s012", "Some Movie 2018 HD_TV", "SDTV"),
        Tier("s013", "Some Movie 2018 1080p-HD-TV", "HDTV-1080p"),
        Tier("s014", "Some Movie 2018 1080p.HD.TV", "HDTV-1080p"),
        Tier("s015", "Some Movie 2018 1080p HD TV", "HDTV-1080p"),
        Tier("s016", "Some Movie 2018 1080p_HD_TV", "HDTV-1080p"),
        Tier("s017", "Some Movie 2018 2160p-HD-TV", "HDTV-2160p"),
        Tier("s018", "Some Movie 2018 2160p.HD.TV", "HDTV-2160p"),
        Tier("s019", "Some Movie 2018 2160p HD TV", "HDTV-2160p"),
        Tier("s020", "Some Movie 2018 2160p_HD_TV", "HDTV-2160p"),
        Tier("s021", "Some Movie 2018 720p-WEB-DL", "WEBDL-720p"),
        Tier("s022", "Some Movie 2018 720p.WEB-DL", "WEBDL-720p"),
        Tier("s023", "Some Movie 2018 720p WEB-DL", "WEBDL-720p"),
        Tier("s024", "Some Movie 2018 720p_WEB-DL", "WEBDL-720p"),
        Tier("s025", "Some Movie 2018 1080p-WEB-DL", "WEBDL-1080p"),
        Tier("s026", "Some Movie 2018 1080p.WEB-DL", "WEBDL-1080p"),
        Tier("s027", "Some Movie 2018 1080p WEB-DL", "WEBDL-1080p"),
        Tier("s028", "Some Movie 2018 1080p_WEB-DL", "WEBDL-1080p"),
        Tier("s029", "Some Movie 2018 2160p-WEB-DL", "WEBDL-2160p"),
        Tier("s030", "Some Movie 2018 2160p.WEB-DL", "WEBDL-2160p"),
        Tier("s031", "Some Movie 2018 2160p WEB-DL", "WEBDL-2160p"),
        Tier("s032", "Some Movie 2018 2160p_WEB-DL", "WEBDL-2160p"),
        Tier("s033", "Some Movie 2018 720p-BluRay", "Bluray-720p"),
        Tier("s034", "Some Movie 2018 720p.BluRay", "Bluray-720p"),
        Tier("s035", "Some Movie 2018 720p BluRay", "Bluray-720p"),
        Tier("s036", "Some Movie 2018 720p_BluRay", "Bluray-720p"),
        Tier("s037", "Some Movie 2018 1080p-BluRay", "Bluray-1080p"),
        Tier("s038", "Some Movie 2018 1080p.BluRay", "Bluray-1080p"),
        Tier("s039", "Some Movie 2018 1080p BluRay", "Bluray-1080p"),
        Tier("s040", "Some Movie 2018 1080p_BluRay", "Bluray-1080p"),
        Tier("s041", "Some Movie 2018 2160p-BluRay", "Bluray-2160p"),
        Tier("s042", "Some Movie 2018 2160p.BluRay", "Bluray-2160p"),
        Tier("s043", "Some Movie 2018 2160p BluRay", "Bluray-2160p"),
        Tier("s044", "Some Movie 2018 2160p_BluRay", "Bluray-2160p"),
        Tier("s045", "Some Movie 2018 1080p-Remux", "Remux-1080p"),
        Tier("s046", "Some Movie 2018 1080p.Remux", "Remux-1080p"),
        Tier("s047", "Some Movie 2018 1080p Remux", "Remux-1080p"),
        Tier("s048", "Some Movie 2018 1080p_Remux", "Remux-1080p"),
        Tier("s049", "Some Movie 2018 2160p-Remux", "Remux-2160p"),
        Tier("s050", "Some Movie 2018 2160p.Remux", "Remux-2160p"),
        Tier("s051", "Some Movie 2018 2160p Remux", "Remux-2160p"),
        Tier("s052", "Some Movie 2018 2160p_Remux", "Remux-2160p"),
    ];

    private static IReadOnlyList<CorpusCase> TitleCases() =>
    [
        Title("t001", "The.Movie.from.U.N.C.L.E.2015.1080p.BluRay.x264-SPARKS", "The Movie from U.N.C.L.E."),
        Title("t002", "1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE", "1776"),
        Title("t003", "MY MOVIE (2016) [R][Action, Horror][720p.WEB-DL.AVC.8Bit.6ch.AC3].mkv", "MY MOVIE"),
        Title("t004", "R.I.P.D.2013.720p.BluRay.x264-SPARKS", "R.I.P.D."),
        Title("t005", "V.H.S.2.2013.LIMITED.720p.BluRay.x264-GECKOS", "V.H.S. 2"),
        Title("t006", "This Is A Movie (1999) [IMDB #] <Genre, Genre, Genre> {ACTORS} !DIRECTOR +MORE_SILLY_STUFF_NO_ONE_NEEDS ?", "This Is A Movie"),
        Title("t007", "We Are the Movie!.2013.720p.H264.mkv", "We Are the Movie!"),
        Title("t008", "(500).Days.Of.Movie.(2009).DTS.1080p.BluRay.x264.NLsubs", "(500) Days Of Movie"),
        Title("t009", "To.Live.and.Movie.in.L.A.1985.1080p.BluRay", "To Live and Movie in L.A."),
        Title("t010", "A.I.Artificial.Movie.(2001)", "A.I. Artificial Movie"),
        Title("t011", "A.Movie.Name.(1998)", "A Movie Name"),
        Title("t012", "www.Torrenting.com - Movie.2008.720p.X264-DIMENSION", "Movie"),
        Title("t013", "www.5MovieRulz.tc - Movie (2000) Malayalam HQ HDRip - x264 - AAC - 700MB.mkv", "Movie"),
        Title("t014", "Movie: The Movie World 2013", "Movie: The Movie World"),
        Title("t015", "Movie.The.Final.Chapter.2016", "Movie The Final Chapter"),
        Title("t016", "Der.Movie.James.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", "Der Movie James"),
        Title("t017", "Movie.German.DL.AC3.Dubbed..BluRay.x264-PsO", "Movie"),
        Title("t018", "Valana la Movie TRUEFRENCH BluRay 720p 2016 kjhlj", "Valana la Movie"),
        Title("t019", "Movie.Movie.2000.FRENCH..BluRay.-AiRLiNE", "Movie Movie"),
        Title("t020", "My Movie 1999 German Bluray", "My Movie"),
        Title("t021", "Leaving Movie by Movie (1897) [DVD].mp4", "Leaving Movie by Movie"),
        Title("t022", "Movie.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie"),
        Title("t023", "Movie.Title.Imax.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie Title"),
        Title("t024", "World.Movie.Z.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z"),
        Title("t025", "World.Movie.Z.2.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z 2"),
        Title("t026", "G.I.Movie.Movie.2013.THEATRiCAL.COMPLETE.BLURAY-GLiMMER", "G.I. Movie Movie"),
        Title("t027", "www.Torrenting.org - Movie.2008.720p.X264-DIMENSION", "Movie"),
        Title("t028", "The.French.Movie.2013.720p.BluRay.x264 - ROUGH[PublicHD]", "The French Movie"),
        Title("t029", "The.Good.German.2006.720p.BluRay.x264-RlsGrp", "The Good German"),
    ];

    private static CorpusCase Tier(string id, string input, string tierId) => new()
    {
        CaseId = id,
        Input = input,
        Source = MatchSource.ReleaseName,
        ExpectedQuality = tierId
    };

    private static CorpusCase Title(string id, string input, string title) => new()
    {
        CaseId = id,
        Input = input,
        Source = MatchSource.ReleaseName,
        ExpectedTitle = title
    };
}
