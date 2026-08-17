// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Tests.Quality.Support;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// The cutoff, the point it reads, and the size expectation the same point feeds.
/// </summary>
[TestFixture]
public class CutoffAndPointTests
{
    [Test]
    public void AFloorReadsInRichnessSoOneWidgetServesEveryAxis()
    {
        var policy = QualityPolicy.For(
            AxisFixtures.VideoType,
            declaration => declaration
                .GoodEnoughAt(AxisFixtures.Resolution, AxisValue.Quantity(1080))
                .GoodEnoughAt(AxisFixtures.Generation, AxisValue.Quantity(1)));

        var untouched = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        var onceEncoded = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var twiceEncoded = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Quantity(AxisFixtures.Generation, 2));

        var tooSmall = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 720),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsGoodEnough(untouched), Is.True);
            Assert.That(policy.IsGoodEnough(onceEncoded), Is.True);
            Assert.That(
                policy.IsGoodEnough(twiceEncoded),
                Is.False,
                "On a descending axis 'at least this rich' is 'at most this many', so one floor covers both "
                + "polarities and nobody has to remember which axes count downwards.");
            Assert.That(policy.IsGoodEnough(tooSmall), Is.False);
        });
    }

    [Test]
    public void AFloorThatIgnoresSilenceIsSatisfiedVacuouslyRatherThanFailing()
    {
        var strict = QualityPolicy.For(
            AxisFixtures.VideoType,
            policy => policy.GoodEnoughAt(AxisFixtures.Generation, AxisValue.Quantity(1)));

        var forgiving = QualityPolicy.For(
            AxisFixtures.VideoType,
            policy => policy.GoodEnoughAt(AxisFixtures.Generation, AxisValue.Quantity(1), UnknownEvidence.Ignore));

        var silent = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 1080));

        Assert.Multiple(() =>
        {
            Assert.That(strict.IsGoodEnough(silent), Is.False);
            Assert.That(
                forgiving.IsGoodEnough(silent),
                Is.True,
                "A file whose axis has a legitimate reason to be silent must still be able to satisfy a "
                + "cutoff written for files that do state it.");
        });
    }

    [Test]
    public void WhetherToGoLookingIsCarriedByThePolicyAndNotDecidedByIt()
    {
        var policy = QualityPolicy.For(
            AxisFixtures.VideoType,
            declaration => declaration
                .Prefer(AxisFixtures.Resolution)
                .WithoutUpgrades());

        Assert.That(
            policy.Cutoff.UpgradesEnabled,
            Is.False,
            "It says whether to schedule a search; a decision a user asked for explicitly still gets an "
            + "honest answer.");
    }

    [Test]
    public void AnUndeclaredAxisReadsAsAbsentRatherThanThrowing()
    {
        var point = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 1080));
        var reading = point[AxisFixtures.Packaging];

        Assert.Multiple(() =>
        {
            Assert.That(reading.IsKnown, Is.False);
            Assert.That(reading.Values, Is.Empty);
            Assert.That(reading.Value.IsKnown, Is.False);
            Assert.That(reading.Axis, Is.EqualTo(AxisFixtures.Packaging));
        });
    }

    [Test]
    public void ASetValuedReadingHasNoSingleValueButStillHoldsItsMembers()
    {
        var reading = AxisReading.OfMany(
            AxisFixtures.DynamicRange,
            EvidenceSource.ReleaseTitle,
            AxisFixtures.DolbyVision,
            AxisFixtures.HighDynamicRange10Plus);

        Assert.Multiple(() =>
        {
            Assert.That(reading.Value.IsKnown, Is.False);
            Assert.That(reading.Holds(AxisFixtures.DolbyVision), Is.True);
            Assert.That(reading.Holds(AxisFixtures.HighDynamicRange10), Is.False);
        });
    }

    [Test]
    public void AnUnassessableExpectationPassesEverythingRatherThanRejectingIt()
    {
        var unknown = SizeExpectation.NotAssessable;
        var known = new SizeExpectation(1_000_000, 350_000, 3_000_000, "computed");

        Assert.Multiple(() =>
        {
            Assert.That(
                unknown.Assess(1),
                Is.EqualTo(SizeVerdict.NotAssessable),
                "A release nobody can assess is not thereby implausible; a size gate that refused what it "
                + "could not measure would be a requirement in disguise.");
            Assert.That(known.Assess(1_000_000), Is.EqualTo(SizeVerdict.Plausible));
            Assert.That(known.Assess(200_000), Is.EqualTo(SizeVerdict.ImplausiblySmall));
            Assert.That(known.Assess(60_000_000), Is.EqualTo(SizeVerdict.ImplausiblyLarge));
        });
    }
}
