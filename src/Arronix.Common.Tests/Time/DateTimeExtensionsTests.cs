using System;
using Arronix.Common.Time;

namespace Arronix.Common.Tests.Time;

/// <summary>
/// Covers the date and time helpers. No test advances time by waiting: the window predicates take the clock
/// they read, so a test supplies a fixed one and asserts against an exact instant.
/// </summary>
[TestFixture]
public class DateTimeExtensionsTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void WithoutTicks_DiscardsTheSubSecondPart()
    {
        var value = new DateTime(2026, 7, 27, 12, 0, 30, DateTimeKind.Utc).AddTicks(1234567);

        Assert.That(
            value.WithoutTicks(),
            Is.EqualTo(new DateTime(2026, 7, 27, 12, 0, 30, DateTimeKind.Utc)));
    }

    [Test]
    public void WithoutTicks_PreservesTheKind()
    {
        var value = new DateTime(2026, 7, 27, 12, 0, 30, DateTimeKind.Local).AddTicks(999);

        Assert.That(value.WithoutTicks().Kind, Is.EqualTo(DateTimeKind.Local));
    }

    [Test]
    public void WithTicksFrom_AdoptsTheSubSecondPartOfTheOtherValue()
    {
        var value = new DateTime(2026, 7, 27, 12, 0, 30, DateTimeKind.Utc).AddTicks(1);
        var other = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(7654321);

        var result = value.WithTicksFrom(other);

        Assert.That(result.Ticks % TimeSpan.TicksPerSecond, Is.EqualTo(7654321));
        Assert.That(result.WithoutTicks(), Is.EqualTo(value.WithoutTicks()));
    }

    [Test]
    public void InNext_AcceptsAnInstantInsideTheWindow()
    {
        var clock = new FixedTimeProvider(Now);

        Assert.That(Now.AddMinutes(30).InNext(TimeSpan.FromHours(1), clock), Is.True);
    }

    [Test]
    public void InNext_AcceptsBothBoundaries()
    {
        var clock = new FixedTimeProvider(Now);

        Assert.That(Now.InNext(TimeSpan.FromHours(1), clock), Is.True);
        Assert.That(Now.AddHours(1).InNext(TimeSpan.FromHours(1), clock), Is.True);
    }

    [Test]
    public void InNext_RejectsThePastAndBeyondTheWindow()
    {
        var clock = new FixedTimeProvider(Now);

        Assert.That(Now.AddSeconds(-1).InNext(TimeSpan.FromHours(1), clock), Is.False);
        Assert.That(Now.AddHours(2).InNext(TimeSpan.FromHours(1), clock), Is.False);
    }

    [Test]
    public void InLast_AcceptsAnInstantInsideTheWindow()
    {
        var clock = new FixedTimeProvider(Now);

        Assert.That(Now.AddMinutes(-30).InLast(TimeSpan.FromHours(1), clock), Is.True);
    }

    [Test]
    public void InLast_RejectsTheFutureAndBeforeTheWindow()
    {
        var clock = new FixedTimeProvider(Now);

        Assert.That(Now.AddSeconds(1).InLast(TimeSpan.FromHours(1), clock), Is.False);
        Assert.That(Now.AddHours(-2).InLast(TimeSpan.FromHours(1), clock), Is.False);
    }

    [Test]
    public void WindowPredicates_ReadTheClockOnceSoTheBoundaryCannotMove()
    {
        // A clock that advances on every reading stands in for the wall clock the implementation this
        // replaces read twice per call. Reading it once means an instant sitting exactly on "now" satisfies
        // the predicate, which is what makes the boundary assertions above meaningful rather than flaky.
        var clock = new AdvancingTimeProvider(Now, TimeSpan.FromSeconds(1));

        Assert.That(Now.InNext(TimeSpan.FromHours(1), clock), Is.True);
        Assert.That(clock.Readings, Is.EqualTo(1));
    }

    [Test]
    public void WindowPredicates_RejectAMissingClock()
    {
        Assert.That(() => Now.InNext(TimeSpan.FromHours(1), null!), Throws.ArgumentNullException);
        Assert.That(() => Now.InLast(TimeSpan.FromHours(1), null!), Throws.ArgumentNullException);
    }

    [Test]
    public void WindowPredicates_RejectANegativeWindow()
    {
        var clock = new FixedTimeProvider(Now);

        Assert.That(
            () => Now.InNext(TimeSpan.FromHours(-1), clock),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => Now.InLast(TimeSpan.FromHours(-1), clock),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// A clock stopped at one instant.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than taken from the time-testing package, which the test project does not yet
    /// reference; the subsystem that needs timers will add it and this can then be deleted.
    /// </remarks>
    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    /// <summary>
    /// A clock that moves forward every time it is read, and counts the readings.
    /// </summary>
    private sealed class AdvancingTimeProvider(DateTime start, TimeSpan step) : TimeProvider
    {
        public int Readings { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            var reading = start.Add(step * Readings);
            Readings++;

            return new DateTimeOffset(reading, TimeSpan.Zero);
        }
    }
}
