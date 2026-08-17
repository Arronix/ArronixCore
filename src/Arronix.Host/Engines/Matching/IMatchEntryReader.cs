using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

// The media-shape contracts are experimental; this seam is the match engine's read window over them.
#pragma warning disable ARX0013

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The read window the match engine sees the library through.
/// </summary>
/// <remarks>
/// <para>
/// The engine is written once and is media-agnostic; everything it knows about entries and units arrives
/// through this seam as <see cref="ItemView"/> rows. The storage milestone supplies the durable
/// implementation; until then any in-memory projection over an item source satisfies it.
/// </para>
/// <para>
/// Deliberately narrower than the item-source seam: the matcher needs candidates, units and identifier
/// resolution, and giving it paging or workbench access would let match behavior grow query-shaped
/// dependencies the declaration cannot describe.
/// </para>
/// </remarks>
internal interface IMatchEntryReader
{
    /// <summary>
    /// Resolves an external catalog identifier to an item.
    /// </summary>
    /// <param name="kind">The media kind being matched within.</param>
    /// <param name="externalId">The identifier to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when the identifier is unknown.</returns>
    Task<MediaItemRef?> ResolveExternalAsync(
        MediaKindId kind,
        ExternalId externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every library entry of one kind, as candidates for entry resolution.
    /// </summary>
    /// <param name="kind">The media kind being matched within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<ItemView>> GetEntriesAsync(
        MediaKindId kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one item.
    /// </summary>
    /// <param name="reference">The item wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when there is no such item.</returns>
    Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the units a match within one entry can resolve to, in running order.
    /// </summary>
    /// <param name="entry">The resolved entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The units.</returns>
    Task<IReadOnlyList<ItemView>> GetUnitsAsync(
        MediaItemRef entry,
        CancellationToken cancellationToken = default);
}
