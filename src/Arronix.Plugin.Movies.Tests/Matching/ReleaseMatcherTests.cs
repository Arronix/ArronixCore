namespace Arronix.Plugin.Movies.Tests.Matching;

/// <summary>
/// Matching a parsed reading onto a catalog entry.
/// </summary>
/// <remarks>
/// <para>
/// Movies is the layered-key-lookup member of the match strategy family, and the cascade is host code
/// parameterized by the declaration: four ordered key layers, one agreement rule, an identifier precedence
/// and a scope rule. The declared half is asserted in <c>MatchDeclarationTests</c>; the cases below run a
/// reading through the cascade and need the engine.
/// </para>
/// <para>
/// The corpus is preserved row for row and marked ignored rather than deleted so the gap is visible.
/// </para>
/// </remarks>
[TestFixture]
public class ReleaseMatcherTests
{
    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void DeclaresTheMediaKindItAnswersFor()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The.Godfather.1972.1080p.BluRay.x264-RlsGrp", "The Godfather")]
    [TestCase("Inception.2010.2160p.UHD.BluRay.x265-RlsGrp", "Inception")]
    [TestCase("The.Matrix.1999.1080p.BluRay.x264-RlsGrp", "The Matrix")]
    [TestCase("Dune.Part.Two.2024.2160p.WEB-DL.DDP5.1.Atmos.H.265-RlsGrp", "Dune: Part Two")]
    [TestCase("12.Angry.Men.1957.1080p.BluRay.x264-RlsGrp", "12 Angry Men")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void MatchesOnTitleAndYear(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void TellsTwoFilmsOfTheSameNameApartByYear()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void DoesNotTakeANumericTitleForAYear()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void MatchesAnAlternativeTitle()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void MatchesAnOriginalLanguageTitle()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void MatchesATranslatedTitle()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void MatchesAcrossARomanNumeralRewrite()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ShortCircuitsOnAnIdentifierCarriedByTheText()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ShortCircuitsOnAnIdentifierSuppliedByTheCaller()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RefusesAnIdentifierWhoseYearContradictsTheCatalog()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AcceptsEitherOfAFilmsTwoYears()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void PrefersTheImdbIdentifierOverTheCatalogOne()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RefusesAReleaseThatContradictsTheRequestedMovie()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AcceptsAReleaseThatNamesTheRequestedMovie()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void AcceptsAnIdentifierInsideAScopeThatTheTitleContradicts()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RefusesAScopeThatNamesNothingInThisCatalog()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void RefusesATitleTheCatalogDoesNotHold()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void FallsBackToTheRawTextWhenNothingParses()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("ReleaseName")]
    [TestCase("FileName")]
    [TestCase("FolderName")]
    [TestCase("DownloaderTitle")]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void GradesConfidenceOnProvenanceWhenNoYearWasStated(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void StampsTheSingleCoordinateOnEveryMatch()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void CarriesNoCoordinatesOnARejection()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReportsWhyItMatchedOnlyThroughAnExtensionLocalOverload()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ReportsNoReasonForARejection()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void UsesTheFolderPatternOnlyForAFolder()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now - the host binds them from this declaration through its public binder and MoviesEngines drives them - so the old reason is gone. What blocks this row is that the bound kind's item source is the host's pre-storage one and holds no rows: this milestone has no persistence and no metadata pipeline, so matching, query planning, catalog projection and any rename that has to resolve an item have nothing to answer with. The storage milestone unblocks it; more visibility does not.")]
    public void ResolvesEveryMatchToExactlyOneUnit()
        => Assert.Fail("Unreachable: see the fixture remarks.");
}
