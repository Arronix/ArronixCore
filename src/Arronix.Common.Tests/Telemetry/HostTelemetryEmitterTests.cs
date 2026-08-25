using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Common.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Tests.Telemetry;

/// <summary>
/// What the telemetry pipeline does on the caller's thread, what it does on its own, and what it will not
/// let a contribution do.
/// </summary>
[TestFixture]
public class HostTelemetryEmitterTests
{
    private static readonly PluginId Movies = PluginId.FromString("a.movies");

    private static readonly PluginId Television = PluginId.FromString("b.television");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ASecretIsMaskedBeforeAnythingAnExtensionContributedSeesItAsync()
    {
        var enricher = new RecordingEnricher();
        var filter = new RecordingFilter();
        var sink = new RecordingSink();
        var contributions = new StubContributions().Add(Movies, enricher).Add(Movies, filter);

        await using var pipeline = Pipeline(sinks: [sink], contributions: contributions, rules: Secret());

        pipeline.Emitter.Emit(Event("connecting with token=abc123 now"), Movies);
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(enricher.Seen.Single().Message, Does.Not.Contain("abc123"));
            Assert.That(filter.Seen.Single().Message, Does.Not.Contain("abc123"));
            Assert.That(sink.Received.Single().Message, Does.Not.Contain("abc123"));
            Assert.That(sink.Received.Single().Message, Does.Contain("connecting with token="));
        });
    }

    [Test]
    public async Task WhatAnEnricherAddsIsRedactedTooAsync()
    {
        var sink = new RecordingSink();
        var enricher = new RecordingEnricher(seen => seen with
        {
            Tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = "https://x/?token=zzz999" },
        });

        await using var pipeline = Pipeline(enrichers: [enricher], sinks: [sink], rules: Secret());

        pipeline.Emitter.Emit(Event("nothing to hide"));
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.That(sink.Received.Single().Tags["url"], Does.Not.Contain("zzz999"));
    }

    [Test]
    public async Task AnEnricherCannotChangeWhatTheEventIsAsync()
    {
        var sink = new RecordingSink();
        var raised = new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
        var identifier = Guid.CreateVersion7();

        var enricher = new RecordingEnricher(seen => seen with
        {
            EventId = Guid.CreateVersion7(),
            Timestamp = raised.AddYears(-5),
            Severity = TelemetrySeverity.Debug,
            Exception = new InvalidOperationException("put back"),
            Tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HostTelemetryEmitter.OriginTag] = Television.ToString(),
            },
        });

        var contributions = new StubContributions().Add(Movies, enricher);
        await using var pipeline = Pipeline(sinks: [sink], contributions: contributions);

        pipeline.Emitter.Emit(
            new TelemetryEvent(identifier, raised, TelemetrySeverity.Warning, "as raised"),
            Movies);

        await pipeline.DrainAsync().ConfigureAwait(false);

        var delivered = sink.Received.Single();

        Assert.Multiple(() =>
        {
            Assert.That(delivered.EventId, Is.EqualTo(identifier), "identity is not an enricher's to change");
            Assert.That(delivered.Timestamp, Is.EqualTo(raised), "nor when it happened");
            Assert.That(delivered.Severity, Is.EqualTo(TelemetrySeverity.Warning), "nor how serious it was");
            Assert.That(delivered.Tags[HostTelemetryEmitter.OriginTag], Is.EqualTo(Movies.ToString()), "nor who raised it");
            Assert.That(delivered.Exception, Is.Null, "and a live exception put back does not travel");
        });
    }

    [Test]
    public async Task AnExtensionSeesItsOwnEventsAndNoOthersAsync()
    {
        var mine = new RecordingEnricher();
        var theirs = new RecordingEnricher();
        var contributions = new StubContributions().Add(Movies, mine).Add(Television, theirs);

        await using var pipeline = Pipeline(contributions: contributions);

        pipeline.Emitter.Emit(Event("mine"), Movies);
        pipeline.Emitter.Emit(Event("the host's"));
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(mine.Seen.Select(seen => seen.Message), Is.EqualTo(new[] {"mine"}));
            Assert.That(theirs.Seen, Is.Empty, "another extension's event is not this one's to read");
            Assert.That(
                contributions.UnscopedContracts,
                Has.None.EqualTo(typeof(ITelemetryEnricher)).And.None.EqualTo(typeof(ITelemetryEventFilter)),
                "leasing every extension to reach one is not scoping");
            Assert.That(contributions.OwnedRequests, Does.Not.Contain(Television));
        });
    }

    [Test]
    public async Task AnExtensionFilterQuietsOnlyItsOwnAndOnlyBelowErrorAsync()
    {
        var sink = new RecordingSink();
        var refusing = new RecordingFilter(answer: false);
        var contributions = new StubContributions().Add(Movies, refusing);

        await using var pipeline = Pipeline(sinks: [sink], contributions: contributions);

        pipeline.Emitter.Emit(Event("its own chatter"), Movies);
        pipeline.Emitter.Emit(Event("its own failure", TelemetrySeverity.Error), Movies);
        pipeline.Emitter.Emit(Event("somebody else's"), Television);
        pipeline.Emitter.Emit(Event("the host's"));
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.That(
            sink.Received.Select(received => received.Message),
            Is.EqualTo(new[] {"its own failure", "somebody else's", "the host's"}),
            "an extension may quiet its own noise, and nothing above it");
    }

    [Test]
    public async Task AHostFilterIsTrustedWithEverythingAsync()
    {
        var sink = new RecordingSink();
        await using var pipeline = Pipeline(filters: [new RecordingFilter(answer: false)], sinks: [sink]);

        pipeline.Emitter.Emit(Event("dropped", TelemetrySeverity.Fatal));
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.That(sink.Received, Is.Empty, "the host's own filters are the host's policy");
    }

    [Test]
    public async Task NoLiveExceptionEverReachesAContributionAsync()
    {
        var enricher = new RecordingEnricher();
        var sink = new RecordingSink();
        var contributions = new StubContributions().Add(Movies, enricher);

        await using var pipeline = Pipeline(sinks: [sink], contributions: contributions);

        pipeline.Emitter.Emit(
            Event("it failed") with { Exception = new InvalidOperationException("the reason") },
            Movies);

        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(enricher.Seen.Single().Exception, Is.Null);
            Assert.That(sink.Received.Single().Exception, Is.Null);
            Assert.That(sink.Received.Single().ExceptionSummary!.Message, Is.EqualTo("the reason"));
            Assert.That(
                sink.Received.Single().ExceptionSummary!.TypeName,
                Is.EqualTo(typeof(InvalidOperationException).FullName));
        });
    }

    [Test]
    public async Task AFailureThatWillNotDescribeItselfIsStillNamedByItsOwnTypeAsync()
    {
        var sink = new RecordingSink();

        await using var pipeline = Pipeline(sinks: [sink]);

        pipeline.Emitter.Emit(Event("it failed") with { Exception = new Unreadable() });

        await pipeline.DrainAsync().ConfigureAwait(false);

        var summary = sink.Received.Single().ExceptionSummary!;

        Assert.Multiple(() =>
        {
            Assert.That(
                summary.TypeName,
                Is.EqualTo(typeof(Unreadable).FullName),
                "a getter that objects to being read says nothing about what was thrown");
            Assert.That(summary.Message, Does.Contain("would not describe itself"));
            Assert.That(sink.Received.Single().Exception, Is.Null);
        });
    }

    [Test]
    public async Task AcceptingRunsNoContributionOnTheCallersThreadAsync()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var blocking = new RecordingEnricher(seen =>
        {
            entered.Set();
            release.Wait(Patience);
            return seen;
        });

        await using var pipeline = Pipeline(enrichers: [blocking]);

        var accepted = Task.Run(() => pipeline.Emitter.Emit(Event("first")));

        Assert.That(
            accepted.Wait(TimeSpan.FromSeconds(2)),
            Is.True,
            "accepting an event must not wait on anything, least of all on contributed code");

        Assert.That(entered.Wait(Patience), Is.True, "and the contribution still runs, on the pump");
        release.Set();
        await pipeline.DrainAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ARepeatedIdentifierIsDeliveredOnceAndCountedAsync()
    {
        var sink = new RecordingSink();
        await using var pipeline = Pipeline(sinks: [sink]);

        var repeated = Event("said twice");
        pipeline.Emitter.Emit(repeated);
        pipeline.Emitter.Emit(repeated);
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(sink.Received, Has.Count.EqualTo(1));
            Assert.That(pipeline.Emitter.Duplicates, Is.EqualTo(1));
        });
    }

    [Test]
    public void AFullQueueDropsAndSaysHowMany()
    {
        // No pump is running, so nothing leaves the queue and the bound is reached deterministically.
        var emitter = Emitter(new TelemetryOptions { QueueCapacity = 4 });

        for (var index = 0; index < 10; index++)
        {
            emitter.Emit(Event($"event {index}"));
        }

        Assert.That(emitter.Dropped, Is.EqualTo(6), "six were evicted, and the pipeline says so");
    }

    [Test]
    public async Task WhatACallerPassesIsCutToSizeAsync()
    {
        var sink = new RecordingSink();
        await using var pipeline = Pipeline(sinks: [sink]);

        var tags = Enumerable.Range(0, 500).ToDictionary(index => $"tag{index}", _ => new string('v', 5_000));

        pipeline.Emitter.Emit(Event(new string('m', 100_000)) with
        {
            Tags = tags,
            Fingerprint = [.. Enumerable.Range(0, 500).Select(index => index.ToString())],
        });

        await pipeline.DrainAsync().ConfigureAwait(false);
        var delivered = sink.Received.Single();

        Assert.Multiple(() =>
        {
            Assert.That(delivered.Message, Has.Length.LessThan(TelemetryLimits.MaxTextLength + 64));
            Assert.That(delivered.Tags, Has.Count.EqualTo(TelemetryLimits.MaxTags));
            Assert.That(delivered.Fingerprint, Has.Count.EqualTo(TelemetryLimits.MaxFingerprint));
            Assert.That(delivered.Tags.Values, Has.All.Length.LessThan(TelemetryLimits.MaxTokenLength + 64));
        });
    }

    [Test]
    public async Task ASinkThatFailsDoesNotStopItsPeersAsync()
    {
        var after = new RecordingSink("after");
        await using var pipeline = Pipeline(sinks: [new ObjectingSink(), after]);

        pipeline.Emitter.Emit(Event("delivered anyway"));
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.That(after.Received, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ASinkThatNeverAnswersIsAbandonedAndItsPeersStillGetTheEventAsync()
    {
        using var release = new ManualResetEventSlim();
        var after = new RecordingSink("after");
        var hanging = new HangingSink(release);

        await using var pipeline = Pipeline(
            sinks: [hanging, after],
            options: new TelemetryOptions { SinkAttempt = TimeSpan.FromMilliseconds(100) });

        pipeline.Emitter.Emit(Event("delivered past the hang"));

        Assert.That(
            await Waited(() => after.Received.Count == 1).ConfigureAwait(false),
            Is.True,
            "the wait on one sink is bounded; the call itself cannot be taken back");

        release.Set();
        await pipeline.DrainAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ShutdownDrainsWhatWasAcceptedAndFlushesEverySinkAsync()
    {
        var host = new RecordingSink("host");
        var contributed = new RecordingSink("contributed");
        var contributions = new StubContributions().Add(Movies, contributed);

        var pipeline = Pipeline(sinks: [host], contributions: contributions);

        pipeline.Emitter.Emit(Event("last words"));

        await pipeline.Emitter.DrainAsync().ConfigureAwait(false);
        await pipeline.Emitter.FlushContributedAsync().ConfigureAwait(false);
        await pipeline.Emitter.FlushHostAsync().ConfigureAwait(false);
        await pipeline.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(host.Received.Select(one => one.Message), Is.EqualTo(new[] {"last words"}));
            Assert.That(contributed.Received.Select(one => one.Message), Is.EqualTo(new[] {"last words"}));
            Assert.That(host.Flushes, Is.EqualTo(1));
            Assert.That(contributed.Flushes, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnEventRaisedAfterTheDrainIsRefusedAndCountedAsync()
    {
        var sink = new RecordingSink();
        var pipeline = Pipeline(sinks: [sink]);

        await pipeline.Emitter.DrainAsync().ConfigureAwait(false);
        pipeline.Emitter.Emit(Event("too late"));

        Assert.Multiple(() =>
        {
            Assert.That(sink.Received, Is.Empty);
            Assert.That(pipeline.Emitter.Refused, Is.EqualTo(1));
        });

        await pipeline.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ClosingDynamicDeliveryDeliversWhatWasOwedAndNothingAfterItAsync()
    {
        var host = new RecordingSink("host");
        var contributed = new RecordingSink("contributed");
        var enricher = new RecordingEnricher();
        var filter = new RecordingFilter();
        var contributions = new StubContributions()
            .Add(Movies, contributed)
            .Add(Movies, enricher)
            .Add(Movies, filter);

        await using var pipeline = Pipeline(sinks: [host], contributions: contributions);

        pipeline.Emitter.Emit(Event("before the cutoff"), Movies);

        Assert.That(
            await Waited(() => pipeline.Emitter.Delivered == 1).ConfigureAwait(false),
            Is.True,
            "the first event is delivered before the cutoff, which is what the cutoff is then closing over");

        await pipeline.Emitter.CloseDynamicDeliveryAsync().ConfigureAwait(false);

        pipeline.Emitter.Emit(Event("cleanup, after the cutoff"), Movies);
        await pipeline.DrainAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(
                contributed.Received.Select(one => one.Message).ToArray(),
                Is.EqualTo(new[] {"before the cutoff"}),
                "an extension's sink receives what reached it before the cutoff and nothing after");
            Assert.That(contributed.Flushes, Is.EqualTo(1), "and it is flushed while it can still be leased");
            Assert.That(
                host.Received.Select(one => one.Message).ToArray(),
                Is.EqualTo(new[] {"before the cutoff", "cleanup, after the cutoff"}),
                "the host's own sinks go on receiving, which is what makes cleanup telemetry worth raising");
            Assert.That(enricher.Seen, Has.Count.EqualTo(1), "no extension callback of any kind after the cutoff");
            Assert.That(filter.Seen, Has.Count.EqualTo(1));
            Assert.That(pipeline.Emitter.PendingDynamic, Is.Zero, "nothing is inside an extension callback");
        });
    }

    [Test]
    public void AnEvictedEventThatWasOwedToAnExtensionIsAccountedFor()
    {
        var contributions = new StubContributions().Add(Movies, new RecordingSink("contributed"));
        var emitter = Emitter(new TelemetryOptions { QueueCapacity = 2 }, contributions);

        for (var index = 0; index < 6; index++)
        {
            emitter.Emit(Event($"event {index}"), Movies);
        }

        Assert.Multiple(() =>
        {
            Assert.That(emitter.Dropped, Is.EqualTo(4));
            Assert.That(
                emitter.EvictedOwed,
                Is.EqualTo(4),
                "an evicted event was owed to a contributed sink and is counted as never reaching one");
            Assert.That(emitter.PendingDynamic, Is.Zero, "nothing is inside a contributed sink");
        });
    }

    [Test]
    public async Task AnExtensionsLeaseOutlivesACallThatWasAbandonedAsync()
    {
        using var release = new ManualResetEventSlim();
        var contributions = new StubContributions().Add(Movies, new HangingSink(release));

        await using var pipeline = Pipeline(
            contributions: contributions,
            options: new TelemetryOptions { SinkAttempt = TimeSpan.FromMilliseconds(100) });

        pipeline.Emitter.Emit(Event("into the void"), Movies);

        Assert.That(
            await Waited(() => pipeline.Emitter.Delivered == 1).ConfigureAwait(false),
            Is.True,
            "the pump goes on once the wait is abandoned");

        Assert.That(
            contributions.SinkLeasesReleased,
            Is.Zero,
            "the extension's code is still running; releasing its lease would let teardown unload it");

        release.Set();

        Assert.That(
            await Waited(() => contributions.SinkLeasesReleased == 1).ConfigureAwait(false),
            Is.True,
            "and the lease is given up once the call it abandoned actually returns");

        await pipeline.DrainAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task AQueuedEventProcessedAfterTheCutoffReachesNoExtensionCallbackAsync()
    {
        var host = new RecordingSink("host");
        var contributed = new RecordingSink("contributed");
        var enricher = new RecordingEnricher();
        var filter = new RecordingFilter();
        var hostEnricher = new RecordingEnricher();

        var contributions = new StubContributions()
            .Add(Movies, contributed)
            .Add(Movies, enricher)
            .Add(Movies, filter);

        // No pump: the event is accepted before the cutoff and is still in the queue when it closes.
        var emitter = new HostTelemetryEmitter(
            [hostEnricher],
            [],
            [host],
            RedactionEngine.Empty,
            TimeProvider.System,
            NullLogger<HostTelemetryEmitter>.Instance,
            Options.Create(new TelemetryOptions()),
            contributions);

        emitter.Emit(Event("queued before the cutoff"), Movies);
        await emitter.CloseDynamicDeliveryAsync().ConfigureAwait(false);

        var pump = emitter.RunAsync(CancellationToken.None);
        await emitter.DrainAsync().ConfigureAwait(false);
        await pump.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(hostEnricher.Seen, Has.Count.EqualTo(1), "the host's own contributions still run");
            Assert.That(host.Received, Has.Count.EqualTo(1), "and the host's own sinks still receive it");
            Assert.That(enricher.Seen, Is.Empty, "no extension enricher begins after the cutoff");
            Assert.That(filter.Seen, Is.Empty, "no extension filter begins after the cutoff");
            Assert.That(contributed.Received, Is.Empty, "no extension sink begins after the cutoff");
            Assert.That(emitter.WrittenOff, Is.EqualTo(1), "and what it was owed is written off and counted");
        });
    }

    [Test]
    public async Task OneExtensionsHangingSinkDoesNotHoldAnotherExtensionOpenAsync()
    {
        using var release = new ManualResetEventSlim();
        var quick = new RecordingSink("quick");
        var contributions = new StubContributions()
            .Add(Movies, new HangingSink(release))
            .Add(Television, quick);

        await using var pipeline = Pipeline(
            contributions: contributions,
            options: new TelemetryOptions { SinkAttempt = TimeSpan.FromMilliseconds(100) });

        pipeline.Emitter.Emit(Event("to both"));

        Assert.That(
            await Waited(() => quick.Received.Count == 1).ConfigureAwait(false),
            Is.True,
            "the extension that answers is not made to wait for the one that does not");

        Assert.Multiple(() =>
        {
            Assert.That(
                contributions.ReleasedFor(Television),
                Is.EqualTo(1),
                "and its package is let go, because nothing of its code is still running");
            Assert.That(
                contributions.ReleasedFor(Movies),
                Is.Zero,
                "while the one that is still running holds only its own package open");
        });

        release.Set();

        Assert.That(await Waited(() => contributions.ReleasedFor(Movies) == 1).ConfigureAwait(false), Is.True);
        await pipeline.DrainAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task OneExtensionsHangingFlushDoesNotHoldAnotherExtensionOpenAsync()
    {
        using var release = new ManualResetEventSlim();
        var quick = new RecordingSink("quick");
        var contributions = new StubContributions()
            .Add(Movies, new HangingSink(release, hangOnFlush: true))
            .Add(Television, quick);

        await using var pipeline = Pipeline(
            contributions: contributions,
            options: new TelemetryOptions { SinkAttempt = TimeSpan.FromMilliseconds(100) });

        var flushing = pipeline.Emitter.FlushContributedAsync();

        Assert.That(await Waited(() => quick.Flushes == 1).ConfigureAwait(false), Is.True);
        await flushing.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(contributions.ReleasedFor(Television), Is.EqualTo(1));
            Assert.That(contributions.ReleasedFor(Movies), Is.Zero, "its flush is still running");
        });

        release.Set();

        Assert.That(await Waited(() => contributions.ReleasedFor(Movies) == 1).ConfigureAwait(false), Is.True);
        await pipeline.DrainAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ALateFailureFromAnAbandonedSinkIsReportedRatherThanLostAsync()
    {
        var ending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contributions = new StubContributions().Add(Movies, new DeferredSink(ending));
        var heldWhenReported = -1;

        var log = new CapturingLogger(text =>
        {
            if (text.Contains("after the wait for it was abandoned", StringComparison.Ordinal))
            {
                heldWhenReported = contributions.SinkLeasesReleased;
            }
        });

        await using (var pipeline = Pipeline(
            contributions: contributions,
            options: new TelemetryOptions { SinkAttempt = TimeSpan.FromMilliseconds(50) },
            log: log))
        {
            pipeline.Emitter.Emit(Event("late"), Movies);
            await pipeline.DrainAsync().ConfigureAwait(false);
        }

        Assert.That(
            contributions.SinkLeasesReleased,
            Is.Zero,
            "the lease is owed to a call that has not ended, whatever the pipeline stopped waiting for");

        ending.SetException(new InvalidOperationException("the sink gave up later"));

        Assert.That(
            await Waited(() => contributions.SinkLeasesReleased == 1).ConfigureAwait(false),
            Is.True,
            "and it is given up once that call has ended");

        var written = log.Written;

        Assert.Multiple(() =>
        {
            Assert.That(
                written,
                Has.Some.Contains("after the wait for it was abandoned"),
                "the failure cannot travel back through an abandoned await, so this is where it is said");
            Assert.That(written, Has.Some.Contains("System.InvalidOperationException"));
            Assert.That(written, Has.Some.Contains("the sink gave up later"));
            Assert.That(
                heldWhenReported,
                Is.Zero,
                "the owner's lease is held while how its call ended is read, and given up after");
        });
    }

    [Test]
    public async Task AnAbandonedSinkThatEventuallySucceedsReportsNoFailureAsync()
    {
        var ending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new CapturingLogger();
        var contributions = new StubContributions().Add(Movies, new DeferredSink(ending));

        await using (var pipeline = Pipeline(
            contributions: contributions,
            options: new TelemetryOptions { SinkAttempt = TimeSpan.FromMilliseconds(50) },
            log: log))
        {
            pipeline.Emitter.Emit(Event("slow but sound"), Movies);
            await pipeline.DrainAsync().ConfigureAwait(false);
        }

        ending.SetResult();

        Assert.That(
            await Waited(() => contributions.SinkLeasesReleased == 1).ConfigureAwait(false),
            Is.True,
            "a call that ends well ends the hold on its owner just the same");

        Assert.Multiple(() =>
        {
            Assert.That(log.Written, Has.None.Contains("after the wait for it was abandoned"));
            Assert.That(log.Written, Has.Some.Contains("did not finish its send"), "the wait is still reported");
        });
    }

    [Test]
    public async Task ACallerThatStopsWaitingIsReportedAsThatRatherThanAsTheAttemptBudgetAsync()
    {
        using var stopping = new CancellationTokenSource();
        var ending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new DeferredSink(ending);
        var log = new CapturingLogger();

        // Long enough that the attempt bound cannot be what fires; the caller's token is the only candidate.
        var emitter = new HostTelemetryEmitter(
            [],
            [],
            [sink],
            RedactionEngine.Empty,
            TimeProvider.System,
            log,
            Options.Create(new TelemetryOptions { SinkAttempt = TimeSpan.FromSeconds(30) }));

        var pump = emitter.RunAsync(stopping.Token);
        emitter.Emit(Event("stopped mid-send"));

        await sink.Entered.Task.WaitAsync(Patience).ConfigureAwait(false);
        await stopping.CancelAsync().ConfigureAwait(false);
        await pump.WaitAsync(Patience).ConfigureAwait(false);

        ending.SetResult();

        Assert.Multiple(() =>
        {
            Assert.That(log.Written, Has.Some.Contains("was still running its send when the pipeline stopped"));
            Assert.That(
                log.Written,
                Has.None.Contains("did not finish its send"),
                "the attempt bound had nothing to do with it");
        });
    }

    [Test]
    public async Task ALoggerThatObjectsDoesNotStopTheEventAsync()
    {
        var sink = new RecordingSink();

        var emitter = new HostTelemetryEmitter(
            [],
            [],
            [sink],
            RedactionEngine.Empty,
            TimeProvider.System,
            new ObjectingLogger(),
            Options.Create(new TelemetryOptions()));

        var pump = emitter.RunAsync(CancellationToken.None);
        emitter.Emit(Event("delivered despite the log"));

        await emitter.DrainAsync().ConfigureAwait(false);
        await pump.ConfigureAwait(false);

        Assert.That(sink.Received, Has.Count.EqualTo(1));
    }

    private static async Task<bool> Waited(Func<bool> until)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (until())
            {
                return true;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        return false;
    }

    private static TelemetryEvent Event(string message, TelemetrySeverity severity = TelemetrySeverity.Info)
        => new(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, severity, message);

    private static IReadOnlyList<OwnedRedactionRules> Secret()
        => [new OwnedRedactionRules("host", [new RedactionRule("token", "token=(?<secret>[A-Za-z0-9]+)")])];

    private static HostTelemetryEmitter Emitter(TelemetryOptions options, StubContributions? contributions = null)
        => new(
            [],
            [],
            [],
            RedactionEngine.Empty,
            TimeProvider.System,
            NullLogger<HostTelemetryEmitter>.Instance,
            Options.Create(options),
            contributions);

    private static Running Pipeline(
        IReadOnlyList<ITelemetryEnricher>? enrichers = null,
        IReadOnlyList<ITelemetryEventFilter>? filters = null,
        IReadOnlyList<ITelemetrySink>? sinks = null,
        StubContributions? contributions = null,
        IReadOnlyList<OwnedRedactionRules>? rules = null,
        TelemetryOptions? options = null,
        CapturingLogger? log = null)
    {
        var settings = options ?? new TelemetryOptions();

        var emitter = new HostTelemetryEmitter(
            enrichers ?? [],
            filters ?? [],
            sinks ?? [],
            rules is null ? RedactionEngine.Empty : RedactionEngine.Compile(rules, settings),
            TimeProvider.System,
            log ?? (Microsoft.Extensions.Logging.ILogger<HostTelemetryEmitter>)NullLogger<HostTelemetryEmitter>.Instance,
            Options.Create(settings),
            contributions);

        return new Running(emitter);
    }

    /// <summary>An emitter with its pump running, stopped the way the host stops it.</summary>
    private sealed class Running(HostTelemetryEmitter emitter) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _pump = emitter.RunAsync(CancellationToken.None);

        internal HostTelemetryEmitter Emitter { get; } = emitter;

        internal async Task DrainAsync()
        {
            await Emitter.DrainAsync().ConfigureAwait(false);
            await _pump.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await Emitter.DrainAsync().ConfigureAwait(false);
            await _stopping.CancelAsync().ConfigureAwait(false);
            _stopping.Dispose();
        }
    }

    /// <summary>A sink whose call outlives the pipeline's wait for it, and ends when the fixture says.</summary>
    private sealed class DeferredSink(TaskCompletionSource ending) : ITelemetrySink
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SinkId => "deferred";

        public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return ending.Task;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Keeps what the pipeline said, which for an abandoned call is the only place it is said.</summary>
    private sealed class CapturingLogger(Action<string>? watching = null)
        : Microsoft.Extensions.Logging.ILogger<HostTelemetryEmitter>
    {
        private readonly List<string> _written = [];

        internal IReadOnlyList<string> Written
        {
            get
            {
                lock (_written)
                {
                    return [.. _written];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);

            lock (_written)
            {
                _written.Add(text);
            }

            // Read where the pipeline stands at the moment it says this, which is the only place the order
            // between reporting and releasing is visible.
            watching?.Invoke(text);
        }
    }

    private sealed class ObjectingSink : ITelemetrySink
    {
        public string SinkId => throw new InvalidOperationException("even my name objects");

        public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("and so does my delivery");

        public Task FlushAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("and my flush");
    }

    /// <summary>A logger that fails at the one moment a pipeline would want to report something.</summary>
    private sealed class ObjectingLogger : Microsoft.Extensions.Logging.ILogger<HostTelemetryEmitter>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("the log is full");
    }

    /// <summary>A failure that will not say what went wrong.</summary>
    private sealed class Unreadable : Exception
    {
        public override string Message => throw new InvalidOperationException("not telling");

        public override string? StackTrace => throw new InvalidOperationException("nor that");
    }

    private sealed class HangingSink(ManualResetEventSlim release, bool hangOnFlush = false) : ITelemetrySink
    {
        public string SinkId => "hanging";

        public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
            => hangOnFlush ? Task.CompletedTask : Task.Run(() => release.Wait(Patience), CancellationToken.None);

        public Task FlushAsync(CancellationToken cancellationToken = default)
            => hangOnFlush ? Task.Run(() => release.Wait(Patience), CancellationToken.None) : Task.CompletedTask;
    }
}
