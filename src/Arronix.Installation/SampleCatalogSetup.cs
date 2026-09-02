using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Arronix.Installation;

/// <summary>
/// Configures the sample catalog once, so an installation can be evaluated the moment it opens.
/// </summary>
/// <remarks>
/// <para>
/// A cataloger has to be configured before anything routes work to it, and that is correct: the platform
/// does not decide on an operator's behalf which catalogs answer for a media kind. It does mean a brand
/// new installation shows an empty catalog until somebody adds a provider, which for a sample package
/// shipped specifically to be clicked through is friction with no purpose.
/// </para>
/// <para>
/// This adds exactly one definition, through the same public route the settings screen posts to, and only
/// for the sample package. It discovers the provider by asking which catalogers the running installation
/// admitted and taking the one that declared the sample scheme, so it never hard-codes a minted provider
/// identifier. It is idempotent: an installation that already has a definition for that provider is left
/// exactly as the operator left it, including a disabled or edited one.
/// </para>
/// </remarks>
internal static class SampleCatalogSetup
{
    /// <summary>The scheme the sample package is the identity authority for.</summary>
    public const string SampleScheme = "sample";

    /// <summary>
    /// Adds the sample catalog definition if the installation does not already have one.
    /// </summary>
    /// <param name="client">The client used to talk to the running server.</param>
    /// <param name="address">The server's address.</param>
    /// <param name="cancellationToken">Abandons the work.</param>
    /// <returns>What happened, in one sentence, or <see langword="null"/> when there was nothing to do.</returns>
    public static async Task<string?> EnsureConfiguredAsync(
        HttpClient client,
        Uri address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(address);

        var catalogers = await ReadArrayAsync(client, new Uri(address, "api/v1/providers?family=Cataloger"), cancellationToken)
            .ConfigureAwait(false);

        var sample = catalogers.FirstOrDefault(entry =>
            string.Equals((string?)entry?["catalogScheme"], SampleScheme, StringComparison.Ordinal));

        if (sample is null)
        {
            return null;
        }

        var provider = (string?)sample["provider"];
        var kind = (string?)sample["pairedMediaKind"];

        if (provider is null or { Length: 0 } || kind is null or { Length: 0 })
        {
            return null;
        }

        var definitions = await ReadArrayAsync(client, new Uri(address, "api/v1/providers/definitions"), cancellationToken)
            .ConfigureAwait(false);

        if (definitions.Any(definition =>
            string.Equals((string?)definition?["provider"], provider, StringComparison.Ordinal)))
        {
            return $"The sample catalog is already configured for {kind}.";
        }

        var body = new JsonObject
        {
            ["id"] = 0,
            ["provider"] = provider,
            ["family"] = "cataloger",
            ["name"] = "Sample movie catalog",
            ["enabled"] = true,
            ["settings"] = new JsonObject(),
            ["mediaKinds"] = new JsonArray(kind),
        };

        using var response = await client
            .PostAsJsonAsync(new Uri(address, "api/v1/providers/definitions"), body, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new InstallationException(
                $"The sample catalog could not be configured (HTTP {(int)response.StatusCode}): {detail}");
        }

        return $"Configured the sample catalog for {kind}.";
    }

    private static async Task<IReadOnlyList<JsonNode?>> ReadArrayAsync(
        HttpClient client,
        Uri address,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(address, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InstallationException(
                $"The running installation refused {address} with HTTP {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return JsonNode.Parse(payload) is JsonArray entries
            ? [.. entries]
            : [];
    }
}
