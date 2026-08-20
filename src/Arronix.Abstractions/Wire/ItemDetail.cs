using System.Collections.ObjectModel;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// One item as the platform publishes it: what the extension knows about it, plus the host state and
/// derivations the extension cannot own.
/// </summary>
/// <remarks>
/// The split is exactly the catalog-and-library split the shape model declares. The extension supplies
/// the catalog half and the host supplies monitoring, links, the chosen variant and everything computed
/// from them, so there is never more than one answer to "is this wanted?".
/// </remarks>
public sealed record ItemDetail
{
    /// <summary>
    /// Gets what the extension knows about the item.
    /// </summary>
    public required ItemView Item { get; init; }

    /// <summary>
    /// Gets whether the user wants the item, rolled up across its monitor dimensions and its children.
    /// </summary>
    public required MonitorState Monitor { get; init; }

    /// <summary>
    /// Gets the item's answer to each monitor dimension, keyed by
    /// <see cref="MonitorDimension.DimensionId"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> MonitorChoices { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets how much of what is wanted beneath this item has been obtained, when the level has anything
    /// beneath it.
    /// </summary>
    public ProgressSummary? Progress { get; init; }

    /// <summary>
    /// Gets the chosen manifestation, for an item whose child level is a variant axis.
    /// </summary>
    public MediaItemRef? SelectedVariant { get; init; }

    /// <summary>
    /// Gets the <see cref="Intent.StateDescriptor.StateId"/> values the item is currently in.
    /// </summary>
    public IReadOnlyList<string> StateIds { get; init; } = [];

    /// <summary>
    /// Gets what can be done with this item, after the deployment's configuration is taken into account.
    /// </summary>
    public IReadOnlyList<Affordance> Affordances { get; init; } = [];

    /// <summary>
    /// Gets the number of files satisfying this item or anything beneath it.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Gets the total size of those files, in bytes.
    /// </summary>
    public long SizeOnDisk { get; init; }
}

/// <summary>
/// Whether the user wants an item.
/// </summary>
/// <remarks>
/// Four states rather than two, because a container is routinely wanted in part, and because a level with
/// no monitor dimensions must be able to say the question does not apply rather than answer "no".
/// </remarks>
public enum MonitorState
{
    /// <summary>The user does not want it.</summary>
    Unmonitored = 0,

    /// <summary>The user wants it.</summary>
    Monitored = 1,

    /// <summary>The user wants some of what is beneath it.</summary>
    Partial = 2,

    /// <summary>The question does not apply at this level.</summary>
    NotApplicable = 3
}

/// <summary>
/// One page of published items.
/// </summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of items per page.</param>
/// <param name="TotalCount">The number of items the query matched in total.</param>
public sealed record ItemDetailPage(IReadOnlyList<ItemDetail> Items, int Page, int PageSize, int TotalCount);
