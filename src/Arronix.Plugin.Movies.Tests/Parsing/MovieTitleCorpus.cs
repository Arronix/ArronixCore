using System.Linq;
using NUnitTestCaseData = global::NUnit.Framework.TestCaseData;

namespace Arronix.Plugin.Movies.Tests.Parsing;

/// <summary>Representative movie release names and their expected parsed titles.</summary>
internal static class MovieTitleCorpus
{
    internal static IReadOnlyList<MovieTitleCase> Cases { get; } =
    [
        new("t001", "The.Movie.from.U.N.C.L.E.2015.1080p.BluRay.x264-SPARKS", "The Movie from U.N.C.L.E."),
        new("t002", "1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE", "1776"),
        new("t003", "MY MOVIE (2016) [R][Action, Horror][720p.WEB-DL.AVC.8Bit.6ch.AC3].mkv", "MY MOVIE"),
        new("t004", "R.I.P.D.2013.720p.BluRay.x264-SPARKS", "R.I.P.D."),
        new("t005", "V.H.S.2.2013.LIMITED.720p.BluRay.x264-GECKOS", "V.H.S. 2"),
        new("t006", "This Is A Movie (1999) [IMDB #] <Genre, Genre, Genre> {ACTORS} !DIRECTOR +MORE_SILLY_STUFF_NO_ONE_NEEDS ?", "This Is A Movie"),
        new("t007", "We Are the Movie!.2013.720p.H264.mkv", "We Are the Movie!"),
        new("t008", "(500).Days.Of.Movie.(2009).DTS.1080p.BluRay.x264.NLsubs", "(500) Days Of Movie"),
        new("t009", "To.Live.and.Movie.in.L.A.1985.1080p.BluRay", "To Live and Movie in L.A."),
        new("t010", "A.I.Artificial.Movie.(2001)", "A.I. Artificial Movie"),
        new("t011", "A.Movie.Name.(1998)", "A Movie Name"),
        new("t014", "Movie: The Movie World 2013", "Movie: The Movie World"),
        new("t015", "Movie.The.Final.Chapter.2016", "Movie The Final Chapter"),
        new("t016", "Der.Movie.James.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", "Der Movie James"),
        new("t017", "Movie.German.DL.AC3.Dubbed..BluRay.x264-PsO", "Movie"),
        new("t018", "Valana la Movie TRUEFRENCH BluRay 720p 2016 kjhlj", "Valana la Movie"),
        new("t019", "Movie.Movie.2000.FRENCH..BluRay.-AiRLiNE", "Movie Movie"),
        new("t020", "My Movie 1999 German Bluray", "My Movie"),
        new("t021", "Leaving Movie by Movie (1897) [DVD].mp4", "Leaving Movie by Movie"),
        new("t022", "Movie.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie"),
        new("t024", "World.Movie.Z.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z"),
        new("t025", "World.Movie.Z.2.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z 2"),
        new("t026", "G.I.Movie.Movie.2013.THEATRiCAL.COMPLETE.BLURAY-GLiMMER", "G.I. Movie Movie"),
        new("t028", "The.French.Movie.2013.720p.BluRay.x264 - ROUGH[PublicHD]", "The French Movie"),
        new("t029", "The.Good.German.2006.720p.BluRay.x264-RlsGrp", "The Good German")
    ];

    internal static IEnumerable<TestCaseData> TestCases =>
        Cases.Select(static test =>
            new NUnitTestCaseData(test.Input, test.ExpectedTitle));
}

/// <summary>One movie title parsing regression case.</summary>
/// <param name="Id">The stable case identifier.</param>
/// <param name="Input">The release name to parse.</param>
/// <param name="ExpectedTitle">The expected movie title.</param>
internal sealed record MovieTitleCase(string Id, string Input, string ExpectedTitle);
