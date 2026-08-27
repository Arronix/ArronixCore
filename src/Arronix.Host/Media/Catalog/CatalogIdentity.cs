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
/// The rule is here; where its answers are written down is not. A composed host supplies a journal, so an
/// identity that was handed out was recorded and a restart continues the sequence instead of reissuing
/// numbers the library is already keyed by. The unjournaled form exists for fixtures and unit tests.
/// </para>
/// </remarks>
public sealed class CatalogIdentity
{
    private readonly Dictionary<Scope, MediaItemId> _assigned = [];
    private readonly Dictionary<Assignment, MediaItemId> _superseded = [];
    private readonly Dictionary<MediaKindId, long> _issued = [];
    private readonly Lock _gate = new();
    private readonly ICatalogIdentityJournal? _journal;

    /// <summary>
    /// Creates an allocator that keeps its answers only for as long as it lives.
    /// </summary>
    /// <remarks>
    /// The form a projection fixture and a unit test use, where nothing outlives the assertion. A running
    /// host composes the journaled form; this one is never registered.
    /// </remarks>
    public CatalogIdentity()
    {
    }

    /// <summary>
    /// Creates an allocator that writes its answers down and continues from what it reads.
    /// </summary>
    /// <param name="journal">Where the answers are kept.</param>
    /// <exception cref="ArgumentNullException"><paramref name="journal"/> is <see langword="null"/>.</exception>
    internal CatalogIdentity(ICatalogIdentityJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;

        var state = journal.Load();

        foreach (var assignment in state.Assignments)
        {
            _assigned[new Scope(assignment.Kind, assignment.Level, assignment.CatalogId)] = assignment.Identity;
        }

        foreach (var supersession in state.Supersessions)
        {
            _superseded[new Assignment(supersession.Kind, supersession.Level, supersession.Superseded)] =
                supersession.Surviving;
        }

        foreach (var allocation in state.Allocations)
        {
            _issued[allocation.Kind] = allocation.Issued;
        }
    }

    /// <summary>Determines whether a scheme is in the canonical form the platform routes by.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <returns>
    /// <see langword="true"/> when it is non-empty, lower-case, free of white space, and carries no
    /// <c>:</c>.
    /// </returns>
    /// <remarks>
    /// The separator is excluded because an identifier is written and read back as <c>scheme:value</c>,
    /// split at the first <c>:</c>. A scheme containing one renders text that reads back as a different
    /// identifier — <c>foo:bar</c> with value <c>x</c> becomes <c>foo:bar:x</c>, which parses as scheme
    /// <c>foo</c> and value <c>bar:x</c>. Nothing else about the spelling is decided here.
    /// </remarks>
    public static bool IsCanonicalScheme(string? scheme)
        => !string.IsNullOrWhiteSpace(scheme)
            && scheme.All(static character =>
                !char.IsWhiteSpace(character) && !char.IsUpper(character) && character != SchemeSeparator);

    /// <summary>The character an identifier's written form puts between its scheme and its value.</summary>
    private const char SchemeSeparator = ':';

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

            var minted = known.Length == 0 ? _issued.GetValueOrDefault(kind) + 1 : (long?)null;
            var identity = minted is { } next ? MediaItemId.FromInt64(next) : known[0];

            // Keyed by identifier: the same one arrives more than once whenever the identifier asked for is
            // also the one the catalog answered with, and a binding is one row per identifier either way.
            var bindings = new Dictionary<ExternalId, MediaItemId>();
            var supersessions = new List<CatalogIdentitySupersession>();

            // Identifiers assigned separately have turned out to name one entity. The later assignments are
            // superseded by the earlier one and recorded as such, so a caller still holding one resolves.
            // Both the record and the reassignment are confined to this kind and level: the same number in
            // another scope belongs to another entity.
            foreach (var superseded in known.Skip(1))
            {
                supersessions.Add(new CatalogIdentitySupersession(kind, level, superseded, identity));

                foreach (var moved in _assigned
                    .Where(entry => entry.Key.Kind == kind
                        && entry.Key.Level == level
                        && entry.Value == superseded)
                    .Select(static entry => entry.Key.Id)
                    .ToArray())
                {
                    bindings[moved] = identity;
                }
            }

            foreach (var candidate in catalogIds)
            {
                bindings[candidate] = identity;
            }

            var assignments = bindings
                .Select(binding => new CatalogIdentityAssignment(kind, level, binding.Key, binding.Value))
                .ToArray();

            // Written before it is believed. The change is computed against the state as it stands, so a
            // journal that refuses leaves the allocator exactly as the caller found it rather than holding
            // an identity nothing recorded.
            _journal?.Commit(new CatalogIdentityCommit(kind, level, assignments, supersessions, minted));

            foreach (var supersession in supersessions)
            {
                _superseded[new Assignment(kind, level, supersession.Superseded)] = supersession.Surviving;
            }

            foreach (var assignment in assignments)
            {
                _assigned[new Scope(kind, level, assignment.CatalogId)] = assignment.Identity;
            }

            if (minted is { } issued)
            {
                _issued[kind] = issued;
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
    /// This resolves the identity. The rows keyed by a superseded one are moved when the merge is recorded,
    /// in the same transaction, so a caller holding an old reference and a caller holding the surviving one
    /// reach the same record and the same library entry.
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
                + "no white space and no ':'.",
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
