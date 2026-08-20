using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Arronix.Abstractions.Http;

/// <summary>
/// A re-sendable description of an outbound HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// The framework's own request message is single-use: once sent it cannot be sent again, which makes it
/// unusable as the unit a retry, a redirect follow or an interceptor pipeline passes around. This type
/// is a specification rather than a message, so the gateway can build a fresh framework message from it
/// as many times as it needs to.
/// </para>
/// <para>
/// It also carries the policy the gateway applies on the caller's behalf — throttling partition,
/// redirect handling, error suppression — which is information a transport-level message has nowhere to
/// put.
/// </para>
/// </remarks>
public sealed class OutboundHttpRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundHttpRequest"/> class.
    /// </summary>
    /// <param name="url">The absolute request URL.</param>
    public OutboundHttpRequest(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Url = url;
    }

    /// <summary>
    /// Gets or sets the absolute request URL.
    /// </summary>
    public Uri Url { get; set; }

    /// <summary>
    /// Gets or sets the request method. Defaults to <see cref="HttpMethod.Get"/>.
    /// </summary>
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    /// <summary>
    /// Gets the request headers.
    /// </summary>
    public HttpHeaderCollection Headers { get; } = new();

    /// <summary>
    /// Gets the cookies to send with the request, in addition to anything in the gateway's jar.
    /// </summary>
    public IDictionary<string, string> Cookies { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the request body.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; set; }

    /// <summary>
    /// Gets or sets a short, safe description of the body for logs. Set this rather than logging the
    /// body itself when the body carries credentials.
    /// </summary>
    public string? ContentSummary { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the gateway should follow redirects. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AllowAutoRedirect { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an error status should be returned to the caller instead
    /// of throwing.
    /// </summary>
    public bool SuppressHttpError { get; set; }

    /// <summary>
    /// Gets or sets the specific error statuses that should be returned instead of throwing, when
    /// <see cref="SuppressHttpError"/> is <see langword="false"/>.
    /// </summary>
    public IReadOnlyCollection<HttpStatusCode> SuppressedStatusCodes { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the response body may be written to the log. Off by
    /// default because response bodies routinely contain credentials.
    /// </summary>
    public bool LogResponseContent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cookies sent with this request are stored in the
    /// gateway's jar. Defaults to <see langword="true"/>.
    /// </summary>
    public bool StoreRequestCookie { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether cookies returned by the response are stored in the
    /// gateway's jar.
    /// </summary>
    public bool StoreResponseCookie { get; set; }

    /// <summary>
    /// Gets or sets the per-request timeout, or <see langword="null"/> to use the gateway's default.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the minimum interval between requests sharing <see cref="RateLimitKey"/>, or
    /// <see langword="null"/> for no additional throttling beyond the gateway's own.
    /// </summary>
    public TimeSpan? RateLimit { get; set; }

    /// <summary>
    /// Gets or sets the throttling partition, or <see langword="null"/> to derive one from the host.
    /// The host composes the caller's identity into the effective partition.
    /// </summary>
    public string? RateLimitKey { get; set; }

    /// <summary>
    /// Gets or sets a stream to write the response body to instead of buffering it in memory. Use this
    /// for downloads; leaving it <see langword="null"/> buffers the whole body.
    /// </summary>
    public Stream? ResponseStream { get; set; }

    /// <summary>
    /// Sets the request body from text.
    /// </summary>
    /// <param name="content">The text to send.</param>
    /// <param name="encoding">The encoding to use. Defaults to UTF-8.</param>
    public void SetContent(string content, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = (encoding ?? Encoding.UTF8).GetBytes(content);
    }

    /// <inheritdoc />
    public override string ToString() => ContentSummary is null
        ? $"[{Method}] {Url}"
        : $"[{Method}] {Url}: {ContentSummary}";
}
