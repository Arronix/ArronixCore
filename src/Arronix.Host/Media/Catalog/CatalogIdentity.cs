using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Media.Catalog;

/// <summary>
/// The durable local identity the platform holds catalog items under.
/// </summary>
/// <remarks>
/// <para>
/// Host state, with the host's lifetime. It is deliberately not owned by a media kind's runtime: that
/// runtime is rebuilt whenever its extension is reloaded, and identity that was rebuilt with it would be
/// reissued while the library rows keyed by it survived.
/// </para>
/// <para>
/// Identifiers are scoped by media kind and level. An item's and a group's identifiers are different key
/// spaces, so the same scheme and value in each are two different things. A local identity is unique within
/// its media kind and no further: two kinds hold the same number for different entities, so every lookup
/// here carries the whole scope rather than the number alone.
/// </para>
/// <para>
/// In memory in this milestone. What a persistent implementation has to reproduce is the assignment rule,
/// not this storage.
/// </para>
/// </remarks>
public sealed class CatalogIdentity
{
    private readonly Dictionary<Scope, MediaItemId> _assigned = [];
    private readonly Dictionary<Assignment, MediaItemId> _superseded = [];
    private readonly Dictionary<MediaKindId, long> _issued = [];
    private readonly Lock _gate = new();

    /// <summary>Determines whether a scheme is in the canonical form the platform routes by.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <returns><see langword="true"/> when it is non-empty, lower-case and free of white space.</returns>
    public static bool IsCanonicalScheme(string? scheme)
        => !string.IsNullOrWhiteSpace(scheme)
            && scheme.All(static character => !char.IsWhiteSpace(character) && !char.IsUpper(character));

    /// <summary>
    /// Assigns, idempotently, the reference one catalog item is held under.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="level">The level the entity sits at, which is its key space.</param>
    /// <param name="catalogIds">Every catalog identifier the entity is known by.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalogIds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="catalogIds"/> is empty or holds an identifier that is not well formed.
    /// </exception>
    public MediaItemRef Identify(
        MediaKindId kind,
        MediaLevelId level,
        IReadOnlyCollection<ExternalId> catalogIds)
    {
        ArgumentNullException.ThrowIfNull(catalogIds);

        if (catalogIds.Count == 0)
        {
            throw new ArgumentException(
                "An entity with no catalog identifier cannot be given a durable identity.",
                nameof(catalogIds));
        }

        foreach (var candidate in catalogIds)
        {
            RequireWellFormed(candidate, nameof(catalogIds));
        }

        lock (_gate)
        {
            var known = catalogIds
                .Select(id => _assigned.TryGetValue(new Scope(kind, level, id), out var existing)
                    ? existing
                    : (MediaItemId?)null)
                .OfType<MediaItemId>()
                .Distinct()
                .OrderBy(static existing => existing.Value)
                .ToArray();

            var identity = known.Length == 0 ? Mint(kind) : known[0];

            // Identifiers assigned separately have turned out to name one entity. The later assignments are
            // superseded by the earlier one and recorded as such, so a caller still holding one resolves.
            // Both the record and the reassignment are confined to this kind and level: the same number in
            // another scope belongs to another entity.
            foreach (var superseded in known.Skip(1))
            {
                _superseded[new Assignment(kind, level, superseded)] = identity;

                foreach (var moved in _assigned
                    .Where(entry => entry.Key.Kind == kind
                        && entry.Key.Level == level
                        && entry.Value == superseded)
                    .Select(static entry => entry.Key)
                    .ToArray())
                {
                    _assigned[moved] = identity;
                }
            }

            foreach (var candidate in catalogIds)
            {
                _assigned[new Scope(kind, level, candidate)] = identity;
            }

            return new MediaItemRef(kind, level, identity);
        }
    }

    /// <summary>
    /// Reads the reference already assigned to a catalog identifier.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="level">The level the entity sits at.</param>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="reference">The reference, when one has been assigned.</param>
    /// <returns><see langword="true"/> when one has been assigned.</returns>
    public bool TryIdentify(
        MediaKindId kind,
        MediaLevelId level,
        ExternalId catalogId,
        out MediaItemRef reference)
    {
        lock (_gate)
        {
            if (_assigned.TryGetValue(new Scope(kind, level, catalogId), out var identity))
            {
                reference = new MediaItemRef(kind, level, identity);
                return true;
            }
        }

        reference = default;
        return false;
    }

    /// <summary>
    /// Resolves a reference whose local identity has been superseded by a merge.
    /// </summary>
    /// <param name="reference">The reference held by the caller.</param>
    /// <returns>The surviving reference, or <paramref name="reference"/> when it was never superseded.</returns>
    /// <remarks>
    /// This resolves the identity only. Library rows already written under a superseded identity are not
    /// moved; migrating them is a store operation this milestone does not have.
    /// </remarks>
    public MediaItemRef Canonical(MediaItemRef reference)
    {
        lock (_gate)
        {
            var identity = reference.Id;

            // Scoped to the reference's own kind and level, and bounded: each step moves to a strictly
            // lower identity within that one scope.
            while (_superseded.TryGetValue(new Assignment(reference.Kind, reference.Level, identity), out var surviving)
                && surviving != identity)
            {
                identity = surviving;
            }

            return reference with { Id = identity };
        }
    }

    /// <summary>
    /// Places one assignment directly, so a test can build the numeric collisions across kinds and levels
    /// that the allocator does not produce on its own. Not a production path.
    /// </summary>
    internal MediaItemRef Assign(MediaKindId kind, MediaLevelId level, ExternalId catalogId, MediaItemId identity)
    {
        RequireWellFormed(catalogId, nameof(catalogId));

        lock (_gate)
        {
            _assigned[new Scope(kind, level, catalogId)] = identity;
            _issued[kind] = Math.Max(_issued.GetValueOrDefault(kind), identity.Value);
            return new MediaItemRef(kind, level, identity);
        }
    }

    /// <summary>Issues the next identity for one kind. Local identity is unique within its kind.</summary>
    private MediaItemId Mint(MediaKindId kind)
    {
        var next = _issued.GetValueOrDefault(kind) + 1;
        _issued[kind] = next;
        return MediaItemId.FromInt64(next);
    }

    internal static void RequireWellFormed(ExternalId catalogId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(catalogId.Value))
        {
            throw new ArgumentException("A catalog identifier carries a value.", parameterName);
        }

        if (!IsCanonicalScheme(catalogId.Scheme))
        {
            throw new ArgumentException(
                $"'{catalogId.Scheme}' is not a canonical catalog scheme; a scheme is lower-case and carries "
                + "no white space.",
                parameterName);
        }
    }

    /// <summary>One entity's key space: the kind, the level within it, and the catalog identifier.</summary>
    private readonly record struct Scope(MediaKindId Kind, MediaLevelId Level, ExternalId Id);

    /// <summary>One assigned local identity, scoped: the same number in another kind or level is not it.</summary>
    private readonly record struct Assignment(MediaKindId Kind, MediaLevelId Level, MediaItemId Identity);
}

/// <summary>Reads the identifier a cataloger's own scheme contributes to an item it returned.</summary>
internal static class CatalogScheme
{
    /// <summary>
    /// Finds the one identifier in a cataloger's own scheme among an item's identifiers.
    /// </summary>
    /// <param name="scheme">The cataloger's declared scheme.</param>
    /// <param name="catalogIds">The item's external identifiers.</param>
    /// <param name="identity">The single identifier in that scheme, when there is exactly one.</param>
    /// <returns><see langword="true"/> when the item states exactly one identifier in that scheme.</returns>
    internal static bool TryIdentityIn(
        string scheme,
        IReadOnlyList<ExternalId> catalogIds,
        out ExternalId identity)
    {
        identity = default;

        if (string.IsNullOrWhiteSpace(scheme) || catalogIds is null)
        {
            return false;
        }

        var found = false;

        foreach (var candidate in catalogIds)
        {
            if (!string.Equals(candidate.Scheme, scheme, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(candidate.Value))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            identity = candidate;
            found = true;
        }

        return found;
    }
}
