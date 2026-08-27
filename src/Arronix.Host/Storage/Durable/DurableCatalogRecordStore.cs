using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Common.Lifetimes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// The catalog record store, backed by the local database.
/// </summary>
/// <param name="schema">The created schema, and the factory contexts come from.</param>
/// <param name="clock">The clock a write is stamped with.</param>
internal sealed class DurableCatalogRecordStore(
    StoreSchema schema,
    TimeProvider clock) : ICatalogRecordStore
{
    private readonly IDbContextFactory<ArronixStoreContext> _contexts =
        (schema ?? throw new ArgumentNullException(nameof(schema))).Contexts;

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>SQLITE_CONSTRAINT_UNIQUE.</summary>
    private const int SqliteUniqueViolation = 2067;

    /// <summary>SQLITE_CONSTRAINT_PRIMARYKEY.</summary>
    private const int SqlitePrimaryKeyViolation = 1555;

    /// <summary>The character that makes the next one in a pattern ordinary.</summary>
    private const string LikeEscape = "\\";

    /// <inheritdoc />
    public async ValueTask<CatalogRecord?> FindAsync(
        MediaItemRef reference,
        CancellationToken cancellationToken = default)
    {
        var kind = reference.Kind.Value;
        var level = reference.Level.Value;
        var identity = reference.Id.Value;

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await store.CatalogRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kind && candidate.Level == level && candidate.Identity == identity,
                cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Read(row);
    }

    /// <inheritdoc />
    public async ValueTask<CatalogRecord?> FindByCatalogIdAsync(
        MediaKindId kind,
        MediaLevelId level,
        ExternalId catalogId,
        CancellationToken cancellationToken = default)
    {
        var kindValue = kind.Value;
        var levelValue = level.Value;
        var scheme = catalogId.Scheme;
        var value = catalogId.Value;

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await store.CatalogRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kindValue
                    && candidate.Level == levelValue
                    && candidate.CatalogScheme == scheme
                    && candidate.CatalogValue == value,
                cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Read(row);
    }

    /// <inheritdoc />
    public async ValueTask<CatalogRecordPage> PageAsync(
        MediaKindId kind,
        MediaLevelId level,
        CatalogRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        var kindValue = kind.Value;
        var levelValue = level.Value;

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var matching = store.CatalogRecords
            .AsNoTracking()
            .Where(candidate => candidate.Kind == kindValue && candidate.Level == levelValue);

        if (!string.IsNullOrWhiteSpace(query.TextSearch))
        {
            // The wildcards belong to the pattern this builds, not to what the user typed. A title
            // containing '%' or '_' searches for that character, and the escape character escapes itself.
            var pattern = $"%{Escape(query.TextSearch)}%";
            matching = matching.Where(candidate => EF.Functions.Like(candidate.Title, pattern, LikeEscape));
        }

        var total = await matching.CountAsync(cancellationToken).ConfigureAwait(false);

        // The identity order is the level's natural one, and it is the tiebreak under a title order too, so
        // two records sharing a title never swap places between one page and the next.
        var ordered = (query.ByTitle, query.Descending) switch
        {
            (true, false) => matching.OrderBy(candidate => candidate.Title).ThenBy(candidate => candidate.Identity),
            (true, true) => matching.OrderByDescending(candidate => candidate.Title).ThenBy(candidate => candidate.Identity),
            (false, true) => matching.OrderByDescending(candidate => candidate.Identity),
            _ => matching.OrderBy(candidate => candidate.Identity),
        };

        // Computed wide and checked, because a page number a caller is free to choose must not be able to
        // multiply into a negative offset. Past the end of a 32-bit offset there is nothing to return, and
        // the total still says how much there was.
        var offset = ((long)query.Page - 1) * query.PageSize;

        if (offset > int.MaxValue)
        {
            return new CatalogRecordPage([], total);
        }

        var rows = await ordered
            .Skip((int)offset)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CatalogRecordPage([.. rows.Select(Read)], total);
    }

    /// <inheritdoc />
    public async ValueTask<CatalogRecordMaterialization> MaterializeAsync(
        CatalogRecord record,
        DateTimeOffset addedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await store.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (await HeldAsync(store, record, cancellationToken).ConfigureAwait(false) is { } already)
        {
            // The record was written by an earlier add. Its presence was written in the same transaction,
            // so there is nothing missing to repair here.
            return new CatalogRecordMaterialization(Read(already), Created: false);
        }

        store.CatalogRecords.Add(Write(record, revision: 1, _clock.GetUtcNow()));
        await PresentAsync(store, record.Reference, addedAt, cancellationToken).ConfigureAwait(false);

        try
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsUniquenessConflict(failure))
        {
            // Another writer reached one of the two unique addresses first. That, and only that, is
            // reconcilable — and only when what it wrote is this same item under this same identifier. A
            // disk, schema or connection failure is not this and is not caught here.
            store.ChangeTracker.Clear();

            var winner = await HeldAsync(store, record, cancellationToken).ConfigureAwait(false)
                ?? throw new ArronixException(
                    CoreErrorCode.CatalogIdentityInvalid,
                    $"Materializing '{record.Reference}' from "
                    + $"'{record.CatalogId.Scheme}:{record.CatalogId.Value}' lost a race to a write that is "
                    + "not the same record, so this add cannot be reconciled with what is stored.",
                    failure);

            return new CatalogRecordMaterialization(Read(winner), Created: false);
        }

        return new CatalogRecordMaterialization(record with { Revision = 1 }, Created: true);
    }

    /// <summary>Records that the user holds this item, unless they already did.</summary>
    /// <remarks>
    /// In the same transaction as the record, and never restamping an existing entry: the date somebody
    /// added an item is theirs, and a retried add is not a second decision.
    /// </remarks>
    private static async Task PresentAsync(
        ArronixStoreContext store,
        MediaItemRef reference,
        DateTimeOffset addedAt,
        CancellationToken cancellationToken)
    {
        var kind = reference.Kind.Value;
        var level = reference.Level.Value;
        var identity = reference.Id.Value;

        var held = await store.LibraryEntries
            .AnyAsync(
                candidate => candidate.Kind == kind && candidate.Level == level && candidate.Identity == identity,
                cancellationToken)
            .ConfigureAwait(false);

        if (held)
        {
            return;
        }

        store.LibraryEntries.Add(new LibraryEntryRow
        {
            Kind = kind,
            Level = level,
            Identity = identity,
            AddedAt = addedAt,
        });
    }

    /// <inheritdoc />
    public async ValueTask<CatalogRecord> RefreshAsync(
        CatalogRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var kind = record.Reference.Kind.Value;
        var level = record.Reference.Level.Value;
        var identity = record.Reference.Id.Value;

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await store.CatalogRecords
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kind && candidate.Level == level && candidate.Identity == identity,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArronixException(
                CoreErrorCode.MediaItemNotFound,
                $"No catalog record is held under '{record.Reference}', so there is nothing to refresh.");

        // A refresh restates what one catalog says about a record it owns. A different scheme is a different
        // authority, and letting one overwrite another's record here would make the record's owner a
        // property of whoever refreshed last.
        if (!string.Equals(row.CatalogScheme, record.CatalogId.Scheme, StringComparison.Ordinal))
        {
            throw new ArronixException(
                CoreErrorCode.CatalogIdentityInvalid,
                $"'{record.Reference}' is held under catalog '{row.CatalogScheme}', and this refresh states "
                + $"'{record.CatalogId.Scheme}'. A catalog refreshes only the records it is the authority for.");
        }

        // Only the catalog-owned half. The user-owned facet is a different table and is not reachable here.
        row.CatalogScheme = record.CatalogId.Scheme;
        row.CatalogValue = record.CatalogId.Value;
        row.Title = record.Title;
        row.CatalogState = (int)record.State;
        row.ContractMetadataHash = record.ContractMetadataHash;
        row.Payload = record.Payload;
        row.RefreshedAt = _clock.GetUtcNow();
        row.Revision++;

        try
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException failure)
        {
            throw new ArronixException(
                CoreErrorCode.CatalogIdentityInvalid,
                $"'{record.Reference}' was refreshed by another writer while this refresh was being written, "
                + "so this one is refused rather than replacing what that writer stored.",
                failure);
        }

        return Read(row);
    }

    /// <summary>
    /// Whether a write failed because a uniqueness rule refused it, rather than because the store did.
    /// </summary>
    /// <remarks>
    /// Both codes matter: the natural key is a unique index and the reference is the primary key, and the
    /// same race can land on either. Anything else is a real storage failure and must reach the caller.
    /// </remarks>
    private static bool IsUniquenessConflict(DbUpdateException failure)
        => !ProcessFailure.IsFatal(failure)
            && ProcessFailure.Chain(failure)
                .OfType<SqliteException>()
                .Any(static sqlite => sqlite.SqliteExtendedErrorCode
                    is SqliteUniqueViolation or SqlitePrimaryKeyViolation);

    /// <summary>
    /// Finds the record already held for this item, when this add is a repeat of an earlier one.
    /// </summary>
    /// <returns>That record, or <see langword="null"/> when neither address is taken.</returns>
    /// <exception cref="ArronixException">
    /// The two addresses disagree about which item this is. Answering with whichever row was found would
    /// bind them together silently.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Both addresses are looked up, not one query over either, because a reference already carrying a
    /// different identifier and an identifier already carrying a different reference are different faults —
    /// and only a repeat is idempotent.
    /// </para>
    /// <para>
    /// A repeat is not only the identical pair. Two catalogs answering for one work converge on one
    /// identity, so adding through the second one is adding an item that is already here. What decides it
    /// is the scheme: an item states exactly one identifier in its own cataloger's scheme, and identifiers
    /// in other schemes are cross-references. So a second identifier in the owning scheme is a genuine
    /// conflict, and one in another scheme is the same item arriving by another road. The record keeps the
    /// authority that materialized it either way — a later catalog does not take ownership of a record by
    /// being asked second.
    /// </para>
    /// </remarks>
    private static async Task<CatalogRecordRow?> HeldAsync(
        ArronixStoreContext store,
        CatalogRecord record,
        CancellationToken cancellationToken)
    {
        var kind = record.Reference.Kind.Value;
        var level = record.Reference.Level.Value;
        var identity = record.Reference.Id.Value;
        var scheme = record.CatalogId.Scheme;
        var value = record.CatalogId.Value;

        var atReference = await store.CatalogRecords
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kind && candidate.Level == level && candidate.Identity == identity,
                cancellationToken)
            .ConfigureAwait(false);

        var atCatalogId = await store.CatalogRecords
            .FirstOrDefaultAsync(
                candidate => candidate.Kind == kind
                    && candidate.Level == level
                    && candidate.CatalogScheme == scheme
                    && candidate.CatalogValue == value,
                cancellationToken)
            .ConfigureAwait(false);

        if (atReference is null && atCatalogId is null)
        {
            return null;
        }

        if (atReference is not null && atCatalogId is not null && atReference.Identity == atCatalogId.Identity)
        {
            return atReference;
        }

        // The identity already resolved this identifier onto a record. When the identifier belongs to
        // another catalog than the one that owns the record, the two catalogs have converged on one work
        // and this add is a repeat; the record and its owner stand.
        if (atReference is not null
            && atCatalogId is null
            && !string.Equals(atReference.CatalogScheme, scheme, StringComparison.Ordinal))
        {
            return atReference;
        }

        throw new ArronixException(
            CoreErrorCode.CatalogIdentityInvalid,
            atReference is not null
                ? $"'{record.Reference}' is held under '{atReference.CatalogScheme}:{atReference.CatalogValue}', "
                    + $"and this add states '{scheme}:{value}' in that same catalog. An item states exactly "
                    + "one identifier in its own cataloger's scheme."
                : $"Catalog identifier '{scheme}:{value}' is held under "
                    + $"'{atCatalogId!.Kind}:{atCatalogId.Level}:{atCatalogId.Identity}', and this add states "
                    + $"'{record.Reference}'. One catalog record materializes one item.");
    }

    /// <summary>Escapes the wildcards a stored pattern must treat as ordinary characters.</summary>
    private static string Escape(string text) => text
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    private static CatalogRecordRow Write(CatalogRecord record, long revision, DateTimeOffset refreshedAt) =>
        new()
        {
            Kind = record.Reference.Kind.Value,
            Level = record.Reference.Level.Value,
            Identity = record.Reference.Id.Value,
            CatalogScheme = record.CatalogId.Scheme,
            CatalogValue = record.CatalogId.Value,
            Title = record.Title,
            CatalogState = (int)record.State,
            ContractMetadataHash = record.ContractMetadataHash,
            Payload = record.Payload,
            RefreshedAt = refreshedAt,
            Revision = revision,
        };

    private static CatalogRecord Read(CatalogRecordRow row) =>
        new()
        {
            Reference = new MediaItemRef(
                MediaKindId.FromString(row.Kind),
                MediaLevelId.FromString(row.Level),
                MediaItemId.FromInt64(row.Identity)),
            CatalogId = new ExternalId(row.CatalogScheme, row.CatalogValue),
            Title = row.Title,
            State = (CatalogRecordState)row.CatalogState,
            ContractMetadataHash = row.ContractMetadataHash,
            Payload = row.Payload,
            RefreshedAt = row.RefreshedAt,
            Revision = row.Revision,
        };
}
