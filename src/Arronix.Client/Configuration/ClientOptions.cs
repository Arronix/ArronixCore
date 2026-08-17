namespace Arronix.Client.Configuration;

/// <summary>
/// Everything the client needs to know about the deployment it is talking to.
/// </summary>
/// <remarks>
/// <para>
/// Bound by hand from configuration rather than through the platform's validated-options helper: that
/// helper lives in a host-side assembly this project may not reference, and pulling in an options-binding
/// package to replace it would buy nothing for six values. The section name is still declared once, here,
/// so the key is greppable exactly as it is elsewhere.
/// </para>
/// <para>
/// The address defaults to the origin the application was served from, which is the arrangement that
/// works without configuration; setting it explicitly is what allows the client to be hosted separately
/// from the server it talks to.
/// </para>
/// </remarks>
public sealed class ClientOptions
{
    /// <summary>
    /// The configuration section these values are read from.
    /// </summary>
    public const string SectionName = "Arronix:Client";

    /// <summary>
    /// Gets the address of the server, ending in a slash.
    /// </summary>
    public required Uri ServerAddress { get; init; }

    /// <summary>
    /// Gets the path of the live-event endpoint, relative to <see cref="ServerAddress"/>.
    /// </summary>
    public string EventHubPath { get; init; } = "hub/events";

    /// <summary>
    /// Gets the number of items requested per page.
    /// </summary>
    public int PageSize { get; init; } = 60;

    /// <summary>
    /// Gets the delay before the first attempt to reach a server that has stopped answering.
    /// </summary>
    public TimeSpan ProbeInitialDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the longest the client will wait between attempts to reach an unreachable server.
    /// </summary>
    public TimeSpan ProbeMaximumDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long a request may take before the server is treated as unreachable.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
}
