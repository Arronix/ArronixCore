# Arronix Storage Layer — Design

> **Status:** Design. No code has been written. This document is the direct input to the Storage-Layer
> milestone, which is the milestone `docs/design/unified-host-runtime.md` §8 names as the trigger for
> "Persistence / relational `IMediaStore`".
>
> **Target:** `src/Arronix.Host/Storage/` (implementation, Tier B), plus one additive change to
> `Arronix.Abstractions` (§7.6) and one new package registration (§6.1). Nothing under `src/NzbDrone.*`,
> `src/Sonarr.*` or `frontend/` is touched.
>
> **Governing documents:** `docs/design/unified-host-runtime.md` (authoritative; its §1 resolution table,
> §2 shape model and §4.6 storage seam are treated as fixed input), `ARCHITECTURE.md` §5/§11,
> `docs/arronix-common-extraction-plan.md` (three-tier promotion rule), `docs/contracts/stability.md`.
>
> **Empirical basis:** the six survey reports, the legacy `src/NzbDrone.Core/Datastore/` tree, the four
> reference `*arr` datastores, and **four executed experiments against FluentMigrator 8.0.1 and SQLite**
> (§0). Every claim about the FluentMigrator API in this document was run, not read.
>
> **Non-negotiable inheritances.** Schema starts at v1; there is no upgrade path from any existing `*arr`
> install. The core is media-agnostic: no table, column, index or enum member in the core schema names a
> TV, movie, music or book concept. **No EF Core** (hard contributor rule). Dapper + FluentMigrator +
> SQLite/Postgres, all already registered in `Directory.Packages.props`.

---

## How the contested decisions were resolved

Same format as `docs/arronix-common-extraction-plan.md`. Where this document recommends against something
the reader will expect, the row says so in the first three words.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | How is per-plugin migration numbering scoped? | (a) one `VersionInfo` table with an owner column; (b) offset each plugin's numbers into a reserved band; (c) one FluentMigrator version table **per owner**. | **(c)** | Verified executable (§0): `.WithVersionTable()` + one `IServiceProvider` per owner gives three independent sequences all starting at 1 in one SQLite file; (a) needs a fork of `VersionLoader`, (b) makes plugin ids a global registry. |
| 2 | Do plugins ship `[Migration]` classes? | The obvious answer is yes, as every `*arr` does. | **No — plugins ship a declarative schema document; the host builds the migrations** | A plugin references `Arronix.Abstractions` **only**, zero packages (`unified-host-runtime.md` §7.3), so it physically cannot derive from `FluentMigrator.Migration`, and its ALC would throw on the attempt (#21). This is not a preference; it is the only shape the invariants permit. Verified working end-to-end (§0.4). |
| 3 | Per-plugin Postgres `SCHEMA`, or a name prefix on both engines? | Postgres schemas are the "right" relational answer and give `DROP SCHEMA … CASCADE`. | **Name prefix on both engines; no per-plugin Postgres schema** | SQLite has no in-file schema namespace, so schemas mean the two engines take different code paths through every plugin's DDL — an untested branch per plugin. The prefix is one rule, one path, and purge is a `DROP TABLE` loop over a ledger the host already keeps. |
| 4 | Is table-name ownership prevented or detected? | Prevention needs an `IMigrationProcessor` decorator binding to FluentMigrator internals. | **Prevented, because the schema is declarative** | Decision #2 turns this from a hard problem into a trivial one: the host validates every table name in the plugin's schema document *before* constructing a single expression. Detection (a post-run `sqlite_master`/`information_schema` diff into the ledger) is kept as the belt to the braces. |
| 5 | Uninstall: drop the plugin's data or keep it? | Prowlarr's `RemoveMissingImplementations` deletes rows for missing implementations. | **Keep. Purge is a separate, explicit, destructive action** | `unified-host-runtime.md` #42 already inverts Prowlarr here for provider definitions; applying a different rule to tables would mean uninstalling a plugin for ten minutes destroys the user's library state. |
| 6 | Does purge use `MigrateDown(0)` or drop the ledger's tables? | `MigrateDown` is the framework-native answer and it works (verified, §0.3). | **Drop the ledger's tables. `Down()` is never called and never required** | Requiring a correct `Down()` for every migration is a tax the surveyed apps refused to pay — `NzbDroneMigrationBase.Down()` throws `NotImplementedException` (`src/NzbDrone.Core/Datastore/Migration/Framework/NzbDroneMigrationBase.cs:62`) after 223 migrations. A forward-only store with a ledger-driven drop is honest about that. |
| 7 | Downgrade (installed plugin older than its applied schema) | Auto-revert; or refuse. | **Refuse: quarantine with `PluginSchemaAhead`** | Auto-revert is `MigrateDown` under another name (#6). "Loaded and inert" was already rejected in #17 of the spec; "quarantined with a diagnostic naming both versions" is the same medicine. |
| 8 | Is `IMediaStore` promoted to Abstractions? | The spec holds it host-side (its #41, agreed by both inputs). | **Agreed — stays host-side, unchanged, byte for byte** | Still zero plugin implementers. `RelationalMediaStore` replaces `InMemoryMediaStore` behind the identical interface; the swap is one line in host composition and no caller changes. |
| 9 | How is polymorphic library state stored, given the host does not know the media shape? | A per-kind table set generated from the shape; or one generic table keyed by the shape's own coordinates. | **One generic table set keyed by `MediaItemRef = (kind_id, level_id, item_id)`** | The four surveyed `History` models differ **only** in their FK columns (`EpisodeId+SeriesId` / `MovieId` / `TrackId+AlbumId+ArtistId` / `BookId+AuthorId`); the triple is exactly the union, so one table replaces four. Generated per-kind tables would reintroduce the four schemas this platform exists to merge. |
| 10 | Does the host store the plugin's catalog? | The spec's #10 says no — the plugin projects it via `IMediaItemSource`. | **No, with one narrow exception: a rebuildable coordinate cache** | `IMediaStore.LinkAsync` must enforce `SpanConstraint` "against the unit's coordinates" (§4.6), which requires coordinates at write time. Stored as an explicitly *derived*, rebuildable cache with its own refresh job — not as catalog truth. |
| 11 | How are ordinal coordinates made sortable? | A packed sortable byte key; or four nullable columns. | **Four nullable `BIGINT` columns + a component count** | `OrdinalPath` is capped at four components (`unified-host-runtime.md` §2.2), so the natural relational encoding is available; a packed key is unreadable in a debugger and unindexable per component. |
| 12 | Are the `FileBinding` cardinality booleans enforced in code or in the database? | Code is easier; indexes are stronger. | **Both — the database is the source of truth** | `AtMostOneFilePerUnit`/`AtMostOneUnitPerFile` *are* partial unique indexes; the spec's own remark says the booleans are "the storage uniqueness constraints, written in the language the store enforces them in". Code pre-checks only to produce a good error message. |
| 13 | Who creates those per-kind partial indexes? | A core migration; or an activation-time reconciler. | **An activation-time DDL reconciler, ledgered** | Which indexes exist depends on which kinds are installed, which is not known at migration time. Reconciling shape → index at activation is the same admission-control pattern the capability model uses. |
| 14 | `OrdinalIsMeaningful = false` — enforced how? | A `CHECK` constraint. | **Code + integrity audit only, and this is recorded as a wart** | A unique index cannot express "this column must be NULL", and SQLite cannot add a `CHECK` to an existing table. Stating the gap beats pretending the index set is complete. |
| 15 | `SpanConstraint` enforcement | Triggers; a materialized min/max table; or a denormalized key. | **A denormalized `span_key` on the link row + one `HAVING COUNT(DISTINCT …) > 1` audit query** | Sonarr throws `InvalidSeasonException` at runtime and has no way to detect an existing violation; a denormalized key makes detection a single portable query on both engines. |
| 16 | Separate log database, as every `*arr` has? | Sonarr/Radarr/Lidarr/Readarr all ship a second SQLite file with its own migration sequence. | **No — one database, one `log` table, hard-capped** | Two databases means two version-table sets, two backups, two connection factories and an `IDatabase` every consumer must choose between. The second file exists because pre-WAL SQLite writers blocked; WAL plus a bounded table plus a trim job removes the reason. |
| 17 | Do plugin tables carry foreign keys to core tables? | Convenient for cascade deletes. | **No cross-owner foreign keys, in either direction** | A plugin→core FK makes core migration order a plugin dependency and makes purge order-sensitive; core→plugin is forbidden outright by media-agnosticism. Plugins address core state through `MediaItemRef` values, never through referential integrity. |
| 18 | Are SQLite foreign keys enabled? | Legacy leaves them off (SQLite's default) and compensates with 20 `CleanupOrphaned*` housekeepers. | **`PRAGMA foreign_keys = ON`, on every connection** | `src/NzbDrone.Core/Housekeeping/Housekeepers/` contains 14 `CleanupOrphaned*` classes that exist only because referential integrity was never turned on. Enabling it deletes most of that surface and converts "sweep the orphans nightly" into "the write failed". |
| 19 | JSON columns: `TEXT` or Postgres `JSONB`? | `JSONB` is indexable and typed. | **`TEXT` on both** | A query capability that exists on one engine and not the other is a divergence that will be used and then discovered; the surveyed apps store JSON blobs and never query inside them. |
| 20 | Keep the legacy LINQ-expression `WhereBuilder` query translator? | It is ~700 lines across `ExpressionVisitor`/`WhereBuilder`/`WhereBuilderSqlite`/`WhereBuilderPostgres` and it works. | **Drop it. Hand-written SQL, Dapper for materialization** | The store has ~35 queries, all known at compile time; a homegrown, half-implemented LINQ provider is the single largest maintenance liability in the surveyed datastore and buys nothing a `const string` does not. |
| 21 | Keep `ModelBase` / `TableMapping` / `LazyLoaded<T>`? | Every `*arr` repository is built on them. | **Drop all three** | `ModelBase` forces an `int Id` onto value types that do not want one; `TableMapping` is a mutable static registry initialized from a static constructor (`DbFactory.cs:34`); `LazyLoaded<T>` puts a lazy-loading proxy *inside a DTO*, which is why Readarr's models need `Equ`'s `[MemberwiseEqualityIgnore]` and why every list screen is an N+1. |
| 22 | SQLite ADO provider: `System.Data.SQLite` (registered) or `Microsoft.Data.Sqlite`? | The legacy tree uses `System.Data.SQLite` + `SourceGear.sqlite3`. | **`Microsoft.Data.Sqlite`, with a pinned `SQLitePCLRaw.bundle_e_sqlite3`** | The dependency policy prefers Microsoft-owned packages; `Microsoft.Data.Sqlite` is an ADO.NET-only provider with **no EF Core dependency** (it is the layer EF Core sits on, not the reverse). Caveat found while running §0: the transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.11` carries advisory **NU1903 (high)**, so the bundle version must be pinned explicitly and checked. |
| 23 | The five `*Status` tables (`IndexerStatus`, `DownloadClientStatus`, `ImportListStatus`, `NotificationStatus`, `ApplicationStatus`) | Every `*arr` ships four or five structurally identical tables. | **One `provider_status` table keyed by `(family, definition_id)`** | They are the same table five times, and they drag five identical `FixFuture*StatusTimes` housekeepers behind them. One table, one housekeeper. |
| 24 | Postgres backup | Sonarr's `BackupService.BackupDatabase()` silently does nothing unless `DatabaseType == SQLite` (`src/NzbDrone.Core/Backup/BackupService.cs`). | **Explicitly out of scope, and *said so* in the UI** | Shelling out to `pg_dump` from a container that may not have it is worse than a documented boundary. The silent no-op is the actual bug; the fix is a health warning, not a feature. |
| 25 | What does a backup contain besides the database? | The database and `config.xml`, as today. | **Database + config + a `manifest.json` naming every schema owner and its applied version** | Without it, restoring a backup taken when the Books plugin was installed into a host where it is not silently leaves orphan tables no code owns. |

---

## 0. Verification log — what was executed, not assumed

Everything in §2 rests on FluentMigrator 8.0.1 behaving in a specific way. All of it was run. Scratch
projects: `…/scratchpad/fmtest/` and `…/scratchpad/fmasm/` (throwaway; not part of the repo).

**API surface confirmed present in 8.0.1** by reading the shipped XML documentation in
`~/.nuget/packages/fluentmigrator.runner.core/8.0.1/lib/netstandard2.0/FluentMigrator.Runner.Core.xml`:

| Member | Signature as shipped in 8.0.1 |
|---|---|
| `MigrationRunnerBuilderExtensions.WithVersionTable` | `IMigrationRunnerBuilder WithVersionTable(this IMigrationRunnerBuilder, IVersionTableMetaData)` |
| `MigrationRunnerBuilderExtensions.ScanIn` | `IScanInBuilder ScanIn(this IMigrationRunnerBuilder, params Assembly[])` → `.For.Migrations()` / `.VersionTableMetaData()` / `.All()` |
| `IVersionTableMetaData` | `SchemaName`, `TableName`, `ColumnName`, `DescriptionColumnName`, `UniqueIndexName`, `AppliedOnColumnName`, `OwnsSchema`, `CreateWithPrimaryKey` — **eight** members; the pre-4.x `ApplicationContext` is gone |
| `IMigrationInformationLoader` | `SortedList<long, IMigrationInfo> LoadMigrations()` |
| `MigrationInfo` ctor | `MigrationInfo(long version, string description, TransactionBehavior, bool isBreakingChange, Func<IMigration> migrationFunc)` |
| `RunnerOptions` | `TransactionPerSession`, `Version`, `StartVersion`, `Steps`, `Tags`, `AllowBreakingChange`, `IncludeUntaggedMigrations`, … |
| `TypeFilterOptions` | `Namespace`, `NestedNamespaces` |
| `IMigrationRunner` | `MigrateUp()`, `MigrateUp(long)`, `MigrateDown(long)`, `RollbackToVersion(long)`, `ValidateVersionOrder()`, `HasMigrationsToApplyUp(long?)` |

### 0.1 Three owners, three sequences all starting at 1, one SQLite file — **PASS**

Three migration sets (`core` 1–2, `tv` 1–3, `movies` 1–2), one `Data Source=test.db`, one
`IServiceProvider` per owner, each with `.WithVersionTable(new ScopedVersionTable(owner))`. Result:

```
=== TABLES ===
core_tag  mov_movie  schema_version_core  schema_version_movies  schema_version_tv
sqlite_sequence  tv_episode  tv_series

=== schema_version_core ===   1 | M1     2 | M2
=== schema_version_tv ===     1 | M1     2 | M2     3 | M3
=== schema_version_movies === 1 | M1     2 | M2
```

No migration was skipped. This is the hazard, closed.

### 0.2 A plugin shipping its own `[VersionTableMetaData]` cannot hijack the host's — **PASS**

A class marked `[VersionTableMetaData]` returning `TableName => "VersionInfo"` (FluentMigrator's default)
was placed in the scanned assembly *and* the runner was configured with `.ScanIn(asm).For.All()`, which
registers the assembly as a version-table source. `.WithVersionTable(instance)` still won: every owner
wrote to `schema_version_<owner>`. The host's explicit instance takes priority over assembly scanning.
(The design does not rely on this — it uses `.For.Migrations()`, which never registers a version-table
source at all — but it is worth knowing the stronger form holds.)

### 0.3 Genuinely separate assemblies + `TransactionPerSession` + `ValidateVersionOrder` — **PASS**

Two real class libraries (`PluginA` 1–2, `PluginB` 1–3) referencing only `FluentMigrator`, driven from a
host that calls `ValidateVersionOrder()` then `MigrateUp()` per owner with
`RunnerOptions.TransactionPerSession = true`:

```
TABLES:  a_one  a_two  b_one  b_three  b_two  schema_version__a  schema_version__b
schema_version__a: 1 | M1 | …   2 | M2 | …
schema_version__b: 1 | M1 | …   2 | M2 | …   3 | M3 | …
AFTER RE-RUN, row counts:  2 | 3        (idempotent; nothing re-applied)
```

Uninstall/reinstall was also exercised (`MigrateDown(0)`, drop the version table, re-run): the other
owners' tables and version rows were untouched, and the reinstall replayed 1–3 cleanly. The design
does **not** use `MigrateDown` (#6), but the mechanism is proven to work either way.

### 0.4 The declarative path — a plugin that ships **no migration code at all** — **PASS**

The decisive experiment, because decision #2 says this is the only legal shape. The "plugin" contributed
plain records (`TableDef` / `ColumnDef` / `IndexDef` / `SchemaStep`) and nothing else. The host supplied
one `DeclarativeMigration : Migration` type that translates a step into fluent expressions, and one
`IMigrationInformationLoader` that yields `MigrationInfo(version, description, …, () => new
DeclarativeMigration(step, prefix))`. The `IMigrationInformationLoader` was registered directly into the
service collection, replacing `DefaultMigrationInformationLoader`; `ScanIn` was not called at all.

```
1: DeclarativeMigration migrating → CreateTable p_tv__series
2: DeclarativeMigration migrating → CreateIndex p_tv__series (title)
1: DeclarativeMigration migrating → CreateTable p_movies__movie

  p_movies__movie | table        p_tv__ix_series_title | index      p_tv__series | table
  schema_version__p_movies | table                      schema_version__p_tv | table
  tv v=1   tv v=2   mov v=1
```

Generated DDL, read back from `sqlite_master`:

```sql
CREATE TABLE "p_tv__series" ("id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "title" TEXT NOT NULL)
CREATE TABLE "schema_version__p_tv" ("version" INTEGER NOT NULL, "applied_on" DATETIME,
    "description" TEXT, CONSTRAINT "ux_schema_version__p_tv" PRIMARY KEY ("version"))
```

Two things worth recording from that output. `AsInt64().PrimaryKey().Identity()` renders as
`INTEGER … AUTOINCREMENT` on SQLite — FluentMigrator's SQLite type map already special-cases the rowid
alias, so a 64-bit surrogate key is safe. And every identifier is emitted **quoted**, which is what makes
mixed-case names behave identically on Postgres (§6.3).

### 0.5 Not verified by execution

* **Everything Postgres.** No Postgres server is available in this environment (`psql`, `pg_ctl`,
  `pg_isready` all absent). Postgres claims in §6 are derived from the legacy tree's working
  configuration (`MigrationController.cs:41,51–58`, `ConnectionStringFactory.cs:63–76`) and from
  documented engine behavior. **WP-2 must re-run §0.1–§0.4 against Postgres before anything else
  lands.**
* **`Microsoft.Data.Sqlite` under a per-plugin `AssemblyLoadContext`.** Not applicable in the chosen
  design (no plugin ever touches a data provider), but if #2 is ever revisited, this is the first thing
  to test.

---

## 1. The hazard, stated precisely

FluentMigrator's `VersionLoader` reads a set of applied version numbers out of **one** table and asks the
runner to apply every migration whose bare integer is not in that set. The legacy configuration is a
single global runner over a single assembly writing a single table:

```csharp
// src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs:36-59
var serviceProvider = new ServiceCollection()
    .AddFluentMigratorCore()
    .ConfigureRunner(builder => builder
        .AddPostgres()
        .AddNzbDroneSQLite()
        .WithGlobalConnectionString(connectionString)
        .ScanIn(Assembly.GetExecutingAssembly()).For.All())
    .Configure<TypeFilterOptions>(opt => opt.Namespace = "NzbDrone.Core.Datastore.Migration")
    …
```

```csharp
// src/NzbDrone.Core/Datastore/Database.cs:65
return db.QueryFirstOrDefault<int>(
    "SELECT \"Version\" from \"VersionInfo\" ORDER BY \"Version\" DESC LIMIT 1");
```

Every surveyed app numbers from 1, and the ranges overlap almost completely:

| Source tree | Migration files | Range |
|---|---|---|
| `ArronixCore/src/NzbDrone.Core` (Sonarr/TV) | 218 | 1 … 223 |
| `_reference/Radarr` | 140 | 1 … 242 |
| `_reference/Lidarr` | 81 | 1 … 081 |
| `_reference/Readarr` | 41 | 1 … 040 |
| `_reference/Prowlarr` | 43 | 1 … 044 |

Point four of those at one `VersionInfo` table and the first owner to reach version 40 causes the Books
plugin's migrations 1–40 to be recorded as already applied. Its tables are never created; the failure
surfaces later as a missing-table error from a query, with nothing in the log connecting it to migration
at all. The failure is silent, data-losing and arbitrarily deferred — which is why this is the first
section of this document.

**Two aggravating factors the milestone spec does not mention.** The version number space is not the only
global: table *names* are too (`Series` exists in both the TV and the Books schema —
`_reference/Readarr/.../TableMapping.cs` maps `Entity<Series>("Series")` for a *book* series), and index
names are too, under a **63-byte** Postgres identifier limit. §3 handles both.

---

## 2. The owner model and per-plugin migration scoping

### 2.1 The owner

An **owner** is the unit of schema ownership. There is exactly one `core` owner and one owner per
installed plugin. An owner has:

* an **owner id** — `core`, or `p_` + the plugin's `PluginId` slugified (lowercase, `[^a-z0-9]` → `_`,
  truncated to **16** characters, then `_` + the first 6 lowercase hex characters of an XxHash3 of the
  full `PluginId` when truncation occurred). 16 + 7 = 23 characters worst case; see §3.3 for why that
  number and not another;
* a **table prefix** — `<ownerId>__` for plugins, empty for core;
* a **version table** — `schema_version` for core, `schema_version__<ownerId>` for plugins;
* an **applied version** — the maximum row in that table;
* a **state** — `Active`, `Quarantined`, `Disabled`, `Absent`.

### 2.2 `IVersionTableMetaData`, per owner

```csharp
// src/Arronix.Host/Storage/Migrations/OwnerVersionTable.cs
namespace Arronix.Host.Storage.Migrations;

/// <remarks>
/// The whole of the per-plugin migration-scoping mechanism is this class plus one service provider per
/// owner. FluentMigrator compares bare <see cref="int"/> migration numbers against the rows of ONE table;
/// giving each owner its own table is therefore giving each owner its own number space. Verified against
/// FluentMigrator 8.0.1: three owners whose migrations are all numbered 1, 2, 3 apply independently and
/// idempotently into a single SQLite file.
/// </remarks>
internal sealed class OwnerVersionTable : IVersionTableMetaData
{
    private readonly string _ownerId;

    public OwnerVersionTable(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        _ownerId = ownerId;
    }

    /// <summary>Empty: Arronix uses no per-owner Postgres schema (resolution #3).</summary>
    public string SchemaName => string.Empty;

    public string TableName =>
        _ownerId == SchemaOwners.Core ? "schema_version" : $"schema_version__{_ownerId}";

    public string ColumnName => "version";
    public string DescriptionColumnName => "description";
    public string UniqueIndexName => $"ux_{TableName}";
    public string AppliedOnColumnName => "applied_on";
    public bool OwnsSchema => false;
    public bool CreateWithPrimaryKey => true;
}
```

`CreateWithPrimaryKey = true` makes the version column the table's primary key rather than a separate
unique index (confirmed in the §0.4 DDL dump), which is one fewer object per owner to name under the
63-byte limit.

### 2.3 The core runner — code migrations, scanned from one assembly

```csharp
// src/Arronix.Host/Storage/Migrations/CoreMigrationRunner.cs
private static ServiceProvider BuildCore(StorageOptions options)
    => new ServiceCollection()
        .AddLogging(b => b.AddNLog())
        .AddFluentMigratorCore()
        .ConfigureRunner(rb => rb
            .AddSQLite()
            .AddPostgres()
            .WithGlobalConnectionString(options.ConnectionString)
            .WithVersionTable(new OwnerVersionTable(SchemaOwners.Core))
            .ScanIn(typeof(CoreMigrationRunner).Assembly).For.Migrations())
        .Configure<TypeFilterOptions>(o =>
        {
            o.Namespace = "Arronix.Host.Storage.Schema";
            o.NestedNamespaces = false;
        })
        .Configure<RunnerOptions>(o =>
        {
            o.TransactionPerSession = true;   // the owner's whole run is one transaction
            o.AllowBreakingChange = false;
        })
        .Configure<ProcessorOptions>(o => { o.PreviewOnly = false; o.Timeout = TimeSpan.FromMinutes(5); })
        .Configure<SelectingProcessorAccessorOptions>(o => o.ProcessorId = options.Engine.FluentId)
        .Configure<SelectingGeneratorAccessorOptions>(o => o.GeneratorId = options.Engine.FluentId)
        .BuildServiceProvider(validateScopes: false);
```

`SelectingProcessorAccessorOptions` / `SelectingGeneratorAccessorOptions` with `"sqlite"` / `"postgres"`
is the legacy tree's own working pattern (`MigrationController.cs:51–58`) and is kept verbatim; it is the
only way to register both providers and pick one at runtime.

Note `.For.Migrations()`, not `.For.All()`. `.All()` additionally registers the assembly as a source of
`IVersionTableMetaData`, `IConventionSet` and embedded resources. None of those is wanted, and the
narrower call removes a whole class of accidental cross-owner discovery.

### 2.4 The plugin runner — declarative, no plugin code

This is decision #2 and it deserves the space, because the obvious design is illegal here.

`unified-host-runtime.md` §7.3 fixes `Arronix.Plugin.*` at *"ProjectReferences: **Abstractions ONLY** —
PackageReferences: **none**"*, and #21 fixes the plugin `AssemblyLoadContext` to **throw** when asked to
resolve anything else. A plugin therefore cannot reference `FluentMigrator`, cannot derive from
`FluentMigrator.Migration`, and cannot carry a `[Migration]` attribute. Any design in which plugins ship
migration classes silently requires relaxing the strongest structural invariant in the platform.

So the plugin ships **data** and the host ships the only code that touches FluentMigrator.

#### The contract the plugin implements

Additive to `Arronix.Abstractions`, new area **`ARX0018` Schema**, namespace
`Arronix.Abstractions.Schema`, every type `[Experimental(ExperimentalContracts.Schema, …)]`, gated by a
new `Capability.Persistence` (§7.6). Zero packages, no `Type`, no delegates — the same "pure data"
property that makes `MediaShape` serve three consumers.

```csharp
// src/Arronix.Abstractions/Schema/IPluginSchemaProvider.cs
/// <remarks>
/// A plugin declares its tables; it never executes DDL and never references a migration framework.
/// This is not a stylistic choice: a plugin references <c>Arronix.Abstractions</c> only and its
/// <c>AssemblyLoadContext</c> throws on anything else, so <c>FluentMigrator.Migration</c> is unreachable
/// from a plugin by construction. The host translates these declarations into migration expressions,
/// which is also what lets it VALIDATE every table name against the owner's prefix before executing
/// anything (§3.2) instead of discovering the violation afterwards.
/// </remarks>
public interface IPluginSchemaProvider
{
    /// <summary>Ordered, gapless from 1, never edited once released.</summary>
    IReadOnlyList<SchemaStep> Steps { get; }
}

public sealed record SchemaStep
{
    /// <summary>Numbered from 1, per plugin. Two plugins may both have a step 1 — that is the point.</summary>
    public required int Version { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<TableDefinition> CreateTables { get; init; } = [];
    public IReadOnlyList<ColumnAddition> AddColumns { get; init; } = [];
    public IReadOnlyList<IndexDefinition> CreateIndexes { get; init; } = [];
    public IReadOnlyList<string> DropIndexes { get; init; } = [];
    public IReadOnlyList<string> DropTables { get; init; } = [];
    /// <summary>Escape hatch. Executed verbatim, per engine, and flagged in the load report.</summary>
    public IReadOnlyList<RawStatement> Raw { get; init; } = [];
}

public sealed record TableDefinition
{
    /// <summary>Bare, unprefixed, <c>^[a-z][a-z0-9_]{0,23}$</c>. The host applies the owner prefix.</summary>
    public required string Name { get; init; }
    public required IReadOnlyList<ColumnDefinition> Columns { get; init; }
    public IReadOnlyList<string> PrimaryKey { get; init; } = [];
}

public sealed record ColumnDefinition
{
    public required string Name { get; init; }
    public required ColumnType Type { get; init; }
    public bool Nullable { get; init; } = true;
    public bool Identity { get; init; }
    public int MaxLength { get; init; }              // Text only; 0 = unbounded
    public string? DefaultLiteral { get; init; }     // validated against Type, never interpolated
}

/// <summary>
/// Closed, and deliberately small. Every member has an exact rendering on BOTH engines (§6.2); a plugin
/// cannot reach a type that exists on only one of them, which is the same reasoning that keeps JSON in a
/// <c>TEXT</c> column rather than a Postgres <c>JSONB</c> one.
/// </summary>
public enum ColumnType
{
    Int32 = 0, Int64 = 1, Decimal = 2, Boolean = 3, Text = 4,
    Instant = 5,        // UTC point in time
    CalendarDate = 6,   // date with no time
    Json = 7,           // TEXT carrying UTF-8 JSON
    Binary = 8,
}

public sealed record IndexDefinition
{
    public required string Name { get; init; }       // bare; prefixed by the host
    public required string Table { get; init; }      // bare
    public required IReadOnlyList<IndexColumn> Columns { get; init; }
    public bool Unique { get; init; }
}

public readonly record struct IndexColumn(string Name, bool Descending = false);
public sealed record ColumnAddition(string Table, ColumnDefinition Column);
public sealed record RawStatement(StorageEngine Engine, string Sql);
public enum StorageEngine { Sqlite = 0, Postgres = 1 }
```

#### The host-side translator

```csharp
// src/Arronix.Host/Storage/Migrations/DeclaredSchemaMigration.cs
internal sealed class DeclaredSchemaMigration(SchemaStep step, string prefix, StorageEngine engine)
    : FluentMigrator.Migration
{
    public override void Up()
    {
        foreach (var table in step.CreateTables) { /* Create.Table(prefix + table.Name)… */ }
        foreach (var add in step.AddColumns)     { /* Alter.Table(prefix + add.Table)…    */ }
        foreach (var ix in step.CreateIndexes)   { /* Create.Index(prefix + ix.Name)…     */ }
        foreach (var name in step.DropIndexes)   { Delete.Index(prefix + name); }
        foreach (var name in step.DropTables)    { Delete.Table(prefix + name); }
        foreach (var raw in step.Raw) if (raw.Engine == engine) Execute.Sql(raw.Sql);
    }

    /// <remarks>
    /// Arronix migrations are forward-only (resolution #6). The host never calls <c>MigrateDown</c>;
    /// purge drops the owner's tables from the ledger instead. Throwing here is the loud form of that
    /// decision — the surveyed apps write the same throw and then rely on it never happening
    /// (<c>NzbDroneMigrationBase.cs:62</c>).
    /// </remarks>
    public override void Down() =>
        throw new NotSupportedException("Arronix schema migrations are forward-only.");
}

// src/Arronix.Host/Storage/Migrations/DeclaredSchemaLoader.cs
internal sealed class DeclaredSchemaLoader(
    IReadOnlyList<SchemaStep> steps, string prefix, StorageEngine engine) : IMigrationInformationLoader
{
    public SortedList<long, IMigrationInfo> LoadMigrations()
    {
        var list = new SortedList<long, IMigrationInfo>();
        foreach (var step in steps.OrderBy(s => s.Version))
        {
            list.Add(step.Version, new MigrationInfo(
                step.Version, step.Description, TransactionBehavior.Default, isBreakingChange: false,
                () => new DeclaredSchemaMigration(step, prefix, engine)));
        }
        return list;
    }
}
```

and the runner, with `ScanIn` deliberately absent:

```csharp
private static ServiceProvider BuildPlugin(SchemaOwner owner, StorageOptions options)
{
    var services = new ServiceCollection()
        .AddLogging(b => b.AddNLog())
        .AddFluentMigratorCore()
        .ConfigureRunner(rb => rb
            .AddSQLite()
            .AddPostgres()
            .WithGlobalConnectionString(options.ConnectionString)
            .WithVersionTable(new OwnerVersionTable(owner.OwnerId)))
        .Configure<RunnerOptions>(o => { o.TransactionPerSession = true; o.AllowBreakingChange = false; })
        .Configure<ProcessorOptions>(o => o.Timeout = TimeSpan.FromMinutes(5))
        .Configure<SelectingProcessorAccessorOptions>(o => o.ProcessorId = options.Engine.FluentId)
        .Configure<SelectingGeneratorAccessorOptions>(o => o.GeneratorId = options.Engine.FluentId);

    // Replaces DefaultMigrationInformationLoader. No assembly is scanned; the plugin ships no code.
    services.AddSingleton<IMigrationInformationLoader>(
        _ => new DeclaredSchemaLoader(owner.Steps, owner.TablePrefix, options.Engine));

    return services.BuildServiceProvider(validateScopes: false);
}
```

Verified end-to-end in §0.4.

### 2.5 Ordering, atomicity and failure

`SchemaMigrationService.MigrateAllAsync()` runs during host startup, **before** any plugin is activated
and before the API binds a port:

1. Open a connection, take an advisory lock (`PRAGMA locking_mode` is not enough on SQLite; use a
   host-level `SemaphoreSlim` plus, on Postgres, `pg_advisory_lock(<constant>)` so two host processes
   pointed at one Postgres database cannot migrate concurrently).
2. Migrate `core`. `ValidateVersionOrder()` then `MigrateUp()`. Failure here is **fatal** — the host
   cannot start.
3. For each plugin owner, in ordinal `PluginId` order (deterministic, so a failure is reproducible):
   a. **Validate** the declared schema (§3.2). A violation quarantines the plugin and skips it; the host
      continues.
   b. Compare `appliedVersion` with `max(step.Version)`. If `applied > declared`, quarantine with
      `PluginSchemaAhead` naming both numbers (#7). If equal, skip.
   c. `ValidateVersionOrder()`; `MigrateUp()` inside `TransactionPerSession`.
   d. Reconcile the object ledger (§3.4). A stray object rolls the owner back to its previous applied
      version by dropping the objects created in this run and deleting the version rows above the
      previous maximum, then quarantines.
   e. Write `schema_migration_audit` rows.
4. Reconcile the shape-derived indexes (§5.4) for every kind whose plugin activated successfully.

Migration failure of a plugin is **never** fatal to the host — the same inversion as #42. A quarantined
plugin's tables are left exactly as the failed transaction left them (which, under
`TransactionPerSession`, is exactly as they were).

### 2.6 Uninstall, purge, downgrade, reinstall — the complete state table

| Event | Version table | Owner's tables | `schema_owner` row | Behavior |
|---|---|---|---|---|
| Plugin **disabled** by the user | kept | kept | `state = Disabled` | Nothing is dropped. Re-enabling replays nothing. |
| Plugin directory **removed** from disk | kept | kept | `state = Absent`, `absent_since` set | A health warning names the owner, its table count and the row count, and offers Purge. Never automatic (#5). |
| Plugin **reinstalled**, same or newer version | kept | kept | `state = Active` | `MigrateUp()` applies only steps above `applied_version`. Verified idempotent (§0.3). |
| Plugin reinstalled **older** than `applied_version` | kept | kept | `state = Quarantined` | `PluginSchemaAhead`: *"Books plugin declares schema steps up to 4; this database has applied 7. Install 1.3.0 or later, or purge the plugin's data."* No automatic revert (#7). |
| **Purge** — explicit, confirmed, destructive | dropped | dropped | row deleted | `DROP TABLE` every `owned_table` row for the owner, in reverse `created_at` order, inside one transaction, with FK enforcement off for the duration; then drop the version table; then delete the ledger rows. Never `MigrateDown` (#6). |
| Plugin **upgraded** with a step that drops a table | — | per the step | — | `DropTables` removes the ledger row too. A step that drops a table it does not own fails validation in 3(a). |

Purge is exposed as one API call requiring the plugin id and the current table count as a confirmation
token, and it emits a `SchemaOwnerPurged` audit record that survives the purge.

---

## 3. Schema ownership and namespacing

### 3.1 Which tables the core owns

Derived from the intersection of the five surveyed `TableMapping.Map()` bodies: a table is core-owned if
it appears, structurally identical modulo FK columns, in at least three of the five. Everything else is
plugin-owned. The right-hand column names the surveyed tables it replaces.

| Core table | Purpose | Replaces (per app) |
|---|---|---|
| `schema_version`, `schema_version__*` | migration state per owner | `VersionInfo` |
| `schema_owner`, `owned_object`, `schema_migration_audit` | the ownership ledger | *(new — nothing surveyed has it)* |
| `plugin`, `plugin_capability` | plugin registry and grants | *(new)* |
| `config` | host key/value settings | `Config` |
| `tag`, `root_folder` | shared library organization | `Tags`, `RootFolders` |
| `scheduled_task`, `job` | scheduler and queue | `ScheduledTasks`, `Commands` |
| `log` | bounded, capped (#16) | `Logs` **and the entire second log database** |
| `provider_definition` | all five provider families in one table | `Indexers`, `DownloadClients`, `ImportLists`, `Notifications`, `Metadata`, `IndexerProxies`, `Applications` |
| `provider_status` | backoff/failure state, one table (#23) | `IndexerStatus`, `DownloadClientStatus`, `ImportListStatus`, `NotificationStatus`, `ApplicationStatus` |
| `quality_definition`, `quality_profile`, `quality_profile_item` | the "how good" axis | same names |
| `custom_format`, `custom_format_specification`, `quality_profile_format_score` | the "which releases" axis | `CustomFormats`, `QualityProfiles.FormatItems` |
| `selection_profile`, `selection_profile_facet` | the **third** axis, media-agnostic | Lidarr/Readarr `MetadataProfiles` |
| `delay_profile`, `release_profile`, `naming_config` | policy | same names |
| `library_item`, `library_monitor`, `library_tag` | `LibraryFacet` | `Series`/`Movies`/`Artists`/`Authors` user-owned columns |
| `sequence_policy` | the demoted `Season`'s policy record | Sonarr's `Seasons` (dropped in its migrations 020/021 and re-embedded as JSON) |
| `library_coordinate` | rebuildable coordinate cache (#10) | Sonarr's `Episodes.SeasonNumber`/`AbsoluteEpisodeNumber`/`Scene*` |
| `media_file`, `media_file_language`, `unit_file_link` | files and the join | `EpisodeFiles`+`Episodes.EpisodeFileId`, `MovieFiles`+`Movies.MovieFileId`, `TrackFiles`+`Tracks.TrackFileId`, `BookFiles.EditionId`+`Part` |
| `media_group`, `group_membership` | grouping axes | `Collections`, `SeriesBookLink` |
| `history`, `history_scope` | activity | `History`/`EpisodeHistory`/`EntityHistory` |
| `download`, `download_target`, `download_message` | queue and tracked downloads | `DownloadHistory` + in-memory `TrackedDownload` |
| `blocklist`, `blocklist_scope` | blocklist | `Blocklist` |
| `pending_release`, `pending_release_target` | delayed grabs | `PendingReleases` |
| `exclusion` | import-list exclusions | `ImportListExclusions`, `ImportExclusions` |
| `extra_file` | subtitle/metadata/other sidecars, one table with a `kind` column | `MetadataFiles`, `SubtitleFiles`, `ExtraFiles`, `LyricFiles` |
| `remote_path_mapping`, `custom_filter`, `user`, `update_history` | host odds and ends | same names |

Everything media-specific is plugin-owned. From the surveyed trees that is: `Series`, `Episodes`,
`SceneMappings`; `Movies`, `MovieMetadata`, `AlternativeTitles`, `MovieTranslations`, `Credits`,
`Collections`\*; `Artists`, `ArtistMetadata`, `Albums`, `AlbumReleases`, `Tracks`; `Authors`,
`AuthorMetadata`, `Books`, `Editions`, `Series`\*, `SeriesBookLink`\*.

\* The grouping-axis tables are the interesting case. `MovieCollection` and Readarr's book `Series` are
*catalog* entities with their own metadata fetch (`GroupingAxis.HasOwnMetadata`), so the **catalog
half is plugin-owned** and the **state half is core-owned** (`media_group` carries `monitored`, the
profile and root-folder overrides; `group_membership` carries `position`, `sort_index`, `is_primary`).
That is the same catalog/library split the shape model already declares, applied one level down.

**`Series` appearing in both the TV and the Books schema is the collision this section exists to
prevent** — and under §3.3 they are `p_tv__series` and `p_books__series`, which cannot collide.

### 3.2 Validation, before any expression is constructed

`PluginSchemaValidator.Validate(owner, steps)` runs at step 3(a) of §2.5 and returns a defect list in the
`ValidatedShape` style — parse, don't validate. Once it passes, `DeclaredSchemaMigration` cannot emit a
name outside the owner's namespace, because every name it emits is `prefix + validatedName`.

| Rule | Defect |
|---|---|
| `Version` values are `1..n`, strictly increasing, no duplicates, no gaps | `SchemaStepsNotContiguous` |
| A released step's content hash matches `schema_migration_audit` | `SchemaStepEdited` (§9.3) |
| Table and index names match `^[a-z][a-z0-9_]{0,23}$` | `SchemaIdentifierMalformed` |
| Column names match `^[a-z][a-z0-9_]{0,29}$` | `SchemaIdentifierMalformed` |
| `prefix + name` is ≤ 63 bytes UTF-8, and so is every index name the host will generate | `SchemaIdentifierTooLong` |
| No name in the reserved set: `schema_version*`, `schema_owner`, `owned_object`, `schema_migration_audit`, `sqlite_*`, `pg_*`, or any core table from §3.1 | `SchemaIdentifierReserved` |
| `AddColumns` / `CreateIndexes` / `DropTables` / `DropIndexes` reference a table this owner created in this or an earlier step | `SchemaObjectNotOwned` |
| Every `ColumnType` is a declared enum member | `SchemaColumnTypeUnknown` |
| `DefaultLiteral` parses as its `ColumnType` | `SchemaDefaultInvalid` |
| At most one `Identity` column per table, and it is `Int32` or `Int64` | `SchemaIdentityInvalid` |
| No `Raw` statement matches `` /\b(attach|pragma|copy|create\s+(extension|schema|role)|grant|revoke|drop\s+(database|schema))\b/i `` | `SchemaRawStatementForbidden` |
| `Raw` is present at all | not a defect — a **warning** surfaced in the plugin's status view, because raw SQL is the one place a plugin can write engine-specific behavior |

### 3.3 Naming, and the 63-byte cliff

```
plugin table    p_<slug>__<table>
plugin index    p_<slug>__<index>
version table   schema_version__p_<slug>
core table      <table>                  (no prefix)
```

Budget, worst case, in bytes:

```
schema_version__p_<slug>          15 + 2 + 23 = 40   ok
ux_schema_version__p_<slug>       + 3         = 43   ok
p_<slug>__<table>                  2 + 23 + 2 + 24 = 51  ok
p_<slug>__<index>                  same             51   ok
```

That is why the slug is capped at 16 + 7 and the bare names at 24 and 30. **Postgres silently truncates
identifiers at `NAMEDATALEN - 1` = 63 bytes**; two names that differ only past byte 63 become one object,
and the second `CREATE` fails with a duplicate-object error whose message names a table the developer
never wrote. FluentMigrator's auto-generated constraint and index names (`IX_<table>_<col>`,
`FK_<table>_<other>`) make this reachable, so the design forbids auto-naming entirely: every index in a
declared schema **must** carry an explicit `Name`, and the validator computes the final byte length of
every identifier the run will emit. SQLite has no such limit, which is precisely why this bug would ship
undetected from a SQLite-only test suite.

### 3.4 The ledger

```sql
CREATE TABLE schema_owner (
    owner_id            TEXT      NOT NULL PRIMARY KEY,   -- 'core' | 'p_<slug>'
    kind                SMALLINT  NOT NULL,               -- 0 core, 1 plugin
    plugin_id           TEXT      NULL,                   -- full PluginId, unslugged
    plugin_version      TEXT      NULL,
    table_prefix        TEXT      NOT NULL,
    version_table       TEXT      NOT NULL,
    applied_version     INTEGER   NOT NULL DEFAULT 0,
    declared_version    INTEGER   NOT NULL DEFAULT 0,
    state               SMALLINT  NOT NULL,               -- Active|Quarantined|Disabled|Absent
    state_reason        TEXT      NULL,
    first_migrated_at   TEXT      NOT NULL,
    last_migrated_at    TEXT      NOT NULL,
    absent_since        TEXT      NULL
);

CREATE TABLE owned_object (
    owner_id     TEXT     NOT NULL REFERENCES schema_owner(owner_id) ON DELETE CASCADE,
    object_kind  SMALLINT NOT NULL,          -- 0 table, 1 index
    object_name  TEXT     NOT NULL,          -- the PHYSICAL, prefixed name
    created_at   TEXT     NOT NULL,
    PRIMARY KEY (owner_id, object_kind, object_name)
);

CREATE TABLE schema_migration_audit (
    owner_id       TEXT     NOT NULL REFERENCES schema_owner(owner_id) ON DELETE CASCADE,
    version        INTEGER  NOT NULL,
    description    TEXT     NOT NULL,
    content_hash   TEXT     NOT NULL,        -- XxHash3 of the canonical JSON of the SchemaStep
    plugin_version TEXT     NULL,
    applied_at     TEXT     NOT NULL,
    PRIMARY KEY (owner_id, version)
);
```

After each owner's run, the host lists physical objects (`SELECT name, type FROM sqlite_master WHERE
type IN ('table','index') AND name NOT LIKE 'sqlite_%'` / `SELECT tablename FROM pg_tables WHERE
schemaname = current_schema()` + `pg_indexes`) and diffs against `owned_object` ∪ the core set. A stray
object triggers the rollback in §2.5 step 3(d). A *missing* object triggers a health error, because
something outside Arronix dropped it.

`content_hash` is what makes "never edit a released migration" (§9.3) mechanically checkable — a property
the surveyed apps do not have, because a `[Migration]` class body has no stable hash the runtime can
compute, whereas a declarative `SchemaStep` serializes canonically.

---

## 4. Core schema DDL

Written in a portable subset; §6.2 gives the per-engine type rendering once so it is not repeated on
every table. Every identifier is lowercase `snake_case` and is always emitted quoted (§6.3).

### 4.1 The universal key

Every reference to a plugin-owned entity is the same triple, and it is the single most consequential
decision in the schema:

```sql
kind_id   TEXT    NOT NULL      -- MediaKindId
level_id  TEXT    NOT NULL      -- MediaLevelId
item_id   BIGINT  NOT NULL      -- MediaItemId
```

`MediaItemId` is `readonly record struct MediaItemId(long Value)`
(`src/Arronix.Abstractions/Identity/MediaItemId.cs`), and `MediaFileId` is `long` alongside it
(`src/Arronix.Abstractions/Shape/MediaFileId.cs`). `BIGINT` is the matching column type: **the CLR width and
the column width are the same width, and that is a requirement, not a coincidence.**

Earlier drafts had both structs at `int` while the columns were `BIGINT`, deliberately, on the reasoning
that a later widening would then be "a contract change and not a schema migration" — with an instruction not
to "fix" the mismatch. Both halves of that were wrong. There is no version boundary making a widening
expensive (below 1.0.0 the struct may change in a MINOR), so nothing was being bought; and §9.4 freezes
`M0001_Baseline` at the first tagged release, so an unsettled width does not stay unsettled — it ships. A
mismatch would then be permanent in the shipped v1 schema and in every `MediaItemRef` triple, with a
silent narrowing at every read boundary and a documented instruction discouraging anyone from noticing.

The rule that replaces it: **the CLR type is where the width is decided, the column rendering follows it
(`int` → `INTEGER`, `long` → `BIGINT`), and the decision is made before `M0001_Baseline` is tagged.** It has
been made — `long`, both types — so §6.2's rendering table and this triple agree by construction rather than
by hedge. A test asserting that every id column's declared type matches its CLR counterpart's width belongs
with the §9 migration tests, so the agreement is checked rather than remembered.

Three columns rather than one composite string because all three participate in different indexes:
`kind_id` gates almost every query, `(kind_id, level_id)` is the natural browse key, and `item_id` is the
join target.

### 4.2 Library state — `LibraryFacet` made relational

```sql
CREATE TABLE library_item (
    id                        BIGINT    NOT NULL PRIMARY KEY,   -- identity
    kind_id                   TEXT      NOT NULL,
    level_id                  TEXT      NOT NULL,
    item_id                   BIGINT    NOT NULL,
    -- LibraryEntry levels only; NULL elsewhere.
    path                      TEXT      NULL,
    root_folder_id            BIGINT    NULL REFERENCES root_folder(id) ON DELETE SET NULL,
    -- VariantSelection. Set on the PARENT of a VariantAxis level.
    selected_variant_level_id TEXT      NULL,
    selected_variant_item_id  BIGINT    NULL,
    auto_switch_variant       BOOLEAN   NOT NULL DEFAULT TRUE,
    quality_profile_id        BIGINT    NULL REFERENCES quality_profile(id) ON DELETE SET NULL,
    selection_profile_id      BIGINT    NULL REFERENCES selection_profile(id) ON DELETE SET NULL,
    added_at                  TEXT      NULL,
    updated_at                TEXT      NOT NULL,
    CONSTRAINT ux_library_item UNIQUE (kind_id, level_id, item_id),
    CONSTRAINT ck_library_item_variant
        CHECK ((selected_variant_level_id IS NULL) = (selected_variant_item_id IS NULL))
);
CREATE INDEX ix_library_item_kind_level ON library_item (kind_id, level_id);
CREATE INDEX ix_library_item_root       ON library_item (root_folder_id)
                                        WHERE root_folder_id IS NOT NULL;

CREATE TABLE library_monitor (
    library_item_id BIGINT NOT NULL REFERENCES library_item(id) ON DELETE CASCADE,
    dimension_id    TEXT   NOT NULL,     -- MonitorDimension.DimensionId
    choice          TEXT   NOT NULL,     -- FacetValue.Value, or 'true'/'false' for Toggle
    PRIMARY KEY (library_item_id, dimension_id)
);

CREATE TABLE library_tag (
    library_item_id BIGINT NOT NULL REFERENCES library_item(id) ON DELETE CASCADE,
    tag_id          BIGINT NOT NULL REFERENCES tag(id)          ON DELETE CASCADE,
    PRIMARY KEY (library_item_id, tag_id)
);
CREATE INDEX ix_library_tag_tag ON library_tag (tag_id);
```

**Note what the `selected_variant_*` pair buys.** Lidarr and Readarr each enforce "exactly one selected
variant" by hand in a repository (`Ensure.That(all.Count(x => x.Monitored) == 1)`). Here the invariant is
*structural*: one nullable column pair on the parent row. It is not enforced — it is unrepresentable to
violate. That is the clearest single win of moving the shape's declarations into the schema.

`MonitorDimensionKind.Toggle` stores `'true'`/`'false'` as text rather than getting its own boolean
column, because the dimension set is shape-declared and open; two storage shapes for one concept is the
drift #21 rejects elsewhere.

### 4.3 The demoted `Season` — `sequence_policy`

`SequenceAxis.HasPolicyRecord` is the flag that says "a per-`(parent, coordinate)` record exists". Sonarr
ran `Season` as a table for twenty migrations, dropped it in 020/021, and re-embedded it as JSON on
`Series`. Both were wrong: a table because a season is not a level, and JSON because a monitored bit
needs an index. The right answer is a narrow core table keyed by the axis:

```sql
CREATE TABLE sequence_policy (
    id              BIGINT   NOT NULL PRIMARY KEY,
    kind_id         TEXT     NOT NULL,
    parent_level_id TEXT     NOT NULL,
    parent_item_id  BIGINT   NOT NULL,
    axis_id         TEXT     NOT NULL,   -- SequenceAxis.AxisId: 'season', 'disc'
    axis_value      BIGINT   NOT NULL,   -- 0 = Sonarr's specials
    monitored       BOOLEAN  NOT NULL DEFAULT TRUE,
    path_override   TEXT     NULL,
    artwork_url     TEXT     NULL,
    updated_at      TEXT     NOT NULL,
    CONSTRAINT ux_sequence_policy
        UNIQUE (kind_id, parent_level_id, parent_item_id, axis_id, axis_value)
);
```

Media-agnostic: no column says "season". The exceptions (`SequenceException(0, "Specials",
ExcludedFromCompleteness)`) are shape declarations, not rows — completeness reads them from the validated
shape at query time and excludes matching `axis_value`s. No hard-coded `SeasonNumber > 0` survives.

### 4.4 Files, and the join with an anchor

```sql
CREATE TABLE media_file (
    id                BIGINT   NOT NULL PRIMARY KEY,
    -- FileBinding.AnchorLevelId. Lidarr's TrackFile.AlbumId: two levels above the unit it satisfies.
    kind_id           TEXT     NOT NULL,
    anchor_level_id   TEXT     NOT NULL,
    anchor_item_id    BIGINT   NOT NULL,
    path              TEXT     NOT NULL,
    size_bytes        BIGINT   NOT NULL DEFAULT 0,
    modified_at       TEXT     NULL,
    added_at          TEXT     NOT NULL,
    quality_tier_id   TEXT     NULL,
    quality_revision  INTEGER  NOT NULL DEFAULT 1,
    format_family_id  TEXT     NULL,          -- FormatFamily.FamilyId: 'ebook' vs 'audiobook'
    original_path     TEXT     NULL,
    release_group     TEXT     NULL,
    scene_name        TEXT     NULL,
    media_info        TEXT     NULL,          -- JSON
    CONSTRAINT ux_media_file_path UNIQUE (path)
);
CREATE INDEX ix_media_file_anchor ON media_file (kind_id, anchor_level_id, anchor_item_id);

CREATE TABLE media_file_language (
    media_file_id BIGINT NOT NULL REFERENCES media_file(id) ON DELETE CASCADE,
    language_code TEXT   NOT NULL,
    ordinal       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (media_file_id, language_code)
);
```

`ux_media_file_path` is the constraint that makes "is this file already imported?" a single indexed
lookup instead of the surveyed apps' four different per-app queries.

The join is §5.

### 4.5 Grouping axes

```sql
CREATE TABLE media_group (
    id             BIGINT  NOT NULL PRIMARY KEY,
    kind_id        TEXT    NOT NULL,
    axis_id        TEXT    NOT NULL,    -- GroupingAxis.AxisId: 'collection', 'book-series'
    group_item_id  BIGINT  NOT NULL,    -- the plugin's id for the group entity
    monitored      BOOLEAN NOT NULL DEFAULT FALSE,   -- GroupingAxis.IsMonitorable
    quality_profile_id BIGINT NULL REFERENCES quality_profile(id) ON DELETE SET NULL,
    root_folder_id     BIGINT NULL REFERENCES root_folder(id)     ON DELETE SET NULL,
    added_at       TEXT    NOT NULL,
    CONSTRAINT ux_media_group UNIQUE (kind_id, axis_id, group_item_id)
);

CREATE TABLE group_membership (
    media_group_id  BIGINT  NOT NULL REFERENCES media_group(id) ON DELETE CASCADE,
    member_level_id TEXT    NOT NULL,
    member_item_id  BIGINT  NOT NULL,
    position        TEXT    NULL,       -- MemberPosition.Labeled: "2.5", "1-3", ""
    sort_index      BIGINT  NOT NULL DEFAULT 0,
    is_primary      BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (media_group_id, member_level_id, member_item_id)
);
CREATE INDEX ix_group_membership_member ON group_membership (member_level_id, member_item_id);
CREATE UNIQUE INDEX ux_group_membership_primary
    ON group_membership (member_level_id, member_item_id) WHERE is_primary;
```

`position TEXT` is not laziness: `SeriesBookLink.Position` is genuinely a string in Readarr, carrying
`"2.5"`, `"1-3"` and `""`. `sort_index` is the orderable companion, supplied by the plugin — the same
split as `Coordinate.Label` + `ItemView.SortIndex`.

`ux_group_membership_primary` is a partial unique index enforcing `HasPrimaryMember` — one member per
group may be primary. Readarr enforces this nowhere.

`GroupLifetime.RefCounted` (Readarr's book series, which dies with its last member and needs an orphan
sweeper) is enforced by `ON DELETE CASCADE` plus a `DELETE FROM media_group WHERE NOT EXISTS (SELECT 1
FROM group_membership …)` step in the same transaction, only for axes the shape declares `RefCounted`.
Readarr's orphan sweeper becomes a transaction, not a job.

### 4.6 Activity — one `history` for four

```sql
CREATE TABLE history (
    id             BIGINT   NOT NULL PRIMARY KEY,
    kind_id        TEXT     NOT NULL,
    level_id       TEXT     NOT NULL,    -- the target unit's level
    item_id        BIGINT   NOT NULL,
    event_kind     SMALLINT NOT NULL,
    source_title   TEXT     NULL,
    download_id    TEXT     NULL,
    indexer_id     BIGINT   NULL REFERENCES provider_definition(id) ON DELETE SET NULL,
    quality_tier_id  TEXT   NULL,
    quality_revision INTEGER NOT NULL DEFAULT 1,
    languages      TEXT     NULL,        -- JSON array
    data           TEXT     NULL,        -- JSON bag; the surveyed Dictionary<string,string> Data
    occurred_at    TEXT     NOT NULL
);
CREATE INDEX ix_history_item      ON history (kind_id, level_id, item_id, occurred_at DESC);
CREATE INDEX ix_history_download  ON history (download_id) WHERE download_id IS NOT NULL;
CREATE INDEX ix_history_time      ON history (occurred_at DESC);

-- One row per ancestor of the target, at the level chain's depth, captured at write time.
CREATE TABLE history_scope (
    history_id BIGINT NOT NULL REFERENCES history(id) ON DELETE CASCADE,
    level_id   TEXT   NOT NULL,
    item_id    BIGINT NOT NULL,
    PRIMARY KEY (history_id, level_id, item_id)
);
CREATE INDEX ix_history_scope_item ON history_scope (level_id, item_id);
```

The four surveyed `History` models are *identical* except for their FK columns:

| App | FK columns | This schema |
|---|---|---|
| Sonarr `EpisodeHistory.cs:24-25` | `EpisodeId`, `SeriesId` | target `episode`; scope `series` |
| Radarr `History.cs:24` | `MovieId` | target `movie`; scope `movie` |
| Lidarr `EntityHistory.cs:22-24` | `TrackId`, `AlbumId`, `ArtistId` | target `track`; scope `album`, `artist` |
| Readarr `EntityHistory.cs:22-23` | `BookId`, `AuthorId` | target `book`; scope `author` |

Lidarr is the case that forces `history_scope` to be a table rather than one denormalized
`library_item_id` column: "history for this album" is a real, common query and the album is neither the
target nor the library entry. `blocklist_scope` and `download_target` follow the same shape. Three small
child tables rather than one polymorphic `activity_scope`, because three real foreign keys with
`ON DELETE CASCADE` beat one table that cannot have any.

### 4.7 Providers, unified

```sql
CREATE TABLE provider_definition (
    id                 BIGINT   NOT NULL PRIMARY KEY,
    family             SMALLINT NOT NULL,   -- Indexer|DownloadClient|ImportList|Notification|Metadata
    plugin_id          TEXT     NULL,       -- NULL for host-supplied providers
    implementation     TEXT     NOT NULL,
    name               TEXT     NOT NULL,
    enabled            BOOLEAN  NOT NULL DEFAULT TRUE,
    priority           INTEGER  NOT NULL DEFAULT 25,
    settings           TEXT     NOT NULL,   -- JSON, schema from the declarative SettingsField set
    config_contract    TEXT     NOT NULL,
    created_at         TEXT     NOT NULL,
    CONSTRAINT ux_provider_definition_name UNIQUE (family, name)
);
CREATE INDEX ix_provider_definition_plugin ON provider_definition (plugin_id)
                                           WHERE plugin_id IS NOT NULL;

CREATE TABLE provider_status (
    provider_definition_id  BIGINT   NOT NULL PRIMARY KEY
                            REFERENCES provider_definition(id) ON DELETE CASCADE,
    initial_failure_at      TEXT     NULL,
    most_recent_failure_at  TEXT     NULL,
    escalation_level        INTEGER  NOT NULL DEFAULT 0,
    disabled_till           TEXT     NULL,
    last_error              TEXT     NULL
);
```

`ON DELETE CASCADE` from `provider_definition` to `provider_status` is the whole of what
`CleanupOrphanedIndexerStatus`, `CleanupOrphanedDownloadClientStatus`,
`CleanupOrphanedImportListStatus` and `CleanupOrphanedNotificationStatus` do — four housekeeping classes
replaced by one constraint, once `PRAGMA foreign_keys` is on (#18). `provider_status` being one table
replaces five identical ones and their five `FixFuture*StatusTimes` twins.

**Provider rows survive their plugin's absence** (#5): `plugin_id` is a plain `TEXT`, not a foreign key,
precisely so that uninstalling a plugin does not cascade its users' indexer configuration away. This is
the deliberate inversion of Prowlarr's `RemoveMissingImplementations`.

### 4.8 Logs, capped

```sql
CREATE TABLE log (
    id         BIGINT   NOT NULL PRIMARY KEY,
    logged_at  TEXT     NOT NULL,
    level      SMALLINT NOT NULL,
    logger     TEXT     NOT NULL,
    message    TEXT     NOT NULL,
    exception  TEXT     NULL,
    correlation_id TEXT NULL
);
CREATE INDEX ix_log_time ON log (logged_at DESC);
```

One table, one database (#16). A `TrimLog` job enforces `Arronix:Storage:LogRetention` (default: 7 days
**and** 100 000 rows, whichever bites first) and is the only writer of `DELETE` here. NLog file targets
remain the primary log sink; this table exists solely to power the UI's log grid, which is the only thing
the surveyed `Logs` table ever fed.

---

## 5. Coordinates and bindings — the physical layer

This is the section where the shape model becomes storage, and the one the spec's §4.6 remark is aimed
at: *"the eventual relational schema cannot be biased toward any one `*arr`'s FK direction"*.

### 5.1 The four surveyed FK directions, and why none of them is the schema

| App | Unit → file | File → unit/ancestor | Cardinality |
|---|---|---|---|
| Sonarr | `Episode.EpisodeFileId` (`Tv/Episode.cs:20`) | `EpisodeFile.SeriesId`, `.SeasonNumber` (`MediaFiles/EpisodeFile.cs:15-16`) | one file per episode; **many episodes per file** |
| Radarr | `Movie.MovieFileId` (`Movies/Movie.cs:36`) | `MovieFile.MovieId` (`MediaFiles/MovieFile.cs:15`) | 1:1, redundantly both ways |
| Lidarr | `Track.TrackFileId` (`Music/Model/Track.cs:32`) | `TrackFile.AlbumId` (`MediaFiles/TrackFile.cs:24`) — **two levels above the unit** | one file per track; many tracks per file |
| Readarr | *(none)* | `BookFile.EditionId` + `.Part` + `.PartCount` (`MediaFiles/BookFile.cs:23,25,32`) | **many files per edition**, one edition per file, ordinal meaningful |

Four apps, four different answers, and Radarr's is two answers at once. The join
`(unit, file, ordinal?)` is the only representation that is a projection of all four rather than a
generalization of one.

### 5.2 The join table

```sql
CREATE TABLE unit_file_link (
    id            BIGINT   NOT NULL PRIMARY KEY,
    kind_id       TEXT     NOT NULL,
    unit_level_id TEXT     NOT NULL,      -- FileBinding.UnitLevelId
    unit_item_id  BIGINT   NOT NULL,
    media_file_id BIGINT   NOT NULL REFERENCES media_file(id) ON DELETE CASCADE,
    -- FileBinding.OrdinalIsMeaningful. Readarr's BookFile.Part.
    ordinal       INTEGER  NULL,
    -- Denormalized SpanConstraint key: the MustNotSpan component values for this unit,
    -- joined with 0x1F. NULL when the shape declares no span constraints. See §5.5.
    span_key      TEXT     NULL,
    created_at    TEXT     NOT NULL,
    CONSTRAINT ux_unit_file_link
        UNIQUE (kind_id, unit_level_id, unit_item_id, media_file_id)
);
CREATE INDEX ix_unit_file_link_file ON unit_file_link (media_file_id);
CREATE INDEX ix_unit_file_link_unit ON unit_file_link (kind_id, unit_level_id, unit_item_id);
```

`ON DELETE CASCADE` on `media_file_id` means deleting a file removes its links atomically — which is
`CleanupOrphanedEpisodeFiles` / `CleanupOrphanedSubtitleFiles` / `CleanupOrphanedExtraFiles`, gone.

There is no FK to the unit, because the unit is a plugin-owned catalog row and cross-owner FKs are
forbidden (#17). Orphan links whose unit no longer exists are found by the integrity check in §8.3, not
by the engine — this is the one place the FK-everywhere story genuinely does not reach, and it is
recorded rather than glossed.

### 5.3 The cardinality booleans **are** the indexes

`AtMostOneFilePerUnit` and `AtMostOneUnitPerFile` are not schema switches — they are partial unique
indexes, created per kind at activation:

```sql
-- FileBinding.AtMostOneFilePerUnit == true, for kind 'tv'
CREATE UNIQUE INDEX ux_ufl_one_file_per_unit__tv
    ON unit_file_link (unit_level_id, unit_item_id)
    WHERE kind_id = 'tv';

-- FileBinding.AtMostOneUnitPerFile == true, for kind 'movies'
CREATE UNIQUE INDEX ux_ufl_one_unit_per_file__movies
    ON unit_file_link (media_file_id)
    WHERE kind_id = 'movies';
```

Projected over the acceptance table (`unified-host-runtime.md` §2.12):

| Kind | `AtMostOneFilePerUnit` | `AtMostOneUnitPerFile` | Indexes created |
|---|---|---|---|
| TV | true | false | `ux_ufl_one_file_per_unit__tv` |
| Movies | true | true | both |
| Music | true | false | `ux_ufl_one_file_per_unit__music` |
| Books | false | true | `ux_ufl_one_unit_per_file__books` |

Four cardinalities, one table, zero schema variation. And N:M — both false — is representable, which the
spec explicitly wants (*"N:M not structurally forbidden"*), because it simply creates neither index.

Partial (filtered) unique indexes are supported by SQLite ≥ 3.8.0 and Postgres ≥ 7.2, so this is one
statement on both engines.

### 5.4 The activation-time index reconciler

```csharp
// src/Arronix.Host/Storage/Schema/ShapeIndexReconciler.cs
/// <remarks>
/// The indexes in §5.3 depend on WHICH KINDS ARE INSTALLED, which is not known at migration time — a
/// core migration cannot create them and a plugin does not own the table they sit on. So they are
/// reconciled at activation from the validated shape, and ledgered like everything else. If a kind's
/// FileBinding tightens between releases (false → true) and the existing data violates the new
/// constraint, CREATE UNIQUE INDEX fails; the kind is quarantined with the offending rows named. That
/// is the correct outcome: a shape change that invalidates stored data must be visible.
/// </remarks>
internal interface IShapeIndexReconciler
{
    Task ReconcileAsync(ValidatedShape shape, CancellationToken ct);
}
```

Reconcile is: compute the desired index set from the shape, diff against `owned_object` rows with
`owner_id = 'core'` and `object_name LIKE 'ux_ufl_%\_\_' || kindId`, create what is missing, drop what is
no longer wanted, update the ledger. Dropping a kind's indexes is part of purge.

`OrdinalIsMeaningful = false` cannot be expressed as an index — a unique index cannot say "this column
must be NULL", and SQLite cannot add a `CHECK` to an existing table. It is enforced in
`RelationalMediaStore.LinkAsync` and audited by §8.3 check 4. **This is a wart** (#14) and it is the only
shape invariant in this design that the database does not itself hold.

### 5.5 Span constraints

`SpanConstraint("aired", "season", MustNotSpan)` says a file may span episodes but never seasons —
Sonarr's `InvalidSeasonException`, declared instead of thrown.

Physically: on `LinkAsync`, the store computes `span_key` for the unit by reading the constrained
components from `library_coordinate` (§5.6) and joining them with `0x1F`. For TV's constraint that is
just the season number, so a season-4 episode gets `span_key = "4"`. The guard, inside the write
transaction:

```sql
SELECT 1
  FROM unit_file_link
 WHERE media_file_id = @fileId
   AND span_key IS NOT NULL
   AND span_key <> @spanKey
 LIMIT 1;
```

A hit rejects the link with `SpanConstraintViolated` naming the space, the component and both values.
Postgres additionally takes `SELECT … FOR UPDATE` on the file row; SQLite serializes writers already.

And, decisively, the *existing-violation* query is one portable statement — something Sonarr cannot do at
all, because the constraint lives only in a thrown exception:

```sql
SELECT media_file_id
  FROM unit_file_link
 WHERE span_key IS NOT NULL
 GROUP BY media_file_id
HAVING COUNT(DISTINCT span_key) > 1;
```

### 5.6 The coordinate cache

```sql
CREATE TABLE library_coordinate (
    kind_id     TEXT     NOT NULL,
    level_id    TEXT     NOT NULL,
    item_id     BIGINT   NOT NULL,
    space_id    TEXT     NOT NULL,     -- CoordinateSpace.SpaceId: 'aired', 'absolute', 'scene', …
    coord_kind  SMALLINT NOT NULL,     -- CoordinateKind
    confidence  SMALLINT NOT NULL,     -- CoordinateConfidence
    -- CoordinateKind.Ordinal. OrdinalPath is capped at four components (Abstractions §2.2).
    ord_0       BIGINT   NULL,
    ord_1       BIGINT   NULL,
    ord_2       BIGINT   NULL,
    ord_3       BIGINT   NULL,
    ord_length  SMALLINT NOT NULL DEFAULT 0,
    -- CoordinateKind.Date
    date_value  TEXT     NULL,
    -- CoordinateKind.Label. Equatable, not orderable: "A1", "2.5", "1-3", "".
    label_value TEXT     NULL,
    sort_index  BIGINT   NOT NULL DEFAULT 0,   -- ItemView.SortIndex, for Label spaces
    refreshed_at TEXT    NOT NULL,
    PRIMARY KEY (kind_id, level_id, item_id, space_id)
);
CREATE INDEX ix_library_coordinate_ordinal
    ON library_coordinate (kind_id, level_id, space_id, ord_0, ord_1, ord_2, ord_3);
CREATE INDEX ix_library_coordinate_date
    ON library_coordinate (kind_id, level_id, space_id, date_value) WHERE date_value IS NOT NULL;
```

**Four columns, not a packed key** (#11). `OrdinalPath` holds at most four `long`s, so the natural
relational encoding is available, each component is independently indexable and filterable ("everything
in season 3" is `ord_0 = 3`), and a human reading the table can see what it says. A packed sortable byte
key would be none of those things.

`ord_length` preserves `OrdinalPath.Length` exactly, so `Of(3)` and `Of(3, 0)` round-trip differently —
which matters because `OrdinalPath.CompareTo` is documented as *shorter-is-lesser on a shared prefix*.

**The `NULLS FIRST` trap.** To reproduce shorter-is-lesser in SQL, unused components must sort before
populated ones. SQLite sorts `NULL` first by default; **Postgres sorts `NULL` last in `ASC`**. Every
`ORDER BY` over these columns must therefore write the direction explicitly:

```sql
ORDER BY ord_0 ASC NULLS FIRST, ord_1 ASC NULLS FIRST,
         ord_2 ASC NULLS FIRST, ord_3 ASC NULLS FIRST
```

`NULLS FIRST` / `NULLS LAST` has been valid SQLite since 3.30 (2019) and Postgres since 8.3, so one SQL
text serves both — but omitting it produces a query that is correct on SQLite and silently wrong on
Postgres, which is exactly the class of bug a SQLite-only test suite ships. A governance test asserts no
`ORDER BY` over `ord_*` in the store's SQL lacks an explicit null ordering.

**This table is a cache and is treated as one** (#10). The catalog belongs to `IMediaItemSource`. The
cache exists because `LinkAsync` must enforce span constraints at write time, and because completeness
queries must not fan out to the plugin per row. It carries `refreshed_at`, it is populated as a
side-effect of `IMediaItemSource` reads, it is rebuildable in full by a `RebuildCoordinateCache` job, and
**it is excluded from backup verification** — a restored database with an empty `library_coordinate`
is valid and repopulates.

Rows are kept only for units that have library state, a file link, or a `sequence_policy` ancestor. That
bounds the table by "what the user owns" rather than "the whole catalog", which is the difference
between thousands and millions of rows.

### 5.7 Completeness, in SQL

`CompletenessCalculator` (spec §4.6) becomes one query per library item, and every clause traces to a
shape declaration:

```sql
SELECT COUNT(*)                                        AS total,
       COUNT(l.media_file_id)                          AS have
  FROM library_coordinate c
  LEFT JOIN unit_file_link l
         ON l.kind_id = c.kind_id
        AND l.unit_level_id = c.level_id
        AND l.unit_item_id  = c.item_id
 WHERE c.kind_id  = @kind
   AND c.level_id = @completenessLevel          -- MediaLevelRoles.CompletenessUnit
   AND c.space_id = @canonicalSpace             -- CoordinateSpace.IsCanonical
   AND (@variantItemId IS NULL OR c.item_id IN (
         SELECT item_id FROM library_coordinate
          WHERE kind_id = @kind AND level_id = @completenessLevel
            AND item_id = c.item_id))           -- CompletenessIsVariantRelative
   AND c.ord_0 NOT IN (SELECT value FROM @excludedSequenceValues);  -- SequenceException
```

Lidarr's variant-relative counts and Sonarr's specials exclusion both fall out of declarations. No
`ICompletenessPolicy` contract is needed, which is why the spec does not define one.

---

## 6. SQLite and Postgres

### 6.1 Providers and packages

| Concern | Package | Status |
|---|---|---|
| Migrations | `FluentMigrator.Runner`, `.SQLite`, `.Postgres` 8.0.1 | already registered |
| Mapping | `Dapper` 2.1.79 | already registered |
| Postgres | `Npgsql` 10.0.3 | already registered |
| Retry | `Polly` 8.7.0 | already registered |
| SQLite | **`Microsoft.Data.Sqlite`** + a pinned `SQLitePCLRaw.bundle_e_sqlite3` | **new registration requested** |

One package request, justified under the dependency policy (#22). `Microsoft.Data.Sqlite` is
Microsoft-owned, is an ADO.NET provider with no EF Core dependency, and supplies
`SqliteConnection.BackupDatabase` — the online backup API §8.1 needs. Replacing `System.Data.SQLite`
2.0.3 / `SourceGear.sqlite3` also removes the legacy tree's four `No_*` environment-variable hacks
(`DbFactory.cs:37-44`).

**Verified caveat, found while running §0:** the transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.11` that
`Microsoft.Data.Sqlite 10.0.0` pulls in raises **NU1903, high severity**
(`GHSA-2m69-gcr7-jv3q`). `TreatWarningsAsErrors=true` is on for every project in this tree, so this will
**fail the build**. WP-1 must either pin `SQLitePCLRaw.bundle_e_sqlite3` to a patched version or take
`Microsoft.Data.Sqlite.Core` plus an explicitly-versioned bundle. Do not resolve it with `NoWarn`.

### 6.2 The type map

One table, applied by both the core migrations and the declarative translator. Anything not in it is
unreachable from a plugin, by construction (`ColumnType` is closed).

| `ColumnType` | SQLite | Postgres | CLR | Notes |
|---|---|---|---|---|
| `Int32` | `INTEGER` | `integer` | `int` | |
| `Int64` | `INTEGER` | `bigint` | `long` | `.Identity()` on SQLite renders `INTEGER PRIMARY KEY AUTOINCREMENT` even from `AsInt64()` — verified §0.4 |
| `Decimal` | `NUMERIC` | `numeric(19,4)` | `decimal` | never `REAL`; money and sizes are exact |
| `Boolean` | `INTEGER` (0/1) | `boolean` | `bool` | Dapper handles both; never compare to `1` in SQL, always `= @p` |
| `Text` | `TEXT` | `text` | `string` | `MaxLength` renders `varchar(n)` on Postgres, plain `TEXT` on SQLite (which ignores length) |
| `Instant` | `TEXT` ISO-8601 `yyyy-MM-ddTHH:mm:ss.fffffffZ` | `timestamptz` | `DateTimeOffset` | see below |
| `CalendarDate` | `TEXT` `yyyy-MM-dd` | `date` | `DateOnly` | |
| `Json` | `TEXT` | `text` | `string` | **not `jsonb`** (#19) |
| `Binary` | `BLOB` | `bytea` | `byte[]` | |

**`Instant` is the one that bites.** SQLite has no date type; the legacy tree sets
`DateTimeKind = DateTimeKind.Utc` on the connection string (`ConnectionStringFactory.cs:48`) and relies
on the provider to reinterpret. `Microsoft.Data.Sqlite` has no such knob. The rule is therefore explicit
and enforced in code: **every `Instant` is written as UTC ISO-8601 with a `Z` and read back as
`DateTimeOffset`**, via a single pair of Dapper `TypeHandler`s registered once. `TimeProvider` is
injected everywhere; `DateTime.UtcNow` appears nowhere in the store (house rule). The lexicographic sort
order of that format matches chronological order, so `ORDER BY occurred_at DESC` is correct on SQLite
without a conversion.

FluentMigrator's own version-table columns are outside this map — the §0.4 dump shows `applied_on`
rendered as SQLite `DATETIME`. That is fine and is not touched; only Arronix-owned columns follow the
table.

### 6.3 Identifiers, case and quoting

Postgres folds unquoted identifiers to lowercase; SQLite preserves them. The legacy tree sidesteps this
by quoting everything (`TableMapper.cs:52`), and FluentMigrator quotes everything it generates (§0.4
dump). The rule here is stronger and removes the question entirely: **every Arronix identifier is
lowercase `snake_case`, and every hand-written SQL string quotes it**. A governance test greps the
store's SQL for an unquoted mixed-case identifier.

String comparison is the other half. SQLite's `NOCASE` collation and Postgres's `citext` are not the same
thing and neither handles Unicode the way the other does. The design does not use either: wherever
case-insensitive matching is needed, a normalized companion column is stored
(`clean_title`, `sort_title`, `normalized_path`), computed in C# by `Arronix.Common`'s existing text
folding. That is what the surveyed apps do anyway (`UpdateCleanTitleForSeries` is a housekeeper because
the column exists), and it makes matching behavior identical on both engines and testable without a
database.

### 6.4 SQL that is genuinely one text on both engines

* **Upsert.** `INSERT INTO … VALUES … ON CONFLICT (cols) DO UPDATE SET …` — SQLite ≥ 3.24, Postgres
  ≥ 9.5. Replaces `BasicRepository.Upsert`'s `if (model.Id == 0) Insert else Update` (`BasicRepository.cs:312`).
* **Returning the identity.** `INSERT … RETURNING id` — SQLite ≥ 3.35, Postgres ≥ 8.2. Replaces the
  legacy branch between `last_insert_rowid()` and Postgres's `RETURNING`.
* **Partial indexes** (§5.3). SQLite ≥ 3.8.0, Postgres ≥ 7.2.
* **Explicit null ordering** (§5.6). SQLite ≥ 3.30, Postgres ≥ 8.3.
* **CTEs and window functions**, for paging counts. SQLite ≥ 3.25, Postgres ≥ 8.4.

`SourceGear.sqlite3` 3.53.3 and `Npgsql` 10 clear all of these comfortably. Minimum supported versions
are asserted at startup: SQLite ≥ 3.35, Postgres ≥ 13. The legacy tree already logs the Postgres version
in migration 000 (`000_database_engine_version_check.cs`) — this promotes that log line to a check.

### 6.5 Connections and transactions

```csharp
// src/Arronix.Host/Storage/IArronixDatabase.cs   (Tier B, host-side)
internal interface IArronixDatabase
{
    StorageEngine Engine { get; }
    ValueTask<DbConnection> OpenAsync(CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<DbConnection, DbTransaction, Task<T>> work, CancellationToken ct);
    Task InTransactionAsync(Func<DbConnection, DbTransaction, Task> work, CancellationToken ct);
}
```

* **Explicit transactions only.** No `TransactionScope`, no ambient enlistment. The legacy Postgres
  connection string sets `Enlist = false` (`ConnectionStringFactory.cs:72`) — that is a scar, and the
  design starts where the scar ended.
* **Pooling** is the provider's, on both engines. No hand-rolled connection cache.
* **SQLite pragmas, set on every opened connection**, in this order:

  ```sql
  PRAGMA journal_mode = WAL;      -- Truncate on macOS, per the legacy OsInfo.IsOsx branch
  PRAGMA foreign_keys = ON;       -- OFF by default; see #18
  PRAGMA busy_timeout = 10000;
  PRAGMA synchronous = NORMAL;    -- safe under WAL
  PRAGMA cache_size = -20000;     -- 20 MB, matching the legacy setting
  ```

  `PRAGMA foreign_keys` is a **per-connection** setting in SQLite, not a database property. Forgetting it
  on one connection silently disables every `ON DELETE CASCADE` in §4 for that connection's writes. It
  goes in one place — the connection factory — and a test asserts it is on for a connection obtained
  through the factory.
* **Writer contention.** SQLite permits one writer. Keep the legacy Polly retry on `SQLITE_BUSY`
  (`BasicRepository.cs:54-69`) — three attempts, exponential, jittered — and keep write transactions
  short. Postgres needs none of this.
* **Isolation.** Default on both (`SERIALIZABLE` in SQLite's single-writer sense; `READ COMMITTED` on
  Postgres). The two places that need more — `LinkAsync`'s span guard and `UpsertLibraryAsync`'s variant
  swap — take `SELECT … FOR UPDATE` on Postgres, which is a no-op'd extension point on SQLite.
* **Concurrent hosts.** Two host processes against one Postgres database is possible; §2.5's
  `pg_advisory_lock` serializes their migrations. It is not otherwise supported and a health warning says
  so.

---

## 7. The repository seam

### 7.1 What does not change

`IMediaStore` (`unified-host-runtime.md` §4.6) is reproduced **byte for byte**, including `LibraryFacet`,
`UnitFileLink`, `GroupMembership` and `MediaFileRecord`. Not one signature moves. That is the entire
point of the spec having fixed the seam a milestone early, and this document's job is to prove the bet
paid: every method on it maps to §4/§5 without an adapter.

| `IMediaStore` member | Implementation |
|---|---|
| `FindLibraryAsync` | one `SELECT` on `library_item` + two child selects, or a single 3-way `LEFT JOIN` |
| `FindLibraryManyAsync` | the same with `WHERE (kind_id, level_id, item_id) IN …`, chunked at 900 (SQLite's default `SQLITE_MAX_VARIABLE_NUMBER` is 999) |
| `UpsertLibraryAsync` | `INSERT … ON CONFLICT (kind_id, level_id, item_id) DO UPDATE`, plus monitor/tag diffs, in one transaction |
| `UpsertFileAsync` | `INSERT … ON CONFLICT (path) DO UPDATE … RETURNING id` |
| `LinkAsync` | cardinality pre-check → span guard (§5.5) → `INSERT`; the partial unique indexes are the real gate |
| `UnlinkAsync` | `DELETE FROM unit_file_link WHERE …` |
| `LinksForUnitAsync` / `LinksForFileAsync` | `ix_unit_file_link_unit` / `ix_unit_file_link_file` |
| `SetGroupMembershipAsync` / `MembersOfAsync` | `group_membership` upsert / select |

### 7.2 The swap

```csharp
// src/Arronix.Host/Composition/StorageRegistration.cs
internal static class StorageRegistration
{
    public static IServiceCollection AddArronixStorage(this IServiceCollection services)
    {
        services.AddValidatedOptions<StorageOptions>(StorageOptions.SectionName);
        services.TryAddSingleton<IArronixDatabase, ArronixDatabase>();
        services.TryAddSingleton<ISchemaMigrationService, SchemaMigrationService>();
        services.TryAddSingleton<IShapeIndexReconciler, ShapeIndexReconciler>();

        // The one line the whole milestone is about.
        services.TryAddSingleton<IMediaStore, RelationalMediaStore>();
        return services;
    }
}
```

`InMemoryMediaStore` is **kept**, not deleted, as the test double and as the `Arronix:Storage:Provider =
memory` mode for integration tests. Everything in `Arronix.Host/Storage/` except `IMediaStore` and its
records stays `internal`, exactly as the spec specifies, so this really is a one-line change and no
caller compiles differently.

### 7.3 What the store is made of

```
src/Arronix.Host/Storage/
├── IMediaStore.cs                      (unchanged from the milestone)
├── InMemoryMediaStore.cs               (unchanged; the test double)
├── RelationalMediaStore.cs             (new)
├── IArronixDatabase.cs / ArronixDatabase.cs
├── StorageOptions.cs
├── Dialect/
│   ├── ISqlDialect.cs                  ── 6 members: Quote, Upsert, Returning, ForUpdate,
│   ├── SqliteDialect.cs                    NullOrdering, LimitOffset
│   └── PostgresDialect.cs
├── Mapping/
│   ├── DapperTypeHandlers.cs           ── DateTimeOffset, DateOnly, MediaItemRef, OrdinalPath
│   └── Sql.cs                          ── every query as a named const string
├── Migrations/
│   ├── OwnerVersionTable.cs
│   ├── SchemaMigrationService.cs
│   ├── CoreMigrationRunner.cs
│   ├── DeclaredSchemaMigration.cs
│   ├── DeclaredSchemaLoader.cs
│   └── PluginSchemaValidator.cs
├── Schema/
│   ├── M0001_Baseline.cs               ── the ENTIRE core schema, one file (§9.1)
│   └── ShapeIndexReconciler.cs
├── Ledger/  ── SchemaOwnerRepository, OwnedObjectRepository, ObjectInventory
├── Backup/  ── BackupService, SqliteOnlineBackup, BackupManifest
└── Integrity/ ── IntegrityCheckService + one class per check (§8.3)
```

Six members on `ISqlDialect`, against the legacy tree's `SqlBuilder` + `WhereBuilder` +
`WhereBuilderSqlite` + `WhereBuilderPostgres` + `ExpressionVisitor` (#20). Every query is a
`const string` in `Sql.cs` with the dialect's differences interpolated from those six members — greppable,
reviewable, and `EXPLAIN`-able by pasting it into a shell.

### 7.4 Deliberately absent

* **No `ModelBase`.** Store records carry their own keys; `LibraryFacet` has no `int Id` and does not want
  one.
* **No `TableMapping` / `TableMapper`.** A mutable static dictionary initialized from a static constructor
  (`DbFactory.cs:30-35`) is a global whose initialization order is a startup-crash source and which a
  plugin model makes worse, not better.
* **No `LazyLoaded<T>`.** A lazy-loading proxy inside a DTO is why Readarr's `SeriesBookLink` needs
  `[MemberwiseEqualityIgnore]` on two properties and why list screens N+1. Joins are explicit.
* **No `IEmbeddedDocument` / `EmbeddedDocumentConverter`.** JSON columns are `string` in the record and
  are deserialized at the boundary, not by a Dapper type handler that hides an allocation per row.
* **No repository base class.** Nine surveyed repositories inherit `BasicRepository<T>` and override
  `Builder()` to bolt on joins; the base class's `All()`, `Purge()`, `HasItems()` and paging are dead
  weight on a store with eight methods.

### 7.5 Plugin-owned data, and why no plugin uses it yet

The plugin-side seam that reads and writes plugin tables (`IPluginDataStore`) is **deferred**, with the
trigger recorded in §12. This milestone builds the migration and ownership machinery — which is exactly
what §2 designs — but no plugin persists anything, because `unified-host-runtime.md` #10 makes plugins
stateless projections and that is still the right way to learn the store's shape before freezing it.

Building the machinery now anyway is not speculative: the *hazard* is structural and the cost of
discovering it after four plugins have shipped migrations is a data-loss bug in the field. The contract
surface it adds is `IPluginSchemaProvider` + eight records, all `[Experimental]`, all behind
`Capability.Persistence`, and none of it is reachable without that capability.

### 7.6 The additive contract change

| Change | Where |
|---|---|
| New area `ARX0018` **Schema** → `Arronix.Abstractions.Schema` | `Experimental/ExperimentalContracts.cs`, `docs/contracts/stability.md` diagnostic table |
| `IPluginSchemaProvider` + 8 records/enums, all `[Experimental(ExperimentalContracts.Schema)]` | `src/Arronix.Abstractions/Schema/` |
| `Capability.Persistence` enum member; no implication (it implies neither `storage` nor `network`) | `Arronix.Abstractions` capability enum |
| `IPluginRegistry.AddSchemaProvider(IPluginSchemaProvider)`, gated on `Capability.Persistence` | `Arronix.Abstractions` |
| One `expectedByNamespace` row in `ExperimentalSurfaceTests`; one `CapabilityMatrix` row | tests |

Abstractions goes 0.3.0 → **0.4.0**, additive. No stable contract is touched. `Capability.Persistence`
implies nothing on purpose: a plugin that wants to persist rows is not thereby asking to read the user's
filesystem, and #15's reasoning ("silently granting `storage` widens least privilege for ergonomics")
applies unchanged.

---

## 8. Backup, restore, integrity

### 8.1 Backup

A backup is a zip containing `arronix.db` (or nothing, on Postgres), `config.xml`, and
**`manifest.json`** — which is the part the surveyed apps do not have and cannot work without under a
plugin model.

```json
{
  "formatVersion": 1,
  "createdAt": "2026-08-10T18:22:31.4471180Z",
  "hostVersion": "0.4.0",
  "engine": "sqlite",
  "owners": [
    { "ownerId": "core",      "appliedVersion": 1,  "tables": 41 },
    { "ownerId": "p_tv",      "appliedVersion": 3,  "tables": 3, "pluginId": "arronix.tv",
      "pluginVersion": "1.2.0" },
    { "ownerId": "p_books",   "appliedVersion": 2,  "tables": 6, "pluginId": "arronix.books",
      "pluginVersion": "0.9.1" }
  ]
}
```

**SQLite:** `SqliteConnection.BackupDatabase(target)` — the online backup API, taken against a live
database with no downtime, as `MakeDatabaseBackup.cs:36-45` already does. The target connection is opened
with `journal_mode = DELETE` so the copy is a single self-contained file with no `-wal`/`-shm` siblings;
the legacy code uses `Truncate` for the same reason and its comment (*"WAL has issues when page sizes
change"*) is worth preserving verbatim in the new code.

**Postgres: not supported, and said so** (#24). Sonarr's `BackupService.BackupDatabase()` is:

```csharp
private void BackupDatabase()
{
    if (_maindDb.DatabaseType == DatabaseType.SQLite) { … }
}
```

— a silent no-op that produces a zip containing `config.xml` and nothing else, which a user discovers
during a restore. Arronix instead: the backup still runs (config and manifest are worth having), the zip
contains a `POSTGRES-README.txt`, the API response carries `databaseIncluded: false`, and a **standing
health warning** tells the operator that database backup is their responsibility on this engine. Shelling
out to `pg_dump` from a container that may not contain it is a worse failure than a documented boundary.

Backups run under the existing scheduler (`daily 03:00` by default, per the §4.7 grammar), retain the
last N, and refuse to run when the target folder is not writable.

### 8.2 Restore

Restore is file-swap-then-migrate, the shape `DatabaseRestorationService` already has: the uploaded
database is written to `arronix.db.restore`, the process restarts, and on startup — *before* any
connection is opened — the restore file replaces `arronix.db` along with its `-wal`/`-shm`/`-journal`
siblings. Migration then runs normally.

Three checks the legacy code does not do, all reading `manifest.json`:

1. **Owner ahead of installed plugin.** A backup whose `p_books` is at version 5 restored into a host
   where the Books plugin declares 3 steps → the plugin is quarantined with `PluginSchemaAhead` after
   restore (§2.6). The restore itself is allowed; refusing it would be worse, because the data is the
   thing being rescued.
2. **Owner with no installed plugin.** Its tables are retained, `state = Absent`, one health warning
   naming the plugin and offering Purge.
3. **Format version.** `formatVersion > 1` → refuse, naming the host version that wrote it.

`library_coordinate` is truncated on restore and rebuilt (§5.6) — it is a cache, and a stale cache
restored alongside a plugin whose catalog has moved on is worse than an empty one.

### 8.3 Integrity checking

One `IntegrityCheckService`, run on demand and nightly, contributing to `IHealthRegistry` (spec §4.9).
Engine-native checks first, then eight portable ones.

**Engine-native.** SQLite: `PRAGMA integrity_check` and `PRAGMA foreign_key_check`. The second is worth
stating a reason for, because Arronix enforces foreign keys on every connection it opens (resolution #18
and §6.5 put `PRAGMA foreign_keys = ON` in the connection factory, with a test asserting it), so no
violation can be created *through Arronix*. It runs because the database file is not only reached through
Arronix: a `sqlite3` session, a backup tool, a restored partial copy, or a filesystem-level corruption can
all leave rows that no `INSERT` of ours would have permitted, and this pragma is the only thing that finds
them. Postgres: none by default; `amcheck` is an extension and is used only when present.

**Portable checks**, all one statement each:

| # | Check | Query shape |
|---|---|---|
| 1 | Object ledger reconciliation | physical inventory ∆ `owned_object` ∪ core set (§3.4) |
| 2 | Version consistency | `schema_owner.applied_version` = `MAX(version)` in each version table = `MAX` in `schema_migration_audit` |
| 3 | Released step edited | `schema_migration_audit.content_hash` ≠ the hash of the currently declared step |
| 4 | Ordinal where the shape forbids it | `ordinal IS NOT NULL` for a kind whose `OrdinalIsMeaningful` is false (the §5.4 wart's audit) |
| 5 | Span violations | the `HAVING COUNT(DISTINCT span_key) > 1` query in §5.5 |
| 6 | Cardinality violations | the two `GROUP BY … HAVING COUNT(*) > 1` forms, in case an index was dropped out of band |
| 7 | Selected variant is not a variant level | `library_item.selected_variant_level_id` not carrying `MediaLevelRoles.VariantAxis` in the validated shape |
| 8 | Dangling unit references | `unit_file_link` / `library_item` rows whose `(kind, level, item)` `IMediaItemSource` no longer resolves — the one place §5.2 cannot use a foreign key |
| 9 | Identifier length | any physical identifier > 63 bytes UTF-8 (§3.3) |

Checks 1–3, 7 and 9 are **errors**: they mean the schema is not what the host thinks it is. Checks 4–6
and 8 are **warnings** with a repair action, because they are data conditions a user can act on.

Check 8 is also the replacement for the surveyed `CleanupOrphaned*` housekeepers that survive #18 — the
ones that clean up references to *plugin*-owned rows, which no foreign key can reach. There are two of
them under this schema, against fourteen in `src/NzbDrone.Core/Housekeeping/Housekeepers/`.

`VACUUM` is a separate, explicit, user-triggered maintenance action, not part of the integrity check, and
never automatic — the legacy `Database.Vacuum()` swallows its own exception (`Database.cs:82`) and runs
where a user cannot see it fail.

---

## 9. "Schema starts at v1", operationally

### 9.1 What v1 is

**One core migration, numbered 1, containing the entire schema of §4.** Not a replay of Sonarr's 223
migrations, not a squash of them, not a translation. There is no upgrade path from any `*arr` install, so
there is nothing to replay and the baseline is written as if the product had always looked like this.

Concretely: `src/Arronix.Host/Storage/Schema/M0001_Baseline.cs`, one file, ~41 tables, `[Migration(1)]`.
It is large and that is correct — a baseline should be readable as a schema, not reconstructed from a
history of edits nobody made.

Each plugin's baseline is likewise its `SchemaStep { Version = 1 }`.

### 9.2 Numbering

* Per owner, `int`, starting at 1, **contiguous** (the validator rejects gaps — see §3.2; core migrations
  are held to the same rule by a governance test).
* No date-based or timestamp-based numbers. The surveyed apps' `NNN_snake_case_description.cs` convention
  is kept for core migrations because it sorts, reads and reviews well.
* `ValidateVersionOrder()` is called before every `MigrateUp()` (§2.3). It catches the specific bug of
  merging a branch that adds version 5 after 6 has already been applied — which, in a repository with
  multiple plugins under parallel development, is a matter of time.
* `RunnerOptions.AllowBreakingChange = false`.

### 9.3 Immutability

**A released migration is never edited.** For core migrations that is a review rule and a governance test
comparing the file set against a checked-in `migrations.lock` of `(version, sha256)` pairs. For plugin
schemas it is *mechanical*: `SchemaStep` serializes canonically, so `schema_migration_audit.content_hash`
detects an edited step at the next startup and quarantines the plugin (`SchemaStepEdited`). The surveyed
apps cannot do this, because a compiled `[Migration]` class body has no hash a runtime can compute — a
concrete capability the declarative design buys that the obvious design does not.

Corrections are new steps. A step that was wrong and shipped is corrected by a step that fixes it.

### 9.4 The `v1` freeze point

`M0001_Baseline` is editable freely until the first tagged release that writes a database anywhere,
including a developer's. After that it is frozen and `migrations.lock` gains its row. Before that point
the CI database is deleted and recreated on every run, so there is no state to preserve. This is stated
explicitly because "schema starts at v1" otherwise sounds like it means "v1 is forever malleable", and it
is not.

---

## 10. Tests

| Test | Asserts | WP |
|---|---|---|
| `OwnerVersionTableTests` | three owners numbered 1..n apply independently and idempotently into one file (§0.1, promoted to a real test) | WP-3 |
| `DeclaredSchemaLoaderTests` | a `SchemaStep` set with no assembly produces the expected tables, prefixed, with the expected version rows (§0.4) | WP-4 |
| `PluginSchemaValidatorTests` | ~25 malformed schemas each produce the expected defect and only that one — the `ShapeDefectCorpusTests` pattern | WP-4 |
| `IdentifierBudgetTests` | worst-case slug + table + index names are ≤ 63 bytes; a 24-character table name in a 23-character owner passes, 25 fails | WP-4 |
| `SchemaOwnerLifecycleTests` | the full §2.6 state table: disable, absent, reinstall, downgrade→quarantine, purge | WP-5 |
| `LedgerReconciliationTests` | a migration that creates an unprefixed table is rolled back and quarantined | WP-5 |
| `UnitFileLinkInvariantTests` | *(the milestone's existing test, re-pointed at `RelationalMediaStore`)* — all four cardinalities, ordinal, span | WP-8 |
| `CardinalityIndexTests` | the reconciler creates exactly the indexes the four §2.12 shapes imply, and dropping one is caught by integrity check 6 | WP-8 |
| `NullOrderingTests` | `OrdinalPath` comparison order is reproduced by the SQL `ORDER BY` on **both** engines; and a grep test that no `ORDER BY ord_` in `Sql.cs` omits `NULLS FIRST` | WP-7 |
| `EngineParityTests` | the entire store test suite runs twice — SQLite and Postgres — from one `[TestFixtureSource]`, as `postgres.runsettings` already does for the legacy tree | WP-9 |
| `ForeignKeyPragmaTests` | a connection from the factory has `foreign_keys = 1`; deleting a `media_file` cascades its links | WP-6 |
| `InstantRoundTripTests` | `DateTimeOffset` round-trips to the microsecond on both engines and sorts lexicographically on SQLite | WP-6 |
| `BackupManifestTests` | manifest content; restore of a backup with an unknown owner; restore of an owner ahead of its plugin | WP-10 |
| `IntegrityCheckTests` | each of the nine checks fires on a deliberately corrupted fixture and on nothing else | WP-11 |
| `MediaNeutralityTests` *(extended)* | the DDL text of `M0001_Baseline` contains no media noun — the same word list the milestone already enforces on type names, applied to SQL | WP-12 |
| `MigrationImmutabilityTests` | every core migration file's sha256 matches `migrations.lock` | WP-12 |

`EngineParityTests` is the one that must exist from WP-2 rather than at the end. Every parity bug in §6
(null ordering, `foreign_keys`, identifier folding, `Instant` handling) is invisible on SQLite alone.

---

## 11. Work packages, in order

| WP | Deliverable | Depends on | Notes |
|---|---|---|---|
| **WP-1** | Package registration: `Microsoft.Data.Sqlite` + pinned `SQLitePCLRaw.bundle_e_sqlite3`; resolve **NU1903** without `NoWarn` | — | Blocking. `TreatWarningsAsErrors` will fail the build otherwise (§6.1). |
| **WP-2** | `IArronixDatabase`, `StorageOptions`, dialects, connection pragmas, **and a Postgres CI service** re-running §0.1–§0.4 against Postgres | WP-1 | Nothing else lands until Postgres is exercised (§0.5). |
| **WP-3** | `OwnerVersionTable`, `SchemaMigrationService`, `CoreMigrationRunner`, the ledger tables, advisory locking | WP-2 | The hazard, closed. Ships with `M0001_Baseline` stubbed to the ledger tables only. |
| **WP-4** | `Arronix.Abstractions` 0.4.0: `ARX0018`, `IPluginSchemaProvider` + records, `Capability.Persistence`, `AddSchemaProvider`; `PluginSchemaValidator`, `DeclaredSchemaMigration`, `DeclaredSchemaLoader` | WP-3 | The four coordinated contract-governance edits per `unified-host-runtime.md` §7.2. |
| **WP-5** | Owner lifecycle: disable / absent / reinstall / downgrade / purge; ledger reconciliation and rollback | WP-3, WP-4 | §2.6 in full. |
| **WP-6** | `M0001_Baseline` — the whole core schema of §4 | WP-3 | Large, mechanical, one file. Reviewed as a schema. |
| **WP-7** | `library_coordinate`, the cache refresh path, `RebuildCoordinateCache`, null-ordering rules | WP-6 | §5.6. |
| **WP-8** | `RelationalMediaStore`, `ShapeIndexReconciler`, span guard; flip the one composition line | WP-6, WP-7 | The milestone's existing `UnitFileLinkInvariantTests` must pass unchanged against it. |
| **WP-9** | Full engine-parity test run (SQLite + Postgres) across the store suite | WP-8 | |
| **WP-10** | Backup / restore / manifest; the Postgres health warning | WP-6 | |
| **WP-11** | `IntegrityCheckService` and its nine checks; health contributor; delete the superseded housekeepers' equivalents from the new tree | WP-8, WP-10 | |
| **WP-12** | Governance: `migrations.lock`, DDL neutrality test, `Sql.cs` grep tests; documentation of `Arronix:Storage:*` | WP-8 | |

WP-1 through WP-3 are serial. WP-4 and WP-6 can run in parallel once WP-3 lands. WP-7 and WP-8 are the
critical path to the milestone's user-visible outcome (persistent library state).

---

## 12. Deferred, with the trigger

| Deferred | Where it would live | Trigger |
|---|---|---|
| **`IPluginDataStore`** — the read/write seam over plugin-owned tables | `Arronix.Abstractions.Schema` | The first plugin that must persist its own catalog rather than project it. The *schema* machinery ships now (§7.5); only the query surface waits, and it waits because a query contract designed against zero callers is the exact thing the three-tier rule exists to stop. |
| **`IMediaStore` promotion to Abstractions** | `Arronix.Abstractions` | Unchanged from `unified-host-runtime.md` §8. Still zero plugin implementers. |
| **`MigrateDown` / reversible migrations** | `DeclaredSchemaMigration.Down` | A user requirement that purge cannot satisfy. Would require every step to declare its inverse — an unpaid tax the surveyed apps also refused (#6). |
| **Per-plugin Postgres schemas** | `OwnerVersionTable.SchemaName`, the translator | A deployment where a DBA must grant per-plugin privileges. The `IVersionTableMetaData.SchemaName` and `OwnsSchema` members are already there; the blocker is SQLite parity (#3), so the trigger is really "Postgres-only deployments become a supported tier". |
| **`JSONB` and JSON-path queries** | `ColumnType.Json` rendering | A query that must filter inside a JSON column on Postgres *and* has a SQLite answer. Until both exist, adding it creates a divergence (#19). |
| **Read replicas / a separate read connection string** | `IArronixDatabase` | Evidence that read load is a problem. `ArronixDatabase` already funnels every read through one method, so this is an implementation change. |
| **Full-text search** | `library_coordinate` / a new `search_index` table | A search requirement `LIKE` cannot serve. SQLite FTS5 and Postgres `tsvector` are genuinely different enough to need a design, not a column. |
| **Sharding library state by kind** | — | A database large enough for it to matter. Nothing in §4 assumes single-table storage; the `kind_id` prefix on every key is already the partition key. |
| **Per-migration content hashing for *core* migrations** | `migrations.lock` | Ships in WP-12, but as a file hash rather than a semantic one. A semantic hash of a compiled migration is not computable; that asymmetry with plugin schemas (§9.3) is recorded as a known inconsistency. |
| **A Roslyn analyzer for unprefixed identifiers (`ARX1002`)** | a new analyzer project | Same reasoning as the milestone's `ARX1001`: the tests catch it at build, an analyzer would catch it at edit. Not this milestone. |
| **Automatic `VACUUM` / `pg_repack`** | `Integrity/` | Evidence of real bloat. Automatic compaction that silently fails (`Database.cs:82`) is worse than none. |
| **`pg_dump`-based Postgres backup** | `Backup/` | A deployment story where the binary's presence can be guaranteed. Until then it is a documented boundary, not a silent no-op (#24). |

---

## 13. Where this document depends on decisions the milestone spec does not make

Recorded rather than guessed, because `docs/design/unified-host-runtime.md` was being written
concurrently with this one.

1. **The spec is silent on whether plugins may own tables at all.** Its §4.6 says the store holds "library
   state only" and its §8 defers persistence wholesale; §2.11 makes plugins stateless projections. This
   document assumes plugins **will** own tables in a later milestone — that assumption is the entire
   reason the migration hazard matters — and gates it behind a new `Capability.Persistence` so that today
   the answer is still "no plugin owns a table". **If the platform decides plugins will never own
   tables, §2.4, §3.2, §3.3 and WP-4 collapse to nothing and the hazard disappears with them.** That
   decision should be taken explicitly rather than by default.
2. **The spec does not define a `Capability.Persistence`.** Its #14 enumerates the capability set and
   explicitly *drops* `PostImportMutation` and *defers* `ProviderDefinitions` and `LibraryBackend` on the
   grounds of zero implementers. `Persistence` has zero implementers today too, by the same standard. It
   is proposed here anyway because the gated registration (`AddSchemaProvider`) and the forward check
   both have an exact meaning from day one; if the reviewer disagrees, the fallback is to defer
   `IPluginSchemaProvider` entirely and ship §2 with `core` as the only owner — the mechanism is
   identical, only unused.
3. **The spec does not say where storage configuration lives.** This document assumes
   `Arronix:Storage:*` under the established `Arronix:` prefix with a `StorageOptions.SectionName`
   const, registered via `AddValidatedOptions<T>`, per `arronix-current-state.md` §7.
4. **The spec does not address a second database.** It never mentions the log database that all four
   surveyed apps ship. §16 here decides against one; if the host later needs an independently-backed-up
   store, the owner model already generalizes — a second `IArronixDatabase` with its own owner set —
   but nothing in this design assumes it.
5. **The id width is settled, and it is `long`.** `MediaItemId` and `MediaFileId` are both
   `readonly record struct …(long Value)` and both render as `BIGINT` (§4.1, §6.2). This was an open
   question in earlier drafts, parked on the reasoning that widening later would be free; that reasoning
   does not survive `M0001_Baseline` freezing at the first tagged release, so it was closed rather than
   parked. No assumption about the spec remains here — the requirement this document places on the
   contract assembly is only that CLR width and column width stay equal, and a test asserts it.
