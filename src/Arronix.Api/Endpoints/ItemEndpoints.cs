using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Api.Configuration;
using Arronix.Host.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;


namespace Arronix.Api.Endpoints;

/// <summary>
/// Browsing a library along whatever axes its extension declared.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this file knows a media concept. A route names a kind and a level, both of which are
/// identifiers the extension chose; the traversal is whichever browse axis the extension declared; and the
/// half of each item that is the platform's own — whether it is wanted, how much of it is present, which
/// variant is selected, what may be done to it — is composed by the host, because it is the only party
/// that holds both the catalog projection and the library state.
/// </para>
/// <para>
/// The composition is deliberately not done here. An extension projects a catalog; the host stores what
/// the user did to it; joining the two is the host's job, and doing it in the transport layer would mean a
/// command line or a scheduled task got a different answer than a browser did.
/// </para>
/// </remarks>
internal static class ItemEndpoints
{
    /// <summary>
    /// The reserved filter key naming the grouping axis a query is being browsed along.
    /// </summary>
    /// <remarks>
    /// A query carries a level, a parent and a filter bag, but no slot for a grouping axis, so grouping
    /// rides the bag under a reserved key. It is documented rather than hidden because it is a real gap in
    /// the query contract, and a reserved key is the cheapest thing that does not require the contract to
    /// change before a grouped view can exist.
    /// </remarks>
    internal const string GroupAxisFilterKey = "arronix.group-axis";

    /// <summary>
    /// The reserved filter key naming the specific group whose members are wanted.
    /// </summary>
    internal const string GroupFilterKey = "arronix.group";

    private static readonly string[] ReservedQueryNames =
        ["parent", "axis", "q", "filter", "sort", "desc", "page", "size", "group"];

    /// <summary>
    /// Maps the browsing routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapItemEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var kinds = group.MapGroup("/kinds/{kind}").WithTags("Library");

        kinds.MapGet("/levels/{level}/items", ListItems)
            .WithName("ListItems")
            .WithSummary("Lists the items at one level of a media kind.");

        kinds.MapGet("/items/{id}", GetItem)
            .WithName("GetItem")
            .WithSummary("Returns one item, identified as 'level:id'.");

        kinds.MapGet("/items/{id}/children", GetChildren)
            .WithName("GetItemChildren")
            .WithSummary("Lists the children of one item, at the single level below it.");

        kinds.MapGet("/groups/{axisId}", GetGroup)
            .WithName("ListGroupedItems")
            .WithSummary("Lists items along one of the kind's declared grouping axes.");

        return group;
    }

    private static async Task<Results<Ok<ItemDetailPage>, ProblemHttpResult>> ListItems(
        string kind,
        string level,
        IMediaKindRegistry registry,
        MediaItemBroker items,
        MediaItemProjection projection,
        IOptions<ApiOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (!MediaLevelId.TryParse(level, out var levelId) || !HasLevel(registered, levelId))
        {
            return UnknownLevel(kind, level);
        }

        MediaItemRef? parent = null;
        var parentText = context.Request.Query["parent"].ToString();
        if (!string.IsNullOrEmpty(parentText))
        {
            if (!ApiRequests.TryParseItemRef(registered.Kind, parentText, out var parsed))
            {
                return ApiRequests.Problem(
                    StatusCodes.Status400BadRequest,
                    CoreErrorCode.MediaItemNotFound,
                    $"'{parentText}' is not a well-formed item reference; the form is 'level:id'.");
            }

            parent = parsed;
        }

        var query = BuildQuery(registered, levelId, parent, context, options.Value, ApiRequests.Filters(context.Request));
        var page = await items.QueryAsync(registered.Kind, query, cancellationToken).ConfigureAwait(false);

        if (page is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' is no longer installed.");
        }

        var projected = await projection.ProjectAsync(registered, page, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(projected);
    }

    private static async Task<Results<Ok<ItemDetail>, ProblemHttpResult>> GetItem(
        string kind,
        string id,
        IMediaKindRegistry registry,
        MediaItemBroker items,
        MediaItemProjection projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(projection);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (!ApiRequests.TryParseItemRef(registered.Kind, id, out var reference) || !HasLevel(registered, reference.Level))
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.MediaItemNotFound,
                $"'{id}' is not a well-formed item reference for '{kind}'; the form is 'level:id'.");
        }

        var item = await items.GetAsync(registered.Kind, reference, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaItemNotFound,
                $"'{kind}' has no item '{id}'.");
        }

        // The catalog half decides whether the item exists, and it has already answered; the projection
        // only adds the half the host owns, so there is no second way for this to be a miss.
        var detail = await projection.ProjectAsync(registered, item, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<ItemDetailPage>, ProblemHttpResult>> GetChildren(
        string kind,
        string id,
        IMediaKindRegistry registry,
        MediaItemBroker items,
        MediaItemProjection projection,
        IOptions<ApiOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (!ApiRequests.TryParseItemRef(registered.Kind, id, out var reference))
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.MediaItemNotFound,
                $"'{id}' is not a well-formed item reference; the form is 'level:id'.");
        }

        // A level has at most one child in this milestone's shape rules, so "the children of this item" is
        // unambiguous and needs no parameter. The axis is still accepted, because it decides the ordering.
        var child = registered.Descriptor.Shape.Levels
            .FirstOrDefault(candidate => candidate.Parent == reference.Level);

        if (child is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"Level '{reference.Level}' of '{kind}' has no child level.");
        }

        var query = BuildQuery(registered, child.Id, reference, context, options.Value, ApiRequests.Filters(context.Request));
        var page = await items.QueryAsync(registered.Kind, query, cancellationToken).ConfigureAwait(false);

        if (page is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' is no longer installed.");
        }

        var projected = await projection.ProjectAsync(registered, page, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(projected);
    }

    private static async Task<Results<Ok<ItemDetailPage>, ProblemHttpResult>> GetGroup(
        string kind,
        string axisId,
        IMediaKindRegistry registry,
        MediaItemBroker items,
        MediaItemProjection projection,
        IOptions<ApiOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        var axis = registered.Descriptor.Shape.GroupingAxes
            .FirstOrDefault(candidate => string.Equals(candidate.AxisId, axisId, StringComparison.Ordinal));

        if (axis is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' declares no grouping axis '{axisId}'.");
        }

        var filters = new Dictionary<string, IReadOnlyList<string>>(ApiRequests.Filters(context.Request), StringComparer.Ordinal)
        {
            [GroupAxisFilterKey] = [axis.AxisId],
        };

        var group = context.Request.Query["group"].ToString();
        if (!string.IsNullOrEmpty(group))
        {
            filters[GroupFilterKey] = [group];
        }

        var query = BuildQuery(registered, axis.MemberLevelId, null, context, options.Value, filters);
        var page = await items.QueryAsync(registered.Kind, query, cancellationToken).ConfigureAwait(false);

        if (page is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' is no longer installed.");
        }

        var projected = await projection.ProjectAsync(registered, page, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(projected);
    }

    private static ItemQuery BuildQuery(
        RegisteredMediaKind registered,
        MediaLevelId level,
        MediaItemRef? parent,
        HttpContext context,
        ApiOptions options,
        IReadOnlyDictionary<string, IReadOnlyList<string>> filters)
    {
        var (page, size) = ApiRequests.Paging(
            TryInt(context.Request.Query["page"].ToString()),
            TryInt(context.Request.Query["size"].ToString()),
            options);

        var sort = context.Request.Query["sort"].ToString();
        if (string.IsNullOrEmpty(sort))
        {
            // An axis that partitions by a field also implies the ordering the user expects to see it in,
            // so an explicitly requested sort wins and a declared axis supplies one when none was asked for.
            var axisId = context.Request.Query["axis"].ToString();
            var axis = registered.Descriptor.Intent.BrowseAxes.FirstOrDefault(candidate =>
                string.Equals(candidate.AxisId, axisId, StringComparison.Ordinal));

            sort = axis?.FieldId ?? string.Empty;
        }

        var text = context.Request.Query["q"].ToString();

        return new ItemQuery
        {
            Kind = registered.Kind,
            Level = level,
            Parent = parent,
            TextSearch = string.IsNullOrWhiteSpace(text) ? null : text,
            Filters = filters,
            SortFieldId = string.IsNullOrEmpty(sort) ? null : sort,
            SortDescending = IsTrue(context.Request.Query["desc"].ToString()),
            Page = page,
            PageSize = size,
        };
    }

    private static bool HasLevel(RegisteredMediaKind registered, MediaLevelId level)
        => registered.Descriptor.Shape.Levels.Any(candidate => candidate.Id == level);

    private static ProblemHttpResult UnknownLevel(string kind, string level)
        => ApiRequests.Problem(
            StatusCodes.Status404NotFound,
            CoreErrorCode.MediaKindNotFound,
            $"'{kind}' declares no level '{level}'.");

    private static int? TryInt(string text)
        => int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool IsTrue(string text)
        => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "1", StringComparison.Ordinal);

    /// <summary>
    /// The reserved query names a browsing route consumes itself, so anything else can be treated as an
    /// input a declaration asked for.
    /// </summary>
    internal static IReadOnlyList<string> Reserved => ReservedQueryNames;
}
