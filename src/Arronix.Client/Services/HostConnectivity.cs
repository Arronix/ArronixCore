using System.Globalization;
using System.Net.Http;
using Arronix.Client.Configuration;

namespace Arronix.Client.Services;

/// <summary>
/// Whether the server is answering, and the recovery loop that finds out when it starts again.
/// </summary>
/// <remarks>
/// <para>
/// The server holds every piece of data this application shows, so an installed client that loses it has
/// nothing to fall back on. What it must not do is fail the way an uninstalled page fails — a browser
/// error screen replacing the application. The shell is cached, so the application stays up and states
/// plainly what has happened, and this type is the state it states.
/// </para>
/// <para>
/// Recovery is automatic and unattended. A person who has just restarted the server should not have to
/// reload anything: the probe loop backs off up to a ceiling and, the moment a probe succeeds, everything
/// listening reloads itself.
/// </para>
/// </remarks>
public sealed class HostConnectivity : IDisposable
{
    private readonly HttpClient _http;
    private readonly ClientOptions _options;
    private readonly CancellationTokenSource _stopping = new();
    private TaskCompletionSource _nudge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _probing;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostConnectivity"/> class.
    /// </summary>
    /// <param name="http">The client used to probe the server.</param>
    /// <param name="options">The deployment's settings.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public HostConnectivity(HttpClient http, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options;
    }

    /// <summary>
    /// Occurs when <see cref="State"/> changes.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Occurs when the server starts answering again after having stopped.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="StateChanged"/> because it is the signal a view reloads on. Reloading on
    /// every state change would refetch on the way down as well as on the way up.
    /// </remarks>
    public event EventHandler? Recovered;

    /// <summary>
    /// Gets how the server is answering.
    /// </summary>
    public HostState State { get; private set; } = HostState.Unknown;

    /// <summary>
    /// Gets the number of probes made since the server stopped answering.
    /// </summary>
    public int ProbeAttempts { get; private set; }

    /// <summary>
    /// Gets when the next probe is due, when one is.
    /// </summary>
    public DateTimeOffset? NextProbeAt { get; private set; }

    /// <summary>
    /// Records that the server answered.
    /// </summary>
    public void ReportReachable()
    {
        var recovered = State is HostState.Unreachable;
        ProbeAttempts = 0;
        NextProbeAt = null;

        // A request succeeding says the server is there; it says nothing about whether live events are
        // arriving. Promoting a lost live connection to "online" here would clear the banner that is the
        // only warning a view is about to go quietly stale.
        if (State is not HostState.LiveConnectionLost)
        {
            Transition(HostState.Online);
        }

        if (recovered)
        {
            Recovered?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Records that the server did not answer, and starts probing for its return.
    /// </summary>
    public void ReportUnreachable()
    {
        Transition(HostState.Unreachable);
        StartProbing();
    }

    /// <summary>
    /// Brings the next probe forward to now, for a user who is not prepared to wait out the backoff.
    /// </summary>
    /// <remarks>
    /// Offered because the ceiling is half a minute and a person who has just fixed the thing knows
    /// perfectly well that it is fixed. It shortens the wait; it does not replace the loop.
    /// </remarks>
    public void ProbeNow()
    {
        StartProbing();
        _nudge.TrySetResult();
    }

    /// <summary>
    /// Records whether the live-event connection is up.
    /// </summary>
    /// <param name="connected">Whether the connection is up.</param>
    /// <remarks>
    /// A dropped live connection is not the same as an unreachable server: requests may still succeed and
    /// the only loss is that views stop updating themselves. Conflating the two would put a full-page
    /// notice in front of a user whose library is still perfectly usable.
    /// </remarks>
    public void ReportLiveConnection(bool connected)
    {
        if (connected)
        {
            if (State is not HostState.Unreachable)
            {
                Transition(HostState.Online);
            }

            return;
        }

        if (State is HostState.Online or HostState.Unknown)
        {
            Transition(HostState.LiveConnectionLost);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private void Transition(HostState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartProbing()
    {
        if (_probing || _disposed)
        {
            return;
        }

        _probing = true;
        _ = ProbeUntilReachableAsync();
    }

    private async Task ProbeUntilReachableAsync()
    {
        try
        {
            var delay = _options.ProbeInitialDelay;

            while (!_stopping.IsCancellationRequested && State is HostState.Unreachable)
            {
                NextProbeAt = DateTimeOffset.UtcNow.Add(delay);
                StateChanged?.Invoke(this, EventArgs.Empty);

                _nudge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await Task.WhenAny(Task.Delay(delay, _stopping.Token), _nudge.Task);
                _stopping.Token.ThrowIfCancellationRequested();

                ProbeAttempts++;

                if (await ProbeAsync().ConfigureAwait(false))
                {
                    ReportReachable();
                    return;
                }

                var next = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.ProbeMaximumDelay.Ticks));
                delay = next;
            }
        }
        catch (OperationCanceledException)
        {
            // The application is going away; nothing to recover to.
        }
        finally
        {
            _probing = false;
        }
    }

    private async Task<bool> ProbeAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri($"health?probe={ProbeAttempts.ToString(CultureInfo.InvariantCulture)}", UriKind.Relative));

            using var response = await _http.SendAsync(request, _stopping.Token).ConfigureAwait(false);

            // Any answer at all proves the server is there. A degraded platform still answers with 503,
            // and refusing to recover from that would leave the client dark for exactly the deployment
            // whose operator most needs to see the health panel.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// How the server is answering.
/// </summary>
public enum HostState
{
    /// <summary>Nothing has been asked of the server yet.</summary>
    Unknown = 0,

    /// <summary>The server is answering and pushing live events.</summary>
    Online = 1,

    /// <summary>Requests succeed, but live events have stopped arriving.</summary>
    LiveConnectionLost = 2,

    /// <summary>The server is not answering at all.</summary>
    Unreachable = 3
}
