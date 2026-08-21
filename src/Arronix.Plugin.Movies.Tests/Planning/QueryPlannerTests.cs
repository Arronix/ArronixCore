using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitIgnoreAttribute = global::NUnit.Framework.IgnoreAttribute;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;

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
[NUnitTestFixtureAttribute]
public class QueryPlannerTests
{
    static QueryPlannerTests()
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
    public void PlansAnIdentifierTierAndATextFallback()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansOnlyATextTierForATitleSearch()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesBothIdentifiersInOneQueryWithTheCatalogOneFirst()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesOnlyTheCatalogIdentifierForAMovieWithNoOther()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesFreeTextOnAnIdentifierQueryToo()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansOneTextQueryPerTitleSpelling()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void SuppliesTheYearBothAsTextAndAsAnArgument()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansNoTextQueryForAMovieWithNoYear()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansASingleUnnamedBrowseForASweep()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ScopesEveryQueryToTheKindsCategories()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansNothingForASearchKindThisShapeDoesNotDeclare()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansNothingForAUnitTheCatalogDoesNotHold()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PlansForEveryUnitInABatch()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AsksTheSourceForWordsRatherThanForAComparisonKey()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RejectsANullRequest()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");
}
