using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace Arronix.Abstractions.Http;

/// <summary>
/// Thrown when a remote answers with 429 Too Many Requests.
/// </summary>
/// <remarks>
/// <para>
/// Extensions catch this to back off, so it has to be catchable without referencing an implementation
/// assembly.
/// </para>
/// <para>
/// The <c>Retry-After</c> header has two forms — a delay in seconds and an absolute HTTP date — and both
/// are parsed here through the framework's own header parser. Parsing the date form by hand against the
/// ambient culture, as the code this replaces did, silently produced a wrong instant or none at all on
/// any machine not configured in English.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Http, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class HttpRateLimitedException : HttpGatewayException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRateLimitedException"/> class.
    /// </summary>
    public HttpRateLimitedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRateLimitedException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public HttpRateLimitedException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRateLimitedException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public HttpRateLimitedException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRateLimitedException"/> class from a response,
    /// reading <c>Retry-After</c> if the remote sent one.
    /// </summary>
    /// <param name="response">The throttled response.</param>
    public HttpRateLimitedException(OutboundHttpResponse response)
        : base(response, BuildMessage(response))
    {
        var header = response.Headers.GetValues("Retry-After");

        if (header.Count == 0 || !RetryConditionHeaderValue.TryParse(header[0], out var retryCondition))
        {
            return;
        }

        RetryAfterDelta = retryCondition.Delta;
        RetryAfterDate = retryCondition.Date;
    }

    /// <summary>
    /// Gets the delay the remote asked for, when it expressed <c>Retry-After</c> as a number of seconds.
    /// </summary>
    public TimeSpan? RetryAfterDelta { get; }

    /// <summary>
    /// Gets the instant the remote said to retry after, when it expressed <c>Retry-After</c> as a date.
    /// </summary>
    public DateTimeOffset? RetryAfterDate { get; }

    /// <summary>
    /// Resolves how long to wait before retrying.
    /// </summary>
    /// <param name="utcNow">
    /// The current instant, supplied by the caller's clock so that the result is deterministic and the
    /// exception itself never reads a clock.
    /// </param>
    /// <returns>
    /// The delay, never negative, or <see langword="null"/> when the remote gave no usable
    /// <c>Retry-After</c> and the caller should apply its own back-off.
    /// </returns>
    public TimeSpan? GetRetryAfter(DateTimeOffset utcNow)
    {
        if (RetryAfterDelta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (RetryAfterDate is { } date)
        {
            var remaining = date - utcNow;

            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        return null;
    }
}
