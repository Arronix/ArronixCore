using System.Net.Http;

namespace Arronix.Abstractions.Http;

/// <summary>
/// Thrown when an outbound request fails or returns an unsuppressed error status.
/// </summary>
/// <remarks>
/// It derives from <see cref="HttpRequestException"/> so that catch blocks and resilience pipelines
/// written against the framework's HTTP stack keep working, and so that
/// <see cref="HttpRequestException.StatusCode"/> carries the status without the caller reaching into
/// <see cref="Response"/>.
/// </remarks>
public class HttpGatewayException : HttpRequestException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpGatewayException"/> class.
    /// </summary>
    public HttpGatewayException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpGatewayException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public HttpGatewayException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpGatewayException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public HttpGatewayException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpGatewayException"/> class from a response.
    /// </summary>
    /// <param name="response">The response that failed.</param>
    public HttpGatewayException(OutboundHttpResponse response)
        : this(response, BuildMessage(response))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpGatewayException"/> class from a response.
    /// </summary>
    /// <param name="response">The response that failed.</param>
    /// <param name="message">The message describing the failure.</param>
    public HttpGatewayException(OutboundHttpResponse response, string? message)
        : base(message, null, (response ?? throw new ArgumentNullException(nameof(response))).StatusCode)
    {
        Request = response.Request;
        Response = response;
    }

    /// <summary>
    /// Gets the request that failed, when the failure happened after the request was built.
    /// </summary>
    public OutboundHttpRequest? Request { get; }

    /// <summary>
    /// Gets the response that failed, when one was received.
    /// </summary>
    public OutboundHttpResponse? Response { get; }

    /// <summary>
    /// Builds the default message for a failed response.
    /// </summary>
    /// <param name="response">The response that failed.</param>
    /// <returns>The message.</returns>
    protected static string BuildMessage(OutboundHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return $"HTTP request failed with {(int)response.StatusCode} {response.StatusCode}: "
            + $"[{response.Request.Method}] {response.Request.Url}";
    }
}
