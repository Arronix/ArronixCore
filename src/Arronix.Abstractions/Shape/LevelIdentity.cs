using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Declares which kinds of record a level has: the shared catalog half, the user-owned library half,
/// or both.
/// </summary>
/// <remarks>
/// <para>
/// Three of the four surveyed applications invented this split independently, each of them late and each
/// of them through a schema migration, and in all three every downstream foreign key ended up pointing at
/// the catalog record rather than at the library record.
/// </para>
/// <para>
/// It is a facet of a level rather than a level of its own, and no store can guess it: whether rows may
/// exist for entities the user has not added determines the direction of every relationship below it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record LevelIdentity
{
    /// <summary>
    /// Gets a value indicating whether the level has a globally de-duplicated, externally keyed record
    /// that is shared across library entries and may exist without one.
    /// </summary>
    public required bool HasCatalogRecord { get; init; }

    /// <summary>
    /// Gets a value indicating whether the level has user-owned state: monitoring, profiles, tags,
    /// added-at.
    /// </summary>
    public required bool HasLibraryRecord { get; init; }

    /// <summary>
    /// Gets a value indicating whether the level keeps a redirect chain when an external catalog merges
    /// two identifiers into one.
    /// </summary>
    public bool SupportsIdentifierRedirects { get; init; }

    /// <summary>
    /// Gets the identifier roles this level requires in order to function at all.
    /// </summary>
    /// <remarks>
    /// The role half of external identity, and the half a media kind owns. A kind states that it needs a
    /// primary work identifier; which scheme fills that role is a fact about the installed catalogers,
    /// which is why it is not stated here. A level that requires a role no installed cataloger fills has
    /// no identifier search and no identity stamp in its folder names, and that must surface as a health
    /// warning rather than as a silent degradation.
    /// </remarks>
    public IReadOnlyList<IdentifierRole> RequiredRoles { get; init; } = [];

    /// <summary>
    /// Gets the identifier roles this level will carry when a cataloger supplies one, and does without
    /// otherwise.
    /// </summary>
    public IReadOnlyList<IdentifierRole> AdmittedRoles { get; init; } = [];

    /// <summary>
    /// Gets the external catalogs this level admits identifiers from.
    /// </summary>
    /// <remarks>
    /// The scheme half, and the half the host owns for a level whose kind declares
    /// <see cref="RequiredRoles"/>: composed from the catalogers installed at registration and empty until
    /// one is. A level that names its schemes directly is stating provider knowledge inside a media kind.
    /// </remarks>
    public IReadOnlyList<ExternalIdScheme> ExternalIds { get; init; } = [];
}
