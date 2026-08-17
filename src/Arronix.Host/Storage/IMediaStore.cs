using Arronix.Abstractions.Shape;

// The shape contracts are experimental; every argument here is keyed by one of their identity types.
#pragma warning disable ARX0013

namespace Arronix.Host.Storage;

/// <summary>
/// Where the platform keeps the half of an item that the user owns.
/// </summary>
/// <remarks>
/// <para>
/// This milestone has no persistence and no relational schema, but the <em>shape</em> of the seam is fixed
/// now, because getting it wrong later is expensive and every surveyed application shows a different wrong
/// answer. The in-memory implementation behind this interface enforces the same invariants a relational one
/// will, so the replacement is a change of implementation and nothing else moves.
/// </para>
/// <para>
/// Held host-side deliberately. No extension implements a store — extensions are stateless projections of
/// their own catalog in this milestone, which is the cheapest way to learn a store's real shape before
/// freezing it. Promoting it to the contract assembly would be irreversible under the stability policy and
/// would be done against zero implementers.
/// </para>
/// <para>
/// The store holds library state only. The catalog is projected by the owning extension and merged at
/// read time, so nothing here duplicates a title, a year or an artwork reference.
/// </para>
/// </remarks>
public interface IMediaStore
{
    /// <summary>
    /// Reads the user-owned facet of one item.
    /// </summary>
    /// <param name="reference">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facet, or <see langword="null"/> when the user has expressed nothing about the item.</returns>
    ValueTask<LibraryFacet?> FindLibraryAsync(MediaItemRef reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the user-owned facet of one item.
    /// </summary>
    /// <param name="facet">The facet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the facet is stored.</returns>
    /// <exception cref="Abstractions.Errors.ArronixException">
    /// The facet would break a declared invariant of its media kind's shape — a chosen variant that is not a
    /// variant of the item, or a monitoring answer on an axis the shape does not declare.
    /// </exception>
    ValueTask UpsertLibraryAsync(LibraryFacet facet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the user-owned facets of many items at once.
    /// </summary>
    /// <param name="references">The items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facets that exist, keyed by item. Items with nothing stored are absent from the result.</returns>
    /// <remarks>
    /// Present because merging a projected catalog page with library state is the single hottest read in
    /// the platform, and doing it one item at a time is how a list of fifty becomes fifty round trips against
    /// the eventual relational store.
    /// </remarks>
    ValueTask<IReadOnlyDictionary<MediaItemRef, LibraryFacet>> FindLibraryManyAsync(
        IReadOnlyList<MediaItemRef> references,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a file, minting an identifier for one the store has not seen.
    /// </summary>
    /// <param name="file">The file. An identifier of zero asks the store to mint one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file's identifier.</returns>
    ValueTask<MediaFileId> UpsertFileAsync(MediaFileRecord file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a file record.
    /// </summary>
    /// <param name="file">The file's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when the store has no such file.</returns>
    ValueTask<MediaFileRecord?> FindFileAsync(MediaFileId file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a file satisfies a unit.
    /// </summary>
    /// <param name="link">The join row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the link is stored.</returns>
    /// <exception cref="Abstractions.Errors.ArronixException">
    /// The link would break one of the shape's declared cardinality or span rules.
    /// </exception>
    ValueTask LinkAsync(UnitFileLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a link.
    /// </summary>
    /// <param name="link">The join row. The ordinal is ignored when matching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the link is gone.</returns>
    ValueTask UnlinkAsync(UnitFileLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the files satisfying a unit.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The links, ordered by ordinal where the shape gives the ordinal meaning.</returns>
    ValueTask<IReadOnlyList<UnitFileLink>> LinksForUnitAsync(
        MediaItemRef unit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the units a file satisfies.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The links.</returns>
    ValueTask<IReadOnlyList<UnitFileLink>> LinksForFileAsync(
        MediaFileId file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records, updates or removes one membership of a cross-cutting group.
    /// </summary>
    /// <param name="membership">The membership.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the membership is stored.</returns>
    ValueTask SetGroupMembershipAsync(GroupMembership membership, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one membership of a cross-cutting group.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="member">The member leaving it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the membership is gone.</returns>
    ValueTask RemoveGroupMembershipAsync(
        MediaItemRef group,
        MediaItemRef member,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a group's members.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memberships, ordered by their sort index.</returns>
    ValueTask<IReadOnlyList<GroupMembership>> MembersOfAsync(
        MediaItemRef group,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the groups an item belongs to.
    /// </summary>
    /// <param name="member">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memberships.</returns>
    ValueTask<IReadOnlyList<GroupMembership>> GroupsOfAsync(
        MediaItemRef member,
        CancellationToken cancellationToken = default);
}
