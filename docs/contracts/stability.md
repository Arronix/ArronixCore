# Arronix.Abstractions API Stability Policy

## Version: 0.1.0

This document defines the stability guarantees and versioning policies for the `Arronix.Abstractions` contract layer.

## Semantic Versioning

`Arronix.Abstractions` follows [Semantic Versioning 2.0.0](https://semver.org/):

- **MAJOR** version increments indicate breaking changes to public APIs
- **MINOR** version increments indicate new functionality added in a backwards-compatible manner
- **PATCH** version increments indicate backwards-compatible bug fixes

## Contract Compatibility

Plugins declare their compatible `Arronix.Abstractions` version range using npm/yarn-style semver ranges in their `plugin.json` manifest:

```json
{
  "contracts": {
    "arronix": ">=0.1 <1.0"
  }
}
```

The host runtime will reject plugin loading if the installed `Arronix.Abstractions` version does not satisfy the plugin's declared range.

## API Stability Levels

### Stable APIs

APIs marked without any special attributes are considered **stable** and follow strict semantic versioning guarantees:

- **Breaking changes** (signature changes, removals) only occur at MAJOR version boundaries
- **Additions** (new methods, properties, optional parameters) occur at MINOR version boundaries
- **Bug fixes** that don't change signatures occur at PATCH version boundaries

Current stable APIs include:
- All identity types (`MediaKindId`, `MediaItemId`, `ReleaseId`)
- Core interfaces (`IMediaKind`, `IMediaIdResolver`, `IReleaseParser`, etc.)
- DTOs and enums

### Experimental APIs

APIs that are still evolving may be marked with an `[Experimental]` attribute (to be implemented). These APIs:

- May change signature or behavior at any time, even in MINOR or PATCH releases
- Will be promoted to stable status before 1.0.0 release
- Should not be used by external plugins until stabilized

Currently, there are **no experimental APIs** in version 0.1.0.

## Deprecation Policy

When a stable API needs to be removed or changed in a breaking way:

1. **Deprecation announcement**: The API is marked with `[Obsolete]` attribute with a message indicating the replacement and timeline
2. **Deprecation period**: The deprecated API remains functional for at least one MINOR version cycle
3. **Removal**: The deprecated API is removed in the next MAJOR version

Example:
```csharp
[Obsolete("Use NewMethod instead. This will be removed in version 2.0.0")]
public void OldMethod() { }
```

## Additive Changes

The following changes are considered **non-breaking** and may occur in MINOR releases:

- Adding new interfaces
- Adding new methods to existing interfaces (with default implementations where possible)
- Adding new properties to existing interfaces (with default implementations where possible)
- Adding new optional parameters to existing methods
- Adding new DTOs, records, or enums
- Adding new enum values (where appropriate)
- Adding new attributes for metadata

## Breaking Changes

The following changes are considered **breaking** and only occur in MAJOR releases:

- Removing or renaming public types, interfaces, methods, or properties
- Changing method signatures (parameter types, return types)
- Removing enum values
- Changing the behavior of existing methods in incompatible ways
- Changing from nullable to non-nullable or vice versa

## Version History

### 0.1.0 (Initial Release)

**Released**: [Current]

Initial stable release of core abstractions including:

- **Identity**: `MediaKindId`, `MediaItemId`, `ReleaseId`
- **Media**: `IMediaKind`, `IMediaIdResolver`
- **Parsing**: `IReleaseParser`
- **Quality**: `IQualityModel`
- **Import**: `IImportPipeline`
- **Naming**: `IRenamePolicy`, `ILibraryLayout`
- **Providers**: `IMetadataProvider`, `IIndexerProvider`, `IDownloadClientAdapter`
- **Scheduling**: `IScheduledJob`, `IBackgroundTaskRegistry`
- **DTOs**: `ReleaseCandidate`, `ParsedRelease`, `MatchDecision`, `ImportDecision`, `LibraryPathSpec`, `NamingToken`, `QualityTier`, `Language`, `CutoffPolicy`
- **Health**: `HealthCheck`, `HealthStatus`, `HealthSeverity`, `CoreErrorCode`

All APIs in this release are stable.

## Plugin Development Guidelines

### Version Range Selection

When developing plugins:

- **For stable plugins**: Use a range that allows MINOR and PATCH updates: `">=0.1 <1.0"`
- **For plugins tracking latest**: Use a more permissive range: `">=0.1 <2.0"`
- **For plugins requiring specific features**: Use a minimum version: `">=0.2 <1.0"`

### Handling Breaking Changes

When `Arronix.Abstractions` releases a new MAJOR version:

1. Review the changelog for breaking changes
2. Update plugin code to accommodate changes
3. Update the `contracts.arronix` range in `plugin.json`
4. Test thoroughly against the new version
5. Release a new plugin version

## Testing Compatibility

The host runtime provides tools to test plugin compatibility:

- **Contract validation**: Automatic validation during plugin load
- **Version checking**: Runtime checks for compatible version ranges
- **Interface completeness**: Validation that declared capabilities have corresponding implementations

## Questions or Feedback

For questions about API stability or to report compatibility issues:

- Open an issue in the [ArronixCore repository](https://github.com/Arronix/ArronixCore)
- Tag with `contracts` and `api-stability` labels
