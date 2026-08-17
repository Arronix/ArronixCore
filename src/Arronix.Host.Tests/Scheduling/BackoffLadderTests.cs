using System.Linq;
using Arronix.Host.Scheduling;
using FluentAssertions;

namespace Arronix.Host.Tests.Scheduling;

/// <summary>
/// The ladder and its jitter.
/// </summary>
/// <remarks>
/// The jitter sample is an argument rather than a random source, so these can assert the exact delay a given
/// attempt produces. Full jitter is the property being asserted: a delay lands anywhere in the window rather
/// than clustering at its end, which is the only variant that fully decorrelates several extensions retrying
/// one dead remote after a shared outage.
/// </remarks>
[TestFixture]
internal sealed class BackoffLadderTests
{
    [Test]
    public void TheLadderIsTheOneAllFourSurveyedApplicationsUse()
        => BackoffLadder.Periods.Should().Equal(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24));

    [Test]
    public void TheFirstFailureWaitsNothing()
        => BackoffLadder.PeriodFor(1).Should().Be(TimeSpan.Zero);

    [TestCase(1, 0)]
    [TestCase(2, 1)]
    [TestCase(3, 5)]
    [TestCase(4, 15)]
    [TestCase(5, 30)]
    [TestCase(6, 60)]
    public void EachAttemptClimbsOneRung(int attempt, int expectedMinutes)
        => BackoffLadder.PeriodFor(attempt).Should().Be(TimeSpan.FromMinutes(expectedMinutes));

    [Test]
    public void PastTheTopOfTheLadderTheWaitStopsGrowing()
    {
        BackoffLadder.PeriodFor(10).Should().Be(TimeSpan.FromHours(24));
        BackoffLadder.PeriodFor(1000).Should().Be(TimeSpan.FromHours(24));
    }

    [Test]
    public void AnAttemptBelowOneIsTreatedAsTheFirst()
        => BackoffLadder.PeriodFor(0).Should().Be(TimeSpan.Zero);

    [Test]
    public void JitterIsFullRatherThanAdditive()
    {
        // Full jitter multiplies the rung; additive jitter would add to it and could never produce a delay
        // shorter than the rung itself. A sample of zero proving a delay of zero is the discriminating case.
        BackoffLadder.Delay(3, 0d).Should().Be(TimeSpan.Zero);
        BackoffLadder.Delay(3, 0.5d).Should().Be(TimeSpan.FromMinutes(2.5));
        BackoffLadder.Delay(3, 0.999d).Should().BeLessThan(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void EveryDelayLandsInsideItsWindow()
    {
        foreach (var attempt in Enumerable.Range(1, 12))
        {
            foreach (var sample in new[] { 0d, 0.25d, 0.5d, 0.75d, 0.999999d })
            {
                var delay = BackoffLadder.Delay(attempt, sample);

                delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
                delay.Should().BeLessThanOrEqualTo(BackoffLadder.PeriodFor(attempt));
            }
        }
    }

    [TestCase(-0.1d)]
    [TestCase(1d)]
    [TestCase(1.5d)]
    public void ASampleOutsideTheUnitIntervalIsRefused(double sample)
    {
        var act = () => BackoffLadder.Delay(2, sample);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
