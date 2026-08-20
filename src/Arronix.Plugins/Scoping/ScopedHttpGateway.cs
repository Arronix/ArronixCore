using System.Globalization;
using System.IO;
using System.Linq;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;


namespace Arronix.Plugins.Scoping;

/// <summary>
/// Attributes, throttles and confines one extension's outbound calls.
/// </summary>
/// <remarks>
/// <para>
/// Three jobs, all of which the contract already promises and none of which anything delivered until now.
/// </para>
/// <para>
/// It attributes: the extension identifier is appended to the user agent, so a remote operator who wants to
/// know which part of the platform is calling can read it off the request rather than guess. It throttles:
/// when a caller leaves the throttling partition unset, the extension identifier and the remote host become
/// the partition, so one extension hammering a remote cannot spend another's budget. And it confines: an
/// operator may allow or deny remote hosts per extension, which is the only lever that exists for an
/// extension that has the network privilege but should not be reaching a particular destination.
/// </para>
/// <para>
/// A denied host fails with the gateway's own failure type rather than a bespoke one, so an extension that
/// already handles a refused call handles this too.
/// </para>
/// </remarks>
public sealed class ScopedHttpGateway : IHttpGateway
{
    private readonly IHttpGateway _inner;
    private readonly string[] _allowedHosts;
    private readonly string[] _deniedHosts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopedHttpGateway"/> class.
    /// </summary>
    /// <param name="inner">The unattributed gateway.</param>
    /// <param name="plugin">The extension making the calls.</param>
    /// <param name="allowedHosts">
    /// The only remote hosts the extension may reach, or an empty list to allow any host that is not denied.
    /// </param>
    /// <param name="deniedHosts">Remote hosts the extension may never reach.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public ScopedHttpGateway(
        IHttpGateway inner,
        PluginId plugin,
        IReadOnlyList<string>? allowedHosts = null,
        IReadOnlyList<string>? deniedHosts = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        Plugin = plugin;
        _allowedHosts = Clean(allowedHosts);
        _deniedHosts = Clean(deniedHosts);
    }

    /// <summary>
    /// Gets the extension making the calls.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Determines whether the extension may reach a host.
    /// </summary>
    /// <param name="host">The remote host name.</param>
    /// <returns><see langword="true"/> when the call is permitted.</returns>
    public bool IsHostPermitted(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (_deniedHosts.Any(denied => Matches(host, denied)))
        {
            return false;
        }

        return _allowedHosts.Length == 0 || _allowedHosts.Any(allowed => Matches(host, allowed));
    }

    /// <inheritdoc />
    public Task<OutboundHttpResponse> ExecuteAsync(
        OutboundHttpRequest request,
        CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync(Scope(request), cancellationToken);

    /// <inheritdoc />
    public Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
        OutboundHttpRequest request,
        CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync<TResource>(Scope(request), cancellationToken);

    /// <inheritdoc />
    public Task<OutboundHttpResponse> DownloadAsync(
        OutboundHttpRequest request,
        Stream destination,
        CancellationToken cancellationToken = default)
        => _inner.DownloadAsync(Scope(request), destination, cancellationToken);

    /// <summary>
    /// Stamps the extension's identity onto a request and checks its destination.
    /// </summary>
    /// <param name="request">The request the extension built.</param>
    /// <returns>The same request, stamped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HttpGatewayException">The extension may not reach the destination.</exception>
    private OutboundHttpRequest Scope(OutboundHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.Url.Host;

        if (!IsHostPermitted(host))
        {
            throw new HttpGatewayException(
                $"Extension '{Plugin}' may not reach '{host}'. The host is outside the destinations this extension is permitted.");
        }

        // Only filled in when the caller left it unset: an extension that partitions its own throttling
        // deliberately — one budget per indexer, say — knows better than the decorator does.
        request.RateLimitKey ??= string.Create(CultureInfo.InvariantCulture, $"{Plugin}|{host}");

        var comment = $"(+{Plugin})";
        var existing = request.Headers.UserAgent;

        if (string.IsNullOrWhiteSpace(existing))
        {
            request.Headers.UserAgent = comment;
        }
        else if (!existing.Contains(comment, StringComparison.Ordinal))
        {
            request.Headers.UserAgent = $"{existing} {comment}";
        }

        return request;
    }

    private static string[] Clean(IReadOnlyList<string>? hosts)
    {
        if (hosts is null || hosts.Count == 0)
        {
            return [];
        }

        var cleaned = new List<string>(hosts.Count);
        foreach (var host in hosts)
        {
            if (!string.IsNullOrWhiteSpace(host))
            {
                cleaned.Add(host.Trim());
            }
        }

        return [.. cleaned];
    }

    /// <remarks>
    /// A pattern beginning with a dot matches the domain and everything beneath it, which is the only form
    /// of wildcard an operator needs and the only one whose meaning is unambiguous.
    /// </remarks>
    private static bool Matches(string host, string pattern)
    {
        if (pattern.StartsWith('.'))
        {
            return host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
                || host.Equals(pattern[1..], StringComparison.OrdinalIgnoreCase);
        }

        return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
