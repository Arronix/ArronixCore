using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitIgnoreAttribute = global::NUnit.Framework.IgnoreAttribute;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestCaseAttribute = global::NUnit.Framework.TestCaseAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;

namespace Arronix.Plugin.Movies.Tests.Catalog;

/// <summary>
/// Projecting movies as library items.
/// </summary>
/// <remarks>
/// <para>
/// There is no per-kind item source any more. Filtering, sorting, paging and text search derive from the
/// semantics each field declares — searchable, sortable, filterable, groupable — and the host item store
/// executes them for every kind. The declared field semantics these cases depend on are asserted in
/// <c>ItemProjectionTests</c>; the cases below query a store and need the engine.
/// </para>
/// <para>
/// The corpus is preserved row for row and marked ignored rather than deleted so the gap is visible.
/// </para>
/// </remarks>
[NUnitTestFixtureAttribute]
public class ItemSourceTests
{
    static ItemSourceTests()
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

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProjectsExactlyTheDeclaredFieldSet()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProjectsEveryValueWithTheDeclaredKind()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProjectsTheTitleAndTheReferenceOnEveryItem()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReportsNoChildrenAndNoParentOnEveryItem()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void StampsTheSingleCoordinateOnEveryItem()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void NamesTheOnlyFormatFamilyOnEveryItem()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PublishesTheCatalogIdentifiersOnEveryItem()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReportsATotalCountThatSurvivesPaging()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PagesWithoutRepeatingOrDroppingAnItem()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReturnsNothingForAPageBeyondTheEnd()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("title")]
    [NUnitTestCaseAttribute("year")]
    [NUnitTestCaseAttribute("runtime")]
    [NUnitTestCaseAttribute("tmdbRating")]
    [NUnitTestCaseAttribute("releaseDate")]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SortsIntoATotalOrder(string arg0)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SortsAnAbsentDateLastWhenAscending()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SearchesTheFieldsDeclaredSearchable()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FindsAFilmByAnAlternativeTitleInFreeText()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReturnsNothingForATextSearchThatMatchesNothing()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesAnItemByReference()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesNothingForAReferenceTheCatalogDoesNotHold()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("tmdb", "238", "The Godfather")]
    [NUnitTestCaseAttribute("imdb", "tt0068646", "The Godfather")]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesAnItemByExternalIdentifier(string arg0, string arg1, string arg2)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesNothingForAnIdentifierSchemeThisKindDoesNotIssue()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");
}
