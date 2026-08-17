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
[TestFixture]
public class ItemSourceTests
{
    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void DeclaresTheMediaKindItAnswersFor()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProjectsExactlyTheDeclaredFieldSet()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProjectsEveryValueWithTheDeclaredKind()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ProjectsTheTitleAndTheReferenceOnEveryItem()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReportsNoChildrenAndNoParentOnEveryItem()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void StampsTheSingleCoordinateOnEveryItem()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void NamesTheOnlyFormatFamilyOnEveryItem()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PublishesTheCatalogIdentifiersOnEveryItem()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReportsATotalCountThatSurvivesPaging()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PagesWithoutRepeatingOrDroppingAnItem()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReturnsNothingForAPageBeyondTheEnd()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("title")]
    [TestCase("year")]
    [TestCase("runtime")]
    [TestCase("tmdbRating")]
    [TestCase("releaseDate")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SortsIntoATotalOrder(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SortsAnAbsentDateLastWhenAscending()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SearchesTheFieldsDeclaredSearchable()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FindsAFilmByAnAlternativeTitleInFreeText()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReturnsNothingForATextSearchThatMatchesNothing()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesAnItemByReference()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesNothingForAReferenceTheCatalogDoesNotHold()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("tmdb", "238", "The Godfather")]
    [TestCase("imdb", "tt0068646", "The Godfather")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesAnItemByExternalIdentifier(string arg0, string arg1, string arg2)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesNothingForAnIdentifierSchemeThisKindDoesNotIssue()
        => Assert.Fail("Unreachable: see the fixture remarks.");
}
