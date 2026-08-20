using System.Net;
using System.Net.Mime;
using System.Text;

namespace Arronix.Abstractions.Http;

/// <summary>
/// The result of an outbound HTTP request.
/// </summary>
/// <remarks>
/// The body is exposed as bytes and decoded lazily, because the charset lives in a header that has to
/// be parsed first and because a caller that only wants the status should not pay for a decode.
/// </remarks>
public class OutboundHttpResponse
{
    private string? _content;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundHttpResponse"/> class.
    /// </summary>
    /// <param name="request">The request that produced this response.</param>
    /// <param name="headers">The response headers.</param>
    /// <param name="statusCode">The response status.</param>
    /// <param name="content">The response body, empty when it was streamed elsewhere.</param>
    /// <param name="version">The HTTP version the response was served over.</param>
    public OutboundHttpResponse(
        OutboundHttpRequest request,
        HttpHeaderCollection headers,
        HttpStatusCode statusCode,
        ReadOnlyMemory<byte> content,
        Version? version = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(headers);

        Request = request;
        Headers = headers;
        StatusCode = statusCode;
        ContentBytes = content;
        Version = version;
    }

    /// <summary>
    /// Gets the request that produced this response.
    /// </summary>
    public OutboundHttpRequest Request { get; }

    /// <summary>
    /// Gets the response headers.
    /// </summary>
    public HttpHeaderCollection Headers { get; }

    /// <summary>
    /// Gets the response status.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the HTTP version the response was served over, when the transport reported one.
    /// </summary>
    public Version? Version { get; }

    /// <summary>
    /// Gets the raw response body. Empty when the body was streamed to
    /// <see cref="OutboundHttpRequest.ResponseStream"/>.
    /// </summary>
    public ReadOnlyMemory<byte> ContentBytes { get; }

    /// <summary>
    /// Gets the response body decoded as text, using the charset from <c>Content-Type</c> and falling
    /// back to UTF-8 when it is absent or unrecognized.
    /// </summary>
    public string Content => _content ??= Decode();

    /// <summary>
    /// Gets a value indicating whether the status is 400 or above.
    /// </summary>
    public bool HasHttpError => (int)StatusCode >= 400;

    /// <summary>
    /// Gets a value indicating whether the status is 500 or above.
    /// </summary>
    public bool HasHttpServerError => (int)StatusCode >= 500;

    /// <summary>
    /// Gets a value indicating whether the status is one of the redirect statuses.
    /// </summary>
    public bool HasHttpRedirect => StatusCode is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Gets the raw <c>Set-Cookie</c> headers, in the order the server sent them.
    /// </summary>
    public IReadOnlyList<string> SetCookieHeaders => Headers.GetValues("Set-Cookie");

    /// <summary>
    /// Parses the <c>Set-Cookie</c> headers into name and value pairs.
    /// </summary>
    /// <returns>
    /// The cookies that apply to the request URL. Cookies the framework's parser rejects are skipped
    /// rather than failing the call.
    /// </returns>
    /// <remarks>
    /// Parsing goes through the framework's cookie machinery, which applies the domain, path and expiry
    /// rules. The hand-rolled expression this replaces took the first name and value it saw and treated
    /// attributes such as <c>Path</c> and <c>Expires</c> as cookies in their own right.
    /// </remarks>
    public IReadOnlyDictionary<string, string> GetCookies()
    {
        var container = new CookieContainer();

        foreach (var header in SetCookieHeaders)
        {
            try
            {
                container.SetCookies(Request.Url, header);
            }
            catch (CookieException)
            {
                // A malformed cookie is the server's problem, not a reason to fail the response.
            }
        }

        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Cookie cookie in container.GetCookies(Request.Url))
        {
            cookies[cookie.Name] = cookie.Value;
        }

        return cookies;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"[{(int)StatusCode} {StatusCode}] {Request.Method} {Request.Url} ({ContentBytes.Length} bytes)";

    private string Decode()
    {
        if (ContentBytes.IsEmpty)
        {
            return string.Empty;
        }

        return ResolveEncoding().GetString(ContentBytes.Span);
    }

    private Encoding ResolveEncoding()
    {
        var contentType = Headers.GetValues("Content-Type");

        if (contentType.Count == 0)
        {
            return Encoding.UTF8;
        }

        try
        {
            var charSet = new ContentType(contentType[0]).CharSet;

            return string.IsNullOrWhiteSpace(charSet) ? Encoding.UTF8 : Encoding.GetEncoding(charSet);
        }
        catch (FormatException)
        {
            return Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            // An encoding this runtime does not carry. UTF-8 is a better guess than failing.
            return Encoding.UTF8;
        }
    }
}

/// <summary>
/// An outbound response whose body has already been deserialized.
/// </summary>
/// <typeparam name="TResource">The deserialized payload type.</typeparam>
/// <remarks>
/// There is no <c>new()</c> constraint and no substitution of an empty instance for a missing payload.
/// Both were workarounds for an older serializer, and the second one is actively harmful: it reports a
/// null payload as a successfully deserialized empty object, so the caller cannot tell the difference.
/// </remarks>
public sealed class OutboundHttpResponse<TResource> : OutboundHttpResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundHttpResponse{TResource}"/> class.
    /// </summary>
    /// <param name="response">The underlying response.</param>
    /// <param name="resource">The deserialized payload, or <see langword="null"/> when there was none.</param>
    public OutboundHttpResponse(OutboundHttpResponse response, TResource? resource)
        : base(
            (response ?? throw new ArgumentNullException(nameof(response))).Request,
            response.Headers,
            response.StatusCode,
            response.ContentBytes,
            response.Version) => Resource = resource;

    /// <summary>
    /// Gets the deserialized payload, or <see langword="null"/> when the body was empty or JSON null.
    /// </summary>
    public TResource? Resource { get; }
}
