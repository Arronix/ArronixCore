using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Matching;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

// Every contract named here is experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The assignment-over-features strategy: the Munkres solver, the six-operator distance vocabulary and
/// the declared feature tuning.
/// </summary>
[TestFixture]
internal sealed class MatchEngineAssignmentTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static AssignmentOverFeaturesStrategy Strategy() =>
        new(DistanceFeatureCatalog.CreateDefault(Clock));

    private static IReadOnlyList<FeatureParameter> AllFeatures(params string[] featureIds) =>
        featureIds.Select(id => new FeatureParameter { FeatureId = id }).ToArray();

    private static AssignmentCandidate Row(string title, long? position = null, int? seconds = null) => new()
    {
        Title = title,
        Position = position,
        Length = seconds is { } stated ? TimeSpan.FromSeconds(stated) : null,
    };

    [Test]
    public void TheSolverFindsTheCheapestAssignmentNotTheGreedyOne()
    {
        // Greedy row-by-row would take (0,0)=1, then (1,1)=4, then (2,2)=9, total 14; the optimum
        // reverses the diagonal for 10.
        double[][] costs =
        [
            [1, 2, 3],
            [2, 4, 6],
            [3, 6, 9],
        ];

        var solver = new MunkresSolver(costs);
        var solution = solver.Solve();

        solution.Should().HaveCount(3);
        solver.CostOf(solution).Should().Be(10.0, "columns 2,1,0 against rows 0,1,2 is the optimum");
    }

    [Test]
    public void ARectangularProblemLeavesTheSurplusSideUnassigned()
    {
        double[][] costs =
        [
            [0.9, 0.1, 0.9],
            [0.9, 0.9, 0.1],
        ];

        var solution = new MunkresSolver(costs).Solve();

        solution.Should().HaveCount(2);
        solution.Should().Contain((0, 1)).And.Contain((1, 2));
    }

    [Test]
    public void TheAssignmentRecoversAShuffledRunningOrder()
    {
        var request = new AssignmentRequest
        {
            Features = AllFeatures("title", "position"),
            Sources =
            [
                Row("Gamma Part", position: 3),
                Row("Alpha Part", position: 1),
                Row("Beta Part", position: 2),
            ],
            Targets =
            [
                Row("Alpha Part", position: 1),
                Row("Beta Part", position: 2),
                Row("Gamma Part", position: 3),
            ],
        };

        var result = Strategy().Assign(request);

        result.Pairs.Should().BeEquivalentTo(new[]
        {
            new AssignmentPair(0, 2, 0.0),
            new AssignmentPair(1, 0, 0.0),
            new AssignmentPair(2, 1, 0.0),
        });
        result.NormalizedDistance.Should().Be(0.0);
        result.IsAcceptable.Should().BeTrue();
    }

    [Test]
    public void UnmatchedRowsOnEitherSidePenalizeTheAggregate()
    {
        var request = new AssignmentRequest
        {
            Features = AllFeatures("title"),
            Sources = [Row("Alpha Part")],
            Targets = [Row("Alpha Part"), Row("Beta Part"), Row("Gamma Part")],
        };

        var result = Strategy().Assign(request);

        result.Pairs.Should().ContainSingle().Which.TargetIndex.Should().Be(0);
        result.UnassignedTargets.Should().BeEquivalentTo(new[] { 1, 2 });
        result.NormalizedDistance.Should().BeApproximately(2.0 / 3.0, 1e-9, "two of three targets went unmatched");
        result.IsAcceptable.Should().BeFalse("the surveyed 0.15 gate rejects a two-thirds miss");
    }

    [Test]
    public void TheDeclaredAcceptThresholdIsTheGateNotHostPolicy()
    {
        var request = new AssignmentRequest
        {
            Features = AllFeatures("title"),
            Sources = [Row("Alpha Part")],
            Targets = [Row("Alpha Part"), Row("Beta Part"), Row("Gamma Part")],
            AcceptThreshold = 0.9,
        };

        var result = Strategy().Assign(request);

        result.IsAcceptable.Should().BeTrue("the binding declared a looser gate, visibly");
    }

    [Test]
    public void ADeclaredWeightChangesWhichTargetWins()
    {
        // The source's title matches one target and its position matches the other; the declared
        // weights decide, exactly as Lidarr's externalized weight table does (Distance.cs:11-32).
        var sources = new[] { Row("Alpha Part", position: 2) };
        var targets = new[]
        {
            Row("Alpha Part", position: 9),
            Row("Unrelated Name", position: 2),
        };

        var titleHeavy = Strategy().Assign(new AssignmentRequest
        {
            Features =
            [
                new FeatureParameter { FeatureId = "title", Weight = 10.0 },
                new FeatureParameter { FeatureId = "position", Weight = 0.1 },
            ],
            Sources = sources,
            Targets = targets,
        });

        var positionHeavy = Strategy().Assign(new AssignmentRequest
        {
            Features =
            [
                new FeatureParameter { FeatureId = "title", Weight = 0.1 },
                new FeatureParameter { FeatureId = "position", Weight = 10.0 },
            ],
            Sources = sources,
            Targets = targets,
        });

        titleHeavy.Pairs.Should().ContainSingle().Which.TargetIndex.Should().Be(0);
        positionHeavy.Pairs.Should().ContainSingle().Which.TargetIndex.Should().Be(1);
    }

    [Test]
    public void ADisabledFeatureDoesNotParticipate()
    {
        var result = Strategy().Assign(new AssignmentRequest
        {
            Features =
            [
                new FeatureParameter { FeatureId = "title", Enabled = false },
                new FeatureParameter { FeatureId = "position" },
            ],
            Sources = [Row("Completely Wrong Name", position: 1)],
            Targets = [Row("Alpha Part", position: 1)],
        });

        result.Pairs.Should().ContainSingle().Which.Distance.Should().Be(
            0.0,
            "with the title feature disabled only the agreeing position speaks");
    }

    [Test]
    public void TheLengthFeatureKeepsItsGraceAndItsDeclarableCap()
    {
        // DistanceCalculator.cs:42-48: |difference| minus a 10-second grace, as a ratio of the cap.
        var strategy = Strategy();

        var inGrace = strategy.Assign(new AssignmentRequest
        {
            Features = AllFeatures("length"),
            Sources = [Row("A", seconds: 100)],
            Targets = [Row("A", seconds: 108)],
        });

        var outside = strategy.Assign(new AssignmentRequest
        {
            Features = AllFeatures("length"),
            Sources = [Row("A", seconds: 100)],
            Targets = [Row("A", seconds: 125)],
        });

        inGrace.Pairs[0].Distance.Should().Be(0.0, "an eight-second difference is inside the grace");
        outside.Pairs[0].Distance.Should().BeApproximately(0.5, 1e-9, "fifteen graceless seconds against the 30-second cap");
    }

    [Test]
    public void TheYearFeatureScalesDisagreementByDistanceFromThePresent()
    {
        var strategy = Strategy();

        var request = new AssignmentRequest
        {
            Features = AllFeatures("year"),
            Sources = [Row("A") with { Year = 2020 }],
            Targets = [Row("A") with { Year = 2023 }],
        };

        var result = strategy.Assign(request);

        // |2020-2023| = 3 against |2026-2023| = 3: a full penalty, because the clock says 2026.
        result.Pairs[0].Distance.Should().BeApproximately(1.0, 1e-9);
    }

    [Test]
    public void AnIdentifierMismatchIsAStrongSignal()
    {
        var stated = ExternalId.Of("cat", 1);
        var current = ExternalId.Of("cat", 2);

        var result = Strategy().Assign(new AssignmentRequest
        {
            Features =
            [
                new FeatureParameter { FeatureId = "title", Weight = 1.0 },
                new FeatureParameter { FeatureId = "identifier", Weight = 10.0 },
            ],
            Sources = [Row("Alpha Part") with { ExternalIds = [stated] }],
            Targets =
            [
                Row("Alpha Part") with { ExternalIds = [current] },
                Row("Alpha Parts") with { ExternalIds = [stated] },
            ],
        });

        result.Pairs.Should().ContainSingle().Which.TargetIndex.Should().Be(
            1,
            "a stated identifier outweighs a marginally better title, at the declared weight");
    }

    [Test]
    public void AnUnpublishedFeatureIsALoadFailureNamingTheCatalog()
    {
        var act = () => Strategy().Assign(new AssignmentRequest
        {
            Features = AllFeatures("no-such-feature"),
            Sources = [Row("A")],
            Targets = [Row("A")],
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no-such-feature*")
            .WithMessage("*unit-distance*");
    }

    [Test]
    public void TheDistanceOperatorsKeepTheirPortedSemantics()
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);

        var strings = new DistanceAccumulator(weights);
        strings.AddString("s", "The Title!", "the title");
        strings.NormalizedDistance().Should().Be(0.0, "cleaning strips case and punctuation before comparing");

        var numbers = new DistanceAccumulator(weights);
        numbers.AddNumber("n", 1, 3);
        numbers.Penalties["n"].Should().HaveCount(2, "each whole unit of disagreement is its own penalty");

        var ratio = new DistanceAccumulator(weights);
        ratio.AddRatio("r", 45, 30);
        ratio.NormalizedDistance().Should().Be(1.0, "the ratio clamps at its cap");

        var priority = new DistanceAccumulator(weights);
        priority.AddPriority("p", "second", ["first", "second", "third", "fourth"]);
        priority.NormalizedDistance().Should().BeApproximately(0.25, 1e-9, "position over list length");

        var equality = new DistanceAccumulator(weights);
        equality.AddEquality("e", "missing", ["present", "listed"]);
        equality.NormalizedDistance().Should().Be(1.0);

        var flag = new DistanceAccumulator(weights);
        flag.AddBool("b", mismatch: false);
        flag.NormalizedDistance().Should().Be(0.0);
    }

    [Test]
    public void TheRegistryRefusesAnUnknownStrategyNamingWhatExists()
    {
        var registry = MatchStrategyRegistry.CreateDefault(Clock);

        var act = () => registry.Resolve<IUnitAssignmentStrategy>(
            MatchStrategyRoles.UnitAssignment,
            "no-such-strategy");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no-such-strategy*")
            .WithMessage("*assignment-over-features*");
    }

    [Test]
    public void TheRegistryRefusesAStrategyResolvedIntoTheWrongRoleSurface()
    {
        var registry = MatchStrategyRegistry.CreateDefault(Clock);

        var act = () => registry.Resolve<IUnitAssignmentStrategy>(
            MatchStrategyRoles.EntryResolution,
            "layered-key-lookup");

        act.Should().Throw<InvalidOperationException>().WithMessage("*IUnitAssignmentStrategy*");
    }

    /// <summary>
    /// The assignment strategy is derived from whether the kind's units can span, not named by a string.
    /// </summary>
    /// <remarks>
    /// What this replaces asserted that a strategy binding naming <c>assignment-over-features</c> produced
    /// that strategy — a test that the engine could read back a string it had just been handed. The fact was
    /// always derivable from the unit rules sitting next to the binding, so it is derived, and these two
    /// cases pin both sides of the derivation rather than one side of a lookup.
    /// </remarks>
    [Test]
    public void AKindWhoseUnitsCannotSpanGetsNoAssignmentStrategy()
    {
        var matcher = MatchEngineFixtures.Matcher(
            MatchEngineFixtures.Declaration(),
            new MatchEngineFixtures.StubReader());

        matcher.UnitAssignment.Should().BeNull(
            "every unit rule reads SpanExpansion.None, so no release can cover more than one unit and there "
            + "is no assignment problem to pose");
    }

    [Test]
    public void AKindWhoseUnitsCanSpanGetsTheAssignmentStrategy()
    {
        var declaration = MatchEngineFixtures.Declaration();
        var spanning = declaration with
        {
            Units = [.. declaration.Units.Select(static rule => rule with
            {
                Expansion = SpanExpansion.SequenceMembers,
            })],
        };

        var matcher = MatchEngineFixtures.Matcher(spanning, new MatchEngineFixtures.StubReader());

        matcher.UnitAssignment.Should().NotBeNull();
        matcher.UnitAssignment!.StrategyId.Should().Be("assignment-over-features");
    }
}
