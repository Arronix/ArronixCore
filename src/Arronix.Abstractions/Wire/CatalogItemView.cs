using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// One item as a catalog describes it, with the one platform fact a caller looking at a catalog needs.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="ItemDetail"/>: nothing has been added, so monitoring, variant, file and
/// affordance state would all be defaults presented as answers. The item routes publish the full detail
/// once an item is in the library.
/// </remarks>
public sealed record CatalogItemView
{
    /// <summary>Gets the item, projected from the exact value its catalog returned.</summary>
    public required ItemView Item { get; init; }

    /// <summary>Gets the identifier the owning catalog answered with.</summary>
    public required ExternalId CatalogId { get; init; }

    /// <summary>Gets whether an explicit add has already materialized a durable record for the item.</summary>
    public required bool InLibrary { get; init; }
}

/// <summary>One page of catalog results.</summary>
/// <param name="Items">The results on this page.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of results per page.</param>
/// <param name="TotalCount">
/// How many results the catalog answered with. A catalog search carries no paging, so this is the size of
/// that answer rather than a claim about how many records the catalog holds.
/// </param>
public sealed record CatalogItemPage(
    IReadOnlyList<CatalogItemView> Items,
    int Page,
    int PageSize,
    int TotalCount);
