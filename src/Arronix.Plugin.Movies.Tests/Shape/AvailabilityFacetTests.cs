
using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// Minimum availability: a movie's only selection policy, and the clearest example in this plugin of a
/// domain concept the string surface could hold but not describe.
/// </summary>
/// <remarks>
/// The rule is <c>status &gt;= chosen</c> over five ordered values. Under the string surface the ordering
/// survived only in a hand-written rank function no consumer could read, and the facet had to call itself
/// set membership. The policy is declared against an enumeration now, so the order is the enumeration's,
/// the comparison is the compiler's, and the descriptor says both.
/// </remarks>
[TestFixture]
public class AvailabilityFacetTests
{
    private static SelectionFacet Availability { get; } = MoviesDeclaration.Shape.SelectionFacets
        .Single(static facet => facet.FacetId == "status");

    private static SelectionFacet Delay { get; } = MoviesDeclaration.Shape.SelectionFacets
        .Single(static facet => facet.FacetId == "availabilityDelay");

    [Test]
    public void AppliesToTheMovieLevel()
        => Assert.That(Availability.AppliesToLevelId, Is.EqualTo(MoviesDeclaration.Level.Id));

    [Test]
    public void IsNamedForWhatItAsksRatherThanForThePropertyItReads()
        => Assert.That(Availability.Name, Is.EqualTo("Minimum availability"));

    [Test]
    public void IsSingleValued()
        => Assert.That(
            Availability.MultiValued,
            Is.False,
            "It is a threshold with one chosen point, not a set of acceptable states.");

    [Test]
    public void DefaultsToReleased()
        => Assert.That(Availability.DefaultAllowed, Is.EqualTo(new[] { "released" }));

    [Test]
    public void OffersTheFourStatesAUserMayChoose()
        => Assert.That(
            Availability.Values.Select(static value => value.Value),
            Is.EqualTo(new[] { "tba", "announced", "inCinemas", "released" }));

    /// <summary>
    /// <b>The gap this facet used to record against itself is closed.</b> The declaration now says its
    /// values are ordered, so a consumer cannot re-sort them alphabetically without violating the contract
    /// — which is exactly what it could do before.
    /// </summary>
    [Test]
    public void DeclaresThatItsValuesAreOrdered()
        => Assert.Multiple(() =>
        {
            Assert.That(Availability.ValuesAreOrdered, Is.True);
            Assert.That(
                Availability.Values.Select(static value => (int)Enum.Parse<MovieReleaseStage>(value.Value, true)),
                Is.Ordered.Ascending,
                "The order is the enumeration's, not the call site's.");
        });

    /// <summary>
    /// The rank the string surface carried in code the host could not see is the enumeration's own value.
    /// </summary>
    [TestCase(MovieReleaseStage.Tba, 0)]
    [TestCase(MovieReleaseStage.Announced, 1)]
    [TestCase(MovieReleaseStage.InCinemas, 2)]
    [TestCase(MovieReleaseStage.Released, 3)]
    public void RanksEveryStatus(MovieReleaseStage status, int expected)
        => Assert.That((int)status, Is.EqualTo(expected));

    /// <summary>The rule itself, applied — and it is an ordinary comparison the compiler checks.</summary>
    [TestCase(MovieReleaseStage.Released, MovieReleaseStage.InCinemas, true)]
    [TestCase(MovieReleaseStage.InCinemas, MovieReleaseStage.Released, false)]
    [TestCase(MovieReleaseStage.Released, MovieReleaseStage.Released, true)]
    [TestCase(MovieReleaseStage.Announced, MovieReleaseStage.Tba, true)]
    public void DecidesAvailabilityByRankRatherThanByMembership(
        MovieReleaseStage status,
        MovieReleaseStage chosen,
        bool expected)
        => Assert.That(status >= chosen, Is.EqualTo(expected));

    /// <summary>Catalog withdrawal is not a release-stage choice.</summary>
    [Test]
    public void DoesNotOfferTheWithdrawnStateAsAChoice()
        => Assert.That(
            Availability.Values.Select(static value => value.Value),
            Does.Not.Contain("withdrawn"));

    /// <summary>
    /// <b>The defect this facet recorded against itself is closed.</b> An unavailable movie is not hidden
    /// and its row is not uncreated: the user sees it, and only a grab is refused. The old declaration had
    /// to pick the less destructive of two wrong answers; the policy gates acquisition now, and it gets
    /// there without saying so, because a threshold over a property of an item cannot un-materialize the
    /// item that carries the property.
    /// </summary>
    [Test]
    public void GatesAcquisitionRatherThanVisibility()
        => Assert.That(Availability.Application, Is.EqualTo(FacetApplication.Acquisition));

    [Test]
    public void DeclaresTheDelayAsANumericThresholdInDays()
        => Assert.Multiple(() =>
        {
            Assert.That(Delay.Kind, Is.EqualTo(SelectionFacetKind.Threshold));
            Assert.That(Delay.ThresholdDirection, Is.EqualTo(ThresholdDirection.AtLeast));
            Assert.That(Delay.Unit, Is.EqualTo("days"));
            Assert.That(Delay.DefaultNumber, Is.Zero);
        });

    /// <summary>
    /// The delay is the one policy with no backing property — it is per-profile rather than per-item — so
    /// it is the only selection row still declared by identifier, and it names no field.
    /// </summary>
    [Test]
    public void DeclaresTheDelayByIdentifierBecauseItHasNoBackingProperty()
        => Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey(Delay.FacetId));

    /// <summary>
    /// Availability, by contrast, <i>is</i> a property, and the facet is named by it rather than by a
    /// string that had to agree with one. The pair is what makes the policy computable.
    /// </summary>
    [Test]
    public void NamesTheStatusPropertyTheThresholdIsMeasuredOn()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Fields, Does.ContainKey(Availability.FacetId));
            Assert.That(
                MoviesDeclaration.Fields[Availability.FacetId].Semantics.HasFlag(FieldSemantics.Status),
                Is.True);
        });
}
