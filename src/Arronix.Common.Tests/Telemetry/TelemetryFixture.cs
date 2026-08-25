using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Common.Contributions;

namespace Arronix.Common.Tests.Telemetry;

/// <summary>A contribution source standing in for a live extension runtime.</summary>
internal sealed class StubContributions : IPluginContributionSource
{
    private readonly Dictionary<PluginId, List<object>> _byOwner = [];

    internal List<PluginId> OwnedRequests { get; } = [];

    internal int UnscopedRequests { get; private set; }

    internal List<Type> UnscopedContracts { get; } = [];

    internal int Released { get; private set; }

    internal int SinkLeasesReleased { get; private set; }

    internal StubContributions Add(PluginId owner, object contribution)
    {
        if (!_byOwner.TryGetValue(owner, out var owned))
        {
            owned = [];
            _byOwner[owner] = owned;
        }

        owned.Add(contribution);
        return this;
    }

    public IContributionLease<TContract> Acquire<TContract>()
        where TContract : class
    {
        UnscopedRequests++;
        UnscopedContracts.Add(typeof(TContract));
        return Lease<TContract>(_byOwner.SelectMany(pair => pair.Value.OfType<TContract>()
            .Select(value => new PluginContribution<TContract>(pair.Key, 0, value))));
    }

    public IContributionLease<TContract> AcquireOwned<TContract>(PluginId owner)
        where TContract : class
    {
        OwnedRequests.Add(owner);

        return Lease<TContract>(_byOwner.TryGetValue(owner, out var owned)
            ? owned.OfType<TContract>().Select(value => new PluginContribution<TContract>(owner, 0, value))
            : []);
    }

    public IContributionLease<EventHandlerContribution> AcquireEventHandlers(Type eventType)
        => throw new NotSupportedException();

    public bool TryAcquireOwner(Type type, out PluginId owner, out IDisposable? lease)
    {
        owner = default;
        lease = null;
        return false;
    }

    private ContributionLease<TContract> Lease<TContract>(IEnumerable<PluginContribution<TContract>> contributions)
        where TContract : class
        => new([.. contributions], () =>
        {
            Released++;

            if (typeof(TContract) == typeof(ITelemetrySink))
            {
                SinkLeasesReleased++;
            }
        });

    private sealed class ContributionLease<TContract>(
        IReadOnlyList<PluginContribution<TContract>> contributions,
        Action onRelease) : IContributionLease<TContract>
        where TContract : class
    {
        public IReadOnlyList<PluginContribution<TContract>> Contributions => contributions;

        public void Dispose() => onRelease();
    }
}

/// <summary>A sink that records what it was given.</summary>
internal sealed class RecordingSink(string id = "recording") : ITelemetrySink
{
    private readonly List<TelemetryEvent> _received = [];

    public string SinkId => id;

    internal IReadOnlyList<TelemetryEvent> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received];
            }
        }
    }

    internal int Flushes { get; private set; }

    public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        lock (_received)
        {
            _received.Add(telemetryEvent);
        }

        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Flushes++;
        return Task.CompletedTask;
    }
}

/// <summary>An enricher that records what it was shown and returns what the fixture told it to.</summary>
internal sealed class RecordingEnricher(Func<TelemetryEvent, TelemetryEvent>? contribute = null) : ITelemetryEnricher
{
    private readonly List<TelemetryEvent> _seen = [];

    internal IReadOnlyList<TelemetryEvent> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    public TelemetryEvent Enrich(TelemetryEvent telemetryEvent)
    {
        lock (_seen)
        {
            _seen.Add(telemetryEvent);
        }

        return contribute?.Invoke(telemetryEvent) ?? telemetryEvent;
    }
}

/// <summary>A filter that records what it was shown and answers what the fixture told it to.</summary>
internal sealed class RecordingFilter(bool answer = true) : ITelemetryEventFilter
{
    private readonly List<TelemetryEvent> _seen = [];

    internal IReadOnlyList<TelemetryEvent> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    public bool ShouldSend(TelemetryEvent telemetryEvent)
    {
        lock (_seen)
        {
            _seen.Add(telemetryEvent);
        }

        return answer;
    }
}
