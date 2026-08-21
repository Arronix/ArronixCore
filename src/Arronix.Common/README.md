# Arronix.Common

Host-side platform infrastructure for the Arronix media platform.

## Overview

`Arronix.Common` is the implementation half of the platform foundation. It supplies the infrastructure every
core component needs and none of them should re-implement: filesystem access, outbound HTTP, caching,
throttling, process launch and management, hosting identity, serialization, redaction and telemetry
composition.

It is the counterpart to `Arronix.Abstractions`, which holds the contracts. `Arronix.Common` depends on
`Arronix.Abstractions`; the reverse dependency does not exist and must never be introduced.

## What this library is not

- **Not a contract library.** Anything a plugin needs is an interface in `Arronix.Abstractions`, implemented
  here.
- **Never referenced by a plugin.** Plugins reference `Arronix.Abstractions` only. That topology is what makes
  contract-range negotiation meaningful and capability gating enforceable.
- **Not media-specific.** No product name, no domain model, no persisted entity. Host identity — application
  name, instance name, process name, file-name prefixes — arrives through one injected options type,
  `HostIdentityOptions`.
- **No persistence.** No ORM, no data access. Storage is a separate core component.
- **No inbound HTTP server, no authentication, no API surface, no self-updater.**
- **Names no concrete logging or telemetry framework in its public surface.** Sinks sit behind seams; the
  logging binding lives in its own companion project.

## Package Contents

Organized by folder, one charter each:

| Folder | Charter |
| --- | --- |
| `Archives/` | Zip and tar.gz extraction and creation for backup, update and signed plugin packages. |
| `Caching/` | TTL, sliding and bulk-refresh cache implementations, all driven by an injected `TimeProvider`. |
| `Collections/` | The small set of LINQ helpers with no in-box equivalent. |
| `Composition/` | The single explicit dependency-injection registration entrypoint. |
| `Configuration/` | Only the options this library itself binds, including host identity. |
| `Diagnostics/` | Failure capture, exception enrichment, event de-duplication and progress reporting. |
| `Diagnostics/Redaction/` | The rule-driven secret-redaction engine, auditable in isolation. |
| `Diagnostics/Telemetry/` | Event construction, enrichment and fan-out to registered sinks. Owns zero remote sinks. |
| `FileSystem/` | Host filesystem, capability-enforcing decorator, verified transfer, path algebra, guards and browsing. |
| `Hashing/` | Content and identity hashing, kept apart from cryptographic use. |
| `Hosting/` | Where the process is running and as what: paths, version, OS and runtime identity, PID file. |
| `Http/` | The outbound spine: gateway, scoped decorator, request template, user agent. |
| `Http/Proxy/` | Proxy configuration resolution and web-proxy construction. |
| `Http/Transport/` | Sockets transport and Happy Eyeballs, isolated so they can be removed as a unit. |
| `Naming/` | Diacritic folding and filesystem-safe token sanitization and truncation. |
| `Net/` | Host and address classification. |
| `Processes/` | Process launch and capture split from process management, plus browser launching. |
| `Security/` | Server certificate material handling. |
| `Serialization/` | One canonical `JsonSerializerOptions`, one serializer, one source-generated context. |
| `Serialization/Converters/` | The converter set, auditable in one place. |
| `Text/` | Pure text utilities with no network, path or serialization coupling. |
| `Threading/` | Debounce, per-key locking and task helpers. |
| `Throttling/` | Keyed rate limiting, with its own options and state store. |
| `Time/` | Date and time helpers bound to an injected `TimeProvider`. |
| `Xml/` | Namespace-agnostic feed traversal, plus the one in-box workaround still needed. |

## Version: 0.1.0

Initial release. This assembly is host-internal: it carries no stability guarantee to plugin authors, because
plugins never reference it.

## Dependencies

One project reference and eight packages, all Microsoft-owned, none appearing in the public API surface:

- `Arronix.Abstractions` (project reference — the only one)
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Options.DataAnnotations`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Http`
- `System.Threading.RateLimiting`
- `System.IO.Hashing`

JSON is `System.Text.Json` only, in-box on `net11.0` and deliberately absent from central package management.

Package versions are managed centrally in `Directory.Packages.props` at the repository root; every
`PackageReference` here is version-less.

## Usage

### For Host Developers

Reference the project and register the platform services once, from the composition root:

```xml
<ProjectReference Include="..\Arronix.Common\Arronix.Common.csproj" />
```

Registration is explicit, not convention-scanned.

### For Plugin Developers

Do not reference this library. Reference `Arronix.Abstractions` and declare a contract range in
`plugin.json`. Everything a plugin legitimately needs is injected through a contract interface, scoped to the
capabilities the plugin was granted.

## Documentation

- [API Stability Policy](../../docs/contracts/stability.md) - Versioning and compatibility
- [Extraction Plan](../../docs/arronix-common-extraction-plan.md) - Scope, dispositions and work packages
- [Architecture](../../ARCHITECTURE.md) - Overall system design

## Building

```bash
dotnet build
```

`TreatWarningsAsErrors` is on, the analysis level is `6.0-all` and documentation generation is enabled, so
every analyzer and nullability diagnostic is a build break.

## Testing

Tests are in `Arronix.Common.Tests`:

```bash
cd ../Arronix.Common.Tests
dotnet test
```

Anything taking a clock takes `TimeProvider`; tests advance time with a fake provider and never sleep.

## License

GNU GPL v3 - See [LICENSE.md](../../LICENSE.md)

Portions derived from Sonarr v5, © 2010–2025 the Sonarr developers.
