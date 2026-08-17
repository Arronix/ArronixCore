using System.Linq;
using Arronix.Abstractions.Quality;
using FluentAssertions;

// The quality-axes model is experimental (ARX0021).
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The guarantee the ordering owes: it cannot cycle, whatever policy a user composes.
/// </summary>
/// <remarks>
/// <para>
/// A comparison admitting a strict preference cycle is not a corner case, it is an unbounded download
/// loop: a grab happens whenever the comparison says better, so three points that beat each other in a ring
/// are three real downloads and three real imports, repeating forever. The guarantee is therefore not a
/// nicety, and it cannot be established by example — an ordering is acyclic or it is not, and the policies
/// that matter are the ones a <b>user</b> composes in an editor, which no analyzer ever sees.
/// </para>
/// <para>
/// So this is a property test over generated policies and generated points. Each generated policy is
/// representable: every ordering it states is one a user could compose from the axes this family declares,
/// with every control the editor offers — an inverted polarity, a ceiling, a floor, a re-ranking of a closed
/// axis's members, a stated reading of silence, and a bonus score on an axis the ordering does not touch.
/// The three properties asserted are exactly what a total preorder is, and a total preorder is transitive,
/// and a transitive relation has no strict cycles.
/// </para>
/// <para>
/// The non-vacuity guard is not decoration. A property test over points that never differ passes while
/// checking nothing, and that failure is silent.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class QualityAxisOrderPropertyTests
{
    private const int Policies = 40;
    private const int PointsPerPolicy = 24;

    private IQualityType type = null!;

    [SetUp]
    public void SetUp() => type = QualityAxisFixtures.Registry().Get(QualityAxisFixtures.Family);

    [Test]
    public void EveryRepresentablePolicyOrdersTotallyTransitivelyAndWithoutACycle()
    {
        var random = new Random(20260817);
        var strict = 0;
        var chains = 0;

        for (var attempt = 0; attempt < Policies; attempt++)
        {
            var policy = Compose(random);
            var points = Points(random, PointsPerPolicy);

            foreach (var left in points)
            {
                foreach (var right in points)
                {
                    var forward = policy.Compare(left, right);
                    var backward = policy.Compare(right, left);

                    forward.Should().NotBe(
                        QualityJudgment.Incomparable,
                        "every entry a user can state maps an absent reading to a fixed place in the order, "
                        + "so two points of one family always compare");

                    Inverse(forward).Should().Be(
                        backward,
                        "a comparison that is not its own mirror is not an ordering at all");

                    if (forward != QualityJudgment.Same)
                    {
                        strict++;
                    }
                }
            }

            foreach (var first in points)
            {
                foreach (var second in points)
                {
                    if (policy.Compare(first, second) == QualityJudgment.Worse)
                    {
                        continue;
                    }

                    foreach (var third in points)
                    {
                        if (policy.Compare(second, third) == QualityJudgment.Worse)
                        {
                            continue;
                        }

                        chains++;

                        policy.Compare(first, third).Should().NotBe(
                            QualityJudgment.Worse,
                            "at least as good as, composed twice, is at least as good as");
                    }
                }
            }
        }

        strict.Should().BeGreaterThan(
            2000,
            "a property test whose generated points never separate passes while checking nothing");

        chains.Should().BeGreaterThan(
            2000,
            "and one whose triples never reach the transitivity assertion does the same");
    }

    [Test]
    public void TheThreeAxisCounterexampleThatCyclesUnderSkippingDoesNotCycleHere()
    {
        // Three points and three axes, each point silent on a different axis. An ordering that skipped an
        // axis when either side said nothing would rank these in a ring — B above A on the second axis, C
        // above B on the third, A above C on the third again — and would then download all three forever.
        // Placing silence somewhere in the order instead of stepping over it is what removes the ring, and
        // it is why an ordering entry cannot be told to ignore an absent reading.
        var policy = QualityPolicy.For(type, builder => builder
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Resolution))).WhenUnknownRanksLowest()
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Generation))).WhenUnknownRanksLowest()
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Corrections))).WhenUnknownRanksLowest());

        var a = Point(resolution: null, generation: 0, corrections: 2);
        var b = Point(resolution: null, generation: 1, corrections: 0);
        var c = Point(resolution: 720, generation: null, corrections: 1);

        var ring = new[]
        {
            policy.Compare(a, b),
            policy.Compare(b, c),
            policy.Compare(c, a),
        };

        ring.Count(static judgment => judgment == QualityJudgment.Better).Should().BeLessThan(
            3,
            "three strict preferences around a ring is a cycle, and a cycle is an unbounded download loop");

        // And the ordering the shipped mechanism actually produces is a chain, not a ring: the one point
        // that states a resolution leads, and the two that do not are separated beneath it by the axis they
        // both state.
        policy.Compare(a, c).Should().Be(QualityJudgment.Better);
        policy.Compare(c, b).Should().Be(QualityJudgment.Worse);
        policy.Compare(a, b).Should().Be(QualityJudgment.Worse);
    }

    [Test]
    public void APolicyOrderingNothingComparesEverythingEqualAndIsNeverSatisfied()
    {
        var policy = QualityPolicy.For(type, static _ => { });

        var rich = Point(resolution: 2160, generation: 0, corrections: 0);
        var poor = Point(resolution: 480, generation: 3, corrections: 0);

        policy.Compare(rich, poor).Should().Be(QualityJudgment.Same);
        policy.IsGoodEnough(rich).Should().BeFalse(
            "a policy that states nothing to satisfy has not been satisfied, and should keep looking rather "
            + "than stop on the first file it sees");
    }

    [Test]
    public void ABonusScoreSeparatesOnlyWhatTheOrderingLeftTied()
    {
        var policy = QualityPolicy.For(type, builder => builder
            .Prefer(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.Resolution))).WhenUnknownRanksLowest()
            .Facet(QualityAxisFixtures.Axis(nameof(VideoQualityFacts.DynamicRange)))
                .Worth(QualityAxisFixtures.Member(DynamicRangeFormat.DolbyVision), 15));

        var plain = type.Project(new VideoQualityFacts { Resolution = Stated(1080) });
        var bonus = type.Project(new VideoQualityFacts
        {
            Resolution = Stated(1080),
            DynamicRange = EvidenceSet<DynamicRangeFormat>.Of(
                EvidenceSource.ReleaseTitle,
                DynamicRangeFormat.DolbyVision),
        });

        policy.Compare(plain, bonus).Should().Be(QualityJudgment.Better);

        var richer = type.Project(new VideoQualityFacts { Resolution = Stated(2160) });

        policy.Compare(richer, bonus).Should().Be(
            QualityJudgment.Worse,
            "a bonus consulted only on a tie can never overturn an ordered judgment, which is the whole "
            + "reason it is safe where one merged score was not");
    }

    private QualityPolicy Compose(Random random)
    {
        var orderable = type.Axes
            .Where(static axis => axis.Form != AxisForm.Nominal)
            .OrderBy(_ => random.Next())
            .Take(random.Next(1, 5))
            .ToArray();

        var scorable = type.Axes
            .Where(static axis => axis.Form == AxisForm.Nominal)
            .OrderBy(_ => random.Next())
            .Take(random.Next(0, 3))
            .ToArray();

        return QualityPolicy.For(type, builder =>
        {
            foreach (var axis in orderable)
            {
                var entry = builder.Prefer(axis.Id);

                if (random.Next(2) == 0)
                {
                    entry = entry.PreferringLess();
                }

                if (axis.Form == AxisForm.Scalar)
                {
                    if (random.Next(3) == 0)
                    {
                        entry = entry.UpTo(AxisValue.Quantity(random.Next(0, 2200)));
                    }

                    if (random.Next(3) == 0)
                    {
                        entry = entry.DownTo(AxisValue.Quantity(random.Next(0, 2200)));
                    }
                }
                else if (axis.Members.Count > 1 && random.Next(3) == 0)
                {
                    entry = entry.RankedAs(Regroup(random, axis.Members));
                }

                _ = random.Next(2) == 0
                    ? entry.WhenUnknownRanksLowest()
                    : entry.WhenUnknownAssume(Anything(random, axis));
            }

            foreach (var axis in scorable)
            {
                var facet = builder.Facet(axis.Id);

                foreach (var member in axis.Members)
                {
                    facet = facet.Worth(member, random.Next(-100, 101));
                }
            }
        });
    }

    private static IReadOnlyList<AxisValue>[] Regroup(Random random, IReadOnlyList<AxisValue> members)
    {
        var shuffled = members.OrderBy(_ => random.Next()).ToArray();
        var groups = new List<IReadOnlyList<AxisValue>>();
        var taken = 0;

        while (taken < shuffled.Length)
        {
            var size = Math.Min(random.Next(1, 3), shuffled.Length - taken);

            groups.Add(shuffled[taken..(taken + size)]);
            taken += size;
        }

        return [.. groups];
    }

    private static AxisValue Anything(Random random, QualityAxis axis) =>
        axis.Form == AxisForm.Scalar
            ? AxisValue.Quantity(random.Next(0, 2200))
            : axis.Members[random.Next(axis.Members.Count)];

    private IReadOnlyList<QualityPoint> Points(Random random, int count)
    {
        var points = new List<QualityPoint>(count);

        for (var index = 0; index < count; index++)
        {
            points.Add(type.Project(new VideoQualityFacts
            {
                Origin = Maybe(random, (VideoOrigin)random.Next(0, 10)),
                Generation = Maybe(random, random.Next(0, 3)),
                Resolution = Maybe(random, random.Next(0, 5) switch
                {
                    0 => 480,
                    1 => 720,
                    2 => 1080,
                    3 => 1440,
                    _ => 2160,
                }),
                Audio = Maybe(random, (AudioPresentation)random.Next(0, 6)),
                Codec = Maybe(random, (VideoCodec)random.Next(0, 8)),
                FrameRate = Maybe(random, random.Next(0, 2) == 0 ? 24d : 60d),
                Corrections = Maybe(random, random.Next(0, 3)),
                Mislabels = Maybe(random, random.Next(0, 2)),
                Packaging = Maybe(random, (Packaging)random.Next(0, 3)),
                Repacked = Maybe(random, (Repackaging)random.Next(0, 2)),
                DynamicRange = random.Next(3) == 0
                    ? EvidenceSet<DynamicRangeFormat>.None
                    : EvidenceSet<DynamicRangeFormat>.Of(
                        EvidenceSource.ReleaseTitle,
                        (DynamicRangeFormat)random.Next(0, 6)),
                Flaws = random.Next(3) == 0
                    ? EvidenceSet<VideoFlaw>.None
                    : EvidenceSet<VideoFlaw>.Of(EvidenceSource.ReleaseTitle, (VideoFlaw)random.Next(0, 8)),
            }));
        }

        return points;
    }

    private QualityPoint Point(int? resolution, int? generation, int corrections) =>
        type.Project(new VideoQualityFacts
        {
            Resolution = resolution is { } lines ? Stated(lines) : Evidence<int>.None,
            Generation = generation is { } encodes ? Stated(encodes) : Evidence<int>.None,
            Corrections = Stated(corrections),
        });

    private static Evidence<TValue> Maybe<TValue>(Random random, TValue value)
        where TValue : struct =>
        random.Next(4) == 0 ? Evidence<TValue>.None : Stated(value);

    private static Evidence<TValue> Stated<TValue>(TValue value)
        where TValue : struct =>
        Evidence<TValue>.From(value, EvidenceSource.ReleaseTitle);

    private static QualityJudgment Inverse(QualityJudgment judgment) =>
        judgment switch
        {
            QualityJudgment.Better => QualityJudgment.Worse,
            QualityJudgment.Worse => QualityJudgment.Better,
            _ => judgment,
        };
}
