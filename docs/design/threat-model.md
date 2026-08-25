# Arronix Plugin & Capability System — Adversarial Threat Model (v0.3.0 milestone)

> **Status:** Review artifact. This document attacks the design in
> `docs/design/unified-host-runtime.md` and `ARCHITECTURE.md` §11. It proposes no new contracts of its own;
> every mitigation is either a change to a file that milestone already creates, or a test added to §7.5.
>
> **Scope of the attack:** the plugin model (§3), the loader and `AssemblyLoadContext` (§4.1), the scoping
> decorators (§4.2), the capability matrix (§4.1), the scheduler (§4.7), the provider model (§5) and the
> §6.8 security claims. Storage is in scope only as a target; the media-shape model is out of scope.
>
> **Method:** the attacker is assumed to be a competent .NET developer who has read this repository. Every
> "not stopped" verdict below was checked against actual CoreCLR semantics (ALC binding order, reflection
> accessibility, `Thread.Abort` removal, `Exception.TargetSite`), not against intent. Where the spec is
> silent I say so and name it as a dependency rather than assuming the good case.
>
> **Governing documents:** `ARCHITECTURE.md` §4/§11, `docs/design/unified-host-runtime.md` §1 decisions
> 11–21, `docs/arronix-common-extraction-plan.md` (three-tier rule), `docs/contracts/stability.md`.

---

## 0. The headline, stated first

**ARCHITECTURE §11's claim that "enforcement is mechanical rather than advisory" is true of exactly one
thing — *which Arronix contracts a plugin can obtain* — and false of everything a plugin actually does.**

A plugin runs in the host process with the full, unrestricted .NET base class library. `Capability.Network`
gates `IHttpGateway`; it does not gate `new HttpClient()`. `Capability.Storage` gates `IFileSystem`; it does
not gate `File.ReadAllBytes("/config/arronix.db")`. Nothing in the design gates `Process.Start`,
`[DllImport("libc")] static extern int system(string cmd)`, `Socket`, `Environment.GetEnvironmentVariables()`
or `System.Reflection.Emit`. .NET Core removed Code Access Security in 2016; there is no partial-trust mode
to fall back on. The `PluginLoadContext` blocked-prefix list and the `PEReader` reference check are controls
over **Arronix assemblies**, and a hostile plugin does not need a single Arronix assembly to do harm.

That is not a defect the design introduced — it is the consequence of in-process plugins, which
ARCHITECTURE §11 half-admits ("early versions rely on trust + review"). The defect is that the rest of §11,
and §6.8's *"secrets are structurally unable to leave"*, read as a sandbox to anyone who does not already
know this. **The most important output of this document is the wording change in §7.**

The second-most-important output is that the situation is materially improvable without leaving the process,
and the improvement is cheap: **extend `PluginReferenceInspector` from the `AssemblyRef` table to the
`TypeRef`, `MemberRef` and `ImplMap` tables** (§4, T-01). P/Invoke declarations, `AssemblyLoadContext` use,
`AppDomain` use, `Reflection.Emit` and `System.Net.Http` are all statically visible in ECMA-335 metadata,
read with the same `System.Reflection.Metadata` the loader already opens, before a single instruction of
plugin code executes. That one change is what converts `network` and `storage` from *advisory labels* into
*checked properties* for every plugin that is not deliberately obfuscating — which is every honest plugin
and most dishonest ones.

The third output is that there are **five findings that need no reflection tricks at all** — they are holes
in the contract surface itself, reachable by a plugin that plays entirely by the rules: the ungated
telemetry enricher/filter (T-02), the process-wide HTTP interceptor granted by `indexing` (T-04), the
plugin-controlled rate-limit partition key (T-06), the whole-bus event subscription (T-09), and the
telemetry sink that needs no `network` capability (T-11). Those are worth fixing on their own merits,
because they are the ones that will be triggered by *accident* and that survive any future move to real
isolation.

---

## 1. Resolution table — the contested calls made here

Format matches `docs/arronix-common-extraction-plan.md`. Every row is a place where this document takes a
position that the current design does not, or takes a position against what a reader would expect.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Is the capability system a security boundary? | ARCH §11: "mechanical rather than advisory"; §6.8: "secrets are structurally unable to leave". | **No. It is a declaration, admission and audit system.** | The BCL is ungated and in-process reflection is unrestricted, so every behavioral gate is a speed bump; a control that stops honest mistakes and documents intent is valuable, but calling it enforcement invites someone to install a plugin they should not — **amend §11, see §7**. |
| 2 | Build a metadata deny-list even though a determined attacker defeats it with string-based reflection? | Not considered by the spec. | **Build it (WP-T1).** | It costs one class in a project that already opens `PEReader`, it rejects the entire naive attack class at *discovery* time before any code runs, and it is the only mechanism that makes `network` mean anything in-process. |
| 3 | Should `System.Net.Http` / `System.Net.Sockets` be denied to **all** plugins, not only those lacking `network`? | Not considered. | **Yes — deny to all; `IHttpGateway` becomes the sole egress path.** | Otherwise the rate limiter, the circuit breaker, the per-plugin host allow-list, the User-Agent and the redaction of outbound secrets are all opt-in; the four first-party plugins take zero packages this milestone, so the cost is zero today and the rule is easiest to adopt before it is expensive. |
| 4 | Do `ITelemetryEnricher` and `ITelemetryEventFilter` stay ungated? | Spec §4.1 matrix: ungated. | **No — gate both, and scope them to the declaring plugin's own events.** | An enricher sees every event in the process including live host `Exception` objects, and a filter can silently delete the audit trail — these are the two highest-privilege interfaces in the system and they are currently free. |
| 5 | Does `telemetry-sink` imply `network`? | Spec decision #15: only `indexing\|metadata\|download\|notification\|import-list ⇒ network`. | **Yes, add `telemetry-sink ⇒ network`, and additionally require per-plugin operator opt-in.** | A sink's entire purpose is to send events somewhere; a manifest that says `["telemetry-sink"]` and nothing else, on a plugin that reads every event in the process, is a dishonest declaration produced by the rule rather than by the author. |
| 6 | Should `import ⇒ storage` be added now that we know the BCL is open anyway? | Spec decision #15 refuses it. | **No — keep the refusal.** | "Plugins can cheat anyway" is an argument for making declarations *more* informative, not less; the implication closure exists so the manifest tells the operator something true. |
| 7 | Per-plugin outbound host allow-list: configuration only, or declared in the manifest? | Spec §4.2: "a per-plugin host allow/deny list from configuration". | **Both — the manifest declares intended hosts, configuration overrides, default is deny-unlisted for `notification` and `telemetry-sink` families.** | Configuration alone means the safe state requires the operator to have written something, and nobody writes it; a declared list is reviewable at install time and diffable at upgrade. |
| 8 | Does `PartitionedRateLimiter` prefix the partition key with the plugin id? | Spec §4.2 says it does, *and* claims §12's "shared across plugins hitting the same remote" still holds. | **THIRD — two limiters, not one prefix.** | Those two claims are mutually exclusive: a per-plugin prefix means two plugins hitting one indexer each get a full budget, which is the IP ban §12 exists to prevent; the destination limiter must key on the resolved authority with plugin input ignored, and a separate per-plugin fairness limiter layers on top. |
| 9 | Does the reference inspector examine the entry assembly or the whole plugin directory? | Spec §4.1: "opens the entry assembly". | **Whole directory, and from the same byte buffer the ALC subsequently loads.** | An entry assembly that references only Abstractions, next to a sibling `Helper.dll` that references `Arronix.Common`, passes the static check today; and re-reading the file at load is a TOCTOU window for free. |
| 10 | Is quarantine a containment control? | Spec §4.8: quarantine, never fatal — and it is presented as the answer to a bad plugin. | **No. Quarantine after step 7 means arbitrary code has already run.** | Module initializers, static constructors and `Configure` all execute before the forward capability check, the shape validation and the token checks; a plugin quarantined at step 14 may already own a `Default` ALC `Resolving` handler and a background thread. |
| 11 | Can plugin unload be relied on to reclaim a hostile plugin? | Spec: `isCollectible: true`, unload "not exercised in this milestone". | **No, and it never can be. Do not present collectibility as a mitigation.** | Unload completes only when no reference into the ALC survives; a hostile plugin's entire job is to keep one, and a merely-buggy one keeps a `Timer` or a thread by accident. |
| 12 | Do plugin-facing contracts foreclose ARCH §15's stated WASM/process isolation path? | Spec is silent. | **Yes, materially — and that should be a conscious product decision now, not a discovery later.** | `IFileSystem` returns `Stream`, `IHttpGateway.DownloadAsync` takes one, `ICacheProvider` takes a `Func<>`, `IPluginContext` exposes `TimeProvider`, `TelemetryEvent` carries a live `Exception`; none of these marshal, so "future hardening" currently means "rewrite every contract". See §6. |
| 13 | Does `OutboundHttpRequest.AllowAutoRedirect` stay `true` by default? | Contract ships `= true` (`Http/OutboundHttpRequest.cs:74`). | **The default may stay; the handler must not follow redirects itself.** | A host allow-list checked on hop 0 and then handed to `HttpClient`'s automatic redirect is an allow-list checked once and bypassed thereafter — the gateway must run the redirect loop so policy applies per hop. |
| 14 | Is a Roslyn analyzer an acceptable substitute for the metadata deny-list? | The spec defers an analyzer for invariant 1 (`ARX1001`). | **No — an analyzer is a courtesy to plugin authors, not a control.** | Analyzers run in the author's build, which the attacker owns; the host must validate the artifact it was handed. |

---

## 2. What is being defended, and against whom

**Assets, ranked by what a real attacker wants:**

| # | Asset | Where it lives |
|---|---|---|
| 1 | Indexer/tracker credentials and API keys, download-client credentials, notification webhooks | Provider `ProviderDefinition.Settings`, and in flight in `OutboundHttpRequest.Headers` |
| 2 | The host's own API key and any database credentials | `IConfiguration`, process environment variables |
| 3 | The user's library contents, paths and full acquisition history | `IMediaStore`, `NotificationMessage`, the event bus, telemetry |
| 4 | The user's IP reputation with private trackers | Outbound HTTP behavior |
| 5 | Arbitrary file read/write as the host user | The process itself |
| 6 | Availability of the host | Scheduler slots, thread pool, memory |

**Actors:**

- **A1 — the buggy plugin.** By far the most likely. Ignores `CancellationToken`, blocks on `.Result`,
  retries in a `while(true)`, caches without bound, uses `System.IO` because it did not know `IFileSystem`
  existed. Every control in §11 pays for itself against A1 alone.
- **A2 — the careless plugin.** Declares `["notification"]`, then reads the whole event bus because
  `AddEventHandler<IDomainEvent>` compiles. Not malicious; simply took the widest thing on offer.
- **A3 — the hostile plugin.** Wants assets 1–3 and will read this repository first.
- **A4 — the compromised supply chain.** A3 with a legitimate-looking history, arriving by update.

**Trust boundaries that actually exist today:**

| Boundary | Enforced by | Real? |
|---|---|---|
| Browser ↔ host | HTTP, no plugin code in the browser (§6.8) | **Yes** — this one is genuine and well-designed |
| Host ↔ plugin *contract topology* | `PluginLoadContext` deny, `PEReader` check, no `IServiceCollection` | **Partly** — see T-01, T-03, T-08 |
| Host ↔ plugin *behavior* | nothing | **No** |
| Plugin ↔ plugin | scoping decorators, id-namespaced partitions | **No** — see T-02, T-04, T-09 |
| Host ↔ plugin *resources* | `ConcurrencyGovernor`, `IRateLimiter` | **No** — see T-06, T-07 |

---

## 3. Ranked findings

Severity: **Critical** = full compromise of assets 1–3 or of the host process. **High** = one asset class, or
cross-plugin compromise. **Medium** = degradation, blinding, or a control that does not do what it says.
**Low** = information disclosure or bounded nuisance.
Exploitability: **Trivial** = a few lines of ordinary C# by a plugin acting within its declared capabilities.
**Easy** = ordinary C#, some knowledge of the host. **Moderate** = deliberate reflection/obfuscation.

| # | Finding | Severity | Exploit | Stopped by current design? | Mitigation summary |
|---|---|---|---|---|---|
| **T-01** | The BCL is ungated: `HttpClient`, `File`, `Process`, `Socket`, `DllImport`, `Reflection.Emit`, env vars. `network`/`storage` are labels. | **Critical** | Trivial | **No** — and cannot be, in-process | Metadata deny-list over `TypeRef`/`MemberRef`/`ImplMap` at discovery (WP-T1); amend §11 |
| **T-02** | `ITelemetryEnricher` / `ITelemetryEventFilter` are **ungated** and process-wide; `TelemetryEvent.Exception` hands over a live host `Exception` (→ `TargetSite.DeclaringType.Assembly`); `ShouldSend` can delete the audit trail. | **Critical** | Trivial | **No** | Gate both; scope to the declaring plugin's own events; strip `Exception` before plugin-visible fan-out (WP-T2) |
| **T-03** | Reflection over the injected `IPluginContext` graph reaches every host object the decorators wrap, including the unscoped services and (if held) the root `IServiceProvider`. | **High** | Trivial | **No** | Minimize reachable state; `PluginContextReachabilityTests` walks the graph and fails on forbidden types (WP-T3) |
| **T-04** | `AddOutboundHttpInterceptor` is granted by `indexing` and, as specified, intercepts **every** plugin's outbound HTTP — including `Authorization` headers and indexer API keys. | **High** | Trivial | **No** | Scope interceptors to the declaring plugin's `ScopedHttpGateway` (WP-T4) |
| **T-05** | SSRF / egress-policy bypass: the per-plugin host allow-list is checked on hop 0 while `AllowAutoRedirect = true`; nothing denies loopback, RFC1918, link-local or cloud metadata. | **High** | Easy | **No** | Host-run redirect loop with per-hop policy; default-deny private/loopback ranges; `ConnectCallback` re-check against DNS rebinding (WP-T4) |
| **T-06** | `OutboundHttpRequest.RateLimitKey` is plugin-controlled and only stamped "when the caller left it null" — a plugin escapes the shared per-remote limit (→ IP ban) or squats another plugin's partition. The per-plugin prefix also contradicts §12. | **High** | Trivial | **No** | Two limiters: host-computed per-authority (plugin input ignored) + per-plugin fairness (WP-T4) |
| **T-07** | Scheduler starvation: `AddScheduledJob` ungated, plugin-supplied `MaxConcurrency`/`Priority` unclamped, no per-plugin slot cap in `ThrottleLimits` defaults, and a job ignoring the token is **unkillable** (`Thread.Abort` is gone). | **High** | Easy | **No** | Per-plugin slot cap; clamp plugin-supplied ints; dedicated threads not pool threads; hung-job watchdog → quarantine + health (WP-T5) |
| **T-08** | Static reference check inspects only the **entry** assembly, re-reads the file separately from the loader (TOCTOU), and never checks the `AssemblyRef` **version** of `Arronix.Abstractions` against the manifest range. | **High** | Easy | **Partly** | Inspect every `*.dll` in the plugin directory; one byte buffer for inspect+load; cross-check the referenced Abstractions version (WP-T1) |
| **T-09** | `AddEventHandler<TEvent>` with `TEvent = IDomainEvent` subscribes to the entire bus. The spec asserts subscription filtering but specifies only publication-side filtering and gives no event→owner attribution. | **Medium** | Trivial | **No** | Reject abstract/interface/open `TEvent`; require the type to be declared in the plugin's own assembly or be on a closed platform allow-list (WP-T2) |
| **T-10** | `ScopedFileSystem` path jail is defeated by symlinks/hardlinks inside a granted root, by `StartsWith` prefix-boundary bugs, and by TOCTOU. "Granted roots" is **undefined** in the spec. | **Medium** | Easy | **No** | Normalize + `ResolveLinkTarget(returnFinalTarget: true)` + boundary-aware compare + re-check after open; define the root set (WP-T6) |
| **T-11** | `telemetry-sink` does not imply `network`: a plugin declaring only `["telemetry-sink"]` reads every telemetry event in the process and ships it out with its own `HttpClient`. | **Medium** | Trivial | **No** | Add the implication; require explicit operator opt-in; deliver only post-redaction events (WP-T2) |
| **T-12** | `RedactionRule.Pattern` is a plugin-supplied raw regex applied to every log line → catastrophic backtracking stalls the whole host; a shadowing rule can also *reduce* redaction. | **Medium** | Easy | **No** | `RegexOptions.NonBacktracking` + `matchTimeout`; cap rule count and pattern length; host rules always win (WP-T2) |
| **T-13** | Code runs before **and despite** quarantine (`[ModuleInitializer]`, static ctors, `Configure` at step 9; quarantine can happen at steps 10–15). Runtime isolation denials are a silent `throw`, not an alarm. | **Medium** | Easy | **No** | Ban module initializers via the deny-list; log/telemetry/health on every isolation deny; mark the process tainted after a post-load quarantine (WP-T1, WP-T5) |
| **T-14** | Manifest and package are unsigned, unverified plaintext; the plugin declares its own capabilities. Lidarr's installer shape (download zip from a GitHub release, extract, no hash, no signature) is the model to *not* copy. | **Medium** | Trivial | **No** (no installer exists yet) | No installer this milestone; when one lands: detached signature, pinned hash, contained extraction, capability diff on upgrade (deferred, §8) |
| **T-15** | `IsSharedFramework(name)` is unspecified. A prefix heuristic either forces a plugin's private `Microsoft.Extensions.*` onto the host's copy (shared static state) or admits a plugin's private `System.*` shim. | **Medium** | Moderate | **Unspecified** | Compute the set once from the shared-framework TPA list; exact simple-name match; test both directions (WP-T1) |
| **T-16** | A `["notification"]` plugin receives the entire activity stream (`RenderedSummary`, `MediaItemRef`, `FileSummary` paths) and, via implied `network`, may POST it anywhere. | **Medium** | Trivial | **No** | Manifest-declared egress hosts, default-deny for notifier/sink families; enforce `SupportedEvents` dispatch host-side with a test (WP-T4) |
| **T-17** | Ungated `AddDiacriticFolding` and `AddHealthContributor` mutate host-wide behavior: folding affects every plugin's parsing/naming (and can inject path separators if it runs after sanitization); a contributor can report false-green. | **Low** | Trivial | **No** | Scope folding to the declaring plugin's kind; fix fold→sanitize order and test it; attribute health results (already namespaced) and never let a plugin *lower* an aggregate (WP-T6) |
| **T-18** | No quota on `ICacheProvider`, `IPluginPaths.TempFolder`, or `IEventPublisher` publication rate → memory, disk and bus exhaustion. | **Low** | Moderate | **No** | Per-plugin entry/byte caps, temp sweep with a ceiling, publish rate limit (WP-T5) |
| **T-19** | Information disclosure: `IHostRuntimeInfo` (`ExecutingApplication`, `IsAdministrator`, `IsContainerized`) plus ungated `Environment.GetEnvironmentVariables()`, which typically carries `ARRONIX__…` config including the API key. | **Low** | Trivial | **No** | Deny-list `System.Environment`; consider trimming `IHostRuntimeInfo` to what plugins demonstrably need (WP-T1, WP-T3) |
| **T-20** | *Correctly stopped, recorded so reviewers do not "fix" it:* the plugin-ships-its-own-`Arronix.Abstractions` type-identity double-load; `Assembly.Load("Arronix.Common")` from plugin code; `IServiceCollection` reachability from a contract. | — | — | **Yes** | None — decisions #11, #20, #21 are right and their tests must stay |

**Strategic (not exploitable; architectural):**

| # | Finding | Impact |
|---|---|---|
| **S-01** | The plugin-facing contract surface is structurally in-process-only (`Stream`, `TimeProvider`, `Func<>`, live `Exception`), which forecloses ARCHITECTURE §15's stated WASM/process-isolation hardening path without a contract rewrite. | See §6 |

---

## 4. Findings in detail

### 4.1 Can a plugin escape its `AssemblyLoadContext`?

The brief's vectors, each answered against real CoreCLR binding semantics. CoreCLR resolves a reference from
an assembly in ALC *X* by: (1) *X*'s already-loaded table, (2) `X.Load(name)`, (3) **fallback to the Default
ALC's TPA probing**, (4) `X.Resolving`, (5) `AssemblyLoadContext.Default.Resolving`, (6)
`AppDomain.AssemblyResolve`. Step 3 is exactly why decision #21 (*throw, never return null*) is correct and
non-negotiable.

| Vector | Stopped? | Detail |
|---|---|---|
| Static reference to `Arronix.Common` | **Yes, twice** | `PluginReferenceInspector` at discovery, `PluginLoadContext.Load` throw at bind |
| `Assembly.Load("Arronix.Common")` | **Yes** | `Assembly.Load` binds in the *calling assembly's* ALC → the plugin ALC → throw |
| `Type.GetType("…, Arronix.Host")` | **Yes** | Same binding path; `throwOnError: false` does not swallow a non-`FileNotFound` exception |
| **`AssemblyLoadContext.Default.LoadFromAssemblyName(new("Arronix.Common"))`** | **No** | The plugin names the Default ALC explicitly; `PluginLoadContext.Load` is never consulted. `Arronix.Common.dll` is in the host's TPA list because `Arronix.Host` references it |
| **`Assembly.LoadFile(path)` / `LoadFrom` / `Load(byte[])`** | **No** | `LoadFile` creates a fresh anonymous ALC; the blocked-prefix list is not consulted by any of these |
| **`AppDomain.CurrentDomain.GetAssemblies()`** | **No, and unstoppably** | Returns assemblies from **all** load contexts. `AssemblyLoadContext.All` and `.Assemblies` are equivalent. From a handle: `GetType(name)`, `BindingFlags.NonPublic`, `Activator.CreateInstance` — .NET Core has no CAS, so private reflection is unrestricted |
| **`AppDomain.CurrentDomain.FirstChanceException`** | **No** | Observes *every* exception in the process, message and all, including provider failures carrying URLs and tokens. Also `AssemblyResolve`/`ProcessExit`/`UnhandledException` |
| **`AssemblyLoadContext.Default.Resolving` hijack** | **No** | A plugin registers a handler and thereafter supplies assemblies to the *host's* own late binds |
| `[ModuleInitializer]` | **No** (see T-13) | Runs at first type touch — i.e. at step 8's `Activator.CreateInstance`, before the forward capability check (step 10) and before shape/token validation (steps 11–15) |
| Static constructors | **No** | Same window; a `static` field initializer on the `IPluginModule` type runs before `Configure` |
| Serialization callbacks | **Marginal** | `IJsonSerializer` deserializing a plugin-declared type runs plugin setters/ctors on a host thread. Not a distinct escape — the plugin already runs on host threads — but it *is* a way to run code from a request-handling path rather than a job path, which matters for T-07 |
| Static state in `Arronix.Abstractions` | **N/A today; unguarded** | Abstractions is loaded once in Default and shared by every plugin. Today it is interfaces, records and consts — but nothing forbids someone adding a `static` cache or event, which would instantly be a plugin↔plugin and plugin↔host channel. See T-17 mitigation |
| `System.Reflection.Emit` + `DynamicMethod(…, skipVisibility: true)` | **No** | Bypasses accessibility entirely and faster than reflection |
| `unsafe` / pointers / `DllImport` | **No** | The plugin owns its own `.csproj`; `AllowUnsafeBlocks` is not the host's to set. Type safety is not a boundary either |

**Verdict.** The ALC is a **resolution** boundary, not a **security** boundary — which is exactly what
Microsoft documents it as. The design's ALC work (decisions #20, #21, load-from-stream, collectible) is
correct and worth keeping; it just does not do the job the surrounding prose implies.

**Mitigation — the metadata deny-list (WP-T1).** `PluginReferenceInspector` already opens the assembly with
`PEReader` + `MetadataReader`. Extend it from `AssemblyRef` to:

```csharp
// src/Arronix.Plugins/Loading/PluginReferenceInspector.cs  (extended)
// All four tables are ECMA-335 metadata; no code executes, no package is added.
//  - AssemblyRef  : blocked Arronix assemblies (existing), + Arronix.Abstractions VERSION cross-check
//  - TypeRef      : denied types by full name
//  - MemberRef    : denied members on otherwise-permitted types (Assembly.Load, Type.GetType, …)
//  - ImplMap      : every [DllImport] declaration — MethodDefinition.GetImport() → MethodImport
//  - CustomAttribute: [ModuleInitializer], [UnmanagedCallersOnly]

internal static class PluginMetadataDenyList
{
    // Unconditional — no capability grants these.
    public static readonly string[] DeniedTypes =
    [
        "System.Runtime.Loader.AssemblyLoadContext",
        "System.AppDomain",
        "System.Reflection.Emit.DynamicMethod",
        "System.Reflection.Emit.AssemblyBuilder",
        "System.Runtime.InteropServices.NativeLibrary",
        "System.Runtime.InteropServices.Marshal",
        "System.Diagnostics.Process",
        "System.Environment",
        "System.Runtime.CompilerServices.Unsafe",
    ];

    public static readonly string[] DeniedMembers =
    [
        "System.Reflection.Assembly::Load",  "System.Reflection.Assembly::LoadFile",
        "System.Reflection.Assembly::LoadFrom", "System.Type::GetType",
        "System.Delegate::CreateDelegate",
    ];

    // Capability-conditional: absence of the capability turns these into a load rejection.
    // Presence of the capability does NOT admit them — IHttpGateway/IFileSystem are the sanctioned paths.
    public static readonly string[] DeniedNamespacePrefixes =
    [
        "System.Net.Http.", "System.Net.Sockets.", "System.Net.WebSockets.",
        // System.IO.* is permitted for Stream types; the FILE-SYSTEM members are denied by member name.
    ];

    public static readonly string[] DeniedIoMembers =
    [
        "System.IO.File::", "System.IO.Directory::", "System.IO.FileStream::.ctor",
        "System.IO.FileInfo::.ctor", "System.IO.DirectoryInfo::.ctor",
    ];
}
```

Failure is `CoreErrorCode.PluginIsolationViolation` at **discovery**, with the offending type/member and the
declaring method named in the defect list. Any `ImplMap` row is an unconditional rejection: P/Invoke is a
total bypass of everything, and no legitimate media plugin needs it (the one real case — a plugin shipping
native decoding — is a `Capability.NativeCode` conversation to have deliberately, not by omission).

**Honest limits of the deny-list, stated up front so nobody oversells it.** A determined attacker builds the
type name from encrypted string fragments and reaches it through `object.GetType().Assembly.GetType(s)` — the
`Assembly::GetType` `MemberRef` is itself detectable, so the arms race continues for a few rounds and then
the attacker wins by using an entry point the list forgot. What the deny-list buys is: **every A1/A2 case
rejected mechanically**, **every A3 case that has not been deliberately obfuscated rejected mechanically**,
and — importantly — **obfuscation itself becomes the signal**, because an honest media plugin has no reason
to build type names at runtime. That is a genuinely different posture from today, and it costs one class.

**Mitigation — the Abstractions purity test (WP-T2), cheap and permanent:**

```csharp
// src/Arronix.Abstractions.Tests/Governance/AbstractionsPurityTests.cs
// Every static field in Arronix.Abstractions is const or static readonly of a deeply-immutable type;
// no static events; no [ModuleInitializer]. Asserted now, while the answer is trivially "yes",
// because the day it becomes "no" is the day plugin A can talk to plugin B through the contract library.
```

### 4.2 Can a plugin reach a contract it did not declare?

The closed registration surface (decision #11) is the strongest single idea in the design and it works
**for the declaration path**: there is no `IServiceCollection`, no `Add<T>(object)`, no assembly scan, and
`Arronix.Abstractions`' zero-package rule makes the container unreachable *by construction* rather than by
policy. Admission-not-filtering is right. The holes are elsewhere.

**T-03 — object-graph reflection.** Every gated dependency is handed out as a decorator, and a decorator by
definition holds the thing it decorates:

```csharp
// Inside a plugin's Configure(IPluginContext context) — no capability required for the first two hops.
var t     = context.GetType();                                    // Arronix.Plugins.Registration.PluginContext
var inner = t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
// ... whatever PluginContext holds: the registry, the ledger, the unscoped gateway, the host file system,
//     the loader, and — if any of those holds it — IServiceProvider, from which everything is one call away.
```

This cannot be closed in-process. It can be **bounded and regression-tested**, which is worth doing:

- `PluginContext` and every scoping decorator hold **only** the single inner service they wrap. No
  `IServiceProvider` field, no `IConfiguration` field, no `IOptions<T>` monitor, no back-pointer to the
  loader or to `PluginRuntimeRegistry`, anywhere in the transitive closure.
- `ScopedHttpGateway` should hold a *host-owned policy object plus the inner gateway*, and the policy object
  must not hold configuration for other plugins.
- **`PluginContextReachabilityTests` (WP-T3):** build a real `PluginContext` for a fixture plugin, walk its
  object graph by reflection to depth 6 across all fields and auto-property backing fields, and assert the
  reachable set contains no `IServiceProvider`, no `IConfiguration`, no `IServiceCollection`, no type from
  `Arronix.Host`, and no `IFileSystem` instance that is not a `ScopedFileSystem`. This is a genuinely useful
  test: it converts "we were careful" into a build failure the day someone adds a convenient field.

**T-02 — the ungated telemetry interception points.** This is the worst contract-level hole and it needs no
reflection at all.

`IPluginRegistry.AddTelemetryEnricher` and `AddTelemetryEventFilter` are **ungated** (§4.1 matrix), and the
host's emitter *"fans out to `ILogger` and to registered sinks: one pipeline"* (§3.7). One pipeline means one
enricher chain, so a plugin declaring `["parsing"]` and nothing else receives:

```csharp
public sealed record TelemetryEvent(Guid EventId, DateTimeOffset Timestamp,
                                    TelemetrySeverity Severity, string Message)
{
    public IReadOnlyDictionary<string, string> Tags { get; init; }   // every other plugin's context
    public Exception? Exception { get; init; }                       // ← a LIVE host object
    public string? CorrelationId { get; init; }
}
```

Three separate problems, in increasing order of seriousness:

1. **Cross-plugin and host observation.** Every message, every tag, every correlation id, from every plugin
   and from the host itself. If enrichers run **before** the redaction engine, that includes unredacted
   secrets. The spec does not state the order. *This is an open dependency, and the answer must be
   "redaction first, and it is enforced by the pipeline's construction, not by convention."*
2. **`ShouldSend` is a delete.** A filter returning `false` for `Severity >= Error` blinds the operator and
   destroys the auditability that §11 is otherwise buying. A hostile plugin's first action should be to
   suppress evidence of its second action, and the design hands it the mechanism for free.
3. **`Exception` is an escape hatch.** `ex.TargetSite?.DeclaringType?.Assembly` (or, for a host-defined
   exception type, `ex.GetType().Assembly`) is an `Arronix.Host` or `Arronix.Common` assembly handle —
   obtained without touching `AppDomain`, without `AssemblyLoadContext`, without tripping the blocked-prefix
   throw, and without anything the metadata deny-list would flag. It is the cleanest answer to the brief's
   question *"a plugin that references only Abstractions but calls into it to obtain something that
   transitively exposes more."*

**Mitigations (WP-T2), all mechanical:**

- Gate `AddTelemetryEnricher` and `AddTelemetryEventFilter` behind `Capability.TelemetrySink`, and add rows
  to `CapabilityMatrix` so `CapabilityMatrixTests` keeps them honest.
- **Scope both to events whose originating plugin id equals the declaring plugin.** The host already stamps
  origin (`PluginTelemetryEmitter`); the fan-out is the place to filter. A plugin enriching its own events is
  the legitimate use case, and it is fully preserved.
- **A filter may never suppress a host-originated event, and never an event above `Warning`.** Encode as
  `effectiveShouldSend = isOwnEvent && severity < Error && filter.ShouldSend(e)`.
- **Strip `Exception` before any plugin-visible fan-out.** Replace it with a pre-rendered
  `ExceptionSummary(TypeName, Message, StackTraceText)` computed host-side. This is an additive change to a
  *0.2.0 experimental* contract, which `stability.md` explicitly permits, and the cost is that a plugin sink
  writing structured logs loses the object it never should have had.
- Run **redaction before enrichment**, and assert the order in a test.

> **Resolution as implemented (2026-08-26).** Everything above landed, with one deliberate departure from
> the first bullet. Gating the enricher and the filter behind `Capability.TelemetrySink` would have made an
> extension ask for the whole post-redaction stream, and the `network` implication and operator grant that
> come with it, in order to shape the events it raised itself — a wider grant to obtain a narrower
> privilege. The grant was therefore split: `Capability.TelemetryProcessing` (`telemetry-processing`) gates
> `AddTelemetryEnricher` and `AddTelemetryEventFilter`, implies nothing, and needs no operator grant;
> `Capability.TelemetrySink` keeps its original meaning and its trusted tier. The scoping, the suppression
> floor, the `ExceptionSummary` replacement and the redaction ordering are all as recommended.

**T-11 — `telemetry-sink` without `network`.** A sink receives (post-fix, all) events and needs somewhere to
put them. A manifest reading `["telemetry-sink"]` therefore describes a plugin that reads the process's
entire diagnostic stream and — via `new HttpClient()` today, or via `IHttpGateway` after the deny-list
lands — sends it anywhere. Add `TelemetrySink ⇒ Network` to `CapabilitySet.WithImplied()` so the declaration
is at least honest, and treat `telemetry-sink` as the system's one **trusted-tier** capability: the loader
should refuse to grant it unless the operator has named that plugin id in
`Arronix:Plugins:TrustedSinks`. Compare: it is strictly more powerful than `storage`.

> **Resolution as implemented (2026-08-26).** Both landed as recommended: `TelemetrySink ⇒ Network` is in
> `CapabilitySet.WithImplied()`, and the loader refuses a package declaring `telemetry-sink` that the
> operator has not named in `Arronix:Plugins:TrustedSinks` — before its entry assembly is staged, so an
> unapproved sink never runs a line. The narrower `telemetry-processing` capability added under T-02 is
> deliberately outside this tier: it neither reads another extension's events nor implies the network.

**T-04 — the process-wide HTTP interceptor.** `AddOutboundHttpInterceptor` requires only `indexing`. As
specified, the interceptor is registered into the host's gateway, so it observes and can **rewrite** every
outbound request from every plugin — the download client's credentials, the metadata provider's API key, the
notifier's webhook URL. The two cited legitimate implementers (CloudFlare, Torcache workarounds) only ever
need to see *their own* plugin's traffic.

Fix: `ScopedHttpGateway` runs `interceptors[declaringPluginId]` only. One dictionary lookup; no contract
change; the legitimate use case is untouched. Add a test that a request issued through plugin A's gateway
never reaches plugin B's interceptor.

**T-09 — the whole-bus event subscription.** `IEventHandler<in TEvent>` is contravariant and `TEvent` is
constrained only by `where TEvent : IDomainEvent`. So:

```csharp
context.Registry.AddEventHandler<IDomainEvent>(new EverythingHandler());  // compiles; ungated
```

`FilteredEventPublisher` is described as filtering **publication** (*"a plugin sees platform events plus its
own namespace"*), but no mechanism is given for attributing an arbitrary `IDomainEvent`-implementing type to
an owning plugin — and for a subscriber registered on the base interface there is nothing to key on. This is
a specification gap, not merely an implementation risk.

Fix, entirely mechanical, at `AddEventHandler`:

- Reject `TEvent` that is an interface, abstract, or open generic → `PluginCapabilityMissing` with a message
  naming the constraint.
- Require `typeof(TEvent).Assembly == pluginEntryAssembly` **or** `TEvent` ∈ a closed, host-declared
  `PlatformEvents` allow-list (job lifecycle, health, plugin lifecycle) whose payloads are host-authored and
  reviewed for cross-plugin content.
- Rate-limit `IEventPublisher.PublishAsync` per plugin (T-18).

**Other probes from the brief, answered:**

| Probe | Verdict |
|---|---|
| DI service-locator patterns | Not available — no `IServiceProvider` on `IPluginContext`, and Abstractions cannot name one. Decision #11 holds. Reachable only by T-03 reflection |
| Generic variance | **Real**, via `IEventHandler<in TEvent>` — see T-09. `ICacheProvider.GetCache<TOwner, TValue>`'s `TOwner` is only a partition discriminator and the decorator prefixes it with the plugin id, so it is not a channel |
| Capturing an injected object graph | **Real** — T-03 |
| `IServiceProvider` transitively reachable | Only through T-03; the fix is to guarantee it is not held, and test it |
| Delegates closing over host state | `ICacheProvider.GetSelfRefreshingCache(…, Func<CancellationToken, Task<…>> fetch)` runs *plugin* code on a *host* refresh timer — an execution-context concern (unhandled exceptions, unbounded runtime, T-07) rather than a privilege escape. Bound it with a timeout and exception isolation |
| Exception objects carrying host references | **Real, and the cleanest one found** — `TelemetryEvent.Exception`, T-02 |

### 4.3 Is the static reference-graph check sufficient?

It is the cheapest strong control in the design and decision #20 is right. It is not sufficient, in four
specific ways — three fixable, one not.

| Attack | Defeated? | Note |
|---|---|---|
| **Entry assembly only** | **Yes, trivially** | The spec inspects `manifest.EntryAssembly`. Ship `Helper.dll` beside it referencing `Arronix.Common`; static check passes. The ALC's dynamic throw still catches the *bind*, so this is a downgrade from "rejected before execution" to "rejected during execution, catchable by the plugin" — which is exactly what T-13 is about. **Fix: inspect every `*.dll` in the plugin directory** |
| **TOCTOU** | **Yes** | The inspector opens the path; `PluginLoadContext` later does `File.ReadAllBytes(path)`. Two reads, one window. **Fix: read once into a `byte[]`, inspect that buffer, `LoadFromStream` that buffer** |
| **Unverified Abstractions version** | **Yes** | The contract range comes from `plugin.json` — attacker-controlled text with no relationship to what the assembly was compiled against. A plugin can declare `>=0.3 <0.4` and reference `Arronix.Abstractions, Version=0.9.0.0`; the failure mode is a `MissingMethodException` at first call, i.e. a runtime crash instead of a load rejection. **Fix: read the `AssemblyRef` version for `Arronix.Abstractions` and require it to satisfy both the declared range and the host version.** This is the single change that makes the manifest non-authoritative for the one thing that is mechanically checkable, and it costs about ten lines |
| **String-based reflection loading** | **No** | No `AssemblyRef` row is produced. Partially addressed by the deny-list's `MemberRef` scan (`Assembly::Load`, `Type::GetType`); fully defeated by obfuscation |
| **ILMerge / ILRepack of `Arronix.Common` into the plugin** | **Not a real attack** | The merged types have a different `Type` identity from the host's, so no host object can be cast to them and no host state is reached. The plugin gets a private copy of Common's *code* — which it could have written itself anyway. Recorded here only to close the question |
| **Source generators** | **Not a real attack** | Generators run in the plugin author's build, on the author's machine; the emitted IL still produces ordinary `AssemblyRef`/`TypeRef`/`MemberRef` rows and is checked identically. Not a bypass |
| **Reference only Abstractions, obtain more transitively** | **Yes — and it is the main event** | T-02 (`TelemetryEvent.Exception`) and T-03 (context graph). No reflection over assembly tables is needed at all |

### 4.4 Supply chain

There is **no plugin installer in this milestone**, which is the correct scope decision and also means the
supply chain is entirely undefended by construction. What matters is that the next milestone does not repeat
what the surveyed *arr does. Lidarr's `InstallPluginService.InstallPlugin`
(`_reference/Lidarr/src/NzbDrone.Core/Plugins/InstallPluginService.cs:60-79`) resolves a plugin from a GitHub
release, `DownloadFile(package.PackageUrl, …)`, `_archiveService.Extract(zip, pluginFolder)`, restart. No
signature, no hash pin, no publisher identity, and archive extraction into a path the archive itself
influences.

| Issue | Status | Required when an installer lands |
|---|---|---|
| Unsigned assemblies | Not addressed | **Detached ECDSA P-256 / SHA-256 signature over a digest ladder**, per `plugin-distribution.md` §3.1–§3.2 (D10/D11): the signature covers `sha256(package.json)`, and `package.json` carries a `sha256` for every payload entry, so one signature covers every byte while still permitting per-file re-verification at load. Verified before extraction, re-verified at every load. Strong-naming is not a substitute (it is an identity mechanism, trivially re-signed). See the note below on why not Ed25519 |
| Manifest tampering | **Inherent** — the plugin writes its own capability list | Signature must cover `plugin.json`; the operator sees the capability set at install and a **capability diff** at upgrade. This is the single most valuable UX control in the whole model |
| Plugin ships its own `Arronix.Abstractions` | **Correctly handled** | Decision #21's `return null` at the Abstractions name is right and its test must stay. Add the version cross-check from §4.3 |
| Dependency confusion (plugin-private vs host) | **Mostly handled, spec-silent at the edge** | `AssemblyDependencyResolver` resolves plugin-private first for non-blocked, non-shared-framework names — correct. But **T-15**: `IsSharedFramework` is undefined. Compute it once at startup from the shared-framework TPA entries and match on exact simple name; do **not** prefix-match `System.`/`Microsoft.`, which would silently force a plugin's private `Microsoft.Extensions.DependencyInjection` (explicitly permitted by §3.4) onto the host's copy and share its static state |
| Native code | **Not addressed anywhere** | `LoadUnmanagedDll` resolves from the plugin directory. Native code has no isolation and no capability model at all. Reject any plugin with an `ImplMap` row (§4.1) and any unmanaged binary in its directory, until `Capability.NativeCode` is designed deliberately |
| Zip-slip on extraction | Not applicable yet | Contained extraction with per-entry path validation against the destination root |

**Signing: reconciled with `plugin-distribution.md`, in that document's favor.** An earlier draft of the
row above required a *detached Ed25519/minisign signature over the whole archive*, which contradicted
`plugin-distribution.md` D10/D11 on both the algorithm and what the signature covers. Both requirements
cannot ship; this one yields, for three reasons.

1. **Ed25519 has no in-box implementation to verify with.** `System.Security.Cryptography` in the `net10.0`
   ref pack exposes `ECDsa`, `MLDsa` and `SlhDsa`; the string `Ed25519` appears in it only inside the
   composite ML-DSA hybrid names (`MLDsa65WithEd25519`) and there is no standalone Ed25519 signature type.
   Mandating it means taking a package (BouncyCastle or NSec) into the load path of the one component that
   must work before any plugin code runs. `ECDsa.ImportSubjectPublicKeyInfo` + `VerifyData` cost zero
   packages and are cross-platform. minisign is worse for this purpose than either: it is an external binary
   and a keyring outside the process, which `plugin-distribution.md` rejects for GPG on the same grounds.
2. **"Whole package" is the weaker property, not the stronger one.** A signature over the archive bytes is
   verifiable exactly once — before extraction — and says nothing thereafter. The install directory is a
   normal directory that anything on the machine can edit between install and the next restart, and the
   attack this row exists to stop (a swapped `lib/*.dll`) happens *there*, not in transit. The digest ladder
   is verified before extraction **and** re-verified at every load against the files on disk, which is the
   property that matters. It also does not lose the whole-package guarantee: `package.json` enumerates every
   installable entry with its digest, and it is itself the signing input, so one signature still covers
   every byte.
3. **The security-relevant requirements from this section survive intact**, and are the ones to hold
   `plugin-distribution.md` to: the signature must cover `plugin.json` (it is a `files[]` entry — the
   capability list cannot be tampered with without invalidating the signature, which is what makes the
   install-time capability screen and the upgrade-time capability diff meaningful); `alg` is validated
   against a closed allow-list before any parsing, so there is no `alg: "none"`; and verification precedes
   extraction, so a hostile archive never reaches the filesystem.

The one thing a digest ladder must not do is verify the manifest and then trust the payload. It does not:
every extracted entry's digest is checked as it is written (`plugin-distribution.md` §1.6), and an entry
absent from `files[]` is an extraction failure rather than an unchecked file.

### 4.5 Resource abuse

This is where the **A1 buggy plugin** lives, and it is the category most likely to actually hurt a user.

**T-07a — starvation.** `SchedulerOptions.MaxConcurrentJobs` is **global**. `ThrottleLimits` defaults are
given for `indexing` 4, `metadata` 2, `import` 2, `download` 8 — capability-scoped, not plugin-scoped. The
spec *does* synthesize a `plugin:{id}` throttle key, but no default limit is assigned to it, so a plugin
registering twenty jobs saturates the global pool and every other plugin stops. Fix: a default
`plugin:*` limit of `max(1, MaxConcurrentJobs / 2)`, and a hard invariant that no single plugin may hold more
than that many slots. One line in the defaults plus one test.

**T-07b — plugin-supplied integers.** `IScheduledJob.MaxConcurrency` and `.Priority` are plain `int`
properties on a **stable** contract, read from the plugin. `MaxConcurrency = int.MaxValue` neutralizes limit
(2); `Priority = int.MaxValue` on every job monopolizes the queue and starves the host's own work. Clamp both
at `AddScheduledJob` — `Math.Clamp(job.MaxConcurrency, 1, SchedulerOptions.MaxPerJob)` and priority into a
small host-defined band — and record the clamp in telemetry so an honest author sees it.

**T-07c — the unkillable job.** `Thread.Abort` throws `PlatformNotSupportedException` on .NET Core.
`CancellationToken` is **cooperative**. Therefore: *a plugin job that ignores its token cannot be reclaimed,
and the only recovery is restarting the process.* This must be written down, because every operational
runbook depends on it. Mitigations that do work:

- **A declared shutdown grace on the contract.** `plugin-distribution.md` §7.2 adds
  `IScheduledJob.ShutdownDeadline`, host-clamped by `JobScheduler.MaxShutdownDeadline`. This does not make a
  runaway job killable — nothing does — but it turns an undeclared, unbounded wait into a bounded one the
  author stated, so exceeding it is an attributable contract violation rather than an anonymous hang. Land
  it now: the interface has zero implementers today and four the moment the reference plugins are written.
- A **watchdog**: if a job exceeds `SchedulerOptions.HungJobThreshold` after cancellation was requested,
  stop scheduling that plugin, emit a permanent `HealthCheck.Unhealthy` naming the plugin and the job, and
  recommend a restart. The slot is *never* returned — pretending otherwise double-books the pool.
- Run plugin job bodies on **dedicated threads** (or `TaskCreationOptions.LongRunning`), not on shared
  `ThreadPool` workers. The *arr codebases are full of sync-over-async; a plugin doing `.Result` on a pool
  thread starves the same pool that serves `Arronix.Api` requests, so a single bad plugin takes the UI down.
  This is the highest-value single change in the resource category.

**T-07d — the IP ban, which is the user-visible one.** The §4.7 backoff ladder with full jitter is correct
and well-chosen — but it governs *retries the scheduler performs*. A plugin doing its own
`while (true) { await http.GetAsync(indexer); }` inside one job execution never touches it. The only real
in-process defense is that the **gateway is the sole egress path** (resolution #3 + the deny-list) and that
the gateway's rate limiter is keyed on the destination, not on plugin-supplied text — which brings us to:

**T-06 — the rate limiter does not limit what it claims to.** `OutboundHttpRequest.RateLimitKey` is
`{ get; set; }` on a plugin-constructed object (`Http/OutboundHttpRequest.cs:121`), and `ScopedHttpGateway`
stamps the plugin id *"when the caller left it null"*. So a plugin sets it and:

- escapes the shared per-remote budget → the private-tracker ban §12 exists to prevent; **or**
- sets it to another plugin's partition and exhausts that plugin's budget → cross-plugin DoS.

And the §4.2 description of `PartitionedRateLimiter` (*"composes the caller identity into the effective
partition key, so §12's 'shared across plugins hitting the same remote' holds while a plugin cannot starve
another"*) asserts two mutually exclusive properties: a key containing the plugin id **cannot** be shared
across plugins. Resolution #8 above: **two limiters.**

```csharp
// ScopedHttpGateway — the plugin's RateLimitKey is advisory metadata, never the effective key.
var authority   = request.Url.GetLeftPart(UriPartial.Authority);         // host-computed
var destination = await _sharedLimiter.AcquireAsync($"host:{authority}", ct);   // §12: shared, IP-ban defense
var fairness    = await _pluginLimiter.AcquireAsync($"plugin:{_pluginId}", ct); // §11: one plugin ≠ all plugins
```

Acquire in a fixed order (destination then fairness, ordinal within each) so the §4.7 deadlock argument still
holds. And note the redirect interaction: the authority must be re-derived per hop (T-05).

**T-18 — the quiet exhaustion paths.** `ICacheProvider` is ungated with no size bound — a plugin caching
every release it ever saw is an OOM with no attribution, and the GC is shared, so the host dies rather than
the plugin. `IPluginPaths.TempFolder` has no quota. `IEventPublisher.PublishAsync` has no rate limit. None of
these is exciting; all three are what an A1 plugin does by accident. Per-plugin entry and byte caps on the
cache, a temp sweep with a ceiling, and a publish rate limit — and accept that **memory cannot be attributed
or bounded per plugin in-process at all**, which belongs in §7's honest list.

### 4.6 Data exfiltration

**The brief's question — what can a notification-only plugin actually send, and where?**

Answer: `["notification"]` implies `network` (§11.1), so the plugin gets `IHttpGateway`. It receives, for
every event in its declared `SupportedEvents`, a `NotificationMessage` carrying `RenderedSummary`,
`MediaKindId`, `MediaItemRef` and `IReadOnlyList<FileSummary>` — i.e. **titles and local file paths for
everything the user acquires, in real time**. Destination: any host on the internet, subject only to a
per-plugin allow-list that §4.2 says comes "from configuration" and which is therefore empty on a fresh
install. So: a Discord notifier is, by construction, a complete real-time feed of the user's library to an
arbitrary endpoint, and the manifest word for that is "notification".

This is *inherent* to notifications — the plugin has to send something somewhere. The problem is that the
capability label does not convey the blast radius, and the default is open. Fix (T-16):

```jsonc
// plugin.json — new, host-side only, no contract change
"network": {
  "hosts": ["discord.com", "*.discordapp.com"],   // declared intent; shown to the operator at install
  "allowPrivateNetworks": false                   // default; explicit opt-in for LAN download clients
}
```

- For the `notification` and `telemetry-sink` families the effective policy is **deny-unlisted**.
- For `indexing`/`download`, where the user configures the base URL, the declared list is advisory and the
  effective policy is the operator's configuration — but the *declaration* is still shown and diffed on
  upgrade, which is where a compromised update gets caught.
- Enforce `SupportedEvents` dispatch host-side and test it: a notifier declaring `[Grabbed]` must never be
  handed an `ItemAdded`. The spec says the host dispatches on the event; make it a test rather than a
  sentence.

**Cross-plugin leakage inventory:**

| Channel | Leaks? | Note |
|---|---|---|
| `IMediaStore` | **No** | Held host-side (Tier B), no plugin reaches it. Correct decision (#41) |
| `IMediaItemSource` | **No** | Each plugin projects its own catalog |
| Event bus | **Yes** | T-09 — base-interface subscription |
| Telemetry pipeline | **Yes, worst** | T-02 — enricher/filter/sink see everything |
| Outbound HTTP interceptors | **Yes** | T-04 — including `Authorization` headers |
| `ICacheProvider` | No | Partitioned by plugin id (verify the prefix is a boundary, not `StartsWith`) |
| `IRateLimiter` | **Yes (DoS, not read)** | T-06 — partition squatting |
| `IFileSystem` | **Partly** | T-10 — jail escape; and the plugin can use `System.IO` regardless |
| Process environment / config | **Yes** | T-19 — `ARRONIX__…` env vars typically hold the API key |
| Client / browser | **No** | §6.8 is correct: no plugin code in the browser, and the client references only Abstractions. This boundary is real |

**T-05 — SSRF, which is the one that reaches beyond the host.** `AllowAutoRedirect` defaults to `true`
(`Http/OutboundHttpRequest.cs:74`). If `ScopedHttpGateway` checks the allow-list against `request.Url` and
then hands the request to an `HttpClient` that follows redirects itself, the list is checked exactly once and
every subsequent hop is unchecked. Combined with the absence of any private-range denial, a `network` plugin
can reach `http://127.0.0.1:{apiPort}/api/v1/…` (the host's own API), the user's router admin page, their NAS,
and `169.254.169.254` in a cloud deployment. Fixes, all in `ScopedHttpGateway`:

1. `SocketsHttpHandler.AllowAutoRedirect = false`; the gateway runs the redirect loop with a hop cap and
   re-applies host policy, the destination rate-limit key and the interceptor set **per hop**.
2. Default-deny loopback, RFC1918, CGNAT, link-local and IPv6 ULA/loopback, with
   `"allowPrivateNetworks": true` as an explicit per-plugin opt-in — download clients on a LAN are the real
   use case and they should have to say so.
3. Enforce against the **resolved** endpoint via `SocketsHttpHandler.ConnectCallback`, not the hostname, so
   DNS rebinding does not walk through the policy.

`Arronix.Common/Net/` is already chartered as *"all host and address classification in one place"*, so the
classifier exists; the gateway just has to call it.

**T-10 — the file-system jail.** `ScopedFileSystem` is described as "every path argument checked against the
plugin's granted roots". Four things must be true for that to hold, and the spec states none of them — plus
**"granted roots" is never defined anywhere in the document**, which is an open dependency, not a nitpick:
does `storage` grant the plugin's own `IPluginPaths` tree, the configured library roots, or both?

1. Normalize first (`Path.GetFullPath`), then compare with a **boundary-aware** test — `StartsWith` alone
   makes `/data/tv-evil` pass a `/data/tv` root.
2. Resolve links before comparing: `File.ResolveLinkTarget(path, returnFinalTarget: true)`. A symlink the
   plugin created inside its own root, pointing at `/config`, defeats pure path algebra. `TryCreateHardLink`
   is on the gated interface (`FileSystem/IFileSystem.cs:197`) and is the tool for building exactly that.
3. TOCTOU: the check and the open are separate syscalls. Re-validate after opening (compare the opened
   handle's resolved path), or accept the window and document it.
4. Platform edges: `..` segments, case-insensitive comparison on Windows/macOS, `\\?\` and UNC prefixes,
   ADS (`file.txt:stream`), and trailing-dot/space normalization.

None of this stops a plugin that just calls `System.IO.File` — which is precisely why resolution #2 and #3
(deny the `System.IO` file members outright) is what makes `ScopedFileSystem` worth building at all.

### 4.7 The quarantine window and silent denials (T-13)

Two related problems with the load pipeline.

**Code runs before validation completes.** Steps 1–6 are pure metadata and execute nothing — that ordering is
correct and is the design's best structural decision after decision #11. But step 7 loads the assembly, step
8 constructs the module (running `[ModuleInitializer]` and static constructors), step 9 calls `Configure`,
and steps **10–15** — the forward capability check, shape validation, intent validation, token cross-check,
token collisions, provider id collisions — all happen **after** arbitrary plugin code has run. A plugin
quarantined at step 14 has already had the opportunity to register an `AppDomain.CurrentDomain.AssemblyResolve`
handler, hook `FirstChanceException`, start a `Timer`, and write files.

So: **quarantine is a registration control, not a containment control.** Consequences to encode:

- Ban `[ModuleInitializer]` and `<Module>::.cctor` via the metadata deny-list (both are visible in the
  `CustomAttribute` and `TypeDef` tables), so step 8 constructs a type rather than running a program.
- On a quarantine that occurs at step ≥ 7, mark the process **tainted**: a `HealthCheck.Unhealthy` stating
  that plugin *X* executed code before being rejected and that a restart is recommended. Silently continuing
  is the current implied behavior and it is worse than saying so.
- Do not describe collectible ALCs as a remediation (resolution #11). Unload of a plugin that is trying to
  stay loaded never completes.

**Isolation denials are silent.** `PluginLoadContext.Load` throws `PluginIsolationException` — inside plugin
code, at JIT/type-load time, wrapped in `TypeInitializationException` or `FileLoadException`. **The plugin can
catch it.** A deny that the attacker can swallow, and that the host never sees, wastes the strongest signal
available: *a plugin that probed for `Arronix.Common` and handled the failure gracefully is not making a
mistake.* Log, emit telemetry and raise a health item from inside `Load` **before** throwing, attributed to
the plugin id (which the ALC already carries in its name). Two lines; converts a control into a detector.

### 4.8 Host-wide behavior mutation (T-17)

Three ungated registrations change behavior outside the declaring plugin:

- **`AddDiacriticFolding`** — folding is used by parsing and by naming. A folding provider that emits a path
  separator or a `..` from a fold is a path-traversal primitive **if folding runs after sanitization**. The
  spec does not fix the order. Fix the order (fold, *then* sanitize, always), test it, and scope the provider
  to the declaring plugin's media kind.
- **`AddHealthContributor`** — ungated, correctly wrapped with a per-contributor timeout and exception
  isolation, and correctly namespaced to `{pluginId}/{checkId}`. The residual risk is a plugin reporting
  false-green about itself, which is bounded and acceptable; the host's own quarantine contributor is the one
  that matters and it is host-authored. **No change needed** — recorded so the good design is not disturbed.
- **`AddRedactionRules`** — the matrix says "implied by the capability owning the secret", i.e. effectively
  ungated. `RedactionRule.Pattern` is a raw regex string (`Diagnostics/RedactionRule.cs:25-29`) applied to
  every log line in the process. Catastrophic backtracking stalls logging host-wide (T-12), and a rule that
  matches broadly can also *shadow* a host rule's capture group. Fix: compile with
  `RegexOptions.NonBacktracking` plus an explicit `matchTimeout`; cap rules per plugin and pattern length;
  evaluate host rules first and never let a plugin rule replace a host redaction. Trade-off, stated:
  `NonBacktracking` forbids lookarounds and backreferences, so some rule authors will have to rewrite —
  correct trade, since a redaction rule needing a backreference is a rule that should be a host rule.
  **Settled:** `observability.md` §4.2 adopts this with no fallback compile mode — a rule using either
  construct is quarantined at compile, and §4.7 has the test that keeps it that way. An earlier draft there
  kept a `RegexOptions.Compiled` fallback so that a querystring rule inherited from
  `CleanseLogMessage.cs:15-60`'s lookbehind shape would compile; the rule was rewritten with a capture group
  instead, which is what a rule being written from scratch should have done.

---

## 5. What the capability system actually buys

This section is the point of the document. Read it before quoting §11 at anyone.

**It does buy, and these are worth the whole build:**

1. **Honest declaration.** A manifest reading `["notification"]` tells an operator, a reviewer and a UI what
   the author *intended*. Almost every plugin is honest, so almost every manifest is true. This is the same
   value proposition as an Android permission list, and it is real.
2. **Accident prevention (the A1/A2 actors).** The closed `IPluginRegistry` means a plugin *cannot express*
   "register a thing I did not declare". The bidirectional check means it cannot declare a thing it did not
   register. Neither is defeated by being careless — only by being deliberate. Most harm in a plugin
   ecosystem is carelessness.
3. **Auditability.** Every gated grant is a named call site (`TryGetHttp`, `RequireFileSystem`) rather than a
   constructor parameter, so *"what does this plugin touch?"* is a `grep`. Reviewing a plugin is a bounded
   task. This is a direct, deliberate design win from decision #12.
4. **A real topology boundary for the client.** §6.8 is correct and under-celebrated: no plugin code runs in
   the browser, so the browser is outside the capability model because there is nothing to gate. Very few
   plugin systems can say that.
5. **Defense in depth against the *unsophisticated* hostile plugin** — and, with the metadata deny-list
   (T-01), against the merely-sophisticated one too. Raising the cost of an attack from "ten lines" to
   "deliberate obfuscation that is itself detectable" is a genuine improvement, and obfuscation-as-signal is
   worth more than the deny-list itself.
6. **A structure that survives real isolation.** The capability enum, the closed registry, the manifest and
   the load pipeline are all exactly what a WASM or out-of-process host would need. The work is not wasted
   when the boundary becomes real — *provided* S-01 is addressed.

**It does not buy, and no amount of work inside the current architecture will make it:**

1. **Protection from a malicious plugin.** A plugin can read every file the host user can read, open any
   socket, spawn any process, P/Invoke `libc`, and reflect over every object in the process. Installing an
   Arronix plugin is exactly as dangerous as running an executable the same author shipped you.
2. **Confidentiality between plugins.** Any plugin can reach any other plugin's settings, credentials and
   traffic, whether by the contract holes in §4.2 or by reflection.
3. **Availability guarantees.** One plugin can hang the scheduler, exhaust memory, or block the thread pool
   that serves the API, and there is no in-process way to reclaim a running job or bound an allocation.
4. **Integrity of the audit trail** — until T-02 is fixed. Even after, a plugin that has reflected its way to
   the sink list can still tamper.
5. **Any supply-chain assurance whatsoever.** No signature, no hash, no publisher identity today.

**Recommended replacement text for `ARCHITECTURE.md` §11** (this document does not edit that file; the owner
should):

> Plugins execute **in-process with full trust**. The capability system is a *declaration, admission and
> audit* mechanism, not a sandbox: it constrains which Arronix contracts a plugin may obtain, and makes what
> a plugin intends to do reviewable and diffable. It does **not** constrain what a plugin can do with the
> .NET base class library, and it therefore offers no protection against a deliberately malicious plugin.
> Installing a plugin is a decision to trust its author with the full privileges of the Arronix process.
> Real isolation requires a process or WASM boundary (§15) and is not delivered by any control described
> here.

And in **§6.8**, replace *"Secrets are structurally unable to leave"* with *"Secrets declared
`SettingSensitivity.Secret` are structurally unable to reach the **client**"* — the claim is true and
valuable at that boundary, and false if read as a claim about plugins.

---

## 6. S-01 — the hardening path is being foreclosed

ARCHITECTURE §15 names *"WASM or process per plugin"* as the future isolation answer, and §7's deferred list
repeats it. That path requires every plugin-facing contract to be **marshallable**. The current surface is
not, and the gap is growing this milestone:

| Contract | Blocker |
|---|---|
| `IFileSystem.OpenRead/OpenWrite` | returns `Stream` — a live object with host-side state |
| `IHttpGateway.DownloadAsync(request, Stream destination)` | caller-supplied `Stream` |
| `ICacheProvider.GetSelfRefreshingCache(…, Func<CancellationToken, Task<…>>)` | a delegate crossing the boundary in the *callback* direction |
| `IPluginContext.Clock` → `TimeProvider` | an abstract BCL class, not an interface, not marshallable |
| `TelemetryEvent.Exception` | a live `Exception` (also T-02) |
| `IScheduledJob.ExecuteAsync(JobExecutionContext, …)` | `Parameters` is `IReadOnlyDictionary<string, object>` — the §4.7 well-known keys explicitly put an **`IProgressReporter` instance** in it |
| `IPluginRegistry.Add*(instance)` | the whole model passes constructed objects, which is right for in-process and wrong for out-of-process |

This is not an argument to change any of it today — the in-process design is coherent and the alternative
costs real ergonomics. It **is** an argument to stop describing process/WASM isolation as deferred work with
a trigger, because the trigger currently reads *"rewrite `Arronix.Abstractions`"*. Two honest options:

- **(a) Accept it.** State in §8 that adopting isolation later means reshaping the seven members above, and
  price it in call sites. Recommended if the product intent is a curated first-party plugin set.
- **(b) Shape for it now.** Add a governance test asserting that no plugin-facing contract exposes `Stream`,
  `Exception`, `Delegate` or non-interface BCL types, and rework the ~7 members above (byte-range reads
  instead of `Stream`, a poll-based progress channel, an `ExceptionSummary` record). Costs real ergonomics
  today; keeps §15 additive.

**This document recommends (a) plus the §5 honesty text**, and recommends *saying so*, because the current
state — a stated hardening path that the contracts quietly prevent — is the worst of both.

**What (a) is priced on, and what it is not.** An earlier draft of this section priced (a) partly as
*"isolation would be a 1.0/2.0 contract break, not an additive change"*. That price is zero and this section
no longer charges it. `Arronix.Abstractions` is below 1.0 with no implementer outside this repository, so
any of the seven members may be reshaped in a MINOR (`docs/contracts/stability.md`, pre-1.0 clause), and
"we cannot break the contract" is not a reason for anything in this document. Re-running the choice with
that term removed:

- **Ergonomics is the whole remaining cost, and it survives the re-run.** `Stream` on `IFileSystem` is the
  interface every .NET author already knows and the one that streams a 60 MB NFO or a feed body without
  buffering it; byte-range reads make every caller write a loop. `TimeProvider` is not merely *a* clock, it
  is the clock the BCL itself consumes (`Task.Delay`, `PeriodicTimer`, `ITimer`), so replacing it with a
  bespoke `IClock` interface costs interoperability with the platform to buy marshallability nothing
  currently marshals. `ICacheProvider`'s callback is a self-refreshing cache, which is the callback. These
  are real losses paid today for a boundary that does not exist and may never be built.
- **Two of (b)'s seven are being done anyway, for other reasons.** `TelemetryEvent.Exception` →
  `ExceptionSummary` is WP-T2, required by T-02 regardless of isolation. `JobExecutionContext.Parameters`
  carrying an `IProgressReporter` instance under a published string key is being replaced by a typed member
  on the record (`unified-host-runtime.md` §4.7's untyped bag is withdrawn) — still a live object, so still
  not marshallable, but it stops being an *undeclared* one. Take both; neither is an isolation concession.
- **The cost that is real, and that this decision does spend, is call sites.** Reshaping seven contract
  members is cheap while the reference plugins are unwritten and expensive once four of them plus the host
  are calling through them. That, not a version number, is the clock this decision runs against — and it is
  the honest form of the "expensive later" claim the earlier draft made with a version boundary.

So (a) stands, on ergonomics and product intent alone: **Arronix is being built for a curated first-party
plugin set, and it buys in-process ergonomics with the knowledge that real isolation would cost a reshape of
those seven members.** If the product intent changes to supported third-party distribution, that is the
trigger to re-open this, and the re-open is a contract reshape that pre-1.0 costs a MINOR and post-1.0 costs
a MAJOR — which is a reason to make the call *before* 1.0, not a reason to avoid it.

---

## 7. Work packages

Ordered by value per unit of effort. Each names the file the milestone already creates. The two contract
changes happen to fall on **0.2.0 experimental** types, but that is an observation, not a constraint the
ordering was built to satisfy: below 1.0.0 any contract here may be reshaped in a MINOR
(`docs/contracts/stability.md`), so where a mitigation needs a contract change the question asked was
whether the change is *right*, never whether it is *permitted*. Nothing in this section was scoped down to
avoid touching a contract.

| WP | Owns | Change | Fixes | Size |
|---|---|---|---|---|
| **WP-T1** | `src/Arronix.Plugins/Loading/PluginReferenceInspector.cs` (+ new `PluginMetadataDenyList.cs`) | `TypeRef`/`MemberRef`/`ImplMap`/`CustomAttribute` scan; inspect **all** `*.dll` in the plugin directory; single byte buffer shared with `LoadFromStream`; `Arronix.Abstractions` `AssemblyRef` version cross-check; specify `IsSharedFramework` from the TPA list | T-01, T-08, T-13, T-15, T-19 | ~1 class + tests |
| **WP-T2** | `src/Arronix.Plugins/Registration/{PluginRegistry,CapabilityMatrix}.cs`, `Scoping/PluginTelemetryEmitter.cs`, `Arronix.Abstractions/Telemetry/TelemetryEvent.cs` | Gate + scope enricher/filter; drop `Exception` for `ExceptionSummary`; filters may not suppress host or ≥`Error` events; redact-before-enrich; `TelemetrySink ⇒ Network` + trusted-sink opt-in; reject base/interface/open `TEvent`; regex hardening for redaction rules | T-02, T-09, T-11, T-12 | ~2 days |
| **WP-T3** | `src/Arronix.Plugins/Registration/PluginContext.cs` + new `PluginContextReachabilityTests` | No `IServiceProvider`/`IConfiguration`/`Arronix.Host` type anywhere in the reachable closure; graph-walking test to depth 6 | T-03, T-19 | ~1 day |
| **WP-T4** | `src/Arronix.Plugins/Scoping/{ScopedHttpGateway,PartitionedRateLimiter}.cs` | Per-hop redirect policy (`AllowAutoRedirect=false` in the handler); default-deny private/loopback with per-plugin opt-in; `ConnectCallback` endpoint check; ignore plugin-supplied `RateLimitKey`; two limiters (destination + fairness); interceptors scoped to the declaring plugin; manifest `network.hosts` | T-04, T-05, T-06, T-16 | ~2 days |
| **WP-T5** | `src/Arronix.Host/Scheduling/{ConcurrencyGovernor,JobScheduler}.cs` | Default `plugin:*` slot cap; clamp `MaxConcurrency`/`Priority`; dedicated threads for plugin job bodies; hung-job watchdog → quarantine + permanent health item; per-plugin cache/temp/publish quotas; taint flag after a post-load quarantine | T-07, T-13, T-18 | ~2 days |
| **WP-T6** | `src/Arronix.Plugins/Scoping/ScopedFileSystem.cs` | Define the granted-root set; normalize + `ResolveLinkTarget(returnFinalTarget:true)` + boundary-aware compare; re-check after open; cross-root hardlink denial; fold-then-sanitize order fixed and tested | T-10, T-17 | ~1 day |
| **WP-T7** | `ARCHITECTURE.md` §11, `docs/design/unified-host-runtime.md` §6.8, §8 | The §5 replacement text; the §6.8 wording fix; S-01 recorded in §8 with option (a) or (b) chosen | the headline | ~1 hour, highest value |

**Governance tests to add to §7.5:**

| Test | Asserts | WP |
|---|---|---|
| `PluginMetadataDenyListTests` | fixture assemblies containing a `DllImport`, an `AssemblyLoadContext` `TypeRef`, an `AppDomain` `TypeRef`, a `System.Net.Http` `TypeRef` and a `[ModuleInitializer]` are each rejected at **discovery**, with the offending member named | WP-T1 |
| `PluginDirectoryInspectionTests` | a plugin whose *entry* assembly is clean but whose sibling `Helper.dll` references `Arronix.Common` is rejected before any ALC exists | WP-T1 |
| `AbstractionsVersionCrossCheckTests` | a plugin declaring `>=0.3 <0.4` but compiled against Abstractions 0.9 is rejected with `PluginContractMismatch`, not `MissingMethodException` at first call | WP-T1 |
| `AbstractionsPurityTests` | no non-`const`/non-`readonly` static field, no static event, no `[ModuleInitializer]` in `Arronix.Abstractions` | WP-T2 |
| `TelemetryScopingTests` | plugin A's enricher never sees plugin B's or a host event; a filter cannot suppress `Error`; `TelemetryEvent` carries no `Exception` on any plugin-visible path | WP-T2 |
| `EventSubscriptionTests` | `AddEventHandler<IDomainEvent>` is rejected; a handler for a type declared in another assembly is rejected | WP-T2 |
| `PluginContextReachabilityTests` | the reachable object closure from `IPluginContext` contains no `IServiceProvider`, `IConfiguration`, or `Arronix.Host` type | WP-T3 |
| `EgressPolicyTests` | a 302 to a denied host is blocked; loopback and RFC1918 are denied by default; a plugin-set `RateLimitKey` does not change the effective destination partition | WP-T4 |
| `InterceptorScopingTests` | a request through plugin A's gateway never reaches plugin B's interceptor | WP-T4 |
| `SchedulerFairnessTests` | one plugin registering 20 jobs cannot exceed its slot cap; `MaxConcurrency = int.MaxValue` is clamped; a job ignoring cancellation produces a hung-job health item and its slot is not double-booked | WP-T5 |
| `ScopedFileSystemEscapeTests` | symlink out of a granted root, `../` traversal, `/data/tv-evil` vs `/data/tv`, and a cross-root `TryCreateHardLink` are each denied | WP-T6 |
| `IsolationDenyIsObservableTests` | a plugin that catches `PluginIsolationException` still produces a telemetry event and a health item | WP-T1 |

---

## 8. Deferred, with the trigger that un-defers it

| Deferred | Trigger |
|---|---|
| **Process-per-plugin or WASM isolation** | The only real fix for T-01/T-03/T-07/T-18. Trigger: third-party plugin distribution becoming a supported product feature. Note S-01 — declined on ergonomics and product intent, **not** on a contract-break cost, which pre-1.0 is zero. Adopting it means reshaping the seven §6 members; that reshape gets cheaper the earlier it is decided and stops being a MINOR at 1.0.0, so the decision belongs before 1.0 |
| **Package signing, hash pinning, publisher identity, capability diff on upgrade** | The first plugin installer. Non-negotiable at that point; T-14 is otherwise unmitigated |
| **`Capability.NativeCode`** | A plugin with a demonstrated need for native interop. Until then the deny-list rejects every `ImplMap` row |
| **Per-plugin memory accounting** | Not achievable in-process; trigger is the isolation milestone |
| **Egress proxy enforced outside the process (container network policy, `iptables`)** | Deployment-level hardening; the only way to make `network` non-bypassable while staying in-process. Worth documenting for container users *now*, as the one thing an operator can do today |
| **A Roslyn analyzer mirroring the metadata deny-list** | Plugin-author ergonomics only — it tells an author at edit time what the loader will reject at install time. Never a substitute (resolution #14) |
| **Plugin unload as remediation** | **Never.** Resolution #11 |
| **Rate-limiting `IJsonSerializer` / deserialization budgets for plugin-supplied payloads** | A plugin-parsed indexer feed causing a host-side OOM. Bounded by `IHttpGateway` response size limits, which should exist; folded into WP-T4 if cheap |

---

## 9. Open dependencies — where the spec is silent

These are not findings; they are questions the specification does not answer and that a mitigation depends
on. Each needs an owner.

1. **What are a plugin's "granted roots"?** §4.2 and §3.3 both rely on the term; it is defined nowhere. Is
   `storage` the plugin's `IPluginPaths` tree, the configured library roots, or both? T-10 cannot be
   implemented without an answer.
2. **Does redaction run before or after plugin enrichers?** Determines whether T-02 leaks raw secrets or only
   redacted ones.
3. **What is `IsSharedFramework(name)`?** T-15.
4. **Does `ScopedHttpGateway`'s host allow/deny list default to allow or deny, and per capability family?**
   §4.2 says "from configuration", which on a fresh install means "empty", which means "allow all".
5. **Does folding run before or after filesystem-safe sanitization in `Arronix.Common/Naming/`?** T-17.
6. **Where do plugins come from in this milestone?** If the answer is "a directory the operator populates by
   hand", say so explicitly — it is a legitimate and much safer answer than an installer, and it makes T-14
   a deferred concern rather than an open one.
7. **Is `ICacheProvider`'s partition prefix a boundary-aware comparison or a `StartsWith`?** Same class of
   bug as T-10.
