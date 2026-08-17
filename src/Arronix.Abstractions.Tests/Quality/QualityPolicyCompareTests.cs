// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Tests.Quality.Support;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// The four orderings one policy has to hold at once, plus the controls that hold them.
/// </summary>
/// <remarks>
/// The first two are the pair no single-axis model can hold together: a stream re-encode and a stream
/// download are equivalent, and a disc re-encode and a disc bitstream copy are not. They are held here by
/// two independent controls — a member on the origin axis, and a ceiling on the generation axis — and
/// neither one reaches into the other.
/// </remarks>
[TestFixture]
public class QualityPolicyCompareTests
{
    private static QualityPolicy Policy => AxisFixtures.ShippedVideoDefault();

    [Test]
    public void AStreamReEncodeAndAStreamDownloadAtOneResolutionAreEquivalent()
    {
        var reEncoded = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var downloaded = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        Assert.That(
            Policy.Compare(reEncoded, downloaded),
            Is.EqualTo(QualityJudgment.Same),
            "The generation ceiling ties zero and one re-encode, which is what stops the library "
            + "re-downloading across a difference of a few percent of bitrate.");
    }

    [Test]
    public void ADiscBitstreamCopyIsAboveADiscReEncodeAtOneResolution()
    {
        var reEncoded = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var bitstream = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDiscBitstream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        Assert.That(
            Policy.Compare(reEncoded, bitstream),
            Is.EqualTo(QualityJudgment.Better),
            "The master-to-rip cliff lives on the origin axis, so the same generation ceiling that ties "
            + "the stream pair leaves this pair separated.");
    }

    [Test]
    public void ASecondGenerationRipIsBelowAFirstGenerationOne()
    {
        var first = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var second = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc),
            AxisFixtures.Quantity(AxisFixtures.Generation, 2));

        Assert.That(
            Policy.Compare(first, second),
            Is.EqualTo(QualityJudgment.Worse),
            "A ceiling caps an axis; it does not switch it off. A rip of a rip is still a real drop.");
    }

    [Test]
    public void AnUntouchedBroadcastStreamIsAboveABroadcastCapture()
    {
        var capture = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Broadcast),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var untouched = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.BroadcastBitstream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        Assert.That(Policy.Compare(capture, untouched), Is.EqualTo(QualityJudgment.Better));
    }

    [Test]
    public void ACeilingCapsRichnessRatherThanRefusingIt()
    {
        var capped = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 2160));
        var beyond = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 4320));

        Assert.Multiple(() =>
        {
            Assert.That(Policy.Compare(capped, beyond), Is.EqualTo(QualityJudgment.Same));
            Assert.That(
                Policy.Admits(beyond).IsAdmitted,
                Is.True,
                "Capping and refusing are different intents, and only one of them is a requirement.");
        });
    }

    [Test]
    public void AnAbsentReadingSortsBelowEveryPresentOneUnderTheLowestMode()
    {
        var silent = AxisFixtures.Point(AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream));
        var stated = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 480),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream));

        Assert.That(Policy.Compare(silent, stated), Is.EqualTo(QualityJudgment.Better));
    }

    [Test]
    public void TheFacetTierOnlySpeaksWhenTheOrderingIsSilent()
    {
        var plain = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        var withDynamicRange = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0),
            AxisReading.OfMany(
                AxisFixtures.DynamicRange,
                EvidenceSource.ReleaseTitle,
                AxisFixtures.DolbyVision,
                AxisFixtures.HighDynamicRange10Plus));

        var richerButPlain = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 2160),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        Assert.Multiple(() =>
        {
            Assert.That(
                Policy.Compare(plain, withDynamicRange),
                Is.EqualTo(QualityJudgment.Better),
                "On a core tie the bonus decides.");
            Assert.That(
                Policy.Compare(richerButPlain, withDynamicRange),
                Is.EqualTo(QualityJudgment.Worse),
                "A bonus can never overturn something the ordering already decided.");
            Assert.That(
                Policy.Facets.Of(withDynamicRange),
                Is.EqualTo(15),
                "A set-valued facet takes its greatest member, never the sum: a release is not better for "
                + "stating its dynamic range twice.");
        });
    }

    [Test]
    public void AUserCanReRankAClosedAxisWithoutTouchingAnyOtherClause()
    {
        var policy = QualityPolicy.For(
            AxisFixtures.VideoType,
            declaration => declaration
                .Prefer(AxisFixtures.Origin)
                .RankedAs(
                    [AxisFixtures.CameraCapture],
                    [AxisFixtures.Workprint],
                    [AxisFixtures.Broadcast],
                    [AxisFixtures.BroadcastBitstream],
                    [AxisFixtures.HighDefinitionDisc, AxisFixtures.HighDefinitionDiscBitstream],
                    [AxisFixtures.Stream]));

        var disc = AxisFixtures.Point(AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc));
        var bitstream = AxisFixtures.Point(
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDiscBitstream));
        var stream = AxisFixtures.Point(AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream));

        Assert.Multiple(() =>
        {
            Assert.That(
                policy.Compare(disc, bitstream),
                Is.EqualTo(QualityJudgment.Same),
                "Two chips in one row is how a user declares two members equivalent.");
            Assert.That(
                policy.Compare(disc, stream),
                Is.EqualTo(QualityJudgment.Better),
                "The family declares a disc above a stream; this user says otherwise, and a contested pair "
                + "is a setting rather than an argument.");
        });
    }

    [Test]
    public void APolicyRefusesToRankAPointOfAnotherFamily()
    {
        var video = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 1080));
        var written = new QualityPoint { Family = AxisFixtures.Written, Readings = [] };

        Assert.Multiple(() =>
        {
            Assert.That(() => Policy.Compare(video, written), Throws.ArgumentException);
            Assert.That(() => Policy.Admits(written), Throws.ArgumentException);
        });
    }
}
