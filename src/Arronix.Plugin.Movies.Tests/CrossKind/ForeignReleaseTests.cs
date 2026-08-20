
using System.Linq;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.CrossKind;

/// <summary>
/// What keeps one kind's releases out of another kind's library.
/// </summary>
/// <remarks>
/// <para>
/// A media type declares semantic searches and format use. A source plugin owns any wire-protocol category
/// mapping because the same movie search can be implemented by Newznab, Torznab or a catalog API without
/// changing what a movie is.
/// </para>
/// <para>
/// The cases that read a foreign release's text and watch the matcher refuse it need the parse and match
/// engines, which are internal to <c>Arronix.Host</c>. They are marked ignored rather than deleted.
/// </para>
/// </remarks>
[TestFixture]
public class ForeignReleaseTests
{
    [TestCase("The.Office.US.S03E15.720p.HDTV.x264-DIMENSION")]
    [TestCase("Breaking.Bad.S05E14.1080p.BluRay.x264-DEMAND")]
    [TestCase("Game.of.Thrones.S01E01.HDTV.XviD-2HD")]
    [TestCase("The.Wire.S01.1080p.BluRay.x264-GROUP")]
    [TestCase("The Simpsons S28E21 1080p WEB x264-TBS")]
    [TestCase("Top.Gear.30x01.1080p.HDTV.x264-ORGANiC")]
    [TestCase("Some.Show.S02.COMPLETE.1080p.WEB-DL.DDP5.1.H.264-GROUP")]
    [TestCase("Big Movie (S01E18) Complete 360p HDTV AAC H.264-NEXT")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RefusesATelevisionReleaseThatCarriesCoordinates(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("Doctor.Who.2005.S09E01.720p.HDTV.x264-FoV", "Doctor Who")]
    [TestCase("Some.Show.2018.S02E03.1080p.WEB-DL.DD5.1.H264-GROUP", "Some Show")]
    [TestCase("Another.Show.2018.S02E03E04.1080p.WEB-DL-GROUP", "Another Show")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReadsATelevisionReleaseThatCarriesAYearAndLeavesItToTheCatalogToRefuse(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("Pink_Floyd-The_Wall-2CD-FLAC-1979-LoKET")]
    [TestCase("VA-Now_Thats_What_I_Call_Music_100-2CD-2018-MTD")]
    [TestCase("Radiohead - OK Computer (1997) [FLAC]")]
    [TestCase("Daft Punk - Discovery (2001) [24bit-96kHz]")]
    [TestCase("Adele-30-WEB-2021-ENRiCH")]
    [TestCase("Miles Davis - Kind of Blue (1959) [SACD]")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReadsAMusicReleaseAndLeavesItToTheCatalogToRefuse(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("Brandon Sanderson - The Way of Kings (2010) (epub)")]
    [TestCase("Stephen.King.The.Shining.1977.Retail.EPUB.eBook-BitBook")]
    [TestCase("Iain M Banks - Consider Phlebas (1987) [mobi]")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReadsABookReleaseAndLeavesItToTheCatalogToRefuse(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RefusesABookOfAFilmTheCatalogHoldsBecauseTheYearDisagrees()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void NeverReadsAMediaKindOutOfTheReleaseText()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    public void DeclaresNoSourceProtocolCategories()
        => Assert.That(
            MoviesDeclaration.Shape.SearchKinds.SelectMany(static kind => kind.Categories),
            Is.Empty,
            "provider-specific category mappings belong to the source plugin");

    /// <summary>
    /// A format family's extension set is the discriminator the shape gate enforces across kinds, and a
    /// movie claims nothing a music or book library would put on disk.
    /// </summary>
    [TestCase(".flac")]
    [TestCase(".mp3")]
    [TestCase(".m4a")]
    [TestCase(".opus")]
    [TestCase(".wav")]
    [TestCase(".epub")]
    [TestCase(".mobi")]
    [TestCase(".azw3")]
    [TestCase(".pdf")]
    [TestCase(".cbz")]
    public void ClaimsNoExtensionAnotherKindWouldClaim(string extension)
        => Assert.That(
            MoviesDeclaration.Shape.FormatFamilies.SelectMany(static family => family.FileExtensions),
            Does.Not.Contain(extension));

    /// <summary>
    /// The one genuinely shared extension. A television library and a movie library both hold
    /// <c>.mkv</c> files, so eligibility requires the typed target/search context rather than pretending a
    /// source protocol number is intrinsic movie vocabulary.
    /// </summary>
    [Test]
    public void SharesItsContainerExtensionsWithEveryOtherVideoKind()
        => Assert.That(
            MoviesDeclaration.Video.FileExtensions,
            Does.Contain(".mkv").And.Contain(".mp4"),
            "A television episode is an .mkv too; the typed target tells the host which search is running.");
}
