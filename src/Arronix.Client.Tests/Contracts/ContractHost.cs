using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;

namespace Arronix.Client.Tests.Contracts;

/// <summary>A host a client can read an installation from, answered in memory.</summary>
internal static class ContractHost
{
    /// <summary>Connects to a host that offers nothing, so a read costs one manifest and no bytes.</summary>
    /// <returns>The connection.</returns>
    public static HttpClient OfferingNothing()
    {
        var manifest = new ClientContractManifest(
            MediaContractLoader.ClientContractIdentity,
            new string('B', 64),
            [],
            []);

        return Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(manifest, ApiJsonOptions.Default),
                Encoding.UTF8,
                "application/json"),
        });
    }

    /// <summary>Connects to a host answering however the caller says.</summary>
    /// <param name="answer">What to answer for one request path.</param>
    /// <returns>The connection.</returns>
    public static HttpClient Answering(Func<string, HttpResponseMessage> answer)
        => new(new StubHandler(answer)) { BaseAddress = new Uri("https://host.invalid/") };

    private sealed class StubHandler(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(answer(request.RequestUri!.AbsolutePath));
    }
}
