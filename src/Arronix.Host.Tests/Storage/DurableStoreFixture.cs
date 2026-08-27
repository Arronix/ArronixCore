using System.IO;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Storage;
using Arronix.Host.Storage.Durable;
using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// One real database in a temporary folder, and the seams over it.
/// </summary>
/// <remarks>
/// A file rather than an in-memory database, because what these tests are about is what survives the
/// process that wrote it. <see cref="Reopen"/> is the restart: the same file, a new schema, new seams, and
/// nothing carried across in memory.
/// </remarks>
internal sealed class DurableStoreFixture : IDisposable
{
    private readonly string _root;

    internal DurableStoreFixture()
    {
        _root = Directory.CreateTempSubdirectory("arronix-g07b-store").FullName;
        DataSource = Path.Combine(_root, "arronix.db");
        Schema = new StoreSchema(new Factory(DataSource));
    }

    internal string DataSource { get; }

    internal StoreSchema Schema { get; private set; }

    /// <summary>Starts again over the same file, as a new process would.</summary>
    internal void Reopen() => Schema = new StoreSchema(new Factory(DataSource));

    internal DurableCatalogRecordStore Records(TimeProvider? clock = null)
        => new(Schema, clock ?? TimeProvider.System);

    internal ArronixStoreContext Read() => Schema.Contexts.CreateDbContext();

    /// <summary>The allocator, reading back whatever the file already holds.</summary>
    internal Arronix.Host.Media.Catalog.CatalogIdentity Identity()
        => new(new DurableCatalogIdentityJournal(Schema));

    internal DurableProviderDefinitionJournal Definitions() => new(Schema);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>One catalog record, with an opaque payload the store never reads.</summary>
    internal static CatalogRecord Record(
        MediaItemRef reference,
        string scheme,
        string value,
        string title = "A title",
        CatalogRecordState state = CatalogRecordState.Active,
        byte[]? payload = null)
        => new()
        {
            Reference = reference,
            CatalogId = ExternalId.Of(scheme, value),
            Title = title,
            State = state,
            ContractMetadataHash = "HASH",
            Payload = payload ?? [1, 2, 3],
        };

    internal static MediaItemRef Item(long identity)
        => new(
            MediaKindId.FromString("works"),
            MediaLevelId.FromString("work"),
            MediaItemId.FromInt64(identity));

    private sealed class Factory(string dataSource) : IDbContextFactory<ArronixStoreContext>
    {
        public ArronixStoreContext CreateDbContext()
            => new(new DbContextOptionsBuilder<ArronixStoreContext>()
                .UseSqlite($"Data Source={dataSource};Foreign Keys=True")
                .Options);
    }
}
