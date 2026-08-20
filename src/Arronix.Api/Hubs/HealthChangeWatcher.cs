using Arronix.Abstractions.Health;
using Arronix.Abstractions.Wire;
using Arronix.Api.Configuration;
using Arronix.Api.Endpoints;
using Arronix.Api.Serialization;
using Arronix.Host.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


#pragma warning disable CA1848 // Source-generated log delegates buy an allocation on a hot path;
                               // every call site in this file is a startup event or a rare failure.

namespace Arronix.Api.Hubs;

/// <summary>
/// Watches the platform's health and pushes a change in it to connected clients.
/// </summary>
/// <remarks>
/// <para>
/// Health is the one part of the surface a client cannot learn about by observing the consequences of its
/// own requests: nothing the user did causes a mount to disappear or an extension to be quarantined. The
/// aggregate is already cached with its own lifetime and invalidated when extensions and providers change
/// state, so reading it on a timer costs a dictionary lookup in the common case.
/// </para>
/// <para>
/// Only a change in the overall status is pushed, not every reading. A feed that repeats "still healthy"
/// every half minute is a feed people learn to ignore.
/// </para>
/// </remarks>
internal sealed class HealthChangeWatcher : BackgroundService
{
    private readonly IHealthAggregator _health;
    private readonly EventBroadcaster _broadcaster;
    private readonly TimeProvider _clock;
    private readonly IOptionsMonitor<ApiOptions> _options;
    private readonly ILogger<HealthChangeWatcher> _logger;

    private HealthStatus? _last;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthChangeWatcher"/> class.
    /// </summary>
    /// <param name="health">The aggregate this watcher reads.</param>
    /// <param name="broadcaster">The translator envelopes are published through.</param>
    /// <param name="clock">The clock the polling interval is measured against.</param>
    /// <param name="options">The API settings, read live so the interval can be changed without a restart.</param>
    /// <param name="logger">The logger.</param>
    public HealthChangeWatcher(
        IHealthAggregator health,
        EventBroadcaster broadcaster,
        TimeProvider clock,
        IOptionsMonitor<ApiOptions> options,
        ILogger<HealthChangeWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _health = health;
        _broadcaster = broadcaster;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CurrentValue.HealthPollInterval, _clock, stoppingToken).ConfigureAwait(false);

                var snapshot = await _health.CollectAsync(stoppingToken).ConfigureAwait(false);
                if (_last == snapshot.Status)
                {
                    continue;
                }

                _last = snapshot.Status;

                await _broadcaster.BroadcastAsync(
                    new EventEnvelope
                    {
                        Kind = EventKind.HealthChanged,
                        At = snapshot.CheckedAt,
                        State = WireText.Name(snapshot.Status),
                        Health = HealthProjection.ToView(snapshot),
                    },
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failure to read health must not take the watcher down; the next tick retries.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogWarning(exception, "Could not read the platform's health; retrying on the next interval.");
            }
        }
    }
}
