using Arronix.Client.Composition;
using Arronix.Client.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Composition;

/// <summary>What the client's own composition builds, over the container it registers into.</summary>
[TestFixture]
internal sealed class ClientCompositionTests
{
    /// <summary>The payload loader resolves, and resolves to one instance.</summary>
    /// <remarks>
    /// It is registered by factory because its constructor is internal. Resolving it is the only way to see
    /// that works: a container validates a factory registration without ever calling it.
    /// </remarks>
    [Test]
    public async Task ThePayloadLoaderResolvesAsOneSingleton()
    {
        var services = new ServiceCollection();

        // What Blazor's host supplies and AddArronixClient does not.
        services.AddSingleton<IJSRuntime>(new RefusingJsRuntime());
        services.AddArronixClient(new ConfigurationBuilder().Build(), new Uri("http://127.0.0.1:5223/"));

        await using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<ContractPayloadLoader>();

        resolve.Should().NotThrow("a registration the container cannot build is a page that cannot render");
        resolve().Should().BeSameAs(resolve(), "one browser holds one record of what it has loaded");
    }

    /// <summary>A script host with no browser behind it.</summary>
    private sealed class RefusingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException("There is no browser here.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new NotSupportedException("There is no browser here.");
    }
}
