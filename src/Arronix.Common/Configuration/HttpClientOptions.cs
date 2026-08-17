using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Configuration;

/// <summary>
/// Operator control over the outbound HTTP stack: how long it waits, how far it follows a redirect chain
/// and what it sends on every request.
/// </summary>
public sealed class HttpClientOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Http";

    /// <summary>
    /// Gets or sets how long a single request may take end to end before it is abandoned. A caller that
    /// needs longer — a large download, for instance — sets a timeout on the request rather than raising
    /// this ceiling for everything.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:30:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Gets or sets how long establishing a connection may take. Kept well below
    /// <see cref="RequestTimeout"/> so an unreachable host is reported as unreachable rather than as slow.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long the connection racing algorithm waits for one address family before starting
    /// an attempt on the next. The default is the value recommended by the specification for that
    /// algorithm; lowering it makes a host with a broken address on one family recover faster at the cost
    /// of more connection attempts.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:00:05", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan ConnectionAttemptDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets how long a pooled connection handler is reused before it is rotated, which is what
    /// lets a long-running process observe a DNS change.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:10", "24:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan HandlerLifetime { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets how many redirects are followed before the chain is treated as a loop. Zero disables
    /// redirect following entirely.
    /// </summary>
    [Range(0, 20)]
    public int MaxAutomaticRedirects { get; set; } = 5;

    /// <summary>
    /// Gets the headers applied to every outbound request that does not set them itself. The user agent is
    /// not among them: it is derived from the host identity and applied centrally, so that it cannot be
    /// silently replaced by configuration.
    /// </summary>
    public IDictionary<string, string> DefaultRequestHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
