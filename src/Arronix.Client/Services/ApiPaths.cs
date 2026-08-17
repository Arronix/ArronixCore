using System.Globalization;
using System.Text;

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
    /// <param name="itemId">The item's identifier.</param>
    /// <returns>The address.</returns>
    public static string Item(string kind, long itemId)
        => $"{Kind(kind)}/items/{itemId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Builds the address of one item's children.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="itemId">The item's identifier.</param>
    /// <param name="query">The query values to append.</param>
    /// <returns>The address.</returns>
    public static string ItemChildren(string kind, long itemId, IEnumerable<KeyValuePair<string, string?>> query)
        => WithQuery($"{Item(kind, itemId)}/children", query);

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
