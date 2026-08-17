using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Http;

/// <summary>
/// Observes and rewrites outbound requests and responses as they pass through the gateway.
/// </summary>
/// <remarks>
/// <para>
/// This is the declared place for remote-specific workarounds — a challenge page that has to be solved
/// before the real request goes out, a response envelope that has to be unwrapped — so that such
/// knowledge lives with the extension that needs it instead of accumulating inside the gateway.
/// </para>
/// <para>
/// Interceptors run in registration order on the way out and in reverse on the way back. Both members
/// are asynchronous and take a token, because a workaround that has to make its own request is exactly
/// the case that motivates the seam.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Http, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IOutboundHttpInterceptor
{
    /// <summary>
    /// Inspects or rewrites a request before it is sent.
    /// </summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request to send, which may be the argument unchanged.</returns>
    Task<OutboundHttpRequest> OnRequestAsync(
        OutboundHttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects or rewrites a response before it is returned to the caller.
    /// </summary>
    /// <param name="response">The response received.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response to return, which may be the argument unchanged.</returns>
    Task<OutboundHttpResponse> OnResponseAsync(
        OutboundHttpResponse response,
        CancellationToken cancellationToken = default);
}
