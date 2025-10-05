# Arronix.Abstractions

Core abstractions and contracts for the Arronix media platform.

## Overview

`Arronix.Abstractions` defines the stable contract layer that enables media-agnostic plugin development for Arronix. This library contains no implementation code—only interfaces, DTOs, and type definitions that plugins and the host runtime use to communicate.

## Purpose

- **Decouple plugins from host**: Plugins depend only on abstractions, not the host implementation
- **Enable versioning**: Semantic versioning with compatibility ranges
- **Prevent coupling**: No media-specific types (TV, Movies) in the contract layer
- **Enforce contracts**: Interface definitions act as compile-time contracts

## Package Contents

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

### Provider Interfaces
- `IMetadataProvider` - External metadata sources
- `IIndexerProvider` - Release indexers
- `IDownloadClientAdapter` - Download client integration

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

## Version: 0.1.0

This is the initial stable release. All APIs follow semantic versioning guarantees.

## Usage

### For Plugin Developers

Reference this package in your plugin:

```xml
<PackageReference Include="Arronix.Abstractions" Version="0.1.0" />
```

Declare your supported version range in `plugin.json`:

```json
{
  "contracts": {
    "arronix": ">=0.1 <1.0"
  }
}
```

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
