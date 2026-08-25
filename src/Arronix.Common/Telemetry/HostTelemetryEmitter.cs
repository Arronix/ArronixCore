using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Channels;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Common.Contributions;
using Arronix.Common.Diagnostics;
using Arronix.Common.Lifetimes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Telemetry;

/// <summary>
/// The platform's telemetry pipeline: accept without waiting, then enrich, filter and deliver on a pump.
/// </summary>
/// <remarks>
/// <para>
/// Accepting an event does four things and no more: it takes a snapshot of what the caller passed, cut to
/// size and copied into host-owned strings and collections; it renders any live exception to text; it masks
/// every secret the installation described; and it hands the result to a bounded queue. Nothing else runs
/// on the caller's thread — no enricher, no filter, no sink, not even the host's own log — because the
/// caller is usually the code that is already going wrong.
/// </para>
/// <para>
/// The pump then runs the contributions, and the order there is the security property. Redaction has
/// already happened, so nothing an extension contributed ever sees an unredacted message or tag. The live
/// exception is already gone, so nothing an extension contributed ever receives a handle onto the assembly
/// that threw. After every contribution the identifier, timestamp, severity and attribution are restored
/// from the envelope and the text is redacted again, so an enricher can add context and cannot change what
/// the event is, when it happened, how serious it was, who raised it, or put a live exception back.
/// </para>
/// <para>
/// An extension's enricher and filter see that extension's own events and no others, leased from the exact
/// runtime that contributed them. A filter may suppress only its own extension's events, and only below
/// <see cref="TelemetrySeverity.Error"/>: a component that could delete the record of what it did next is
/// not a filter. The host's own filters are host policy and are trusted.
/// </para>
/// </remarks>
internal sealed partial class HostTelemetryEmitter : IOriginatedTelemetryEmitter, ITelemetryShutdown
{
    /// <summary>The tag carrying the extension an event was raised by.</summary>
    /// <remarks>
    /// Written for whoever reads tags. Nothing in the pipeline decides anything from it: origin is a field
    /// on the envelope, and this tag is restored from that field after every contribution.
    /// </remarks>
    internal const string OriginTag = "plugin";

    private const int RecentCapacity = 2048;

    private readonly IReadOnlyList<ITelemetryEnricher> _enrichers;
    private readonly IReadOnlyList<ITelemetryEventFilter> _filters;
    private readonly IReadOnlyList<ITelemetrySink> _sinks;
    private readonly IPluginContributionSource? _contributions;
    private readonly RedactionEngine _redaction;
    private readonly TimeProvider _clock;
    private readonly ILogger<HostTelemetryEmitter> _log;
    private readonly TelemetryOptions _options;

    private readonly Channel<TelemetryEnvelope> _queue;
    private readonly HashSet<Guid> _recent = [];
    private readonly Queue<Guid> _recentOrder = new();
    private readonly Lock _recentGate = new();

    /// <summary>
    /// Completed when no ordinary event-delivery extension callback can begin and the pipeline has stopped
    /// awaiting the ones it started. A call abandoned at <see cref="TelemetryOptions.SinkAttempt"/> may
    /// still be running; it holds its own owner's lease until it returns, which is what keeps that
    /// extension loaded underneath it. The final <c>FlushAsync</c> inside
    /// <see cref="CloseDynamicDeliveryAsync"/> is the one deliberate exception to the barrier, and it can
    /// overlap such a call.
    /// </summary>
    private readonly TaskCompletionSource _dynamicQuiet = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long _dropped;
    private long _refused;
    private long _duplicates;
    private long _delivered;
    private long _evictedOwed;
    private long _writtenOff;
    private int _pendingDynamic;
    private int _dynamicClosed;
    private int _closed;

    /// <summary>Initializes a new instance of the <see cref="HostTelemetryEmitter"/> class.</summary>
    /// <param name="enrichers">The host's own enrichers, in registration order.</param>
    /// <param name="filters">The host's own filters, in registration order.</param>
    /// <param name="sinks">The host's own sinks.</param>
    /// <param name="redaction">The installation's compiled redaction rules.</param>
    /// <param name="clock">The clock an event without a timestamp is stamped from.</param>
    /// <param name="log">The host's own record of what it delivered.</param>
    /// <param name="options">The bounds the pipeline runs under.</param>
    /// <param name="contributions">The live extension runtime, when there is one.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HostTelemetryEmitter(
        IEnumerable<ITelemetryEnricher> enrichers,
        IEnumerable<ITelemetryEventFilter> filters,
        IEnumerable<ITelemetrySink> sinks,
        RedactionEngine redaction,
        TimeProvider clock,
        ILogger<HostTelemetryEmitter> log,
        IOptions<TelemetryOptions>? options = null,
        IPluginContributionSource? contributions = null)
    {
        ArgumentNullException.ThrowIfNull(enrichers);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(redaction);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);

        _enrichers = [.. enrichers];
        _filters = [.. filters];
        _sinks = [.. sinks];
        _redaction = redaction;
        _clock = clock;
        _log = log;
        _options = options?.Value ?? new TelemetryOptions();
        _contributions = contributions;

        // Dropping the oldest is safe here only because a queued envelope holds nothing but host-owned
        // strings and values: no lease, no handle, no extension object. Both the evictions and the writes
        // refused after close are counted rather than silently absorbed.
        _queue = Channel.CreateBounded<TelemetryEnvelope>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            Evicted);
    }

    /// <summary>
    /// Gets how many accepted events were never delivered: evicted because delivery fell behind, or still
    /// unread when the pump stopped.
    /// </summary>
    internal long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Gets how many queue evictions were of events marked for extension processing.</summary>
    internal long EvictedOwed => Interlocked.Read(ref _evictedOwed);

    /// <summary>
    /// Gets how many events marked for extension processing received none of it: they reached the pump
    /// after the cutoff, or were left unread when it stopped. The first kind still reaches the host's own
    /// sinks; the second is counted in <see cref="Dropped"/> as well.
    /// </summary>
    internal long WrittenOff => Interlocked.Read(ref _writtenOff);

    /// <summary>
    /// Gets how many events hold a permit to run extension callbacks — an enricher, a filter or a sink.
    /// The permit is given up once the pipeline stops waiting, which for an abandoned sink call is before
    /// that call returns.
    /// </summary>
    internal int PendingDynamic => Volatile.Read(ref _pendingDynamic);

    /// <summary>Gets how many events were refused because the pipeline had already been closed.</summary>
    internal long Refused => Interlocked.Read(ref _refused);

    /// <summary>Gets how many events were dropped as repeats of one already accepted.</summary>
    internal long Duplicates => Interlocked.Read(ref _duplicates);

    /// <summary>Gets how many events the pump has finished handing to the sinks.</summary>
    internal long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>
    /// Settles an evicted envelope: it will never be delivered, so what it owed is written off and counted.
    /// </summary>
    private void Evicted(TelemetryEnvelope envelope)
    {
        Interlocked.Increment(ref _dropped);

        if (envelope.DynamicDelivery)
        {
            Interlocked.Increment(ref _evictedOwed);
        }
    }

    /// <inheritdoc />
    public void Emit(TelemetryEvent telemetryEvent) => Accept(telemetryEvent, origin: null);

    /// <inheritdoc />
    public void Emit(TelemetryEvent telemetryEvent, PluginId origin) => Accept(telemetryEvent, origin);

    /// <summary>
    /// Runs the delivery pump until the token fires or the pipeline is closed.
    /// </summary>
    /// <param name="cancellationToken">Stops the pump.</param>
    /// <returns>A task that completes when the queue is closed and drained, or the token fires.</returns>
    /// <remarks>
    /// Owned by the caller — the generic host, in an ordinary installation — rather than started in the
    /// constructor. A pump started by a constructor is a task nobody awaits, so a process-fatal failure in
    /// it becomes an unobserved exception instead of reaching the host.
    /// </remarks>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await DeliverAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping is not a failure.
        }
        finally
        {
            // Whatever is left will never be delivered. Emptying it is what makes the counts true and what
            // stops a stopped pipeline holding a backlog nobody will read.
            while (_queue.Reader.TryRead(out var abandoned))
            {
                Interlocked.Increment(ref _dropped);

                if (abandoned.DynamicDelivery)
                {
                    Interlocked.Increment(ref _writtenOff);
                }
            }
        }
    }

    /// <summary>
    /// Stops accepting and waits a bounded time for what was already accepted to be delivered.
    /// </summary>
    /// <param name="cancellationToken">Cuts the wait short.</param>
    /// <returns><see langword="true"/> when the queue emptied within the budget.</returns>
    internal async Task<bool> DrainAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _closed, 1);
        _queue.Writer.TryComplete();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.ShutdownDrain);

        try
        {
            await _queue.Reader.Completion.WaitAsync(budget.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            Report(() => DrainAbandoned(_log, _options.ShutdownDrain, _queue.Reader.Count));
            return false;
        }
    }

    /// <inheritdoc />
    public async Task CloseDynamicDeliveryAsync(CancellationToken cancellationToken = default)
    {
        // The cutoff and the permit count are read under one lock, so an event's extension processing
        // either began before the cutoff — and is waited for below — or is refused. An envelope still
        // queued when this happens is written off and counted: it was owed, and the extension it was owed
        // to is about to be taken away.
        lock (_recentGate)
        {
            if (Interlocked.Exchange(ref _dynamicClosed, 1) != 0)
            {
                return;
            }

            SettleDynamic();
        }

        // A queued backlog does not hold this: an event that has not started its extension callbacks never
        // will. What does hold it is an event already inside them. An enricher or filter is synchronous,
        // cannot be abandoned, and holds this for as long as it runs; a sink await is bounded by
        // TelemetryOptions.SinkAttempt, after which the pipeline stops waiting - the call itself may be
        // running still, holding its own owner's lease until it returns. Then the contributed sinks are
        // flushed, which is the last call they get, and which can overlap such a call.
        await _dynamicQuiet.Task.ConfigureAwait(false);

        await FlushContributedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes the barrier once no extension callback may begin and none the pipeline is still awaiting
    /// remains.
    /// </summary>
    private void SettleDynamic()
    {
        if (Volatile.Read(ref _dynamicClosed) != 0 && Volatile.Read(ref _pendingDynamic) == 0)
        {
            _dynamicQuiet.TrySetResult();
        }
    }

    /// <summary>Takes a permit to run this event's extension callbacks, or reports that they have closed.</summary>
    private bool TryBeginDynamic()
    {
        lock (_recentGate)
        {
            if (Volatile.Read(ref _dynamicClosed) != 0)
            {
                return false;
            }

            Interlocked.Increment(ref _pendingDynamic);
            return true;
        }
    }

    /// <summary>Gives up this event's permit, once the pipeline has stopped waiting on its callbacks.</summary>
    private void ReleaseDynamic()
    {
        Interlocked.Decrement(ref _pendingDynamic);

        lock (_recentGate)
        {
            SettleDynamic();
        }
    }

    /// <summary>
    /// Gives every extension-contributed sink its bounded chance to flush, while it is still published.
    /// </summary>
    /// <param name="cancellationToken">Cuts the attempt short.</param>
    /// <returns>A task that completes once every contributed sink has had its turn.</returns>
    internal async Task FlushContributedAsync(CancellationToken cancellationToken = default)
    {
        if (_contributions is null)
        {
            return;
        }

        foreach (var owner in _contributions.ContributorsOf<ITelemetrySink>())
        {
            await EachSinkAsync(
                    owner,
                    (sink, hold, token) => AttemptAsync(sink, "flush", sink.FlushAsync, hold, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one operation against each sink one extension contributed, holding that extension's lease and
    /// nothing else.
    /// </summary>
    /// <remarks>
    /// The lease is released when the loop ends, except that an abandoned call keeps it: the extension's
    /// code is still running, and unloading it would be the one outcome worse than a slow sink. That hold
    /// is the abandoning extension's alone.
    /// </remarks>
    private async Task EachSinkAsync(
        PluginId owner,
        Func<ITelemetrySink, LeaseHold, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var contributed = _contributions!.AcquireOwned<ITelemetrySink>(owner);
        var hold = new LeaseHold(contributed);

        try
        {
            foreach (var contribution in contributed.Contributions)
            {
                await operation(contribution.Value, hold, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            hold.Release();
        }
    }

    /// <summary>Gives every host-registered sink its bounded chance to flush.</summary>
    /// <param name="cancellationToken">Cuts the attempt short.</param>
    /// <returns>A task that completes once every host sink has had its turn.</returns>
    internal async Task FlushHostAsync(CancellationToken cancellationToken = default)
    {
        foreach (var sink in _sinks)
        {
            await AttemptAsync(sink, "flush", sink.FlushAsync, hold: null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Everything that happens on the caller's thread: snapshot, cap, render, redact, enqueue.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An emitter that can throw at the point something is already going wrong is a liability; process-fatal conditions still propagate.")]
    private void Accept(TelemetryEvent telemetryEvent, PluginId? origin)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        try
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                Interlocked.Increment(ref _refused);
                return;
            }

            var envelope = Redacted(Snapshot(telemetryEvent, origin));

            if (!FirstSighting(envelope.EventId))
            {
                Interlocked.Increment(ref _duplicates);
                return;
            }

            // Marked while extension processing is open. The mark is what the envelope is owed; whether any
            // extension callback actually runs for it is decided again at delivery, because the cutoff may
            // have happened while it waited in the queue.
            if (Volatile.Read(ref _dynamicClosed) == 0)
            {
                envelope = envelope with { DynamicDelivery = true };
            }

            if (!_queue.Writer.TryWrite(envelope))
            {
                Interlocked.Increment(ref _refused);
            }
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            Report(() => PipelineFailed(_log, failure.GetType().FullName ?? failure.GetType().Name, Safe(failure)));
        }
    }

    /// <summary>
    /// Copies what the caller passed into host-owned values, cut to size, with the failure rendered to text.
    /// </summary>
    /// <remarks>
    /// Every member read here can be an extension's own object or an overridden getter, so this is the one
    /// place the caller's event is touched — and it runs while the caller still holds that extension's
    /// invocation ticket, because that is what the ticket is for.
    /// </remarks>
    private TelemetryEnvelope Snapshot(TelemetryEvent telemetryEvent, PluginId? origin)
    {
        var eventId = telemetryEvent.EventId == Guid.Empty ? Guid.CreateVersion7() : telemetryEvent.EventId;
        var timestamp = telemetryEvent.Timestamp == default ? _clock.GetUtcNow() : telemetryEvent.Timestamp;
        var severity = telemetryEvent.Severity;

        var snapshot = new TelemetryEvent(
            eventId,
            timestamp,
            severity,
            TelemetryLimits.Cut(Read(() => telemetryEvent.Message), TelemetryLimits.MaxTextLength))
        {
            Tags = Attributed(TelemetryLimits.Tags(Read(() => telemetryEvent.Tags)), origin),
            Fingerprint = TelemetryLimits.Fingerprint(Read(() => telemetryEvent.Fingerprint)),
            CorrelationId = TelemetryLimits.CutOrNull(
                Read(() => telemetryEvent.CorrelationId) ?? CorrelationContext.Current,
                TelemetryLimits.MaxTokenLength),

            // The live exception stops here and never enters the queue. Everything downstream reads the
            // rendered form, because the object itself is a handle onto the assembly that threw.
            Exception = null,
            ExceptionSummary = TelemetryLimits.Summary(Rendered(telemetryEvent)),
        };

        return new TelemetryEnvelope(
            eventId,
            timestamp,
            severity,
            origin,
            snapshot.CorrelationId,
            DynamicDelivery: false,
            snapshot);
    }

    /// <summary>Renders the failure, containing an overridden member that objects to being read.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An exception's own members are third-party code when the exception is an extension's.")]
    private ExceptionSummary? Rendered(TelemetryEvent telemetryEvent)
    {
        if (telemetryEvent.ExceptionSummary is { } summary)
        {
            return summary;
        }

        if (telemetryEvent.Exception is not { } failure)
        {
            return null;
        }

        try
        {
            return ExceptionSummary.From(failure);
        }
        catch (Exception unreadable) when (!ProcessFailure.IsFatal(unreadable))
        {
            // The failure's own type, which is what ExceptionSummary promises to name. A message getter
            // that objects says nothing about what was thrown, and naming its objection instead would put
            // the wrong type in a support bundle. Reading a type cannot throw.
            return new ExceptionSummary(
                failure.GetType().FullName ?? failure.GetType().Name,
                $"the failure would not describe itself ({unreadable.GetType().Name})",
                StackTrace: null);
        }
    }

    /// <summary>Reads one member of the caller's event, containing a getter that objects.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every member of the caller's event may be an overridden getter belonging to an extension.")]
    private TValue? Read<TValue>(Func<TValue?> member)
    {
        try
        {
            return member();
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            Report(() => ContributionFailed(_log, "event member", "the caller's event", Safe(failure)));
            return default;
        }
    }

    /// <summary>Writes the attribution tag from the envelope's origin, replacing whatever was there.</summary>
    private static IReadOnlyDictionary<string, string> Attributed(
        IReadOnlyDictionary<string, string> tags,
        PluginId? origin)
    {
        var written = new Dictionary<string, string>(tags, StringComparer.Ordinal);

        if (origin is { } raised)
        {
            written[OriginTag] = raised.ToString();
        }
        else
        {
            written.Remove(OriginTag);
        }

        return new ReadOnlyDictionary<string, string>(written);
    }

    /// <summary>Masks every secret the installation described, everywhere text can carry one.</summary>
    private TelemetryEnvelope Redacted(TelemetryEnvelope envelope)
    {
        var telemetryEvent = envelope.Event;

        var tags = telemetryEvent.Tags.ToDictionary(
            tag => tag.Key,
            tag => _redaction.Redact(tag.Value),
            StringComparer.Ordinal);

        return envelope with
        {
            Event = telemetryEvent with
            {
                Message = _redaction.Redact(telemetryEvent.Message),
                Tags = new ReadOnlyDictionary<string, string>(tags),
                Fingerprint = [.. telemetryEvent.Fingerprint.Select(_redaction.Redact)],
                ExceptionSummary = telemetryEvent.ExceptionSummary is { } summary
                    ? summary with
                    {
                        Message = _redaction.Redact(summary.Message),
                        StackTrace = summary.StackTrace is null ? null : _redaction.Redact(summary.StackTrace),
                    }
                    : null,
            },
        };
    }

    /// <summary>
    /// Restores everything a contribution is not entitled to change, then redacts what it added.
    /// </summary>
    private TelemetryEnvelope Restored(TelemetryEnvelope envelope, TelemetryEvent contributed)
        => Redacted(envelope with
        {
            Event = contributed with
            {
                EventId = envelope.EventId,
                Timestamp = envelope.Timestamp,
                Severity = envelope.Severity,
                CorrelationId = envelope.CorrelationId,
                Tags = Attributed(TelemetryLimits.Tags(contributed.Tags), envelope.Origin),
                Fingerprint = TelemetryLimits.Fingerprint(contributed.Fingerprint),
                Message = TelemetryLimits.Cut(contributed.Message, TelemetryLimits.MaxTextLength),
                ExceptionSummary = TelemetryLimits.Summary(contributed.ExceptionSummary),

                // An enricher may return an event with a live exception put back. It does not travel.
                Exception = null,
            },
        });

    private async Task DeliverAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        // The permit is taken before any extension callback and held across all of them: an enricher, a
        // filter and a sink are equally this extension's code, and the cutoff means none of them begins.
        var contributing = envelope.DynamicDelivery && _contributions is not null && TryBeginDynamic();

        if (envelope.DynamicDelivery && !contributing)
        {
            // Owed, and written off: delivery to this extension's contributions closed while the event
            // waited in the queue, and the extension is being taken away.
            Interlocked.Increment(ref _writtenOff);
        }

        try
        {
            var enriched = Enriched(envelope, contributing);

            if (!Allowed(enriched, contributing))
            {
                return;
            }

            // Logged after filtering, because a suppression a filter is entitled to make means the event is
            // suppressed. The host's own log is the one place the live exception would have been useful,
            // and it is deliberately not kept for it: nothing downstream of acceptance holds one.
            Report(() => Recorded(_log, Level(enriched.Severity), enriched.Event.Message, enriched.EventId, null));

            foreach (var sink in _sinks)
            {
                await SendAsync(sink, enriched.Event, hold: null, cancellationToken).ConfigureAwait(false);
            }

            if (contributing)
            {
                // One lease per owner, in the order the source names them. A composite lease over every
                // sink would mean one extension that ignores its token holds every other extension's
                // package open as well, and none of them could then be torn down.
                foreach (var owner in _contributions!.ContributorsOf<ITelemetrySink>())
                {
                    await EachSinkAsync(
                            owner,
                            (sink, hold, token) => SendAsync(sink, enriched.Event, hold, token),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            Interlocked.Increment(ref _delivered);
        }
        finally
        {
            if (contributing)
            {
                ReleaseDynamic();
            }
        }
    }

    /// <summary>Runs the host's enrichers, then the raising extension's own.</summary>
    private TelemetryEnvelope Enriched(TelemetryEnvelope envelope, bool contributing)
    {
        var enriched = envelope;

        foreach (var enricher in _enrichers)
        {
            enriched = Contributed(enricher, enriched);
        }

        // After the cutoff a cleanup event still goes through the host's enrichers and reaches the host's
        // sinks. It reaches no extension's contribution of any kind: those are being taken away.
        if (!contributing || envelope.Origin is not { } raised)
        {
            return enriched;
        }

        var contributions = _contributions!;

        using var contributed = contributions.AcquireOwned<ITelemetryEnricher>(raised);

        foreach (var contribution in contributed.Contributions)
        {
            enriched = Contributed(contribution.Value, enriched);
        }

        return enriched;
    }

    /// <summary>One enricher's turn, contained, and followed by restoration.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An enricher is third-party code; one that fails loses its context and never the event.")]
    private TelemetryEnvelope Contributed(ITelemetryEnricher enricher, TelemetryEnvelope envelope)
    {
        try
        {
            var contributed = enricher.Enrich(envelope.Event);
            return contributed is null ? envelope : Restored(envelope, contributed);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            Report(() => ContributionFailed(
                _log,
                "enricher",
                enricher.GetType().FullName ?? enricher.GetType().Name,
                Safe(failure)));

            return envelope;
        }
    }

    /// <summary>Asks the host's filters, then the raising extension's own within what it may suppress.</summary>
    private bool Allowed(TelemetryEnvelope envelope, bool contributing)
    {
        foreach (var filter in _filters)
        {
            if (!Asked(filter, envelope.Event))
            {
                return false;
            }
        }

        // An extension may quiet its own noise. It may not quiet the host, another extension, or anything
        // serious enough that an operator would be misled by its absence.
        if (!contributing
            || envelope.Origin is not { } raised
            || envelope.Severity >= TelemetrySeverity.Error)
        {
            return true;
        }

        using var contributed = _contributions!.AcquireOwned<ITelemetryEventFilter>(raised);

        return contributed.Contributions.All(one => Asked(one.Value, envelope.Event));
    }

    /// <summary>One filter's turn, contained. A filter that fails has not said no.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A filter is third-party code; one that fails must not delete the event it failed on.")]
    private bool Asked(ITelemetryEventFilter filter, TelemetryEvent telemetryEvent)
    {
        try
        {
            return filter.ShouldSend(telemetryEvent);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            Report(() => ContributionFailed(
                _log,
                "filter",
                filter.GetType().FullName ?? filter.GetType().Name,
                Safe(failure)));

            return true;
        }
    }

    /// <summary>Whether this is the first time this identifier has been seen recently.</summary>
    private bool FirstSighting(Guid eventId)
    {
        lock (_recentGate)
        {
            if (!_recent.Add(eventId))
            {
                return false;
            }

            _recentOrder.Enqueue(eventId);

            if (_recentOrder.Count > RecentCapacity)
            {
                _recent.Remove(_recentOrder.Dequeue());
            }

            return true;
        }
    }

    /// <summary>
    /// One sink's turn, bounded and contained, so a sink that never answers delays only itself.
    /// </summary>
    /// <remarks>
    /// The bound is a budget, not a kill: in-process code that ignores its token cannot be stopped, and the
    /// wait is abandoned rather than the call. The lease this runs under is held by the caller until the
    /// call it abandoned actually returns, because unloading code that is still running is the one outcome
    /// worse than a slow sink.
    /// </remarks>
    private async Task SendAsync(
        ITelemetrySink sink,
        TelemetryEvent telemetryEvent,
        LeaseHold? hold,
        CancellationToken cancellationToken)
        => await AttemptAsync(sink, "send", token => sink.SendAsync(telemetryEvent, token), hold, cancellationToken)
            .ConfigureAwait(false);

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A sink is third-party code; one that fails or hangs must not stop the pump or its peers.")]
    private async Task AttemptAsync(
        ITelemetrySink sink,
        string what,
        Func<CancellationToken, Task> attempt,
        LeaseHold? hold,
        CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.SinkAttempt);

        Task running;

        try
        {
            // Started here rather than inside the wait, so the call itself is a value this method holds on
            // to. Abandoning the wait is not abandoning the call, and the two must not be confused.
            running = attempt(budget.Token) ?? Task.CompletedTask;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            budget.Dispose();
            Report(() => SinkFailed(_log, Named(sink), Safe(failure)));
            return;
        }

        try
        {
            await running.WaitAsync(budget.Token).ConfigureAwait(false);
            budget.Dispose();
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            Report(() => SinkAbandoned(_log, Named(sink), what, _options.SinkAttempt));

            // The wait is over; the sink is not. Whatever kept it — the lease on its package included — is
            // held until it actually finishes, because unloading code that is still running is worse than
            // waiting forever for a sink that never answers.
            hold?.Abandoned(running);
            _ = running.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                budget,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            budget.Dispose();
            Report(() => SinkFailed(_log, Named(sink), Safe(failure)));
        }
    }

    /// <summary>
    /// One acquisition of the contributed sinks, released only when nothing it lent out is still running.
    /// </summary>
    /// <remarks>
    /// A sink that ignores its token cannot be stopped, and its package must not be unloaded while it runs.
    /// The honest consequence is stated rather than hidden: a trusted in-process sink that never returns
    /// pins its package for the life of the process.
    /// </remarks>
    private sealed class LeaseHold(IDisposable lease)
    {
        private readonly Lock _gate = new();
        private int _outstanding;
        private bool _released;

        internal void Abandoned(Task running)
        {
            lock (_gate)
            {
                _outstanding++;
            }

            _ = running.ContinueWith(
                static (_, state) => ((LeaseHold)state!).Finished(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal void Release()
        {
            lock (_gate)
            {
                _released = true;

                if (_outstanding > 0)
                {
                    return;
                }
            }

            lease.Dispose();
        }

        private void Finished()
        {
            lock (_gate)
            {
                _outstanding--;

                if (!_released || _outstanding > 0)
                {
                    return;
                }
            }

            lease.Dispose();
        }
    }

    /// <summary>The text of a failure, bounded, without trusting an overridden message getter.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failure's own message is third-party code when the failure is an extension's.")]
    private static string Safe(Exception failure)
    {
        try
        {
            return TelemetryLimits.Cut(failure.Message, TelemetryLimits.MaxTokenLength);
        }
        catch (Exception unreadable) when (!ProcessFailure.IsFatal(unreadable))
        {
            return $"<{failure.GetType().Name}, whose message threw {unreadable.GetType().Name}>";
        }
    }

    /// <summary>
    /// Writes one report, and gives up rather than recursing if the log itself objects.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A logger is a sink of its own; one that fails must not take the pipeline with it, and must not be reported to itself.")]
    private static void Report(Action write)
    {
        try
        {
            write();
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            // Nowhere left to say it. Reporting a failed report through the thing that just failed is how a
            // diagnostic pipeline turns one broken logger into a stack overflow.
        }
    }

    /// <summary>Names a sink for a report. Its identifier getter is third-party code as well.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A sink's own identifier getter is third-party code, and naming it must not fail the report.")]
    private static string Named(ITelemetrySink sink)
    {
        try
        {
            return TelemetryLimits.Cut(sink.SinkId, TelemetryLimits.MaxTokenLength);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return $"{sink.GetType().Name} (its identifier threw {failure.GetType().Name})";
        }
    }

    private static LogLevel Level(TelemetrySeverity severity)
        => severity switch
        {
            TelemetrySeverity.Debug => LogLevel.Debug,
            TelemetrySeverity.Info => LogLevel.Information,
            TelemetrySeverity.Warning => LogLevel.Warning,
            TelemetrySeverity.Error => LogLevel.Error,
            TelemetrySeverity.Fatal => LogLevel.Critical,
            _ => LogLevel.Information,
        };

    [LoggerMessage(EventId = 9400, Message = "{Message} [{EventId}]")]
    private static partial void Recorded(
        ILogger logger,
        LogLevel level,
        string message,
        Guid eventId,
        Exception? exception);

    [LoggerMessage(
        EventId = 9401,
        Level = LogLevel.Error,
        Message = "Telemetry {Kind} '{Contributor}' failed and was skipped: {Failure}")]
    private static partial void ContributionFailed(ILogger logger, string kind, string contributor, string failure);

    [LoggerMessage(
        EventId = 9402,
        Level = LogLevel.Error,
        Message = "Telemetry sink '{Sink}' failed: {Failure}")]
    private static partial void SinkFailed(ILogger logger, string sink, string failure);

    [LoggerMessage(
        EventId = 9403,
        Level = LogLevel.Error,
        Message = "The telemetry pipeline failed while accepting an event ({Failure}: {Message}). The event was lost.")]
    private static partial void PipelineFailed(ILogger logger, string failure, string message);

    [LoggerMessage(
        EventId = 9404,
        Level = LogLevel.Warning,
        Message = "Telemetry delivery did not drain within {Budget}; {Remaining} event(s) were abandoned.")]
    private static partial void DrainAbandoned(ILogger logger, TimeSpan budget, int remaining);

    [LoggerMessage(
        EventId = 9405,
        Level = LogLevel.Warning,
        Message = "Telemetry sink '{Sink}' did not finish its {What} within {Budget}; the wait was abandoned and its package stays held until the call returns.")]
    private static partial void SinkAbandoned(ILogger logger, string sink, string what, TimeSpan budget);

}
