using System.IO;

namespace Arronix.Abstractions.Http;

/// <summary>
/// The only supported way to make an outbound HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// Everything the platform applies to outbound traffic — throttling, the cookie jar, the interceptor
/// pipeline, proxy resolution, the certificate policy, redaction of logged URLs — is applied here. Code
/// that constructs its own client bypasses all of it.
/// </para>
/// <para>
/// The gateway is also the enforcement point for network access: extensions receive a per-extension
/// view that stamps their identity into the throttling partition and the user agent and applies their
/// allowed-host list. An extension with no network-implying capability is never handed one at all,
/// which only works because this contract lives here rather than in the implementation assembly.
/// </para>
/// <para>
/// Every member is asynchronous. The synchronous members on the interface this replaces all blocked on
/// an asynchronous call underneath.
/// </para>
/// </remarks>
public interface IHttpGateway
{
    /// <summary>
    /// Sends a request and buffers the response.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    /// <exception cref="HttpGatewayException">
    /// The request failed, or returned an error status the request did not suppress.
    /// </exception>
    /// <exception cref="HttpRateLimitedException">The remote answered 429.</exception>
    Task<OutboundHttpResponse> ExecuteAsync(
        OutboundHttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and deserializes the response body using the platform's JSON serializer.
    /// </summary>
    /// <typeparam name="TResource">The payload type.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, with the payload deserialized.</returns>
    /// <exception cref="HttpGatewayException">
    /// The request failed, or returned an error status the request did not suppress.
    /// </exception>
    /// <exception cref="UnexpectedHtmlResponseException">
    /// The remote returned browser content instead of a payload.
    /// </exception>
    Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
        OutboundHttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and streams the response body to a destination instead of buffering it.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="destination">The stream to write the body to. Not disposed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, whose buffered body is empty.</returns>
    /// <remarks>
    /// The destination is a stream rather than a path deliberately: choosing where bytes land on disk is
    /// the file system contract's job, and it is the only place the caller's granted roots are enforced.
    /// </remarks>
    Task<OutboundHttpResponse> DownloadAsync(
        OutboundHttpRequest request,
        Stream destination,
        CancellationToken cancellationToken = default);
}
