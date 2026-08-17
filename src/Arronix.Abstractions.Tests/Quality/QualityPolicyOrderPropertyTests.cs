// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Tests.Quality.Support;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// The ordering property the whole model rests on, asserted over generated points rather than over chosen
/// examples.
/// </summary>
/// <remarks>
/// <para>
/// A comparison that admits a strict preference cycle is not a corner case: a grab happens whenever the
/// comparison says "better", so a cycle <c>A above C above B above A</c> is held A, grab C, grab B, grab
/// A, forever — each iteration a real download and a real import. Examples cannot establish the absence of
/// one; a property over sampled triples can, and it is cheap.
/// </para>
/// <para>
/// The three properties together are exactly "total preorder": every pair is comparable, the comparison is
/// symmetric under swapping the arguments, and the induced at-least-as-good relation is transitive. A
/// total preorder is transitive, and a transitive relation has no strict cycles.
/// </para>
/// </remarks>
[TestFixture]
public class QualityPolicyOrderPropertyTests
{
    private static readonly AxisValue[] Origins =
    [
        AxisFixtures.CameraCapture,
        AxisFixtures.Workprint,
        AxisFixtures.Broadcast,
        AxisFixtures.BroadcastBitstream,
        AxisFixtures.Stream,
        AxisFixtures.HighDefinitionDisc,
        AxisFixtures.HighDefinitionDiscBitstream,
    ];

    private static readonly double[] Resolutions = [480d, 540d, 720d, 1080d, 1440d, 2160d, 4320d];

    /// <summary>Gets the policies the property is asserted over, each stressing a different control.</summary>
    public static IEnumerable<TestCaseData> Policies
    {
        get
        {
            yield return new TestCaseData(AxisFixtures.ShippedVideoDefault()).SetName("shipped default");

            yield return new TestCaseData(QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy
                    .Prefer(AxisFixtures.Origin).WhenUnknownRanksLowest()
                    .Prefer(AxisFixtures.Generation).WhenUnknownRanksLowest()
                    .Prefer(AxisFixtures.Resolution).WhenUnknownRanksLowest()))
                .SetName("everything silent sorts lowest");

            yield return new TestCaseData(QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy
                    .Prefer(AxisFixtures.Resolution)
                        .PreferringLess()
                        .DownTo(AxisValue.Quantity(720))
                        .WhenUnknownAssume(AxisValue.Quantity(1080))
                    .Prefer(AxisFixtures.Generation).UpTo(AxisValue.Quantity(1)).WhenUnknownAssume(AxisValue.Quantity(2))
                    .Prefer(AxisFixtures.Origin).WhenUnknownAssume(AxisFixtures.Stream)))
                .SetName("inverted, floored and assuming");

            yield return new TestCaseData(QualityPolicy.For(
                AxisFixtures.VideoType,
                policy => policy
                    .Prefer(AxisFixtures.Origin)
                        .RankedAs(
                            [AxisFixtures.CameraCapture, AxisFixtures.Workprint],
                            [AxisFixtures.Broadcast],
                            [AxisFixtures.Stream, AxisFixtures.BroadcastBitstream],
                            [AxisFixtures.HighDefinitionDisc, AxisFixtures.HighDefinitionDiscBitstream])
                    .Prefer(AxisFixtures.Resolution).WhenUnknownRanksLowest()
                    .Facet(AxisFixtures.DynamicRange)
                        .Worth(AxisFixtures.HighDynamicRange10, 10)
                        .Worth(AxisFixtures.DolbyVision, -20)))
                .SetName("re-ranked with a negative facet");
        }
    }

    [Test]
    [TestCaseSource(nameof(Policies))]
    public void EveryPairOfPointsIsComparable(QualityPolicy policy)
    {
        var points = Generate(seed: 20260817, count: 40);

        foreach (var left in points)
        {
            foreach (var right in points)
            {
                Assert.That(
                    policy.Compare(left, right),
                    Is.Not.EqualTo(QualityJudgment.Incomparable),
                    "No axis preference can produce an incomparable pair; that member survives only for a "
                    + "bespoke family with a genuinely partial axis.");
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(Policies))]
    public void SwappingTheArgumentsInvertsTheJudgment(QualityPolicy policy)
    {
        var points = Generate(seed: 991, count: 40);

        foreach (var left in points)
        {
            foreach (var right in points)
            {
                var forward = policy.Compare(left, right);
                var backward = policy.Compare(right, left);

                var expected = forward switch
                {
                    QualityJudgment.Better => QualityJudgment.Worse,
                    QualityJudgment.Worse => QualityJudgment.Better,
                    _ => QualityJudgment.Same,
                };

                Assert.That(backward, Is.EqualTo(expected));
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(Policies))]
    public void TheAtLeastAsGoodRelationIsTransitive(QualityPolicy policy)
    {
        var points = Generate(seed: 4242, count: 22);
        var exercised = 0;

        foreach (var low in points)
        {
            foreach (var middle in points)
            {
                if (!AtLeastAsGood(policy, middle, low))
                {
                    continue;
                }

                foreach (var high in points)
                {
                    if (!AtLeastAsGood(policy, high, middle))
                    {
                        continue;
                    }

                    exercised++;

                    Assert.That(
                        AtLeastAsGood(policy, high, low),
                        Is.True,
                        "Transitivity fails, so a strict preference cycle exists and the upgrade loop does "
                        + "not terminate.");
                }
            }
        }

        // A property test whose guard filters every triple is a green run that checks nothing, and a
        // governance rule nobody can tell is dead is worse than no rule because it is trusted.
        Assert.That(
            exercised,
            Is.GreaterThan(1000),
            $"Only {exercised.ToString(CultureInfo.InvariantCulture)} chains reached the assertion.");
    }

    [Test]
    public void TheThreeAxisCounterexampleThatUsedToCycleNoLongerDoes()
    {
        // Three points, three axes, each point silent on a different axis. Under a mode that skipped a
        // silent axis this is a strict cycle; under "silence sorts lowest" it is an ordinary chain.
        var x = QualityAxisId.FromProperty("X");
        var y = QualityAxisId.FromProperty("Y");
        var z = QualityAxisId.FromProperty("Z");
        var family = FormatFamilyId.From("cycle");

        var type = new TestQualityType(
            family,
            "Cycle",
            [
                AxisFixtures.Scalar(x, "X", "units", greaterIsRicher: true),
                AxisFixtures.Scalar(y, "Y", "units", greaterIsRicher: true),
                AxisFixtures.Scalar(z, "Z", "units", greaterIsRicher: true),
            ]);

        var policy = QualityPolicy.For(
            type,
            declaration => declaration
                .Prefer(x).WhenUnknownRanksLowest()
                .Prefer(y).WhenUnknownRanksLowest()
                .Prefer(z).WhenUnknownRanksLowest());

        QualityPoint At(double? first, double? second, double? third)
        {
            var readings = new List<AxisReading>();

            if (first is { } one)
            {
                readings.Add(AxisReading.Of(x, AxisValue.Quantity(one), EvidenceSource.ReleaseTitle));
            }

            if (second is { } two)
            {
                readings.Add(AxisReading.Of(y, AxisValue.Quantity(two), EvidenceSource.ReleaseTitle));
            }

            if (third is { } three)
            {
                readings.Add(AxisReading.Of(z, AxisValue.Quantity(three), EvidenceSource.ReleaseTitle));
            }

            return new QualityPoint { Family = family, Readings = readings };
        }

        var a = At(null, 0, 2);
        var b = At(null, 1, 0);
        var c = At(0, null, 1);

        Assert.Multiple(() =>
        {
            Assert.That(policy.Compare(a, b), Is.EqualTo(QualityJudgment.Better));
            Assert.That(policy.Compare(b, c), Is.EqualTo(QualityJudgment.Better));
            Assert.That(
                policy.Compare(a, c),
                Is.EqualTo(QualityJudgment.Better),
                "The cycle closed here because a skipped axis let the third comparison run backwards; with "
                + "silence pinned to the bottom of the order the chain is A, then B, then C.");
        });
    }

    private static bool AtLeastAsGood(QualityPolicy policy, QualityPoint candidate, QualityPoint held) =>
        policy.Compare(held, candidate) is QualityJudgment.Better or QualityJudgment.Same;

    private static IReadOnlyList<QualityPoint> Generate(int seed, int count)
    {
        var random = new Random(seed);
        var points = new List<QualityPoint>(count);

        for (var index = 0; index < count; index++)
        {
            var readings = new List<AxisReading>();

            if (random.Next(4) > 0)
            {
                readings.Add(AxisFixtures.Quantity(
                    AxisFixtures.Resolution,
                    Resolutions[random.Next(Resolutions.Length)]));
            }

            if (random.Next(4) > 0)
            {
                readings.Add(AxisFixtures.Member(AxisFixtures.Origin, Origins[random.Next(Origins.Length)]));
            }

            if (random.Next(4) > 0)
            {
                readings.Add(AxisFixtures.Quantity(AxisFixtures.Generation, random.Next(3)));
            }

            if (random.Next(4) > 0)
            {
                readings.Add(AxisFixtures.Quantity(AxisFixtures.Corrections, random.Next(3)));
            }

            if (random.Next(4) > 0)
            {
                readings.Add(AxisFixtures.Quantity(AxisFixtures.Mislabels, random.Next(2)));
            }

            if (random.Next(3) > 0)
            {
                var members = new[]
                    {
                        AxisFixtures.StandardDynamicRange,
                        AxisFixtures.HighDynamicRange10,
                        AxisFixtures.HighDynamicRange10Plus,
                        AxisFixtures.DolbyVision,
                    }
                    .Where(_ => random.Next(2) == 0)
                    .ToArray();

                readings.Add(AxisReading.OfMany(
                    AxisFixtures.DynamicRange,
                    EvidenceSource.ReleaseTitle,
                    members));
            }

            points.Add(new QualityPoint { Family = AxisFixtures.Video, Readings = readings });
        }

        Assert.That(
            points,
            Has.Count.EqualTo(count),
            $"The generator produced {points.Count.ToString(CultureInfo.InvariantCulture)} points.");

        return points;
    }
}
