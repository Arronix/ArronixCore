using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Media.Catalog;

/// <summary>One catalog identifier bound to the local identity it resolved to.</summary>
/// <param name="Kind">The media kind.</param>
/// <param name="Level">The level, which with the kind is the identifier's key space.</param>
/// <param name="CatalogId">The catalog identifier.</param>
/// <param name="Identity">The local identity it resolves to.</param>
internal readonly record struct CatalogIdentityAssignment(
    MediaKindId Kind,
    MediaLevelId Level,
    ExternalId CatalogId,
    MediaItemId Identity);

/// <summary>One local identity that a merge replaced with another.</summary>
/// <param name="Kind">The media kind.</param>
/// <param name="Level">The level.</param>
/// <param name="Superseded">The identity a caller may still hold.</param>
/// <param name="Surviving">The identity it now resolves to.</param>
internal readonly record struct CatalogIdentitySupersession(
    MediaKindId Kind,
    MediaLevelId Level,
    MediaItemId Superseded,
    MediaItemId Surviving);

/// <summary>The high-water mark of the identities issued for one media kind.</summary>
/// <param name="Kind">The media kind.</param>
/// <param name="Issued">The greatest identity issued so far.</param>
internal readonly record struct CatalogIdentityAllocation(MediaKindId Kind, long Issued);

/// <summary>Everything the allocator knew when it was last written.</summary>
/// <param name="Assignments">Every identifier bound to an identity.</param>
/// <param name="Supersessions">Every identity a merge replaced.</param>
/// <param name="Allocations">Each kind's high-water mark.</param>
internal sealed record CatalogIdentityState(
    IReadOnlyList<CatalogIdentityAssignment> Assignments,
    IReadOnlyList<CatalogIdentitySupersession> Supersessions,
    IReadOnlyList<CatalogIdentityAllocation> Allocations)
{
    /// <summary>Gets the state of an allocator that has never issued anything.</summary>
    internal static CatalogIdentityState Empty { get; } = new([], [], []);
}

/// <summary>What one assignment changed.</summary>
/// <param name="Kind">The media kind the change is scoped to.</param>
/// <param name="Level">The level it is scoped to.</param>
/// <param name="Assignments">The bindings to write, which includes any moved by a merge.</param>
/// <param name="Supersessions">The merges to record.</param>
/// <param name="Issued">The kind's new high-water mark when this assignment minted one; otherwise null.</param>
internal sealed record CatalogIdentityCommit(
    MediaKindId Kind,
    MediaLevelId Level,
    IReadOnlyList<CatalogIdentityAssignment> Assignments,
    IReadOnlyList<CatalogIdentitySupersession> Supersessions,
    long? Issued);

/// <summary>
/// Where the assignment rule's own state is kept between processes.
/// </summary>
/// <remarks>
/// The rule stays in <see cref="CatalogIdentity"/>; this is only where its answers are written down. One
/// commit is one transaction, so an identity that was handed out was recorded, and a restart continues the
/// sequence rather than reissuing numbers the library is already keyed by.
/// </remarks>
internal interface ICatalogIdentityJournal
{
    /// <summary>Reads everything previously written.</summary>
    /// <returns>The state.</returns>
    CatalogIdentityState Load();

    /// <summary>Writes one assignment's change as one transaction.</summary>
    /// <param name="commit">The change.</param>
    void Commit(CatalogIdentityCommit commit);
}
