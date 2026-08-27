using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// The narrow local store this vertical needs, with every table, key and index stated.
/// </summary>
/// <remarks>
/// Nothing is discovered; no convention decides a column. Cross-media groups, unit and file links,
/// profiles, operation records and import state are deliberately absent — later gates constrain those
/// relationships, and inventing their tables now would harden a shared schema before anything has told it
/// what to hold.
/// </remarks>
/// <param name="options">How the context connects.</param>
internal sealed class ArronixStoreContext(DbContextOptions<ArronixStoreContext> options) : DbContext(options)
{
    internal DbSet<CatalogIdentityRow> CatalogIdentities => Set<CatalogIdentityRow>();

    internal DbSet<CatalogRedirectRow> CatalogRedirects => Set<CatalogRedirectRow>();

    internal DbSet<CatalogAllocationRow> CatalogAllocations => Set<CatalogAllocationRow>();

    internal DbSet<CatalogRecordRow> CatalogRecords => Set<CatalogRecordRow>();

    internal DbSet<LibraryEntryRow> LibraryEntries => Set<LibraryEntryRow>();

    internal DbSet<LibraryMonitorRow> LibraryMonitors => Set<LibraryMonitorRow>();

    internal DbSet<ProviderDefinitionRow> ProviderDefinitions => Set<ProviderDefinitionRow>();

    internal DbSet<ProviderDefinitionSettingRow> ProviderDefinitionSettings => Set<ProviderDefinitionSettingRow>();

    internal DbSet<ProviderDefinitionKindRow> ProviderDefinitionKinds => Set<ProviderDefinitionKindRow>();

    internal DbSet<ProviderDefinitionTagRow> ProviderDefinitionTags => Set<ProviderDefinitionTagRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<CatalogIdentityRow>(row =>
        {
            row.ToTable(StoreTables.CatalogIdentity);
            row.HasKey(entry => new { entry.Kind, entry.Level, entry.Scheme, entry.Value });
            row.Property(entry => entry.Kind).IsRequired();
            row.Property(entry => entry.Level).IsRequired();
            row.Property(entry => entry.Scheme).IsRequired();
            row.Property(entry => entry.Value).IsRequired();

            // Every identifier that resolved to one identity, in one seek: the read a merge makes.
            row.HasIndex(entry => new { entry.Kind, entry.Level, entry.Identity });
        });

        modelBuilder.Entity<CatalogRedirectRow>(row =>
        {
            row.ToTable(StoreTables.CatalogRedirect);
            row.HasKey(entry => new { entry.Kind, entry.Level, entry.Superseded });
            row.Property(entry => entry.Kind).IsRequired();
            row.Property(entry => entry.Level).IsRequired();
        });

        modelBuilder.Entity<CatalogAllocationRow>(row =>
        {
            row.ToTable(StoreTables.CatalogAllocation);
            row.HasKey(entry => entry.Kind);
            row.Property(entry => entry.Kind).IsRequired();
        });

        modelBuilder.Entity<CatalogRecordRow>(row =>
        {
            row.ToTable(StoreTables.CatalogRecord);
            row.HasKey(entry => new { entry.Kind, entry.Level, entry.Identity });
            row.Property(entry => entry.Kind).IsRequired();
            row.Property(entry => entry.Level).IsRequired();
            row.Property(entry => entry.CatalogScheme).IsRequired();
            row.Property(entry => entry.CatalogValue).IsRequired();
            row.Property(entry => entry.Title).IsRequired();
            row.Property(entry => entry.ContractMetadataHash).IsRequired();
            row.Property(entry => entry.Payload).IsRequired();

            // One catalog identifier materializes one record in its own scope; a repeated add is refused here.
            row.HasIndex(entry => new { entry.Kind, entry.Level, entry.CatalogScheme, entry.CatalogValue })
                .IsUnique();

            // The browse order, taken in the store rather than by reading every payload.
            row.HasIndex(entry => new { entry.Kind, entry.Level, entry.Title });

            // A lost update on the catalog half is detected rather than silently overwriting a refresh.
            row.Property(entry => entry.Revision).IsConcurrencyToken();
        });

        modelBuilder.Entity<LibraryEntryRow>(row =>
        {
            row.ToTable(StoreTables.LibraryEntry);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).ValueGeneratedOnAdd();
            row.Property(entry => entry.Kind).IsRequired();
            row.Property(entry => entry.Level).IsRequired();
            row.HasIndex(entry => new { entry.Kind, entry.Level, entry.Identity }).IsUnique();
        });

        modelBuilder.Entity<LibraryMonitorRow>(row =>
        {
            row.ToTable(StoreTables.LibraryMonitor);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).ValueGeneratedOnAdd();
            row.Property(entry => entry.Dimension).IsRequired();
            row.Property(entry => entry.Choice).IsRequired();
            row.HasIndex(entry => new { entry.EntryId, entry.Dimension }).IsUnique();

            row.HasOne<LibraryEntryRow>()
                .WithMany()
                .HasForeignKey(entry => entry.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderDefinitionRow>(row =>
        {
            row.ToTable(StoreTables.ProviderDefinition);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).ValueGeneratedNever();
            row.Property(entry => entry.Provider).IsRequired();
            row.Property(entry => entry.Name).IsRequired();
        });

        modelBuilder.Entity<ProviderDefinitionSettingRow>(row =>
        {
            row.ToTable(StoreTables.ProviderDefinitionSetting);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).ValueGeneratedOnAdd();
            row.Property(entry => entry.FieldId).IsRequired();
            row.Property(entry => entry.Value).IsRequired();
            row.HasIndex(entry => new { entry.DefinitionId, entry.FieldId }).IsUnique();

            row.HasOne<ProviderDefinitionRow>()
                .WithMany()
                .HasForeignKey(entry => entry.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderDefinitionKindRow>(row =>
        {
            row.ToTable(StoreTables.ProviderDefinitionKind);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).ValueGeneratedOnAdd();
            row.Property(entry => entry.Kind).IsRequired();
            row.HasIndex(entry => entry.DefinitionId);

            row.HasOne<ProviderDefinitionRow>()
                .WithMany()
                .HasForeignKey(entry => entry.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderDefinitionTagRow>(row =>
        {
            row.ToTable(StoreTables.ProviderDefinitionTag);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).ValueGeneratedOnAdd();
            row.Property(entry => entry.Value).IsRequired();
            row.HasIndex(entry => entry.DefinitionId);

            row.HasOne<ProviderDefinitionRow>()
                .WithMany()
                .HasForeignKey(entry => entry.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
