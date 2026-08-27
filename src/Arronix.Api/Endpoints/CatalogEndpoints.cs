using System.Globalization;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Api.Configuration;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Arronix.Api.Endpoints;

/// <summary>
/// Finding an item in a catalog, adding it, and refreshing what a catalog says about it.
/// </summary>
/// <remarks>
/// <para>
/// Three routes and one rule between them: a search is a question and an add is a decision. Searching mints
/// the identity the platform will hold a hit under and materializes nothing, so browsing a catalog cannot
/// quietly fill a library. Adding is the explicit act that writes the record.
/// </para>
/// <para>
/// Nothing here names a media concept. A route names a kind, the scheme of a catalog, and an identifier in
/// that catalog's own namespace; which catalogers own which scheme is an installation fact.
/// </para>
/// <para>
/// A result is a catalog item, not a library item. It carries the item the catalog described and whether
/// the platform already holds a record for it, and nothing else: composing monitoring, files and
/// affordances for something nobody has added would be publishing defaults as answers. Once an item is
/// added, the ordinary item routes own it and publish the full detail with the library facet beside it.
/// </para>
/// </remarks>
internal static class CatalogEndpoints
{
    /// <summary>What an add asks for.</summary>
    /// <param name="CatalogId">
    /// The identifier to add, as <c>scheme:value</c> in the owning catalog's own namespace. It may be an
    /// alias that catalog redirects.
    /// </param>
    internal sealed record AddRequest(string CatalogId);

    /// <summary>
    /// Maps the catalog routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var catalog = group.MapGroup("/kinds/{kind}/catalog").WithTags("Catalog");

        catalog.MapGet("/search", Search)
            .WithName("SearchCatalog")
            .WithSummary("Searches one catalog, giving each hit the identity the platform would hold it under.");

        catalog.MapPost("/items", Add)
            .WithName("AddCatalogItem")
            .WithSummary("Adds one catalog item, materializing its durable record.");

        catalog.MapPost("/items/{id}/refresh", Refresh)
            .WithName("RefreshCatalogItem")
            .WithSummary("Refreshes one added item's catalog-owned facts, leaving library state alone.");

        return group;
    }

    private static async Task<Results<Ok<CatalogItemPage>, ProblemHttpResult>> Search(
        string kind,
        string scheme,
        string? q,
        string? id,
        IMediaKindRegistry registry,
        CatalogLibrary catalog,
        IOptions<ApiOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.CatalogSchemeUnowned,
                "A search names the catalog to search, as 'scheme'.");
        }

        if (!CatalogIdentity.IsCanonicalScheme(scheme))
        {
            return NonCanonicalScheme(scheme);
        }

        ExternalId? resolved = null;

        if (!string.IsNullOrWhiteSpace(id))
        {
            if (!TryParseCatalogId(id, out var parsed))
            {
                return MalformedCatalogId(id);
            }

            resolved = parsed;
        }

        if (string.IsNullOrWhiteSpace(q) && resolved is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                "A search states text to search for, an identifier to resolve, or both.");
        }

        if (!TryBounds(context, options.Value, out var page, out var pageSize, out var refusal))
        {
            return refusal!;
        }

        try
        {
            var found = await catalog
                .SearchAsync(
                    registered.Kind,
                    scheme,
                    new CatalogQuery(q ?? string.Empty, resolved),
                    page,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Ok(found);
        }
        catch (ArronixException failure)
        {
            return ApiRequests.Refused(failure);
        }
    }

    /// <summary>
    /// Reads the paging a search asked for, refusing anything outside what this server will serve.
    /// </summary>
    /// <remarks>
    /// Refused rather than clamped. A caller who asked for a page size the server will not serve and
    /// silently received a different one has no way to know the answer is not the one they asked for, and
    /// will page through a list whose boundaries are not the ones they are counting on.
    /// </remarks>
    private static bool TryBounds(
        HttpContext context,
        ApiOptions options,
        out int page,
        out int pageSize,
        out ProblemHttpResult? refusal)
    {
        page = 1;
        pageSize = options.DefaultPageSize;
        refusal = null;

        var requestedPage = context.Request.Query["page"].ToString();
        var requestedSize = context.Request.Query["size"].ToString();

        if (!string.IsNullOrEmpty(requestedPage)
            && (!int.TryParse(requestedPage, NumberStyles.Integer, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            refusal = ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"'{requestedPage}' is not a page; a page is a whole number of one or more.");

            return false;
        }

        if (string.IsNullOrEmpty(requestedSize))
        {
            return true;
        }

        if (!int.TryParse(requestedSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out pageSize)
            || pageSize < 1)
        {
            refusal = ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"'{requestedSize}' is not a page size; a page size is a whole number of one or more.");

            return false;
        }

        if (pageSize > options.MaxPageSize)
        {
            refusal = ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"This server serves at most {options.MaxPageSize} results per page, and will not answer a "
                + $"request for {pageSize} with a smaller page as though it had.");

            return false;
        }

        return true;
    }

    private static async Task<Results<Created<CatalogItemView>, Ok<CatalogItemView>, ProblemHttpResult>> Add(
        string kind,
        AddRequest request,
        IMediaKindRegistry registry,
        CatalogLibrary catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (!TryParseCatalogId(request.CatalogId, out var catalogId))
        {
            return MalformedCatalogId(request.CatalogId);
        }

        try
        {
            var added = await catalog
                .AddAsync(registered.Kind, catalogId, cancellationToken)
                .ConfigureAwait(false);

            if (added is not { } addition)
            {
                return ApiRequests.Problem(
                    StatusCodes.Status404NotFound,
                    CoreErrorCode.MediaItemNotFound,
                    $"No catalog holding '{request.CatalogId}' has a record for it.");
            }

            // The status carries what happened, so the body says the same thing either way: a retried add
            // is not a different item and must not be described as one.
            return addition.Created
                ? TypedResults.Created(ItemAddress(kind, addition.View.Item.Ref), addition.View)
                : TypedResults.Ok(addition.View);
        }
        catch (ArronixException failure)
        {
            return ApiRequests.Refused(failure);
        }
    }

    /// <summary>Where the item a caller just added is published from.</summary>
    private static string ItemAddress(string kind, MediaItemRef reference)
        => $"{ApiEndpoints.BasePath}/kinds/{kind}/items/{ApiRequests.ToPathSegment(reference)}";

    private static async Task<Results<Ok<CatalogItemView>, ProblemHttpResult>> Refresh(
        string kind,
        string id,
        IMediaKindRegistry registry,
        CatalogLibrary catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (!ApiRequests.TryParseItemRef(registered.Kind, id, out var reference))
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.MediaItemNotFound,
                $"'{id}' is not a well-formed item reference for '{kind}'; the form is 'level:id'.");
        }

        try
        {
            var refreshed = await catalog
                .RefreshAsync(registered.Kind, reference, cancellationToken)
                .ConfigureAwait(false);

            return refreshed is null
                ? ApiRequests.Problem(
                    StatusCodes.Status502BadGateway,
                    CoreErrorCode.CatalogerConnectionFailed,
                    $"No catalog that is the authority for '{id}' answered, so its record is unchanged.")
                : TypedResults.Ok(refreshed);
        }
        catch (ArronixException failure)
        {
            return ApiRequests.Refused(failure);
        }
    }

    /// <summary>
    /// Reads the <c>scheme:value</c> form a catalog identifier travels in.
    /// </summary>
    /// <remarks>
    /// Split on the first separator only: a catalog's own value may contain one, and the scheme may not.
    /// The scheme is held to the canonical form the platform routes by here, at the edge, so a request that
    /// cannot be routed is refused as a bad request rather than arriving inside the dispatcher as a fault.
    /// </remarks>
    private static bool TryParseCatalogId(string? text, out ExternalId catalogId)
    {
        catalogId = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var split = text.IndexOf(':', StringComparison.Ordinal);

        if (split <= 0 || split == text.Length - 1)
        {
            return false;
        }

        var scheme = text[..split];
        var value = text[(split + 1)..];

        if (!CatalogIdentity.IsCanonicalScheme(scheme) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        catalogId = new ExternalId(scheme, value);
        return true;
    }

    private static ProblemHttpResult MalformedCatalogId(string? text)
        => ApiRequests.Problem(
            StatusCodes.Status400BadRequest,
            CoreErrorCode.CatalogIdentityInvalid,
            $"'{text}' is not a well-formed catalog identifier; the form is 'scheme:value', where the scheme "
            + "is lower-case and carries no white space, and the value is not empty.");

    private static ProblemHttpResult NonCanonicalScheme(string scheme)
        => ApiRequests.Problem(
            StatusCodes.Status400BadRequest,
            CoreErrorCode.CatalogSchemeUnowned,
            $"'{scheme}' is not a catalog scheme; a scheme is lower-case and carries no white space or ':'.");
}
