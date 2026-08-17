namespace Arronix.Plugin.Movies.Tests.Naming;

/// <summary>
/// Rendering a movie's file and folder names.
/// </summary>
/// <remarks>
/// <para>
/// Rendering is the host naming engine now, driven by the declaration's four templates, one condition row,
/// folder spine and two token fallbacks. Those declared rows are asserted in <c>NamingDeclarationTests</c>;
/// the cases below render against a catalog and a probed file, and need the engine.
/// </para>
/// <para>
/// The corpus is preserved row for row and marked ignored rather than deleted so the gap is visible.
/// </para>
/// </remarks>
[TestFixture]
public class RenamePolicyTests
{
    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void DeclaresTheMediaKindItAnswersFor()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("{Movie Title}", "The Godfather")]
    [TestCase("{Movie Title} ({Movie Release Year})", "The Godfather (1972)")]
    [TestCase("{Movie CleanTitle}", "The Godfather")]
    [TestCase("{Movie TitleThe}", "Godfather, The")]
    [TestCase("{Movie CleanTitleThe} ({Movie Release Year})", "Godfather, The (1972)")]
    [TestCase("{Movie TitleFirstCharacter}", "G")]
    [TestCase("{Movie Certification}", "R")]
    [TestCase("{Movie Collection}", "The Godfather Collection")]
    [TestCase("{Movie CollectionThe}", "Godfather Collection, The")]
    [TestCase("{Movie TmdbId}", "238")]
    [TestCase("{Movie ImdbId}", "tt0068646")]
    [TestCase("{Movie Title} {tmdb-{Movie TmdbId}}", "The Godfather {tmdb-238}")]
    [TestCase("{Movie Title} {imdb-{Movie ImdbId}}", "The Godfather {imdb-tt0068646}")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheMovieOnlyVocabulary(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("{Movie Title}", "The Godfather")]
    [TestCase("{Movie.Title}", "The.Godfather")]
    [TestCase("{Movie_Title}", "The_Godfather")]
    [TestCase("{Movie-Title}", "The-Godfather")]
    [TestCase("{MOVIE TITLE}", "THE GODFATHER")]
    [TestCase("{movie title}", "the godfather")]
    [TestCase("{MOVIE.TITLE}", "THE.GODFATHER")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesTheTokensOwnSpellingIntoItsValue(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheDefaultTemplate()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("{Quality Title}", "Bluray-1080p")]
    [TestCase("{Quality Full}", "Bluray-1080p")]
    [TestCase("{Release Group}", "RlsGrp")]
    [TestCase("{Movie Title} [{Quality Title}]", "The Godfather [Bluray-1080p]")]
    [TestCase("{Movie.Title}.{_Quality.Title_}", "The.Godfather._Bluray-1080p")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheFileVocabularyWhenAFileIsSupplied(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersARevisionIntoTheQualityTokens()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ElidesAWrappedTokenThatResolvesToNothing()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersAnEditionTagWithItsWrapperAndItsOwnCasing()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheProbeVocabulary()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void OmitsAnEnglishOnlyAudioLanguageList()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AssumesEightBitWhenTheProbeReportedNoDepth()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersOnlyTheMovieSubsetThroughTheContractMethod()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesNoTokensAtAllForAnItemTheCatalogDoesNotHold()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FallsBackToTheOriginalNameRatherThanWritingAnEmptyOne()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void TruncatesToTheConfiguredComponentBudget()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RejectsABlankTemplate()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("{Movie Title} ({Movie Release Year})", true)]
    [TestCase("{Movie CleanTitleThe} ({Movie Release Year}) {Quality Full}", true)]
    [TestCase("{Movie OriginalTitle} ({Movie Release Year})", true)]
    [TestCase("{Original Title}", true)]
    [TestCase("{Original Filename}", true)]
    [TestCase("{Movie Title}", false)]
    [TestCase("{Movie Release Year}", false)]
    [TestCase("{Quality Full}", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("{Not A Token}", false)]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ValidatesAFileTemplate(string arg0, bool arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("{Movie Title} ({Movie Release Year})", true)]
    [TestCase("{Movie TitleThe} ({Movie Release Year}) {tmdb-{Movie TmdbId}}", true)]
    [TestCase("{Movie Title}", true)]
    [TestCase("{Movie Title} {Quality Full}", false)]
    [TestCase("{Movie Title} {Release Group}", false)]
    [TestCase("{Movie Title} {Edition Tags}", false)]
    [TestCase("{Movie Title} {MediaInfo Simple}", false)]
    [TestCase("{Original Title}", false)]
    [TestCase("{Movie Release Year}", false)]
    [TestCase("", false)]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ValidatesAFolderTemplate(string arg0, bool arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AcceptsItsOwnDefaultsUnderItsOwnValidation()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProhibitsExactlyTheTokensAFolderCannotResolve()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FindsEveryTokenInATemplate()
        => Assert.Fail("Unreachable: see the fixture remarks.");
}
