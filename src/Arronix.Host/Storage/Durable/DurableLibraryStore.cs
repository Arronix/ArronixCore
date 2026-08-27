using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// The store a composed host uses: the library facet, kept durably, and an explicit refusal for everything
/// this milestone does not keep.
/// </summary>
/// <remarks>
/// <para>
/// The facet half is what the durable vertical needs and is the half a catalog refresh must not be able to
/// reach, so it is the half that moves to the database. Files, unit-file links and group memberships get no
/// tables here: the relationships they encode are constrained by later gates, and giving them a schema now
/// would harden a shared one before anything has told it what to hold.
/// </para>
/// <para>
/// So a write to one of those seams is refused rather than accepted into memory. Accepting it would tell a
/// caller its data was stored by a store whose whole purpose is that data survives, and it would not; a
/// refusal naming the seam is the only answer that is true. Reads answer empty, because empty is what is
/// actually held.
/// </para>
/// <para>
/// Presence and monitoring are all this milestone keeps of a facet. A facet arriving with a chosen variant,
/// a path, a root folder or tags is refused, because writing it without them would silently lose what the
/// caller decided.
/// </para>
/// </remarks>
/// <param name="schema">The created schema, and the factory contexts come from.</param>
/// <param name="kinds">The registry a facet's declared invariants are read from.</param>
internal sealed class DurableLibraryStore(
    StoreSchema schema,
    MediaKindRegistry kinds) : IMediaStore
{
    private const string Deferred =
        "This build keeps catalog records, library presence and monitoring. Files, unit-file links and group "
        + "memberships have no durable home yet, so writing one is refused rather than accepted into memory "
        + "and lost on restart.";

    private readonly IDbContextFactory<ArronixStoreContext> _contexts =
        (schema ?? throw new ArgumentNullException(nameof(schema))).Contexts;

    private readonly MediaKindRegistry _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));

    /// <inheritdoc />
    public async ValueTask<LibraryFacet?> FindLibraryAsync(
        MediaItemRef reference,
        CancellationToken cancellationToken = default)
    {
        var kind = reference.Kind.Value;
        var level = reference.Level.Value;
        var identity = reference.Id.Value;

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entry = await store.LibraryEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kind && candidate.Level == level && candidate.Identity == identity,
                cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        var answers = await store.LibraryMonitors
            .AsNoTracking()
            .Where(candidate => candidate.EntryId == entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Read(reference, entry, answers);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<MediaItemRef, LibraryFacet>> FindLibraryManyAsync(
        IReadOnlyList<MediaItemRef> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);

        var found = new Dictionary<MediaItemRef, LibraryFacet>(references.Count);

        if (references.Count == 0)
        {
            return found;
        }

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // One query per scope rather than one per item: merging a projected page with library state is the
        // platform's hottest read, and a page of fifty must not become fifty round trips.
        foreach (var scope in references.GroupBy(static reference => (reference.Kind, reference.Level)))
        {
            var kind = scope.Key.Kind.Value;
            var level = scope.Key.Level.Value;
            var identities = scope.Select(static reference => reference.Id.Value).Distinct().ToArray();

            var entries = await store.LibraryEntries
                .AsNoTracking()
                .Where(candidate => candidate.Kind == kind
                    && candidate.Level == level
                    && identities.Contains(candidate.Identity))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entries.Count == 0)
            {
                continue;
            }

            var ids = entries.Select(static entry => entry.Id).ToArray();

            var answers = await store.LibraryMonitors
                .AsNoTracking()
                .Where(candidate => ids.Contains(candidate.EntryId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var entry in entries)
            {
                var reference = new MediaItemRef(
                    scope.Key.Kind,
                    scope.Key.Level,
                    Abstractions.Identity.MediaItemId.FromInt64(entry.Identity));

                found[reference] = Read(
                    reference,
                    entry,
                    [.. answers.Where(candidate => candidate.EntryId == entry.Id)]);
            }
        }

        return found;
    }

    /// <inheritdoc />
    public async ValueTask UpsertLibraryAsync(LibraryFacet facet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facet);

        LibraryFacetRules.RequireDeclaredMonitoring(ShapeOf(facet.Ref), facet);
        RequirePersistable(facet);

        var kind = facet.Ref.Kind.Value;
        var level = facet.Ref.Level.Value;
        var identity = facet.Ref.Id.Value;

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await store.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var entry = await store.LibraryEntries
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kind && candidate.Level == level && candidate.Identity == identity,
                cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            entry = new LibraryEntryRow
            {
                Kind = kind,
                Level = level,
                Identity = identity,
                AddedAt = facet.AddedAt,
            };

            store.LibraryEntries.Add(entry);
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            entry.AddedAt = facet.AddedAt;

            var stale = await store.LibraryMonitors
                .Where(candidate => candidate.EntryId == entry.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            store.LibraryMonitors.RemoveRange(stale);
        }

        foreach (var (dimension, choice) in facet.Monitor)
        {
            store.LibraryMonitors.Add(new LibraryMonitorRow
            {
                EntryId = entry.Id,
                Dimension = dimension,
                Choice = choice,
            });
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<MediaFileId> UpsertFileAsync(MediaFileRecord file, CancellationToken cancellationToken = default)
        => throw Refuse();

    /// <inheritdoc />
    /// <remarks>No file has been recorded, so there is none to find.</remarks>
    public ValueTask<MediaFileRecord?> FindFileAsync(MediaFileId file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<MediaFileRecord?>(null);
    }

    /// <inheritdoc />
    public ValueTask LinkAsync(UnitFileLink link, CancellationToken cancellationToken = default)
        => throw Refuse();

    /// <inheritdoc />
    public ValueTask UnlinkAsync(UnitFileLink link, CancellationToken cancellationToken = default)
        => throw Refuse();

    /// <inheritdoc />
    /// <remarks>Nothing links a file to a unit yet, so every unit answers with no links.</remarks>
    public ValueTask<IReadOnlyList<UnitFileLink>> LinksForUnitAsync(
        MediaItemRef unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<UnitFileLink>>([]);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<UnitFileLink>> LinksForFileAsync(
        MediaFileId file,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<UnitFileLink>>([]);
    }

    /// <inheritdoc />
    public ValueTask SetGroupMembershipAsync(
        GroupMembership membership,
        CancellationToken cancellationToken = default)
        => throw Refuse();

    /// <inheritdoc />
    public ValueTask RemoveGroupMembershipAsync(
        MediaItemRef group,
        MediaItemRef member,
        CancellationToken cancellationToken = default)
        => throw Refuse();

    /// <inheritdoc />
    /// <remarks>No membership has been recorded, so every group answers with no members.</remarks>
    public ValueTask<IReadOnlyList<GroupMembership>> MembersOfAsync(
        MediaItemRef group,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<GroupMembership>>([]);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<GroupMembership>> GroupsOfAsync(
        MediaItemRef member,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<GroupMembership>>([]);
    }

    private static ArronixException Refuse() => new(CoreErrorCode.InvalidConfiguration, Deferred);

    private static LibraryFacet Read(
        MediaItemRef reference,
        LibraryEntryRow entry,
        IReadOnlyList<LibraryMonitorRow> answers) =>
        new()
        {
            Ref = reference,
            AddedAt = entry.AddedAt,
            Monitor = answers.ToDictionary(
                static answer => answer.Dimension,
                static answer => answer.Choice,
                StringComparer.Ordinal),
        };

    private static void RequirePersistable(LibraryFacet facet)
    {
        var unheld = new List<string>();

        if (facet.SelectedVariant is not null)
        {
            unheld.Add(nameof(LibraryFacet.SelectedVariant));
        }

        if (facet.Path is not null)
        {
            unheld.Add(nameof(LibraryFacet.Path));
        }

        if (facet.RootFolderPath is not null)
        {
            unheld.Add(nameof(LibraryFacet.RootFolderPath));
        }

        if (facet.Tags.Count > 0)
        {
            unheld.Add(nameof(LibraryFacet.Tags));
        }

        if (unheld.Count > 0)
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"This build keeps library presence and monitoring only, so a facet stating "
                + $"{string.Join(", ", unheld)} is refused rather than written without it. Chosen variants, "
                + "paths and tags arrive with the work that gives them meaning.");
        }
    }

    private ValidatedShape ShapeOf(MediaItemRef reference)
    {
        var registered = _kinds.Require(reference.Kind);

        return registered.Shape.HasLevel(reference.Level)
            ? registered.Shape
            : throw new ArronixException(
                CoreErrorCode.MediaItemNotFound,
                $"Media kind '{reference.Kind}' has no level '{reference.Level}'.");
    }
}
