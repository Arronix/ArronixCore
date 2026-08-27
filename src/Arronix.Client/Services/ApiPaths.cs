using System.Globalization;
using System.Text;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Services;

/// <summary>
/// Builds the addresses of the platform's endpoints.
/// </summary>
/// <remarks>
/// One place that knows the route shapes and one place that escapes their segments. Media kind, level and
/// item identifiers all come from extensions, so treating any of them as safe to concatenate would make
/// an extension's choice of identifier a defect in the client.
/// </remarks>
public static class ApiPaths
{
    private const string Root = "api/v1";

    /// <summary>The address of the media-kind catalog.</summary>
    public static string Kinds => $"{Root}/kinds";

    /// <summary>The address of the installed-extension list.</summary>
    public static string Plugins => $"{Root}/plugins";

    /// <summary>The address of the registered-job list.</summary>
    public static string Jobs => $"{Root}/jobs";

    /// <summary>The address of the work queue.</summary>
    public static string Queue => $"{Root}/queue";

    /// <summary>The address of the health snapshot.</summary>
    public static string Health => $"{Root}/health";

    /// <summary>The address of the configured provider list.</summary>
    public static string ProviderDefinitions => $"{Root}/providers/definitions";

    /// <summary>Builds the address of one media kind.</summary>
    /// <param name="kind">The media kind.</param>
    /// <returns>The address.</returns>
    public static string Kind(string kind) => $"{Root}/kinds/{Escape(kind)}";

    /// <summary>Builds the address of one level's items.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="level">The level.</param>
    /// <param name="query">The query values to append.</param>
    /// <returns>The address.</returns>
    public static string LevelItems(string kind, string level, IEnumerable<KeyValuePair<string, string?>> query)
        => WithQuery($"{Kind(kind)}/levels/{Escape(level)}/items", query);

    /// <summary>Builds the address of one item.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="reference">The item's complete reference.</param>
    /// <returns>The address.</returns>
    public static string Item(string kind, MediaItemRef reference)
        => $"{Kind(kind)}/items/{Escape(ItemReference(kind, reference))}";

    /// <summary>Builds the address of one item's children.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="reference">The containing item's complete reference.</param>
    /// <param name="query">The query values to append.</param>
    /// <returns>The address.</returns>
    public static string ItemChildren(
        string kind,
        MediaItemRef reference,
        IEnumerable<KeyValuePair<string, string?>> query)
        => WithQuery($"{Item(kind, reference)}/children", query);

    /// <summary>Builds the address of one catalog search.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="scheme">The catalog scheme to search.</param>
    /// <param name="text">The text to search for.</param>
    /// <param name="id">The external identifier to resolve.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The number of results wanted.</param>
    /// <returns>The address.</returns>
    public static string CatalogSearch(
        string kind,
        string scheme,
        string? text,
        ExternalId? id,
        int page,
        int pageSize)
        => WithQuery(
            $"{Kind(kind)}/catalog/search",
            [
                new("scheme", scheme),
                new("q", text),
                new("id", id?.ToString()),
                new("page", page.ToString(CultureInfo.InvariantCulture)),
                new("size", pageSize.ToString(CultureInfo.InvariantCulture)),
            ]);

    /// <summary>Builds the address where a catalog item is explicitly added.</summary>
    /// <param name="kind">The media kind.</param>
    /// <returns>The address.</returns>
    public static string CatalogItems(string kind) => $"{Kind(kind)}/catalog/items";

    /// <summary>Builds the address where one added item's catalog facts are refreshed.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="reference">The complete reference of the added item.</param>
    /// <returns>The address.</returns>
    public static string CatalogItemRefresh(string kind, MediaItemRef reference)
        => $"{CatalogItems(kind)}/{Escape(ItemReference(kind, reference))}/refresh";

    /// <summary>Renders the complete route-local form of an item reference.</summary>
    /// <param name="reference">The item reference to render.</param>
    /// <returns>The <c>level:id</c> form.</returns>
    /// <exception cref="ArgumentException"><paramref name="reference"/> cannot name an issued item.</exception>
    public static string ItemReference(MediaItemRef reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Kind.Value)
            || string.IsNullOrWhiteSpace(reference.Level.Value)
            || reference.Id.Value < 1)
        {
            throw new ArgumentException("An item route requires a kind, level and positive identifier.", nameof(reference));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{reference.Level}:{reference.Id}");
    }

    /// <summary>Reads a route-local <c>level:id</c> item reference.</summary>
    /// <param name="kind">The media kind established by the containing route.</param>
    /// <param name="text">The route segment to read.</param>
    /// <param name="reference">The parsed reference when the segment was well formed.</param>
    /// <returns>Whether the segment named one possible issued item.</returns>
    public static bool TryParseItemReference(MediaKindId kind, string? text, out MediaItemRef reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(kind.Value) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(':');
        var (levelText, idText) = parts.Length switch
        {
            2 => (parts[0], parts[1]),
            3 when string.Equals(parts[0], kind.Value, StringComparison.Ordinal) => (parts[1], parts[2]),
            _ => (null, null),
        };

        if (levelText is null
            || idText is null
            || !MediaLevelId.TryParse(levelText, out var level)
            || !long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || id < 1)
        {
            return false;
        }

        reference = new MediaItemRef(kind, level, MediaItemId.FromInt64(id));
        return true;
    }

    /// <summary>Builds the address of a cross-cutting collection's members.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="axisId">The grouping axis.</param>
    /// <param name="query">The query values to append.</param>
    /// <returns>The address.</returns>
    public static string Groups(string kind, string axisId, IEnumerable<KeyValuePair<string, string?>> query)
        => WithQuery($"{Kind(kind)}/groups/{Escape(axisId)}", query);

    /// <summary>Builds the address an action is invoked at.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="actionId">The action.</param>
    /// <returns>The address.</returns>
    public static string Action(string kind, string actionId)
        => $"{Kind(kind)}/actions/{Escape(actionId)}";

    /// <summary>Builds the address a working surface's proposal is requested at.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">The working surface.</param>
    /// <param name="query">The inputs the surface declared.</param>
    /// <returns>The address.</returns>
    public static string WorkbenchProposal(
        string kind,
        string workbenchId,
        IEnumerable<KeyValuePair<string, string?>> query)
        => WithQuery($"{Kind(kind)}/workbenches/{Escape(workbenchId)}/proposal", query);

    /// <summary>Builds the address a working surface's permitted values are read from.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">The working surface.</param>
    /// <param name="sourceId">The set of values.</param>
    /// <param name="rowId">The row the values are for.</param>
    /// <returns>The address.</returns>
    public static string WorkbenchOptions(string kind, string workbenchId, string sourceId, string? rowId)
        => WithQuery(
            $"{Kind(kind)}/workbenches/{Escape(workbenchId)}/options/{Escape(sourceId)}",
            [new KeyValuePair<string, string?>("row", rowId)]);

    /// <summary>Builds the address a working surface is committed at.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">The working surface.</param>
    /// <returns>The address.</returns>
    public static string WorkbenchCommit(string kind, string workbenchId)
        => $"{Kind(kind)}/workbenches/{Escape(workbenchId)}/commit";

    /// <summary>Builds the address of the available provider catalog.</summary>
    /// <param name="family">The provider family to narrow to.</param>
    /// <param name="kind">The media kind to narrow to.</param>
    /// <returns>The address.</returns>
    public static string Providers(string? family, string? kind)
        => WithQuery(
            $"{Root}/providers",
            [new KeyValuePair<string, string?>("family", family), new KeyValuePair<string, string?>("kind", kind)]);

    /// <summary>Builds the address of one configured provider.</summary>
    /// <param name="definitionId">The configuration's identifier.</param>
    /// <returns>The address.</returns>
    public static string ProviderDefinition(int definitionId)
        => $"{ProviderDefinitions}/{definitionId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Builds the address one configured provider is tested at.</summary>
    /// <param name="definitionId">The configuration's identifier.</param>
    /// <returns>The address.</returns>
    public static string ProviderTest(int definitionId) => $"{ProviderDefinition(definitionId)}/test";

    /// <summary>Builds the address one configured provider's permitted values are read from.</summary>
    /// <param name="definitionId">The configuration's identifier.</param>
    /// <param name="sourceId">The set of values.</param>
    /// <returns>The address.</returns>
    public static string ProviderOptions(int definitionId, string sourceId)
        => $"{ProviderDefinition(definitionId)}/options/{Escape(sourceId)}";

    /// <summary>Builds the address one job is triggered at.</summary>
    /// <param name="jobId">The job.</param>
    /// <returns>The address.</returns>
    public static string JobTrigger(string jobId) => $"{Jobs}/{Escape(jobId)}/trigger";

    private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private static string ItemReference(string kind, MediaItemRef reference)
    {
        if (!string.Equals(kind, reference.Kind.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException("An item route cannot name a reference from another media kind.", nameof(reference));
        }

        return ItemReference(reference);
    }

    private static string WithQuery(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var builder = new StringBuilder(path);
        var first = true;

        foreach (var pair in query)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                continue;
            }

            builder.Append(first ? '?' : '&');
            builder.Append(Escape(pair.Key));
            builder.Append('=');
            builder.Append(Escape(pair.Value));
            first = false;
        }

        return builder.ToString();
    }
}
