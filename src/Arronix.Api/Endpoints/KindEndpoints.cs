using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Wire;
using Arronix.Api.Configuration;
using Arronix.Api.Serialization;
using Arronix.Host.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;


namespace Arronix.Api.Endpoints;

/// <summary>
/// Publishes what each loaded extension declared, as data.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the seam the whole client/server split rests on, so it is worth being explicit about
/// what crosses it: declarations, and never code.</strong> A descriptor is a media kind's entire
/// user-facing schema — its levels and their fields, what may be done to each of them and how consequential
/// each of those things is, how the kind is browsed, sorted and filtered, what states its items can be in,
/// and which editable grids it offers. All of it is inert data with no behavior attached. Four properties
/// follow from that, and none of them holds in any comparable application today:
/// </para>
/// <list type="number">
///   <item><description>No extension code ever runs on the client. There is nothing to sandbox in the
///   client, because there is nothing there to run — which is the only honest way to make that claim, given
///   the client runs outside this host's enforcement entirely.</description></item>
///   <item><description>A gated contract cannot reach the client. The client compiles against the contract
///   assembly and nothing else, and every implementation of a gated contract lives in an assembly the client
///   cannot reference. The project topology is the enforcement, not a runtime check.</description></item>
///   <item><description>The host is the sole publisher of intent. These declarations are serialized here,
///   by the host, so an action or a workbench an extension has no privilege to offer is refused when the
///   extension is loaded — not filtered out later by whichever front end happens to be rendering.</description></item>
///   <item><description>The worst a malicious declaration can do is mislead: it can put wrong words on a
///   control. It cannot execute, cannot reach a contract it was not granted, and cannot inject markup,
///   because nothing in the vocabulary carries any.</description></item>
/// </list>
/// <para>
/// The rule that keeps all four true is simple enough to hold in review: nothing added to this payload may
/// name a user-interface technology, a control, a color, a size or a layout. If a declaration cannot be
/// honored sensibly by a command line and by a voice assistant as well as by a screen, it is
/// presentational and does not belong in it.
/// </para>
/// </remarks>
internal static class KindEndpoints
{
    /// <summary>
    /// Entity tags, held only as long as the descriptor they describe. A reload mints a new descriptor, and
    /// therefore a new tag, without anything having to remember to invalidate anything.
    /// </summary>
    private static readonly ConditionalWeakTable<MediaKindDescriptor, string> EntityTags = new();

    /// <summary>
    /// Maps the descriptor routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapKindEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var kinds = group.MapGroup("/kinds").WithTags("Media kinds");

        kinds.MapGet("/", ListKinds)
            .WithName("ListMediaKinds")
            .WithSummary("Lists every media kind a loaded extension declares.")
            .WithDescription("The complete schema for each kind, as declared data. Nothing here is executable.");

        kinds.MapGet("/{kind}", GetKind)
            .WithName("GetMediaKind")
            .WithSummary("Returns one media kind's complete declaration.")
            .WithDescription("Carries an entity tag; a client that already holds the current one gets 304.");

        return group;
    }

    private static Ok<IReadOnlyList<MediaKindDescriptor>> ListKinds(IMediaKindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        // Every descriptor in full rather than a trimmed summary. The descriptor's shape is not optional, so
        // there is no smaller form of it that is still the same type, and a client that has to fetch each
        // kind again to be able to render anything has paid the same bytes plus a round trip per kind. The
        // saving that matters is made by the entity tag on the single-kind route instead: the payload is
        // large but it is also almost perfectly static, changing only when an extension is loaded, upgraded
        // or quarantined.
        IReadOnlyList<MediaKindDescriptor> descriptors = [.. registry.All.Select(static kind => kind.Descriptor)];
        return TypedResults.Ok(descriptors);
    }

    private static Results<Ok<MediaKindDescriptor>, StatusCodeHttpResult, ProblemHttpResult> GetKind(
        string kind,
        IMediaKindRegistry registry,
        IOptions<ApiOptions> options,
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        var descriptor = registered.Descriptor;
        var tag = EntityTags.GetValue(descriptor, ComputeEntityTag);

        context.Response.Headers[HeaderNames.ETag] = tag;
        context.Response.Headers[HeaderNames.CacheControl] = string.Create(
            CultureInfo.InvariantCulture,
            $"private, max-age={(int)options.Value.DescriptorCacheDuration.TotalSeconds}, must-revalidate");

        var known = context.Request.Headers[HeaderNames.IfNoneMatch];
        if (known.Count > 0 && known.Contains(tag, StringComparer.Ordinal))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(descriptor);
    }

    private static string ComputeEntityTag(MediaKindDescriptor descriptor)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, ApiJsonOptions.Default);
        var digest = SHA256.HashData(payload);
        return string.Create(CultureInfo.InvariantCulture, $"\"{Convert.ToHexStringLower(digest.AsSpan(0, 16))}\"");
    }
}
