# Copilot Instructions for Arronix

## Project Overview

Arronix is a modular, extensible media automation platform that enables different media types (TV, movies, music, books) to operate as plugins on a shared, unified core.

**Arronix is a clean-sheet rewrite, not a restructuring of the Sonarr v5 codebase.** Read that as a standing
instruction, because it decides how most ambiguous questions resolve:

- The *-arr* codebases are **evidence, not source material**. Survey them, cite them by file and line, and let
  what they got right and wrong inform a decision — then write the answer fresh. Do not port a type, a schema, a
  wire shape, an enum ordering or an API contract across, and do not "incrementally refactor" legacy code toward
  the plugin architecture.
- **No backwards compatibility, ever, before 1.0.** Nothing has shipped against an Arronix contract, so there is
  nothing to preserve. See invariant 9 below for what that forbids concretely.
- **No `[Obsolete]`, no deprecation window, no shim, no bridge, no dual contract.** A contract with no implementer
  outside this repository is **deleted**, not deprecated. There are zero `[Obsolete]` attributes in `Arronix.*`
  today; keep it that way.
- **There is no upgrade path.** Migration from a specific version of a specific *-arr* application is the job of
  separate, later, version-specific scripts — see "Migration Context" below. That is where version-specific
  knowledge belongs, and it is why the platform never has to carry two vocabularies.
- **The codebase is en-US** (invariant 8), build-enforced.

**Key Objectives:**
- Replace fragmented *-arr* applications with one shared architecture
- Separate core orchestration from media-specific logic
- Provide a plugin interface for extensibility
- Reach the usability and reliability *-arr* users expect, on a design chosen fresh rather than inherited

**Current reality (do not overclaim in code comments, PR text or docs):** the contract layer, plugin loader, host
subsystems, four reference media plugins, the Blazor client and `Arronix.Api` are implemented and tested. The
solution builds with **0 errors and 0 warnings** and the suite is **1,313 passing / 1 skipped** — that is the bar,
and a change that leaves either worse has failed. There is **no persistence**, **no telemetry emitter**, **no
authentication** and **no policy execution engine**. `ARCHITECTURE.md` §3 is the authoritative per-component status
table; keep it true.

## Architecture

### Project Set (all present in `src/`)

```
Contracts
 └── Arronix.Abstractions      0.3.0  Zero package references. The ONLY assembly a plugin
                                      or the browser client may reference.
Core
 ├── Arronix.Common            0.1.0  Host-side shared implementation of the contract layer
 ├── Arronix.Plugins           0.1.0  Manifest, versioning, ALC isolation, 16-step load
 │                                    pipeline, capability gating, scoped services
 └── Arronix.Host              0.1.0  Composition root: media kind registry, shape
                                      validation, media store, scheduler/queue,
                                      providers, health, intent registry
Edge
 ├── Arronix.Api               0.1.0  ASP.NET Core REST + SignalR. NOT Blazor.
 └── Arronix.Client            0.1.0  Blazor WebAssembly PWA
Plugins
 └── Arronix.Plugin.{Tv,Movies,Music,Books}  0.1.0 each
Tests
 ├── Arronix.{Abstractions,Common,Plugins,Host,Plugin.Tv,Plugin.Music}.Tests
 └── Arronix.Architecture.Tests      Governance: the platform invariants, enforced
```

### Hard invariants — violating any of these is a rejected change

1. **Media-agnostic core.** No `Series`, `Episode`, `Season`, `Movie`, `Album`, `Track`, `Artist`, `Book`, `Author`
   or `Edition` type may exist outside an `Arronix.Plugin.*` project. This holds for exported **members** and
   enum values too, not just type names.
2. **Plugins reference `Arronix.Abstractions` ONLY** — never Common, Plugins, Host, Api or another plugin. Enforced
   mechanically: the loader inspects the plugin's reference graph from PE metadata before loading it.
3. **`Arronix.Client` also references `Arronix.Abstractions` ONLY** — same discipline as a plugin, so no host-side
   implementation assembly can ever be shipped to a browser.
4. **`Arronix.Api` is NOT Blazor.** Plain ASP.NET Core REST + SignalR. It must contain no `.razor` file and takes
   **zero `PackageReference`s**. It does not reference `Arronix.Client`; it serves a configured client directory.
5. **No legacy edits.** Do not touch `src/NzbDrone.*`, `src/Sonarr.*`, `src/ServiceHelpers`, `frontend/` or
   `src/Sonarr.sln` when working on the platform. Platform work is purely additive.
6. **No new naming containing `NzbDrone` or `Sonarr`.**
7. **A plugin never supplies UI code.** It declares intent as data; the client maps intent to its own components.
   There is no component registry and there will not be one — see "UI Model" below.
8. **The codebase is American English.** Identifiers, XML doc comments, inline comments, string literals, wire
   values, manifest keys and Markdown prose. `Catalog` not `Catalogue`, `Normalize` not `Normalise`, `Behavior`
   not `Behaviour`, `Canceled` not `Cancelled`, `Labeled` not `Labelled`, `Color` not `Colour`, `License` not
   `Licence`. The reason is alignment, not taste: the BCL is en-US (`Initialize`, `Serialize`, `CodeAnalysis`), so
   en-US spelling puts Arronix identifiers in the same dialect as the members they sit beside.
   **Enforced mechanically** by `Arronix.Architecture.Tests/Naming/AmericanEnglishTests.cs`, which fails the build
   on an en-GB word in any `Arronix.*` identifier. Legitimate exceptions are names we do not own — BCL and SDK
   members, proper nouns (`Calibre`), GitHub Actions builtins (`cancelled()`), quoted external text. If you need
   one, add it to that fixture's controls with a reason; do not suppress the rule.
9. **No back-compatibility. Ever, before 1.0.** Nothing has ever shipped against an Arronix contract, so there is
   nothing to preserve. A contract that is wrong is **deleted and replaced**, not deprecated. Specifically, do not
   add: a sibling "v2" interface, an `[Obsolete]` deprecation period, a shim, or a host-side bridge that projects
   one vocabulary onto another. Migration from a specific *-arr* version is the job of a dedicated migration
   script written later — that is where version-specific knowledge belongs, and it is why the platform does not
   have to carry two vocabularies forever. The 0.4.0 provider work is the worked example: the legacy provider trio
   and its `StableProviderBridge` were deleted outright. See `ARCHITECTURE.md` §14.1 and resolution **#31** in
   `docs/design/unified-host-runtime.md`.

### Key Components
- **Abstractions**: versioned interfaces and DTOs (unmarked ones are not thereby stable — see invariant 9),
  plus five areas published `[Experimental]`
  (`Shape` ARX0013, `Plugins` ARX0014, `Providers` ARX0015, `Intent` ARX0016, `Wire` ARX0017)
- **Plugin Loader**: `plugin.json` discovery, per-plugin `AssemblyLoadContext`, PE-metadata reference check,
  bidirectional capability gating, scoped file system / HTTP / cache / rate limiter / events
- **Host Runtime**: media shape validation and registry, in-memory media store, scheduler and queue, provider
  subsystem, health aggregation, intent registry
- **Media Shape Model**: the central concept — see below
- **Policy Engine**: declared in manifests, syntactically validated, **not executed** (no engine exists)

### The Media Shape Model — read `ARCHITECTURE.md` §5 before touching anything media-related

A media kind is **a value, not a class hierarchy**. A plugin declares a `MediaShape`: levels and their roles
(library entry / acquisition unit / variant axis / completeness unit / file-bearing), how files bind to items
(anchor level, unit level, two uniqueness booleans, span constraints), coordinate spaces (ordinal / date / label /
singleton, with canonical, provenance-sensitive, may-be-unverified and dense flags), sequence axes and their
exceptions, grouping axes, format families with per-family quality ladders, selection facets, monitor dimensions,
acquisition scopes, search kinds over a closed nine-term vocabulary, field descriptors and naming tokens.

The host validates that declaration against **sixteen rules** at load time, then derives affordances and
completeness from it. Consequences for contributors:

- **Do not add a media noun to a contract.** If a shape cannot express something, the right move is usually a new
  declarative construct, not a media-specific escape.
- **Do not declare what can be derived.** Affordances and completeness are host-computed. A declaration that can be
  derived is a declaration that can disagree.
- **Closed vocabularies are enums; union values are tagged records with a `Kind` discriminator.** There is **no
  `[JsonPolymorphic]` anywhere in Abstractions**, deliberately — these values cross two independently versioned
  assembly boundaries (plugin *and* browser).
- Only **four behavior seams** exist: shape provider, release matcher, release query planner, item source.
  Everything else is data.

### UI Model — settled

- `Arronix.Api` is a plain ASP.NET Core REST + SignalR server (`/api/v1/...`, `/health`, `/hub/events`).
- `Arronix.Client` is a separate Blazor WebAssembly PWA referencing Abstractions only.
- The client renders **from descriptors**. `Arronix.Client/Rendering/IntentResolver.cs` is the single index mapping
  declared intent to components; five closed-enum tables make up the entire coupling, each a `switch` with **no
  discard arm**, so adding an intent member is a build failure until every table answers for it.
- A plugin-supplied UI component registry is **rejected**, permanently. It would require shipping plugin code to the
  browser, which breaks invariant 3. Where a declarative vocabulary genuinely cannot express a workflow (manual
  import being the daily example), the answer is a `WorkbenchDescriptor` — a declared editable grid over a
  plugin-supplied proposal. It lets a plugin *point at* a generic surface; never inject markup.
- No rich text anywhere. That is the first XSS surface.

### Design Principles
1. **Media-agnostic Core**: no TV/Movie types in Core
2. **Versioned Contracts**: plugins pin to ranges; experimental contracts gated and greppable. Stability is a
   guarantee that starts **at 1.0** — below it, any contract may be changed, renamed or deleted in a MINOR
3. **Declaration over code**: a media kind is a value
4. **Derive rather than declare** whatever can be derived
5. **Single Scheduler**: plugins register jobs; host coordinates
6. **Parse, don't validate**: after the shape gate, no host lookup can fail
7. **Testable by Design**: contract tests, shape corpus tests, real-assembly loader tests

## Technology Stack

Platform (`src/Arronix.*`):
* **Runtime**: .NET 10 (`global.json` pins the 10.0 SDK with prerelease allowed). Already adopted — this is not a
  future step.
* **Frontend**: Blazor WebAssembly (`Arronix.Client`), installable PWA. No Node, yarn or webpack.
* **Transport**: ASP.NET Core minimal APIs + SignalR (`Arronix.Api`).
* **Data access**: **none yet.** The media store is in-memory only.
* **Extensibility**: `AssemblyLoadContext` per plugin, with a PE-metadata reference check before load.
* **Testing**: NUnit 4 + FluentAssertions 8 + Moq.

Legacy (still in repo, untouched, builds from `src/Sonarr.sln`):
* **Frontend (legacy)**: TypeScript/React with webpack (`frontend/`)
* **Database**: relational DB with the legacy custom persistence abstractions
* **Build**: .NET SDK + Node.js/yarn (only for the legacy React assets)

### Data access: EF Core (owner decision, 2026-08-12)

**Persistence is EF Core + SQLite/Postgres.** This is an owner decision that supersedes the earlier
"EF Core is REJECTED" rule previously recorded here, which in turn had reversed a still-earlier plan.
The pendulum stops here: EF Core it is.

What carries over unchanged from `docs/design/storage-layer.md` (its decisions are ORM-agnostic unless
stated): schema ownership (core tables vs plugin-owned), the catalog/library split, the
`(kind_id, level_id, item_id)` universal key, the `unit_file_link` join with cardinality realized as
partial unique indexes, forward-only migrations, downgrade-quarantines, uninstall-never-drops-data.
What that document must be revised to replace: everything FluentMigrator-specific — per-plugin migration
scoping maps onto **one `DbContext` per schema owner with its own `MigrationsHistoryTable`**, which is the
EF-native form of the same isolation. `Microsoft.Data.Sqlite`'s NU1903 `SQLitePCLRaw` advisory noted there
applies equally to `Microsoft.EntityFrameworkCore.Sqlite` and must be handled under `TreatWarningsAsErrors`
when the storage milestone starts.

**Boundary rule (unchanged and load-bearing): EF Core lives host-side only** — in the storage assembly,
behind the `IMediaStore` seam. `Arronix.Abstractions` keeps zero package references, so no plugin and not
the client can ever see a `DbContext`; media-kind plugins declare schema/shape as data and the host owns
all persistence. The storage milestone has not started — no persistence code has been written.

**Query rule (owner decision, 2026-08-17): LINQ for everything, zero SQL anywhere.** No `FromSqlRaw`,
`FromSqlInterpolated`, `SqlQuery`, `ExecuteSqlRaw`, `migrationBuilder.Sql(...)`, or SQL strings of any
kind, in queries or migrations. Set-based writes use `ExecuteUpdate`/`ExecuteDelete` (LINQ-based). Use
provider-neutral constructs only — no provider-specific `EF.Functions.*` members (e.g. Npgsql `ILike`),
so every query has exactly one definition and two translations (SQLite/Postgres parity by construction).
Grouped-latest/window-shaped queries are expressed as LINQ (`GroupBy` + ordered `First`), which EF Core 10
translates. This rule is to be enforced mechanically by `Arronix.Architecture.Tests` when the storage
assembly exists. The upstream *arrs' hand-rolled SQL layer is the cautionary tale, not the model: their
homegrown `Where` builder once generated table-truncating DELETEs (Radarr PR #3964/#3968).

## Development Setup

### Required Tools
* .NET 10 SDK (prerelease allowed; see `global.json`)
* Git, Visual Studio Code (with recommended extensions)
* Node.js and yarn — **only** if modifying the legacy React UI
* `dotnet workload install wasm-tools` for publishing `Arronix.Client`

### Getting Started
```bash
dotnet build Arronix.sln        # NOTE: currently fails on Arronix.Api (6 CS0246 errors)
dotnet test src/Arronix.Host.Tests/Arronix.Host.Tests.csproj
```

There is no runnable platform application yet. The legacy Sonarr app is unchanged: `yarn install`, `yarn start`,
build `src/Sonarr.sln`, browse to `http://localhost:8989`.

## Code Conventions

### Build conventions (`src/Directory.Build.props` — read it before adding a project)
- `net10.0`, `Nullable=enable`, `LangVersion=latest`, `GenerateDocumentationFile=true`
- **`TreatWarningsAsErrors=true`, `AnalysisLevel=6.0-all`, `EnforceCodeStyleInBuild=true`** — warnings ARE errors.
  Budget time to reach zero; do not add `NoWarn` to get there.
- Every new csproj mirrors `src/Arronix.Common/Arronix.Common.csproj`: explicit `TargetFramework`, `RootNamespace`,
  `AssemblyName`, `Version`, `Description`, plus the three-line Sentry opt-out PropertyGroup
  (`SentryUploadSymbols` / `PublishRepositoryUrl` / `EmbedAllSources` all false) — that block is inherited from the
  legacy props and must be switched off.
- `Arronix.Client` must set `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` and clear `<RuntimeIdentifiers/>`,
  because `Directory.Build.props` otherwise defaults the RID to the build host's.
- **Central package management**: `Directory.Packages.props` owns every version. If a package you need is not
  registered, raise it — do not add it in passing.

### C# Code Style
- Follow `.editorconfig`; LF line endings
- File-scoped namespaces; an explicit `GlobalUsings.cs` per project
- Dependency injection for all service dependencies; no static singletons
- Keep Core media-agnostic; media-specific logic belongs in plugins
- Use contracts from `Arronix.Abstractions`
- Structured failures carrying a `CoreErrorCode` and specific defects — never a bare "load failed"

### Experimental contracts
Consuming an experimental area (`ARX0013`–`ARX0017`) requires a **file-scoped** `#pragma warning disable ARXnnnn`
with a reason comment. **Never add a project-wide `NoWarn` for an `ARXnnnn`**, and never suppress one in
`Directory.Build.props`. In `.razor` files the pragma must sit in a `@{ }` block at the very top of the file,
because Razor-generated code raises the diagnostic on markup as well as `@code`.

A single media plugin typically needs four at once: `ARX0013` (Shape), `ARX0015` (validation types reachable
through workbench proposals), `ARX0016` (Intent) and `ARX0017` (Wire).

### Blazor Guidelines (`Arronix.Client`)
* Organize components by capability (`Browse/`, `Workbench/`, `Providers/`, `Shared/`, `Rendering/`)
* Keep every component media-agnostic; **no `kind == "..."` comparisons anywhere**
* Switch over closed enums with **no discard arm**, so a new contract member is a build failure (CS8509). Only
  `CS8524` (unnamed enum value) may be suppressed, and only with a stated reason — that is precisely what keeps
  CS8509 meaningful.
* Minimize JS interop; encapsulate what is needed in a thin, testable service
* Client and server `JsonSerializerOptions` must stay identical, including the identity converters — those types
  have private constructors and cannot be rebuilt by the serializer without them

### Naming Conventions
* C# classes / methods / Razor components: PascalCase
* C# private fields: `_camelCase`
* TypeScript (legacy UI): camelCase for variables/functions, PascalCase for components
* Configuration keys: PascalCase, sectioned (`Arronix:Host`, `Arronix:Plugins`, `Arronix:Api`)

## Testing

### Test Structure
- **Platform tests**: `Arronix.*.Tests` — note the **plural**. `Directory.Build.props` auto-adds the test SDK
  packages only for `*.Test` (singular), so an `Arronix.*.Tests` project must declare them itself. Mirror
  `src/Arronix.Common.Tests/*.csproj` exactly.
- **Legacy tests**: `NzbDrone.*.Test` (singular) — unchanged
- **Contract tests**: the experimental surface is enumerated and every new type asserted to carry `[Experimental]`
  with its own area's diagnostic id
- **Shape corpus tests**: all four reference shapes plus a corpus of deliberately broken shapes
- **Loader tests**: a real plugin assembly is emitted at test time with `PersistedAssemblyBuilder` and run through
  the full pipeline. A host-constructed fake cannot exercise the cross-load-context cast, so it cannot prove it.

### Running Tests
- Per project: `dotnet test src/Arronix.Host.Tests/Arronix.Host.Tests.csproj`
- Solution-wide: `dotnet test Arronix.sln` — 1,313 passing, 1 skipped.

### Governance tests
The platform invariants above are enforced by `Arronix.Architecture.Tests`, not by review. It reads the **working
tree** rather than compiled output, so a rule keeps being checked even while the project it polices is mid-rewrite:
media neutrality over exported types and members, the UI-technology deny-list, project topology, capability gating,
contract-area coverage, and the en-US rule (invariant 8).

Two conventions apply when adding a rule there. Every detector needs a **positive and a negative control** in
`Repository/DetectorSelfTests.cs` or beside the rule — a governance suite's characteristic failure mode is that the
detector silently stops detecting and every rule reports success while checking nothing. And match **words, not
substrings** (`SourceScanner.Words`): a rule that over-reports is a rule that gets suppressed, and a suppressed
rule enforces nothing.

### Test projects that do not exist yet
`Arronix.Api.Tests`, `Arronix.Plugin.Movies.Tests`, `Arronix.Plugin.Books.Tests`.

## Plugin Development

### Plugin Manifest Structure (`schemaVersion: 0`)
Required: `schemaVersion`, `id`, `name`, `version`, `contracts`, `entryAssembly`, `capabilities`.
Optional: `description`, `mediaKinds`, `identifiers`, `tokens`, `policies`. **Any other key is a load failure.**

- `id` — lower-case alphanumeric segments separated by dots, starting with a letter
- `contracts.arronix` — a documented subset of npm/yarn range syntax: comparators, whitespace conjunction, `||`,
  partial versions. **Caret, tilde, wildcards and hyphen ranges are rejected.** A plugin using experimental
  contracts must bound its range at the host's next minor: against a 0.3 host, use `>=0.3 <0.4`.
  Both `>=0.3 <1.0` and `>=0.1 <1.0` are **refused**.
- `entryAssembly` — a bare `*.dll` name; path separators (both kinds, on every platform) and `..` are rejected
- `capabilities` — from a closed vocabulary of fourteen: `indexing`, `metadata`, `parsing`, `matching`, `quality`,
  `renaming`, `import`, `download`, `notification`, `media-kind`, `curation`, `network`, `storage`,
  `telemetry-sink`. (`curation` replaced `import-list` in 0.4.0 — renamed, not aliased.) **Do not declare `network`** — it is implied by indexing/metadata/download/notification, and
  declaring it without a matching registration fails the forward check.
- `mediaKinds` — requires `media-kind`, and `media-kind` requires at least one entry here
- `tokens` — brace-delimited (`{Series Title}`), as a string or an object with `name`/`description`/`exampleValue`/
  `isRequired`. They must match `MediaShape.Tokens` **symmetrically** — a token in one and not the other is a load
  failure.
- `policies` — five closed categories: `parsing`, `matching`, `quality`, `import`, `naming`. A sixth is a failure.

`src/Arronix.Plugins/Manifest/plugin.schema.json` is an **editor aid only**; the runtime gate is the reader plus the
validator, and the JSON schema is never consulted at load time.

### Plugin Guidelines
- Implement exactly one `IPluginModule` per plugin; its id must equal the manifest id
- Register everything explicitly through the plugin registry — there is no container, no assembly scan, and the host
  never reflects over plugin types
- Declare only capabilities you actually register. Enforcement is **bidirectional**: a declared capability needs a
  registration, and a gated registration needs a declaration
- Ship `plugin.json` as `Content` / `PreserveNewest` beside the assembly
- Declare zero `PackageReference`s in a media plugin
- **Static-initialization hazard:** a `public static MediaShape Declaration { get; } = Build();` declared *above* the
  static members `Build()` reads will silently construct the whole shape out of `null`s — it compiles and passes
  most tests. Declare `Declaration` **last**.

## Common Workflows

### Adding a New Feature
1. Create or reference an existing GitHub issue
2. Fork from `dev` using `feature/feature-name`
3. Make changes, following the conventions above
4. Add tests
5. Update documentation (see the synchronization rules below)
6. Ensure all tests pass and the build is warning-free
7. PR back to `dev`

### API Development
- Routes live under `/api/v1/...`; keep them media-neutral. No media noun in a route, a DTO or a query parameter.
- `Arronix.Api` takes **zero packages** — solve problems with what the shared framework provides
- **There is no authentication in `Arronix.Api` today.** The design is in `docs/design/authentication.md`; nothing
  is implemented. Do not describe the platform API as authenticated.
- Secrets in provider settings are declared sensitive once, which drives storage, redaction and editing. Never log a
  setting value; never return an unmasked secret.

### Storage / Migrations
No storage code exists. Before writing any, read `docs/design/storage-layer.md` in full — it resolves 24 contested
decisions, most of them ORM-agnostic and still binding. **Persistence is EF Core** (owner decision, 2026-08-12 —
see "Data access" above); the document's FluentMigrator-specific sections are superseded and pending revision, with
per-plugin migration scoping mapping onto one `DbContext` per schema owner with its own `MigrationsHistoryTable`.
EF Core stays host-side only, behind the `IMediaStore` seam — never reachable from a plugin or the client.

## Important Files and Locations

* **Docs**: `README.md`, `ARCHITECTURE.md`, `GLOSSARY.md`, `CONTRIBUTING.md`
* **Contract policy**: `docs/contracts/stability.md`
* **Authoritative platform spec**: `docs/design/unified-host-runtime.md`
* **Design notes** (intent, not evidence): `docs/design/*.md`
* **Build config**: `global.json`, `Directory.Packages.props`, `src/Directory.Build.props`, `.editorconfig`
* **Platform source**: `src/Arronix.*`
* **Legacy source**: `src/NzbDrone.*`, `src/Sonarr.*`, `frontend/src/`
* **CI/Workflows**: `.github/workflows/`

## Glossary Quick Reference

Key terms (see `GLOSSARY.md` for full definitions): media shape, level role, addressing scheme, acquisition unit,
file-bearing unit, anchor level, grouping axis, selection facet, format family, affordance, behavior seam,
capability, contract range, plugin module, intent surface, UI contribution, workbench, wire contract.

**The five provider families** — `Indexer` (`IIndexer`), `Downloader` (`IDownloader`), `Notifier` (`INotifier`),
`Cataloger` (`ICataloger`), `Curator` (`ICurator`). The last two are the pair to get right:

* a **Cataloger** answers **what a thing *is*** — an external authority supplying identity and canonical facts,
  which populate the catalog record;
* a **Curator** answers **which things you *want*** — an external list selecting what belongs in the library, and
  saying nothing about what its entries are.

TheMovieDB is a Cataloger; a Trakt watchlist is a Curator. Neither is derivable from the other. The old names
(`IDownloadClient`, `IMetadataSource`, `IImportListSource`) and the legacy provider trio are **deleted**, not
aliased — see invariant 9. User-facing labels still read Indexers / Download Clients / Metadata / Import Lists,
because those are the words the *-arr* community already knows; only the identifiers changed.

## Security Considerations

- Never commit secrets or API keys
- Use structured logging; never log a declared-sensitive setting value
- Plugin code executes in-process. Isolation is enforced at three layers — the reference-graph check before load, the
  deny-listing load context, and capability-scoped platform services — but it is not a security boundary against
  hostile code
- Validate all user inputs
- The platform API is currently unauthenticated; do not deploy it on an untrusted network

## Branching and Pull Requests

- **Default branch**: `dev`
- **Feature branches**: `feature/feature-name`
- **PR requirements**: linked issue, all tests passing, zero warnings, code review approval, documentation updates

## Notes for Contributors

- This is an active clean-sheet build; the architecture is still being settled, and a decision that turns out wrong
  is corrected rather than layered over
- The platform is written beside the legacy tree, not out of it. Do not "incrementally refactor" legacy code
  toward the plugin architecture — the platform is clean-room and the legacy tree is frozen.
- Follow the Definition of Done in `CONTRIBUTING.md`
- Coordinate significant changes via issues first

## Migration Context

Arronix is a **clean-sheet rewrite**. It does **not** migrate a Sonarr database in place. Earlier drafts of this
file described extracting TV logic from the legacy core and re-mapping its schema; that plan is superseded.

1. The legacy tree is untouched and referenced by no `Arronix.*` project
2. The four media plugins are **clean-room reference implementations** of the shape model, informed by surveys of
   the corresponding *-arr* applications rather than derived from their code
3. Schema starts at v1. User migration is the job of **dedicated per-*arr* migration scripts, written later** —
   one-shot tools that read an existing installation and re-establish that library in the new model. Those scripts
   do not exist yet. This is the counterpart to invariant 9: version-specific knowledge lives in a script that runs
   once and is discarded, **not** in dual contracts the platform carries forever

### Upcoming Milestones (not started)
1. Register the five platform services (`ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`,
   `IHostRuntimeInfo`, `IOperatingSystemInfo`) so plugins can actually activate
2. Storage layer: Dapper + FluentMigrator + SQLite/Postgres per `docs/design/storage-layer.md`
3. Telemetry emitter and sinks per `docs/design/observability.md`
4. Authentication and identity per `docs/design/authentication.md`
5. Acquisition pipeline, import pipeline and downloaders
6. Per-*arr* migration scripts (see "Migration Context")

Do not begin these without an associated tracking issue and architectural approval.

## Documentation Synchronization & Auto-Maintenance Rules

These instructions MUST be kept up to date by the assistant whenever a change transitions from "planned" to
"implemented" in the repository.

### Core Rule
When a feature, technology or structural element described here as "planned", "future" or "not started" becomes
present in the codebase, update:
1. `.github/copilot-instructions.md` (remove or rephrase the planned status)
2. `ARCHITECTURE.md` (the §3 status table first, then the affected section)
3. `GLOSSARY.md` (add/modify terms introduced or changed)
4. `CONTRIBUTING.md` (workflows, build steps, contribution expectations)
5. `README.md` (project layout, status table, quickstart)

### The overclaiming rule
**Partial is not implemented.** If a subsystem builds and is tested but cannot yet do its job in a running
deployment, say so and say what is missing. `ARCHITECTURE.md` §3 must distinguish ✅ implemented, 🟡 partial and
🚧 planned, and every 🟡 must name its gap. A design document under `docs/design/` is **never** evidence that
something exists — check the source tree.

### Trigger Conditions
| Trigger | Required Doc Updates |
|---------|----------------------|
| A new top-level `src/Arronix.*` project | All five docs: project layout, status table, glossary if it introduces a concept |
| `Arronix.Api` compiles / becomes runnable | README quickstart and status, Architecture §3 and §8, Contributing |
| Persistence lands (Dapper/FluentMigrator) | Architecture §6 and §3, Glossary (Media Store, migration terms), Contributing (migration workflow), Copilot Instructions |
| A telemetry emitter or sink lands | Architecture §11 and §3, Copilot Instructions, Glossary (Observability Sink) |
| Authentication lands | Architecture §12, Copilot Instructions (remove "unauthenticated"), README |
| A policy execution engine lands | Architecture §4.2 and §3, Glossary (move terms out of "Declared but not executed") |
| A new contract area or experimental diagnostic id | Architecture §3.2 and §9.1, `docs/contracts/stability.md`, the experimental surface test |
| A change to the media shape model | Architecture §5, Glossary, and the sixteen-rule list if a rule changes |
| A new capability, policy category or search term | Architecture §4.1/§5.6, Glossary, `plugin.schema.json` |
| Token system changes | Glossary, Architecture §10, Copilot Instructions |
| A governance test suite lands | Architecture §15, Copilot Instructions (remove "by review, not by test") |
| Removal of the legacy React build pipeline | Copilot Instructions, Contributing, README |

### Update Procedure (Assistant Checklist)
1. Detect the change (a new project, package reference, or removed asset in the diff)
2. **Verify against the source tree, not against a report or a design doc.** Build it. Run the tests. Grep for the
   type. A claim you have not checked does not go in a doc.
3. Search for remaining occurrences of "planned", "future" or "not started" relevant to that change
4. Edit affected docs in a single PR with a concise summary referencing the enabling change
5. Ensure internal consistency: diagrams match the project set; quickstarts match current build steps; the §3 table
   matches what actually builds
6. Remove obsolete instructions — do not leave contradictory parallel workflows unless intentionally transitional,
   and if transitional, label both with exit criteria
7. Add any new glossary terms (conceptual terms, not class or interface names)

### Language & Style Guidance
* Use "Implemented" only when it is true end to end; otherwise "Partial" with the gap named
* Prefer neutral declarative sentences over speculative wording
* Keep roadmap items in a clearly marked section
* Never describe a design document as though it were shipped code

### Anti-Drift Safeguards
Before closing any PR that adds or removes:
* A top-level `src/Arronix.*` project
* A major framework dependency
* A build step

...verify and adjust the five doc targets above.

### Glossary Enrichment Rules
* Only durable concepts; no temporary codenames or transient migration tasks
* Prefer conceptual names over class/interface names
* "Declared but not executed" means **declared, and not yet acted on**. A concept that was considered and
  **refused** does not belong there — it goes in "Refused" (which exists to stop a rejected idea reading as a
  backlog item) or is deleted outright. Say which in the PR description.

### Conflict Resolution
If simultaneous changes would make instructions ambiguous, label both paths clearly ("Legacy React (until
removed)" / "Blazor (current)") and include an exit criterion.

### Assistant Responsibility Statement
The assistant is expected to proactively inspect diffs of structural changes and initiate documentation updates
without waiting for an explicit prompt — and to verify every claim against the code on disk before asserting it.

## License

Licensed under GNU GPL v3. Portions originated from Sonarr v5 (GPLv3).
