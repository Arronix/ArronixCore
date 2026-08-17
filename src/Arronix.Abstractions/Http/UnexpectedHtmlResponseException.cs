using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Http;

/// <summary>
/// Thrown when a remote returns browser content where a machine-readable payload was expected.
/// </summary>
/// <remarks>
/// This is usually an interstitial: a sign-in page, a bot check, or an outage notice served with a
/// success status. It gets its own type because it is not a transport failure and not a deserialization
/// bug, and because callers routinely want to treat it as temporary and retry later.
/// </remarks>
[Experimental(ExperimentalContracts.Http, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class UnexpectedHtmlResponseException : HttpGatewayException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnexpectedHtmlResponseException"/> class.
    /// </summary>
    public UnexpectedHtmlResponseException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnexpectedHtmlResponseException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public UnexpectedHtmlResponseException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnexpectedHtmlResponseException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public UnexpectedHtmlResponseException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnexpectedHtmlResponseException"/> class from the
    /// offending response.
    /// </summary>
    /// <param name="response">The response carrying browser content.</param>
    public UnexpectedHtmlResponseException(OutboundHttpResponse response)
        : base(
            response,
            "The remote returned browser content instead of the expected payload. "
            + $"This is often temporary: [{(response ?? throw new ArgumentNullException(nameof(response))).Request.Method}] {response.Request.Url}")
    {
    }
}
