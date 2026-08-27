using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Storage;

/// <summary>
/// The catalog half of one item as the store holds it: the reference, the authority that answered, and the
/// item itself in the form its own contract wrote.
/// </summary>
/// <remarks>
/// The payload is opaque here on purpose. The store's job is to hold what a media type said about an item
/// and give it back unchanged; reading it is the contract's job, and doing it anywhere else would put a
/// second opinion about the item's shape in the platform.
/// </remarks>
internal sealed record CatalogRecord
{
    /// <summary>Gets the reference the host holds the item under.</summary>
    public required MediaItemRef Reference { get; init; }

    /// <summary>Gets the identifier the owning catalog answered with.</summary>
    public required ExternalId CatalogId { get; init; }

    /// <summary>Gets the payload's own title, held beside it so a page can be ordered in the store.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the payload's own catalog state, held beside it so a withdrawn record stays findable.</summary>
    public required CatalogRecordState State { get; init; }

    /// <summary>Gets the writing contract's hash over the member graph its reader accepts.</summary>
    public required string ContractMetadataHash { get; init; }

    /// <summary>Gets the item, serialized through its own contract's generated metadata.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>Gets when the catalog-owned half was last written.</summary>
    public DateTimeOffset RefreshedAt { get; init; }

    /// <summary>Gets the revision a concurrent catalog write is detected against.</summary>
    public long Revision { get; init; }
}

/// <summary>What a browse asks the store for.</summary>
/// <param name="TextSearch">A filter over the record's own title, or <see langword="null"/> for all of them.</param>
/// <param name="ByTitle">Whether to order by title rather than by the level's natural identity order.</param>
/// <param name="Descending">Whether that order runs from greatest to least.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of records per page.</param>
internal readonly record struct CatalogRecordQuery(
    string? TextSearch,
    bool ByTitle,
    bool Descending,
    int Page,
    int PageSize);

/// <summary>One page of records and how many the query matched in total.</summary>
/// <param name="Records">The records on this page.</param>
/// <param name="TotalCount">How many the query matched.</param>
internal readonly record struct CatalogRecordPage(IReadOnlyList<CatalogRecord> Records, int TotalCount);

/// <summary>What materializing a record did.</summary>
/// <param name="Record">The record now held.</param>
/// <param name="Created">
/// Whether this call wrote it. A repeated add of the same catalog identifier answers with the record that
/// was already there and <see langword="false"/>, which is what makes an add safe to retry.
/// </param>
internal readonly record struct CatalogRecordMaterialization(CatalogRecord Record, bool Created);
