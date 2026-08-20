# Arronix.Abstractions Implementation Summary

> **Historical record of the 0.1.0 delivery. Not a description of the assembly today.**
>
> This document reports what was delivered when `Arronix.Abstractions` was first created. The assembly has
> changed substantially since — 0.2.0 added twelve infrastructure areas, 0.3.0 added five platform areas, and
> 0.4.0 **deleted** contracts outright. Below 1.0.0 that is expected: any contract may be changed, renamed or
> deleted in a MINOR, and a contract with no implementer outside this repository is deleted rather than
> deprecated. See [What has changed since](#what-has-changed-since) at the end, and treat
> [`src/Arronix.Abstractions/README.md`](../../src/Arronix.Abstractions/README.md), `ARCHITECTURE.md` §3.2/§3.3
> and [`stability.md`](stability.md) as the current sources of truth. Nothing in this file should be cited as
> evidence that a type exists — check the source tree.

## Overview

The `Arronix.Abstractions` project was implemented as the foundational contract layer for the Arronix media platform. This library defines media-agnostic interfaces and DTOs that enable plugin-based architecture.

## What Was Delivered

### 1. Core Identity Types (`Identity/`)

Three strongly-typed identity structs that provide type safety and semantic clarity:

- **`MediaKindId`**: Identifies media types (e.g., "tv", "movies", "music")
- **`MediaItemId`**: Internal identifier for specific media items
- **`ReleaseId`**: Identifier for downloadable releases

All identity types support implicit conversion to/from their underlying types for convenience.

### 2. Media Abstractions (`Media/`)

Interfaces for media kind management and external ID resolution:

- **`IMediaKind`**: Plugin identity, capabilities, supported identifiers, and naming tokens
- **`IMediaIdResolver`**: Maps external IDs (TVDB, TMDB, IMDb) to internal `MediaItemId`s

### 3. Parsing & Quality (`Parsing/`, `Quality/`)

Interfaces for release parsing and quality evaluation:

- **`IReleaseParser`**: Extracts structured information from release titles
- **`IQualityModel`**: Evaluates quality, determines upgrades, and applies cutoff policies

### 4. Import & Naming (`Import/`, `Naming/`)

Interfaces for file import and library organization:

- **`IImportPipeline`**: Validates and imports downloaded media files
- **`IRenamePolicy`**: Generates file names from templates with token resolution
- **`ILibraryLayout`**: Defines folder structure for library organization

### 5. Provider Interfaces (`Providers/`)

Abstractions for external service integrations.

**The three contracts delivered here — `IIndexerProvider`, `IMetadataProvider` and `IDownloadClientAdapter` —
no longer exist.** They were deleted in 0.4.0 along with their DTOs (`IndexerCapabilities`,
`MetadataSearchResult`, `MetadataItem`, `DownloadStatus`) and the host-side `StableProviderBridge` that
projected between the old and new vocabularies. There was no `[Obsolete]` period, no shim and no dual
contract, because nothing had ever shipped against them.

What exists today is one spine, **`IProvider`**, and five families — `IIndexer`, `IDownloader`, `INotifier`,
`ICataloger<TItem>` (external authorities returning a media-owned shape) and `ICurator<TItem>` (external
lists proposing that same shape). See `ARCHITECTURE.md` §6 and the 0.4.0
entry in [`stability.md`](stability.md) for the full rename table.

### 6. Scheduling (`Scheduling/`)

Interfaces for background job management:

- **`IScheduledJob`**: Background task contract with priority and concurrency control
- **`IBackgroundTaskRegistry`**: Job registration and triggering

### 7. Data Transfer Objects (`DTOs/`)

Comprehensive set of records for data exchange:

**Release & Matching:**
- `ReleaseListing`: Raw downloadable listing returned by an indexer
- `ParsedRelease`: Temporary compatibility projection for legacy media engines
- `MatchDecision`: Result of matching releases to media items
- `ImportDecision`: Result of import validation

**Quality & Policy:**
- `QualityTier`: Temporary compatibility rung for legacy media engines
- `CutoffPolicy`: Quality upgrade cutoff rules
- `Language`: Language codes and names

**Library Organization:**
- `LibraryPathSpec`: Folder structure specification
- `NamingToken`: Template token definition

### 8. Health & Errors (`Health/`)

Types for system health monitoring and error reporting:

- **`HealthCheck`**: Health check result with status, severity, and remediation hints
- **`HealthStatus`**: Enum for health states (Healthy, Degraded, Unhealthy)
- **`HealthSeverity`**: Enum for issue severity (Info, Warning, Error, Critical)
- **`CoreErrorCode`**: Comprehensive enum of system error codes (60+ values)

### 9. Documentation

- **XML Documentation**: Complete XML docs on all public APIs
- **Stability Policy** (`docs/contracts/stability.md`): versioning and breaking-change guidance. (The deprecation
  policy this delivery wrote has since been **deleted**; `[Obsolete]` is not used below 1.0.0.)
- **Architecture Update** (`ARCHITECTURE.md`): Implementation status and component details

### 10. Tests (`Arronix.Abstractions.Tests/`)

Contract tests covering:
- Identity type behavior (conversions, equality)
- DTO functionality (quality comparison, cutoff logic)
- Health check factory methods
- 26 passing tests with 100% success rate

## Technical Details

### Project Configuration

- **Target Framework**: .NET 8.0 *(as delivered; the platform targets `net10.0` today)*
- **Language Features**: C# latest with nullable reference types enabled
- **Version**: 0.1.0 (semantic versioning) *(as delivered; `<Version>` is 0.8.0 today)*
- **Assembly**: `Arronix.Abstractions.dll`

### Key Design Decisions

1. **Media-Agnostic**: No TV or movie-specific types; all concepts are generic
2. **Strongly Typed IDs**: Record structs prevent ID mixing and improve type safety
3. **Async by Default**: All I/O operations use `Task<T>` for modern async/await
4. **Immutable DTOs**: Records provide value equality and immutability
5. **Comprehensive Docs**: Every public member has XML documentation

### Building and Testing

```bash
# Build abstractions
cd src/Arronix.Abstractions
dotnet build

# Run tests
cd src/Arronix.Abstractions.Tests
dotnet test
```

Both projects build cleanly with zero warnings or errors.

## Integration with Solution

`Arronix.Abstractions` and `Arronix.Abstractions.Tests` live in **`/Arronix.sln`**, together with every other
`Arronix.*` project. They are **not** members of `src/Sonarr.sln`.

This delivery originally added them to `src/Sonarr.sln`; that was reversed. The legacy solution is frozen, and a
clean-sheet contract assembly does not belong in it — see the extraction plan §7.3 ("Add to `/Arronix.sln` —
**not** `src/Sonarr.sln`") and invariant 5 in `.github/copilot-instructions.md`.

## Next Steps

With the abstractions layer complete, the following can now be implemented:

1. **Arronix.Host**: Runtime that loads plugins and orchestrates operations
2. **Plugin Loader**: Discovers and validates `plugin.json` manifests
3. **TV Plugin**: First concrete implementation using these abstractions
4. **Policy Engine**: Executes parsing, matching, quality, and import pipelines
5. **Storage Layer**: Implements `IMediaStore` for polymorphic persistence

## Compliance with Requirements

✅ **Create project & namespace**: `Arronix.Abstractions` created and configured  
✅ **Define interfaces/records**: All 11 interfaces and 13+ records implemented  
✅ **Add XML docs & invariants**: Complete documentation on all public APIs  
✅ **Add contract tests**: 26 tests covering core functionality  
✅ **Write stability policy**: Comprehensive versioning and compatibility doc  
✅ **Core builds independently**: No references to Sonarr or media-specific types  
✅ **Public interfaces cover responsibilities**: Parsing, quality, import, naming, providers, scheduling all covered  
✅ **Contract tests pass**: All 26 tests passing  
✅ **Architecture document updated**: Implementation status tracked in ARCHITECTURE.md

## Files Created

### Source Files (29 files)
- 3 identity types
- 11 interface files
- 10 DTO files
- 3 health/error files
- 1 project file
- 1 global usings file

### Test Files (9 files)
- 8 test class files
- 1 test project file

### Documentation (2 files)
- API stability policy
- Architecture update

**Total**: 40 new files, ~5,300 lines of code and documentation

---

## What has changed since

Verified against the source tree, not against this document. The point of the list is that **a delivery report
is not a contract**: below 1.0.0 the assembly is free to change, and it has.

| Since 0.1.0 | What happened |
|---|---|
| Target framework | `net8.0` → `net10.0` |
| Assembly `<Version>` | 0.1.0 → **0.8.0**; first-party manifests require `>=0.8 <0.9` |
| Solution membership | Removed from `src/Sonarr.sln`; `/Arronix.sln` is the only home |
| Provider contracts | `IIndexerProvider`, `IMetadataProvider`, `IDownloadClientAdapter`, their DTOs and `StableProviderBridge` **deleted outright** in 0.4.0; replaced by `IProvider` + five families |
| Contract stability | The whole assembly is one pre-1.0 surface; no per-type experimental tier or compiler opt-in remains |
| Deprecation policy | **Deleted.** `[Obsolete]` is not used below 1.0.0; a contract with no implementer outside this repository is deleted, not deprecated |
| Plugin contract ranges | The loader accepts a plugin when the installed contract version satisfies its declared range; first-party manifests currently use `>=0.8 <0.9` |
| Media and quality model | Typed item/target/release contracts and format-owned representation replaced the 0.7 quality-axes experiment; legacy `ParsedRelease` and `QualityTier` are removal-only scaffolding |
