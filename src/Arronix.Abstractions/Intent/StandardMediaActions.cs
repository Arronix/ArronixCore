
namespace Arronix.Abstractions.Intent;

/// <summary>The standard media operations understood by the platform.</summary>
/// <remarks>
/// Media extensions do not redeclare this catalog. The host derives its descriptors from the media
/// type's item, grouping, identity and availability declarations. The enum is the semantic contract;
/// <see cref="StandardActionIds"/> is only its stable wire spelling.
/// </remarks>
public enum StandardMediaAction
{
    /// <summary>Search for releases of selected items.</summary>
    Search = 0,

    /// <summary>Search for every wanted item that has no satisfactory file.</summary>
    SearchMissing = 1,

    /// <summary>Search for upgrades below the configured cutoff.</summary>
    SearchCutoffUnmet = 2,

    /// <summary>Refresh catalog information for selected items.</summary>
    Refresh = 3,

    /// <summary>Read selected library folders again.</summary>
    Rescan = 4,

    /// <summary>Change whether selected items are wanted.</summary>
    SetMonitoring = 5,

    /// <summary>Change whether the members of a group are wanted.</summary>
    SetGroupMonitoring = 6,

    /// <summary>Change the minimum availability accepted for selected items.</summary>
    SetAvailability = 7,

    /// <summary>Rename selected files using the media type's naming policy.</summary>
    Rename = 8,

    /// <summary>Add an externally identified item to the library.</summary>
    Add = 9,

    /// <summary>Remove selected items from the library.</summary>
    Remove = 10,

    /// <summary>Prevent selected items from being added by curation.</summary>
    Exclude = 11,

    /// <summary>Remove a previous curation exclusion.</summary>
    ClearExclusion = 12,

    /// <summary>Refresh the groups on one grouping axis.</summary>
    RefreshGroups = 13,

    /// <summary>Select one manifestation of an item.</summary>
    SelectVariant = 14,

    /// <summary>Replace an item's tags.</summary>
    SetTags = 15,

    /// <summary>Move an item's files to another library location.</summary>
    Relocate = 16,

    /// <summary>Acquire one selected release.</summary>
    Grab = 17
}

/// <summary>Stable wire spellings for <see cref="StandardMediaAction"/>.</summary>
public static class StandardActionIds
{
    public const string Search = "search";
    public const string SearchMissing = "search.missing";
    public const string SearchCutoffUnmet = "search.cutoffUnmet";
    public const string Refresh = "refresh";
    public const string Rescan = "rescan";
    public const string SetMonitoring = "monitor.set";
    public const string SetAvailability = "availability.set";
    public const string Rename = "rename";
    public const string Add = "add";
    public const string Remove = "remove";
    public const string Exclude = "exclude";
    public const string ClearExclusion = "exclude.clear";
    public const string SelectVariant = "variant.select";
    public const string SetTags = "tags.set";
    public const string Relocate = "relocate";
    public const string Grab = "grab";

    /// <summary>Gets the wire identifier for a standard operation.</summary>
    public static string For(StandardMediaAction action) => action switch
    {
        StandardMediaAction.Search => Search,
        StandardMediaAction.SearchMissing => SearchMissing,
        StandardMediaAction.SearchCutoffUnmet => SearchCutoffUnmet,
        StandardMediaAction.Refresh => Refresh,
        StandardMediaAction.Rescan => Rescan,
        StandardMediaAction.SetMonitoring => SetMonitoring,
        StandardMediaAction.SetAvailability => SetAvailability,
        StandardMediaAction.Rename => Rename,
        StandardMediaAction.Add => Add,
        StandardMediaAction.Remove => Remove,
        StandardMediaAction.Exclude => Exclude,
        StandardMediaAction.ClearExclusion => ClearExclusion,
        StandardMediaAction.SelectVariant => SelectVariant,
        StandardMediaAction.SetTags => SetTags,
        StandardMediaAction.Relocate => Relocate,
        StandardMediaAction.Grab => Grab,
        StandardMediaAction.SetGroupMonitoring or StandardMediaAction.RefreshGroups =>
            throw new ArgumentException("A grouping axis is required for a group operation.", nameof(action)),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    /// <summary>Gets the wire identifier for changing monitoring on one grouping axis.</summary>
    public static string GroupMonitoring(string axisId) => $"{RequireAxis(axisId)}.monitor";

    /// <summary>Gets the wire identifier for refreshing one grouping axis.</summary>
    public static string GroupRefresh(string axisId) => $"{RequireAxis(axisId)}.refresh";

    private static string RequireAxis(string axisId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisId);
        return axisId;
    }
}

/// <summary>The semantic roles of parameters used by standard operations.</summary>
public enum StandardMediaActionParameter
{
    /// <summary>Whether bulk search includes items that are not yet available.</summary>
    IncludeUnavailable = 0,

    /// <summary>The wanted state being applied.</summary>
    Wanted = 1,

    /// <summary>Whether missing members should be added while changing a group.</summary>
    AddMissing = 2,

    /// <summary>The external identifier of an item being added.</summary>
    Identifier = 3,

    /// <summary>How far monitoring extends when adding an item.</summary>
    Monitoring = 4,

    /// <summary>The minimum availability selection.</summary>
    Availability = 5,

    /// <summary>Whether acquisition starts immediately after adding an item.</summary>
    SearchImmediately = 6,

    /// <summary>Whether files are deleted while removing an item.</summary>
    DeleteFiles = 7,

    /// <summary>Whether removal also creates a curation exclusion.</summary>
    Exclude = 8
}
