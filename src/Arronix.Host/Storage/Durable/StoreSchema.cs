using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// That the local database exists and holds this build's tables.
/// </summary>
/// <remarks>
/// <para>
/// A dependency rather than a start step, and taken by every durable component, so the schema exists before
/// anything reads through it. A start step would leave the ordering to whichever service resolved first,
/// which is the kind of rule that holds until the day a constructor moves.
/// </para>
/// <para>
/// The schema is created, not migrated. There is no earlier version of it to come from, and inventing an
/// upgrade path before a second version exists would be inventing the shape of a change nobody has made.
/// </para>
/// </remarks>
internal sealed class StoreSchema
{
    /// <summary>Creates the schema if the database does not already hold it.</summary>
    /// <param name="contexts">Where a context comes from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contexts"/> is <see langword="null"/>.</exception>
    public StoreSchema(IDbContextFactory<ArronixStoreContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        using (var store = contexts.CreateDbContext())
        {
            // The folder an operator named may not exist yet, and "unable to open database file" is not a
            // message that tells them which folder or why. The database's own directory is this store's to
            // make; anything above it is the operator's.
            if (store.Database.GetConnectionString() is { } connection
                && new SqliteConnectionStringBuilder(connection).DataSource is { Length: > 0 } file
                && Path.GetDirectoryName(Path.GetFullPath(file)) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            store.Database.EnsureCreated();
        }

        Contexts = contexts;
    }

    /// <summary>Gets the factory, which now yields contexts over a database that holds the tables.</summary>
    internal IDbContextFactory<ArronixStoreContext> Contexts { get; }
}
