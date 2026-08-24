using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>Assembles the smallest real HTTP boundary a provider test needs.</summary>
internal static class TestHarness
{
    public static readonly DateTimeOffset DefaultNow = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a plugin context whose gateway answers through a fake transport.</summary>
    /// <param name="respond">Produces the response for each request the provider sends.</param>
    /// <param name="now">The clock's fixed instant, defaulting to <see cref="DefaultNow"/>.</param>
    public static TestPluginContext CreateContext(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        DateTimeOffset? now = null)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var client = new HttpClient(handler);
        var gateway = new TestHttpGateway(client);
        var clock = new FakeTimeProvider(now ?? DefaultNow);

        return new TestPluginContext(gateway, clock);
    }

    /// <summary>Creates a context that always answers with one fixed response.</summary>
    public static TestPluginContext CreateContext(
        Func<HttpRequestMessage, HttpResponseMessage> respond, DateTimeOffset? now = null) =>
        CreateContext((request, _) => Task.FromResult(respond(request)), now);
}
