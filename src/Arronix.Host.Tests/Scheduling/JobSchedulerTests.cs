using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Host.Configuration;
using Arronix.Host.Scheduling;
using Arronix.Host.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

// Error, plugin and shape contracts are experimental.
#pragma warning disable ARX0003
#pragma warning disable ARX0013
#pragma warning disable ARX0014

namespace Arronix.Host.Tests.Scheduling;

/// <summary>
/// The scheduler driven a pass at a time, against a clock the test controls.
/// </summary>
/// <remarks>
/// Every assertion here would otherwise need real time to pass — a fourth attempt lands fifteen minutes out,
/// a daily job runs tomorrow. Driving the injected clock is what turns those from tests nobody writes into
/// tests that run in a millisecond.
/// </remarks>
[TestFixture]
internal sealed class JobSchedulerTests
{
    private static readonly PluginId Owner = PluginId.FromString("fixture");

    private sealed record Harness(
        JobScheduler Scheduler,
        BackgroundTaskRegistry Registry,
        JobQueue Queue,
        ConcurrencyGovernor Governor,
        FakeTimeProvider Clock) : IDisposable
    {
        public void Dispose() => Governor.Dispose();
    }

    private static Harness Build(
        SchedulerOptions? options = null,
        double jitter = 0.5d)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var settings = options ?? new SchedulerOptions { MaxConcurrentJobs = 4 };
        var queue = new JobQueue();
        var registry = new BackgroundTaskRegistry(queue, clock);
        var governor = new ConcurrencyGovernor(TestOptions.Of(settings));

        var scheduler = new JobScheduler(
            registry,
            queue,
            governor,
            new FailureClassifier(clock),
            clock,
            TestOptions.Of(settings),
            NullLogger<JobScheduler>.Instance,
            () => jitter);

        return new Harness(scheduler, registry, queue, governor, clock);
    }

    private static async Task SettleAsync(Harness harness)
    {
        // The scheduler starts work and does not await it, which is what lets one pass start several jobs.
        // Waiting for the in-flight count to fall back to zero is how a test observes the work finishing
        // without reaching into the scheduler's internals.
        for (var attempt = 0; attempt < 200 && harness.Scheduler.InFlight > 0; attempt++)
        {
            await Task.Delay(5);
        }
    }

    [Test]
    public async Task AnIntervalJobRunsOnItsFirstPassAndThenWaitsItsInterval()
    {
        using var harness = Build();
        var job = FakeJob.Succeeding("interval");

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "every PT15M", null);

        (await harness.Scheduler.TickAsync()).Should().Be(1);
        await SettleAsync(harness);

        (await harness.Scheduler.TickAsync()).Should().Be(0);

        harness.Clock.Advance(TimeSpan.FromMinutes(15));

        (await harness.Scheduler.TickAsync()).Should().Be(1);
        await SettleAsync(harness);

        job.Runs.Should().Be(2);
    }

    [Test]
    public async Task AManualJobNeverRunsUntilItIsTriggered()
    {
        using var harness = Build();
        var job = FakeJob.Succeeding("manual");

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);

        harness.Clock.Advance(TimeSpan.FromDays(7));

        (await harness.Scheduler.TickAsync()).Should().Be(0);

        (await harness.Registry.TriggerJobAsync("manual")).Should().BeTrue();

        (await harness.Scheduler.TickAsync()).Should().Be(1);
        await SettleAsync(harness);

        job.Runs.Should().Be(1);
    }

    [Test]
    public async Task AStartupJobWaitsUntilExtensionsHaveBeenAdmitted()
    {
        using var harness = Build();
        var job = FakeJob.Succeeding("startup");

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "startup", null);

        (await harness.Scheduler.TickAsync()).Should().Be(0);

        harness.Scheduler.ReleaseStartupJobs();

        (await harness.Scheduler.TickAsync()).Should().Be(1);
        await SettleAsync(harness);

        // Once, and only once.
        (await harness.Scheduler.TickAsync()).Should().Be(0);
        job.Runs.Should().Be(1);
    }

    [Test]
    public async Task TheGlobalCeilingBoundsHowMuchStartsInOnePass()
    {
        using var harness = Build(new SchedulerOptions { MaxConcurrentJobs = 2 });
        var gate = new TaskCompletionSource();

        foreach (var index in Enumerable.Range(1, 4))
        {
            var job = new FakeJob(
                $"job-{index}",
                async (_, _) =>
                {
                    await gate.Task;
                    return new JobExecutionResult(true);
                });

            harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
            (await harness.Registry.TriggerJobAsync($"job-{index}")).Should().BeTrue();
        }

        (await harness.Scheduler.TickAsync()).Should().Be(2);
        harness.Scheduler.InFlight.Should().Be(2);

        gate.SetResult();
        await SettleAsync(harness);

        (await harness.Scheduler.TickAsync()).Should().Be(2);
        gate.Task.IsCompleted.Should().BeTrue();
        await SettleAsync(harness);
    }

    [Test]
    public async Task AJobDoesNotExceedTheConcurrencyItDeclaredForItself()
    {
        using var harness = Build(new SchedulerOptions { MaxConcurrentJobs = 8 });
        var gate = new TaskCompletionSource();

        var job = new FakeJob(
            "single",
            async (_, _) =>
            {
                await gate.Task;
                return new JobExecutionResult(true);
            },
            maxConcurrency: 1);

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);

        await harness.Registry.TriggerJobAsync("single");
        await harness.Registry.TriggerJobAsync("single");

        (await harness.Scheduler.TickAsync()).Should().Be(1);
        harness.Queue.Count.Should().Be(1);

        gate.SetResult();
        await SettleAsync(harness);
    }

    [Test]
    public async Task AFailedJobIsRequeuedOntoItsRungOfTheLadder()
    {
        using var harness = Build(jitter: 0.999999d);
        var job = FakeJob.Throwing("failing", new ArronixException(CoreErrorCode.IndexerConnectionFailed, "no answer"));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("failing");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        var requeued = harness.Queue.Snapshot().Should().ContainSingle().Subject;

        requeued.Attempt.Should().Be(2);

        // Attempt one failed, so the wait is the first rung — zero — and the entry is due immediately.
        requeued.NotBefore.Should().Be(harness.Clock.GetUtcNow());
    }

    [Test]
    public async Task EachFurtherFailureClimbsTheLadder()
    {
        using var harness = Build(jitter: 0.999999d);
        var job = FakeJob.Throwing("failing", new ArronixException(CoreErrorCode.IndexerConnectionFailed, "no answer"));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("failing");

        // Attempt 1 fails and waits nothing; attempt 2 fails and waits (almost) a minute.
        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);
        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        var requeued = harness.Queue.Snapshot().Should().ContainSingle().Subject;

        requeued.Attempt.Should().Be(3);
        (requeued.NotBefore - harness.Clock.GetUtcNow())
            .Should().BeCloseTo(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(100));
    }

    [Test]
    public async Task JitterShortensTheWaitRatherThanLengtheningIt()
    {
        using var harness = Build(jitter: 0d);
        var job = FakeJob.Throwing("failing", new ArronixException(CoreErrorCode.IndexerConnectionFailed, "no answer"));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("failing");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);
        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        // Full jitter, so a sample of zero means no wait at all even on the minute rung. Additive jitter
        // could never produce this.
        harness.Queue.Snapshot()[0].NotBefore.Should().Be(harness.Clock.GetUtcNow());
    }

    [Test]
    public async Task APermanentFailureIsNotRetried()
    {
        using var harness = Build();
        var job = FakeJob.Throwing("failing", new InvalidOperationException("this will never work"));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("failing");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        harness.Queue.Count.Should().Be(0);
        job.Runs.Should().Be(1);
    }

    [Test]
    public async Task AConfigurationFailureIsNotRetriedEither()
    {
        using var harness = Build();
        var job = FakeJob.Throwing("failing", new ArronixException(CoreErrorCode.MissingConfiguration, "no key"));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("failing");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        harness.Queue.Count.Should().Be(0);
    }

    [Test]
    public async Task RetryingStopsAtTheConfiguredAttemptCeiling()
    {
        using var harness = Build(new SchedulerOptions { MaxConcurrentJobs = 4, MaxAttempts = 3 }, jitter: 0d);
        var job = FakeJob.Throwing("failing", new ArronixException(CoreErrorCode.IndexerConnectionFailed, "no answer"));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("failing");

        for (var pass = 0; pass < 5; pass++)
        {
            await harness.Scheduler.TickAsync();
            await SettleAsync(harness);
        }

        harness.Queue.Count.Should().Be(0);
        job.Runs.Should().Be(3);
    }

    [Test]
    public async Task ARemotesOwnRetryDeadlineIsUsedInPlaceOfTheLadder()
    {
        using var harness = Build(jitter: 0d);
        var job = new FakeJob(
            "limited",
            (_, _) => Task.FromResult(new JobExecutionResult(
                false,
                "slow down",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [WellKnownJobResults.FailureClass] = "rate-limited",
                    [WellKnownJobResults.RetryAfter] = "PT90S",
                })));

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("limited");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        (harness.Queue.Snapshot()[0].NotBefore - harness.Clock.GetUtcNow())
            .Should().Be(TimeSpan.FromSeconds(90));
    }

    [Test]
    public async Task WorkForAJobThatHasGoneAwayIsDroppedRatherThanRetriedForever()
    {
        using var harness = Build();

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, FakeJob.Succeeding("going"), "manual", null);
        await harness.Registry.TriggerJobAsync("going");

        harness.Queue.Count.Should().Be(1);

        // Unregistering drops the entry directly; queue an orphan the hard way to prove the pass also copes.
        harness.Registry.RegisterJob(Owner, CapabilitySet.None, FakeJob.Succeeding("other"), "manual", null);
        var orphan = harness.Registry.Enqueue(
            harness.Registry.Find("other")!,
            null,
            harness.Clock.GetUtcNow(),
            "correlation");

        harness.Registry.UnregisterJob("going");
        harness.Registry.UnregisterJob("other");

        (await harness.Scheduler.TickAsync()).Should().Be(0);
        harness.Queue.Count.Should().Be(0);
        orphan.JobId.Should().Be("other");
    }

    [Test]
    public async Task EveryRunCarriesTheCorrelationIdentifierAsATypedMemberOfItsContext()
    {
        using var harness = Build();
        string? seen = null;

        var job = new FakeJob(
            "correlated",
            (context, _) =>
            {
                seen = context.CorrelationId;
                return Task.FromResult(new JobExecutionResult(true));
            });

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("correlated");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        seen.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ACallerSuppliedCorrelationIdentifierReachesTheRunUnchanged()
    {
        using var harness = Build();
        string? seen = null;

        var job = new FakeJob(
            "continued",
            (context, _) =>
            {
                seen = context.CorrelationId;
                return Task.FromResult(new JobExecutionResult(true));
            });

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("continued", parameters: null, "an-existing-operation");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        seen.Should().Be("an-existing-operation");
    }

    [Test]
    public async Task TheMediaKindAJobWasRegisteredForReachesTheRunAsATypedMember()
    {
        using var harness = Build();
        MediaKindId? seen = null;

        var job = new FakeJob(
            "kinded",
            (context, _) =>
            {
                seen = context.MediaKind;
                return Task.FromResult(new JobExecutionResult(true));
            });

        harness.Registry.RegisterJob(
            Owner,
            CapabilitySet.None,
            job,
            "manual",
            MediaKindId.FromString("tv"));

        await harness.Registry.TriggerJobAsync("kinded");

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        seen.Should().Be(MediaKindId.FromString("tv"));
    }

    [Test]
    public async Task TheItemsAnEnqueueWasScopedToReachTheRunAsATypedMember()
    {
        using var harness = Build();
        IReadOnlyList<MediaItemRef>? seen = null;

        var job = new FakeJob(
            "scoped",
            (context, _) =>
            {
                seen = context.Items;
                return Task.FromResult(new JobExecutionResult(true));
            });

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);

        var wanted = new MediaItemRef(
            MediaKindId.FromString("tv"),
            MediaLevelId.FromString("episode"),
            MediaItemId.FromInt64(42));

        harness.Registry.Enqueue(
            harness.Registry.Find("scoped")!,
            parameters: null,
            harness.Clock.GetUtcNow(),
            correlationId: null,
            [wanted]);

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        seen.Should().ContainSingle().Which.Should().Be(wanted);
    }

    [Test]
    public async Task ParametersCarryOnlyWhatTheCallerPutInThem()
    {
        using var harness = Build();
        IReadOnlyDictionary<string, object>? seen = null;

        var job = new FakeJob(
            "parameterized",
            (context, _) =>
            {
                seen = context.Parameters;
                return Task.FromResult(new JobExecutionResult(true));
            });

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", MediaKindId.FromString("tv"));

        await harness.Registry.TriggerJobAsync(
            "parameterized",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["depth"] = 3 });

        await harness.Scheduler.TickAsync();
        await SettleAsync(harness);

        // No reserved keys: the platform's own facts are typed members, so nothing the job did not ask for
        // appears in its bag.
        seen.Should().ContainSingle().Which.Key.Should().Be("depth");
    }

    [Test]
    public async Task DrainingWaitsForWorkInFlightAndReportsNothingWhenItStops()
    {
        using var harness = Build();
        var gate = new TaskCompletionSource();

        var job = new FakeJob("polite", async (_, _) => { await gate.Task; return new JobExecutionResult(true); });

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("polite");

        (await harness.Scheduler.TickAsync()).Should().Be(1);

        var drain = harness.Scheduler.DrainAsync();

        gate.SetResult();

        (await drain).Should().BeEmpty();
    }

    [Test]
    public async Task AJobStillRunningWhenItsDeadlinePassesIsReportedRatherThanWaitedOnForever()
    {
        using var harness = Build();
        var gate = new TaskCompletionSource();

        var job = new FakeJob("rude", async (_, _) => { await gate.Task; return new JobExecutionResult(true); })
        {
            ShutdownDeadline = TimeSpan.FromSeconds(5),
        };

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("rude");

        (await harness.Scheduler.TickAsync()).Should().Be(1);

        var drain = harness.Scheduler.DrainAsync();

        harness.Clock.Advance(TimeSpan.FromSeconds(6));

        (await drain).Should().ContainSingle().Which.Should().Be("rude");

        gate.SetResult();
        await SettleAsync(harness);
    }

    [Test]
    public async Task AJobCannotHoldShutdownOpenByDeclaringAnAbsurdDeadline()
    {
        using var harness = Build();
        var gate = new TaskCompletionSource();

        var job = new FakeJob("greedy", async (_, _) => { await gate.Task; return new JobExecutionResult(true); })
        {
            ShutdownDeadline = TimeSpan.FromDays(30),
        };

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, job, "manual", null);
        await harness.Registry.TriggerJobAsync("greedy");

        (await harness.Scheduler.TickAsync()).Should().Be(1);

        var drain = harness.Scheduler.DrainAsync();

        harness.Clock.Advance(JobScheduler.MaxShutdownDeadline + TimeSpan.FromSeconds(1));

        (await drain).Should().ContainSingle().Which.Should().Be("greedy");

        gate.SetResult();
        await SettleAsync(harness);
    }

    [Test]
    public async Task DrainingWithNothingInFlightReportsNothing()
    {
        using var harness = Build();
        (await harness.Scheduler.DrainAsync()).Should().BeEmpty();
    }

    [Test]
    public void RegisteringAJobWithAScheduleTheGrammarRefusesFails()
    {
        using var harness = Build();

        var act = () => harness.Registry.RegisterJob(
            Owner,
            CapabilitySet.None,
            FakeJob.Succeeding("bad"),
            "cron 0 3 * * *",
            null);

        act.Should().Throw<ArronixException>()
            .Which.ErrorCode.Should().Be(CoreErrorCode.JobSchedulingFailed);
    }

    [Test]
    public void RegisteringTwoJobsUnderOneIdentifierFails()
    {
        using var harness = Build();

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, FakeJob.Succeeding("twice"), "manual", null);

        var act = () => harness.Registry.RegisterJob(
            Owner,
            CapabilitySet.None,
            FakeJob.Succeeding("twice"),
            "manual",
            null);

        act.Should().Throw<ArronixException>();
    }

    [Test]
    public async Task TriggeringAJobThatIsNotRegisteredReportsThatRatherThanThrowing()
    {
        using var harness = Build();
        (await harness.Registry.TriggerJobAsync("never-registered")).Should().BeFalse();
    }

    [Test]
    public async Task RemovingAnExtensionRemovesItsJobsAndItsQueuedWork()
    {
        using var harness = Build();

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, FakeJob.Succeeding("owned"), "manual", null);
        await harness.Registry.TriggerJobAsync("owned");

        harness.Registry.RemoveByPlugin(Owner).Should().Be(1);

        harness.Registry.GetAllJobs().Should().BeEmpty();
        harness.Queue.Count.Should().Be(0);
    }

    [Test]
    public async Task HigherPriorityWorkIsConsideredFirst()
    {
        using var harness = Build(new SchedulerOptions { MaxConcurrentJobs = 1 });
        var gate = new TaskCompletionSource();

        var low = new FakeJob("low", async (_, _) => { await gate.Task; return new JobExecutionResult(true); })
        {
            Priority = 1,
        };

        var high = new FakeJob("high", async (_, _) => { await gate.Task; return new JobExecutionResult(true); })
        {
            Priority = 100,
        };

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, low, "manual", null);
        harness.Registry.RegisterJob(Owner, CapabilitySet.None, high, "manual", null);

        await harness.Registry.TriggerJobAsync("low");
        await harness.Registry.TriggerJobAsync("high");

        (await harness.Scheduler.TickAsync()).Should().Be(1);

        high.Runs.Should().Be(1);
        low.Runs.Should().Be(0);

        gate.SetResult();
        await SettleAsync(harness);
    }

    [Test]
    public void EveryRegistrationIsDescribableForAnOperator()
    {
        using var harness = Build();

        harness.Registry.RegisterJob(Owner, CapabilitySet.None, FakeJob.Succeeding("described"), "every PT5M", null);

        var view = harness.Registry.Describe().Should().ContainSingle().Subject;

        view.JobId.Should().Be("described");
        view.Owner.Should().Be("fixture");
        view.Schedule.Should().Be("every PT5M");
        view.NextRun.Should().NotBeNull();
    }
}
