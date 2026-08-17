using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Plugin.Movies.Definition;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Parsing;

/// <summary>
/// Edition detection, ported from Radarr's <c>ParserTests/EditionParserFixture</c>.
/// </summary>
/// <remarks>
/// <para>
/// An edition is the one movie concept with no home on a level's field list: it belongs to a file rather
/// than to an item. The declaration gives it two homes and both are data — the video family's
/// <c>edition</c> technical facet, which is where the naming token derives from, and the declared
/// alternation below, which three title patterns and one guard share by identifier.
/// </para>
/// <para>
/// The positive cases run the declared expression itself, which is exactly the asset being converted. The
/// negative half — an edition word that belongs to the film's own title — is decided by the ordered
/// pattern list rather than by the expression, so it needs the parse engine and is marked ignored rather
/// than weakened.
/// </para>
/// </remarks>
[TestFixture]
public class EditionParserTests
{
    [TestCase("Movie Title 2012 Directors Cut", "Directors Cut")]
    [TestCase("Movie Title 1999 (Despecialized).mkv", "Despecialized")]
    [TestCase("Movie Title.2012.(Special.Edition.Remastered).[Bluray-1080p].mkv", "Special Edition Remastered")]
    [TestCase("Movie Title 2012 Extended", "Extended")]
    [TestCase("Movie Title 2012 Extended Directors Cut Fan Edit", "Extended Directors Cut Fan Edit")]
    [TestCase("Movie Title 2012 Director's Cut", "Director's Cut")]
    [TestCase("Movie Title.2012.(Extended.Theatrical.Version.IMAX).BluRay.1080p.2012.asdf",
        "Extended Theatrical Version IMAX")]
    [TestCase("2021 A Movie (1968) Director's Cut .mkv", "Director's Cut")]
    [TestCase("2021 A Movie 1968 (Extended Directors Cut FanEdit)", "Extended Directors Cut FanEdit")]
    [TestCase("A Fake Movie 2035 2012 Directors.mkv", "Directors")]
    [TestCase("Movie 2049 Director's Cut.mkv", "Director's Cut")]
    [TestCase("Movie Title 2012 50th Anniversary Edition.mkv", "50th Anniversary Edition")]
    [TestCase("Movie 2012 2in1.mkv", "2in1")]
    [TestCase("Movie 2012 IMAX.mkv", "IMAX")]
    [TestCase("Movie 2012 Restored.mkv", "Restored")]
    [TestCase("Movie Title.Special.Edition.Fan Edit.2012..BRRip.x264.AAC-m2g", "Special Edition Fan Edit")]
    [TestCase("Movie Title (Despecialized) 1999.mkv", "Despecialized")]
    [TestCase("Movie Title.(Special.Edition.Remastered).2012.[Bluray-1080p].mkv", "Special Edition Remastered")]
    [TestCase("Movie Title Extended 2012", "Extended")]
    [TestCase("Movie Title Extended Directors Cut Fan Edit 2012", "Extended Directors Cut Fan Edit")]
    [TestCase("Movie Title Director's Cut 2012", "Director's Cut")]
    [TestCase("Movie Title Directors Cut 2012", "Directors Cut")]
    [TestCase("Movie Title.(Extended.Theatrical.Version.IMAX).2012.BluRay.1080p.asdf",
        "Extended Theatrical Version IMAX")]
    [TestCase("Movie Director's Cut (1968).mkv", "Director's Cut")]
    [TestCase("2021 A Movie (Extended Directors Cut FanEdit) 1968 Bluray 1080p", "Extended Directors Cut FanEdit")]
    [TestCase("A Fake Movie 2035 Directors 2012.mkv", "Directors")]
    [TestCase("Movie Director's Cut 2049.mkv", "Director's Cut")]
    [TestCase("Movie Title 50th Anniversary Edition 2012.mkv", "50th Anniversary Edition")]
    [TestCase("Movie 2in1 2012.mkv", "2in1")]
    [TestCase("Movie IMAX 2012.mkv", "IMAX")]
    [TestCase("Fake Movie Final Cut 2016", "Final Cut")]
    [TestCase("Fake Movie 2016 Final Cut ", "Final Cut")]
    [TestCase("My Movie GERMAN Extended Cut 2016", "Extended Cut")]
    [TestCase("My.Movie.GERMAN.Extended.Cut.2016", "Extended Cut")]
    [TestCase("My.Movie.GERMAN.Extended.Cut", "Extended Cut")]
    [TestCase("My.Movie.Assembly.Cut.1992.REPACK.1080p.BluRay.DD5.1.x264-Group", "Assembly Cut")]
    [TestCase("Movie.1987.Ultimate.Hunter.Edition.DTS-HD.DTS.MULTISUBS.1080p.BluRay.x264.HQ-TUSAHD",
        "Ultimate Hunter Edition")]
    [TestCase("Movie.1950.Diamond.Edition.1080p.BluRay.x264-nikt0", "Diamond Edition")]
    [TestCase("Movie.Title.1990.Ultimate.Rekall.Edition.NORDiC.REMUX.1080p.BluRay.AVC.DTS-HD.MA5.1-TWA",
        "Ultimate Rekall Edition")]
    [TestCase("Movie.Title.1971.Signature.Edition.1080p.BluRay.FLAC.2.0.x264-TDD", "Signature Edition")]
    [TestCase("Movie.1979.The.Imperial.Edition.BluRay.720p.DTS.x264-CtrlHD", "Imperial Edition")]
    [TestCase("Movie.1997.Open.Matte.1080p.BluRay.x264.DTS-FGT", "Open Matte")]
    public void ReadsTheEdition(string releaseTitle, string expected)
    {
        var match = Edition.Match(releaseTitle);

        Assert.Multiple(() =>
        {
            Assert.That(match.Success, Is.True, releaseTitle);
            Assert.That(
                match.Groups["edition"].Value.Replace(".", " ", StringComparison.Ordinal).Trim(),
                Is.EqualTo(expected),
                releaseTitle);
        });
    }

    /// <summary>
    /// The negative half, and the one that matters: an edition word inside a <i>title</i> is not an
    /// edition. Every case here is a film whose name contains one.
    /// </summary>
    [TestCase("Movie.Holiday.Special.1978.DVD.REMUX.DD.2.0-ViETNAM")]
    [TestCase("Directors.Cut.German.2006.COMPLETE.PAL.DVDR-LoD")]
    [TestCase("Movie Impossible: Rogue Movie 2012 Bluray")]
    [TestCase("Loving.Movie.2018.TS.FRENCH.MD.x264-DROGUERiE")]
    [TestCase("Uncut.Movie.2019.720p.BluRay.x264-YOL0W")]
    [TestCase("The.Christmas.Edition.1941.720p.HDTV.x264-CRiMSON")]
    public void ReadsNoEditionWhenTheWordBelongsToTheTitle(string releaseTitle)
        => Assert.That(
            MoviesEngines.Parse(releaseTitle)?.AdditionalMetadata,
            Is.Null.Or.Not.ContainKey("parse.tag." + MoviesReleaseTags.Edition),
            releaseTitle);

    /// <summary>
    /// The edition survives the projection onto the stable DTO — as a bag entry, because the DTO has no
    /// member for it.
    /// </summary>
    [Test]
    public void BindsTheEditionCaptureOnEveryPatternThatCarriesOne()
    {
        foreach (var patternId in new[] { "german-truefrench-no-year", "edition-then-year" })
        {
            var pattern = MoviesDeclaration.Pattern(patternId);

            Assert.That(
                pattern.Captures.Any(capture =>
                    capture.GroupName == "edition"
                    && capture.Key == MoviesReleaseTags.Edition),
                Is.True,
                patternId);
        }
    }

    [Test]
    public void DeclaresTheEditionAlternationOnceAndSharesItByIdentifier()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Guard("edition").Regex, Is.EqualTo(MoviesParsing.EditionRegex));
            Assert.That(
                MoviesDeclaration.Pattern("edition-then-year").Regex,
                Does.Contain(MoviesParsing.EditionRegex),
                "The pattern embeds the same alternation, so the two cannot drift apart.");
        });

    [Test]
    public void DeclaresTheEditionAsATechnicalFacetOfTheVideoFamily()
    {
        var facet = MoviesDeclaration.Video.TechnicalFacets.Single();

        Assert.Multiple(() =>
        {
            Assert.That(facet.FacetId, Is.EqualTo("edition"));
            Assert.That(facet.CaseExceptions, Does.Contain("IMAX").And.Contain("3D"));
            Assert.That(facet.OrdinalSuffixesLowerCase, Is.True, "\"25th\", not \"25Th\".");
        });
    }

    private static readonly Regex Edition = new(
        MoviesParsing.EditionRegex,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The stable release DTO has no member for an edition, so the parse publishes it as a bag entry under
    /// the tag key the declaration's capture names.
    /// </summary>
    /// <remarks>
    /// The subject is an edition-before-year layout deliberately. Only <c>edition-then-year</c> and
    /// <c>german-truefrench-no-year</c> bind the edition capture, so a release that states its edition
    /// after the year is read by <c>title-then-year</c> and carries no edition tag. That is the ordered
    /// pattern list working as declared, and it is narrower than the surveyed application, which runs its
    /// edition expression over the whole title whatever the position.
    /// </remarks>
    [Test]
    public void PublishesTheEditionOnTheParsedRelease()
    {
        var parsed = MoviesEngines.Parse("Movie Title Directors Cut 2012");

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null);
            Assert.That(
                parsed!.AdditionalMetadata,
                Does.ContainKey("parse.tag." + MoviesReleaseTags.Edition));
        });
    }
}
