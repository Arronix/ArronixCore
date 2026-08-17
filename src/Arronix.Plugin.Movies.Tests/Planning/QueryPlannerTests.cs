namespace Arronix.Plugin.Movies.Tests.Planning;

/// <summary>
/// Turning an acquisition into queries.
/// </summary>
/// <remarks>
/// <para>
/// Tier ordering, fan-out and limit application are host behavior over the declared tiers. The declared
/// half — which tiers exist, what each asks for, what each refuses to plan without, and the alias order —
/// is asserted in <c>QueryDeclarationTests</c>; the cases below plan against a catalog and need the
/// engine.
/// </para>
/// <para>
/// The corpus is preserved row for row and marked ignored rather than deleted so the gap is visible.
/// </para>
/// </remarks>
[TestFixture]
public class QueryPlannerTests
{
    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void DeclaresTheMediaKindItAnswersFor()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansAnIdentifierTierAndATextFallback()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansOnlyATextTierForATitleSearch()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesBothIdentifiersInOneQueryWithTheCatalogOneFirst()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesOnlyTheCatalogIdentifierForAMovieWithNoOther()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesFreeTextOnAnIdentifierQueryToo()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansOneTextQueryPerTitleSpelling()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SuppliesTheYearBothAsTextAndAsAnArgument()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansNoTextQueryForAMovieWithNoYear()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansASingleUnnamedBrowseForASweep()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ScopesEveryQueryToTheKindsCategories()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansNothingForASearchKindThisShapeDoesNotDeclare()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansNothingForAUnitTheCatalogDoesNotHold()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansForEveryUnitInABatch()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AsksTheSourceForWordsRatherThanForAComparisonKey()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RejectsANullRequest()
        => Assert.Fail("Unreachable: see the fixture remarks.");
}
