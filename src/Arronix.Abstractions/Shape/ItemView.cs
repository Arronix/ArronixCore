using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// One catalog item, as the extension that owns it describes it.
/// </summary>
/// <remarks>
/// <para>
/// Note what is deliberately absent: monitoring state, progress, the selected variant, file counts and
/// affordances. All five are host state or host derivations. An extension cannot report a monitoring
/// state it does not own, and letting it try would create two answers to one question.
/// </para>
/// <para>
/// The host merges its own half onto this value before publishing it, which is the catalog-and-library
/// split of the shape model expressed as a runtime topology.
/// </para>
/// </remarks>
public sealed record ItemView
{
    /// <summary>
    /// Gets the item's identity.
    /// </summary>
    public required MediaItemRef Ref { get; init; }

    /// <summary>
    /// Gets the containing item, or <see langword="null"/> at the root level.
    /// </summary>
    public MediaItemRef? Parent { get; init; }

    /// <summary>
    /// Gets the item's title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the language of <see cref="Title"/> when the typed entity stated it. Absence is unknown,
    /// never an implicit platform language.
    /// </summary>
    public Language? TitleLanguage { get; init; }

    /// <summary>
    /// Gets the normalized form the item sorts by, when it differs from the title.
    /// </summary>
    public string? SortTitle { get; init; }

    /// <summary>
    /// Gets the item's field values, keyed by <see cref="FieldDescriptor.FieldId"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, FieldValue> Fields { get; init; }

    /// <summary>
    /// Gets the item's positions in the coordinate spaces its level admits.
    /// </summary>
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;

    /// <summary>
    /// Gets the external identifiers the item is known by.
    /// </summary>
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];

    /// <summary>
    /// Gets the ordering key for levels whose canonical coordinate space carries labels rather than
    /// ordinals, and which therefore cannot be ordered from their coordinates.
    /// </summary>
    public long SortIndex { get; init; }

    /// <summary>
    /// Gets a value indicating whether the item has children at the level below.
    /// </summary>
    public bool HasChildren { get; init; }

    /// <summary>
    /// Gets the <see cref="FormatFamily.FamilyId"/> the item's files belong to, when the level's format
    /// is determined by the item rather than by the file.
    /// </summary>
    public string? FormatFamilyId { get; init; }
}

/// <summary>
/// One page of catalog items.
/// </summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of items per page.</param>
/// <param name="TotalCount">The number of items the query matched in total.</param>
public sealed record ItemPage(IReadOnlyList<ItemView> Items, int Page, int PageSize, int TotalCount);
