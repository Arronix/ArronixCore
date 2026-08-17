# Arronix Observability — Logging, Telemetry, Diagnostics and Support Bundles

> **Status:** Design. This document is the direct input to the implementation phase for the consuming end of
> the telemetry seams that `Arronix.Common` v0.1.0 shipped and deliberately left unconsumed.
>
> **Scope:** one new project (`src/Arronix.Logging.NLog`), the unbuilt `Diagnostics/` half of
> `src/Arronix.Common` (extraction plan WP11), new `Diagnostics/`, `Observability/` and extended `Health/`
> folders in `src/Arronix.Host`, and four **additive** contract edits to `Arronix.Abstractions` 0.3.0.
> **No new contract area, no new `ARX` diagnostic id.**
>
> **Governing documents:** `ARCHITECTURE.md` §10 (Telemetry & Health), §11 (Security & Isolation), §12
> (Error Handling); `docs/arronix-common-extraction-plan.md` (three-tier promotion rule, resolutions #6 and
> #9); `docs/contracts/stability.md` (experimental gate, stable-list rule);
> `docs/design/unified-host-runtime.md` (resolutions #24 and #47, §3.7, §4.7, §4.9, §6.6).
>
> **Depends on, does not duplicate:** `docs/design/pwa-and-push.md` (§4.5–4.7 VAPID custody, push endpoints
> as secrets, `WebPushHealthContributor`), `docs/design/storage-layer.md` (the acquisition record that
> carries a correlation id across a restart; connection-string secrets),
> `docs/design/acquisition-pipeline.md` (the hop names in §3.3), `docs/design/threat-model.md` (the adversary
> this document's §4 assumes), `docs/design/plugin-distribution.md` (the plugin inventory a bundle reports).
> Where those documents are silent the dependency is named here rather than guessed at.
>
> **Empirical basis:** the six survey reports, plus direct reading of the legacy stack at
> `src/NzbDrone.Common/Instrumentation/` (1,193 lines across 16 files) and
> `src/NzbDrone.Core/HealthCheck/` (26 checks + the aggregation service).

---

## How the contested decisions were resolved

Format matches `docs/arronix-common-extraction-plan.md` §"How the three proposals were resolved" and
`unified-host-runtime.md` §1. Where the expected answer is rejected, the row says so.

| # | Question | Options | Resolution | One-line reason |
|---|----------|---------|------------|-----------------|
| 1 | NLog v6, or `Microsoft.Extensions.Logging` with a first-party structured sink? | (a) NLog as provider behind MEL. (b) Write our own `ILoggerProvider` with a rolling-file writer. | **(a) NLog as the provider, MEL as the only API anyone codes against** | MEL ships **no file provider at all** — choosing (b) means owning rolling files, archive renaming, retention sweeps, crash flush and cross-process locking, which is the one part NLog has hardened for fifteen years; and NLog 6.1.4 is already registered (`Directory.Packages.props:7`), so this costs **zero new packages**. |
| 2 | Which assembly may name NLog? | `Arronix.Common` · a `Arronix.Common.Logging.NLog` sub-assembly · a standalone `Arronix.Logging.NLog` | **Standalone `src/Arronix.Logging.NLog`, referenced only by the composition root** | `Arronix.Common.*` reads as part of Common and invites the mistake `ProjectShapeTests` exists to prevent; and if `Arronix.Host` referenced it, swapping the stack would rebuild the host. Deviates from extraction-plan #9's *name*, not its *split*. |
| 3 | Does the console use the framework's console formatters or NLog's? | In-box `SimpleConsoleFormatter`/`JsonConsoleFormatter` · NLog `ColoredConsoleTarget` | **NLog, for every destination** | Two renderers means two places redaction must be applied — exactly the legacy bug where `CleansingClefLogLayout` and `CleansingConsoleLogLayout` each reimplemented cleansing and each gated it on `IsProduction`. One renderer, one chokepoint. |
| 4 | Is redaction gated on environment? | Production only (legacy) · always | **Always, already decided in `RedactionOptions.cs:9-11`; this document adds the enforcement** | A developer machine and a support bundle are precisely where a captured credential travels furthest; `CleansingClefLogLayout.cs:14` and `CleansingConsoleLogLayout.cs:17` had it backwards. |
| 5 | Correlation id format | `Guid.CreateVersion7()` · W3C 32-hex trace id | **W3C trace id, minted with `ActivityTraceId.CreateRandom()`** | It costs nothing (BCL), and when an OTLP exporter is eventually added the correlation id **is** the trace id — no mapping table, no rewrite. A Guid would need one. |
| 6 | How does the id travel inside the host, given #24 rejected scoped DI? | Thread it by hand · `AsyncLocal` service · `Activity.Current` | **`Activity.Current`, which is the BCL's `AsyncLocal` and is not DI** | #24's objection was that a *DI scope* cannot reach plugins; `Activity.Current` is host-internal plumbing whose **value is the published `Parameters` key**, so there is still exactly one id and one published mechanism. |
| 7 | Do we propagate `traceparent` to indexers and download clients? | Yes (standard practice) · no | **No — recommended against, deliberately** | A remote that logs `traceparent` can link one user's searches across sessions and hosts; we get the same diagnostic value by recording the id on **our** request line, and correlation across a trust boundary buys nothing when the far side ships us no traces. |
| 8 | Following one acquisition from search to file, across a download that completes hours later in a different job | One long-lived correlation id · correlation + causation chain · correlation + a separate acquisition id | **Two ids: `correlationId` (this job's trace) and `acquisitionId` (minted at grab, persisted on the acquisition row)** | A causation chain answers "what caused this" one hop at a time and needs N joins; the import job legitimately has its own trace while processing N downloads with N different origins. Two fields, each answering one question. |
| 9 | Does `LogRedactor` exist today? | The brief says yes | **No — verified absent; this document specifies it** | `src/Arronix.Common/` has eleven folders and no `Diagnostics/`; extraction-plan WP11 (`:1017`) is unbuilt. Only the *seams* (`IRedactionRuleProvider`, `RedactionRule`, `RedactionOptions`) shipped. WP-O2 is therefore a prerequisite, not an enhancement. |
| 10 | Are plugin redaction rules scoped to that plugin's output? | Scoped · global | **Global — scoping is impossible without breaking the control** | An indexer plugin's passkey appears in the **host's** HTTP log line, not the plugin's; a scoped rule would leave exactly the leak that matters unredacted. |
| 11 | Then how is a plugin prevented from reading another's secrets? | Scope the rules · make the redactor unreachable | **`ILogRedactor` is not on `IPluginContext`, and rule sets are write-only** | A plugin holding `Redact()` can binary-search another plugin's secret by feeding candidate strings and observing which are masked. Denying the *oracle* is the control; the rules themselves are shapes, not secrets. |
| 12 | Is regex redaction the primary control? | Yes · no, elide at the source | **No — secrets are elided at the source; the catalog is defense-in-depth** | Fields declared `IsSecret` in the settings schema and `OutboundHttpRequest.Url` never reach a layout unmasked; regex only has to catch leaks through free text (exception messages, third-party library logs, echoed response bodies). |
| 13 | Behavior when a redaction rule times out or throws | Emit raw · emit the line without that rule · suppress the line | **Suppress the line, naming the rule** | The legacy engine has no timeout and no failure path; a plugin-supplied regex is untrusted input and catastrophic backtracking is a denial-of-service *and* a leak if it fails open. |
| 14 | Do we log to the database, as all five *arrs do? | Yes (`DatabaseTarget.cs`) · no | **No — recommended against** | Every Info line becomes an `INSERT` competing with the acquisition workload for the same writer lock, and its only unique benefit (a searchable paged log UI) is served better by reading the file with a correlation column in it. |
| 15 | Default file format | Text · CLEF | **Text, with `Clef` opt-in — but the text layout gains a correlation column** | Self-hosted operators `cat` the log; JSON-per-line is worse there. `${correlationId}` as a fixed field makes `grep <id> arronix.txt` work, which is the actual workflow. |
| 16 | Retention policy | Count of rotated files (current `LoggingOptions`) · age · both | **Both, whichever binds first** | Count alone means a burst of errors silently destroys a week of history in twenty minutes; age alone gives no disk bound. |
| 17 | Raising verbosity to Trace | Sticky until changed · expires | **Expires — `VerboseAutoOffAfter`, default 1 hour** | The most common self-hosted support failure is "I enabled trace logging six months ago and the disk is full." Verbosity is an act with an expiry. |
| 18 | Per-subsystem verbosity keys | .NET logger namespaces · a stable subsystem vocabulary | **A stable vocabulary (`indexers`, `imports`, `http`, …) mapped to logger prefixes, plus `plugin:{id}`** | A user should not have to know `Arronix.Host.Providers.IndexerDispatcher`; and plugin verbosity **cannot** be a logger-name rule at all (see #19). |
| 19 | How is a single plugin's verbosity raised, given plugins get no `ILogger` (§3.7 / #47)? | Logger-name rule · a filter inside the emitter | **An `ITelemetryEventFilter` registered by the host, comparing severity against the per-plugin override** | Everything a plugin says arrives through one `ITelemetryEmitter`, so NLog sees one logger name; the level decision has to happen before the emitter writes. |
| 20 | Is a support bundle's redaction verified or assumed? | Assumed (redact and ship) · re-scan with the same rules · re-scan **plus** a known-secret canary sweep | **All of the last: redact at source → `Inspect` every entry → sweep for the literal secret values the host actually holds** | Regexes only catch shapes they know; the canary sweep uses **ground truth**, so it catches the entire class of "a plugin's secret in a shape no rule anticipated". A failed verification fails the bundle — it never ships "best effort". |
| 21 | Where is a generated bundle stored? | Web root · `AppDataFolder/support/` behind a one-time token | **`AppDataFolder/support/`, one-time token, 10-minute expiry, deleted after download** | Legacy serves log files by filename from a static mapper (`LogFileControllerBase.cs:44-47`) — a directory of secrets behind whatever auth the frontend happens to have. |
| 22 | OpenTelemetry now or later? | Ship OTLP exporters now (ARCHITECTURE §10 names them) · defer | **Defer the exporter; ship the *instruments* now** | `System.Diagnostics.Metrics` and `ActivitySource` are in the `net10.0` shared framework and are **inert until something listens**, so the entire integration surface exists at zero cost; the exporter is two unregistered packages plus a gRPC/protobuf surface for zero demonstrated users. |
| 23 | Health notification trigger | On observation (legacy `HealthCheckFailedEvent`) · on confirmed state transition | **On confirmed transition — ≥2 consecutive observations and ≥`ConfirmAfter`** | `HealthCheckService.cs:104-124` fires on every transition into failure keyed on the check's own name, so a flapping indexer notifies on every flap; legacy debounces the *check run* (`:53`, 5 s), not the *conclusion*. |
| 24 | Health deduplication key | The check id · the **subject** the check is about | **The subject — a new mandatory `HealthCheck.SubjectId`** | One dead download client fails seven of Sonarr's checks simultaneously (`DownloadClientCheck`, `…StatusCheck`, `…RootFolderCheck`, `…RemovesCompletedDownloadsCheck`, `…SortingCheck`, `ImportMechanismCheck`, `RemotePathMappingCheck`) — seven notifications, one cause. |
| 25 | Which severities may notify? | All (legacy) · `Error` and `Critical` only | **`Error` and `Critical` only, with an operator-raisable threshold** | Warnings are the fatigue source; a user who cannot silence warnings silences the whole notification channel. |
| 26 | Do announcement/update-nag checks ship? | Yes (four of the five *arrs have one) · no | **No — recommended against, permanently** | A check that fires on a correctly configured install is a product announcement wearing a health check's clothes; this fork already disabled `ServerSideNotificationService` for beaconing, and this generalizes the rule. |

---

## 1. Where this starts from, verified against the tree

### 1.1 What exists

`src/Arronix.Abstractions/Telemetry/` — the complete sink-agnostic seam set, all `[Experimental(ARX0011)]`:
`ITelemetryEmitter` (one method, `Emit`), `ITelemetrySink` (`SinkId`, `SendAsync`, **`FlushAsync` as a
first-class member** — `ITelemetrySink.cs:34-38` records why), `ITelemetryEnricher`, `ITelemetryEventFilter`,
`TelemetryEvent` (`EventId`, `Timestamp`, `Severity`, `Message`, plus `Fingerprint`, `Tags`, `Exception`,
**`CorrelationId`** — `TelemetryEvent.cs:46`), `TelemetrySeverity`.

`src/Arronix.Abstractions/Diagnostics/` — `IRedactionRuleProvider` (`Rules`, "evaluated once during
composition", "a provider cannot remove or weaken a rule contributed by another" — `:9-12`),
`RedactionRule(RuleId, Pattern, SecretGroupName = "secret", IgnoreCase = true)`, `IProgressReporter`
(already carries `CorrelationId` — `IProgressReporter.cs:41`).

`src/Arronix.Common/Configuration/` — `LoggingOptions` (`Level`, `ConsoleLevel`, `ConsoleFormat`,
`RotateAboveBytes` = 1 MiB, `RotatedFileCount` = 5, `WriteToFile`), `RedactionOptions` (`Enabled` = true,
`Replacement` = `"(redacted)"`, `MaskNetworkAddresses`, `DisabledRuleIds`), `ConsoleLogFormat`
(`Standard | Json | Clef`), `HostIdentityOptions.LogFileNameStem`.

`src/Arronix.Abstractions/Health/` — `HealthCheck` / `HealthStatus` / `HealthSeverity` / `CoreErrorCode` are
**stable** (0.1.0); `IHealthContributor` is `[Experimental(ARX0006)]`.

`Directory.Packages.props` — `NLog` 6.1.4 (`:7`), `NLog.Extensions.Logging` 6.1.4 (`:8`),
`NLog.Layouts.ClefJsonLayout` 1.0.5 (`:49`), `NLog.Targets.Syslog` 7.0.0 (`:50`), `Sentry` 6.8.0 (`:51`) —
all registered, none referenced by any `Arronix.*` project.

### 1.2 What does **not** exist, contrary to the brief

**`LogRedactor` has not been built.** `src/Arronix.Common/` contains eleven folders and no `Diagnostics/`
at all; extraction-plan WP11 (`docs/arronix-common-extraction-plan.md:1017`) is unshipped, exactly as
`arronix-current-state.md` §5.5 reports ("Only 11 [of 25 folders] exist … `Diagnostics/` (incl.
`Diagnostics/Redaction/` and `Diagnostics/Telemetry/`)"). `RedactionOptions` is bound and validated and
**nothing reads it**. Likewise there is no `TelemetryEmitter`, no `TelemetrySinkRegistry`, no
`EventDebouncer`, no `UnhandledExceptionRegistrar`, no `ProgressReporter`.

This changes the shape of the work: **WP-O2 builds the redaction engine and the emitter, and every later
work package depends on it.** Treating it as already-present would leave §4, §5 and §6 with no
implementation underneath them.

### 1.3 What the legacy stack does, and what is worth keeping

| Legacy | File:line | Verdict |
|---|---|---|
| 25 hard-coded cleansing regexes as static `RegexOptions.Compiled` fields built at type-init | `CleanseLogMessage.cs:10-61` | **Replace.** Vendor-specific patterns (TorrentLeech, IPTorrents, BroadcastheNet, Notifiarr, Discord, Telegram, uTorrent, Deluge, SABnzbd, NZBGet) belong to whoever ships that integration — that is what `IRedactionRuleProvider` is for. Generic shapes stay in the core catalog. |
| Redaction applied only when `RuntimeInfo.IsProduction` | `CleansingClefLogLayout.cs:14`, `CleansingConsoleLogLayout.cs:17,28` | **Invert.** Already decided in `RedactionOptions.cs:9-11`; §4.5 enforces it. |
| IP masking handles IPv4 only, and only after `Auth-… ip` / `from` | `CleanseLogMessage.cs:63,97-106` | **Fix.** IPv6 leaks entirely today — flagged by the extraction plan at `:307` and still true. |
| Three log files at Info/Off/Off with 5/50/50 archives, 1 MiB rotation | `NzbDroneLogger.cs:156-185` | **Keep the shape**, add age-based retention and the correlation column. |
| `AutoFlush = true; KeepFileOpen = false` | `NzbDroneLogger.cs:169-170` | **Revisit** — `KeepFileOpen = false` was needed for the old concurrent-write model; one process owning the log folder can keep the handle open and still flush per line. |
| Sentry DSN read from an environment variable "because logging is registered before configuration binding has run" | `NzbDroneLogger.cs:69-88` | **Fix the ordering, not the workaround.** §2.6. |
| Generic keyed TTL gate misfiled under `Sentry/` | `SentryDebounce.cs:8-31` (1 h prod / 10 s dev, key = joined fingerprint) | **Keep the mechanism**, as `Diagnostics.EventDebouncer` — the extraction plan already reclassified it. |
| Every Info+ line `INSERT`ed into a `Logs` table via a 500 ms async batch wrapper | `NzbDrone.Core/Instrumentation/DatabaseTarget.cs:18-36` | **Drop.** Resolution #14. |
| Log files served over HTTP by filename from a static mapper | `Sonarr.Api.V3/Logs/LogFileControllerBase.cs:44-47`, `Sonarr.Http/Frontend/Mappers/LogFileMapper.cs` | **Drop.** Replaced by the bundle flow (§6.3) and a filtered tail endpoint. |
| Global framework noise filters (`System.*` → Warn, `Microsoft.*` → Warn, `Microsoft.Hosting.Lifetime*` → Info) | `NzbDroneLogger.cs:222-232` | **Keep verbatim in spirit** — these three lines remove most of the third-party noise floor. |

---

## 2. The logging stack

### 2.1 The recommendation

**`Microsoft.Extensions.Logging` is the only logging API any Arronix code compiles against. NLog 6 is the
provider, and it is named in exactly one assembly: `src/Arronix.Logging.NLog`.**

The alternative — a first-party `ILoggerProvider` writing a structured file format — was considered and
rejected on one fact: `Microsoft.Extensions.Logging`'s in-box providers are Console, Debug, EventSource and
EventLog. **There is no file provider.** Choosing it means writing, testing and maintaining size-triggered
rotation, archive renaming, age-based sweeps, flush-on-crash, encoding, and the failure path when the log
volume is unwritable — in a project whose stated position is that the `net10.0` shared framework is checked
first precisely so this kind of code is not written. NLog 6.1.4 is already registered, is the incumbent in
the legacy tree so the team already reads its configuration, and its `FileTarget` is the specific component
that has been hardened.

What NLog is *not* allowed to be is visible. §10's requirement — "no component's public surface names a
concrete sink" — is satisfied structurally, not by convention.

### 2.2 The project split

```text
src/Arronix.Logging.NLog/
├── Arronix.Logging.NLog.csproj          NLog, NLog.Extensions.Logging, NLog.Layouts.ClefJsonLayout
│                                        + ProjectReference: Arronix.Common (for options + ILogRedactor)
├── GlobalUsings.cs
├── ArronixLoggingExtensions.cs          PUBLIC — the entire public surface, one type, two methods
├── Internal/NLogConfigurationFactory.cs internal — builds LoggingConfiguration from LoggingOptions
├── Internal/RedactingTextLayout.cs      internal
├── Internal/RedactingJsonLayout.cs      internal
├── Internal/RedactingClefLayout.cs      internal
├── Internal/CorrelationLayoutRenderer.cs internal — ${arronix-correlation}
├── Internal/SubsystemRuleBuilder.cs     internal — the §5.4 vocabulary → logger-name rules
└── Internal/LoggingReconfigurator.cs    internal — IOptionsMonitor<LoggingOptions> → ReconfigExistingLoggers
```

The entire public surface:

```csharp
// src/Arronix.Logging.NLog/ArronixLoggingExtensions.cs
namespace Arronix.Logging;

/// <summary>
/// Binds the platform's logging options to a concrete logging implementation.
/// </summary>
/// <remarks>
/// No type from the logging implementation appears in any signature here, by design and by test. Replacing
/// the implementation is a one-line change in the composition root and touches nothing else in the tree.
/// </remarks>
public static class ArronixLoggingExtensions
{
    /// <summary>
    /// Installs the minimal console-only configuration used before configuration binding has run, so that a
    /// failure during host construction is still visible. Idempotent.
    /// </summary>
    public static void AddArronixBootstrapLogging();

    /// <summary>
    /// Replaces the bootstrap configuration with the operator's, wires redaction into every layout, and
    /// subscribes to option changes so verbosity can be raised without a restart.
    /// </summary>
    public static ILoggingBuilder AddArronixLogging(this ILoggingBuilder builder, IConfiguration configuration);
}
```

**Reference topology, and who may reference what:**

```
Arronix.Abstractions   ── names nothing. Zero PackageReferences. (already enforced)
Arronix.Common         ── Microsoft.Extensions.Logging.Abstractions only. Owns ILogRedactor,
                          the telemetry emitter, the sink registry. Names no framework.
Arronix.Logging.NLog   ── the ONLY assembly referencing any NLog package.
Arronix.Host           ── must NOT reference Arronix.Logging.NLog. Codes against ILogger<T>.
Arronix.Api            ── the composition root; the ONLY project referencing Arronix.Logging.NLog,
                          and only from Program.cs.
```

`Arronix.Host` is excluded deliberately. If the host referenced the logging assembly, swapping the stack
would relink the host, and "the logging implementation is replaceable" would be an assertion rather than a
property. Only the executable knows what it logs with.

> **Deviation from `docs/arronix-common-extraction-plan.md` §9, declared.** The deferred project is named
> there as `src/Arronix.Common.Logging.NLog`. Renamed to **`Arronix.Logging.NLog`**: `Arronix.Common.*`
> reads as a part of Common, and `ProjectShapeTests` asserts Common's `ProjectReference` set is *exactly*
> `["Arronix.Abstractions"]` — a sibling sharing its prefix invites precisely the edit that breaks it. The
> **split** the plan mandates is adopted unchanged; only the name moves.

### 2.3 The tests that make §10 mechanical

Added to `src/Arronix.Common.Tests/ProjectShapeTests.cs` (which already reads csproj XML directly) and a new
`src/Arronix.Logging.NLog.Tests/`:

1. `LoggingAssemblyIsTheOnlyProjectReferencingALoggingFramework` — enumerate every `src/Arronix.*/*.csproj`;
   the set of projects with a `PackageReference` whose id starts `NLog`, `Serilog`, `log4net` or `Sentry`
   must be exactly `["Arronix.Logging.NLog"]`.
2. `HostAssemblyLinksNoLoggingImplementation` — reflection over
   `typeof(ArronixHostServiceCollectionExtensions).Assembly.GetReferencedAssemblies()`; no name starting
   `NLog`.
3. `LoggingAssemblyExportsNoThirdPartyType` — over `GetExportedTypes()` and every public member's parameter,
   return, property and field type: the declaring assembly must be `System.*`, `Microsoft.Extensions.*` or
   `Arronix.*`. This is the test that keeps `AddArronixLogging` from growing an `NLog.Layout` parameter.
4. `CommonAssemblyNamesNoSink` — `Arronix.Common`'s package set stays the approved eight (already asserted);
   this adds the assertion that no exported member's type name contains `NLog` or `Sentry`.

`Arronix.Common.csproj:16-21` already opts out of the shared Release symbol-upload block on the stated
grounds that "this assembly names no crash-reporting vendor". `Arronix.Logging.NLog` and `Arronix.Host` copy
that block; `Arronix.Api` copies it too, because Arronix operates no crash-reporting service (§7.4).

### 2.4 Configuration, and what each option means concretely

`LoggingOptions` gains four members. `Arronix.Common` is not a contract library and carries no stability
obligation, so these are ordinary additions.

```csharp
// src/Arronix.Common/Configuration/LoggingOptions.cs   (additions)

/// <summary>Format of the log FILES. The console is governed separately by <see cref="ConsoleFormat"/>.</summary>
[EnumDataType(typeof(LogFormat))]
public LogFormat FileFormat { get; set; } = LogFormat.Standard;

/// <summary>
/// How long a rotated file is kept regardless of how many newer files exist. Retention is the earlier of
/// this and <see cref="RotatedFileCount"/>: a count answers "how much disk", an age answers "how far back",
/// and a burst of errors satisfies the count in minutes while destroying a week of history.
/// </summary>
[Range(typeof(TimeSpan), "1.00:00:00", "3650.00:00:00")]
public TimeSpan RetainFor { get; set; } = TimeSpan.FromDays(30);

/// <summary>
/// Per-subsystem minimum severity, keyed by the §5.4 subsystem vocabulary or by <c>plugin:{pluginId}</c>.
/// Hot-reloadable; a change takes effect without a restart.
/// </summary>
public IDictionary<string, LogLevel> Overrides { get; } =
    new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);

/// <summary>
/// How long an override at <see cref="LogLevel.Trace"/> survives before reverting to the configured level.
/// Verbosity is an act with an expiry: the commonest self-hosted support failure is trace logging left on
/// for months. Zero disables the expiry, which is a deliberate, auditable choice.
/// </summary>
[Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
public TimeSpan VerboseAutoOffAfter { get; set; } = TimeSpan.FromHours(1);
```

**Rename `ConsoleLogFormat` → `LogFormat`** in the same change. The enum now governs files as well as the
console and its name would be a lie; Common has no stability policy, so this is free, and the three members
(`Standard | Json | Clef`) are unchanged.

Mapping to NLog, built by `NLogConfigurationFactory`:

| Option | NLog |
|---|---|
| `Level` | `LoggingRule("*", level, appFile)` — and the debug/trace files are registered at `LogLevel.Off` unless `Level` reaches them, exactly as `NzbDroneLogger.cs:158-160` does |
| `ConsoleLevel`, `ConsoleFormat` | `ColoredConsoleTarget` with `RedactingTextLayout` / `RedactingJsonLayout` / `RedactingClefLayout` |
| `FileFormat` | the same three layouts on the file targets |
| `RotateAboveBytes` | `FileTarget.ArchiveAboveSize` |
| `RotatedFileCount` | `FileTarget.MaxArchiveFiles` |
| `RetainFor` | `FileTarget.MaxArchiveDays` |
| `WriteToFile` | file targets omitted entirely — the containerized arrangement `LoggingOptions.cs:53-56` describes |
| `Overrides` | `SubsystemRuleBuilder` emits one `LoggingRule` per entry, inserted **before** the wildcard rule |
| `HostIdentityOptions.LogFileNameStem` | file names — never a literal |

The text layout, with the correlation column that makes `grep` work (resolution #15):

```
${longdate}|${level:uppercase=true}|${logger}|${arronix-correlation}|${message}${onexception:inner=${newline}${newline}[v${assembly-version}] ${exception:format=ToString}${newline}}
```

`${arronix-correlation}` is `CorrelationLayoutRenderer`, which reads `Activity.Current?.TraceId` and falls
back to the `CorrelationId` log-event property, rendering `-` when there is neither. Five pipe-delimited
fields with a fixed order means the log-viewer endpoint (§5.5) parses plain text without a format package.

### 2.5 The emitter — one pipeline, two destinations

`Arronix.Common.Diagnostics.Telemetry.TelemetryEmitter : ITelemetryEmitter` is the single implementation.
Per `unified-host-runtime.md` §3.7 it is also the **plugin's only diagnostic surface**, so the fan-out to
`ILogger` is what gives a plugin a log line without a plugin ever naming a framework.

```csharp
// src/Arronix.Common/Diagnostics/Telemetry/TelemetryEmitter.cs   (Tier B — host-side)
internal sealed class TelemetryEmitter : ITelemetryEmitter, IAsyncDisposable
{
    public TelemetryEmitter(
        ILoggerFactory loggerFactory,
        ILogRedactor redactor,
        EventDebouncer debouncer,
        IEnumerable<ITelemetryEnricher> enrichers,
        IEnumerable<ITelemetryEventFilter> filters,
        ITelemetrySinkRegistry sinks,
        TimeProvider clock,
        IOptionsMonitor<TelemetryOptions> options);

    public void Emit(TelemetryEvent telemetryEvent);        // never throws, never blocks
    public Task FlushAsync(CancellationToken ct = default); // called by the host on shutdown
}
```

Order of operations, and why each step is where it is:

1. **Complete the event.** `EventId` ← `Guid.CreateVersion7()` when default; `Timestamp` ← `clock`;
   `CorrelationId` ← `Activity.Current?.TraceId.ToHexString()` **when the caller left it null**. Step 1 is
   what makes correlation host-enforced rather than plugin-cooperative (§3.2).
2. **Enrich**, in registration order, each receiving the previous output (`ITelemetryEnricher.cs:10-11`).
3. **Redact** `Message` and every value in `Tags` through `ILogRedactor`. Before filters, so a
   plugin-registered filter can never observe an unredacted value — the same oracle argument as #11. Keys
   are not redacted; a key is a schema, and redacting it would break grouping.
4. **Filter.** Any `ITelemetryEventFilter` returning false drops the event for every destination
   (`ITelemetryEventFilter.cs:12`).
5. **De-duplicate** through `EventDebouncer`, keyed on `Fingerprint` joined by `|` (the legacy key,
   `SentryDebounce.cs:21`) or, when the fingerprint is empty, on `Severity + message-template +
   Exception?.GetType().FullName`. TTL from `TelemetryOptions.DuplicateWindow`, default 1 hour, matching
   `SentryDebounce.cs:16`. Dropped duplicates increment a counter that the next surviving event carries as
   `Tags["arronix.suppressed"]`, so "this happened 412 times" is never lost — which the legacy debouncer
   does lose.
6. **Write to `ILogger`**, category `Arronix.Telemetry.{pluginId ?? subsystem}`, with
   `Trace→Trace, Debug→Debug, Info→Information, Warning→Warning, Error→Error, Fatal→Critical`, the
   exception attached,
   and `CorrelationId` / `Fingerprint` / every tag as structured state.
7. **Fan out to sinks** over a bounded `Channel<TelemetryEvent>` (capacity from options, default 1,024,
   `BoundedChannelFullMode.DropOldest` with a dropped-count counter) drained by one background pump. A sink
   that throws is logged once per hour per `SinkId` and never propagates — `ITelemetryEmitter.cs:18-19`
   promises exactly this. `FlushAsync` drains the channel then calls every sink's `FlushAsync` with a
   deadline (`TelemetryOptions.ShutdownFlushTimeout`, default 5 s).

**`TelemetrySeverity` is renumbered, here, with `Trace` at the bottom.** The enum's ordering *is* its
meaning — every filter in §5.4 is a `>=` comparison against it — so a `Trace` member appended at value 5
would be a member that sorts above `Fatal`, and the fix is to number it correctly now rather than to
document the inversion:

```csharp
// src/Arronix.Abstractions/Telemetry/TelemetrySeverity.cs
public enum TelemetrySeverity
{
    /// <summary>Per-item detail, normally sampled away and normally off.</summary>
    Trace = 0,
    /// <summary>Diagnostic detail.</summary>
    Debug = 1,
    /// <summary>A noteworthy but expected occurrence.</summary>
    Info = 2,
    /// <summary>Something unexpected that the platform recovered from.</summary>
    Warning = 3,
    /// <summary>An operation failed.</summary>
    Error = 4,
    /// <summary>The process cannot continue.</summary>
    Fatal = 5
}
```

Nothing persists these values — they are compared, mapped to `LogLevel` (§2.5 step 6, which gains
`Trace→Trace`) and discarded — and no plugin exists to recompile, so the renumbering costs the edit and
nothing else. Waiting for "a plugin that wants it" would be the wrong test twice over: zero demand is what
makes the change free, and the demand cannot appear until the member does.

The alternative previously recorded here — plugins reaching Trace through
`IProgressReporter.Report(ProgressLevel.Trace, …)` — is withdrawn. It routed one severity through a second
representation, so `plugin:{id}: Trace` in `LoggingOptions.Overrides` (§5.4, which filters on
`TelemetryEvent.Severity`) had no severity to compare against and would silently not do what it says.
`IProgressReporter` reports *progress*; `TelemetrySeverity` carries *severity*; each now covers its own
range completely.

### 2.6 Startup ordering

`NzbDroneLogger.cs:69-88` reads the Sentry DSN straight out of an environment variable with the comment
"logging is registered before configuration binding has run". That is the ordering bug, not the fix.

```csharp
// src/Arronix.Api/Program.cs  (the only file in the tree that names the logging assembly)
ArronixLoggingExtensions.AddArronixBootstrapLogging();   // console-only, so a failure below is visible

var builder = WebApplication.CreateSlimBuilder(args);
builder.Configuration
       .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
       .AddEnvironmentVariables("ARRONIX__");

builder.Logging.ClearProviders()
       .AddArronixLogging(builder.Configuration);        // replaces the bootstrap configuration

builder.Services.AddArronixHost(builder.Configuration);
```

`AddArronixBootstrapLogging` installs a console-only configuration at the first line of `Main`, so anything
that fails during configuration binding or host construction is still reported. `AddArronixLogging` then
replaces it wholesale — NLog permits `LogManager.Setup().LoadConfiguration(...)` more than once, and the
bootstrap target is removed rather than left duplicating output.

`UnhandledExceptionRegistrar` (Common, WP-O2) is installed by `AddArronixLogging` and is **idempotent**
(legacy double-subscribes), routes through `ILogger` so redaction applies rather than `Console.WriteLine`,
and hooks `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and `ProcessExit` — the
last calling `TelemetryEmitter.FlushAsync`, which is why `ITelemetrySink.FlushAsync` was made a first-class
member.

---

## 3. Correlation

### 3.1 The identifier

A correlation id is **32 lowercase hex characters**, minted as
`ActivityTraceId.CreateRandom().ToHexString()`. The API and the client display and accept the **first eight
characters** (`a3f19c02`) and resolve a unique prefix; the full value is what appears in logs and on the
wire.

`unified-host-runtime.md` §4.7 already fixes the transport: `JobQueueEntry.CorrelationId` is a
`required string` and `JobExecutionContext.CorrelationId` is a typed member of the record, per resolution
#24 as revised in 0.4.0. (An earlier draft routed it through a published `WellKnownJobParameters` string key
on the `Parameters` bag; that type is deleted.) Nothing here changes that.

One additive const is needed:

```csharp
// src/Arronix.Abstractions/Scheduling/IScheduledJob.cs   (a member on the JobExecutionContext record)
/// <summary>
/// The identifier of the acquisition this work belongs to, when it belongs to one. Distinct from
/// <see cref="CorrelationId"/>: an acquisition outlives the job that started it, survives a restart and is
/// completed by a different job hours later, so the two answer different questions.
/// </summary>
string? AcquisitionId    // 32 hex when present
```

It is a typed member, not a published string key, for the reason `unified-host-runtime.md` resolution #24
gives: everything the *platform* supplies to a run is typed. (An earlier draft of this section added it as
a const on `WellKnownJobKeys`; that class was deleted in 0.4.0.)

And the corresponding telemetry tag names, consts only, no behavior:

```csharp
// src/Arronix.Abstractions/Telemetry/TelemetryTagNames.cs   (ARX0011, additive)
public static class TelemetryTagNames
{
    public const string PluginId      = "arronix.plugin";
    public const string MediaKind     = "arronix.media-kind";
    public const string JobId         = "arronix.job";
    public const string AcquisitionId = "arronix.acquisition";
    public const string ProviderId    = "arronix.provider";
    public const string Suppressed    = "arronix.suppressed";
}
```

### 3.2 The chain, hop by hop

**Origin — exactly four places mint an id, and nowhere deeper does.**

| Origin | Who mints | Note |
|---|---|---|
| Scheduler tick | `JobScheduler`, per `JobQueueEntry` | The id is on the entry before it is queued, so a job that waits three hours behind a throttle key keeps the id it was created with |
| Inbound API request | `Arronix.Api` middleware | Accepts a well-formed `traceparent` from the client and adopts its trace id; otherwise mints. This is the **only** place an externally-supplied id is trusted, and it is validated as 32 hex, non-zero |
| Host startup | `PluginBootstrapper` | One id for the whole activation pass, so every plugin's load diagnostics group |
| Push delivery | the Web Push dispatcher (`pwa-and-push.md` §4.5) | Delivery happens outside any job |

**Inside the host — `Activity.Current`.** The host starts an `Activity` from `ArronixActivitySources.Jobs`
with the entry's trace id as its parent before invoking `IScheduledJob.ExecuteAsync`, and the BCL's
`AsyncLocal` propagation carries it across every `await` for free. Host code threads nothing by hand.

This does not reopen resolution #24. #24 rejected **scoped DI** on the grounds that a DI scope cannot reach
a plugin, which is true. `Activity.Current` is not DI and is not a second published mechanism: plugins never
touch it, its *value* is what goes into `JobExecutionContext.CorrelationId`, and its only purpose is to
spare host code from threading an id through forty call sites. One id, one typed member, one ambient
carrier the host owns.

**Job → plugin work.** The plugin receives the id twice already: on
`JobExecutionContext.CorrelationId` and on
`IProgressReporter.CorrelationId` (`IProgressReporter.cs:41`). It may set `TelemetryEvent.CorrelationId`
explicitly — and if it does not, **the emitter fills it from `Activity.Current`** (§2.5 step 1), which the
host set before calling in. Correlation is therefore a property of the platform, not of plugin diligence.

**Plugin → outbound HTTP.** `ScopedHttpGateway` (`unified-host-runtime.md` §4.2) logs one
`http.request` event per attempt carrying `correlationId`, `plugin.id`, method, scheme+host, redacted path,
status, `elapsed_ms`, `attempt`, `rate_limit_wait_ms` and response byte count.

It does **not** send `traceparent`, `X-Correlation-Id` or any equivalent to the remote (resolution #7). An
indexer that logs the header can link one user's searches across sessions and hosts, and receives nothing
back that we can consume. If a future deployment wants propagation to a *first-party* remote, that is an
`IOutboundHttpInterceptor` the operator installs against a host allow-list — an opt-in that names the remote,
not a default that names all of them.

**Grab → download → import: the `acquisitionId`.** At the moment of a grab the host mints a second id and
persists it on the acquisition row. This is the one hop a correlation id cannot cross: the download completes
hours later, in a different job, possibly after a process restart.

```
search job          correlationId=A                       acquisitionId=—
release decision    correlationId=A                       acquisitionId=—
grab                correlationId=A   acquisitionId=Q  ← minted and persisted here
download poll job   correlationId=B   acquisitionId=Q  ← adopted from the acquisition row
import job          correlationId=C   acquisitionId=Q
file transfer       correlationId=C   acquisitionId=Q
notification        correlationId=C   acquisitionId=Q
```

Both fields appear on every log line, in `TelemetryEvent.Tags`, and in the SignalR envelope.
`correlationId` answers *"what else did this job do?"*; `acquisitionId` answers *"how did this file get
here?"*. A causation chain would answer the second only by walking N joins, and reusing one id for the whole
acquisition would make the import job's trace an unreadable interleaving of N unrelated acquisitions.

> **Dependency on `docs/design/storage-layer.md`.** The acquisition row must carry an
> `AcquisitionId CHAR(32) NOT NULL` with an index, and the download-client poll must return it alongside the
> client's own download id. That column belongs to the storage design and is named here rather than
> specified.

**Domain events and SignalR.** `IDomainEvent` is stable and carries no correlation field, and does not gain
one. The host's event publisher captures `Activity.Current` at publish time and carries the ids on the
envelope; handlers run under a restored `Activity` with the same trace id, so a handler executing later on
the thread pool still joins the trace. The wire type (`Arronix.Abstractions.Wire`, ARX0017, owned by
`unified-host-runtime.md` §6.4) needs two string fields:

```csharp
public sealed record EventEnvelope
{
    // … existing members owned by §6.4 …
    public string? CorrelationId { get; init; }
    public string? AcquisitionId { get; init; }
}
```

**Client.** The client sends `traceparent` on its own API calls (same-origin, our own server — the §7
objection does not apply) and receives both ids on every SignalR envelope. The activity feed renders
`#a3f19c02` as a link that opens the log viewer pre-filtered.

### 3.3 Following one acquisition, end to end

The eleven events a user follows, with the level each is emitted at. Names are stable strings; the
acquisition-pipeline design owns the semantics of the middle rows and is referenced rather than restated.

| # | Event | Level | Carries |
|---|---|---|---|
| 1 | `job.start` | Debug (Info if user-triggered) | `job.id`, `plugin.id`, `trigger` |
| 2 | `search.plan` | Debug | query count, indexer count, `search.kind` |
| 3 | `http.request` | Debug (Warning on failure) | one per indexer per attempt, redacted URL |
| 4 | `parse.result` | Trace per release, Debug summary | title hash, matched/unmatched |
| 5 | `release.decision` | **Info** accepted, Debug rejected | `outcome`, `reason` (closed vocabulary) |
| 6 | `download.grab` | **Info** | `acquisitionId` first appears; indexer, client, size |
| 7 | `download.progress` | Trace | sampled; never per-poll at Debug |
| 8 | `download.complete` | **Info** | `acquisitionId` adopted by a new `correlationId` |
| 9 | `import.decision` | **Info** | `outcome`, `reason`, target level |
| 10 | `file.transfer` | **Info** | mode (`Hardlink`/`Copy`/`Move`), bytes, elapsed |
| 11 | `notification.sent` | Debug | provider id, outcome |

Six Info lines per successful acquisition, which is the whole story and no more of it. Everything else is
one `?level=debug` away.

The user-facing path: the activity feed row for a completed import carries `acquisitionId`;
`GET /api/v1/system/logs?acquisition=Q&level=trace` returns every line from every job that touched it, in
order, across process restarts. That query is the feature — the ids exist to make it answerable.

---

## 4. Redaction as a security control

### 4.1 The control this actually is

Regex redaction is **the last line, not the first**. Ordered by strength:

1. **Elision at the source.** A `SettingsField` declared `IsSecret` is replaced with the redaction
   placeholder by the settings reader on every path that leaves the process — API responses
   (`GET /api/v1/providers/definitions` already elides per `unified-host-runtime.md` §6.6), log lines,
   telemetry tags, support bundles. No pattern is involved and none can miss.
2. **Structural masking.** `ScopedHttpGateway` redacts `OutboundHttpRequest.Url` and applies a header
   deny-list (`Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key`, `X-Auth-Token`,
   `X-Plex-Token`, `Api-Key`) **before** anything is handed to `ILogger`. The VAPID private key never leaves
   `AppDataFolder` (`pwa-and-push.md` §4.6) and push endpoints never enter a `TelemetryEvent.Tag` at all.
3. **The rule catalog**, which therefore only has to catch what escapes through **free text**: exception
   messages that embed a URL, third-party library log lines, response bodies echoed into an error, a plugin
   that formats a message carelessly.

Stating the order matters because it sets the catalog's job correctly. A design that treats regex as *the*
control ends up with 25 vendor-specific patterns and an IPv6 hole — which is exactly what
`CleanseLogMessage.cs` is.

### 4.2 The engine

```csharp
// src/Arronix.Common/Diagnostics/Redaction/ILogRedactor.cs   (Tier B — host-side, NOT promoted)
public interface ILogRedactor
{
    /// <summary>Returns the text with every matched secret replaced. Never throws.</summary>
    string Redact(string text);

    /// <summary>
    /// Reports whether the text still contains anything the rule set matches, and which rules matched.
    /// </summary>
    /// <remarks>
    /// Exists so that verification uses the same compiled rule set as redaction and cannot drift from it.
    /// A separate verifier with its own patterns would be a second, weaker rule set that nobody maintains.
    /// </remarks>
    RedactionOutcome Inspect(string text);
}

public readonly record struct RedactionOutcome(bool ContainedSecret, IReadOnlyList<string> MatchedRuleIds);
```

`RedactionRuleCompiler` compiles each contributed `RedactionRule`:

- **`RegexOptions.NonBacktracking`, always. There is no fallback.** A pattern using lookaround or a
  backreference does not compile under `NonBacktracking`, and such a rule is **rejected**, not compiled
  another way. Every rule also gets a `matchTimeout` of 250 ms, as depth rather than as the control.

  This is the one rule in this section with a security argument behind it, so it is stated rather than
  assumed. `RedactionRule.Pattern` is a raw regex string that a plugin supplies (§4.4) and that runs against
  **every log line in the process**. A backtracking engine on plugin-supplied input is a ReDoS primitive, and
  the blast radius here is unusually bad: §4.6 makes a match timeout *suppress the log line*, so one crafted
  pattern does not merely slow logging down, it blinds a subsystem's logging while looking like a
  performance problem. `NonBacktracking` gives linear-time matching by construction, which is a property no
  timeout can supply.

  The price is real and small: `NonBacktracking` forbids lookaround and backreferences, so a rule author who
  wants "the value after `?` or `&`" writes a capture group instead of a lookbehind. `core/qs-credential`
  and `core/url-basic-auth` in §4.3 are written that way — the leading delimiter is matched into the pattern
  and the secret is named by the `(?<secret>…)` group the compiler already requires, so the delimiter is
  simply not part of what gets replaced. Nothing in the catalog needed a lookbehind for any reason except
  that `CleanseLogMessage.cs:15-60` happens to be shaped that way, and these rules are being written here,
  now, from nothing. Inheriting a pattern shape is not a reason to keep a backtracking engine reachable.

  **This is `threat-model.md` T-12's ruling, and it is adopted here verbatim** — "some rule authors will
  have to rewrite — correct trade, since a redaction rule needing a backreference is a rule that should be a
  host rule." A plugin whose secret genuinely cannot be described without a backreference does not get an
  escape hatch; it gets a host rule, reviewed here, in `RedactionRuleCatalog`.
- A rule that fails to compile — including one rejected for using lookaround or a backreference, which is
  the common case and gets its own message naming the construct — or whose pattern has no group named
  `SecretGroupName`, is **quarantined**: logged once at Warning with its `RuleId` and owner, excluded from
  the set, and reported in the health surface. `RedactionRule.cs:9-12` already anticipates this ("It appears
  in diagnostics when a rule fails to compile"). Quarantine on an unsupported construct is deliberately the
  *same* path as quarantine on a syntax error: a rule the engine will not run is a rule that is not running,
  and there is no third state in which it runs more dangerously.
- Rules are applied in a single pass, ordered by `RuleId` ordinal, so output is deterministic and a later
  rule sees earlier redactions.
- Rules in `RedactionOptions.DisabledRuleIds` are excluded — the operator escape hatch for a false positive
  against one installation's data, without disabling redaction wholesale.
- **A heuristic warning at load:** a pattern containing a literal run of ≥16 alphanumerics outside a
  character class is reported at Warning as "this rule may contain a hard-coded secret". Cheap, and it
  catches the obvious plugin mistake.

Replacement is `RedactionOptions.Replacement` (`"(redacted)"`), fixed-length regardless of the secret, so
the original length is unrecoverable. The **rule that matched is not written inline** — it goes in the
bundle's `redaction-report.json` as a per-file hit count (§6.2), which carries the same diagnostic
information without telling a reader what class of secret sat at that position.

### 4.3 The core catalog

`Arronix.Common.Diagnostics.Redaction.RedactionRuleCatalog` — **generic shapes only**. Every
vendor-specific pattern in `CleanseLogMessage.cs:15-60` (TorrentLeech, IPTorrents, BroadcastheNet, Plex,
Notifiarr, Discord, Telegram, uTorrent, Deluge, SABnzbd, NZBGet) moves to the plugin that ships that
integration. `RedactionRule.cs:20-23` already states the reason: "a component that stops shipping also stops
leaking its pattern into every install."

| RuleId | Shape | Pattern sketch |
|---|---|---|
| `core/qs-credential` | Query-string credential keys | `[?&][a-z_]*(?:apikey\|api_key\|passkey\|token\|access_token\|refresh_token\|auth\|authkey\|torrent_pass\|secret\|passwd\|password\|username\|user\|uid\|account\|sig\|signature)=(?<secret>[^&#\s"']+)` |
| `core/url-basic-auth` | `https://user:pass@host` | `://(?<user>[^:/@\s]+):(?<secret>[^@/\s]+)@` |
| `core/authorization` | `Authorization:` in any rendered header dump | `\bAuthorization\s*[:=]\s*(?:Basic\|Bearer\|Digest\|Negotiate\|vapid\|token)?\s*(?<secret>[A-Za-z0-9+/=_.\-]{8,})` |
| `core/cookie` | Whole cookie line — a cookie *name* is not reliably safe | `\b(?:Set-)?Cookie\s*:\s*(?<secret>.+)$` |
| `core/api-key-header` | `X-Api-Key`, `X-Auth-Token`, `Api-Key`, `Proxy-Authorization` | header name then `(?<secret>\S+)` |
| `core/json-credential` | JSON value under a credential-ish key | `"[a-z_]*(?:password\|passwd\|apikey\|api_key\|token\|secret\|passkey\|auth\|private_key)[a-z_]*"\s*:\s*"(?<secret>[^"]*)"` |
| `core/jwt` | Anything JWS-shaped — VAPID JWTs, OAuth tokens | `\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+` |
| `core/pem-private-key` | Key material in an exception dump | `-----BEGIN (?:EC \|RSA \|)PRIVATE KEY-----(?<secret>[\s\S]+?)-----END` |
| `core/connection-string` | `Password=`/`Pwd=`/`User ID=` in a Postgres or SQLite connection string | `\b(?:Password\|Pwd\|User ID\|Uid)\s*=\s*(?<secret>[^;]+)` |
| `core/user-path` | `/home/alice`, `C:\Users\alice` — PII, not a credential | gated by the new `RedactionOptions.MaskUserPaths` |
| `core/ipv4` | Public IPv4 | `a.*.*.d`, skipping loopback/private/CGNAT/link-local |
| `core/ipv6` | Public IPv6 — **the hole in the legacy engine** | first hextet + `:*:*:` + last hextet, same locality skip |

Two new `RedactionOptions` members go with them:

```csharp
/// <summary>
/// Whether the user component of a home-directory path is masked. Separate from
/// <see cref="MaskNetworkAddresses"/> because a username is personal data rather than a credential, and an
/// operator diagnosing a permissions fault legitimately wants the real path.
/// </summary>
public bool MaskUserPaths { get; set; } = true;

/// <summary>
/// Whether redaction failure suppresses the line. Off means a rule timeout emits the line unredacted, which
/// is a leak; it exists only to reproduce a redaction bug against a known input.
/// </summary>
public bool SuppressOnFailure { get; set; } = true;
```

`MaskNetworkAddresses` keeps its existing semantics and its existing default, and now covers both address
families — `RedactionOptions.cs:39-42` already promises that ("masking only one of them leaks the addresses
of every host that has moved to the other") and the implementation delivers it for the first time.

### 4.4 Host-owned and plugin-owned rules

**Host subsystems register their own.** Redaction rules belong to whoever knows the secret's shape, and that
includes host subsystems, not just plugins:

- `WebPushRedactionRules` — the three push-service endpoint shapes plus the `p256dh` and `auth` JSON keys,
  as specified by `docs/design/pwa-and-push.md` §4.10. That document requires the support bundle to contain
  no endpoint; §6.2 here is how that requirement is *verified*.
- `StorageRedactionRules` — whatever connection-string dialects `docs/design/storage-layer.md` settles on
  beyond `core/connection-string`.
- `ApiRedactionRules` — the session cookie name and the API key, both of which the API layer names.

**Plugins register through the existing surface.** `IPluginRegistry.AddRedactionRules(IRedactionRuleProvider)`
(`unified-host-runtime.md` §3.4) is **ungated**, and correctly so: gating it behind `network` would mean a
plugin unable to declare that capability could not protect its own token. The host rewrites each `RuleId` to
`{pluginId}/{ruleId}` at registration, so collision and shadowing between plugins are impossible and
`DisabledRuleIds` addresses are unambiguous.

### 4.5 The isolation property, stated precisely

The requirement is "a plugin contributes rules for its own secrets **without being able to read anyone
else's**". Rules cannot be scoped (resolution #10 — the leak that matters happens in *host* text), so the
isolation has to come from somewhere else. It comes from four properties, each of which is testable:

1. **The rule set is write-only from a plugin's side.** `IPluginContext` (`unified-host-runtime.md` §3.3)
   exposes no member that enumerates redaction rules, and `IPluginRegistry` has no getter. A plugin cannot
   read another plugin's patterns.
2. **`ILogRedactor` is not on `IPluginContext` and never will be.** This is the load-bearing one. A plugin
   holding `Redact(string)` has an **oracle**: feed candidate strings, observe which come back masked, and
   binary-search another plugin's passkey character by character. Denying the oracle is the control.
   Test: `PluginContextExposesNoRedactionSurface` — reflection over `IPluginContext`'s members asserting no
   member type is `ILogRedactor`, `IRedactionRuleProvider` or `RedactionRule`.
3. **Rules are additive and cannot be weakened.** `IRedactionRuleProvider.cs:10-11` already states it; the
   compiler enforces it by construction (there is no removal API), and `DisabledRuleIds` is **operator
   configuration**, never a plugin input.
4. **Patterns are shapes, not secrets.** They are safe to enumerate to the *operator* — and are still not
   written into a support bundle (§6.2 lists id, owner, quarantine state and hit count only), because a
   carelessly written plugin rule might embed a literal, which is what the §4.2 load-time heuristic warns
   about.

What this does **not** defend against: a hostile plugin already inside the process can read a shared log
file through `IFileSystem` if it holds `storage` and the log folder is inside a granted root. The mitigation
is that `IPluginPaths` is pre-scoped to the plugin's own tree and `AppDataFolder` is host-only
(extraction-plan resolution #2), so the log folder is not reachable — a property owned by
`docs/design/threat-model.md` and depended on here.

### 4.6 Failing closed

| Condition | Behavior |
|---|---|
| A rule's regex times out (250 ms) | The line is replaced with `(log line suppressed: redaction rule {ruleId} timed out)`; the rule is quarantined after three timeouts and reported as a health issue |
| The redactor throws for any other reason | Same suppression, `CoreErrorCode.Unknown`, one Error line per hour |
| `RedactionOptions.Enabled = false` | A Warning on startup **and on every log rotation**; a permanent `HealthSeverity.Warning` check for as long as it is off; **support-bundle generation refuses to run** |
| A rule fails to compile at startup | Excluded, Warning naming rule and owner, health issue attributed to the owner |

`RedactionOptions.cs:23-26` already frames disabling redaction as "a deliberate, auditable act with a narrow
purpose". The three consequences above are what make it auditable rather than merely documented.

### 4.7 Tests

`src/Arronix.Common.Tests/Diagnostics/RedactionTests.cs`:

- A golden corpus of ≥60 inputs — every shape in `CleanseLogMessage.cs:10-61`, the three push endpoint
  shapes, a VAPID JWT, a PEM block, both connection-string dialects — asserting both `Redact` masks and
  `Inspect` detects. **Redaction and detection are tested as a pair**, which is what keeps §6.2 honest.
- **An IPv6 case** and a locality case (loopback and RFC1918 addresses are *not* masked).
- **A rejection case: a rule using a lookbehind and a rule using a backreference are each quarantined at
  compile with the construct named, and neither is compiled by any other option set.** This is the test that
  keeps the §4.2 fallback from growing back, and it is the one T-12 depends on.
- Every rule in `RedactionRuleCatalog` compiles under `RegexOptions.NonBacktracking` — asserted by
  construction over the whole catalog, so a lookaround cannot be introduced into a core rule unnoticed.
- A fail-closed case: a rule whose matching exceeds the 250 ms timeout against a crafted input suppresses
  the line rather than hanging or emitting it (defense in depth — with `NonBacktracking` the timeout should
  be unreachable, and a test that reached it would be reporting a regression in the engine, not in a rule).
- A false-positive corpus: a media title containing `token`, a path containing `Users`, a base64 artwork
  blob — none of which may be masked in a way that destroys the line's meaning.
- No vendor-specific rule exists in `RedactionRuleCatalog` (assert by scanning `RuleId` prefixes).

---

## 5. Levels, volume, rotation and verbosity

### 5.1 The level contract

A level is **a promise about volume at steady state**, not a statement about importance. Stated as budgets
against one reference install: five indexers, RSS every 15 minutes, two download clients, 2,000 library
items, nothing wrong.

| Level | Means | Budget on the reference install | Retained |
|---|---|---|---|
| **Critical** | The process cannot continue | 0/year | forever (Info file) |
| **Error** | A thing the user asked for failed and will **not** be retried automatically | 0/day | `RetainFor` |
| **Warning** | Degraded, self-healing, or a decision is needed later. Rate-limited to one per fingerprint per hour | ≤2/day | `RetainFor` |
| **Info** | One line per **user-meaningful state change** | **0 per hour when nothing changes** | `RetainFor` |
| **Debug** | One line per external interaction and per decision | ≤2,000/hour | shorter window (§5.3) |
| **Trace** | Per-item detail: every parsed title, every rejected specification with its reason, redacted headers, SQL | unbounded | shortest window, auto-off |

**Zero Info lines per hour on an idle install is the acid test**, and it is exactly what the *arrs fail: a
scheduled RSS sync that finds nothing still logs its start and finish. Therefore:

- **Scheduled** job start/finish is **Debug**. **User-triggered** job start/finish is **Info** — a person
  did something and should see that it happened.
- An indexer poll that returns the same result set as last time (compared by a hash of the release ids) is
  **Debug**. A poll that yields something new is **Info**, once, with the count.
- A **daily rollup** at Info, so a quiet day is still auditable in the Info file:
  `"RSS sync: 96 polls across 5 indexers, 312 releases seen, 3 new, 0 failures, 0 rate-limits."`

### 5.2 The polling problem

Continuous polling means the dominant log volume is "nothing happened", and the second-most-dominant is "the
same thing is still broken". Three mechanisms, all of which exist elsewhere in the design and are reused
rather than invented:

1. **Change-gated Info**, above.
2. **Escalation, not repetition.** The first failure of a remote is Warning. Subsequent *identical* failures
   are Debug, and the Warning is re-emitted **only when the backoff level increases**. The scheduler's
   ladder is already `0, 1m, 5m, 15m, 30m, 1h, 3h, 6h, 12h, 24h` (`unified-host-runtime.md` §4.7), so a
   dead indexer produces about **ten** Warning lines over 24 hours instead of ninety-six. The fingerprint is
   `(provider.id, exception type, status code)`; a *changed* failure re-escalates immediately, because a
   remote that changes how it fails has done something new.
3. **Sampling at Trace.** When a search returns more than `TraceOptions.SampleThreshold` (default 200)
   results, per-release Trace lines are sampled 1-in-N and the summary line states the true count **and the
   sample rate**, so the reader is never silently misled about how much they are seeing.

`EventDebouncer` (Common, WP-O2) is the shared mechanism for (2) and for the telemetry pipeline's step 5 —
one keyed TTL gate, `TimeProvider`-driven, used by the logger, the emitter and the health aggregator.

### 5.3 Files, rotation, retention

Three text files sharing `HostIdentityOptions.LogFileNameStem`, mirroring the legacy shape
(`NzbDroneLogger.cs:156-160`) with the retention gap closed:

| File | Min level | Rotate above | Keep | Exists when |
|---|---|---|---|---|
| `arronix.txt` | Info | `RotateAboveBytes` (1 MiB) | `max(RotatedFileCount, RetainFor)` | always, unless `WriteToFile = false` |
| `arronix.debug.txt` | Debug | 5 MiB | 20 files / 7 days | `Level ≤ Debug` or an override reaches Debug |
| `arronix.trace.txt` | Trace | 5 MiB | 20 files / 24 hours | `Level ≤ Trace` or an override reaches Trace |

Retention is **the earlier of count and age**, both directions. Debug and trace files are registered at
`LogLevel.Off` when not needed rather than omitted, so enabling verbosity does not require rebuilding the
configuration graph — the legacy trick at `NzbDroneLogger.cs:158-160`, kept because it is right.

**Crash safety.** `AutoFlush = true` (a log that loses the last few kilobytes before a crash loses the part
that mattered) with `KeepFileOpen = true`. Legacy sets `KeepFileOpen = false` (`:170`) — a hangover from a
concurrent-write model that no longer applies now that exactly one process owns the log folder. If a
separate updater process is ever added it writes to its own folder, as legacy's `RegisterUpdateFile` already
does.

**Framework noise.** `NzbDroneLogger.cs:222-232`'s three global filters are kept verbatim in effect:
`System.*` → Warning, `Microsoft.*` → Warning, `Microsoft.Hosting.Lifetime*` → Info. They remove most of the
third-party noise floor for three lines of configuration.

**No database target.** Resolution #14. `DatabaseTarget.cs:18-36` inserts every Info+ line through a 500 ms
async batch wrapper, competing with the acquisition workload for the same writer lock, and its one unique
benefit is a searchable paged log UI. §5.5 provides that from the file.

### 5.4 Per-subsystem verbosity

`LoggingOptions.Overrides` is keyed by a **stable subsystem vocabulary**, not by .NET namespaces:

```
scheduler  plugins  indexers  downloads  imports  metadata  notifications
http       storage  api       push       health   parsing   naming
```

plus `plugin:{pluginId}` for a single plugin. `SubsystemRuleBuilder` maps each to a logger-name prefix and
emits one `LoggingRule` inserted before the wildcard rule. A user diagnosing a stuck import sets
`imports: Trace` and gets exactly that subsystem, at full detail, without the other twelve.

**`plugin:{id}` cannot be a logger-name rule**, and this is a direct consequence of `unified-host-runtime.md`
resolution #47: plugins get no `ILogger`, so everything a plugin says arrives through one
`ITelemetryEmitter` and NLog sees a single category. The override is therefore implemented as an
`ITelemetryEventFilter` the **host** registers, comparing `TelemetryEvent.Severity` against the per-plugin
floor before the emitter writes (§2.5 step 4). One consequence worth naming: raising a plugin's verbosity
raises it for that plugin's sinks too, because the filter is upstream of the fan-out. That is the right
answer — a sink is a destination, not a second policy.

**Hot reload.** `IOptionsMonitor<LoggingOptions>.OnChange` → `LoggingReconfigurator` rebuilds the rule set
and calls `ReconfigExistingLoggers()`. No restart. Overrides persist; an override at `Trace` is stamped with
an expiry of `VerboseAutoOffAfter` and reverted by a timer, with an Info line stating that it reverted, so a
user who forgets is not punished for months.

**API and UI.** `GET /api/v1/system/logging` returns the effective level per subsystem and any pending
expiry; `PUT /api/v1/system/logging` sets overrides. The settings page renders one row per subsystem with a
level selector and, when Trace is selected, an explicit "reverts in 1 hour" note — the expiry is surfaced,
never silent.

### 5.5 Reading logs without a database

`GET /api/v1/system/logs?level=&subsystem=&correlation=&acquisition=&since=&until=&q=&limit=&cursor=`
streams from the newest file backwards. The fixed five-field text layout (§2.4) parses without a format
package; when `FileFormat = Clef` the same endpoint filters on structured properties instead and gains
tag-level predicates. The filter-by-`acquisition` case is the one from §3.3, and it is the reason the
correlation column was added to the text layout rather than left as a structured-only property.

Log files are **never** served as static content by filename, unlike
`Sonarr.Http/Frontend/Mappers/LogFileMapper.cs`. The only whole-file egress path is the support bundle,
which is verified.

---

## 6. Support bundles

The single highest-leverage diagnostic feature for a self-hosted application, because the alternative is a
support conversation conducted by screenshot.

### 6.1 Contents

`arronix-support-{instanceToken}-{yyyyMMddTHHmmssZ}.zip`:

```text
manifest.json          bundle schema version, generated-at, host version + commit, redaction rule-set digest,
                       the option flags the user chose
environment.json       OS + version, architecture, container detection, .NET version, RID, process uptime,
                       time zone, culture, available disk on each configured root, memory
config.json            the full effective configuration tree, secret values elided at source (§4.1 layer 1)
plugins.json           PluginRuntimeState[] — id, version, state, declared vs granted capabilities,
                       declared contract range, host contract version, defect list, quarantine reason
providers.json         ProviderDefinition[] with IsSecret fields elided, plus status, failure counts,
                       current backoff level, last successful contact
schema.json            migration version per store, table names and row counts — never rows
health.json            the current HealthSnapshot plus each issue's first/last observed and count
jobs.json              registered jobs, parsed schedules, next due, last run, last result, queue depth
                       by throttle key
metrics.json           the §7 instrument snapshot
redaction.json         rule id, owner, quarantine state and reason, hit count
                       per bundle file — never a pattern
redaction-report.json  per-file bytes in/out, total masked count, verifier result, canary sweep result
logs/arronix.txt       current + up to SupportBundleOptions.MaxLogFiles rotated Info files
logs/arronix.debug.txt only when the user opted in
logs/arronix.trace.txt only when the user opted in
```

**Deliberately absent:** the database file (it is the user's library, it is large, and it contains every
provider secret in whatever form the storage layer holds them); media file paths beyond the configured root
shapes; any push endpoint or subscription key (`pwa-and-push.md` requires this and §6.2 verifies it); the
VAPID private key; the API key; session cookies; the canary set itself.

### 6.2 Verification, not assumption

Three layers, in order. The bundle is written to a staging directory and **only then** verified; a failure
means it does not ship.

**Layer 1 — redact at the source.** Every text stream entering the bundle passes through
`ILogRedactor.Redact`, and every structured document is built from already-elided values (§4.1 layer 1).

**Layer 2 — re-inspect with the same rules.** The finished archive is reopened and every entry is run
through `ILogRedactor.Inspect`. Any hit fails the bundle and names the file and the rule. Because `Inspect`
and `Redact` share one compiled rule set, a verifier that disagrees with the redactor is impossible by
construction — which is the entire reason `Inspect` is on the interface rather than living in a test.

**Layer 3 — the known-secret canary sweep.** This is the layer that matters, because layers 1 and 2 can only
catch shapes the rule set anticipates, and a plugin's secret may have no anticipated shape at all.

```csharp
// src/Arronix.Host/Diagnostics/SecretCanarySet.cs
/// <summary>
/// The literal secret values this installation actually holds, gathered immediately before verification and
/// zeroed immediately after.
/// </summary>
/// <remarks>
/// Pattern-based verification can only find secrets whose shape someone predicted. This set is ground truth:
/// if the string is in the bundle, the bundle is wrong, whatever shape it took. It is never persisted, never
/// logged, and never written into the bundle it verifies.
/// </remarks>
internal sealed class SecretCanarySet : IDisposable
{
    public static SecretCanarySet Collect(
        IProviderDefinitionStore providers,     // every SettingsField declared IsSecret
        IHostSecretSource hostSecrets,          // API key, VAPID private key, DB password, session key
        IPushSubscriptionStore push);           // every endpoint + p256dh + auth

    /// <summary>Every canary, plus its base64, base64url, percent-encoded and lowercased forms.</summary>
    public IReadOnlySet<string> Forms { get; }

    /// <summary>Values shorter than 8 characters, which are reported as unverifiable rather than skipped.</summary>
    public IReadOnlyList<string> TooShortToVerify { get; }
}
```

Every bundle entry is scanned for a literal occurrence of any canary form. A hit fails the bundle and names
the file — and additionally files a `HealthCheck.Unhealthy`, because a canary hit is a **live secret-leak
bug in some subsystem**, not merely a bundle problem.

Values under eight characters are not used as canaries (a four-character password false-positives against
everything) and are listed in `redaction-report.json` as unverifiable, so the user is told which of their
secrets could not be checked rather than being allowed to assume all of them were.

**Layer 4 — a golden test with a poisoned corpus.**
`src/Arronix.Host.Tests/Diagnostics/SupportBundleRedactionTests.cs` builds a host whose configuration,
provider definitions, push subscriptions and log files are seeded with ≥40 distinct known secrets in every
shape observed in `CleanseLogMessage.cs`, the *arr survey and `pwa-and-push.md`; generates a bundle; and
asserts zero `Inspect` hits and zero canary hits.

The corpus is **generated from the settings schema**, not hard-coded: it walks every registered
`SettingsField` with `IsSecret` and injects a unique marker. A new secret-bearing subsystem that forgets to
elide therefore fails this test the day it is added, which is the only way a check like this stays true.

### 6.3 The user experience

- **One primary button**, "Download support bundle", on the system page and — the important placement —
  **inline on any health issue**, prefilled with the debug logs for the subsystem that issue belongs to.
  The moment a user needs a bundle is the moment they are looking at the problem.
- **An expanded panel** with three checkboxes: include debug logs, include trace logs, include provider
  settings (redacted). Defaults: Info logs only, config included, provider settings included with secrets
  elided.
- **Generation is a job**, with progress over SignalR: collect → redact → archive → verify. Verification is
  a named step the user watches happen, which is most of why they trust the result.
- **A preview before download**: the entry list with byte sizes, plus the report summary —
  *"3,412 values masked across 7 rules; 0 verification failures; 2 secrets too short to verify."* A user who
  can see what is being sent will send it; a user who cannot, will not.
- **Delivery**: written to `AppDataFolder/support/`, offered via a single-use token valid for
  `SupportBundleOptions.DownloadTokenLifetime` (10 minutes), deleted on download or expiry, and swept on
  startup. Never placed in the web root, never addressable by filename.
- **Refusal paths, all explicit**: verification failure names the file and the class and offers to download
  the bundle *without* that file; `RedactionOptions.Enabled = false` refuses outright and says why; a
  quarantined redaction rule produces a prominent warning in the preview because the bundle was verified
  against a **weakened** rule set.
- **Nothing is uploaded anywhere.** Arronix operates no support service, consistent with the decision
  already taken in this fork for `ServerSideNotificationService` and for the Sentry DSN
  (`NzbDroneLogger.cs:78-87`). The user attaches the file wherever they choose.

```text
POST   /api/v1/system/support-bundle            → 202 + { bundleId }        (authenticated; max 1 concurrent)
GET    /api/v1/system/support-bundle/{id}       → { state, step, bytes, report }
GET    /api/v1/system/support-bundle/{id}/file  → the archive               (one-time token, 10 min)
DELETE /api/v1/system/support-bundle/{id}
```

---

## 7. Metrics and tracing

### 7.1 Instrument now, export later

**Recommendation: define the meters and activity sources in this milestone; do not ship an OTLP exporter.**

ARCHITECTURE §10 names "OTLP exporters" among the extensible sinks. The exporter is deferred because
`OpenTelemetry.Exporter.OpenTelemetryProtocol` and `OpenTelemetry.Extensions.Hosting` are **not registered**
in `Directory.Packages.props`, are not Microsoft-owned in the sense the house rule prefers, and bring a
gRPC/protobuf transitive surface — for a feature with no demonstrated user in a self-hosted media
application.

The instruments are a different matter entirely. `System.Diagnostics.Metrics.Meter` and
`System.Diagnostics.ActivitySource` are in the `net10.0` shared framework, cost **zero packages**, and are
**inert until something listens** — an unlistened `Counter.Add` is a null check. They are also the *entire*
integration surface: adding the exporter later is

```csharp
services.AddOpenTelemetry()
        .WithMetrics(m => m.AddMeter("Arronix.*").AddOtlpExporter())
        .WithTracing(t => t.AddSource("Arronix.*").AddOtlpExporter());
```

in `Arronix.Api/Program.cs` and **nothing else anywhere in the tree**. That is a provable "not a rewrite",
not an aspirational one, and it is the whole reason to instrument now.

### 7.2 The instrument catalog

Meters: `Arronix.Host`, `Arronix.Plugins`, `Arronix.Http`, `Arronix.Indexers`, `Arronix.Downloads`,
`Arronix.Imports`, `Arronix.Storage`. Instrument names follow OpenTelemetry semantic-convention style so a
later exporter needs no renaming.

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `arronix.job.duration` | `Histogram<double>` | `s` | `job.id`, `plugin.id`, `outcome` |
| `arronix.job.failures` | `Counter<long>` | `{failure}` | `job.id`, `failure.class` |
| `arronix.job.queue.depth` | `ObservableGauge<int>` | `{job}` | `throttle.key` |
| `arronix.http.client.duration` | `Histogram<double>` | `s` | `http.request.method`, `server.address`, `http.response.status_code`, `plugin.id` |
| `arronix.http.client.rate_limited` | `Counter<long>` | `{request}` | `server.address` |
| `arronix.indexer.releases` | `Counter<long>` | `{release}` | `indexer.id`, `search.kind` |
| `arronix.release.decisions` | `Counter<long>` | `{decision}` | `outcome`, `reason` |
| `arronix.import.files` | `Counter<long>` | `{file}` | `outcome`, `media.kind` |
| `arronix.import.bytes` | `Counter<long>` | `By` | `media.kind`, `transfer.mode` |
| `arronix.plugin.state` | `ObservableGauge<int>` | `{plugin}` | `plugin.id`, `state` |
| `arronix.health.issues` | `ObservableGauge<int>` | `{issue}` | `status`, `severity` |
| `arronix.redaction.matches` | `Counter<long>` | `{match}` | `rule.id` |
| `arronix.storage.query.duration` | `Histogram<double>` | `s` | `operation` |

**A hard cardinality rule, stated up front because it is cheap now and unfixable later: no tag may take a
value derived from user data.** No media item id, no release title, no file path, no URL path, no user name.
`reason` and `outcome` are closed vocabularies enforced at the call site by enum-to-string mapping. This is
the mistake that makes a metrics backend unusable and it costs nothing to forbid on day one.

`arronix.redaction.matches` deserves its own note: a *rising* match rate for a given rule usually means a
subsystem started leaking through free text, so it is a security signal, not just a statistic.

### 7.3 Activity sources and the correlation bridge

`ArronixActivitySources`: `Arronix.Jobs`, `Arronix.Http`, `Arronix.Import`, `Arronix.Api`. Activities are
started unconditionally — with no listener, `ActivitySource.StartActivity` returns `null` and the call sites
use `using var activity = …` which handles it.

Because the correlation id **is** the W3C trace id (§3.1), tracing is already wired: the day a listener is
attached, every existing log line's correlation column is the trace id of the span that produced it, and
every span carries `plugin.id`, `job.id` and `acquisition.id` as attributes. No mapping, no bridging code.

### 7.4 What is exposed today, with zero packages

A `MeterListener` in `Arronix.Host/Observability/MetricsSnapshotCollector.cs` aggregates counters,
histogram summaries (count, sum, p50/p90/p99 from bucketed values) and observable gauges into a snapshot:

- `GET /api/v1/system/metrics` → JSON, authenticated, and the source of `metrics.json` in the bundle.
- A Prometheus text-format endpoint is **offered but off by default** — the formatter is about sixty lines
  over the same listener and needs no package, but an unauthenticated metrics endpoint is an information
  leak, so when enabled it sits behind the same authentication as the rest of the API and is opt-in per
  `MetricsOptions.PrometheusEndpoint`.

**No crash-reporting sink ships.** `Sentry` 6.8.0 is registered (`Directory.Packages.props:51`) and used by
the legacy tree, but Arronix operates no Sentry instance, and `NzbDroneLogger.cs:78-87` already records why
inheriting the upstream DSNs was wrong. `ITelemetrySink` exists precisely so an operator who runs their own
can register one as a plugin under the `telemetry-sink` capability, and `Arronix.Common.csproj:16-21`'s
symbol-upload opt-out already reflects the position. Nothing in the first-party tree names a crash reporter.

### 7.5 The trigger that un-defers OTLP

A user running a collector who asks for it, **or** the first support case that needs cross-service timing
this design cannot answer from logs. At that point: register two packages, add the four lines above,
change nothing else.

---

## 8. Health checks that do not generate alert fatigue

### 8.1 What goes wrong today, with numbers

Health-check counts, counted on disk: **Radarr 33, Sonarr 26, Readarr 26, Prowlarr 19, Lidarr 18.**

The mechanism (`NzbDrone.Core/HealthCheck/HealthCheckService.cs`):

- Checks run on startup, on a schedule, and event-driven via `[CheckOn]` attributes (`:66-78`).
- Execution is debounced by 5 seconds (`:53`) — the **check run** is debounced, not the conclusion.
- A 15-minute startup grace period (`:58`) suppresses the *first notification* of any issue.
- `HealthCheckFailedEvent` fires the first time a check transitions to failure, keyed on
  `result.Source.Name` (`:104-124`); `HealthCheckRestoredEvent` on recovery. Both flow to notification
  providers.

Three failure modes follow directly:

1. **Flap amplification.** A check keyed on its own name fires a notification on every transition, so an
   indexer that fails and recovers every poll notifies on every poll.
2. **Cause fan-out.** One dead download client fails `DownloadClientCheck`, `DownloadClientStatusCheck`,
   `DownloadClientRootFolderCheck`, `DownloadClientRemovesCompletedDownloadsCheck`,
   `DownloadClientSortingCheck`, `ImportMechanismCheck` and `RemotePathMappingCheck` simultaneously — seven
   notifications, one cause, and the user learns to ignore the channel.
3. **Non-actionable checks.** `UpdateCheck`, `PackageGlobalMessageCheck` and `ServerSideNotificationService`
   fire on installations where nothing is wrong. The last of these is already disabled in this fork for
   beaconing; the general lesson is broader.

### 8.2 A health check is a state, not an event

```csharp
// src/Arronix.Host/Health/HealthIssue.cs   (Tier B — host-side)
public sealed record HealthIssue
{
    public required string CheckId { get; init; }              // "{pluginId}/{checkId}" for plugin checks
    public required string SubjectId { get; init; }            // the thing this is about — §8.3
    public required HealthStatus Status { get; init; }
    public required HealthSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? RemediationHint { get; init; }
    public required DateTimeOffset FirstObservedAt { get; init; }
    public required DateTimeOffset LastObservedAt { get; init; }
    public required int ObservationCount { get; init; }
    public required bool Confirmed { get; init; }              // survived the confirmation window
    public DateTimeOffset? NotifiedAt { get; init; }
    public DateTimeOffset? AcknowledgedUntil { get; init; }
}
```

`HealthAggregator` (introduced by `unified-host-runtime.md` §4.9 and extended here) keeps this store and
runs `HealthTransitionDetector` over it. `HealthCheck` — the contributor-facing DTO — carries the one
shape change in §8.3 and nothing else; contributors keep returning stateless results and the host
owns all the state, which is what lets a badly written contributor be merely noisy rather than harmful.

**Confirmation window.** An issue must be observed at **≥2 consecutive runs** *and* for
`HealthOptions.ConfirmAfter` (default 5 minutes) before it becomes `Confirmed` and is eligible to notify.
Recovery requires ≥1 clean run *and* `HealthOptions.ClearAfter` (default 2 minutes). A flapping indexer
therefore produces zero notifications until it is genuinely down. The legacy debouncer at `:53` debounces
the *check execution*; this debounces the *conclusion*, which is the layer the user experiences.

**Startup grace, made causal.** No health notification fires until `PluginBootstrapper` reports every plugin
Active-or-Quarantined **and** the first full scheduled pass has completed, with a hard 15-minute ceiling. A
fixed 15-minute timer (legacy `:58`) suppresses real issues for fourteen minutes on a fast host and expires
mid-boot on a slow NAS; a causal gate is right on both.

### 8.3 Deduplicating by cause

**One contract change**, and it is the one that fixes the seven-notifications problem:

```csharp
// src/Arronix.Abstractions/Health/HealthCheck.cs
public record HealthCheck(
    string CheckId,
    string Name,
    /// <summary>
    /// The identifier of the thing this check is about — a provider, a plugin, a mount, the host itself.
    /// The host groups issues by subject, so one failing download client reported by six different checks
    /// becomes one notification with five details rather than six notifications.
    /// </summary>
    /// <remarks>
    /// Mandatory. Forms: <c>provider:{definitionId}</c>, <c>plugin:{pluginId}</c>, <c>mount:{path}</c>,
    /// <c>host</c>. A contributor that genuinely reports on nothing but itself passes
    /// <c>plugin:{pluginId}</c>; there is no "unknown" value and no null.
    /// </remarks>
    string SubjectId,
    HealthStatus Status,
    HealthSeverity Severity,
    string? Message = null,
    string? RemediationHint = null,
    DateTime? LastChecked = null);
```

**Why positional and mandatory, not an optional init-only property.** `SubjectId` is the whole mechanism of
§8.3: the aggregator's grouping key. An optional key is a key that most contributors will not set, and every
contributor that does not set it reproduces exactly the notification fan-out this section exists to delete —
silently, and at the point where the design's own acceptance criterion (§9, WP-O9: *one dead download client
failing six checks produces one notification*) stops being enforceable by any test the host can write. The
compiler is the only enforcement that scales to contributors nobody here has read.

There is no null-fallback path and no "fall back to the check id" behavior, because there is no behavior to
fall back to: zero `IHealthContributor` implementations exist, so there is no prior notification shape to be
compatible with and nothing that a stricter contract can break. The three static factories
(`HealthCheck.Healthy` / `Degraded` / `Unhealthy`) each take `subjectId` as their third parameter; a
positional parameter keeps construction in one syntax, where a `required` member on a positional record
would split it across a constructor call and an object initializer.

The aggregator groups confirmed issues by `SubjectId` and notifies **once per subject**, using the
highest-severity issue's message and remediation hint as the headline and the rest as detail. Two
contributors reporting the same subject — say `provider:17` from a reachability check and from a failed
grab — collapse into one notification, which is the point.

### 8.4 Which issues reach the user, and how

| Severity | Channel |
|---|---|
| `Critical` | Notification providers **and** a persistent banner **and** the health page |
| `Error` | Banner + health page |
| `Warning` | Health page + a badge count. **No outbound notification.** |
| `Info` | Health page only, no badge |

`HealthOptions.NotifySeverityThreshold` (default `Error`) lets an operator raise the bar further. Warnings
are the fatigue source, and a user who cannot silence warnings silences the channel.

**Acknowledgment with a deadline.** `POST /api/v1/health/{checkId}/acknowledge?until=` snoozes an issue for
a duration (default 7 days) **or until its message changes**, whichever comes first. Deliberately not
permanent dismissal: a check that a user wants gone forever is a check that should not exist, and giving
them a permanent mute hides that signal from us.

**Actionability is enforced, not encouraged.** `HealthAggregator` logs a Warning at registration for any
contributor that returns a non-healthy `HealthCheck` with a null `RemediationHint`, and the UI renders the
hint as the **primary** text with the message as detail. A check that cannot say what to do is a check that
generates a support request instead of resolving one.

**Contributor containment**, extending `unified-host-runtime.md` §4.9: per-contributor timeout
(`HealthOptions.ContributorTimeout`, 10 s), full exception isolation, `{pluginId}/{checkId}` prefixing — and
new here, **a contributor that throws or times out three consecutive times is quarantined** and replaced
with one permanent issue about itself. Otherwise a broken contributor costs ten seconds of every aggregation
pass forever.

**The budget, stated as the acceptance criterion:** *a correctly configured, fully working install produces
zero health issues above `Info`.* Any check that fires when nothing is wrong is a product announcement
wearing a health check's clothes. Consequently, and recommended explicitly: **no update-availability check,
no announcement check, no telemetry-opt-in nag ships as a health check.** Update availability belongs in a
release-notes surface with its own badge. This generalizes the decision already taken in
`ServerSideNotificationService.cs`, whose disabling comment gives the security half of the same argument.

**Surface** (`unified-host-runtime.md` §6.6 already fixes the first two rows):

```text
GET  /health                                  liveness — HealthSnapshotView, 200 / 200 / 503 (§10)
GET  /api/v1/health                           the issue list with state, counts, first/last seen, ack
GET  /api/v1/health/{checkId}/history         transitions over time
POST /api/v1/health/{checkId}/acknowledge     ?until=
HUB  /hub/events → health.changed             confirmed transitions only, never observations
```

---

## 9. Work packages

Ordered. WP-O2 is the hard dependency for everything after it.

| # | Package | Creates | Depends on | Done when |
|---|---|---|---|---|
| **WP-O1** | Options and contract edits | `LogFormat` rename; `LoggingOptions.{FileFormat,RetainFor,Overrides,VerboseAutoOffAfter}`; `RedactionOptions.{MaskUserPaths,SuppressOnFailure}`; `TelemetryOptions`; `HealthOptions` additions; `SupportBundleOptions`; `MetricsOptions`; Abstractions 0.3.0 contract edits (`HealthCheck.SubjectId` — mandatory, plus its three static factories, `TelemetrySeverity` renumbered with `Trace = 0`, `TelemetryTagNames`, `JobExecutionContext.AcquisitionId`, `TelemetryEmitterExtensions`) | — | `OptionsValidationTests` covers every new member; `ExperimentalSurfaceTests` still passes with **no new ARX id** |
| **WP-O2** | **Redaction + telemetry engine** (extraction-plan WP11, unshipped) | `Common/Diagnostics/Redaction/{ILogRedactor,LogRedactor,RedactionRuleCatalog,RedactionRuleCompiler,RedactionOutcome}`; `Diagnostics/{EventDebouncer,UnhandledExceptionRegistrar,IUnhandledExceptionFilter,ProgressReporter,ExceptionExtensions}`; `Diagnostics/Telemetry/{TelemetryEmitter,TelemetrySinkRegistry,TelemetryEnrichmentPipeline,OsEnvironmentEnricher,CorrelationEnricher}` | WP-O1 | §4.7's ≥60-case corpus passes including IPv6, fail-closed and false-positive cases; `Redact`/`Inspect` agree on every case; no vendor rule in the catalog |
| **WP-O3** | `src/Arronix.Logging.NLog` | The §2.2 file list; the three redacting layouts; `${arronix-correlation}`; `SubsystemRuleBuilder`; `LoggingReconfigurator`; `AddArronixBootstrapLogging` / `AddArronixLogging` | WP-O2 | The four §2.3 shape tests pass; a redaction test written against the **layout** (not the redactor) proves a secret in a message template never reaches the file |
| **WP-O4** | Correlation plumbing | `ArronixActivitySources`; scheduler mints and stamps; API `traceparent` middleware; `ScopedHttpGateway` request logging; `EventEnvelope.{CorrelationId,AcquisitionId}`; the acquisition-id adoption path | WP-O3, host scheduler | An integration test drives search → grab → complete → import across a simulated restart and asserts one `acquisitionId` links all eleven §3.3 events |
| **WP-O5** | Levels, rotation, verbosity | The three file targets; change-gated Info; escalation-not-repetition; Trace sampling; `plugin:{id}` filter; `PUT /api/v1/system/logging`; `VerboseAutoOffAfter` timer | WP-O3 | A 1-hour soak against a mock indexer set produces **zero** Info lines with no state change, and ten Warning lines for a permanently dead indexer |
| **WP-O6** | Log reading | `GET /api/v1/system/logs` with the §5.5 filters; the fixed-format text reader; the CLEF reader | WP-O5 | Filtering by `acquisition` returns all events from §3.3 in order; no static log-file route exists anywhere |
| **WP-O7** | **Support bundles** | `Host/Diagnostics/{SupportBundleBuilder,SupportBundleVerifier,SupportBundleContents,SecretCanarySet,RedactionReport}`; the four API routes; the one-time token; the startup sweep | WP-O2, WP-O5, provider store, plugin registry | §6.2's poisoned-corpus test passes; a bundle generated with `RedactionOptions.Enabled=false` is **refused**; no push endpoint appears in any bundle (`pwa-and-push.md` §WP-P09's acceptance criterion) |
| **WP-O8** | Metrics and tracing | `Host/Observability/{ArronixMeters,ArronixActivitySources,MetricsSnapshotCollector}`; instrumentation call sites; `GET /api/v1/system/metrics`; the optional Prometheus formatter | WP-O4 | A cardinality test asserts every tag value comes from a closed vocabulary or a bounded identifier set; **zero new packages** |
| **WP-O9** | Health without fatigue | `Host/Health/{HealthIssue,HealthIssueStore,HealthTransitionDetector,HealthNotificationDispatcher}`; subject grouping; confirmation/clear windows; causal startup gate; acknowledgment; contributor quarantine; the four routes | WP-O1, host health aggregator, notification dispatcher | A flap test (fail/recover every 30 s for 10 minutes) produces **zero** notifications; a single dead download client failing six checks produces **one** |

**Parallelism.** WP-O1 gates everything. WP-O2 → WP-O3 → {WP-O4, WP-O5} is the critical path. WP-O8 and
WP-O9 depend only on WP-O1 plus their subsystem and can run alongside WP-O5. WP-O7 is last because it
depends on nearly everything and its acceptance criterion is a whole-system property.

---

## 10. Deferred, with the trigger that un-defers it

| Deferred | Where it would live | Trigger |
|---|---|---|
| **OTLP metric and trace exporters** | `Arronix.Api/Program.cs` + 2 packages | A user running a collector, or a support case needing cross-service timing. The instruments and sources ship now precisely so this is four lines. |
| **A syslog target** | `Arronix.Logging.NLog` | An operator asking for it. `NLog.Targets.Syslog` 7.0.0 is already registered (`Directory.Packages.props:50`), so it is one internal target class and one option value. |
| **A first-party crash-reporting sink** | a plugin under `telemetry-sink` | Arronix running a service it controls. Deliberately not first-party — `NzbDroneLogger.cs:78-87` records why. |
| **Logging to the database** | — | **Never.** Resolution #14. |
| **`ILogRedactor` on `IPluginContext`** | — | **Never.** It is an oracle (#11). |
| **Promoting `ILogRedactor` / `IHealthAggregator` / `HealthIssue` to Abstractions** | `Arronix.Common` / `Arronix.Host` today | A second implementer or a plugin consumer. None exists; the three-tier rule holds them at Tier B. |
| **Per-plugin log files** | `Arronix.Logging.NLog` | A deployment with enough plugins that one file is unreadable. `plugin:{id}` verbosity plus the log filter covers the case today. |
| **Log shipping / remote write** | — | An operator request. `ITelemetrySink` already covers it as a plugin, so the platform need not. |
| **Structured log search over an index** | — | A user with enough history that grep over rotated files is too slow. The CLEF format is the on-ramp. |
| **Automatic support-bundle upload** | — | **Never**, absent an Arronix-operated service *and* an explicit opt-in per bundle. |
| **Health-issue history beyond the current process** | `Host/Health/` + a table | The storage-layer milestone. `HealthIssue` fixes the shape now so the schema is not biased. |
| **Alert routing rules (issue → specific notification provider)** | `Host/Health/` | Two users asking for different routing. Severity thresholds cover the first case. |
| **A Roslyn analyzer forbidding `ILogger` in plugin projects** | a new analyzer project | The same trigger as `ARX1001` in `unified-host-runtime.md` §8. The reference-graph check already catches it at load; an analyzer would catch it at edit time. |

---

## 11. Deviations and required amendments

| # | Document | Deviation | Justification |
|---|---|---|---|
| 1 | `arronix-common-extraction-plan.md` §9 | The deferred logging project is `src/Arronix.Logging.NLog`, not `src/Arronix.Common.Logging.NLog` | The split is adopted unchanged; only the name moves, because `Arronix.Common.*` invites the edit `ProjectShapeTests` exists to prevent |
| 2 | `arronix-common-extraction-plan.md` §3.1 | `ConsoleLogFormat` is renamed `LogFormat` | It now governs files as well as the console. Common carries no stability obligation, so this is free |
| 3 | ARCHITECTURE §10 | OTLP exporters are **deferred**; the instruments they would export are built now | §10 names the exporters as an example of extensible sinks, not as a milestone requirement; the integration surface exists and is four lines |
| 4 | ARCHITECTURE §10 | "`/health` endpoint plus machine-readable JSON" is delivered, but health *notifications* fire only on confirmed transitions above `Error` | §10 does not specify notification policy; the *arrs' does, and it is the fatigue source |
| 5 | `unified-host-runtime.md` #24 | Correlation additionally rides `Activity.Current` **inside the host** | Not a second published mechanism — plugins never see it, and its value is the published `Parameters` key. #24's objection was to *scoped DI*, which cannot reach a plugin; this does not attempt to |
| 6 | `unified-host-runtime.md` §4.9 | `HealthCheck` gains a mandatory positional `SubjectId`, and contributors that fail repeatedly are quarantined | A constructor-shape change, which any MINOR below 1.0.0 may make (`docs/contracts/stability.md`); zero contributors exist, so nothing is broken, and an optional grouping key is one every contributor can forget — which is how one cause produces N notifications |
| 7 | Common practice | `traceparent` is **not** propagated to third-party remotes | A remote that logs it can link one user's activity across sessions and hosts; we get the diagnostic value from our own request line |

**Recommended amendment to `ARCHITECTURE.md` §10.** The two telemetry bullets should record that (a) sinks
are contributed under the `telemetry-sink` capability and the first-party tree ships none, and (b) health
contributions produce *issues with state*, not events, with notification gated on confirmation and severity.
Both are behaviors §10 currently leaves unstated and both are load-bearing for the fatigue result.

**Contract-governance checklist for WP-O1** (the four-coordinated-edits rule):
`HealthCheck.SubjectId` — a constructor-shape change to a type on the residual 0.1.0 list, **no**
`ExperimentalContracts` edit, **no** `stability.md` id-table edit, one line in `stability.md`'s version
history recording the new parameter and the three factory signatures that follow it.
`TelemetryTagNames` — new type in the existing `Arronix.Abstractions.Telemetry` namespace, so it takes
`[Experimental(ExperimentalContracts.Telemetry)]` (`ARX0011`) and needs no dictionary row in
`ExperimentalSurfaceTests`. **This design adds no new contract area and no new `ARX` identifier.**
