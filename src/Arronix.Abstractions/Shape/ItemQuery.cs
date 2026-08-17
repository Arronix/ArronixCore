using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// A request for one page of items from an extension's catalog.
/// </summary>
/// <remarks>
/// Paging, sorting and filtering are expressed here rather than applied by the host afterwards because
/// the extension owns the catalog and is the only party that can page it efficiently. The host merges
/// its own library state onto whatever comes back.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ItemQuery
{
    /// <summary>
    /// Gets the media kind being queried.
    /// </summary>
    public required MediaKindId Kind { get; init; }

    /// <summary>
    /// Gets the level being queried.
    /// </summary>
    public required MediaLevelId Level { get; init; }

    /// <summary>
    /// Gets the item whose children are wanted, or <see langword="null"/> for the level as a whole.
    /// </summary>
    public MediaItemRef? Parent { get; init; }

    /// <summary>
    /// Gets the free-text filter, applied to the fields the shape marks searchable.
    /// </summary>
    public string? TextSearch { get; init; }

    /// <summary>
    /// Gets the field filters, keyed by <see cref="FieldDescriptor.FieldId"/>, with allowed values as
    /// canonical strings. Interpreted by the source, which owns the field's meaning.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; }
        = ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;

    /// <summary>
    /// Gets the <see cref="FieldDescriptor.FieldId"/> to sort by, or <see langword="null"/> for the
    /// level's natural order.
    /// </summary>
    public string? SortFieldId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the sort runs from greatest to least.
    /// </summary>
    public bool SortDescending { get; init; }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Gets the number of items per page.
    /// </summary>
    public int PageSize { get; init; } = 50;
}
