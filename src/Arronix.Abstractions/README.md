# Arronix.Abstractions

Core abstractions and contracts for the Arronix media platform.

## Overview

`Arronix.Abstractions` defines the contract layer that enables media-agnostic plugin development for Arronix. This library contains no implementation code—only interfaces, DTOs, and type definitions that plugins and the host runtime use to communicate.

## Purpose

- **Decouple plugins from host**: Plugins depend only on abstractions, not the host implementation
- **Enable versioning**: Semantic versioning with compatibility ranges
- **Prevent coupling**: No media-specific types (TV, Movies) in the contract layer
- **Enforce contracts**: Interface definitions act as compile-time contracts

## Nothing here is stable yet

**This assembly is below 1.0.0, so it is unstable in the semantic-versioning sense** — which is what
[SemVer 2.0.0 §4](https://semver.org/#spec-item-4) reserves major-version-zero for. Concretely:

- Any contract may be **changed, renamed or deleted in a MINOR release**. There is no MAJOR boundary to wait
  for; the next MAJOR *is* 1.0.0.
- A contract with **no implementer outside this repository is deleted outright, not deprecated**. There is no
  `[Obsolete]` period, no shim, no bridge and no dual contract. `Arronix.*` contains zero `[Obsolete]`
  attributes and that is the intended steady state.
- Arronix is a clean-sheet rewrite. Migration from a specific version of a specific existing application is the
  job of a separate, later, version-specific script — never of a second contract living beside the first.

The guarantees in [the stability policy](../../docs/contracts/stability.md) take effect **at 1.0.0**. Read that
document before assuming anything below is frozen.

## Package Contents

Contracts fall into two groups by *opt-in cost*, not by stability. The areas below carrying an
`[Experimental]` marker are a **compile error** until the consuming file opts in; the rest compile silently.
Neither group is frozen — see [Nothing here is stable yet](#nothing-here-is-stable-yet).

## Contracts carried from 0.1.0 (no `[Experimental]` marker)

Being unmarked means only that consuming one costs no `#pragma`. It is not a stability promise.

### Identity Types

- `MediaKindId`, `MediaItemId`, `ReleaseId`

### Core Interfaces

- `IMediaKind` - Plugin identity and capabilities
- `IMediaIdResolver` - External ID to internal ID mapping
- `IReleaseParser` - Release name parsing
- `IQualityModel` - Quality evaluation and upgrade logic
- `IImportPipeline` - File import validation and execution
- `IRenamePolicy` - File naming template resolution
- `ILibraryLayout` - Folder structure generation

### Scheduling

- `IScheduledJob` - Background task contract
- `IBackgroundTaskRegistry` - Job registration

### DTOs

- Release: `ReleaseCandidate`, `ParsedRelease`
- Decisions: `MatchDecision`, `ImportDecision`
- Quality: `QualityTier`, `CutoffPolicy`
- Library: `LibraryPathSpec`, `NamingToken`
- Language support

### Health & Errors

- `HealthCheck`, `CoreErrorCode`
- Health status and severity enums

## Experimental contracts (0.2.0 onward)

These are the platform seams. Each is marked `[Experimental]` with the diagnostic id of its area, so
referencing one is a **compile error** until the consuming file opts in.

### Infrastructure seams (0.2.0)

| Area | Id | Contracts |
|---|---|---|
| `Caching` | `ARX0001` | `ICacheProvider`, `ICache`, `ICache<TValue>`, `ISelfRefreshingCache<TValue>` |
| `Diagnostics` | `ARX0002` | `IProgressReporter`, `ProgressLevel`, `IRedactionRuleProvider`, `RedactionRule` |
| `Errors` | `ARX0003` | `ArronixException` |
| `Events` | `ARX0004` | `IDomainEvent`, `IEventPublisher`, `IEventHandler<TEvent>` |
| `FileSystem` | `ARX0005` | `IFileSystem`, `IFilePermissions`, `IFileTransferService`, `FileTransferMode`, `IStorageMount`, `IStorageMountProvider`, `StorageMountOptions`, `DestinationExistsException`, `PlatformPath`, `PlatformPathKind` |
| `Health` | `ARX0006` | `IHealthContributor` |
| `Hosting` | `ARX0007` | `IPluginPaths`, `IOsVersionProbe`, `OsVersionDescriptor`, `IOperatingSystemInfo`, `IHostRuntimeInfo`, `IServiceLifecycleController`, `ServiceLifecycleStatus`, `INativeLibraryResolver` |
| `Http` | `ARX0008` | `IHttpGateway`, `OutboundHttpRequest`, `OutboundHttpResponse`, `OutboundHttpResponse<TResource>`, `HttpHeaderCollection`, `IOutboundHttpInterceptor`, `HttpGatewayException`, `HttpRateLimitedException`, `UnexpectedHtmlResponseException`, `ICertificateValidationPolicy` |
| `Naming` | `ARX0009` | `IDiacriticFoldingProvider` |
| `Serialization` | `ARX0010` | `IJsonSerializer` |
| `Telemetry` | `ARX0011` | `ITelemetrySink`, `ITelemetryEmitter`, `TelemetryEmitterExtensions`, `ITelemetryEnricher`, `ITelemetryEventFilter`, `TelemetryEvent`, `TelemetrySeverity` |
| `Throttling` | `ARX0012` | `IRateLimiter`, `IRateLimitLease` |

`IHealthContributor` reuses `HealthCheck`, `HealthStatus` and `HealthSeverity` verbatim — those are not
re-declared and carry no `[Experimental]` marker, so a file implementing the contributor opts in to
`ARX0006` alone.

### Platform seams (0.3.0)

| Area | Id | What it is |
|---|---|---|
| `Shape` | `ARX0013` | How a media kind is structured — levels and their roles, coordinates, file binding, grouping axes, format families, selection facets, fields and searches — plus the four seams a media extension implements: `IMediaShapeProvider`, `IReleaseMatcher`, `IReleaseQueryPlanner`, `IMediaItemSource` |
| `Plugins` | `ARX0014` | `IPluginModule`, `IPluginContext`, `IPluginRegistry`, `PluginId`, `Capability`, `CapabilityNames`, `CapabilitySet`, `PluginCapabilityException`, `WellKnownJobResults` |
| `Providers` | `ARX0015` | The stateless provider model: one spine, `IProvider`, and five families — `IIndexer` (sources of release candidates), `IDownloader` (clients that transfer them), `INotifier` (outbound notification destinations), `ICataloger` (external authorities answering what a thing *is*, populating the catalog record) and `ICurator` (external lists answering which things you *want*) — plus declarative settings, validation and provider events |
| `Intent` | `ARX0016` | What an extension declares about how its kind is worked with: actions, traversals, orderings, filters, states and working surfaces |
| `Wire` | `ARX0017` | What the host publishes to a client that may reference nothing but this assembly |

Three things are worth knowing about these areas before consuming them.

**`Arronix.Abstractions.Intent` names no interface technology and no control.** It declares intent —
what an action costs, whether a value is a secret credential, what a state means, how a hierarchy is
traversed — and never how any of it is offered. A reader of these contracts cannot tell what consumes
them, and that is deliberate: it is what lets a command line, a text interface or a hands-free client
honor the same declarations without change. An extension contributes declarations, never code, to any
interface.

**`Arronix.Abstractions.Providers` contains no unmarked types at all.** It once shared its namespace with
a trio of legacy provider contracts — `IIndexerProvider`, `IMetadataProvider` and `IDownloadClientAdapter`,
with their DTOs and a host-side `StableProviderBridge` that projected between the two vocabularies. In
0.4.0 all of it was **deleted outright**: no `[Obsolete]` period, no shim, no dual contract. Nothing had
ever shipped against them, so there was nothing to preserve. Every type in the namespace now requires an
`ARX0015` opt-in. The full rename table is in the
[stability policy's 0.4.0 entry](../../docs/contracts/stability.md#040).

**Everything in 0.3.0 and 0.4.0 is experimental**, so a plugin that uses any of it declares
`"arronix": ">=0.3 <0.4"` and re-declares at each MINOR. See the stability policy for why.

### The model areas (0.5.0 onward)

Added after the platform seams and missing from the table above until now.

| Area | Id | What it is |
|---|---|---|
| `Definition` | `ARX0019` | The declarative aggregate a definition-mode extension once registered, and the vocabularies its sections are written in |
| `Media` | `ARX0020` | The typed media model: the entity and group markers, the authoring seam and its builder, the property attribute vocabulary, and the runtime model the host derives from all of them |
| `Quality` | `ARX0021` | The quality-axes model: typed `Evidence<TValue>` and `EvidenceSet<TValue>` with their provenance, the `[Axis]` vocabulary, `IQualityType` (what a family detects) and `QualityPolicy` (what a user prefers), the kind-blind `QualityPoint` they meet on, the size and rendering contracts over it, the normalized token vocabulary a scan and a family's reading share, and the standard families under `Quality.Families` |

**`Quality.Families` is the same area rather than one of its own.** The standard families are what the
framework is for, a consumer that opts in to the model opts in to the families it ships with, and a second
identifier would make one opt-in into two for no boundary anybody can point at. They live in the contract
assembly because the client references only this assembly and has to render a quality label and a policy
editor, so the label rules and the axis descriptors must be reachable from here.

**`ARX0021` shares its namespace with the 0.1.0 `IQualityModel`, which is not part of the area.** That
interface answered two questions with one type — what a file's quality *is*, and which of two is *better* —
and answering them together is what made every ranking decision the media kind's rather than the user's.
The successors separate them: `IQualityType` reads evidence and cannot compare anything, and
`QualityPolicy` compares and knows nothing about parsing. `IQualityModel` stays until its last implementer
converts, and is then deleted rather than deprecated.

To consume one, opt in at the top of the file that needs it, naming only the area it needs:

```csharp
// Contributes redaction rules for this extension's credentials.
#pragma warning disable ARX0002

using Arronix.Abstractions.Diagnostics;
```

Opting in per file, rather than adding the id to a project-wide `NoWarn`, is a deliberate rule: it
keeps "which files depend on contracts that may still change?" answerable with a single search, and it
stops a project silently acquiring new experimental dependencies. `[SuppressMessage]` does not work —
the compiler reports this as an error, and only `#pragma warning disable` and `NoWarn` suppress it.

See [API Stability Policy](../../docs/contracts/stability.md) for when the marker comes off, and for what the
Plugin Loader does with a plugin whose declared range outruns an experimental contract.

## Version

`<Version>` in `Arronix.Abstractions.csproj` is **0.3.0**. The provider deletion and the five-family rename are
recorded in the stability policy as **0.4.0** and are present in the source; the assembly version has not been
bumped to match, and every reference `plugin.json` still declares `">=0.3 <0.4"`.

0.3.0 added five contract areas, all `[Experimental]`. 0.4.0 deleted the legacy provider trio, its DTOs and the
host-side bridge, and renamed the five families — a **breaking change made deliberately and without a migration
path**, which is what being below 1.0.0 is for.

`CoreErrorCode` gained nine members in 0.3.0 (2003–2010, 3002) and had three renamed in 0.4.0
(`DownloadClientConnectionFailed` → `DownloaderConnectionFailed`, `MetadataProviderConnectionFailed` →
`CatalogerConnectionFailed`, `MetadataProviderSearchFailed` → `CatalogerSearchFailed`). Its numeric values are
unchanged so far, but nothing forbids renumbering them: no value is persisted and no consumer reads one. If a
renumbering ever reads better, do it and record it here.

## Usage

### For Plugin Developers

Reference this package in your plugin:

```xml
<PackageReference Include="Arronix.Abstractions" Version="0.3.0" />
```

Declare your supported version range in `plugin.json`:

```json
{
  "contracts": {
    "arronix": ">=0.3 <0.4"
  }
}
```

**Pin to a single MINOR line. `">=0.1 <1.0"` and `">=0.3 <1.0"` are refused at load**, for every plugin,
whether or not it touches an experimental contract. The gate is on the declared range, not on usage:
`VersionRange.SatisfiesExperimentalGate` requires an exclusive upper bound at or below the host's next minor,
and `PluginLoader` quarantines a plugin that reaches past it with `CoreErrorCode.PluginContractMismatch`.
A range spanning more than one MINOR asserts a promise these contracts do not make — below 1.0.0 any of them
may change in any MINOR.

Implement the interfaces relevant to your plugin's capabilities.

### For Host Developers

Reference this package in the host runtime to:

- Validate plugin contract compatibility
- Invoke plugin services via interfaces
- Exchange data using the defined DTOs

## Documentation

- [API Stability Policy](../../docs/contracts/stability.md) - Versioning and compatibility
- [Implementation Summary](../../docs/contracts/implementation-summary.md) - Technical details
- [Architecture](../../ARCHITECTURE.md) - Overall system design

## Building

```bash
dotnet build
```

No dependencies required—this is a pure contract library.

## Testing

Tests are in `Arronix.Abstractions.Tests`:

```bash
cd ../Arronix.Abstractions.Tests
dotnet test
```

## License

GNU GPL v3 - See [LICENSE.md](../../LICENSE.md)

Portions derived from Sonarr v5, © 2010–2025 the Sonarr developers.
