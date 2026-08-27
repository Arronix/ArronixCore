using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Storage;

/// <summary>
/// Where the platform keeps the catalog half of an item after it has been added.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IMediaStore"/>, and separate from it because the two halves have different
/// owners: a catalog write replaces everything a catalog said about an item, and must not be able to reach
/// what the user decided about it.
/// </para>
/// <para>
/// A search does not come here. Searching mints or resolves identity and nothing more; a record exists
/// because someone explicitly added the item.
/// </para>
/// </remarks>
internal interface ICatalogRecordStore
{
    /// <summary>Reads one record.</summary>
    /// <param name="reference">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when the item has not been added.</returns>
    ValueTask<CatalogRecord?> FindAsync(MediaItemRef reference, CancellationToken cancellationToken = default);

    /// <summary>Reads one record by the catalog identifier it was materialized from.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="level">The level the item sits at.</param>
    /// <param name="catalogId">The identifier the owning catalog answered with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when no record was materialized from it.</returns>
    ValueTask<CatalogRecord?> FindByCatalogIdAsync(
        MediaKindId kind,
        MediaLevelId level,
        ExternalId catalogId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one page of records.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="level">The level.</param>
    /// <param name="query">What to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page.</returns>
    /// <remarks>
    /// Ordered, filtered and paged in the store. Reading every record to find out what is on one page would
    /// make a library's size the cost of looking at any part of it.
    /// </remarks>
    ValueTask<CatalogRecordPage> PageAsync(
        MediaKindId kind,
        MediaLevelId level,
        CatalogRecordQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one item: the catalog record and the user's presence beside it, together.
    /// </summary>
    /// <param name="record">The catalog half.</param>
    /// <param name="addedAt">When the user added it, used only when there is no presence already.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record now held, and whether this call wrote it.</returns>
    /// <remarks>
    /// <para>
    /// One transaction, because the two halves are one act. A record written without the presence beside it
    /// is an item the library shows and the user never added; a presence without a record is an entry with
    /// nothing behind it. Neither is a state this platform should be able to be found in.
    /// </para>
    /// <para>
    /// Idempotent by the catalog identifier, so a retried add cannot produce a second record — and it does
    /// not restamp the date somebody added the item.
    /// </para>
    /// </remarks>
    ValueTask<CatalogRecordMaterialization> MaterializeAsync(
        CatalogRecord record,
        DateTimeOffset addedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the catalog-owned half of a record that is already held.
    /// </summary>
    /// <param name="record">The new catalog-owned half.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refreshed record.</returns>
    /// <exception cref="Abstractions.Errors.ArronixException">
    /// No record is held under the reference, or a concurrent refresh wrote it first.
    /// </exception>
    /// <remarks>Touches nothing the user owns: the library facet is a different table with a different owner.</remarks>
    ValueTask<CatalogRecord> RefreshAsync(CatalogRecord record, CancellationToken cancellationToken = default);
}
