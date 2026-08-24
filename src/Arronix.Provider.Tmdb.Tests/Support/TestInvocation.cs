using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>Builds the invocation context every provider call needs, with a configured API key.</summary>
internal static class TestInvocation
{
    public static ProviderInvocation ForCataloger(string apiKey = "test-api-key") =>
        Build(ProviderFamily.Cataloger, "tmdb-movies", apiKey);

    public static ProviderInvocation ForCurator(string apiKey = "test-api-key") =>
        Build(ProviderFamily.Curator, "tmdb-popular", apiKey);

    private static ProviderInvocation Build(ProviderFamily family, string localId, string apiKey)
    {
        var plugin = PluginId.FromString("tmdb");

        var definition = new ProviderDefinition
        {
            Id = 1,
            Provider = ProviderId.Create(plugin, localId),
            Family = family,
            Name = "TMDb test definition",
            Settings = new Dictionary<string, string> { ["apiKey"] = apiKey },
        };

        return new ProviderInvocation(definition, new NoOpSessionStore(), "test-correlation");
    }

    private sealed class NoOpSessionStore : IProviderSessionStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(
            string key, string? value, TimeSpan? lifetime = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
