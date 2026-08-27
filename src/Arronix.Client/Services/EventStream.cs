
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Wire;
using Arronix.Client.Configuration;
using Arronix.Client.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Client.Services;

/// <summary>
/// The live connection over which the platform pushes what is happening.
/// </summary>
/// <remarks>
/// <para>
/// Queue movement, job progress, health and item changes all arrive here rather than being polled for.
/// Polling would have to run at the rate of the fastest thing worth seeing, against a server whose whole
/// job is to be busy doing something else.
/// </para>
/// <para>
/// The connection joins a delivery group per media kind so that someone looking at one library is not
/// woken by churn in another, and a group for platform-wide traffic that is always joined. Subscriptions
/// are reapplied after a reconnect, because a reconnect is a new connection to the server and it
/// remembers nothing about the last one.
/// </para>
/// </remarks>
public sealed class EventStream : IAsyncDisposable
{
    private const string EventMethodName = "event";
    private const string SubscribeMethodName = "Subscribe";
    private const string UnsubscribeMethodName = "Unsubscribe";

    private readonly ClientOptions _options;
    private readonly HostConnectivity _connectivity;
    private readonly HashSet<string> _subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventStream"/> class.
    /// </summary>
    /// <param name="options">The deployment's settings.</param>
    /// <param name="connectivity">Where connection outcomes are reported.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public EventStream(ClientOptions options, HostConnectivity connectivity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectivity);

        _options = options;
        _connectivity = connectivity;
    }

    /// <summary>
    /// Occurs when the platform pushes an event.
    /// </summary>
    public event EventHandler<EventEnvelope>? Received;

    /// <summary>
    /// Occurs when the connection goes up or comes down.
    /// </summary>
    public event EventHandler? ConnectionChanged;

    /// <summary>Raises <see cref="Received"/> for one envelope the platform pushed.</summary>
    /// <param name="envelope">What happened.</param>
    internal void Publish(EventEnvelope envelope) => Received?.Invoke(this, envelope);

    /// <summary>
    /// Gets a value indicating whether the connection is up.
    /// </summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Opens the connection, if it is not open already.
    /// </summary>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    /// <returns>A task that completes when the attempt has finished, successfully or not.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        var failed = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _connection ??= Build();

            if (_connection.State is not HubConnectionState.Disconnected)
            {
                return;
            }

            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
            _connectivity.ReportLiveConnection(true);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);

            foreach (var group in _subscriptions)
            {
                await _connection.InvokeAsync(SubscribeMethodName, group, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            failed = true;
            _connectivity.ReportLiveConnection(false);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }

        if (failed)
        {
            // A connection that never opened raises no closed event, so nothing else would ever try
            // again. Scheduled after the gate is released, so the retry is not waiting on itself.
            _ = RetryLaterAsync();
        }
    }

    /// <summary>
    /// Joins the delivery group for one media kind.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    /// <returns>A task that completes when the group has been joined, or the attempt abandoned.</returns>
    public async Task SubscribeAsync(MediaKindId kind, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.Add(kind.Value) || _connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            await _connection.InvokeAsync(SubscribeMethodName, kind.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The reconnect handler reapplies every subscription, so a failure here costs nothing.
        }
    }

    /// <summary>
    /// Leaves the delivery group for one media kind.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    /// <returns>A task that completes when the group has been left, or the attempt abandoned.</returns>
    public async Task UnsubscribeAsync(MediaKindId kind, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.Remove(kind.Value) || _connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            await _connection.InvokeAsync(UnsubscribeMethodName, kind.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Leaving a group is advisory; the worst outcome is one more event than was wanted.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection is { } connection)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private async Task RetryLaterAsync()
    {
        await Task.Delay(_options.ProbeMaximumDelay).ConfigureAwait(false);

        if (!_disposed && !IsConnected)
        {
            await StartAsync().ConfigureAwait(false);
        }
    }

    private HubConnection Build()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_options.ServerAddress, _options.EventHubPath))
            .WithAutomaticReconnect()
            .AddJsonProtocol(protocol => ApiJsonOptions.Configure(protocol.PayloadSerializerOptions))
            .Build();

        connection.On<EventEnvelope>(EventMethodName, Publish);

        connection.Reconnecting += _ =>
        {
            _connectivity.ReportLiveConnection(false);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };

        connection.Reconnected += async _ =>
        {
            _connectivity.ReportLiveConnection(true);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);

            foreach (var group in _subscriptions)
            {
                try
                {
                    await connection.InvokeAsync(SubscribeMethodName, group).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Another reconnect will reapply them.
                }
            }
        };

        connection.Closed += async _ =>
        {
            _connectivity.ReportLiveConnection(false);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);

            if (_disposed)
            {
                return;
            }

            await Task.Delay(_options.ProbeInitialDelay).ConfigureAwait(false);
            await StartAsync().ConfigureAwait(false);
        };

        return connection;
    }
}
