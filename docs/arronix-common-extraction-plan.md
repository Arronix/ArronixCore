# Arronix.Common — Delivery Plan (v0.1.0)

> **Status:** Plan. No code has been written. This document is the direct input to the implementation phase.
>
> **Target:** `src/Arronix.Common` — root namespace and assembly `Arronix.Common`, `net10.0`, version `0.1.0`.
>
> **Source material:** `/Users/adam/Code/GitHub/Arronix/NzbDrone.Common` (a standalone, already-modernized
> extraction of Sonarr's `NzbDrone.Common`; namespaces `Sonarr.Common.*`, System.Text.Json only).
> This delivery is a **clean-room reimplementation**, not a copy-paste rename.

---

## How the three proposals were resolved

Three independent proposals were produced — a **minimal-core** stance, a **plugin-seam** stance, and a
**pragmatic-migration** stance (the last two are represented respectively by the architect proposal and by the
per-slice surveyor dispositions). This plan does not average them. Where they disagree, one position was chosen
and the reasoning is recorded on one line, both here and inline in the disposition table.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Is `IDiskProvider` one 48-member contract or a split? | Surveyor: one contract. Architect: narrow `IFileSystem` in Abstractions + full `IHostFileSystem` in Common. | **Architect** | Handing plugins `SetEveryonePermissions`, `GetMounts`, `EmptyFolder` and arbitrary-path `DeleteFolder` is a §11 least-privilege violation dressed as a contract. |
| 2 | Is `IAppFolderInfo` one `IAppPaths` contract or a split? | Surveyor: one. Architect: `IPluginPaths` (Abstractions, pre-scoped) + `IAppPaths` (Common, host-only). | **Architect** | Exposing `AppDataFolder` to plugins hands every plugin the host's config and database paths. |
| 3 | `IProcessProvider` — one contract or split by privilege? | Surveyor: one contract. Architect: `IProcessRunner` (gated) + `IProcessManager` (Common-only). | **Architect on the split, minimal-core on the timing** | Plugins must never be able to kill host processes; but `IProcessRunner` has zero real callers today, so it lands as a Common-internal interface (Tier B) and is promoted only when a caller exists. |
| 4 | `UserAgentParser` | Surveyor: CORE, in a new `Http.Inbound` namespace. Architect: out of Common entirely. | **Architect** | Inbound API-caller attribution has no plugin audience and no place in an outbound HTTP stack; inventing an `.Inbound` namespace inside `Http/` is the smell. |
| 5 | `ReflectionExtensions` | Surveyor: CORE (plugin loader needs it). Architect: DROP. | **Architect** | Type discovery must go through per-plugin `AssemblyLoadContext` in the Plugin Loader; a global type catalog in Common is exactly the §4 shortcut to forbid. |
| 6 | `HashUtil.AnonymousToken` | Surveyor: PLUGIN (relocate). Architect: DROP (rewrite). | **Architect** | `MachineName + UserName` over a 32-bit CRC is PII-derived and dictionary-reversible; the telemetry sink should mint a persisted random `Guid.CreateVersion7()` — new code, not a move. |
| 7 | `LifecycleEventAttribute` | Surveyor: CONTRACT. Architect: DROP. | **Architect** | Empty marker, zero references anywhere in `ArronixCore/src`; a clean-room implementation does not import speculation. |
| 8 | `AuthOptions` / `ServerOptions` | Surveyor: CORE. Architect: out of Common (→ `Arronix.Host`). | **Architect** | Common hosts no HTTP server and no authentication; a shared foundation must not define the auth shape of a server it does not run. |
| 9 | Does NLog live in Arronix.Common? | Surveyor: yes (layouts + bootstrapper in Common). Architect: no — separate `Arronix.Common.Logging.NLog`. | **Architect on the split; minimal-core on scope** | §10 requires that no component name a concrete sink; but with no `Arronix.Host` yet, nothing consumes the layouts, so the NLog project is **deferred** and v0.1.0 ships only `Microsoft.Extensions.Logging.Abstractions`. |
| 10 | How much goes into `Arronix.Abstractions` now? | Architect: ~45 new types at once. Minimal-core: as little as possible. | **Minimal-core, using the architect's own mitigation** | The architect's stated biggest risk is contract lock-in against zero implementers; his own remedy ("promote by evidence, build outside-in") is adopted as the governing rule — see [Tiering](#the-three-tier-promotion-rule). |
| 11 | Does Common ship a generic policy-pipeline runner (`Pipelines/`)? | Architect: yes. | **No — deferred** | The Policy Engine is a distinct core component (§3) with no consumer today; shipping a runner with no chain is speculation, and speculation is what the promotion rule exists to stop. |
| 12 | Does Common ship the §9 token template engine? | Architect: yes (`TokenTemplate`, `TokenFormatter`). | **Partially — sanitizer and folding only** | §9 validation is against `IMediaKind.NamingTokens`, which is the Policy Engine's job; a formatter without validation is exactly what `Expansive` was. |
| 13 | `HashConverter` persisted-id migration risk | Surveyor: real risk, needs a shim. Architect: does not apply. | **Architect, with a caveat** | Arronix has no persisted synthetic ids yet, so `XxHash3` can be adopted freely — but the risk is real for the v4→v5 migration tool and is recorded there. |
| 14 | `HttpUriConverter` | Surveyor: CORE (follows `HttpUri`). Architect: DROP. | **Architect** | It dies with `HttpUri`; `System.Uri` has a built-in converter, and this removes the only place `Serialization` reaches into `Http`. |

---

## 1. Purpose & scope

### What Arronix.Common is

`Arronix.Common` is the **host-side platform implementation assembly** for Arronix. It supplies the
infrastructure that every core component (`Arronix.Host`, Plugin Loader, Scheduler, Policy Engine, Storage
Layer, HTTP API, Health & Telemetry) needs and that none of them should re-implement:

- a filesystem abstraction with verified transfer, path algebra and mount/permission seams;
- an outbound HTTP stack (gateway, request templates, transport, proxy, Happy Eyeballs, TLS policy);
- caching, keyed rate limiting, debouncing and per-key locking;
- process launch and management, host paths, OS and runtime identity, PID file, archives;
- a single canonical System.Text.Json configuration;
- log/telemetry redaction, unhandled-exception capture, telemetry emission and sink fan-out;
- text, path, XML, networking and time primitives that survive .NET 10.

### What Arronix.Common is **not**

- **It is not a contract library.** Contracts live in `Arronix.Abstractions`. Anything a plugin needs is an
  interface there, implemented here.
- **It is never referenced by a plugin.** Plugins reference `Arronix.Abstractions` only. That topology is what
  makes §8 contract-range negotiation (`contracts.arronix: ">=0.1 <1.0"`) meaningful and what makes §11
  capability gating enforceable — a plugin bound to an implementation assembly has no declared range and no
  compatibility check.
- **It contains nothing media-, TV- or Sonarr-specific.** No product name, no vendor endpoint, no TV concept,
  no persisted domain model. Host identity (application name, instance name, process name, file-name prefixes)
  arrives through one injected options type, `HostIdentityOptions`.
- **It contains no persistence.** No ORM, no EF Core (hard contributor rule in
  `.github/copilot-instructions.md`), no `IMediaStore`. Storage is a separate core component.
- **It contains no inbound HTTP server, no authentication, no API surface, no self-updater.**
- **It names no concrete logging or telemetry framework in its public surface.** NLog, Sentry and OTLP are all
  behind seams.

### Relationship to `Arronix.Abstractions`

`Arronix.Common` **depends on** `Arronix.Abstractions` — never the reverse. `Arronix.Abstractions` remains a
pure contract library with **zero package references**. This delivery makes an additive **v0.2.0** change to
Abstractions (new folders `Caching/`, `Diagnostics/`, `Errors/`, `Events/`, `FileSystem/`, `Hosting/`, `Http/`,
`Serialization/`, `Telemetry/`, `Throttling/`, plus additions to `Health/` and `Naming/`). Every new type is
marked `[Experimental]` and is governed by `docs/contracts/stability.md` — which, below 1.0.0, means the
opposite of a freeze: **any contract may be changed, renamed or deleted in a MINOR**, and a contract with no
implementer outside this repository is deleted outright. `[Obsolete]` is not used; there is no deprecation
window, no shim and no dual contract.

Arronix.Common must **complement** Abstractions, never duplicate it. Concretely: it consumes `HealthCheck`,
`HealthStatus`, `HealthSeverity` and `CoreErrorCode` verbatim and does not re-declare them; it uses the
existing `IScheduledJob` / `IBackgroundTaskRegistry` job model and does not introduce a rival
command/dispatcher model (§16: *one scheduler, one queue*).

### Relationship to the legacy `src/NzbDrone.Common`

**This delivery is purely additive.** `src/NzbDrone.Common` stays exactly where it is, keeps building, and
keeps serving the 7 direct and ~950 transitive legacy consumers via `src/Sonarr.sln`. Arronix.Common is added
to `Arronix.sln` only. Nothing in the legacy tree is edited, renamed or deleted by this work. There is no
migration of legacy call sites in v0.1.0 — the 172 `IDiskProvider` dependents and the ~1,100 `StringExtensions`
call sites are legacy concerns and Arronix.Common owes them nothing.

### The three-tier promotion rule

The single largest risk in this delivery is **designing contracts against zero real implementers** — inventing
a shape to satisfy a requirement nobody has yet stated, and then discovering the real requirement wants a
different shape. Every new interface is therefore assigned a tier, and the tier is a hard gate.

The rule is *not* justified by irreversibility. Nothing has ever shipped against an Arronix contract, so
promotion is trivially reversible: a promoted type with no implementer outside this repository can be deleted,
and the compiler names every call site. The reason to hold a type back is that **a seam invented before its
second implementer exists is usually the wrong seam** — a real implementation is the cheapest evidence
available about what the shape should be, and it is much cheaper to get the shape right the first time than
to churn it in public. Build it, use it, then promote what survived contact.

| Tier | Where it lives in v0.1.0 | Promotion condition |
|------|--------------------------|---------------------|
| **A — Promoted** | `Arronix.Abstractions` v0.2.0, marked `[Experimental]` | Has a demonstrated second implementer, or is explicitly promised by ARCHITECTURE.md, or the type physically must cross the plugin boundary. |
| **B — Held** | `Arronix.Common`, as a public-but-host-side interface | Promote to Abstractions when a second implementer or a real plugin consumer appears. Costs nothing to promote later, and a shape that a working implementation has already exercised is a shape worth publishing. |
| **C — Deferred** | Does not exist in v0.1.0 | Owned by a different core component (Plugin Loader, Policy Engine, Storage, Host). Listed in [§9 Deferred](#9-deferred). |

Implementation is **outside-in**: build `Arronix.Common` against Tier A + Tier B, and let the working
implementation prove the shape before anything is promoted. `[Experimental]` must be implemented as part of this
delivery — `docs/contracts/stability.md` already anticipates it ("to be implemented") — and the Plugin Loader
must refuse to satisfy an experimental contract for a plugin whose declared range extends past the current
MINOR. That is what makes a change to a published contract *visible* rather than silent: every consuming file
carries a greppable `#pragma`, and no plugin can claim compatibility with a MINOR line whose shapes it has not
seen.

---

## 2. Target structure

```text
src/Arronix.Common/
├── Arronix.Common.csproj
├── GlobalUsings.cs
├── README.md
├── Archives/
├── Caching/
├── Collections/
├── Composition/
├── Configuration/
├── Diagnostics/
│   ├── Redaction/
│   └── Telemetry/
├── FileSystem/
├── Hashing/
├── Hosting/
├── Http/
│   ├── Proxy/
│   └── Transport/
├── Naming/
├── Net/
├── Processes/
├── Security/
├── Serialization/
│   └── Converters/
├── Text/
├── Threading/
├── Throttling/
├── Time/
└── Xml/
```

| Folder / namespace | Charter (one line) |
|--------------------|--------------------|
| `Archives/` | Zip and tar.gz extraction and creation for backup, self-update and signed plugin packages — one dependency (the filesystem), no plugin audience. |
| `Caching/` | Implementations of the `ICache` family: TTL, sliding and bulk-refresh caches, all driven by an injected `TimeProvider`. |
| `Collections/` | The three LINQ helpers that survive .NET 10, kept out of `Text/` so they are not lost in a string grab-bag. |
| `Composition/` | The single explicit DI registration entrypoint (`AddArronixCommon`) — deliberately thin, because it replaces the convention scanning that §11 forbids. |
| `Configuration/` | Only the options `Arronix.Common` itself binds; `HostIdentityOptions` is the one place the product name enters the platform. |
| `Diagnostics/` | Process-level failure capture, exception enrichment, event de-duplication and progress reporting — sink-agnostic by construction. |
| `Diagnostics/Redaction/` | The rule-driven secret-redaction engine — a §11 security control, in its own folder so it is auditable in isolation. |
| `Diagnostics/Telemetry/` | Event construction, enrichment and fan-out to registered sinks; owns **zero** concrete remote sinks. This folder is the §10 seam made physical. |
| `FileSystem/` | The load-bearing abstraction: host filesystem, scoped (capability-enforcing) decorator, verified transfer, path algebra, guards, limits and browsing. |
| `Hashing/` | Content and identity hashing over `System.IO.Hashing` and SHA-256 — kept apart from `Security/` so cryptographic and non-cryptographic uses never blur. |
| `Hosting/` | "Where am I running and as what": app and plugin paths, version/OS/runtime identity, PID file, startup options and the fatal-startup exception. |
| `Http/` | The outbound spine — gateway, scoped decorator, request template, user agent. Inbound HTTP is absent by design. |
| `Http/Proxy/` | Proxy configuration resolution and `IWebProxy` construction, swapped as a unit by the host config layer. |
| `Http/Transport/` | The layer below the gateway: sockets transport and Happy Eyeballs — isolated so it can be deleted wholesale when `dotnet/runtime#26177` ships. |
| `Naming/` | The media-agnostic half of §9: diacritic folding and filesystem-safe token sanitization and truncation. Template validation is the Policy Engine's job and is deliberately absent. |
| `Net/` | All host and address classification in one place, instead of scattered across `Text/` and `Http/` as the legacy code left it. |
| `Processes/` | Process launch and capture (gated) split from process management (host-trusted), plus browser launching. |
| `Security/` | Server certificate material handling — moved out of `Http/` because it configures inbound TLS and is also consumed through the TLS policy contract. |
| `Serialization/` | One canonical `JsonSerializerOptions`, one serializer implementation, one source-generated context for trimming. |
| `Serialization/Converters/` | The converter set, auditable in one place; it should stay at exactly one entry now that `Version`, `TimeSpan` and `Uri` are all BCL-native on net10. |
| `Text/` | Pure text utilities with no network, path or serialization coupling. |
| `Threading/` | Debounce, per-key locking and task helpers — replaces the legacy `TPL/` namespace name, which was never about the Task Parallel Library. |
| `Throttling/` | Keyed rate limiting: a shared-across-plugins policy concern (§12) with its own options and its own state store, not an ambient concurrency primitive. |
| `Time/` | Date/time helpers bound to an injected `TimeProvider` so scheduler behavior is deterministically testable. |
| `Xml/` | Namespace-agnostic feed traversal for indexer plugins, plus the one BCL workaround that still has no replacement. |

**Companion project (deferred, see §9):** `src/Arronix.Common.Logging.NLog` — the NLog binding
(redacting CLEF/text layouts, redacting file target, `AddArronixLogging`). It is where the NLog package
reference lives, so that `Arronix.Common`'s own surface never names a logging framework.

**Test project:** `src/Arronix.Common.Tests` — plural, matching `Arronix.Abstractions.Tests`.

---

## 3. Disposition table

Every concept surveyed appears exactly once, grouped by disposition. Legacy names are given in their
`Sonarr.Common.*` form.

### 3.1 CONTRACT — interface into `Arronix.Abstractions` v0.2.0, implementation in `Arronix.Common`

All of these are **Tier A** and ship marked `[Experimental]`.

| Concept | Legacy name | Disposition | New name | Notes |
|---|---|---|---|---|
| Outbound HTTP facade | `Http.IHttpClient` | CONTRACT | `Arronix.Abstractions.Http.IHttpGateway` | Every provider plugin needs outbound HTTP; the loader withholds registration from plugins with no network-implying capability. Async-only — the 7 sync-over-async members do not survive. |
| Outbound request | `Http.HttpRequest` | CONTRACT | `Http.OutboundHttpRequest` | Renamed away from the `Microsoft.AspNetCore.Http.HttpRequest` collision. A re-sendable spec object stays justified because `HttpRequestMessage` is single-use. |
| Outbound response | `Http.HttpResponse` | CONTRACT | `Http.OutboundHttpResponse` | `Set-Cookie` parsing moves to `System.Net.Http.Headers` from the hand-rolled regex. |
| Typed outbound response | `Http.HttpResponse<T>` | CONTRACT | `Http.OutboundHttpResponse<T>` | Drop `where T : new()` and the `?? new T()` fallback — Newtonsoft-era workarounds that mask a null payload as an empty object. |
| Header collection | `Http.HttpHeader` | CONTRACT | `Http.HttpHeaderCollection` | Reimplement over `Dictionary<string,string[]>` with `OrdinalIgnoreCase`; `NameValueCollection` and the base-hiding `GetEnumerator` both go. |
| HTTP interception | `Http.IHttpRequestInterceptor` | CONTRACT | `Http.IOutboundHttpInterceptor` | The declared extension point for provider-specific workarounds — two implementers exist today (CloudFlare, Torcache). Async with `CancellationToken`. |
| HTTP failure | `Http.HttpException` | CONTRACT | `Http.HttpGatewayException` | Derive from `HttpRequestException` so BCL-aware catch blocks and resilience pipelines still work. |
| HTTP 429 | `Http.TooManyRequestsException` | CONTRACT | `Http.HttpRateLimitedException` | Indexer plugins catch this to back off. Reimplement the `Retry-After` parse over `RetryConditionHeaderValue`, fixing the ambient-culture date parse. |
| Interstitial detection | `Http.UnexpectedHtmlContentException` | CONTRACT | `Http.UnexpectedHtmlResponseException` | Thrown out of typed gateway calls plugins make, so it must be catchable from plugin code. |
| TLS acceptance policy | `Http.Dispatchers.ICertificateValidationService` | CONTRACT | `Http.ICertificateValidationPolicy` | SMTP notifiers and download clients on self-signed LAN hosts need the host's TLS policy. Fix the mis-cased double negative `ShouldByPassValidationError` to `ShouldAccept`, and the file/type name mismatch. |
| Filesystem (narrowed) | `Disk.IDiskProvider` | CONTRACT (split) | `FileSystem.IFileSystem` | ~20 async members (read/probe/write/transfer). The full 48-member surface stays as `IHostFileSystem` in Common. Enforced at runtime by `ScopedFileSystem`. |
| File permissions | (extracted from `IDiskProvider`) | CONTRACT | `FileSystem.IFilePermissions` | chmod/chown/ACL split out so the plugin-facing `IFileSystem` can be narrow; implemented by platform packs. |
| Verified transfer | `Disk.IDiskTransferService` | CONTRACT | `FileSystem.IFileTransferService` | Import is a plugin capability and importing is file transfer with verification, rollback and hardlink negotiation. |
| Transfer mode | `Disk.TransferMode` | CONTRACT | `FileSystem.FileTransferMode` | Part of the transfer signature, so it travels with the contract. |
| Mount | `Disk.IMount` | CONTRACT | `FileSystem.IStorageMount` | Returned by the filesystem contract and consumed by mount health checks plugins will contribute to. |
| Mount provider | (implicit in `DiskProviderBase.GetMounts`) | CONTRACT | `FileSystem.IStorageMountProvider` | Platform packs supply Linux `/proc/mounts` and Windows `DriveInfo` probes; Common ships only the `DriveInfo` fallback. |
| Mount options | `Disk.MountOptions` | CONTRACT | `FileSystem.StorageMountOptions` | Record over `IReadOnlyDictionary`; `ContainsKey("ro")` is Linux `/proc/mounts` syntax and becomes a platform-supplied bool. |
| Destination collision | `Disk.DestinationAlreadyExistsException` | CONTRACT | `FileSystem.DestinationExistsException` | Absorbs `FileAlreadyExistsException` and gains a `Path` property. Derives from `IOException`. |
| Cross-platform path value | `Disk.OsPath` | CONTRACT | `FileSystem.PlatformPath` | No BCL equivalent — reasons about a Windows path while executing on Linux, i.e. remote path mapping, which download-client plugins own. Fix the `GetHashCode`/`Equals` casing asymmetry and the bare `new Exception` on kind mismatch. |
| Path grammar | `Disk.OsPathKind` | CONTRACT | `FileSystem.PlatformPathKind` | Public surface of `PlatformPath`. |
| Plugin-scoped paths | `EnvironmentInfo.IAppFolderInfo` | CONTRACT (split) | `Hosting.IPluginPaths` | Pre-rooted at the plugin's own directory (Data, Cache, Temp). The host-side `IAppPaths` stays in Common. `LegacyAppDataFolder` dropped. |
| OS version probe | `EnvironmentInfo.IOsVersionAdapter` | CONTRACT | `Hosting.IOsVersionProbe` | Already a plugin pattern in disguise — all six implementations live outside Common today. Owned by platform packs. |
| OS version result | `EnvironmentInfo.OsVersionModel` | CONTRACT | `Hosting.OsVersionDescriptor` | Record with a static factory performing the quote/whitespace trimming. |
| OS description | `EnvironmentInfo.IOsInfo` | CONTRACT | `Hosting.IOperatingSystemInfo` | Plugins legitimately need containerization awareness — remote-path-mapping health checks in a download-client plugin are the canonical case. |
| Runtime shape | `EnvironmentInfo.IRuntimeInfo` | CONTRACT | `Hosting.IHostRuntimeInfo` | Trimmed: `IsWindowsTray`, `IsTray`, `IsSystemdService` and `Mode` are dead and dropped; mutable `IsStarting`/`IsExiting`/`RestartPending` move to `IHostApplicationLifetime`. |
| Service lifecycle | `IServiceProvider` (Windows) | CONTRACT | `Hosting.IServiceLifecycleController` | Query status and request restart only. Two platform-pack implementers (Windows, systemd). Also resolves the collision with `System.IServiceProvider`. |
| Native library resolution | (fragment of `Composition.AssemblyLoader`) | CONTRACT | `Hosting.INativeLibraryResolver` | Replaces the hardcoded `sqlite3` → `libsqlite3.so.0` mapping without Common knowing a database engine exists. |
| Cache registry | `Cache.ICacheManager` | CONTRACT | `Caching.ICacheProvider` | 66 dependent files. Replace the `Type`-keyed string concatenation with an explicit partition plus generic owner; partitions are namespaced by plugin id. `Clear()` and `Caches` have zero consumers and are not carried. |
| Cache (non-generic) | `Cache.ICached` | CONTRACT | `Caching.ICache` | The base a provider needs to enumerate and sweep heterogeneous caches. |
| Cache (typed) | `Cache.ICached<T>` | CONTRACT | `Caching.ICache<T>` | Most widely used contract in its slice. Add an async factory overload — the sync-factory-only shape forces blocking inside HTTP paths. |
| Bulk-refresh cache | `Cache.ICachedDictionary<T>` | CONTRACT | `Caching.ISelfRefreshingCache<T>` | Distinct concept with no BCL equivalent. `ExtendTTL` renamed `Touch` — the TTL is not extended, the refresh clock is reset. |
| Rate limiting | `TPL.IRateLimitService` | CONTRACT | `Throttling.IRateLimiter` | §12 requires throttling shared across plugins hitting the same remote, which is only possible if the contract is plugin-visible. Lease-based acquisition, not fire-and-forget wait-and-pulse. |
| Domain event | `Messaging.IEvent` | CONTRACT | `Events.IDomainEvent` | 63 implementers; §15 names an internal event bus. Add a correlation id and timestamp, which §10 requires and a bare marker cannot carry. **The 63 event types themselves are the TV domain model and stay in the TV plugin — only the contract crosses.** |
| Event publish/subscribe | (`NzbDrone.Core` `EventAggregator`) | CONTRACT | `Events.IEventPublisher`, `Events.IEventHandler<T>` | Subscription is filtered so a plugin sees Core events plus its own namespace, never another plugin's (§4). |
| Platform exception root | `Exceptions.NzbDroneException` | CONTRACT | `Errors.ArronixException` | 52 dependents. Derives from `Exception`, **not** `ApplicationException`; carries a `CoreErrorCode` (§12). Drop the `params object[]` + `string.Format` constructors — a literal brace throws `FormatException` from inside an exception constructor. |
| Progress reporting | `Instrumentation.Extensions.LoggerExtensions` | CONTRACT | `Diagnostics.IProgressReporter` | Stops every plugin taking a hard NLog reference just to report progress. Do not carry the shape verbatim — it stuffs an empty-string marker via `Add`, which throws on a duplicate key. Carries the job/correlation id from the scheduler. |
| Vendor-neutral reporting | `Instrumentation.Extensions.SentryLoggerExtensions` | CONTRACT | `Telemetry.ITelemetryEmitter` | The literal §10 violation — application code names the sink at the call site. The magic `"Sentry"` property key becomes a typed fingerprint field. Only `WriteSentryWarn` is used, so the contract needs one method, not five. |
| Telemetry sink | `Instrumentation.Sentry.SentryTarget` (seam) | CONTRACT | `Telemetry.ITelemetrySink` | §10: sinks are extensible and plugins raise events via abstractions, never directly to a sink. |
| Telemetry enrichment | `Instrumentation.InitializeLogger` (seam) | CONTRACT | `Telemetry.ITelemetryEnricher` | Generalizes "attach environment facts to every event at startup"; Common ships an OS/environment enricher. |
| Telemetry filtering | `SentryTarget`'s filter lists (seam) | CONTRACT | `Telemetry.ITelemetryEventFilter` | This is what lets the logging layer stop referencing the database engine — the SQLite error-code filters become storage-contributed. |
| Redaction rules | `Instrumentation.CleanseLogMessage.CleansingRules` | CONTRACT | `Diagnostics.IRedactionRuleProvider`, `Diagnostics.RedactionRule` | 13 of the 25 hardcoded rules name specific third parties; each rule is owned by whoever knows the secret's shape. |
| Health contribution | *(absent — §10 promises it)* | CONTRACT | `Health.IHealthContributor` | **Must be added before any health work.** Reuse the existing `HealthCheck` DTO and `HealthStatus`/`HealthSeverity` enums verbatim; do not re-declare them in Common. |
| Serializer | `Serializer.Json` (facade) | CONTRACT | `Serialization.IJsonSerializer` | Guarantees Core and plugins agree on wire shape (§8) without plugins referencing a static facade in an implementation assembly. |
| Diacritic folding | `Globalization.AdditionalDiacriticsProvider` | CONTRACT | `Naming.IDiacriticFoldingProvider` | Title folding is inherently language-specific, so contributing fold pairs is a textbook plugin seam — and it is what lets `Diacritical.Net` be dropped. Common ships the eth/thorn table as the default. |

### 3.2 CORE — real platform infrastructure, lands in `Arronix.Common`

| Concept | Legacy name | Disposition | New name | Notes |
|---|---|---|---|---|
| HTTP gateway impl | `Http.HttpClient` | CORE | `Http.HttpGateway` | Cookie jar, interceptor pipeline, rate-limit gate, redirect policy. Reimplement on `IHttpClientFactory` named clients; the `RuntimeInfo.IsProduction` check becomes `IHostEnvironment`. |
| Per-plugin HTTP decorator | *(new)* | CORE | `Http.ScopedHttpGateway` | Stamps the plugin id into the rate-limit partition and User-Agent and applies a per-plugin host allow/deny list. |
| Request builder | `Http.HttpRequestBuilder` | CORE | `Http.HttpRequestBuilder` | Nothing in the BCL composes a cloneable request template with segment substitution. Replace `MemberwiseClone() as HttpRequestBuilder` with an explicit copy constructor and `ApplicationException` with `InvalidOperationException`. |
| Request template | `Http.IHttpRequestBuilderFactory` | CORE (Tier B) | `Http.IHttpRequestTemplate` | The prototype pattern behind every base-URL-scoped API client, and the exact seam that keeps `SonarrCloudRequestBuilder` out of Core. It is a template, not a factory of factories. |
| Request template impl | `Http.HttpRequestBuilderFactory` | CORE | `Http.HttpRequestTemplate` | Sealed. The protected ctor and protected `SetRootBuilder` are deleted — no subclass exists anywhere in the tree. |
| Accept header constants | `Http.HttpAccept` (remnant) | CORE | `Http.AcceptHeaders` | Only the composite `rss+xml`/`xml` value survives as a constant; everything else is `System.Net.Mime.MediaTypeNames`. |
| URI helpers | *(extracted from `HttpUri`)* | CORE | `Http.UriExtensions` | `CombinePath`, `AddQueryParams`, `ResolveSegments` over `System.Uri`. Gated on the golden tests — see [§7](#7-build--verification). |
| User agent | `Http.IUserAgentBuilder` / `UserAgentBuilder` | CORE (Tier B) | `Http.IUserAgentProvider` / `UserAgentProvider` | Outbound application identity is host infrastructure applied centrally; plugins never implement or see it. Inject `HostIdentityOptions` instead of the static `BuildInfo.AppName = "Sonarr"`. |
| Transport seam | `Http.Dispatchers.IHttpDispatcher` | CORE (Tier B) | `Http.Transport.IHttpTransport` | Swapped only by tests and the host, never by plugins — keeping it out of Abstractions keeps the stable contract small. |
| Transport impl | `Http.Dispatchers.ManagedHttpDispatcher` | CORE | `Http.Transport.SocketsHttpTransport` | Reimplement on `IHttpClientFactory` (removing the unbounded per-proxy-key client cache), inject Happy Eyeballs rather than newing it in a field initializer, and close the `NotImplementedException` gap on the `Range` header, which blocks resumable downloads. |
| Happy Eyeballs | `Http.HappyEyeballs.HappyEyeballs<TSocket>` | CORE | `Http.Transport.HappyEyeballs<TSocket>` | Verified against the installed .NET 10.0.9 ref pack: `SocketsHttpHandler` still exposes only `ConnectCallback`, with no connection-attempt-delay API. Carry near-verbatim with a delete-when-`runtime#26177`-ships note. |
| Happy Eyeballs binding | `Http.HappyEyeballs.HttpHappyEyeballs` | CORE | `Http.Transport.HappyEyeballsConnector` | Swap NLog for `ILogger`; make the 250 ms attempt delay an option rather than a private const. |
| Server certificate loading | `Http.SslCertificateLoader` | CORE | `Security.ServerCertificateLoader` | Already .NET 10-idiomatic (`CreateFromPemFile`, `X509CertificateLoader`, `SslStreamCertificateContext`). Misfiled under `Http/` — it configures the inbound listener. |
| Certificate load failure | `Http.SslCertificateLoadException` | CORE | `Security.CertificateLoadException` | Moves with the loader; carries the config-error meaning the startup health check needs. |
| Proxy configuration | `Http.Proxy.HttpProxySettings` | CORE | `Http.Proxy.ProxyConfiguration` | Make it a record so value equality replaces the string `Key`. **Security:** the current `Key` joins the cleartext proxy password into two long-lived dictionary keys — a §11 violation. Property deleted. |
| Proxy resolution | `Http.Proxy.IHttpProxySettingsProvider` | CORE (Tier B) | `Http.Proxy.IProxyConfigurationProvider` | Defined by Common, implemented by the host config layer, never by plugins (they get proxying transparently). Per-URI overload takes `System.Uri`. |
| Web proxy factory | `Http.Proxy.ICreateManagedWebProxy` / `ManagedWebProxyFactory` | CORE (Tier B) | `Http.Proxy.IWebProxyFactory` / `WebProxyFactory` | Verb-phrase interface names are a legacy idiom that reads badly beside the noun-phrase interfaces in Abstractions. Cache keyed by the record, not a password-bearing string; fix `GetProxyUri` returning null for unknown types and caching a null `IWebProxy` under a live key. |
| Proxy protocol | `Http.Proxy.ProxyType` | CORE | `Http.Proxy.ProxyProtocol` | These are protocols, not types. Order the members however reads best — the only place that integer is persisted is a legacy *-arr* config database, which Arronix will never read. If the value is ever persisted, persist the **name**. |
| Host filesystem (full) | `Disk.IDiskProvider` (full surface) | CORE (Tier B) | `FileSystem.IHostFileSystem` | The 48-member surface, never registered into a plugin scope. |
| Filesystem base | `Disk.DiskProviderBase` | CORE | `FileSystem.HostFileSystemBase` | Probe filename comes from `FileSystemOptions`, not the literal `"sonarr_write_test.txt"`; the blocking `Thread.Sleep(3000)` retry becomes async backoff; chmod/chown/ACL split out into `IFilePermissions`. |
| Capability-enforcing decorator | *(new)* | CORE | `FileSystem.ScopedFileSystem` | Rejects any path outside the plugin's granted roots. Lives beside the thing it enforces on. |
| Transfer impl | `Disk.DiskTransferService` | CORE | `FileSystem.FileTransferService` | The ignore list (`.nfs`, `debug.log`, `.socket`), the 100 MB threshold and the `.backup~` suffix become bound options — Core must not know what a torrent client's `debug.log` is. |
| Mount fallback | `Disk.DriveInfoMount` | CORE | `FileSystem.DriveInfoStorageMount` | The cross-platform fallback used when no platform pack supplies a richer probe. |
| Path validation scope | `Disk.PathValidationType` | CORE | `FileSystem.PathValidationScope` | Stays Common-side; the narrowed `IFileSystem` validates internally rather than exposing the scope to plugins. |
| Path limits | `Disk.LongPathSupport` | CORE | `FileSystem.PathLimits` | Keep the limit probing (load-bearing for filename truncation); **delete `Enable()` outright** — both `AppContext` switches are .NET Framework/Mono-era and inert no-ops on net10. |
| System path catalog | `Disk.SpecialFolders` + `Disk.SystemFolders` | CORE (merged) | `FileSystem.SystemPathCatalog` | Keep the filtering engine, but the list must be options-bound: Core hard-coding QNAP's `.@__thumb` and Synology's `@eadir`/`#recycle` names three storage vendors inside a media-agnostic foundation. |
| Browse entry | `Disk.FileSystemModel` | CORE | `FileSystem.FileSystemEntry` | Immutable record with non-nullable `Name`/`Path`. |
| Browse entry kind | `Disk.FileSystemEntityType` | CORE | `FileSystem.FileSystemEntryKind` | The `Parent` member is never assigned or compared anywhere and is dropped. |
| Browse result | `Disk.FileSystemResult` | CORE | `FileSystem.FileSystemListing` | Record with `IReadOnlyList` members. |
| Folder browser | `Disk.IFileSystemLookupService` / `FileSystemLookupService` | CORE (Tier B) | `FileSystem.IFileSystemBrowser` / `FileSystemBrowser` | Serves the host's folder-picker endpoint only. Plugins have no reason to browse arbitrary user directories — exposing this would violate §11. |
| Path comparer | `PathEqualityComparer` | CORE | `FileSystem.PathComparer` | Fix the nullability contract violation against `IEqualityComparer<string>` and the `GetHashCode` `.Normalize()` call, which throws on paths containing unpaired surrogates. |
| Path algebra | `Extensions.PathExtensions` (algebra half) | CORE | `FileSystem.PathExtensions` | **Mandatory rename:** `GetDirectoryName` returns the LEAF directory name, the exact opposite of `System.IO.Path.GetDirectoryName`, and becomes `GetLeafName`. |
| Path guard | `EnsureThat.EnsureStringExtensions.IsValidPath` | CORE | `FileSystem.PathGuard.ThrowIfInvalid` | The single guard that survives the BCL — 51 call sites — as a static with `[CallerArgumentExpression]`. |
| Stream helper | `Extensions.StreamExtensions` | CORE | `FileSystem.StreamExtensions` | Reimplement over `Stream.CopyTo` and add `ToBytesAsync(CancellationToken)`; the synchronous-only form is a scalability trap for indexer response handling. |
| Stable hashing | `Crypto.HashConverter` | CORE | `Hashing.StableHash` | Three fixes: `Encoding.Default` makes the hash platform/culture-dependent (must be UTF-8); a single SHA-1 instance under a lock serializes all callers; SHA-1 is the wrong primitive for a non-security id. Arronix has no persisted ids yet, so `XxHash3` can be adopted freely. |
| File hashing | `Crypto.IHashProvider` / `HashProvider` | CORE (Tier B) | `Hashing.IFileHasher` / `FileHasher` | `ValueTask<byte[]> ComputeAsync(path, CancellationToken)`; drop the algorithm name from the interface — `ComputeMd5` leaks the implementation into the contract. Held at Tier B: no caller exists yet. Default to `XxHash128`; offer SHA-256 where integrity matters. |
| Host paths | `EnvironmentInfo.IAppFolderInfo` (host half) | CORE (Tier B) | `Hosting.IAppPaths` | Host use only. `LegacyAppDataFolder` dropped. |
| Host paths impl | `EnvironmentInfo.AppFolderInfo` | CORE | `Hosting.AppPaths` | Product name from `HostIdentityOptions` instead of two compiled-in `"Sonarr"`/`"NzbDrone"` literals; `StartUpFolder` uses `AppContext.BaseDirectory` so single-file publish works. |
| Plugin paths impl | *(new)* | CORE | `Hosting.PluginPaths` | One instance per plugin, pre-rooted at the plugin's own directory. Implements `IPluginPaths`. |
| App-data initialization | `EnvironmentInfo.IAppFolderFactory` / `AppFolderFactory` | CORE (Tier B) | `Hosting.IAppDataInitializer` / `AppDataInitializer` | Keep only ensure-exists + writability + permissions. Every migration branch (folder move, db rename, pid cleanup, mono XDG hack) belongs to the one-shot migration tool named in §13. It initializes; it does not construct. |
| Version info | `EnvironmentInfo.BuildInfo` | CORE | `Hosting.AppVersionInfo` | `AppName` is injected; the `Assembly.Location`-based `BuildDateTime` is replaced — it returns nothing under single-file publish. |
| OS info impl | `EnvironmentInfo.OsInfo` | CORE | `Hosting.OperatingSystemInfo` | Keep only the instance half (distro identity via the probe chain, Docker/Podman detection). Delete the static `Is*` shims — the static constructor already delegates to `OperatingSystem.IsWindows()`, and calling those directly also satisfies the platform-compatibility analyzer. |
| Runtime info impl | `EnvironmentInfo.RuntimeInfo` | CORE | `Hosting.HostRuntimeInfo` | Tray detection, the Sonarr process-name constants and `InternalIsOfficialBuild`'s encoding of Sonarr's release-versioning scheme all go; environment classification defers to `IHostEnvironment` instead of sniffing for `nunit`/`jetbrain`/`resharper`. |
| Startup options | `EnvironmentInfo.IStartupContext` | CORE | `Hosting.HostStartupOptions` | A bound options record. The Windows-installer predicates move to the platform pack. |
| PID file | `Processes.IProvidePidFile` / `PidFileProvider` | CORE (Tier B) | `Hosting.IPidFileWriter` / `PidFileWriter` | Standard daemon behavior once the filename derives from `HostIdentityOptions` instead of the literal `"sonarr.pid"`. Name reads as a noun. |
| Fatal startup failure | `Exceptions.SonarrStartupException` | CORE | `Hosting.HostStartupException` | Stays in Common, not Abstractions — plugins do not abort host startup. Product name from `HostIdentityOptions`. Collapse six constructors to two. |
| Process launch | `Processes.IProcessProvider` (launch half) | CORE (Tier B) | `Processes.IProcessRunner` | Held at Tier B on the architect's own advice: an allow-listed process launcher may be a capability that should not exist, and there is no caller today. |
| Process launch impl | `Processes.ProcessProvider` | CORE | `Processes.ProcessRunner` | `SONARR_PROCESS_NAME`/`SONARR_CONSOLE_PROCESS_NAME` come from `HostIdentityOptions`. Drop the legacy `StringDictionary` from the signatures; make capture async so it stops blocking a pool thread. |
| Process management | `Processes.IProcessProvider` (manage half) | CORE (Tier B) | `Processes.IProcessManager` / `ProcessManager` | Find/kill/exists — Common-only, host-trusted. Plugins must never be able to kill host processes. `SetPriority` and `GetCurrentProcessPriority` have no callers and are dropped. |
| Process result | `Processes.ProcessOutput` | CORE | `Processes.ProcessResult` | Record with `IReadOnlyList` and cached Standard/Error views rather than re-LINQing on every property access. |
| Process line | `Processes.ProcessOutputLine` | CORE | `Processes.ProcessOutputLine` | Immutable record; timestamp from `TimeProvider` so tests are deterministic. |
| Process stream | `Processes.ProcessOutputLevel` | CORE | `Processes.ProcessStreamKind` | "Level" misleadingly suggests log severity when it identifies a stream. |
| Process snapshot | `Model.ProcessInfo` | CORE | `Processes.ProcessDescriptor` | Record. The `Model` namespace — a junk drawer holding exactly one type — is deleted. |
| Executable naming | `Extensions.PathExtensions.ProcessNameToExe` | CORE | `Processes.ProcessNameExtensions.ToExecutableName` | Genuinely cross-platform but a process concern, not a path concern. Use `OperatingSystem.IsWindows()`. |
| Browser launch | `IProcessProvider.OpenDefaultBrowser` | CORE (Tier B) | `Processes.IBrowserLauncher` / `BrowserLauncher` | Not a process concern; extracted to its own seam. |
| Archives | `IArchiveService` / `ArchiveService` | CORE (Tier B) | `Archives.IArchiveService` / `ArchiveService` | Backup, self-update and signed plugin package extraction are host functions. Drop SharpZipLib — which also fixes an **unguarded zip-slip traversal** in `ExtractZip` that `ZipFile.ExtractToDirectory` validates for you. |
| Cache provider impl | `Cache.CacheManager` | CORE | `Caching.CacheProvider` | Genuine infrastructure over `ConcurrentDictionary`. |
| Expiring cache | `Cache.Cached<T>` | CORE | `Caching.ExpiringCache<T>` | `MemoryCache` would collide with `Microsoft.Extensions.Caching.Memory`, and `IMemoryCache` offers neither `ClearExpired` nor `Values` enumeration, both of which are used. Take `TimeProvider`; make `Find` return `T?` — it currently returns `default` under nullable enable, which is a nullability lie. |
| Self-refreshing cache | `Cache.CachedDictionary<T>` | CORE | `Caching.SelfRefreshingCache<T>` | Fix real defects: the fetch delegate runs under no lock so TTL expiry can stampede N concurrent refreshes; `Find` returns `default` for a non-nullable `TValue`; nullable fields are stored in non-nullable slots. |
| Debounce | `TPL.Debouncer` | CORE | `Threading.Debouncer` | Reimplement properly: it locks on the mutable field it also mutates, mixes volatile fields with lock-guarded access, is not `IDisposable` (every instance leaks a `Timer`), and `Resume()` can drive the pause counter negative. Take `TimeProvider`. |
| Keyed locking | `TPL.LockByIdPool` | CORE | `Threading.KeyedLockPool<TKey>` | Currently hardcoded to `int` (leaking a database row-id assumption into a threading primitive), takes a global lock on every acquisition, never evicts, and hands out bare object monitors so nothing can be awaited. Generic, async-capable, evicting, over `System.Threading.Lock`. |
| Task helpers | `TPL.TaskExtensions` | CORE | `Threading.TaskExtensions` | `LogExceptions` → `LogFailures`; the static `NzbDroneLogger` acquisition becomes an explicit `ILogger` parameter — a shared library reaching into a concrete sink is what §10 forbids. |
| Rate limiter impl | `TPL.RateLimitService` | CORE | `Throttling.KeyedRateLimiter` | Keep the capability, discard the implementation: `Thread.Sleep` blocks a pool thread, it takes a concrete `NLog.Logger`, and it abuses the cache manager as a process-global dictionary holder — a cache that can evict is the wrong home for throttling state. **Must not be named `RateLimiter`** (collides with `System.Threading.RateLimiting`). |
| Rate limit partition | *(new)* | CORE | `Throttling.RateLimitPartitionKey` | Explicit partition key type so a plugin id can be composed into the remote-host key. |
| Redaction engine | `Instrumentation.CleanseLogMessage` | CORE | `Diagnostics.Redaction.LogRedactor` | Rule-driven service with `[GeneratedRegex]` rather than a static class with 25 `RegexOptions.Compiled` instances built at type-init. **Fix:** masking handles only IPv4, so IPv6 addresses leak untouched. |
| Generic redaction rules | `CleansingRules` (generic subset) | CORE | `Diagnostics.Redaction.RedactionRuleCatalog` | Only the querystring credential, home-path and IP-masking rules stay in Core. |
| Unhandled exceptions | `Instrumentation.GlobalExceptionHandlers` | CORE | `Diagnostics.UnhandledExceptionRegistrar` | Make `Register()` idempotent (it currently double-subscribes), route everything through `ILogger` instead of `Console.WriteLine` so redaction applies, extract the noise filters to contributed `IUnhandledExceptionFilter` instances, and delete the dead branch matching `Microsoft.AspNet.SignalR` — a framework this codebase does not use. |
| Exception filters | *(extracted)* | CORE (Tier B) | `Diagnostics.IUnhandledExceptionFilter` | The `HttpListener`/SignalR knowledge belongs to whoever owns those subsystems, not to Common. |
| Event debounce | `Instrumentation.Sentry.SentryDebounce` | CORE | `Diagnostics.EventDebouncer` | **Misfiled:** despite name and folder it contains zero Sentry references and is a generic keyed TTL gate any sink, health reporter or notification path wants. Debouncing in Core also means a misbehaving plugin cannot flood a sink. TTL from options, clock from `TimeProvider`. |
| Exception enrichment | `Extensions.ExceptionExtensions` | CORE | `Diagnostics.ExceptionExtensions` | Keep the string/int overloads; move the `HttpResponse`/`HttpUri` overloads into `Http/` so the primitives layer stops depending upward on Http. |
| Progress impl | *(new)* | CORE | `Diagnostics.ProgressReporter` | Implements `IProgressReporter` over `ILogger` with correlation-id scope. |
| Telemetry emitter | *(new)* | CORE | `Diagnostics.Telemetry.TelemetryEmitter` | Owns event construction; the call site names a severity and a fingerprint, never a vendor. |
| Sink registry | *(new)* | CORE | `Diagnostics.Telemetry.TelemetrySinkRegistry` | Collects `IEnumerable<ITelemetrySink>` after verifying the plugin declared the `telemetry-sink` capability. |
| Enrichment pipeline | *(new)* | CORE | `Diagnostics.Telemetry.TelemetryEnrichmentPipeline`, `OsEnvironmentEnricher` | Enrichment + redaction + debounce happen before fan-out, so every sink gets the same redacted event. |
| JSON defaults | `Serializer.Json` (options half) | CORE | `Serialization.JsonDefaults` | Four corrections: `WriteIndented=true` as a global default inflates every payload and is already worked around by cloning options in two places; the `where T : new()` constraint blocks the records Abstractions is made of; the cast-to-object polymorphism trick becomes `[JsonPolymorphic]`; add a serializer context so the host can be trimmed. |
| Source-gen context | *(new)* | CORE | `Serialization.ArronixJsonSerializerContext` | Trimming/AOT safety. |
| Serializer impl | *(new)* | CORE | `Serialization.SystemTextJsonSerializer` | Implements `IJsonSerializer` over `JsonDefaults`. |
| Deep clone | `Extensions.ObjectExtensions.JsonClone` | CORE | `Serialization.JsonCloner.Clone<T>` | A silently-expensive deep clone should not be one dot away from every expression. Drop the pointless `class, new()` constraint. |
| Serialization helpers | `Serializer.JsonExtensions` | CORE | `Serialization.JsonSerializationExtensions` | Thin convenience, preferably static calls rather than extensions on `object` (which pollute IntelliSense for every type and box value types). `ToCompactJson` is dead and must not be ported; any compact variant must reuse one cached options instance rather than constructing a new one per call. |
| UTC date converter | `Serializer.DateTimeToUtcConverter` | CORE | `Serialization.Converters.UtcDateTimeJsonConverter` | Verified **not** redundant on .NET 10.0.9: built-in STJ writes Local as an offset and Unspecified with no marker, and preserves sub-second fractions, so it neither normalizes to UTC nor truncates. Fix the culture-sensitive `DateTime.Parse`. Prefer `DateTimeOffset` in all new DTOs. |
| Diacritic folding impl | `StringExtensions.RemoveDiacritics` | CORE | `Naming.TextFolding` + `Naming.DefaultDiacriticFoldingProvider` | Reimplement over `FormD` normalization so the `Diacritical.Net` dependency disappears; the three-character eth/thorn table becomes the default provider. Registration moves out of a static constructor into DI composition. |
| Token sanitization | *(new; concept from `Expansive`)* | CORE | `Naming.TokenSanitizer` | Illegal filesystem characters per platform, path-component and total-path truncation (driven by `PathLimits`), reserved names, collision handling. Template *validation* is deliberately absent — that is the Policy Engine's. |
| LINQ helpers | `Extensions.EnumerableExtensions` | CORE | `Collections.EnumerableExtensions` | Keep only `AddIfNotNull`, `None`, `ToDictionaryIgnoreDuplicates`. Drop `IntersectBy`/`ExceptBy` — they now shadow BCL methods of the same name with different shapes, a live readability hazard — and `DropLast`/`NotAll` as dead. The file is named `IEnumerableExtensions.cs` while the class is `EnumerableExtensions`; fix on the way over. |
| String survivors | `Extensions.StringExtensions` (survivors) | CORE | `Text.StringExtensions` | Survives as `FirstCharToLower`/`Upper`, `SplitCamelCase`, `ToUrlSlug`, `TrimSuffix` (was `TrimEnd(string)`), `WrapInQuotes`. **Delete the static constructor** — registering a global diacritic provider as a side effect of first touching any string extension is invisible initialization-order coupling. |
| Edit distance | `Extensions.LevenstheinExtensions` | CORE | `Text.StringDistance.Levenshtein` | Kill the misspelling; expose over `ReadOnlySpan<char>` rather than as an extension hanging off every string. |
| Byte sizes | `Extensions.NumberExtensions` | CORE | `Text.ByteSizeExtensions` | **Correctness fix:** the code divides by 1024 but labels the result KB/MB/GB — switch to KiB/MiB/GiB and rename `Megabytes`/`Gigabytes` to match the arithmetic. |
| Nullable parsing | `Extensions.TryParseExtensions` | CORE | `Text.NullableParseExtensions` | The nullable-returning shape composes with LINQ in a way `TryParse` cannot. **Fix `ParseDouble`**, which does `Replace(',', '.')` before parsing invariantly and so mangles `"1,234.5"` into `"1.234.5"`. |
| IP classification | `Extensions.IPAddressExtensions` | CORE | `Net.IpAddressExtensions` | The BCL still has no private-range or CGNAT predicate; this is a genuine networking primitive. File renamed to match the type. |
| Host/address text | `StringExtensions.IsValidIpAddress` / `ToUrlHost` | CORE | `Net.HostAddressExtensions` | Networking concerns misfiled under text; `Net/` owns all host and address handling. |
| URL validation | `Extensions.UrlExtensions` | CORE | `Net.UriValidationExtensions` | Thin but load-bearing in settings validation, and the leading/trailing-space rejection is a real behavior the BCL lacks — `Uri.TryCreate` silently trims. |
| Endpoint formatting | `Extensions.DnsEndPointExtensions` | CORE | `Net.DnsEndPointExtensions` | Trivial but real, consumed by the Happy Eyeballs connector; the slice's only use of the new C# extension-member syntax, so it sets the house style. |
| Date/time | `Extensions.DateTimeExtensions` | CORE | `Time.DateTimeExtensions` | Only `WithoutTicks`/`WithTicksFrom` (DB round-trip fidelity) survive as-is; `InNext`/`InLast` are rebound to an injected `TimeProvider`. `EpochTime`/`Before`/`After`/`Between`/`InNextDays`/`InLastDays` drop as BCL-trivial or unused. |
| XML traversal | `Extensions.XmlExtensions` | CORE | `Xml.XElementExtensions` | Fix `FindDecendants` → `FindDescendants` and annotate `TryGetAttributeValue` `[NotNullWhen(true)] out string?`, which currently assigns null to a non-nullable out. |
| UTF-8 string writer | `Utf8StringWriter` | CORE | `Xml.Utf8StringWriter` | Still the only way to make `XmlWriter` over a string emit `encoding="utf-8"`; `StringWriter.Encoding` remains hardwired to `UnicodeEncoding`. |
| Host identity | `Options.AppOptions` (identity half) | CORE | `Configuration.HostIdentityOptions` | **The most important CORE type in the delivery** — the single injection point that de-brands the User-Agent, PID file, log file names, folder names, service name and startup exception text. `LaunchBrowser` stays; `Theme`, `ProfilerEnabled`, `ProfilerPosition` are web-UI concerns for the Host (the latter two have no consumer anywhere). |
| Logging options | `Options.LogOptions` | CORE | `Configuration.LoggingOptions` | Level, rotation, size and console format are platform config. `FilterSentryEvents` and `AnalyticsEnabled` configure **one concrete sink** and move to the Sentry plugin's own options section; `Sql`/`DbEnabled` move with the storage layer. |
| Console format | `Instrumentation.ConsoleLogFormat` | CORE | `Configuration.ConsoleLogFormat` | Already correctly named. Consider adding a `Json` member mapping to the BCL `JsonConsoleFormatter` so operators who do not need CLEF can shed a package. |
| Filesystem options | *(new)* | CORE | `Configuration.FileSystemOptions` | Probe filename, transfer ignore list, size threshold, temp suffix, vendor folder names. |
| HTTP options | *(new)* | CORE | `Configuration.HttpClientOptions` | Timeouts, redirect limit, connection-attempt delay, default headers. |
| Proxy options | *(new)* | CORE | `Configuration.ProxyOptions` | Binds `ProxyConfiguration` from host config. |
| Redaction options | *(new)* | CORE | `Configuration.RedactionOptions` | Redaction is **on by default**; the legacy "redact only in production" gate is backwards for a security control. |
| DI entrypoint | `Composition.Extensions` (replacement) | CORE | `Composition.ArronixCommonServiceCollectionExtensions.AddArronixCommon` | Explicit registration on plain `Microsoft.Extensions.DependencyInjection`. Deliberately thin — this replaces the convention scanning that is the §11 violation being removed. |

### 3.3 PLUGIN — does not go into `Arronix.Common`

| Concept | Legacy name | Disposition | New home | Notes |
|---|---|---|---|---|
| JSON-RPC envelope | `Http.JsonRpcRequestBuilder` | PLUGIN | `Arronix.Plugin.Support.Rpc.Json` | Nothing in a media-agnostic core speaks JSON-RPC; every consumer is a named download client or notification provider. Fix the 8-hex-char request id while porting. |
| JSON-RPC response | `Http.JsonRpcResponse<T>` | PLUGIN | `Arronix.Plugin.Support.Rpc.Json` | Model `Error` as a typed record (code/message/data) rather than a raw `JsonElement`. |
| XML-RPC envelope | `Http.XmlRpcRequestBuilder` | PLUGIN | `Arronix.Plugin.Support.Rpc.Xml` | A legacy wire protocol needed by exactly two download clients (rTorrent, Aria2). Its static `NzbDroneLogger` resolution is precisely the pattern this rewrite exists to remove. |
| OAuth 1.0a request | `OAuth.OAuthRequest` | PLUGIN | `Arronix.Plugin.Support.OAuth1` | Putting a credentialed request signer in the shared foundation hands it to every loaded plugin regardless of manifest — a §11 least-privilege violation. Vendored public-domain code; carry `OAuth/LICENSE`. **Honest recommendation: drop the Twitter provider and this with it.** |
| OAuth 1.0a signing | `OAuth.OAuthTools` | PLUGIN | `Arronix.Plugin.Support.OAuth1` | Three defects recorded: the 4-arg `GetSignature` silently drops `signatureBase` and signs the consumer secret; HMAC-SHA1 trips CA5350; nonces come from a seeded `System.Random` rather than the CSPRNG. |
| OAuth flow kind | `OAuth.OAuthRequestType` | PLUGIN | `Arronix.Plugin.Support.OAuth1` | Drop the unreachable `ClientAuthentication` member. |
| OAuth signature method | `OAuth.OAuthSignatureMethod` | PLUGIN | `Arronix.Plugin.Support.OAuth1` | Two of three members are fiction — `GetSignature` throws `NotImplementedException` for both. Collapse or delete. |
| OAuth signature treatment | `OAuth.OAuthSignatureTreatment` | PLUGIN | `Arronix.Plugin.Support.OAuth1` | Effectively a constant; every factory hard-codes `Escaped`. Inline it. |
| RFC3986 encoding | `StringExtensions.EncodeRFC3986` | PLUGIN | `Arronix.Plugin.Support.OAuth1` | The comment says "From Twitterizer" and the only call site is the Twitter proxy — this is OAuth 1.0a signature-base encoding. |
| Windows service control | `IServiceProvider` (impl) | PLUGIN (platform pack) | `Arronix.Platform.Windows.Hosting.WindowsServiceController` | A single-OS service-manager integration has no place in a cross-platform foundation. Two defects to fix in transit: the class is declared **inside the interface body**, and `IsServiceRunning`'s `!= Stopped \|\| == StopPending \|\| ...` makes every disjunct after the first unreachable. |
| Windows service impl | `ServiceProvider` (nested) | PLUGIN (platform pack) | `Arronix.Platform.Windows` | `sc.exe` install/uninstall/DACL work is packaging and leaves the runtime entirely. |
| Octal unescaping | `StringExtensions.FromOctalString` | PLUGIN (platform pack) | `Arronix.Platform.Linux` | Sole production call site unescapes `/proc/mounts` field separators — a Linux mount-table parsing detail. |
| Title-matching distance | `LevenstheinExtensions.LevenshteinDistanceClean` | PLUGIN | `Arronix.Plugin.Media.Tv` | The punctuation stripping and the magic 1/3/3 cost weights encode a title-matching heuristic; §4.2 puts matching policies in plugins. Builds on the CORE `StringDistance` primitive. |
| Season/episode sentinel | `Extensions.NullableExtensions.NonNegative` | PLUGIN | `Arronix.Plugin.Media.Tv` | A six-line decoder for the TV `-1` sentinel; re-express inline at the two call sites. |
| Update sandbox paths | `PathExtensions.GetUpdateSandboxFolder` (+8 more) | PLUGIN | `Arronix.Host.Updates` | Nine methods whose entire content is Sonarr-branded update layout. Rooted at `IAppPaths.TempFolder`. |
| Update configuration | `Options.UpdateOptions` | PLUGIN | `Arronix.Host.Updates` | `Branch` names a Sonarr release branch, `Mechanism` parses into Sonarr's enum, `ScriptPath` points at Sonarr's script. Core exposes an `IUpdateChannel` seam; the update extension owns its options section. |
| Vendor endpoints | `Cloud.ISonarrCloudRequestBuilder` | PLUGIN | *(per-plugin base URIs)* | Literally named after the product and hardcoding two vendor endpoints, one of which is a TV-only metadata proxy. Core exposes only the request-template abstraction plus named-endpoint options. |
| Vendor endpoints impl | `Cloud.SonarrCloudRequestBuilder` | PLUGIN | *(dies with its interface)* | Nothing is reusable: the endpoints are baked into the constructor with no configuration hook, so even a self-hosted deployment cannot redirect them. |
| Provider redaction rules | `CleanseLogMessage.CleansingRules` (13 entries) | PLUGIN | *(per-plugin `IRedactionRuleProvider`)* | TorrentLeech/IPTorrents/BTN/announce-key with indexer plugins; Sabnzbd/NzbGet/uTorrent/Deluge/qBittorrent with download-client plugins; Discord/Telegram/Notifiarr with notification plugins; Plex with the media-server plugin. **A plugin that stops shipping stops leaking its rule into every install.** |
| Sentry sink | `Instrumentation.Sentry.SentryTarget` | PLUGIN | `Arronix.Plugin.Telemetry.Sentry` | The clearest plugin in the survey. Its SQLite error filters (which drag `System.Data.SQLite` into the logging layer) and its Jackett/openflixr message filters become contributed `ITelemetryEventFilter` instances. **Carry the fix, not the bug:** `Dispose(bool)` guards with `if (_disposed)` instead of `if (!_disposed)`, so the SDK is never disposed or flushed on shutdown. |
| Sentry redaction adapter | `Instrumentation.Sentry.SentryCleanser` | PLUGIN | `Arronix.Plugin.Telemetry.Sentry` | Both public methods swallow all exceptions and return the **unredacted** event on failure — a redaction control that fails open must instead fail closed and drop the event. |
| Sentry scope enrichment | `Instrumentation.InitializeLogger` | PLUGIN | `Arronix.Plugin.Telemetry.Sentry` | A vendor-specific shim with no independent life; the name is also wrong since it initializes nothing about logging. |

### 3.4 DROP — redundant against .NET 10, dead, or leaving Common entirely

See [§5](#5-dropped-in-favor-of-the-bcl) for the BCL replacement and call-site impact of each.

| Concept | Legacy name | Disposition | Replaced by | Notes |
|---|---|---|---|---|
| Header bridge | `Http.WebHeaderCollectionExtensions` | DROP | direct `HttpHeaders` enumeration | Exists only to bridge modern `HttpHeaders` back to `NameValueCollection`, which is being removed; its `;`-join is lossy for repeated headers such as `Set-Cookie`. |
| URI value type | `Http.HttpUri` | DROP | `System.Uri` + `Http.UriExtensions` | 265 lines reimplementing `System.Uri`, with a malformed IPv6 host character class and a hard-coded `en-AU` culture on the `GeneratedRegex`. **Highest-risk drop in the delivery** — gated on porting `HttpUriFixture` as golden tests first. |
| Accept value object | `Http.HttpAccept` | DROP | `System.Net.Mime.MediaTypeNames` | A value object over string that buys nothing; only the composite rss/xml value survives as `AcceptHeaders`. Widest blast radius in its slice (41 files), so land it as a mechanical call-site change. |
| Multipart DTO | `Http.HttpFormData` | DROP | `MultipartFormDataContent` / `FormUrlEncodedContent` | Feeds a hand-written multipart writer that uses `DateTime.Now.Ticks` as its boundary and carries its own boundary-collision TODO. |
| TLS exception | `Http.TlsFailureException` | DROP | `HttpRequestException` + `HttpRequestError.SecureConnectionError` | Dead — constructed nowhere. Built on `WebRequest` (SYSLIB0014). |
| Basic-auth marker | `Http.BasicNetworkCredential` | DROP | `AuthenticationHeaderValue` | Encoding intent as an empty marker subclass is a legacy trick. Note the upstream ISO-8859-1 → UTF-8 change is a real behavior change to test against non-ASCII credentials. |
| Inbound UA parsing | `Http.UserAgentParser` | DROP (from Common) | *(→ `Arronix.Host` API layer)* | Inbound API-caller attribution has nothing to do with outbound HTTP and no plugin audience. |
| Name/value pair | `OAuth.WebParameter` | DROP | `KeyValuePair<string,string>` | A two-property class reimplementing `KeyValuePair`. Does not enter Common under any disposition. |
| Parameter collection | `OAuth.WebParameterCollection` | DROP | `List<KeyValuePair<,>>` + LINQ | 157 lines of hand-rolled collection that LINQ replaces line for line; still carries dead `#if WINRT` branches. |
| Duplicate collision | `Disk.FileAlreadyExistsException` | DROP | `DestinationExistsException` | Duplicates it and wrongly derives from `Exception` rather than `IOException`, so `catch (IOException)` misses it. |
| Relative-path failure | `Disk.NotParentException` | DROP | `Path.GetRelativePath` + `ArgumentException` | One throw site, inheriting the legacy base being killed. |
| Dead DTO | `Disk.RelativeFileSystemModel` | DROP | — | Dead: the identifier appears exactly once in the repository, on its own declaration. |
| Platform enum | `EnvironmentInfo.Os` | DROP | `OperatingSystem.IsWindows()` etc. | A hand-rolled duplicate of BCL platform detection that additionally conflates Linux and BSD. |
| Runtime version seam | `EnvironmentInfo.IPlatformInfo` | DROP | `Environment.Version` | A mockable wrapper around one static property; its only consumers are a telemetry tag and a test-only gate. |
| Runtime version impl | `EnvironmentInfo.PlatformInfo` | DROP | `RuntimeInformation.FrameworkDescription` | Vestige of the Mono-vs-.NET era; `PlatformName` returns the literal `".NET"`. |
| Runtime mode | `EnvironmentInfo.RuntimeMode` | DROP | `WindowsServiceHelpers.IsWindowsService()` + `Environment.UserInteractive` | A tray host is a separate executable in the Arronix model, so the third state disappears and the remaining two are BCL one-liners. |
| Argv parser | `EnvironmentInfo.StartupContext` | DROP | `Microsoft.Extensions.Configuration.CommandLine` | A bespoke tokenizer with no validation, no help generation, a throwing `Args.Add` on duplicate keys and case-folding bugs. |
| Console presentation | `IConsoleService` | DROP | host CLI command handler | Console presentation belongs to the executable, not a shared library. |
| Console presentation impl | `ConsoleService` | DROP | `System.CommandLine` generated help | Hand-maintained help text with the product name baked in; its `ServiceProvider.SERVICE_NAME` reference is the only thing forcing Common to depend on the Windows-service type that is itself leaving. `IsConsoleAvailable` is dead and does not detect a redirected console anyway. |
| DI facade | `IServiceFactory` | DROP | `IServiceProvider` + `GetRequiredService<T>` | A pass-through wrapper adding nothing but a mockable layer; `GetImplementations` has no callers at all. |
| DI facade impl | `ServiceFactory` | DROP | `TryAddEnumerable` at the composition root | Its one non-trivial line dedupes `BuildAll` by type `FullName` — a band-aid over duplicate DI registrations. |
| Base64 sugar | `Extensions.Base64Extensions` | DROP | `Convert.ToBase64String` | The single surviving call site becomes a direct BCL call; the `long` overload is dead. |
| Dictionary helpers | `Extensions.DictionaryExtensions` | DROP | `ToDictionary` / indexer assignment | `Merge`, both `SelectDictionary` overloads and the `ICollection` `Add` shim have zero call sites anywhere. |
| Extension index | `Extensions.PathExtensions.GetPathExtension` | DROP | `Path.GetExtension` | One call site; the only behavioral difference (dotfiles) is not relied on. |
| Path trimming | `Extensions.PathExtensions.CleanPath` | DROP | — | Zero call sites, and it silently corrupts UNC and rooted paths by trimming segments. |
| Capture arithmetic | `Extensions.RegexExtensions` | DROP | `capture.Index + capture.Length` | One arithmetic expression with a single call site; inline it. |
| Resource reader | `Extensions.ResourceExtensions` | DROP | `GetManifestResourceStream` + `Stream.ReadExactly` | Dead, and buggy: never disposes the stream, never null-checks, and relies on a single `Read()` returning the full length. |
| Whitespace predicates | `StringExtensions.IsNullOrWhiteSpace` / `IsNotNullOrWhiteSpace` | DROP | `string.IsNullOrWhiteSpace` | 305 combined legacy call sites, but the BCL method has carried `[NotNullWhen(false)]` since .NET Core 3.0. Keeping it forces every Common consumer into a `using` just to express a BCL predicate. |
| Case-insensitive compares | `StringExtensions.StartsWith`/`EndsWith`/`Equals`/`ContainsIgnoreCase` | DROP | `StringComparison` overloads | Dropping them fixes a latent bug: all four hardcode `InvariantCultureIgnoreCase` where the call sites (paths, headers, protocol tokens) mean `Ordinal`. |
| Hex conversion | `StringExtensions.HexToByteArray` / `ToHexString` | DROP | `Convert.FromHexString` / `ToHexString` | Exact span-based equivalents including the uppercase convention. |
| Misc string sugar | `StringExtensions.NullSafe`/`Inject`/`Join`/`CleanSpaces`/`Replace`/`Reverse` | DROP | `string.Format`, `string.Join`, spans | All six BCL-trivial or unused; `Inject`'s last consumer is the EnsureThat message formatting that dies with the guard DSL. |
| Guard entrypoint | `EnsureThat.Ensure` | DROP | `ArgumentNullException.ThrowIfNull` etc. | Across the entire solution only four guard shapes are ever used and three are now BCL one-liners with `[CallerArgumentExpression]`. 18 types plus a 22-string `.resx` collapse to one domain guard. |
| Guard carrier | `EnsureThat.Param` / `Param<T>` | DROP | `[CallerArgumentExpression]` | Exists solely to transport a parameter name to the throw site, which the compiler now does for free and without the per-call allocation on `DiskProviderBase` hot paths. |
| Type guard carrier | `EnsureThat.TypeParam` | DROP | pattern matching | Unreachable: its only entry point `Ensure.ThatTypeFor` has zero call sites. |
| Logging guard factory | `EnsureThat.ExceptionFactory` | DROP | direct `throw` | A guard that writes to a global log target before throwing double-reports every argument error and makes guards non-deterministic in tests. It is also the primitives layer's only NLog dependency. |
| Expression walker | `EnsureThat.ExpressionExtensions` | DROP | `[CallerArgumentExpression]` | Builds and walks an expression tree at 51 `IsValidPath` sites just to produce a string the compiler already knows; `GetRightMostMember` can also return null and NRE on `ToPath()`. |
| Object guards | `EnsureThat.EnsureObjectExtensions` | DROP | `ArgumentNullException.ThrowIfNull` | Covers all 20 call sites with the name supplied automatically. |
| Nullable guards | `EnsureThat.EnsureNullableValueTypeExtensions` | DROP | `ArgumentNullException.ThrowIfNull` | Its `Value == null \|\| !Value.HasValue` test is redundant with itself. |
| Collection guards | `EnsureThat.EnsureCollectionExtensions` | DROP | `ThrowIfNull` + `ThrowIfZero` | Three call sites across six overloads. The `IEnumerable<T>` overload also enumerates via `Any()`, silently consuming single-pass iterators. |
| int guards | `EnsureThat.EnsureIntExtensions` | DROP | `ArgumentOutOfRangeException.ThrowIf*` | Zero call sites; every method has a named .NET 8 generic throw-helper. |
| long guards | `EnsureThat.EnsureLongExtensions` | DROP | `ArgumentOutOfRangeException.ThrowIf*` | Zero call sites; the generic helpers make the per-numeric-type duplication obsolete. |
| short guards | `EnsureThat.EnsureShortExtensions` | DROP | `ArgumentOutOfRangeException.ThrowIf*` | Zero call sites. |
| decimal guards | `EnsureThat.EnsureDecimalExtensions` | DROP | `ArgumentOutOfRangeException.ThrowIf*` | Zero call sites. |
| double guards | `EnsureThat.EnsureDoubleExtensions` | DROP | `ArgumentOutOfRangeException.ThrowIf*` | Zero call sites, and unsound for `double` because NaN fails every comparison silently. |
| DateTime guards | `EnsureThat.EnsureDateTimeExtensions` | DROP | `ArgumentOutOfRangeException.ThrowIf*` | Zero call sites for any member including `IsUtc` and `IsValid`. |
| bool guards | `EnsureThat.EnsureBoolExtensions` | DROP | explicit `throw` | Zero call sites. |
| Guid guards | `EnsureThat.EnsureGuidExtensions` | DROP | explicit `throw` | Zero call sites. |
| Type guards | `EnsureThat.EnsureTypeExtensions` | DROP | pattern matching | Unreachable; the entire class plus `TypeParam` exists only to serve itself. |
| Guard messages | `EnsureThat.Resources.ExceptionMessages` + `.resx` | DROP | framework messages | 18 of 22 strings back dead guards. The csproj ItemGroup wiring the `.resx` is already commented out — finish the job rather than restore it. |
| Template engine | `Expansive.Expansive` | DROP | `Naming.TokenSanitizer` + Policy Engine | Dead in production (only consumer is one test fixture) and superseded by §9, which requires validation against a declared token set, collision handling and sanitization that this MS-PL regex engine has no concept of. |
| Cycle exception | `Expansive.CircularReferenceException` | DROP | structured policy failure | Only thrown by `Expansive`. |
| Pattern DTO | `Expansive.PatternStyle` | DROP | — | Internal detail of the dropped engine; only ever instantiated once with a hardcoded pattern, so the configurability was never used. |
| Tree root | `Expansive.Tree<T>` | DROP | — | Adds nothing over `TreeNode<T>` beyond re-assigning `Value`. |
| Tree node | `Expansive.TreeNode<T>` | DROP | — | `CallTree` rebuilds a `List` on every access. |
| Tree node list | `Expansive.TreeNodeList<T>` | DROP | — | `public new Add` hides `List<T>.Add`, so adding through a base reference silently skips reparenting — exactly the inheritance trap not to carry forward. |
| Reflection helpers | `Reflection.ReflectionExtensions` | DROP | `CustomAttributeExtensions`; Plugin Loader ALC | `ImplementationsOf`/`FindTypeByName` belong to the Plugin Loader, which must resolve through per-plugin `AssemblyLoadContext`; `IsSimpleType`/`GetSimpleProperties` serve API schema generation, which is `Arronix.Host`. |
| Product assembly handle | `ReflectionExtensions.CoreAssembly` | DROP | `AssemblyLoadContext` | Hardcodes a product assembly name in a shared library **in a static initializer**, so touching any other member triggers a load that throws if the assembly is absent. Directly hostile to §4 plugin isolation. |
| Base32 codec | `ConvertBase32` | DROP | *(→ torrent plugin if ever needed)* | Zero references anywhere. Also unsafe: `IndexOf` returns `-1` for invalid characters and silently corrupts output. |
| CRC32 | `HashUtil.CalculateCrc` | DROP | `System.IO.Hashing.Crc32` | Not bit-identical (the hand-rolled loop is the unreflected CRC-32/MPEG-2 family), but both consumers tolerate a changed digest. |
| Install identifier | `HashUtil.AnonymousToken` | DROP | `Guid.CreateVersion7()` in the sink plugin | Deriving an install identifier from `MachineName + UserName` over a 32-bit CRC is PII-derived and reversible by dictionary attack. Nothing here should port. |
| Newtonsoft visitor stub | `Instrumentation.CleansingJsonVisitor` | DROP | — | The file is a single comment line; the type was deleted with Newtonsoft. Delete the stub rather than carrying a placeholder into a clean-room implementation. |
| Log event helpers | `Instrumentation.LogEventExtensions` | DROP | `TelemetryEvent.Fingerprint` | Dead: both methods have zero call sites in the extraction and in the legacy solution. `GetFormattedMessage` also shadows NLog's own `FormattedMessage` property. |
| Version renderer | `Instrumentation.VersionLayoutRenderer` | DROP | NLog `${assembly-version}` | Dead, and actively wrong in a plugin architecture: `Assembly.GetExecutingAssembly()` inside Common returns Common's version, not the host's, so any line it produced would misattribute. |
| Logger bootstrap | `Instrumentation.NzbDroneLogger` | DROP (from Common) | `AddArronixLogging` in the deferred NLog project | Four hardcoded `sentry.sonarr.tv` DSNs, `sonarr.txt` file names, the `SONARR__` env prefix and a static `GetLogger` service locator. It is rewritten, not renamed — and the rewrite lives in the NLog binding project, not here. `ResetAllTargets` is dead. |
| CLEF layout | `Instrumentation.CleansingClefLogLayout` | DROP (from Common) | `RedactingClefLayout` in the deferred NLog project | Two fixes to carry when it lands: the "redact only in production" gate is backwards for a security control, and redacting already-serialized JSON can corrupt the document if a replacement lands inside an escape sequence. |
| Console layout | `Instrumentation.CleansingConsoleLogLayout` | DROP (from Common) | `RedactingTextLayout` in the deferred NLog project | Collapse the two duplicated render paths, one of which appends to a possibly non-empty target then clears it. |
| Redacting file target | `Instrumentation.CleansingFileTarget` | DROP (from Common) | `RedactingFileTarget` in the deferred NLog project | Runtime retuning moves from `LogManager` type-sniffing to `IOptionsMonitor` change tokens. It already redacts unconditionally, which is correct and is what the layouts should match. |
| DI logger registration | `Instrumentation.Extensions.CompositionExtensions` | DROP | `services.AddLogging()` | Registers open-generic `ILogger<>` and gives exactly the parent-type-named-logger behavior with no container-specific code. Also the slice's only DryIoc dependency. |
| Polymorphic converter | `Serializer.DynamicJsonConverter<T>` | DROP | `[JsonPolymorphic]` + `[JsonDerivedType]` | Redundant since .NET 7 and dangerous: `Write` re-enters the same converter when the runtime type equals `T` exactly, recursing infinitely. It writes no type discriminator, so it cannot round-trip — which matters for §8 contract versioning. |
| URI converter | `Serializer.HttpUriConverter` | DROP | built-in `Uri` converter | Follows `HttpUri`; removes the only place `Serialization` reaches into `Http`. |
| TimeSpan converter | `Serializer.TimeSpanConverter` | DROP | built-in STJ | **Verified by execution against .NET 10.0.9:** byte-identical for days, fractional seconds and negatives, and round-trips. Removing it also fixes a culture bug — `TimeSpan.Parse` without an `IFormatProvider` is culture-sensitive. |
| Version converter | `Serializer.VersionConverter` | DROP | built-in STJ | **Verified by execution against .NET 10.0.9:** identical output and round-trip. It also carries a nullability defect (`value?.ToString()` on a non-nullable parameter) that the built-in converter gets right. |
| Assembly preloading | `Composition.AssemblyLoader` | DROP | per-plugin `AssemblyLoadContext` + `INativeLibraryResolver` | Hardcodes product assembly names and a database engine, and loads everything into the **default** `AssemblyLoadContext`, directly contradicting §4's per-plugin ALC isolation. |
| Container rules + scanning | `Composition.Extensions.ServiceCollectionExtensions` | DROP | explicit `IPluginModule.ConfigureServices` | Blanket assembly scanning that registers every interface in every loaded assembly is the exact inverse of §11 registration filtering and §4's "only explicitly exported services are visible". This also removes DryIoc entirely. |
| Type catalog | `Composition.KnownTypes` | DROP | manifest + explicit registration | Scanning `GetTypes()` is hostile to per-plugin ALC isolation and to trimming. Its parameterless ctor exists "so unity can resolve for tests" and Unity left this codebase years ago. |
| Debounce factory | `TPL.IDebounceManager` | DROP | `TimeProvider` + `FakeTimeProvider` | A test seam only. Two of its three consumers already bypass it by newing a `Debouncer`, proving it is not load-bearing. |
| Debounce factory impl | `TPL.DebounceManager` | DROP | — | Dies with the interface; adds no behavior over the constructor. |
| Bounded scheduler | `TPL.LimitedConcurrencyLevelTaskScheduler` | DROP | `ConcurrentExclusiveSchedulerPair` | Dead across all 2,685 source files, and the BCL has shipped an equivalent for over a decade. |
| Message marker | `Messaging.IMessage` | DROP | `IDomainEvent` / command contract | Vacuous: two references in the whole solution and nothing is ever handled as an `IMessage`. It conflates commands with events. |
| Lifecycle marker | `Messaging.LifecycleEventAttribute` | DROP | — | Empty marker with zero references anywhere in `ArronixCore/src`; reintroduce with the event bus if and only if the post-shutdown-dispatch semantic is actually needed. |
| Auth configuration | `Options.AuthOptions` | DROP (from Common) | `Arronix.Host` | API-key authentication is the HTTP API's configuration; a shared foundation must not define the auth shape of a server it does not host. `Method`/`Required` should become enums and `ApiKey` flagged for redaction there. |
| Server configuration | `Options.ServerOptions` | DROP (from Common) | `Arronix.Host` (bind Kestrel directly) | Pure Kestrel configuration; Common has no HTTP server. The bootstrap already reads these keys twice. |

---

## 4. Plugin seams

Two tiers of extension exist, and conflating them is the mistake to avoid:

- **Capability-gated plugins** — declare capabilities in `plugin.json`; the Plugin Loader filters DI
  registration by declared capability (§11). Media and provider concerns.
- **Platform packs** — host-trusted, RID-selected, **no manifest and no capability**. They answer questions
  about the machine (OS probes, file permissions, mounts, service control) that the host itself needs before
  any plugin loads. Six `IOsVersionAdapter` implementations already live outside Common today, which is the
  evidence this tier is real.

| Seam interface | Lives in | Implemented by | Manifest capability | Why it cannot stay in Core |
|---|---|---|---|---|
| `ITelemetrySink` | `Arronix.Abstractions.Telemetry` | `Arronix.Plugin.Telemetry.Sentry`, `Arronix.Plugin.Telemetry.Otlp` | `telemetry-sink` | §10 states sinks are extensible and plugins raise events via abstractions, never directly to a sink — a concrete crash-reporting vendor cannot be a compile-time dependency of the shared foundation. Removes Sentry *and* `System.Data.SQLite` from Common. |
| `ITelemetryEmitter` | `Arronix.Abstractions.Telemetry` | `Arronix.Common` (`TelemetryEmitter`); consumed by every plugin | none (universal) | Replaces `SentryLoggerExtensions.WriteSentryWarn`, where the call site names the vendor. The contract must be plugin-visible or plugins keep naming sinks. |
| `ITelemetryEnricher` | `Arronix.Abstractions.Telemetry` | `Arronix.Common` (`OsEnvironmentEnricher`), platform packs, plugins | none (read-only) | Environment facts are contributed by whoever owns the subsystem; Core cannot know what a sink wants tagged. |
| `ITelemetryEventFilter` | `Arronix.Abstractions.Telemetry` | storage layer (SQLite error codes), host (`CorruptDatabaseException`), indexer plugins (Jackett noise) | none | This is precisely what lets the logging layer stop referencing the database engine. |
| `IRedactionRuleProvider` (+ `RedactionRule`) | `Arronix.Abstractions.Diagnostics` | every indexer, download-client, notification and media-server plugin | implied by the capability owning the secret (`indexing`, `download`, `notification`, `metadata`) | **The flagship seam.** A redaction rule is owned by whoever knows the secret's shape; 13 of the 25 hardcoded rules name specific third parties. A plugin that stops shipping stops leaking its rule into every install. |
| `IHealthContributor` | `Arronix.Abstractions.Health` | every plugin and Common subsystem | none | §10 promises it but only the `HealthCheck` DTO and status enums exist. **Must land before any health work**, reusing those types verbatim. |
| `IHttpGateway` | `Arronix.Abstractions.Http` | `Arronix.Common` (`HttpGateway`); plugins receive `ScopedHttpGateway` | `network` (implied by `indexing`, `metadata`, `download`, `notification`) | If plugins can resolve an HTTP client from a directly-referenced implementation assembly, capability gating cannot be enforced. The Plugin Loader registers it into a plugin's child scope only if the capability intersection is non-empty; otherwise activation fails with `CoreErrorCode.PluginCapabilityMissing`. |
| `IOutboundHttpInterceptor` | `Arronix.Abstractions.Http` | indexer plugins (CloudFlare and Torcache workarounds are the existing proof) | `indexing` | The declared extension point for provider-specific HTTP behavior; two implementers already exist outside Common. |
| `ICertificateValidationPolicy` | `Arronix.Abstractions.Http` | `Arronix.Common` / `Arronix.Host` from config; consumed by plugins opening their own TLS connections | `network` | Today the email notifier reaches into the HTTP stack's validation service; this makes that a legitimate contract instead of a layering accident. |
| `IFileSystem` | `Arronix.Abstractions.FileSystem` | `Arronix.Common`, handed to plugins **only** as `ScopedFileSystem` | `storage` | The full 48-member surface includes `SetEveryonePermissions`, `GetMounts`, `EmptyFolder` and arbitrary-path `DeleteFolder`; handing that to plugins is a §11 violation. The narrowed contract plus a path-scoping decorator is the enforcement point. |
| `IFilePermissions` | `Arronix.Abstractions.FileSystem` | **platform packs** `Arronix.Platform.Linux` (chmod/chown), `Arronix.Platform.Windows` (ACLs) | none (platform pack) | Extracting these members is exactly what lets the plugin-facing `IFileSystem` be narrow. |
| `IStorageMountProvider` | `Arronix.Abstractions.FileSystem` | **platform packs** (Linux `/proc/mounts`, Windows `DriveInfo`); `DriveInfoStorageMount` is the Common fallback | none (platform pack) | Mount enumeration is inherently OS-specific and `MountOptions.ContainsKey("ro")` is Linux syntax leaking into Core. |
| `IFileTransferService` | `Arronix.Abstractions.FileSystem` | `Arronix.Common` | `import` | Import is a plugin capability and importing *is* file transfer with verification, rollback and hardlink negotiation; no plugin should re-implement safe file placement. |
| `IPluginPaths` | `Arronix.Abstractions.Hosting` | `Arronix.Common` (`PluginPaths`), one instance per plugin | none (universal, pre-scoped) | Deliberately **not** `IAppPaths` — handing plugins the host `AppDataFolder` would let any plugin read the host config and database paths. |
| `IOsVersionProbe` (+ `OsVersionDescriptor`) | `Arronix.Abstractions.Hosting` | **platform packs** Linux (release-file, issue-file), macOS, Windows, NAS (Synology, FreeBSD) | none (platform pack) | Already a plugin pattern in disguise: all six existing implementations live outside Common. |
| `IOperatingSystemInfo` / `IHostRuntimeInfo` | `Arronix.Abstractions.Hosting` | `Arronix.Common` | none | Plugins legitimately need containerization awareness and startup time; remote-path-mapping health checks in a download-client plugin are the canonical case. |
| `IServiceLifecycleController` | `Arronix.Abstractions.Hosting` | **platform packs** `Arronix.Platform.Windows`, `Arronix.Platform.Systemd` | none (platform pack) | A single-OS service-manager integration has no place in a cross-platform foundation; `sc.exe` install/uninstall/DACL work is packaging and leaves the runtime entirely. |
| `INativeLibraryResolver` | `Arronix.Abstractions.Hosting` | storage providers | none (platform pack) | Replaces `AssemblyLoader`'s hardcoded `sqlite3` → `libsqlite3.so.0` mapping without Common knowing a database engine exists. |
| `IRateLimiter` | `Arronix.Abstractions.Throttling` | `Arronix.Common` (`KeyedRateLimiter`) | `network` | §12 requires throttling and circuit breakers **shared across plugins hitting the same remote**, which is only achievable if every plugin acquires leases from one host-owned limiter. |
| `ICacheProvider` / `ICache` / `ICache<T>` / `ISelfRefreshingCache<T>` | `Arronix.Abstractions.Caching` | `Arronix.Common` | none (universal) | 66 dependent files; partitions are namespaced by plugin id so one plugin cannot read or evict another's entries. |
| `IProgressReporter` | `Arronix.Abstractions.Diagnostics` | `Arronix.Common` | none (universal) | Its existence is what stops every plugin taking a hard NLog reference just to report job progress. Carries the correlation id from the scheduler. |
| `IDomainEvent` / `IEventPublisher` / `IEventHandler<T>` | `Arronix.Abstractions.Events` | plugins both publish and subscribe | none, but subscription is filtered | §4 forbids direct cross-plugin calls; a plugin sees Core events plus its own namespace, never another plugin's. The 63 existing event types are the TV domain model and stay in the TV plugin — only the contract crosses. |
| `IJsonSerializer` | `Arronix.Abstractions.Serialization` | `Arronix.Common` (`SystemTextJsonSerializer` over `JsonDefaults`) | none | Guarantees Core and plugins agree on wire shape (§8) without plugins referencing a static facade in an implementation assembly. |
| `IDiacriticFoldingProvider` | `Arronix.Abstractions.Naming` | `Arronix.Common` (default eth/thorn table), language/media plugins | `parsing` or `renaming` | Title folding is inherently language-specific; contributing fold pairs is a textbook plugin seam and it is what lets `Diacritical.Net` be dropped. |
| `ArronixException` (+ `CoreErrorCode`) | `Arronix.Abstractions.Errors` | thrown and caught by everything | none | Plugins must throw and catch platform exceptions without referencing the implementation; 52 dependents on the legacy root. |

### Proposed capability vocabulary

Extending the §4.1 manifest example:

- **Media capabilities:** `indexing`, `metadata`, `parsing`, `matching`, `quality`, `renaming`, `import`,
  `download`, `notification`
- **Infrastructure capabilities:** `network` (implied by `indexing` | `metadata` | `download` |
  `notification`), `storage`, `process`, `telemetry-sink`, `distribution`

§4.1 validation step 3 becomes **bidirectional**: declaring a capability requires the matching registration
**and** registering a gated interface requires the matching capability — otherwise a plugin can smuggle in an
`ITelemetrySink` without declaring `telemetry-sink`.

> **ARCHITECTURE.md amendment — APPROVED (2026-07-27).** §11 said *"no network unless it declares `indexing`"*,
> which is wrong as written: notification providers and download clients need network and are not indexers.
> `network` is now a first-class capability implied by any of `indexing`, `metadata`, `download` or
> `notification`. ARCHITECTURE.md §11 has been amended accordingly.

### Named future extensions

`Arronix.Plugin.Telemetry.Sentry` · `Arronix.Plugin.Telemetry.Otlp` · `Arronix.Plugin.Support.Rpc` (JSON-RPC +
XML-RPC, referenced only by download-client plugins) · `Arronix.Plugin.DownloadClient.*` ·
`Arronix.Plugin.Indexer.*` · `Arronix.Plugin.Notification.*` · `Arronix.Plugin.Media.Tv` (owns
`LevenshteinDistanceClean` and the `-1` sentinel decoder) · `Arronix.Platform.{Windows,Linux,MacOs,Systemd}` ·
`Arronix.Host.Updates` · `Arronix.Imaging.{ImageSharp,Skia}`.

> **Imaging — APPROVED as a pack (2026-07-27).** Image processing becomes a platform pack, not a media plugin:
> resizing a JPEG carries no TV/movie/music semantics, so it belongs to no media kind. Today the entire
> footprint is `NzbDrone.Core/MediaCover/ImageResizer.cs` — 60 lines behind a one-method interface
> (`void Resize(string source, string destination, int height)`), used only by `MediaCoverService` to
> thumbnail cover art, and it is the sole consumer of `SixLabors.ImageSharp` in the whole solution.
>
> Seam: `IImageProcessor` (Tier B in `Arronix.Common` until a second implementer exists, per the promotion
> rule), with no imaging library types in the signature. Packs implement it; a host with no pack registered
> serves full-size artwork and degrades gracefully rather than failing.
>
> This is the same shape as the FFMpegCore decision already recorded in `DEPENDENCY_INVESTIGATION.md`, where
> video metadata and HDR detection were stubbed for "format support extensions" — a heavyweight, format-specific,
> license-encumbered dependency doing one narrow job. It also dissolves a live problem: ImageSharp 4 gates the
> build on a paid license key, and today that blocks `NzbDrone.Core`, i.e. the whole product. Behind a seam the
> license question collapses to which pack you ship, or none.
>
> **Scope note:** `ImageResizer` lives in `NzbDrone.Core`, not `NzbDrone.Common`, so it is outside this
> delivery's survey. Recorded here so the decision is not rediscovered during the Core extraction.
>
> **Separate, unresolved:** `MediaCoverTypes` (`Poster, Banner, Fanart, Screenshot, Headshot, Clearlogo`) is a
> closed enum with visible TV bias — music would want `Disc`/`CoverBack`. Under the plugin model cover kinds
> should be plugin-declared, like naming tokens. That is a `MediaCoverService` question for the Core delivery.

> **OAuth 1.0a — DROPPED (2026-07-27).** `Arronix.Plugin.Support.OAuth1` is deliberately **not** on the list
> above. OAuth 1.0a existed in the source material for exactly one notification provider (Twitter/X), which
> removed free v1.1 API access; the vendored 2011 signing code carries three recorded defects. Neither the
> provider nor the OAuth 1.0a path is carried into Arronix. `OAuth/` is therefore **DROP**, not PLUGIN — see
> §3.4. Any future provider needing delegated auth should use OAuth 2.0 / PKCE, which is BCL-supportable and
> needs no vendored signer.

---

## 5. Dropped in favor of the BCL

| Legacy type / member | .NET 10 replacement | Call-site impact |
|---|---|---|
| `EnsureThat.Ensure` + 17 companion types + `ExceptionMessages.resx` | `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, `ArgumentOutOfRangeException.ThrowIf*`, all with `[CallerArgumentExpression]` | **~36 legacy call sites total.** Only four guard shapes are ever used; three are BCL one-liners. The fourth, `IsValidPath` (51 sites), survives as `FileSystem.PathGuard.ThrowIfInvalid`. Arronix.Common: zero. |
| `StringExtensions.IsNullOrWhiteSpace` / `IsNotNullOrWhiteSpace` | `string.IsNullOrWhiteSpace` (already `[NotNullWhen(false)]` since .NET Core 3.0) | 305 legacy sites; none in Arronix.Common. Negated form is `!string.IsNullOrWhiteSpace(x)`. |
| `StringExtensions.StartsWith/EndsWith/Equals/ContainsIgnoreCase` | `StringComparison.OrdinalIgnoreCase` overloads; `StringComparer.OrdinalIgnoreCase` for sequences | ~120 legacy sites. **Dropping them fixes a latent bug** — all four hardcode `InvariantCultureIgnoreCase` where paths, headers and protocol tokens mean `Ordinal`. |
| `StringExtensions.HexToByteArray` / `ToHexString` | `Convert.FromHexString` / `Convert.ToHexString` (`ToHexStringLower` for lowercase) | Direct swap; span-based, and the uppercase convention already matches. |
| `StringExtensions.Inject` / `Join` / `NullSafe` / `CleanSpaces` / `Replace(int,int,string)` / `Reverse` | `string.Format`, `string.Join`, span slicing, `new string([.. s.Reverse()])` | `CleanSpaces` and `Inject` are dead once EnsureThat goes; `NullSafe`'s two sites are TV `ToString()` overrides moving to the plugin. |
| `Base64Extensions.ToBase64` | `Convert.ToBase64String(ReadOnlySpan<byte>)` | One live call site; the `long` overload is dead. |
| `ConvertBase32` | *(none — BCL has no Base32; belongs to a torrent plugin if ever needed)* | Zero call sites anywhere. |
| `HashUtil.CalculateCrc` | `System.IO.Hashing.Crc32.HashToUInt32` | **Not bit-identical** — the hand-rolled loop is unreflected CRC-32/MPEG-2, `Crc32` is the reflected zlib variant. Both legacy consumers tolerate a changed digest. |
| `HttpUri` | `System.Uri` / `UriBuilder` + `Http.UriExtensions` | 19 files / 27 refs legacy. `base + relative` → `new Uri(baseUri, relative)`; `AddQueryParams` → `UriBuilder.Query`; only `{segment}` templating has no BCL analog. **Gated on golden tests.** |
| `HttpAccept` | `System.Net.Mime.MediaTypeNames` | 41 legacy files — the widest blast radius in its slice. Only the composite rss/xml value survives as `AcceptHeaders`. |
| `HttpFormData` + the hand-written multipart writer | `MultipartFormDataContent` / `ByteArrayContent` / `FormUrlEncodedContent` | Removes a `DateTime.Now.Ticks` boundary and its own boundary-collision TODO. |
| `BasicNetworkCredential` | `AuthenticationHeaderValue` | 16 legacy sites migrate mechanically. Test non-ASCII credentials — upstream changed ISO-8859-1 → UTF-8. |
| `TlsFailureException` | `HttpRequestException` + `HttpRequestError.SecureConnectionError` | Zero call sites; built on obsoleted `WebRequest` (SYSLIB0014). |
| `WebHeaderCollectionExtensions` | direct `HttpHeaders` enumeration | Two call sites, both rewritten anyway. |
| `WebParameter` / `WebParameterCollection` | `KeyValuePair<string,string>` + `List<T>` + LINQ | 157 lines replaced line-for-line; still carries dead `#if WINRT` branches. |
| `TimeSpanConverter` | built-in STJ `TimeSpan` support | **Verified by execution on .NET 10.0.9 — byte-identical** for days, fractional seconds and negatives. Three extra legacy registrations become deletions. Also fixes a culture bug. |
| `VersionConverter` | built-in STJ `Version` support | **Verified by execution on .NET 10.0.9 — identical output and round-trip.** Also fixes a nullability defect. |
| `HttpUriConverter` | built-in `Uri` converter | Dies with `HttpUri`; removes the only `Serialization` → `Http` dependency. |
| `DynamicJsonConverter<T>` | `[JsonPolymorphic]` + `[JsonDerivedType]` (.NET 7+) | Also removes an infinite-recursion hazard and adds a type discriminator the original lacked. |
| `LimitedConcurrencyLevelTaskScheduler` | `ConcurrentExclusiveSchedulerPair`, `Parallel.ForEachAsync`, `SemaphoreSlim`, bounded `Channel` | Zero call sites across 2,685 files. |
| `IDebounceManager` / `DebounceManager` | `TimeProvider` + `Microsoft.Extensions.Time.Testing.FakeTimeProvider` | Its only purpose was a test seam; two of three consumers already bypass it. |
| `Os` enum, `OsInfo.Is*` statics | `OperatingSystem.IsWindows()` / `IsLinux()` / `IsMacOS()` / `IsFreeBSD()` | Dozens of legacy sites. The BCL guards additionally satisfy the platform-compatibility analyzer, which the enum does not. |
| `IPlatformInfo` / `PlatformInfo` | `Environment.Version`, `RuntimeInformation.FrameworkDescription` | Two consumers, both one-liners. `PlatformName` currently returns the literal `".NET"`. |
| `RuntimeMode` | `WindowsServiceHelpers.IsWindowsService()` + `Environment.UserInteractive` | One legacy reader (a `/system` API field); the `Tray` state disappears in the Arronix model. |
| `StartupContext` | `Microsoft.Extensions.Configuration.CommandLine` + options binding | The host already takes `Microsoft.Extensions.Hosting`, which brings the command-line provider for free. |
| `IServiceFactory` / `ServiceFactory` | `IServiceProvider.GetRequiredService<T>()` / `GetServices<T>()`; `TryAddEnumerable` at the composition root | `GetImplementations` has zero callers; the dedupe-by-`FullName` line is a band-aid over duplicate registrations. |
| `CompositionExtensions.AddNzbDroneLogger` | `services.AddLogging()` (open-generic `ILogger<>`) | 5 legacy sites. Removes the last DryIoc dependency from the instrumentation slice. |
| `Composition.ServiceCollectionExtensions` (DryIoc rules + `AutoAddServices`) | explicit `IPluginModule.ConfigureServices` on `Microsoft.Extensions.DependencyInjection` | Removes DryIoc entirely. |
| `Composition.AssemblyLoader` / `KnownTypes` | `AssemblyLoadContext` + `AssemblyDependencyResolver` + `NativeLibrary.SetDllImportResolver` in the Plugin Loader | Two real consumers, both enumerating command types — which in Arronix comes from the manifest. |
| `ReflectionExtensions.GetAttribute` / `GetAttributes` / `HasAttribute` | `CustomAttributeExtensions.GetCustomAttribute<T>()` | The rest of the class leaves Common (Plugin Loader / Host). |
| `Expansive` (6 types) | `Naming.TokenSanitizer` + Policy Engine token pipeline | One consumer, and it is a test fixture. |
| `DictionaryExtensions`, `ResourceExtensions`, `RegexExtensions`, `PathExtensions.CleanPath`, `PathExtensions.GetPathExtension`, `EnumerableExtensions.DropLast`/`NotAll`, `DateTimeExtensions.EpochTime`/`Before`/`After`/`Between`/`InNextDays`/`InLastDays` | `ToDictionary`, `Stream.ReadExactly`, plain arithmetic, `Path.GetExtension`, `SkipLast`, `DateTime.UnixEpoch`, direct comparisons | All dead or one-site; inline at the call site. |
| `LongPathSupport.Enable()` | *(nothing — the `AppContext` switches are inert no-ops on net10)* | Delete the method; keep the limit probing, which is load-bearing for filename truncation. |
| `SharpZipLib` (`ArchiveService`) | `System.IO.Compression.ZipFile`/`ZipArchive`, `System.Formats.Tar.TarFile`/`TarReader`, `GZipStream` | Also removes the **unguarded zip-slip path traversal** in `ExtractZip`, since the BCL extractors validate entry paths. |
| `Diacritical.Net` (`RemoveDiacritics`, `AdditionalDiacriticsProvider`) | `string.Normalize(NormalizationForm.FormD)` + `CharUnicodeInfo` filtering | The only thing the package adds beyond the BCL is a three-character eth/thorn fold table, which becomes the default `IDiacriticFoldingProvider`. |
| `Newtonsoft.Json` | `System.Text.Json` | Already removed from the source material; explicitly not reintroduced. The three Newtonsoft-era artifacts that outlived it — the `where T : new()` constraint, the `?? new T()` fallback, and the cast-to-object polymorphism trick — go with it. |

---

## 6. Dependencies

### What `Arronix.Common` takes

One `ProjectReference` and seven packages, **all Microsoft-owned, none appearing in the public API surface.**

| Reference | Version | Why |
|---|---|---|
| `Arronix.Abstractions` (ProjectReference) | 0.2.0 | The only project reference. Arronix.Common must never be referenced *by* a plugin. |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.10 (**new CPM entry**) | `ILogger<T>` injection replaces every `NLog.Logger` constructor parameter and the static `NzbDroneLogger.GetLogger` service locator. Abstractions-only, so Common's public surface names no logging framework. |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.10 (**new**) | For `AddArronixCommon` only. Deliberately the Abstractions package, never the full container. |
| `Microsoft.Extensions.Options` | 10.0.10 (**new**) | The seven options types, plus `IOptionsMonitor` change tokens replacing the `LogManager.AllTargets.OfType<>` type-sniffing that reconfigures log rotation today. |
| `Microsoft.Extensions.Options.DataAnnotations` | 10.0.10 (**new**) | `ValidateOnStart`. The legacy pattern of all-nullable properties with `?? GetValue(...)` fallbacks does not survive. |
| `Microsoft.Extensions.Configuration.Abstractions` | 10.0.10 (**new**) | Binding only; the command-line provider itself belongs to `Arronix.Host`. |
| `Microsoft.Extensions.Http` | 10.0.10 (**new**) | `IHttpClientFactory` named clients replace `ManagedHttpDispatcher`'s hand-rolled per-proxy-key `HttpClient` cache and its unbounded growth, and give `SocketsHttpHandler` lifetime rotation for free. |
| `System.Threading.RateLimiting` | 10.0.x (**new**) | `PartitionedRateLimiter` replaces the hand-rolled `AddOrUpdate` timestamp arithmetic and, crucially, the `Thread.Sleep` in `RateLimitService`. Lease-based acquisition is what makes `IRateLimiter` a sane plugin contract. |
| `System.IO.Hashing` | 10.0.x (**new**) | `XxHash3`/`XxHash128` for `StableHash` and `FileHasher`, `Crc32` where a checksum is genuinely wanted. Span-based and AOT-friendly. Replaces the locked shared SHA-1 instance and the `Encoding.Default` hashing that makes the current digest machine-dependent. |
| `System.Text.Json` | — | **No package reference.** In-box on net10.0 and deliberately absent from `Directory.Packages.props`. Add a `JsonSerializerContext` for trimming instead. |

`Microsoft.Extensions.Http.Resilience` is **not** taken in v0.1.0 — it is the right route to §12 circuit breakers,
but there is no shared-remote consumer yet. Recorded in [§9 Deferred](#9-deferred).

Test project adds: `Microsoft.Extensions.Time.Testing` (**new CPM entry** — `FakeTimeProvider`, the thing that
makes `Debouncer`, `ExpiringCache`, `SelfRefreshingCache` and `KeyedRateLimiter` testable without sleeping, and
that makes `IDebounceManager` droppable) and `Moq` (already pinned). NUnit, `NUnit3TestAdapter`,
`Microsoft.NET.Test.Sdk`, `coverlet.collector` and `NunitXml.TestLogger` are auto-added by
`src/Directory.Build.props` once `<TestProject>true</TestProject>` is set explicitly.

### What is shed, relative to `Sonarr.Common`

| Package | Why it goes |
|---|---|
| `DryIoc.dll` + `DryIoc.Microsoft.DependencyInjection` | Its only consumers in the whole survey are `Composition/Extensions.cs` (`WithNzbDroneRules`, `AutoAddServices`) and the one-method `AddNzbDroneLogger`. Both are DROP: blanket assembly scanning is the direct inverse of §11 registration filtering, and parent-type-named loggers are a first-class `Microsoft.Extensions.Logging` feature. |
| `Sentry` | Becomes a plugin dependency (`Arronix.Plugin.Telemetry.Sentry`), never a Common one. §10 makes sinks extensible; a concrete crash-reporting vendor cannot be a compile-time dependency of the shared foundation. **See the build-coupling warning below.** |
| `System.Data.SQLite` + `SourceGear.sqlite3` | Referenced by the logging layer solely for `SentryTarget`'s `SQLiteErrorCode` filter list. A logging target must not know the database engine; those filters become storage-contributed `ITelemetryEventFilter` implementations. |
| `SharpZipLib` | Used only by `ArchiveService`. Fully replaced by `System.IO.Compression` + `System.Formats.Tar`, which also removes the unguarded zip-slip traversal. |
| `Diacritical.Net` | Replaced by `FormD` normalization + `CharUnicodeInfo`; the three-character fold table becomes the default `IDiacriticFoldingProvider`. |
| `System.ServiceProcess.ServiceController` | Used only by the Windows service controller, which leaves for `Arronix.Platform.Windows`. |
| `Microsoft.Extensions.Hosting.WindowsServices` | Used only by `RuntimeInfo` to derive `IsWindowsService`. The platform pack supplies the answer through `IHostRuntimeInfo`, or the composition root calls `WindowsServiceHelpers` directly. |
| `IPAddressRange` | Referenced by the source csproj but by no type in any surveyed slice — `IpAddressExtensions` uses `System.Net` only. Its real consumer is inbound API IP filtering, which is `Arronix.Host`'s concern. |
| `NLog.Targets.Syslog` | No consumer in Common. If syslog output is wanted it is host-side sink configuration, not a foundation dependency. |
| `System.Configuration.ConfigurationManager` | Legacy XML config machinery. Replaced by `Microsoft.Extensions.Configuration` binding; §5 wants pluggable storage and per-plugin schema fragments, not a single XML config file. |
| `Newtonsoft.Json` | Already removed from the source material; explicitly not reintroduced. |
| `Microsoft.Extensions.DependencyInjection` (full package) | Common takes only the `.Abstractions` half. The full container belongs to the composition root. |
| `NLog` + `NLog.Extensions.Logging` + `NLog.Layouts.ClefJsonLayout` | Not shed from the platform, but shed from **Arronix.Common**: they belong to the deferred `Arronix.Common.Logging.NLog` project so Common depends only on `Microsoft.Extensions.Logging.Abstractions`. Without this split, §10's "plugins raise events via abstractions, never directly to a sink" stays aspirational, because the shared library would still be naming a concrete framework. |
| EF Core / any ORM | **Not added.** `.github/copilot-instructions.md` carries a hard contributor rule forbidding EF Core packages or scaffolding until a dedicated migration issue is approved. Arronix.Common contains no persistence. |

> **⚠ Build coupling that must be handled in the same PR.** `src/Directory.Build.props` lines 102–122 apply
> `SentryOrg` / `SentryProject` / `SentryAuthToken` / `SentryUploadSymbols` plus `PublishRepositoryUrl` and
> `EmbedAllSources` to **every** project under `src/` whenever `Configuration == Release` — and Release is the
> default configuration. That block is **not** gated on `SonarrProject`, so a new `Arronix.Common` would
> silently inherit Sentry symbol upload and full source embedding. Either gate the block on
> `'$(SonarrProject)'=='true'`, or have `Arronix.Common` opt out explicitly. This is a one-line change but it
> must not be missed.

**Net result:** the source material's fifteen third-party references become **seven Microsoft-owned packages,
zero of which appear in the public API surface, and zero non-Microsoft dependencies.**

---

## 7. Build & verification

### 7.1 The csproj

Mirror `Arronix.Abstractions.csproj` exactly — 12 lines, no `ItemGroup` beyond the references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Arronix.Common</RootNamespace>
    <AssemblyName>Arronix.Common</AssemblyName>
    <Version>0.1.0</Version>
    <Description>Host-side platform infrastructure for the Arronix media platform. Filesystem, outbound HTTP, caching, throttling, processes, hosting identity, serialization, redaction and telemetry composition. Never referenced by plugins.</Description>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Arronix.Abstractions\Arronix.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Http" />
    <PackageReference Include="System.Threading.RateLimiting" />
    <PackageReference Include="System.IO.Hashing" />
  </ItemGroup>
</Project>
```

Hard rules, each verified against the repo's build configuration:

- **The project name must not start with `Sonarr`.** `src/Directory.Build.props` sets `SonarrProject=true` for
  such names, which force-rewrites `RootNamespace` via `$(MSBuildProjectName.Replace('Sonarr','NzbDrone'))`,
  stamps `Product=Sonarr` / `Company=sonarr.tv` / `AssemblyVersion 10.0.0.*`, and redirects `OutputPath` to
  `_output\`. `Arronix.Common` correctly resolves `SonarrProject=false`, `SonarrOutputType=Library`, output
  `_temp/bin/Release/Arronix.Common/net10.0/`.
- **Do not set** `TargetFrameworks`, `RuntimeIdentifier`, `OutputPath`, `Product`, `Company` or
  `AssemblyVersion` — all come from `src/Directory.Build.props`. Restore is RID-specific and the default RID is
  derived from the host.
- **Package references are version-less.** Central package management is on
  (`ManagePackageVersionsCentrally=true`); every new package needs a matching `<PackageVersion>` in the root
  `Directory.Packages.props`.
- **No `ImplicitUsings`.** Hand-write `GlobalUsings.cs` mirroring Abstractions exactly: `System`,
  `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`. This deliberately diverges from
  the source material, which uses implicit usings — IDE0005 is a hard error here and a minimal global-using set
  is the only safe posture.

### 7.2 The analyzer bar — why a copy-paste lift is impossible

`AnalysisLevel=6.0-all` + `TreatWarningsAsErrors=true` + `EnforceCodeStyleInBuild=true` +
`GenerateDocumentationFile=true`. The root `.editorconfig` downgrades 143 CA rules to `suggestion`; everything
in the .NET 6 wave *not* in that list is a **build error**.

**Empirically verified errors** (a replica project was built with the same props/editorconfig):

| Rule | Meaning |
|---|---|
| `CA1846` | Prefer `AsSpan` over `Substring` |
| `CA1802` | `readonly` field with a constant value must be `const` |
| `CA1805` | Member explicitly initialized to its default |
| `IDE0005` | Unnecessary using directive |
| `IDE0007` | Use `var` instead of an explicit type — **`var` is mandatory everywhere** |
| `IDE0018` | Inline `out` variable declaration |

Rules that are *not* errors because `AnalysisLevel 6.0` predates them: `CA1510`–`CA1513`, `CA1852`, `CA1860`,
`CA1311`, `CA1849`, `CA2263`.

Legacy code is full of all six error cases. This is a **mechanical, not stylistic** argument for the clean-room
reimplementation the user asked for, and it must be stated to any agent tempted to lift a file wholesale.

Other conventions to follow: UTF-8, **LF line endings**, 4-space indent, trim trailing whitespace, final
newline, file-scoped namespaces, `System.*` usings sorted first, no `this.` qualification, private fields
`_camelCase`, **no file/license headers** (SA1633 is off and no Abstractions file has one). `CS1591` is in
`NoWarn`, but house style is a full `/// <summary>` on every public type and member with `<param>`/`<returns>`
— match `Arronix.Abstractions`.

### 7.3 Joining `Arronix.sln`

Add to `/Arronix.sln` — **not** `src/Sonarr.sln`. Project type GUID
`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`, nested under the existing `src` solution folder
`{827E0CD3-B72D-47B6-A68D-7590B98EB39B}`, with `Debug`/`Release` × `{Any CPU, x64, x86}` all mapped to
`Any CPU` for both `ActiveCfg` and `Build.0`.

`dotnet sln add` produces **neither** the x64/x86 mappings **nor** the nesting — hand-edit to match the two
existing project blocks exactly.

### 7.4 The test project

`src/Arronix.Common.Tests` — **plural**, matching `Arronix.Abstractions.Tests`.

Gotcha: `src/Directory.Build.props` sets `TestProject` only when the name `EndsWith('.Test')` (singular), so a
`.Tests` project **must set `<TestProject>true</TestProject>` explicitly** in its own csproj — exactly as
`Arronix.Abstractions.Tests` does, with the comment "Override to mark as test project". That flag is what pulls
in the test SDK packages. `SonarrOutputType` resolves to `Test` via `Contains('.Test')`, routing output to
`_tests\`.

Style: NUnit 3 constraint model (`Assert.That(x, Is.EqualTo(y))`), `GlobalUsings.cs` containing only
`global using NUnit.Framework;`, plus `IsPackable=false` and the same `Nullable`/`LangVersion`/
`EnforceCodeStyleInBuild` trio. FluentAssertions is pinned centrally but is **not** used by
`Arronix.Abstractions.Tests` — do not introduce it.

### 7.5 Acceptance criteria

The delivery is done when **all** of the following hold:

1. `dotnet build Arronix.sln` succeeds with **0 warnings, 0 errors** in both Debug and Release. The baseline is
   green today (`Arronix.Abstractions` builds clean), so any warning is a regression and, under
   `TreatWarningsAsErrors`, a break.
2. `dotnet test Arronix.sln` passes.
3. `dotnet build src/Sonarr.sln` still succeeds — **the legacy tree is untouched and this delivery is
   additive.**
4. No type, namespace, string literal, file name or comment in `src/Arronix.Common` contains `Sonarr`,
   `NzbDrone`, `nzbdrone`, `sonarr`, `Series`, `Episode`, `Season` or `TV`. Enforced by a test
   (`BrandNeutralityTests`) that greps the compiled assembly's exported type names and the source tree.
5. `Arronix.Common` has exactly one `ProjectReference` (`Arronix.Abstractions`) and exactly the eight package
   references listed in §6. Enforced by a test asserting the assembly's referenced-assembly set.
6. `Arronix.Abstractions` still has **zero** package references and does **not** reference `Arronix.Common`.
   Enforced by a test.
7. Every new public type in `Arronix.Abstractions` carries `[Experimental]`; `Arronix.Abstractions` version is
   `0.2.0`; `docs/contracts/stability.md` records the 0.2.0 additions and the now-implemented `[Experimental]`
   attribute.
8. **Golden tests ported from `NzbDrone.Common.Test/Http/HttpUriFixture.cs` pass against
   `System.Uri` + `UriExtensions`** — a hard prerequisite for the `HttpUri` drop, written *before* the
   replacement. Any behavioral divergence is a bug in `UriExtensions`, not an acceptable change.
9. Every type taking a clock takes `TimeProvider`; no test calls `Thread.Sleep` or `Task.Delay` to advance
   time — `FakeTimeProvider` only.
10. The Sentry/SourceLink block in `src/Directory.Build.props` no longer applies to `Arronix.Common` (gated or
    opted out).
11. Docs updated in the same PR per the Anti-Drift Safeguards in `.github/copilot-instructions.md`:
    `ARCHITECTURE.md` (§2 diagram node + §3 status-table row), `GLOSSARY.md` (new concepts — Platform Pack,
    Capability Gate, Redaction Rule, Telemetry Sink, Scoped File System — framed as **concepts, never type
    names**, per the rule at the top of that file), `CONTRIBUTING.md`, `README.md`,
    `.github/copilot-instructions.md`, and a new `src/Arronix.Common/README.md` mirroring
    `src/Arronix.Abstractions/README.md` including the GPLv3 + Sonarr attribution footer.

> **⚠ No CI gate exists.** `.github/workflows` contains only `build_v5.yml`, which targets the legacy solution.
> `Arronix.sln` is built by nothing. `Arronix.Common` will have no automated build unless a workflow is added
> alongside it. Flagged in [§10 Open questions](#10-open-questions).

---

## 8. Implementation order

Fifteen work packages. Each is small enough for one agent, states its inputs and done-condition, and **names the
files it creates**. WP01–WP03 are strictly sequential; WP04–WP12 are largely independent once WP03 lands and
can be parallelized; WP13–WP15 close the delivery.

Throughout: build **outside-in**. Implement against Tier A and Tier B interfaces, and let the working code prove
the shape. Do not promote a Tier B interface to `Arronix.Abstractions` opportunistically.

---

### WP01 — Skeleton, solution wiring, CPM entries

**Inputs:** §7.1, §7.3, `src/Arronix.Abstractions/*` as the style reference.

**Creates:**
- `src/Arronix.Common/Arronix.Common.csproj`
- `src/Arronix.Common/GlobalUsings.cs`
- `src/Arronix.Common/README.md`
- `src/Arronix.Common.Tests/Arronix.Common.Tests.csproj`
- `src/Arronix.Common.Tests/GlobalUsings.cs`
- `src/Arronix.Common.Tests/ProjectShapeTests.cs`

**Edits:** `/Arronix.sln` (hand-edited, two new project blocks + config mappings + nesting);
`/Directory.Packages.props` (eight new `PackageVersion` entries: the six `Microsoft.Extensions.*` at `10.0.10`,
`System.Threading.RateLimiting`, `System.IO.Hashing`, plus `Microsoft.Extensions.Time.Testing` for tests);
`src/Directory.Build.props` (gate the Sentry/SourceLink block on `'$(SonarrProject)'=='true'`).

**Done when:** `dotnet build Arronix.sln` is green with 0 warnings in Debug **and** Release;
`dotnet build src/Sonarr.sln` still green; `ProjectShapeTests` asserts acceptance criteria 5 and 6.

---

### WP02 — `Arronix.Abstractions` v0.2.0, Tier A contracts

**Inputs:** §3.1, §4, the [three-tier rule](#the-three-tier-promotion-rule), `docs/contracts/stability.md`.

**Creates** (all under `src/Arronix.Abstractions/`, every public type marked `[Experimental]`):
- `Experimental/ExperimentalAttribute.cs` — implement the attribute `stability.md` already anticipates
- `Errors/ArronixException.cs`
- `Events/IDomainEvent.cs`, `Events/IEventPublisher.cs`, `Events/IEventHandler.cs`
- `Health/IHealthContributor.cs` — **reuses the existing `HealthCheck`, `HealthStatus`, `HealthSeverity`; must not re-declare them**
- `Diagnostics/IProgressReporter.cs`, `Diagnostics/IRedactionRuleProvider.cs`, `Diagnostics/RedactionRule.cs`
- `Telemetry/ITelemetrySink.cs`, `ITelemetryEmitter.cs`, `ITelemetryEnricher.cs`, `ITelemetryEventFilter.cs`, `TelemetryEvent.cs`, `TelemetrySeverity.cs`
- `Http/IHttpGateway.cs`, `OutboundHttpRequest.cs`, `OutboundHttpResponse.cs`, `HttpHeaderCollection.cs`, `IOutboundHttpInterceptor.cs`, `HttpGatewayException.cs`, `HttpRateLimitedException.cs`, `UnexpectedHtmlResponseException.cs`, `ICertificateValidationPolicy.cs`
- `FileSystem/IFileSystem.cs`, `IFilePermissions.cs`, `IFileTransferService.cs`, `FileTransferMode.cs`, `IStorageMount.cs`, `IStorageMountProvider.cs`, `StorageMountOptions.cs`, `DestinationExistsException.cs`, `PlatformPath.cs`, `PlatformPathKind.cs`
- `Hosting/IPluginPaths.cs`, `IOsVersionProbe.cs`, `OsVersionDescriptor.cs`, `IOperatingSystemInfo.cs`, `IHostRuntimeInfo.cs`, `IServiceLifecycleController.cs`, `INativeLibraryResolver.cs`
- `Caching/ICacheProvider.cs`, `ICache.cs`, `ICacheOfT.cs`, `ISelfRefreshingCache.cs`
- `Throttling/IRateLimiter.cs`
- `Serialization/IJsonSerializer.cs`
- `Naming/IDiacriticFoldingProvider.cs`

**Edits:** `Arronix.Abstractions.csproj` → `<Version>0.2.0</Version>`; `docs/contracts/stability.md` (0.2.0
version-history entry, `[Experimental]` now implemented, note that the Plugin Loader must refuse an
experimental contract for a plugin whose range extends past the current MINOR);
`src/Arronix.Abstractions/README.md`.

**Done when:** Abstractions builds green with **zero package references still**; new contract tests in
`Arronix.Abstractions.Tests` assert every new public type carries `[Experimental]`.

---

### WP03 — Configuration, composition, global primitives

**Inputs:** §3.2 `Configuration/` and `Composition/` rows.

**Creates:**
- `src/Arronix.Common/Configuration/HostIdentityOptions.cs` — **the de-branding injection point**
- `Configuration/LoggingOptions.cs`, `ConsoleLogFormat.cs`, `FileSystemOptions.cs`, `HttpClientOptions.cs`, `ProxyOptions.cs`, `RedactionOptions.cs`
- `Composition/ArronixCommonServiceCollectionExtensions.cs` (`AddArronixCommon`) — starts nearly empty and grows one registration per subsequent WP
- `Composition/ArronixCommonOptionsValidation.cs`
- `src/Arronix.Common.Tests/Configuration/OptionsValidationTests.cs`

**Done when:** every option type validates via DataAnnotations with `ValidateOnStart`; no option property is
nullable-with-a-fallback; `AddArronixCommon` compiles and registers the options.

---

### WP04 — Text, Collections, Time, Net, Xml primitives

**Inputs:** §3.2 rows for `Text/`, `Collections/`, `Time/`, `Net/`, `Xml/`; §5.

**Creates:**
- `Text/StringExtensions.cs` (survivors only; **no static constructor**), `Text/StringDistance.cs`, `Text/ByteSizeExtensions.cs` (KiB/MiB/GiB), `Text/NullableParseExtensions.cs` (with the `ParseDouble` fix)
- `Collections/EnumerableExtensions.cs` (`AddIfNotNull`, `None`, `ToDictionaryIgnoreDuplicates` only)
- `Time/DateTimeExtensions.cs` (`TimeProvider`-bound)
- `Net/IpAddressExtensions.cs`, `Net/HostAddressExtensions.cs`, `Net/UriValidationExtensions.cs`, `Net/DnsEndPointExtensions.cs`
- `Xml/XElementExtensions.cs` (`FindDescendants`, `[NotNullWhen(true)] out string?`), `Xml/Utf8StringWriter.cs`
- Matching fixtures under `src/Arronix.Common.Tests/{Text,Collections,Time,Net,Xml}/`

**Done when:** the four named correctness fixes (KiB labeling, `ParseDouble` thousands separator,
`FindDescendants` spelling, `TryGetAttributeValue` nullability) each have a regression test.

---

### WP05 — Serialization

**Inputs:** §3.2 `Serialization/` rows; §5 converter verdicts.

**Creates:**
- `Serialization/JsonDefaults.cs` (compact by default; an `Indented` variant; `[JsonPolymorphic]` instead of the cast-to-object trick; **no `where T : new()`**)
- `Serialization/ArronixJsonSerializerContext.cs`
- `Serialization/SystemTextJsonSerializer.cs` (implements `IJsonSerializer`)
- `Serialization/JsonCloner.cs`
- `Serialization/JsonSerializationExtensions.cs`
- `Serialization/Converters/UtcDateTimeJsonConverter.cs` (invariant-culture parse)
- `src/Arronix.Common.Tests/Serialization/JsonDefaultsTests.cs`, `UtcDateTimeJsonConverterTests.cs`, `BclConverterRedundancyTests.cs`

**Done when:** `BclConverterRedundancyTests` re-verifies on the build machine that built-in STJ round-trips
`Version` and `TimeSpan` byte-identically (locking in the two DROP decisions), and `Converters/` contains
**exactly one** file.

---

### WP06 — Hashing, Naming, Archives

**Inputs:** §3.2 rows for `Hashing/`, `Naming/`, `Archives/`.

**Creates:**
- `Hashing/StableHash.cs` (UTF-8, `XxHash3`, static hash APIs — no locked shared instance)
- `Hashing/IFileHasher.cs` (**Tier B, Common-internal**), `Hashing/FileHasher.cs` (`XxHash128` default, SHA-256 option, async)
- `Naming/TextFolding.cs` (FormD + `CharUnicodeInfo`), `Naming/DefaultDiacriticFoldingProvider.cs` (eth/thorn), `Naming/TokenSanitizer.cs`
- `Archives/IArchiveService.cs`, `Archives/ArchiveService.cs` (`ZipFile` / `TarFile` / `GZipStream`, async, `CancellationToken`)
- `src/Arronix.Common.Tests/Archives/ZipSlipTests.cs` — **explicitly asserts a traversal entry is rejected**
- `src/Arronix.Common.Tests/{Hashing,Naming}/` fixtures

**Done when:** `ZipSlipTests` proves the legacy `ExtractZip` traversal defect cannot recur;
`Diacritical.Net` appears nowhere.

---

### WP07 — FileSystem core

**Inputs:** §3.1 and §3.2 `FileSystem/` rows. Largest package; may be split into 7a (paths) and 7b (I/O).

**Creates:**
- `FileSystem/PathExtensions.cs` (**`GetDirectoryName` → `GetLeafName`**), `PathComparer.cs` (nullable contract + surrogate fix), `PathGuard.cs` (`[CallerArgumentExpression]`), `PathLimits.cs` (**no `Enable()`**), `PathValidationScope.cs`
- `FileSystem/IHostFileSystem.cs`, `HostFileSystemBase.cs` (options-driven probe filename, async backoff, permissions delegated to `IFilePermissions`)
- `FileSystem/ScopedFileSystem.cs` — the capability enforcement point
- `FileSystem/FileTransferService.cs` (options-bound ignore list, threshold, temp suffix)
- `FileSystem/DriveInfoStorageMount.cs`
- `FileSystem/FileSystemEntry.cs`, `FileSystemEntryKind.cs` (no `Parent`), `FileSystemListing.cs`, `IFileSystemBrowser.cs`, `FileSystemBrowser.cs`
- `FileSystem/SystemPathCatalog.cs` (options-bound; **no vendor folder names compiled in**)
- `FileSystem/StreamExtensions.cs` (`CopyTo` + `ToBytesAsync`)
- `src/Arronix.Common.Tests/FileSystem/ScopedFileSystemTests.cs` — **asserts escape attempts outside granted roots are rejected**, plus fixtures for the rest

**Done when:** `ScopedFileSystem` rejects `..` traversal, absolute paths outside roots, and symlink escapes;
no vendor or product string appears in `SystemPathCatalog`.

---

### WP08 — Hosting

**Inputs:** §3.1 and §3.2 `Hosting/` rows.

**Creates:**
- `Hosting/IAppPaths.cs` (Tier B), `AppPaths.cs` (`HostIdentityOptions`; `AppContext.BaseDirectory`)
- `Hosting/PluginPaths.cs` (implements `IPluginPaths`)
- `Hosting/IAppDataInitializer.cs`, `AppDataInitializer.cs` (**ensure-exists + writability + permissions only — no migration branches**)
- `Hosting/AppVersionInfo.cs`, `OperatingSystemInfo.cs` (instance half only), `HostRuntimeInfo.cs`
- `Hosting/HostStartupOptions.cs`, `HostStartupException.cs`
- `Hosting/IPidFileWriter.cs`, `PidFileWriter.cs`
- `src/Arronix.Common.Tests/Hosting/HostIdentityTests.cs` — parameterizes the identity and asserts the PID file name, log prefix, folder name and User-Agent all change together

**Done when:** changing `HostIdentityOptions.ApplicationName` in a test changes every derived name, proving the
de-branding is genuinely single-point.

---

### WP09 — Processes

**Inputs:** §3.2 `Processes/` rows.

**Creates:**
- `Processes/IProcessRunner.cs` (**Tier B, Common-internal**), `ProcessRunner.cs` (async capture, `IDictionary<string,string>` env, identity from options)
- `Processes/IProcessManager.cs`, `ProcessManager.cs` (find/kill/exists — host-trusted, never plugin-visible)
- `Processes/ProcessResult.cs`, `ProcessOutputLine.cs`, `ProcessStreamKind.cs`, `ProcessDescriptor.cs`
- `Processes/ProcessNameExtensions.cs`, `IBrowserLauncher.cs`, `BrowserLauncher.cs`
- `src/Arronix.Common.Tests/Processes/` fixtures

**Done when:** capture is async (`WaitForExitAsync`), no `StringDictionary` appears, and `IProcessManager` is
absent from `Arronix.Abstractions`.

---

### WP10 — Caching, Threading, Throttling

**Inputs:** §3.1 and §3.2 rows for `Caching/`, `Threading/`, `Throttling/`.

**Creates:**
- `Caching/CacheProvider.cs`, `ExpiringCache.cs` (`TimeProvider`; `Find` returns `T?`), `SelfRefreshingCache.cs` (**single-flight refresh — no stampede**)
- `Threading/Debouncer.cs` (`TimeProvider`, `IDisposable`, `System.Threading.Lock`, balanced pause counter), `KeyedLockPool.cs` (generic, async, evicting), `TaskExtensions.cs` (`LogFailures(ILogger)`)
- `Throttling/KeyedRateLimiter.cs` (`PartitionedRateLimiter`, lease-based, **no `Thread.Sleep`**), `RateLimitPartitionKey.cs`
- `src/Arronix.Common.Tests/{Caching,Threading,Throttling}/` fixtures using `FakeTimeProvider`

**Done when:** a concurrency test proves `SelfRefreshingCache` invokes its fetch delegate exactly once under N
concurrent expiries; no test sleeps.

---

### WP11 — Diagnostics, Redaction, Telemetry

**Inputs:** §3.1 and §3.2 `Diagnostics/` rows; §4 telemetry seams.

**Creates:**
- `Diagnostics/Redaction/LogRedactor.cs` (rule-driven, `[GeneratedRegex]`, **IPv6 masking**), `RedactionRuleCatalog.cs` (generic rules only), `RedactionRuleCompiler.cs`
- `Diagnostics/UnhandledExceptionRegistrar.cs` (idempotent, `ILogger`, no `Console.WriteLine`, no SignalR branch), `IUnhandledExceptionFilter.cs`
- `Diagnostics/EventDebouncer.cs` (`TimeProvider`, TTL from options), `ExceptionExtensions.cs`, `ProgressReporter.cs`
- `Diagnostics/Telemetry/TelemetryEmitter.cs`, `TelemetrySinkRegistry.cs`, `TelemetryEnrichmentPipeline.cs`, `OsEnvironmentEnricher.cs`
- `src/Arronix.Common.Tests/Diagnostics/RedactionTests.cs` — **includes an IPv6 case and a fail-closed case**; `TelemetryPipelineTests.cs`

**Done when:** redaction is on by default (not production-gated), IPv6 addresses are masked, redaction runs on
property values **before** serialization, and no vendor-specific rule exists in the catalog.

---

### WP12 — HTTP: transport, proxy, gateway

**Inputs:** §3.1 and §3.2 `Http/`, `Http/Proxy/`, `Http/Transport/`, `Security/` rows.

**Gate:** `src/Arronix.Common.Tests/Http/UriExtensionsGoldenTests.cs` — ported from
`NzbDrone.Common.Test/Http/HttpUriFixture.cs` — **must be written and passing before `UriExtensions` is
implemented.** Scheme-relative `//host/p`, root-relative `/p` and segment templating are the risk cases.

**Creates:**
- `Http/UriExtensions.cs`, `AcceptHeaders.cs`
- `Http/Transport/IHttpTransport.cs`, `SocketsHttpTransport.cs` (`IHttpClientFactory`; **`Range` header implemented**), `HappyEyeballs.cs`, `HappyEyeballsConnector.cs` (delay from options, `ILogger`)
- `Http/Proxy/ProxyConfiguration.cs` (**record; no password-bearing `Key`**), `IProxyConfigurationProvider.cs`, `IWebProxyFactory.cs`, `WebProxyFactory.cs`, `ProxyProtocol.cs`
- `Security/ServerCertificateLoader.cs`, `CertificateLoadException.cs`
- `Http/HttpRequestBuilder.cs` (explicit copy ctor; `MultipartFormDataContent`), `IHttpRequestTemplate.cs`, `HttpRequestTemplate.cs` (sealed), `IUserAgentProvider.cs`, `UserAgentProvider.cs`
- `Http/HttpGateway.cs`, `Http/ScopedHttpGateway.cs`
- `src/Arronix.Common.Tests/Http/` fixtures

**Done when:** golden tests pass; no `HttpUri` type exists; `ProxyConfiguration` has no property containing a
password; `Range` is supported.

---

### WP13 — Composition completion and DI integration test

**Inputs:** all preceding packages.

**Creates:**
- `src/Arronix.Common.Tests/Composition/AddArronixCommonTests.cs`

**Edits:** `Composition/ArronixCommonServiceCollectionExtensions.cs` — final registration set.

**Done when:** a real `ServiceProvider` built from `AddArronixCommon` resolves every Tier A contract and every
Tier B interface exactly once; a test asserts **no** registration is discovered by scanning (every registration
is explicit and enumerable in source).

---

### WP14 — Brand-neutrality and dependency-shape enforcement

**Inputs:** acceptance criteria 4, 5, 6.

**Creates:**
- `src/Arronix.Common.Tests/Governance/BrandNeutralityTests.cs` — greps exported type names, namespaces and the source tree for the forbidden token list
- `src/Arronix.Common.Tests/Governance/DependencyShapeTests.cs` — asserts the referenced-assembly set, that `Arronix.Abstractions` has zero package references, and that `Arronix.Abstractions` does not reference `Arronix.Common`

**Done when:** both fixtures pass and would fail if a `Sonarr` literal or an extra package reference were
introduced.

---

### WP15 — Documentation and repository hygiene

**Inputs:** acceptance criterion 11; the Anti-Drift Safeguards in `.github/copilot-instructions.md`.

**Edits:** `ARCHITECTURE.md` (§2 mermaid node, §3 status-table row for `Arronix.Common`, §11 `network`
capability amendment), `GLOSSARY.md` (Platform Pack, Capability Gate, Redaction Rule, Telemetry Sink, Scoped
File System — **as concepts, never type names**), `README.md`, `CONTRIBUTING.md`,
`.github/copilot-instructions.md`, `docs/contracts/stability.md`.

**Creates:** `src/Arronix.Common/README.md` (purpose, contents by category, plugin-vs-host usage, build/test
commands, GPLv3 + Sonarr attribution footer).

**Done when:** every file named in the Anti-Drift rule is updated in the same PR.

---

## 9. Deferred

Explicitly **not** in v0.1.0, each with the reason:

| Deferred | Why |
|---|---|
| **`src/Arronix.Common.Logging.NLog`** (`LoggingBootstrapper`/`AddArronixLogging`, `RedactingClefLayout`, `RedactingTextLayout`, `RedactingFileTarget`, `ProgressLayoutRenderer`) | The split is correct and non-negotiable — §10 requires that Common's surface name no logging framework. But with no `Arronix.Host` yet, **nothing consumes the layouts**. Landing an unconsumed NLog project now adds three package references and a second assembly for zero benefit. It lands with the host. The redaction *engine* (`LogRedactor`) is framework-agnostic and ships in v0.1.0. |
| **`Pipelines/`** — generic ordered-pipeline runner, `IPipelineStep<TContext>`, `PipelineOutcome`, `PipelineAnnotation` | The Policy Engine is a distinct core component (§3) and owns Policy Step / Policy Chain / Execution Context / Policy Failure (all defined in GLOSSARY, none in Abstractions). A runner with no chain is speculation, which is exactly what the [promotion rule](#the-three-tier-promotion-rule) exists to stop. |
| **Token template engine** (`TokenTemplate`, `TokenFormatter`) | §9 requires validation against `IMediaKind.NamingTokens` plus a host-owned global token set and collision resolution (§4.1 step 5) — that is Policy Engine work. A formatter without validation is precisely what `Expansive` was. `TokenSanitizer` and `TextFolding` ship now because they are media-agnostic and have real consumers. |
| **`IPluginModule` / `IPluginContext`** | Named in README and §4 but **absent from Abstractions today**. They are the Plugin Loader's contract and must be designed by that delivery, not smuggled in as a side effect of an infrastructure library. **This is the #1 blocking dependency:** none of the capability-gated seams are discoverable until it exists. |
| **`IMediaStore`** | §5 / README / GLOSSARY name it; it belongs to the Storage Layer. Common contains no persistence. |
| **`IUpdateChannel`** and the nine update-sandbox path methods | Land only when the update work starts; `UpdateOptions` goes with it. |
| **`IProcessRunner`, `IFileHasher` promotion to Abstractions** | Tier B. No caller exists. `IProcessRunner` in particular may be a capability that should not exist at all — an allow-listed process launcher is the single most dangerous thing in the capability model. |
| **`Microsoft.Extensions.Http.Resilience` / Polly circuit breakers** | The idiomatic net10 route to §12's *"circuit breakers around external providers, shared across plugins when using the same remote"* — but there is no shared-remote consumer yet. Add when the first two indexer plugins hit the same host. |
| **Manifest, capability enum and semver-range types** | `contracts.arronix` range parsing (§4.1 step 2, §8) does not exist in the source material and is net-new Plugin Loader work. |
| **Cron / interval schedule grammar** | `IBackgroundTaskRegistry.RegisterJob` takes a `string schedule` with no defined grammar. That grammar belongs to the Scheduler delivery. |
| **Legacy call-site migration** | Zero legacy files are edited. The 172 `IDiskProvider` dependents and ~1,100 `StringExtensions` call sites stay on `NzbDrone.Common`. Migration is the §13 migration-path work. |
| **A CI workflow for `Arronix.sln`** | Out of scope for this delivery but flagged — see below. |

---

## 10. Decisions and open questions

### 10.1 Decided (2026-07-27)

1. **`network` capability — AMENDED.** ARCHITECTURE.md §11's *"no network unless it declares `indexing`"* was
   wrong: network access is legitimately needed by more than indexers. `network` is now a first-class
   capability implied by any of `indexing`, `metadata`, `download` or `notification`. **ARCHITECTURE.md §11 has
   been amended.**

2. **Twitter/X and OAuth 1.0a — DROPPED.** Neither the provider nor the OAuth 1.0a signing path is carried
   into Arronix. `OAuth/` is DROP, and `Arronix.Plugin.Support.OAuth1` is removed from the named future
   extensions in §4. Future delegated auth should use OAuth 2.0 / PKCE.

3. **CI — DEFERRED.** No `build_arronix.yml` in this delivery; the project is not ready for CI yet. The §7.5
   acceptance criteria are therefore enforced by local build and by the governance tests in WP14
   (`BrandNeutralityTests`, `DependencyShapeTests`) rather than by a pipeline. **Consequence to accept
   knowingly:** nothing prevents a future commit from regressing them until CI exists. Revisit before the
   first tagged release.

4. **Proof implementers — DEFERRED.** No `Arronix.Plugin.Telemetry.Sentry` and no `Arronix.Platform.*` in this
   milestone. **Consequence to accept knowingly:** the capability gate and the platform-pack tier remain
   validated on paper only, so the Tier A contract shapes are unproven by a second implementer. This is
   precisely the risk the three-tier promotion rule exists to contain — it makes the deferral affordable, but
   it does not make it free. Keep Tier A minimal accordingly.

5. **Sentry symbol upload — GATED.** Arronix has no Sentry organization. `src/Directory.Build.props` now sets
   `SentryUploadSymbols` (and `EmbedAllSources`) only when `SentryAuthToken` is non-empty, which repairs the
   Release build — it previously failed with 22 × *"Auth token is required for this request"*. This supersedes
   the earlier proposal to gate the block on `SonarrProject`.

6. **Dependency policy — latest.** The project is open-source and non-commercial, so packages that are free
   for OSS use are acceptable at their latest versions (this covers FluentAssertions 8.x under the Xceed
   license). Two exceptions remain, both recorded in `Directory.Packages.props`: `SixLabors.ImageSharp` is
   held at 3.1.12 because v4 requires a **license key file at build time** — an operational blocker, not a
   permissions one, and resolvable by obtaining a key from Six Labors; and `Mono.Posix.NETStandard` is held at
   `5.20.1-preview` because NuGet's "latest stable" `1.0.0` is a downgrade.

### 10.2 Still open

7. **Does `Arronix.Common` get an `InternalsVisibleTo` for its test project?** Tier B interfaces are public
   today, which keeps tests simple but enlarges the assembly's surface. **Recommendation: keep them public** —
   `Arronix.Host` is a separate assembly and needs them, and a host-side assembly's public surface carries no
   stability promise. Proceeding on the recommendation unless overridden.

8. **`HashConverter` / `StableHash` algorithm change.** Adopting `XxHash3` is free for Arronix (no persisted
   synthetic ids exist yet), but the legacy `GetHashInt31` values *are* persisted as queue ids. The v4→v5
   migration tool (§13) is assumed to own that compatibility problem, and Arronix.Common is assumed not to
   need to reproduce the legacy digest. Proceeding on that assumption; flag if wrong.

9. **The legacy tree still reports to the upstream project's Sentry.**
   `src/NzbDrone.Common/Instrumentation/NzbDroneLogger.cs:76-83` hardcodes four `sentry.sonarr.tv` DSNs, so a
   Release build of this fork sends its crash reports and error events to the **Sonarr project's** Sentry
   rather than anywhere Arronix controls. Out of scope for this delivery (it is legacy runtime behavior, not
   Arronix.Common), but it should be resolved before any Arronix build is distributed. Arronix.Common's own
   design already prevents a recurrence: §10 of ARCHITECTURE.md and the `Diagnostics/Telemetry/` charter
   forbid Core naming any concrete sink or endpoint.

---

*Plan complete. Decisions section updated 2026-07-27.*
