using Arronix.Host.Configuration;
using Arronix.Host.Intent;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Storage;
using Arronix.Host.Storage.Durable;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arronix.Host.Composition;

/// <summary>
/// Registers the storage seams.
/// </summary>
/// <remarks>
/// <para>
/// One local database behind three seams: the catalog records an add materializes, the library facet the
/// user owns, and the identity the platform holds both under. The database is the narrow mechanism this
/// vertical needs to be honestly durable and is not a statement about the platform's eventual storage
/// contract.
/// </para>
/// <para>
/// The file and group halves of <see cref="IMediaStore"/> have no durable home yet, because the
/// relationships they encode are constrained by later gates. A composed host therefore refuses a write to
/// one rather than accepting it into memory: nothing falls back, silently or otherwise.
/// </para>
/// </remarks>
internal static class StorageRegistration
{
    /// <summary>
    /// Registers the durable store and the seams over it.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddDurableStorage(this IServiceCollection services)
    {
        services.AddDbContextFactory<ArronixStoreContext>(static (provider, builder) =>
            builder.UseSqlite(ConnectionStringOf(provider.GetRequiredService<IOptions<StoreOptions>>().Value)));

        services.TryAddSingleton<StoreSchema>();

        services.TryAddSingleton<IMediaStore, DurableLibraryStore>();
        services.TryAddSingleton<ICatalogRecordStore, DurableCatalogRecordStore>();
        services.TryAddSingleton<ICatalogIdentityJournal, DurableCatalogIdentityJournal>();
        services.TryAddSingleton<Providers.IProviderDefinitionJournal, DurableProviderDefinitionJournal>();
        services.TryAddSingleton<IStandardActionDispatcher, StandardActionDispatcher>();

        return services;
    }

    private static string ConnectionStringOf(StoreOptions options) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = options.DataSource,

            // Referential integrity is enforced by the database rather than swept up afterwards.
            ForeignKeys = true,
        }.ToString();
}
