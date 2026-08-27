using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// The assignment rule's state, written to the local database.
/// </summary>
/// <remarks>
/// <para>
/// One commit is one transaction. The bindings, the merges it recorded and the kind's new high-water mark
/// are written together or not at all, so a process that stopped mid-assignment restarts either before it
/// or after it, never inside it.
/// </para>
/// <para>
/// A merge moves rows. G04 recorded that resolving a superseded identity moved nothing because there was no
/// store to move anything in; there is now, so the same transaction that records the merge re-keys the
/// catalog record and the library entry onto the surviving identity. Where both identities already carry
/// one, the surviving row stays and the superseded entry's earlier addition date and any monitoring answer
/// the survivor does not state are carried over, so nothing the user decided is lost to a merge.
/// </para>
/// <para>
/// Synchronous because the rule it serves assigns under a lock, and a lock cannot be held across an await.
/// The writes are small and local.
/// </para>
/// </remarks>
/// <param name="schema">The created schema, and the factory contexts come from.</param>
internal sealed class DurableCatalogIdentityJournal(StoreSchema schema)
    : ICatalogIdentityJournal
{
    private readonly IDbContextFactory<ArronixStoreContext> _contexts =
        (schema ?? throw new ArgumentNullException(nameof(schema))).Contexts;

    /// <inheritdoc />
    public CatalogIdentityState Load()
    {
        using var store = _contexts.CreateDbContext();

        var assignments = store.CatalogIdentities
            .AsNoTracking()
            .ToList()
            .Select(static row => new CatalogIdentityAssignment(
                MediaKindId.FromString(row.Kind),
                MediaLevelId.FromString(row.Level),
                new ExternalId(row.Scheme, row.Value),
                MediaItemId.FromInt64(row.Identity)))
            .ToArray();

        var supersessions = store.CatalogRedirects
            .AsNoTracking()
            .ToList()
            .Select(static row => new CatalogIdentitySupersession(
                MediaKindId.FromString(row.Kind),
                MediaLevelId.FromString(row.Level),
                MediaItemId.FromInt64(row.Superseded),
                MediaItemId.FromInt64(row.Surviving)))
            .ToArray();

        var allocations = store.CatalogAllocations
            .AsNoTracking()
            .ToList()
            .Select(static row => new CatalogIdentityAllocation(MediaKindId.FromString(row.Kind), row.Issued))
            .ToArray();

        return new CatalogIdentityState(assignments, supersessions, allocations);
    }

    /// <inheritdoc />
    public void Commit(CatalogIdentityCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        using var store = _contexts.CreateDbContext();
        using var transaction = store.Database.BeginTransaction();

        foreach (var assignment in commit.Assignments)
        {
            Bind(store, assignment);
        }

        foreach (var supersession in commit.Supersessions)
        {
            Record(store, supersession);
            Move(store, supersession);
        }

        if (commit.Issued is { } issued)
        {
            Allocate(store, commit.Kind, issued);
        }

        store.SaveChanges();
        transaction.Commit();
    }

    private static void Bind(ArronixStoreContext store, CatalogIdentityAssignment assignment)
    {
        var kind = assignment.Kind.Value;
        var level = assignment.Level.Value;
        var scheme = assignment.CatalogId.Scheme;
        var value = assignment.CatalogId.Value;

        // The tracker first: a binding written earlier in this same transaction is not in the database yet,
        // and adding a second row for it would be an identity conflict rather than an update.
        var row = store.CatalogIdentities.Local.FirstOrDefault(candidate =>
                candidate.Kind == kind
                && candidate.Level == level
                && candidate.Scheme == scheme
                && candidate.Value == value)
            ?? store.CatalogIdentities.FirstOrDefault(candidate =>
                candidate.Kind == kind
                && candidate.Level == level
                && candidate.Scheme == scheme
                && candidate.Value == value);

        if (row is null)
        {
            store.CatalogIdentities.Add(new CatalogIdentityRow
            {
                Kind = kind,
                Level = level,
                Scheme = scheme,
                Value = value,
                Identity = assignment.Identity.Value,
            });

            return;
        }

        row.Identity = assignment.Identity.Value;
    }

    private static void Record(ArronixStoreContext store, CatalogIdentitySupersession supersession)
    {
        var kind = supersession.Kind.Value;
        var level = supersession.Level.Value;
        var superseded = supersession.Superseded.Value;

        var row = store.CatalogRedirects.FirstOrDefault(candidate =>
            candidate.Kind == kind && candidate.Level == level && candidate.Superseded == superseded);

        if (row is null)
        {
            store.CatalogRedirects.Add(new CatalogRedirectRow
            {
                Kind = kind,
                Level = level,
                Superseded = superseded,
                Surviving = supersession.Surviving.Value,
            });

            return;
        }

        row.Surviving = supersession.Surviving.Value;
    }

    private static void Allocate(ArronixStoreContext store, MediaKindId kind, long issued)
    {
        var value = kind.Value;
        var row = store.CatalogAllocations.FirstOrDefault(candidate => candidate.Kind == value);

        if (row is null)
        {
            store.CatalogAllocations.Add(new CatalogAllocationRow { Kind = value, Issued = issued });
            return;
        }

        // Never backwards: an identity already handed out must not be issued again.
        row.Issued = Math.Max(row.Issued, issued);
    }

    private static void Move(ArronixStoreContext store, CatalogIdentitySupersession supersession)
    {
        var kind = supersession.Kind.Value;
        var level = supersession.Level.Value;
        var from = supersession.Superseded.Value;
        var to = supersession.Surviving.Value;

        MoveRecord(store, kind, level, from, to);
        MoveLibraryEntry(store, kind, level, from, to);
    }

    private static void MoveRecord(ArronixStoreContext store, string kind, string level, long from, long to)
    {
        var rows = Live(
            store,
            store.CatalogRecords.Where(candidate => candidate.Kind == kind
                && candidate.Level == level
                && (candidate.Identity == from || candidate.Identity == to)),
            row => row.Kind == kind && row.Level == level && (row.Identity == from || row.Identity == to));

        if (rows.FirstOrDefault(row => row.Identity == from) is not { } superseded)
        {
            return;
        }

        store.CatalogRecords.Remove(superseded);

        if (rows.Any(row => row.Identity == to))
        {
            // The survivor already has a record — possibly one an earlier move in this same transaction
            // put there. Nothing the user owns is in it and the next refresh rewrites it, so the superseded
            // copy is dropped and the survivor keeps one deterministic owner.
            return;
        }

        // The identity is part of the record's key, so the row is re-keyed by being written under the new one.
        store.CatalogRecords.Add(new CatalogRecordRow
        {
            Kind = kind,
            Level = level,
            Identity = to,
            CatalogScheme = superseded.CatalogScheme,
            CatalogValue = superseded.CatalogValue,
            Title = superseded.Title,
            CatalogState = superseded.CatalogState,
            ContractMetadataHash = superseded.ContractMetadataHash,
            Payload = superseded.Payload,
            RefreshedAt = superseded.RefreshedAt,
            Revision = superseded.Revision,
        });
    }

    private static void MoveLibraryEntry(ArronixStoreContext store, string kind, string level, long from, long to)
    {
        var entries = Live(
            store,
            store.LibraryEntries.Where(candidate => candidate.Kind == kind
                && candidate.Level == level
                && (candidate.Identity == from || candidate.Identity == to)),
            row => row.Kind == kind && row.Level == level && (row.Identity == from || row.Identity == to));

        if (entries.FirstOrDefault(row => row.Identity == from) is not { } superseded)
        {
            return;
        }

        if (entries.FirstOrDefault(row => row.Identity == to) is not { } surviving)
        {
            // Identity is an indexed column rather than the key here, so the entry moves in place and keeps
            // the monitoring answers already attached to it.
            superseded.Identity = to;
            return;
        }

        if (superseded.AddedAt is { } added && (surviving.AddedAt is null || added < surviving.AddedAt))
        {
            surviving.AddedAt = added;
        }

        var answered = Live(
                store,
                store.LibraryMonitors.Where(candidate => candidate.EntryId == surviving.Id),
                row => row.EntryId == surviving.Id)
            .Select(static row => row.Dimension)
            .ToHashSet(StringComparer.Ordinal);

        var carried = Live(
            store,
            store.LibraryMonitors.Where(candidate => candidate.EntryId == superseded.Id),
            row => row.EntryId == superseded.Id);

        foreach (var answer in carried)
        {
            // Added once: an answer a previous move in this transaction already carried over is present in
            // the tracker, not yet in the database, and adding it again would break the one-answer-per-axis
            // rule the moment the transaction commits.
            if (answered.Add(answer.Dimension))
            {
                store.LibraryMonitors.Add(new LibraryMonitorRow
                {
                    EntryId = surviving.Id,
                    Dimension = answer.Dimension,
                    Choice = answer.Choice,
                });
            }
        }

        store.LibraryEntries.Remove(superseded);
    }

    /// <summary>
    /// The rows a move must reason over: what the database holds plus what this transaction has already
    /// changed and not yet saved.
    /// </summary>
    /// <remarks>
    /// One commit can carry several merges. A move that reads only the database cannot see the row an
    /// earlier move in the same transaction retargeted or created, so it concludes the survivor has nothing
    /// and writes a second one — a duplicate key, or a duplicated monitoring answer, at commit time.
    /// Matching is on each row's current values, which is what the earlier move changed.
    /// </remarks>
    private static IReadOnlyList<TRow> Live<TRow>(
        ArronixStoreContext store,
        IQueryable<TRow> fromDatabase,
        Func<TRow, bool> matches)
        where TRow : class
    {
        var found = new List<TRow>();

        foreach (var row in store.Set<TRow>().Local)
        {
            if (matches(row) && store.Entry(row).State != EntityState.Deleted)
            {
                found.Add(row);
            }
        }

        // A tracking query hands back the instance already being tracked, so a row reached both ways is one
        // object and is counted once.
        foreach (var row in fromDatabase.ToList())
        {
            if (!found.Contains(row) && matches(row) && store.Entry(row).State != EntityState.Deleted)
            {
                found.Add(row);
            }
        }

        return found;
    }
}
