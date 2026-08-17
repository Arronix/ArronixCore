using Arronix.Api.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arronix.Api.Endpoints;

/// <summary>
/// The whole routing table, assembled from one module per feature.
/// </summary>
/// <remarks>
/// <para>
/// This file is a table of contents and nothing else: it names every module and owns none of their
/// behavior. That mirrors how the host composes its own subsystems, and it exists for the same reason —
/// a surface that can be read in one screen is a surface somebody can audit, and a route that is not
/// listed here does not exist.
/// </para>
/// <para>
/// <strong>Versioning.</strong> The version is a path segment, decided once, here. It is a segment rather
/// than a header or a media-type parameter because this API is consumed by a browser application and by
/// people with a command-line HTTP client, and both of those can copy, bookmark and curl a URL that says
/// what it is. A second version is a second group beside this one; nothing else in the file changes, and
/// the two can be served side by side for as long as they need to be. Every response also states which
/// versions this build serves, so a client that has been away can tell without being told.
/// </para>
/// </remarks>
internal static class ApiEndpoints
{
    /// <summary>The current version segment.</summary>
    internal const string V1 = "v1";

    /// <summary>The prefix every versioned route hangs from.</summary>
    internal const string BasePath = "/api/" + V1;

    /// <summary>The header naming every version this build serves.</summary>
    private const string SupportedVersionsHeader = "Api-Supported-Versions";

    /// <summary>Every version this build serves, newest last.</summary>
    private static readonly string[] SupportedVersions = [V1];

    /// <summary>
    /// Maps every route this server answers.
    /// </summary>
    /// <param name="app">The application being configured.</param>
    /// <returns>The same application, for chaining.</returns>
    internal static WebApplication MapArronixApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Version discovery, unversioned on purpose: a client that does not yet know which versions exist
        // cannot be asked to pick one in the URL it uses to find out.
        app.MapGet("/api", static () => TypedResults.Ok(new ApiVersionIndex(V1, SupportedVersions)))
            .WithName("GetApiVersions")
            .WithSummary("Lists the API versions this build serves.")
            .WithTags("Platform");

        var v1 = app.MapGroup(BasePath)
            .WithGroupName(V1)
            .AddEndpointFilter(static async (context, next) =>
            {
                context.HttpContext.Response.Headers[SupportedVersionsHeader] = string.Join(", ", SupportedVersions);
                return await next(context).ConfigureAwait(false);
            });

        v1.MapKindEndpoints();
        v1.MapItemEndpoints();
        v1.MapActionEndpoints();
        v1.MapWorkbenchEndpoints();
        v1.MapProviderEndpoints();
        v1.MapPluginEndpoints();
        v1.MapJobEndpoints();
        v1.MapQueueEndpoints();
        v1.MapHealthEndpoints();

        // The unversioned liveness route, which is contracted by orchestrators rather than by this API's
        // own clients and therefore does not move when the API version does.
        app.MapPlatformHealthEndpoint();

        app.MapHub<EventHub>(EventHub.Path);

        return app;
    }

    /// <summary>
    /// What versions of this API the server speaks.
    /// </summary>
    /// <param name="Current">The version a new client should use.</param>
    /// <param name="Supported">Every version still answered.</param>
    internal sealed record ApiVersionIndex(string Current, IReadOnlyList<string> Supported);
}
