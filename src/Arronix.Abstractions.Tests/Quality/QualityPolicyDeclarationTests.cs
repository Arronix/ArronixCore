// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Tests.Quality.Support;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// What a policy refuses to compile.
/// </summary>
/// <remarks>
/// An analyzer can cover a policy written in source. It cannot cover a policy a <b>user</b> composed in an
/// editor, and that is the one the cycle-safety argument depends on — a family's shipped default is not
/// where a bad policy comes from. So the validation is here, once, where both paths pass through.
/// </remarks>
[TestFixture]
public class QualityPolicyDeclarationTests
{
    [Test]
    public void AnAxisWithNoOrderCannotOrderAnything()
    {
        Assert.That(
            () => QualityPolicy.For(AxisFixtures.VideoType, policy => policy.Prefer(AxisFixtures.DynamicRange)),
            Throws.ArgumentException.With.Message.Contains("no order"));
    }

    [Test]
    public void AnAxisEitherOrdersOrScoresAndNeverBoth()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy
                    .Prefer(AxisFixtures.Resolution)
                    .Facet(AxisFixtures.Resolution).Worth(AxisValue.Quantity(1080), 10)),
            Throws.ArgumentException.With.Message.Contains("both orders and scores"));
    }

    [Test]
    public void AnAxisHasOnePlaceInTheOrdering()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy.Prefer(AxisFixtures.Resolution).Prefer(AxisFixtures.Resolution)),
            Throws.ArgumentException);
    }

    [Test]
    public void APolicyCannotNameAnAxisTheFamilyDoesNotDeclare()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy.Prefer(QualityAxisId.FromProperty("Loudness"))),
            Throws.ArgumentException.With.Message.Contains("no axis called"));
    }

    [Test]
    public void AFacetPointOutsideTheBoundIsRefused()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy.Facet(AxisFixtures.DynamicRange).Worth(AxisFixtures.DolbyVision, 5000)),
            Throws.ArgumentException.With.Message.Contains("outside plus or minus 100"));
    }

    [Test]
    public void NegativePointsAreAPreferenceAndAreAllowed()
    {
        var policy = QualityPolicy.For(
            AxisFixtures.VideoType,
            declaration => declaration.Facet(AxisFixtures.DynamicRange).Worth(AxisFixtures.DolbyVision, -40));

        var disliked = AxisFixtures.Point(
            AxisReading.Of(AxisFixtures.DynamicRange, AxisFixtures.DolbyVision, EvidenceSource.ReleaseTitle));

        Assert.Multiple(() =>
        {
            Assert.That(policy.Facets.Of(disliked), Is.EqualTo(-40));
            Assert.That(
                policy.Admits(disliked).IsAdmitted,
                Is.True,
                "No number of negative points makes a candidate ineligible; refusal was never a very "
                + "negative preference.");
        });
    }

    [Test]
    public void ARequirementThatNamesNeitherAValueNorABoundIsRefused()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy.Refuse(AxisFixtures.Packaging)),
            Throws.ArgumentException.With.Message.Contains("neither a value nor a bound"));
    }

    [Test]
    public void APartialRankingIsRefusedRatherThanSilentlyTyingEveryUnnamedMember()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy
                    .Prefer(AxisFixtures.Origin)
                    .RankedAs([AxisFixtures.Stream], [AxisFixtures.HighDefinitionDisc])),
            Throws.ArgumentException.With.Message.Contains("exactly once"));
    }

    [Test]
    public void AQuantityHasNoMembersToReRank()
    {
        Assert.That(
            () => QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy
                    .Prefer(AxisFixtures.Resolution)
                    .RankedAs([AxisValue.Quantity(1080)])),
            Throws.ArgumentException.With.Message.Contains("no members to re-rank"));
    }

    [Test]
    public void APolicyThatDeclaresNothingOrdersNothingAndIsNeverSatisfied()
    {
        var policy = QualityPolicy.For(AxisFixtures.VideoType, _ => { });
        var point = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 2160));

        Assert.Multiple(() =>
        {
            Assert.That(policy.Precedence, Is.Empty);
            Assert.That(policy.Facets.Scores, Is.Empty);
            Assert.That(policy.Requirements, Is.Empty);
            Assert.That(
                policy.IsGoodEnough(point),
                Is.False,
                "A cutoff with no floors states nothing to satisfy, so it is never satisfied — which is "
                + "the right default: a policy declaring no cutoff should keep looking.");
            Assert.That(policy.Describe(), Does.StartWith("Order nothing."));
        });
    }
}
