// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using Arronix.Abstractions.Quality;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// Typed absence is the point of these two types, so every assertion here is about telling apart three
/// states a nullable collapses into two.
/// </summary>
[TestFixture]
public class EvidenceTests
{
    private enum Flaw
    {
        Upscaled = 0,
        Interlaced = 1,
        Watermarked = 2,
    }

    [Test]
    public void AnAbsentReadingCarriesNoValueAndSaysSo()
    {
        var absent = Evidence<int>.None;

        Assert.Multiple(() =>
        {
            Assert.That(absent.IsKnown, Is.False);
            Assert.That(absent.TryGet(out _), Is.False);
            Assert.That(absent.Or(720), Is.EqualTo(720), "The fallback is the only way to read an absent value.");
        });
    }

    [Test]
    public void AReadingCarriesItsProvenanceAsWellAsItsValue()
    {
        var claimed = Evidence<int>.From(1080, EvidenceSource.ReleaseTitle);
        var measured = Evidence<int>.From(720, EvidenceSource.ContainerProbe);

        Assert.Multiple(() =>
        {
            Assert.That(claimed.TryGet(out var stated), Is.True);
            Assert.That(stated, Is.EqualTo(1080));
            Assert.That(
                measured.Source,
                Is.GreaterThan(claimed.Source),
                "Provenance decides trust, so the sources have to order; a nullable carries no provenance at all.");
        });
    }

    [Test]
    public void LookingAndFindingNothingIsNotTheSameAsNotLooking()
    {
        var neverLooked = EvidenceSet<Flaw>.None;
        var lookedAndFoundNothing = EvidenceSet<Flaw>.Empty(EvidenceSource.ContainerProbe);

        Assert.Multiple(() =>
        {
            Assert.That(neverLooked.IsKnown, Is.False);
            Assert.That(lookedAndFoundNothing.IsKnown, Is.True);
            Assert.That(lookedAndFoundNothing.Members, Is.Empty);
            Assert.That(
                neverLooked,
                Is.Not.EqualTo(lookedAndFoundNothing),
                "A policy that refuses a defect must not refuse a release it never inspected.");
        });
    }

    [Test]
    public void ASetHoldsSeveralMembersAtOnceAndDeduplicatesThem()
    {
        var set = EvidenceSet<Flaw>.Of(EvidenceSource.ReleaseTitle, Flaw.Upscaled, Flaw.Watermarked, Flaw.Upscaled);

        Assert.Multiple(() =>
        {
            Assert.That(set.Members, Has.Count.EqualTo(2));
            Assert.That(set.Has(Flaw.Upscaled), Is.True);
            Assert.That(set.Has(Flaw.Interlaced), Is.False);
        });
    }

    [Test]
    public void AValueIsIdentifiedByWhereItSitsAndNotByHowItIsSpelled()
    {
        var declared = AxisValue.Member(9, "HighDefinitionDiscBitstream");
        var asAUserWroteIt = AxisValue.Member(9, "Remux");

        Assert.Multiple(() =>
        {
            Assert.That(declared.Names(asAUserWroteIt), Is.True);
            Assert.That(declared.Names(AxisValue.Member(8, "Remux")), Is.False);
            Assert.That(AxisValue.None.Names(AxisValue.None), Is.False, "Absence names no point.");
            Assert.That(
                AxisValue.Member(0, "zero").Names(AxisValue.Quantity(0)),
                Is.False,
                "A member and a quantity are different shapes even at the same number.");
        });
    }
}
