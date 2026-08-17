using Arronix.Host.Scheduling;
using FluentAssertions;

namespace Arronix.Host.Tests.Scheduling;

/// <summary>
/// The schedule grammar: four forms, and a refusal for everything else.
/// </summary>
/// <remarks>
/// The refusals matter more than the acceptances. A schedule guessed wrong runs forever at a rate nobody
/// chose, and the extension author is the only person who can say which of several plausible readings was
/// meant — so anything outside the grammar is a load failure rather than a lenient reinterpretation.
/// </remarks>
[TestFixture]
internal sealed class JobScheduleParserTests
{
    [TestCase("manual", JobScheduleKind.Manual)]
    [TestCase("MANUAL", JobScheduleKind.Manual)]
    [TestCase("  startup  ", JobScheduleKind.Startup)]
    [TestCase("every PT15M", JobScheduleKind.Every)]
    [TestCase("every 00:15", JobScheduleKind.Every)]
    [TestCase("daily 03:30", JobScheduleKind.Daily)]
    public void TheFourFormsParse(string text, JobScheduleKind expected)
    {
        JobScheduleParser.TryParse(text, out var schedule, out var error).Should().BeTrue(error);
        schedule.Kind.Should().Be(expected);
        schedule.Raw.Should().Be(text.Trim());
    }

    [TestCase("every PT15M", 15)]
    [TestCase("every 00:15", 15)]
    [TestCase("every PT1H", 60)]
    [TestCase("every 02:00", 120)]
    public void AnIntervalIsReadInBothNotations(string text, int expectedMinutes)
    {
        JobScheduleParser.TryParse(text, out var schedule, out _).Should().BeTrue();
        schedule.Interval.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Test]
    public void ADailyTimeIsReadOnATwentyFourHourClock()
    {
        JobScheduleParser.TryParse("daily 23:45", out var schedule, out _).Should().BeTrue();
        schedule.TimeOfDay.Should().Be(new TimeOnly(23, 45));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("cron 0 3 * * *")]
    [TestCase("hourly")]
    [TestCase("every")]
    [TestCase("every nonsense")]
    [TestCase("every PT0S")]
    [TestCase("every -00:15")]
    [TestCase("daily 25:00")]
    [TestCase("daily 3:30")]
    [TestCase("daily")]
    public void AnythingOutsideTheGrammarIsRefusedWithAReason(string? text)
    {
        JobScheduleParser.TryParse(text, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void CronIsRefusedDeliberately()
    {
        JobScheduleParser.TryParse("cron 5 4 * * *", out _, out var error).Should().BeFalse();
        error.Should().Contain("manual");
    }

    [Test]
    public void AManualScheduleIsNeverDue()
    {
        JobScheduleParser.TryParse("manual", out var schedule, out _).Should().BeTrue();
        schedule.NextRun(DateTimeOffset.UnixEpoch, null).Should().BeNull();
    }

    [Test]
    public void AStartupScheduleIsDueOnceAndThenNever()
    {
        JobScheduleParser.TryParse("startup", out var schedule, out _).Should().BeTrue();

        var now = DateTimeOffset.UnixEpoch;

        schedule.NextRun(now, null).Should().Be(now);
        schedule.NextRun(now, now).Should().BeNull();
    }

    [Test]
    public void AnIntervalScheduleIsDueImmediatelyTheFirstTime()
    {
        JobScheduleParser.TryParse("every PT15M", out var schedule, out _).Should().BeTrue();

        var now = DateTimeOffset.UnixEpoch;

        schedule.NextRun(now, null).Should().Be(now);
        schedule.NextRun(now, now).Should().Be(now.AddMinutes(15));
    }

    [Test]
    public void ADailyScheduleRollsToTomorrowOnceTodaysTimeHasPassed()
    {
        JobScheduleParser.TryParse("daily 03:30", out var schedule, out _).Should().BeTrue();

        var beforeIt = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var afterIt = new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero);

        schedule.NextRun(beforeIt, null).Should().Be(new DateTimeOffset(2026, 1, 1, 3, 30, 0, TimeSpan.Zero));
        schedule.NextRun(afterIt, null).Should().Be(new DateTimeOffset(2026, 1, 2, 3, 30, 0, TimeSpan.Zero));
    }
}
