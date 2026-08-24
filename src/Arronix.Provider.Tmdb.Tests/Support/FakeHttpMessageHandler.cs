using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>A fake transport that answers outbound requests from a supplied delegate.</summary>
/// <remarks>
/// Only the socket is faked here. Everything above it — the provider's request building, the gateway's
/// request/response translation, and the provider's own JSON parsing — runs for real.
/// </remarks>
internal sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    /// <summary>Gets every request this handler answered, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return await respond(request, cancellationToken).ConfigureAwait(false);
    }
}
