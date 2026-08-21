using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitIgnoreAttribute = global::NUnit.Framework.IgnoreAttribute;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestCaseAttribute = global::NUnit.Framework.TestCaseAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;

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
[NUnitTestFixtureAttribute]
public class RenamePolicyTests
{
    static RenamePolicyTests()
    {
        if (typeof(NUnitAssert).Assembly.GetName().Name != "nunit.framework")
        {
            throw new InvalidOperationException("The compatibility fixture did not bind the real NUnit assertion assembly.");
        }
    }

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void DeclaresTheMediaKindItAnswersFor()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("{Movie Title}", "The Godfather")]
    [NUnitTestCaseAttribute("{Movie Title} ({Movie Release Year})", "The Godfather (1972)")]
    [NUnitTestCaseAttribute("{Movie CleanTitle}", "The Godfather")]
    [NUnitTestCaseAttribute("{Movie TitleThe}", "Godfather, The")]
    [NUnitTestCaseAttribute("{Movie CleanTitleThe} ({Movie Release Year})", "Godfather, The (1972)")]
    [NUnitTestCaseAttribute("{Movie TitleFirstCharacter}", "G")]
    [NUnitTestCaseAttribute("{Movie Certification}", "R")]
    [NUnitTestCaseAttribute("{Movie Collection}", "The Godfather Collection")]
    [NUnitTestCaseAttribute("{Movie CollectionThe}", "Godfather Collection, The")]
    [NUnitTestCaseAttribute("{Movie TmdbId}", "238")]
    [NUnitTestCaseAttribute("{Movie ImdbId}", "tt0068646")]
    [NUnitTestCaseAttribute("{Movie Title} {tmdb-{Movie TmdbId}}", "The Godfather {tmdb-238}")]
    [NUnitTestCaseAttribute("{Movie Title} {imdb-{Movie ImdbId}}", "The Godfather {imdb-tt0068646}")]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheMovieOnlyVocabulary(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("{Movie Title}", "The Godfather")]
    [NUnitTestCaseAttribute("{Movie.Title}", "The.Godfather")]
    [NUnitTestCaseAttribute("{Movie_Title}", "The_Godfather")]
    [NUnitTestCaseAttribute("{Movie-Title}", "The-Godfather")]
    [NUnitTestCaseAttribute("{MOVIE TITLE}", "THE GODFATHER")]
    [NUnitTestCaseAttribute("{movie title}", "the godfather")]
    [NUnitTestCaseAttribute("{MOVIE.TITLE}", "THE.GODFATHER")]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesTheTokensOwnSpellingIntoItsValue(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheDefaultTemplate()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("{Quality Title}", "Bluray-1080p")]
    [NUnitTestCaseAttribute("{Quality Full}", "Bluray-1080p")]
    [NUnitTestCaseAttribute("{Release Group}", "RlsGrp")]
    [NUnitTestCaseAttribute("{Movie Title} [{Quality Title}]", "The Godfather [Bluray-1080p]")]
    [NUnitTestCaseAttribute("{Movie.Title}.{_Quality.Title_}", "The.Godfather._Bluray-1080p")]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheFileVocabularyWhenAFileIsSupplied(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersARevisionIntoTheQualityTokens()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ElidesAWrappedTokenThatResolvesToNothing()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersAnEditionTagWithItsWrapperAndItsOwnCasing()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersTheProbeVocabulary()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void OmitsAnEnglishOnlyAudioLanguageList()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AssumesEightBitWhenTheProbeReportedNoDepth()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RendersOnlyTheMovieSubsetThroughTheContractMethod()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesNoTokensAtAllForAnItemTheCatalogDoesNotHold()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FallsBackToTheOriginalNameRatherThanWritingAnEmptyOne()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void TruncatesToTheConfiguredComponentBudget()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RejectsABlankTemplate()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("{Movie Title} ({Movie Release Year})", true)]
    [NUnitTestCaseAttribute("{Movie CleanTitleThe} ({Movie Release Year}) {Quality Full}", true)]
    [NUnitTestCaseAttribute("{Movie OriginalTitle} ({Movie Release Year})", true)]
    [NUnitTestCaseAttribute("{Original Title}", true)]
    [NUnitTestCaseAttribute("{Original Filename}", true)]
    [NUnitTestCaseAttribute("{Movie Title}", false)]
    [NUnitTestCaseAttribute("{Movie Release Year}", false)]
    [NUnitTestCaseAttribute("{Quality Full}", false)]
    [NUnitTestCaseAttribute("", false)]
    [NUnitTestCaseAttribute("   ", false)]
    [NUnitTestCaseAttribute("{Not A Token}", false)]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ValidatesAFileTemplate(string arg0, bool arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("{Movie Title} ({Movie Release Year})", true)]
    [NUnitTestCaseAttribute("{Movie TitleThe} ({Movie Release Year}) {tmdb-{Movie TmdbId}}", true)]
    [NUnitTestCaseAttribute("{Movie Title}", true)]
    [NUnitTestCaseAttribute("{Movie Title} {Quality Full}", false)]
    [NUnitTestCaseAttribute("{Movie Title} {Release Group}", false)]
    [NUnitTestCaseAttribute("{Movie Title} {Edition Tags}", false)]
    [NUnitTestCaseAttribute("{Movie Title} {MediaInfo Simple}", false)]
    [NUnitTestCaseAttribute("{Original Title}", false)]
    [NUnitTestCaseAttribute("{Movie Release Year}", false)]
    [NUnitTestCaseAttribute("", false)]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ValidatesAFolderTemplate(string arg0, bool arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AcceptsItsOwnDefaultsUnderItsOwnValidation()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProhibitsExactlyTheTokensAFolderCannotResolve()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FindsEveryTokenInATemplate()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");
}
