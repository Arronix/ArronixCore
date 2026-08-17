using Arronix.Abstractions.Shape;

// The shape contracts are experimental; membership is expressed in their identity types.
#pragma warning disable ARX0013

namespace Arronix.Host.Storage;

/// <summary>
/// One item's membership of one cross-cutting group.
/// </summary>
/// <param name="Group">The group.</param>
/// <param name="Member">The item that belongs to it.</param>
/// <param name="Position">
/// Where the member sits, as the source states it. A string because the observed values include
/// <c>"2.5"</c>, <c>"1-3"</c> and the empty string, none of which is a number.
/// </param>
/// <param name="SortIndex">The host's orderable interpretation of <paramref name="Position"/>.</param>
/// <param name="IsPrimary">
/// Whether this is the membership a single-valued naming token resolves against, for a member that belongs
/// to several groups.
/// </param>
/// <remarks>
/// Position is carried twice on purpose. The declared string is what the source said and what a user expects
/// to see; the sort index is what an ordering needs. Deriving the second from the first at read time would
/// make every list ordering depend on a parse that fails for exactly the values that motivated the string.
/// </remarks>
public readonly record struct GroupMembership(
    MediaItemRef Group,
    MediaItemRef Member,
    string? Position,
    long SortIndex,
    bool IsPrimary);
