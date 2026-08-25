using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>Builds the invocation context every provider call needs, with a configured Read Access Token.</summary>
internal static class TestInvocation
{
    public static ProviderInvocation ForCataloger(string readAccessToken = "test-read-access-token") =>
        Build(ProviderFamily.Cataloger, "tmdb-movies", readAccessToken, baseUrl: null);

    public static ProviderInvocation ForCurator(string readAccessToken = "test-read-access-token") =>
        Build(ProviderFamily.Curator, "tmdb-popular", readAccessToken, baseUrl: null);

    /// <summary>Builds a cataloger invocation with a specific, otherwise-invalid configured base URL.</summary>
    public static ProviderInvocation ForCatalogerWithBaseUrl(string baseUrl) =>
        Build(ProviderFamily.Cataloger, "tmdb-movies", "test-read-access-token", baseUrl);

    private static ProviderInvocation Build(ProviderFamily family, string localId, string readAccessToken, string? baseUrl)
    {
        var plugin = PluginId.FromString("tmdb");

        var settings = new Dictionary<string, string> { ["readAccessToken"] = readAccessToken };
        if (baseUrl is not null)
        {
            settings["baseUrl"] = baseUrl;
        }

        var definition = new ProviderDefinition
        {
            Id = 1,
            Provider = ProviderId.Create(plugin, localId),
            Family = family,
            Name = "TMDb test definition",
            Settings = settings,
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
