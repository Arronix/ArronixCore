using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Http;

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>
/// A minimal, real <see cref="IHttpGateway"/> backed by an actual <see cref="HttpClient"/>, standing in
/// for the Host-owned gateway a real installation would supply through <c>IPluginContext</c>.
/// </summary>
/// <remarks>
/// The real gateway lives in <c>Arronix.Plugins</c>, which this provider package must not reference. This
/// is a faithful but minimal substitute: it performs the same request/response translation
/// <see cref="IHttpGateway"/> promises, over a real <see cref="HttpClient"/> whose only fake part is its
/// <see cref="HttpMessageHandler"/>. It is not a test double of the provider under test.
/// </remarks>
internal sealed class TestHttpGateway(HttpClient client) : IHttpGateway, IDisposable
{
    /// <inheritdoc />
    public async Task<OutboundHttpResponse> ExecuteAsync(
        OutboundHttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(request.Method, request.Url);
        if (!request.Content.IsEmpty)
        {
            message.Content = new ByteArrayContent(request.Content.ToArray());
        }

        using var response = await client
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var headers = new HttpHeaderCollection(response.Headers
            .Concat(response.Content.Headers)
            .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value)));

        var outbound = new OutboundHttpResponse(request, headers, response.StatusCode, body, response.Version);

        if (!request.SuppressHttpError
            && outbound.HasHttpError
            && !request.SuppressedStatusCodes.Contains(outbound.StatusCode))
        {
            throw new HttpGatewayException(outbound);
        }

        return outbound;
    }

    /// <inheritdoc />
    public Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
        OutboundHttpRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This harness exercises the untyped ExecuteAsync only; the TMDb provider deserializes TMDb's own JSON shape itself.");

    /// <inheritdoc />
    public Task<OutboundHttpResponse> DownloadAsync(
        OutboundHttpRequest request, Stream destination, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This harness has no download scenario.");

    /// <inheritdoc />
    public void Dispose() => client.Dispose();
}
