using Arronix.Abstractions.Wire;
using Arronix.Plugins.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;


namespace Arronix.Api.Endpoints;

/// <summary>
/// Serves the contract assemblies a browser client is entitled to load, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the second thing that crosses the client/server split, and it is the opposite of the
/// first.</strong> A media-kind descriptor is inert declared data that describes a shape; these routes
/// serve the actual CLR assembly that <i>is</i> the shape. Both are needed and neither replaces the other:
/// the descriptor is how a generic surface presents an installed kind without knowing it, and the contract
/// assembly is how a client deserializes and reasons about the kind's own typed values without a compiled
/// dependency on them.
/// </para>
/// <para>
/// What keeps that safe is the facet rule rather than a filter written here. A package declares which of
/// its published shared contract assemblies a client may have; a shared contract assembly is by
/// construction the half with no module, no parser, no provider implementation and no I/O; and the catalog
/// serves the bytes this host admitted, by content hash, or refuses. There is no route by which an entry
/// assembly, a server-only contract or a file this installation never admitted can be requested.
/// </para>
/// <para>
/// The byte route is content-addressed, and that is what makes the cache protocol work with no
/// invalidation message anywhere: an address names bytes, so it can be cached forever, and an installation
/// that changes mints new addresses rather than changing what an old one means. A client holding a stale
/// address is told its address is superseded instead of being handed different bytes under it.
/// </para>
/// </remarks>
internal static class ClientContractEndpoints
{
    /// <summary>
    /// Maps the client contract routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapClientContractEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var contracts = group.MapGroup("/client-contracts").WithTags("Client contracts");

        contracts.MapGet("/", GetManifest)
            .WithName("GetClientContractManifest")
            .WithSummary("Lists the contract assemblies a browser client may load from this host.")
            .WithDescription(
                "The universal contract identity a client must already carry, one hash over the whole "
                + "installation, and each publishing package with its client-safe assemblies and its "
                + "transitive client dependency closure.");

        contracts.MapGet("/{package}/{contentHash}/{fileName}", GetAssembly)
            .WithName("GetClientContractAssembly")
            .WithSummary("Returns the exact admitted bytes of one client-safe contract assembly.")
            .WithDescription(
                "Content-addressed. The bytes at an address never change, so a client may cache them "
                + "indefinitely; an address this installation no longer publishes is refused as superseded "
                + "rather than answered with different bytes.");

        return contracts;
    }

    /// <remarks>
    /// Never cached. This document is how a client discovers that anything else has changed, so a cached
    /// copy pins a browser to an installation the host is no longer running — the same rule the boot
    /// manifest and the service worker's asset list are held to.
    /// </remarks>
    private static Ok<ClientContractManifest> GetManifest(
        IClientContractCatalog catalog,
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        return TypedResults.Ok(catalog.Manifest());
    }

    private static Results<FileContentHttpResult, NotFound<string>, StatusCodeHttpResult> GetAssembly(
        IClientContractCatalog catalog,
        HttpContext context,
        string package,
        string contentHash,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);

        var found = catalog.Open(package, fileName, contentHash);

        switch (found.Outcome)
        {
            case ClientContractOutcome.Served:
                // An address that names its own bytes can be held for as long as the client likes. The
                // client's own store is keyed by the same hash, so this header removes a conditional
                // request rather than deciding correctness.
                context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
                context.Response.Headers[HeaderNames.ETag] = "\"" + contentHash + "\"";
                context.Response.Headers["Arronix-Assembly-Identity"] = found.Identity;

                return TypedResults.File(
                    found.Content.ToArray(),
                    "application/octet-stream",
                    fileName);

            case ClientContractOutcome.Superseded:
                // Gone rather than not-found, and the distinction is the whole recovery path: the file is
                // still published, at a different address, so the client's manifest is stale and re-reading
                // it fixes this. A 404 would say the file itself had been removed.
                context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
                return TypedResults.StatusCode(StatusCodes.Status410Gone);

            case ClientContractOutcome.NotOffered:
            default:
                return TypedResults.NotFound(
                    $"No Active package named '{package}' offers '{fileName}' to a client.");
        }
    }
}
