using System.Linq;
using NUnitTestCaseData = global::NUnit.Framework.TestCaseData;

namespace Arronix.Plugin.Movies.Tests.Parsing;

/// <summary>Immutable movie-title inputs whose current outcomes are tracked by the compatibility ledger.</summary>
internal static class MovieTitleCompatibilityCorpus
{
    internal static IReadOnlyList<MovieTitleCase> Cases { get; } =
    [
        new("t012", "www.Torrenting.com - Movie.2008.720p.X264-DIMENSION", "Movie"),
        new("t013", "www.5MovieRulz.tc - Movie (2000) Malayalam HQ HDRip - x264 - AAC - 700MB.mkv", "Movie"),
        new("t023", "Movie.Title.Imax.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie Title"),
        new("t027", "www.Torrenting.org - Movie.2008.720p.X264-DIMENSION", "Movie")
    ];

    internal static IEnumerable<TestCaseData> TestCases =>
        Cases.Select(static test =>
            new NUnitTestCaseData(test.Input, test.ExpectedTitle));
}
