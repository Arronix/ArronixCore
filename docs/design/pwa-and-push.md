# Arronix — PWA, Service Worker & Web Push (design v0.1)

> **Status:** Design. No code has been written. This document is the direct input to the implementation phase.
>
> **Targets:** `src/Arronix.Client` (Blazor WebAssembly PWA), `src/Arronix.Api` (ASP.NET Core REST + SignalR
> host), `src/Arronix.Abstractions` (notification contracts), `src/Arronix.Host` (Web Push sender, subscription
> registry, VAPID key custody).
>
> **Dependency:** `docs/design/unified-host-runtime.md` **did not exist** when this was written. Everything here
> is grounded in `ARCHITECTURE.md` (§3, §4, §10, §11, §15), `docs/arronix-common-extraction-plan.md`, the
> contracts actually present in `src/Arronix.Abstractions/`, and the *arr notification model at
> `/Users/adam/Code/GitHub/Arronix/_reference/Radarr/src/NzbDrone.Core/Notifications/`. Where this design
> assumes a shape the host delivery owns — endpoint routes, the provider registry, the storage layer — it is
> marked **[host-owned]** and must be reconciled when that document lands.
>
> **Ownership note:** this delivery writes no files under `src/`. Every file named below is a *specification*
> for the delivery that owns that directory.

---

## How the contested calls were resolved

Every decision below was contested — by an alternative that is either the industry default, the thing the
Blazor template does, or the thing the *arrs already do. One position was chosen and the reason is recorded on
one line.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Render mode | Blazor Server (circuit, server-rendered) vs Blazor WebAssembly (standalone client) | **Blazor WebAssembly**, talking to a plain ASP.NET Core `Arronix.Api` over REST + SignalR | Settled by the product owner; and it is the only shape in which the *client* can be a real application — a Server circuit renders on the host, so an installed app with a dead host is an installed reconnect spinner. See [§1.1](#11-the-decision). |
| 2 | Can a plugin contribute a Razor component? | Yes (server-side ALC-loaded components) vs No (descriptors only) | **No — plugin UI is data, never code** | The server renders no UI and the client ships no plugin assembly, so there is nowhere for plugin code to run in the UI path; this *strengthens* §11 rather than weakening it. See [§1.3](#13-the-plugin-dimension-plugin-ui-is-data-not-code). |
| 3 | Is genuine offline operation a goal? | Offline-first PWA vs shell-only | **Shell-only. Offline data is explicitly out of scope** | The server holds every byte the user cares about; an offline media manager cannot grab, import, search or rename. The honest deliverable is *the app boots and explains itself*, not *the app works*. |
| 4 | Do we cache API responses in the service worker? | Stale-while-revalidate for `/api/**` vs never intercept | **Never intercept `/api/**`** | Cached JSON in a service worker is invisible to C# and un-testable from C#; failure handling belongs in one `DelegatingHandler` in `Arronix.Client`, not split across two languages. |
| 5 | Does the service worker synthesize offline responses for API calls? | Return a synthetic `503 {"error":"host-unreachable"}` vs let `fetch` reject | **Let it reject** | A synthetic response makes every HTTP status in the client a lie; one rejection handled in one handler is simpler and honest. |
| 6 | Microsoft's PWA template output, Workbox, or hand-rolled? | Use `service-worker.published.js` as shipped · add Workbox · write our own | **Keep the SDK's `ServiceWorkerAssetsManifest`; replace the service-worker body with ours** | The manifest is derived from the real publish graph and changes with every SDK version — never hand-roll that; the 40-line template body has no API/hub exclusions, no runtime caches, no update protocol and no push handlers, so it is not reusable. Workbox adds a Node toolchain for the one thing the SDK already provides. |
| 7 | Update activation | `skipWaiting()` on install vs prompt-then-reload | **Prompt, then reload** | Swapping the `_framework` payload under a running WASM instance breaks lazy-loaded assemblies and any in-flight fetch; a waiting worker with a stale-client banner is the correct failure mode. |
| 8 | How is a host upgrade detected? | Poll `blazor.boot.json` · 24 h browser SW check · server tells the client | **The server tells the client over SignalR, and every response carries a build header** | There is already a live hub; polling for something the server knows is a worse version of a push. |
| 9 | Do we cache HTML documents? | Cache `index.html` (template default) vs never | **Cache exactly one: `index.html`, served for in-scope navigations** | It *is* the app for a WASM SPA; but the navigation rule must be scope- and path-aware, not the template's blanket `mode === 'navigate'`. |
| 10 | Do we keep SRI `integrity` on precache requests? | Yes (template default) vs drop it (reverse proxies mangle bodies) | **Keep it, and fail loudly** | A body-rewriting proxy is a real, diagnosable misconfiguration; silently downgrading integrity turns it into an invisible one. See [§2.7](#27-failure-modes-of-the-precache). |
| 11 | Is Web Push a notification provider or its own subsystem? | Bespoke push subsystem vs member of the `Providers/` family | **A provider — with a host-owned subscription registry beside it** | It gets per-event filtering, test-connection, settings validation, status backoff and the settings UI for free; but Radarr's "everything in the settings JSON blob" persistence is rejected outright. See [§4.1](#41-decision-web-push-is-a-notification-provider). |
| 12 | Where do subscriptions live? | In `WebPushSettings` JSON (the *arr shape) vs their own table | **Their own table, host-owned** | Subscriptions arrive dynamically from N browsers, are per-user and per-device, expire independently, and are secrets; a settings blob supports none of that, and `ProviderFactory.Active()` would delete the provider on an empty list. |
| 13 | Can a plugin ship a Web Push provider? | Yes (it's just a provider) vs no (reserved) | **No — `web-push` is a reserved, host-registered provider id** | §11: a plugin that could mint pushes could mint them to endpoints it chose; the endpoints never cross the plugin boundary. |
| 14 | Web Push implementation | `WebPush` / `Lib.Net.Http.WebPush` NuGet vs BCL | **BCL only** (`ECDsa`, `ECDiffieHellman.DeriveRawSecretAgreement`, `HKDF`, `AesGcm`) | Both packages are thin, lightly maintained wrappers over ~250 lines of RFC 8291/8292; the `Arronix.*` dependency policy is Microsoft-owned only, and RFC 8291 §5 ships test vectors. |
| 15 | VAPID private key custody | `appsettings.json` · env var · host config store | **Host config store under `AppDataFolder`, 0600, backup-critical** | It is an install-scoped secret whose rotation silently invalidates every subscription; `IAppPaths.AppDataFolder` is host-only by extraction-plan resolution #2, which is exactly the property required. |
| 16 | Encrypt subscription rows at rest? | Column encryption vs redaction + file permissions | **Redaction + permissions** | The same database already holds indexer API keys and download-client passwords in the clear; encrypting one column is theater unless the whole store is encrypted. |
| 17 | TLS for a LAN app | Ship an ACME client · self-signed + trust · document reverse proxy | **Terminate user-supplied certs; document reverse-proxy DNS-01 as primary and Tailscale Serve as the zero-config path; do not ship ACME** | Self-signed is the option that breaks worst on iOS, which is precisely the platform push exists for. See [§3.5](#35-recommendation). |
| 18 | Permission prompt timing | On first load · on install · earned moment | **Earned moment only, and on iOS gated behind home-screen install** | A first-load prompt is denied by reflex, and a denial is close to permanent — Safari in particular gives no second chance without a Settings excursion. |
| 19 | Default push event set | Everything on (the *arr default is per-connection) · conservative | **Conservative: grabs OFF, imports ON, failures ON, health-error ON, everything user-initiated OFF** | Push what the user must act on or would want to know without opening the app; anything the user just did is not push-worthy. |
| 20 | Offline mutation queue / Background Sync | Queue writes and replay vs disable writes | **Disable writes while unreachable** | Background Sync is Chromium-only, and a queued "search for this release" replayed 40 minutes later is worse than not having been sent. |
| 21 | Does the disconnected UI depend on the service worker? | Yes (needs cached shell) vs no | **No — it ships first and works with no service worker at all** | It is the highest-value deliverable and must not be blocked behind the TLS work; on a warm tab the client is already running when the server dies. |

---

## 1. What a PWA means for Arronix

### 1.1 The decision

**Settled: `Arronix.Client` is Blazor WebAssembly. `Arronix.Api` is a plain ASP.NET Core application that
exposes a REST API and a SignalR hub, serves static assets, and renders no UI.** There is no Blazor Server
circuit, no server-side rendering, and no Razor component executing on the host.

`Arronix.Client` references `Arronix.Abstractions` and nothing else from the solution — the same reference
discipline `docs/arronix-common-extraction-plan.md` imposes on plugins, and it works for the same reason:
`Arronix.Abstractions` has **zero package references** (`src/Arronix.Abstractions/Arronix.Abstractions.csproj`),
so it is trimmable and WASM-safe. The DTOs in `src/Arronix.Abstractions/DTOs/` and the identity types in
`src/Arronix.Abstractions/Identity/` are therefore the literal wire types, shared by compilation rather than by
a code generator.

This is recorded as a decision, not a recommendation. The alternatives are not live options and are not
analyzed below.

### 1.2 What this buys, stated without enthusiasm

A PWA over a self-hosted server application is mostly *not* about offline. Separating what is real from what is
cargo cult:

| Claim | Real? | What it actually means for Arronix |
|---|---|---|
| **Installability / standalone window** | **Yes** | A dock/taskbar/home-screen entry with no URL bar, no tab strip, and its own window. For an app the user opens twenty times a week to check a queue, this is the single most-used feature of the whole delivery. |
| **Mobile home-screen presence** | **Yes** | The realistic Arronix client is a phone. A home-screen icon is the difference between "I check it" and "I would have to find the bookmark". |
| **Push notifications** | **Yes** | The only mechanism by which a homelab box can reach a phone that is not running the app. Everything in §4 depends on the app being installed on iOS. |
| **App-shell instant load** | **Yes, and it is large here** | A Blazor WASM cold start pulls a multi-megabyte `_framework` payload. Precached, the second and every subsequent launch is a local read. This is the caching win that pays for the whole service worker. |
| **The app boots with the server down** | **Yes — and this is the interesting one** | Because the client is WASM, the *application code* genuinely runs locally. The shell renders, routing works, the settings page frame draws. See §1.4. |
| **Offline data / offline-first** | **No** | The server holds the library, the queue, the history, the indexer configuration and the download clients. There is no meaningful read to serve and no write that means anything. Explicitly out of scope — [§6](#6-deferred-and-out-of-scope). |
| **Background sync of queued user actions** | **No** | See resolution #20. A deferred "start a search" is a worse outcome than a refused one. |
| **Works without the LAN** | **No** | If the NAS is unreachable the application is unreachable. A PWA does not change network topology. |

The honest one-sentence scope: **the service worker and manifest are a client-side shell over a client-side
application whose data lives on a server. They make it install, launch fast, and fail well. They do not make it
work offline.**

### 1.3 The plugin dimension: plugin UI is data, not code

Under the settled architecture there is no `AssemblyLoadContext` anywhere near the UI, and this resolves the
hardest problem in the space by removing it:

- **The server renders no UI.** A plugin cannot contribute a Razor component for server-side rendering, because
  nothing renders server-side. The per-plugin ALC model in `ARCHITECTURE.md` §4 is untouched — it governs
  indexers, metadata providers, parsers, import pipelines and jobs, all of which are and remain server-side.
- **The client ships no plugin assembly.** Nothing from a plugin is ever downloaded to, loaded by, or executed
  in the browser.
- **Therefore plugin UI is necessarily descriptor-driven.** A plugin declares nav entries, settings field
  schemas, per-media-kind list/detail view descriptors, naming tokens and capability flags as *data*. The
  server merges them and exposes them; the generic WASM client renders them. This is the concrete form of the
  `ARCHITECTURE.md` §15 row *"Dynamic UI component registry keyed by plugin capability & token exposure"* — it
  is a registry of descriptors, not of components.

**The security consequence is a genuine strengthening of §11 and should be stated as such in
`ARCHITECTURE.md`:** the browser is outside the capability model entirely. A plugin's UI cannot hold a
capability-gated contract, cannot make an outbound request the host did not make, cannot read another plugin's
state, and cannot observe the user's session, because a plugin's UI is not a program. Capability gating only
has to be enforced in one place — the plugin's server-side DI scope — instead of in two places with different
enforcement mechanisms.

It replaces one problem with a smaller, well-bounded one, and the design must not lose sight of it:

> **Descriptors are untrusted input.** They originate in a plugin and are rendered by the host's client. The
> renderer must therefore treat them as text and structured widgets only:
>
> - No `MarkupString`, no `innerHTML`, no raw HTML anywhere in the descriptor render path.
> - Icons are referenced by **name from a host-owned icon set**, never by URL.
> - Links are validated against an allowlist of schemes (`https:`, and host-relative paths); `javascript:`,
>   `data:` and `blob:` are rejected at the server when the descriptor set is assembled, and again in the
>   client.
> - Any plugin-supplied binary asset (a provider logo, say) is served from
>   `/api/v1/plugins/{pluginId}/assets/{name}` **[host-owned]** with a content-type allowlist
>   (`image/png`, `image/svg+xml` sanitized or refused, `image/webp`), `Content-Disposition: inline`,
>   `X-Content-Type-Options: nosniff`, and a CSP that forbids script from that path.
> - The descriptor set is versioned by a `descriptorSetHash` so the client and the service worker can both tell
>   when the loaded plugin set changed ([§2.4](#24-plugin-load--unload-a-second-tier-of-invalidation)).

### 1.4 What "offline" actually means here, view by view

With a WASM client the boundary is not "the app works / the app doesn't". It is drawn between code and data:

| Layer | Server unreachable | Notes |
|---|---|---|
| Boot (`index.html`, `_framework`, runtime, assemblies) | **Works** — served from the precache | This is what installability buys. |
| Routing, layout, rendering, client-side validation | **Works** | It is all WASM. |
| The connectivity surface itself | **Works, and is the point** | See §1.5. |
| Static help/documentation text compiled into the client | **Works** | Cheap and worth doing for the "why can't I reach my server" copy. |
| Every data view: library, queue, calendar, history, search, settings values | **Does not work** | The server owns all of it. Each view degrades individually — §1.5. |
| Every write: add, search, grab, rename, import, edit settings | **Does not work, and is disabled rather than queued** | Resolution #20. |
| Authentication | **Does not work** | A session cannot be established against an unreachable host. If the client holds a valid session it stays on the shell; if not, the login route shows the unreachable surface instead of a failed login. |

So the deliverable is not offline capability. It is: **the installed app always opens, always renders, always
tells the truth about why it has no data, and recovers by itself the moment the server returns.**

### 1.5 The disconnected experience — the most important UI in this delivery

This is what the user experiences every time the NAS reboots, every time they open the phone app on mobile
data, and every time the Docker container restarts on an image pull. It gets designed, not error-handled.

#### 1.5.1 States

One client-side singleton, `IConnectivityMonitor` in `Arronix.Client`, owns a state machine. Its inputs are:
outcomes from the HTTP `DelegatingHandler`, `HubConnection` state transitions, `navigator.onLine` via JS
interop, `visibilitychange`, and the reachability probe.

| State | Entered when | Surface | Available actions |
|---|---|---|---|
| `Online` | REST succeeding **and** hub `Connected` | none | everything |
| `LiveUpdatesPaused` | hub `Reconnecting`/`Disconnected`, REST still succeeding | thin inline chip in the header: *"Live updates paused"* | everything; views fall back to manual refresh |
| `Unreachable` | REST failing (network error, or probe fails) | persistent banner: *"Can't reach Arronix at `nas.local:8989`"* + retry countdown + **Retry now** | reads show last-known-in-session data as `Stale`; writes disabled |
| `Offline` | `navigator.onLine === false` | same banner, copy: *"This device is offline"* | as above; the retry timer does not run until `online` fires |
| `ServerRestarting` | probe returns 502/503/504 (proxy up, app down) | banner copy: *"Arronix is restarting"* + a faster retry cadence | as above |
| `Unauthenticated` | any API call returns 401 | route to login, preserving the return URL | login only |
| `Incompatible` | build-header mismatch outside the compatible range ([§2.5](#25-host-upgrade-the-classic-stale-shell-failure)) | blocking modal: *"Arronix was updated"* | **Reload** only |
| `UpdateReady` | service worker `waiting` | dismissible toast: *"A new version is ready"* | **Reload**, **Later** |

Distinguishing `Unreachable` from `ServerRestarting` matters more than it looks: on a homelab the two have
completely different user actions ("go look at the NAS" vs "wait ten seconds"), and the probe's status code
tells them apart for free.

#### 1.5.2 Per-view degradation

Every data-bound view binds to a small discriminated state rather than to `T?`:

```csharp
// src/Arronix.Client/Data/RemoteData.cs  [client-owned]
public abstract record RemoteData<T>
{
    public sealed record Loading                                   : RemoteData<T>;
    public sealed record Ready(T Value, DateTimeOffset LoadedAt)   : RemoteData<T>;
    public sealed record Stale(T Value, DateTimeOffset LoadedAt)   : RemoteData<T>;
    public sealed record Unavailable(UnavailableReason Reason)     : RemoteData<T>;
}
```

- `Stale` renders the real data, dimmed, with a *"as of 14:32"* chip and writes disabled. It is reached only
  from `Ready` within the same session — nothing is persisted to disk (resolution #3; the persistence seam is
  deferred in [§6](#6-deferred-and-out-of-scope)).
- `Unavailable` renders a purpose-built empty state per view, not a generic error. The queue says *"The queue
  lives on the server. Reconnect to see it."*; the calendar says the same in its own shape. One shared
  component, one line of per-view copy.
- Nothing anywhere renders a raw exception, a browser error page, or an indefinite spinner. A spinner that
  cannot resolve is the failure mode this section exists to eliminate.

#### 1.5.3 Recovery, which must be automatic

```text
GET /app/ping   →  204 No Content, no auth, no logging, no service-worker interception
```

- Backoff: 1 s, then ×2 with full jitter, capped at 30 s. Reset on success and on any successful API call.
- **On `visibilitychange → visible`, probe immediately and reset the backoff.** This is the single most
  valuable line of code in the whole connectivity story: a phone that has been in a pocket for four hours
  otherwise sits on a 30-second timer and feels broken for half a minute after the user opens it.
- On `online` (from `navigator.onLine`), probe immediately.
- On probe success: re-establish the `HubConnection`, then re-run the load for the currently-routed view only.
  Do not stampede every cached view at once — a NAS that just finished booting does not need forty simultaneous
  requests.
- `HubConnection` gets `WithAutomaticReconnect` with an explicit retry policy rather than the default
  four-attempt sequence, because the expected outage here is *minutes* (a reboot), not seconds.

#### 1.5.4 Cold start with the server down

If the user launches the installed app while the server is down, the service worker serves `index.html` and
`_framework` from the precache, the WASM runtime boots, and the client comes up straight into `Unreachable`
with the shell fully rendered. That is the entire justification for precaching, and it should be an explicit
acceptance test: **kill the server, launch the installed app, and the result is a branded Arronix window
saying it cannot reach the host — not a browser dinosaur.**

The boot itself needs a splash, because WASM cold start is not instant even from cache. Use the framework's
built-in loading progress (`.loading-progress` driven by the `--blazor-load-percentage` /
`--blazor-load-percentage-text` CSS custom properties that the Blazor WASM template wires up) rather than an
indeterminate spinner, and put the Arronix mark from `Logo/Arronix.svg` above it.

---

## 2. Service worker design

### 2.1 Build integration: what comes from the SDK and what we write

Keep the SDK's manifest machinery; replace the worker body. In `src/Arronix.Client/Arronix.Client.csproj`
**[client-owned]**:

```xml
<PropertyGroup>
  <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
</PropertyGroup>

<ItemGroup>
  <ServiceWorker Include="wwwroot\service-worker.js"
                 PublishedContent="wwwroot\service-worker.published.js" />
</ItemGroup>
```

- `wwwroot/service-worker.js` — the **development** worker. Keep it a no-op that registers and does nothing
  else. A caching worker during development is a debugging tax paid every day.
- `wwwroot/service-worker.published.js` — **ours**, replacing the template's body wholesale.
- `service-worker-assets.js` — **generated at publish** by the SDK. It emits
  `self.assetsManifest = { version: "<hash>", assets: [{ url, hash: "sha256-…" }, …] }` covering the real
  publish output including the fingerprinted `_framework` payload.

Why not hand-roll the manifest: it is derived from Blazor's asset graph, which changes shape between SDK
versions (Webcil packaging, `.wasm` assemblies, fingerprinted filenames, ICU shards, precompressed variants).
Reimplementing it guarantees a silent break on the next `dotnet` upgrade. Why not Workbox: it would add a Node
toolchain to a .NET-only build to supply a precache manifest the SDK already supplies, and none of the custom
logic below is Workbox-shaped anyway.

> **Re-verify at implementation time (SDK-version-sensitive).** These facts move between .NET releases and
> must be checked against the SDK in `global.json` (currently `10.0.100-rc.1`) before coding:
> the boot-manifest filename and whether it is still a discrete `blazor.boot.json`; the exact asset extensions
> in the publish output; whether `BlazorCacheBootResources` still exists; whether the SDK stamps a version
> comment into the published worker or relies solely on `importScripts` byte comparison. The design below
> depends on `service-worker-assets.js` and on nothing else, precisely so that these can move without
> invalidating it.

### 2.2 Caching strategy by asset class

Two caches, plus one strictly bounded third. Nothing else is cached, ever.

| Asset class | URLs | Strategy | Cache | Why |
|---|---|---|---|---|
| **Framework payload** | `_framework/**` — runtime `.wasm`, assemblies, `dotnet.js`, ICU `.dat`, `.blat` | **Precache on install, cache-first, no revalidation** | `arronix-shell-{version}` | Fingerprinted and SRI-verified; a byte-identical URL can never mean different bytes. This is the megabytes. |
| **App static assets** | `css/**`, `js/**`, `fonts/**`, `_content/**` (RCL assets compiled into the client) | Precache on install, cache-first | `arronix-shell-{version}` | Fingerprinted by `MapStaticAssets`; same argument. |
| **Entry document** | `index.html` | Precache; served for **in-scope navigations only** | `arronix-shell-{version}` | It *is* the app for a SPA. Replaced atomically when a new worker activates. |
| **Manifest & icons** | `manifest.webmanifest`, `app/icons/**` | Precache, cache-first | `arronix-shell-{version}` | Small, stable, and needed by the OS at install time. |
| **Offline-safe app data** | `app/asset-manifest.json` (plugin asset set) | Network-first, cache fallback | `arronix-runtime` | Changes at runtime, not at build. §2.4. |
| **Plugin binary assets** | `api/v1/plugins/*/assets/**` | Stale-while-revalidate, keyed by `descriptorSetHash` | `arronix-runtime` | Appear and disappear with the plugin set; small; never authoritative. |
| **Media images / posters** | `mediacover/**` **[host-owned route]** | Cache-first with LRU eviction and a hard cap | `arronix-images` | Large, numerous, immutable per content hash, and re-fetched on every library scroll. **This is the one place caching genuinely pays.** §2.3. |
| **REST API** | `api/**` | **No interception at all** | — | Resolutions #4 and #5. |
| **SignalR** | `hubs/**` **[host-owned route]** | **No interception at all** | — | §2.6. |
| **Reachability probe** | `app/ping` | **No interception at all** | — | Intercepting the thing that detects the network defeats it. |
| **Auth** | `login`, `logout`, `api/v1/auth/**` | **No interception at all** | — | A cached auth response is a security bug. |
| **The worker itself** | `service-worker.js`, `service-worker-assets.js` | **Never cached; served `Cache-Control: no-cache`** | — | §2.5. |
| **Cross-origin** | anything not same-origin | Pass through untouched | — | Arronix talks to no third party from the browser. |

### 2.3 The image cache, in detail

Posters and fanart are the only class where the cache changes the felt performance of the app rather than just
its cold start.

- **Key:** the request URL. Media cover URLs must be content-addressed or carry a version query
  (`/mediacover/tv/812/poster.jpg?v=<hash>`) **[host-owned]** so that artwork replacement invalidates cleanly.
  If the host cannot supply that, downgrade this class to stale-while-revalidate and accept a one-load lag.
- **Cap:** 300 entries **or** 100 MB, whichever binds first. Eviction is LRU, with access times kept in an
  IndexedDB object store (`arronix-image-index`) because the Cache API has no ordering.
- **Quota awareness:** before each write, consult `navigator.storage.estimate()`; if `usage / quota > 0.6`,
  evict to 40% before writing. Browsers evict whole origins under pressure, and losing the *shell* cache to
  make room for posters would be a self-inflicted cold start.
- **Persistence:** request `navigator.storage.persist()` once, at the same earned moment as the install prompt
  ([§4.8](#48-the-permission-prompt-the-earned-moment)) — not at first load, where it is another reflexive
  dismissal.
- Range requests and opaque responses are not cached.

### 2.4 Plugin load / unload: a second tier of invalidation

Plugin UI is descriptors, so a plugin loading or unloading **must not require a client reload and must not
produce a new service worker**. The two tiers:

| Tier | Trigger | Mechanism | User impact |
|---|---|---|---|
| **Build** | Host/client upgrade | New `assetsManifest.version` → new worker → install → prompt → reload | One prompt |
| **Runtime** | Plugin loaded, unloaded or upgraded | `descriptorSetHash` changes → hub message → client refetches descriptors → posts `RESYNC_ASSETS` to the worker → worker re-syncs `arronix-runtime` from `/app/asset-manifest.json` | None |

```text
Plugin loader activates a plugin
        │
        ├─► descriptor registry recomputes descriptorSetHash            [host-owned]
        │
        ├─► SignalR:  PluginSetChanged { descriptorSetHash }
        │
        ▼
Arronix.Client
        ├─► GET /api/v1/ui/descriptors  (If-None-Match: <old hash>)
        ├─► re-renders nav + settings + per-kind views
        └─► navigator.serviceWorker.controller.postMessage(
                { type: 'RESYNC_ASSETS', hash })
                        │
                        ▼
              Service worker
                        ├─► GET /app/asset-manifest.json
                        ├─► cache.addAll(new URLs)
                        └─► delete cached URLs no longer listed
```

`/app/asset-manifest.json` **[host-owned]** is the runtime counterpart to the build-time
`service-worker-assets.js`:

```json
{
  "descriptorSetHash": "b7c19f2e",
  "assets": [
    { "url": "api/v1/plugins/tv/assets/logo.png" },
    { "url": "api/v1/plugins/movies/assets/logo.png" }
  ]
}
```

No SRI here: these are runtime resources whose hashes the build cannot know. They are also non-authoritative —
a missing plugin logo degrades to the host icon set, it does not break a view.

### 2.5 Host upgrade: the classic stale-shell failure

The failure mode to design against is precise: **between a host upgrade and the user's next reload, old client
code is talking to a new API.** Three mechanisms, layered.

**1. Detection — the server tells the client.**

Every API response carries `X-Arronix-Build: <buildId>`; the client sends `X-Arronix-Client-Build` on every
request. The server also broadcasts `HostUpgraded { buildId }` over the hub at startup. On mismatch the client
calls `registration.update()` immediately rather than waiting for the browser's ≤24 h worker check. Polling
`blazor.boot.json` for this is rejected (resolution #8): there is a live hub, and the server knows.

**2. Compatibility — decide whether the mismatch is fatal.**

The API declares a minimum compatible client build. If the running client is below it, the client enters
`Incompatible` and shows a blocking **Reload** modal; if it is merely behind, it shows the dismissible
`UpdateReady` toast. This reuses the `contracts.arronix` range semantics from `ARCHITECTURE.md` §8 rather than
inventing a second versioning vocabulary.

**3. Activation — prompt, never `skipWaiting()` silently.**

```js
// page side  (src/Arronix.Client/wwwroot/js/pwa.js)  [client-owned]
let reloading = false;
navigator.serviceWorker.addEventListener('controllerchange', () => {
  if (reloading) return;
  reloading = true;
  location.reload();
});

registration.addEventListener('updatefound', () => {
  const installing = registration.installing;
  installing.addEventListener('statechange', () => {
    // A waiting worker with an existing controller means: a genuine update, not a first install.
    if (installing.state === 'installed' && navigator.serviceWorker.controller) {
      Arronix.notifyUpdateReady();          // -> UpdateReady toast in Blazor
    }
  });
});

// invoked from the toast's "Reload" button
export function applyUpdate() {
  registration.waiting?.postMessage({ type: 'SKIP_WAITING' });
}
```

```js
// worker side
self.addEventListener('message', event => {
  if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
  if (event.data?.type === 'RESYNC_ASSETS') event.waitUntil(resyncRuntimeAssets());
});
```

`skipWaiting()` on install is rejected (resolution #7): swapping the `_framework` payload under a running WASM
instance breaks any lazy-loaded assembly and any in-flight `fetch`, and the symptom is an unreproducible
`FileNotFoundException` from the runtime.

**Caching headers the server must set** **[host-owned]** — getting these wrong is the actual cause of most
"stale PWA" reports:

| Path | Header |
|---|---|
| `/service-worker.js` | `Cache-Control: no-cache` |
| `/service-worker-assets.js` | `Cache-Control: no-cache` — it is `importScripts`-ed and byte-compared during the update check |
| `/index.html` | `Cache-Control: no-cache` |
| `/manifest.webmanifest` | `Cache-Control: no-cache`, `Content-Type: application/manifest+json` |
| `/_framework/**`, fingerprinted assets | `Cache-Control: public, max-age=31536000, immutable` |

### 2.6 Implementation hazard: never intercept the API or the hub

A naive catch-all `fetch` handler — including the one the Blazor PWA template ships, whose navigation rule is
`event.request.mode === 'navigate'` with no path check — will break this app. The concrete rules:

```js
const SCOPE = new URL(self.registration.scope);        // honors UrlBase / reverse-proxy subpath
const BASE  = SCOPE.pathname;                          // e.g. "/" or "/arronix/"

// Paths the worker must never touch, relative to BASE.
const PASSTHROUGH = [
  /^api\//,                 // REST — resolution #4
  /^hubs\//,                // SignalR: negotiate POST and any long-poll/SSE fallback are fetches
  /^app\/ping$/,            // reachability probe
  /^login$/, /^logout$/,    // auth
  /^service-worker(-assets)?\.js$/,
];

self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);

  // 1. Cross-origin: never touch.
  if (url.origin !== SCOPE.origin) return;

  // 2. Non-GET: never touch. (Push subscribe, settings writes, hub negotiate.)
  if (event.request.method !== 'GET') return;

  // 3. Out of scope: never touch.
  if (!url.pathname.startsWith(BASE)) return;

  const rel = url.pathname.slice(BASE.length);

  // 4. Explicit passthrough list.
  if (PASSTHROUGH.some(rx => rx.test(rel))) return;

  // 5. In-scope navigation -> the cached SPA document.
  if (event.request.mode === 'navigate') {
    event.respondWith(caches.open(SHELL_CACHE).then(c => c.match('index.html'))
      .then(r => r ?? fetch(event.request)));
    return;
  }

  event.respondWith(handleAsset(event.request, rel));
});
```

Notes that are easy to get wrong:

- **WebSocket handshakes are not `fetch` events** — a service worker cannot intercept them, so the hub's
  primary transport is safe by construction. The `negotiate` POST and any long-polling or SSE fallback **are**
  fetches and must be excluded explicitly. Rules 2 and 4 both cover this; keep both, because a future
  `GET`-based transport should not silently become interceptable.
- **`return` with no `respondWith` is the correct passthrough**, not `event.respondWith(fetch(event.request))`.
  The latter re-issues the request through the worker and loses request properties (streaming upload,
  keepalive, priority hints) for no benefit.
- **Rule 3 is what makes `UrlBase` work.** Arronix behind a reverse proxy at `/arronix/` must not serve
  `index.html` for a navigation to `/sonarr/` on the same host.
- **Rule 5 must not run for OS-initiated navigations to non-app URLs** — this is already covered by rules 3 and
  4, but it is the reason the template's blanket rule is unusable.

### 2.7 Failure modes of the precache

| Failure | Symptom | Design response |
|---|---|---|
| `cache.addAll` rejects (any asset 404s or fails SRI) | New worker never installs | **Correct behavior, keep it.** The old worker stays in control and the app keeps working on the previous version. Log to the console and surface it in Settings → System → App. |
| A reverse proxy recompresses or minifies responses | SRI mismatch → every install fails → PWA silently never updates | Keep `integrity` (resolution #10). Emit a distinguishable console error and a health check ([§4.11](#411-health-telemetry-and-redaction)) naming the likely cause. **Do not** retry without integrity — that converts a diagnosable misconfiguration into a permanent invisible one. |
| Storage quota exceeded mid-install | Partial cache, rejected install | Evict `arronix-images` entirely and retry the install once. Images are recoverable; the shell is not. |
| Two shell caches coexist (old active, new waiting) | ~2× the payload on disk until reload | Accepted. Bound it by deleting every non-current `arronix-shell-*` in `activate`. |
| User never reloads | Runs old client indefinitely | The `Incompatible` gate (§2.5) forces the issue when the API actually changed; otherwise the waiting worker activates naturally when all clients close. |
| Blazor's own boot-resource cache duplicates the precache | ~2× storage for `_framework` | Disable the framework's boot-resource caching when the worker is in charge (`BlazorCacheBootResources` — **verify the property still exists** in the target SDK). |

---

## 3. Web App Manifest and the secure-context problem

### 3.1 `manifest.webmanifest`

Served from the client's `wwwroot`, with `Content-Type: application/manifest+json`. Placeholders in `{}` are
substituted at publish or at request time from the configured `UrlBase` **[host-owned]**.

```json
{
  "id": "{base}",
  "name": "Arronix",
  "short_name": "Arronix",
  "description": "Unified media automation for your server — TV, movies, music and books in one place.",
  "start_url": "{base}?src=pwa",
  "scope": "{base}",
  "display": "standalone",
  "display_override": ["window-controls-overlay", "standalone", "minimal-ui"],
  "orientation": "any",
  "theme_color": "#1f2430",
  "background_color": "#1f2430",
  "lang": "en",
  "dir": "ltr",
  "categories": ["utilities", "productivity"],
  "prefer_related_applications": false,
  "icons": [
    { "src": "app/icons/icon-192.png",  "sizes": "192x192", "type": "image/png", "purpose": "any" },
    { "src": "app/icons/icon-256.png",  "sizes": "256x256", "type": "image/png", "purpose": "any" },
    { "src": "app/icons/icon-384.png",  "sizes": "384x384", "type": "image/png", "purpose": "any" },
    { "src": "app/icons/icon-512.png",  "sizes": "512x512", "type": "image/png", "purpose": "any" },
    { "src": "app/icons/maskable-192.png", "sizes": "192x192", "type": "image/png", "purpose": "maskable" },
    { "src": "app/icons/maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" },
    { "src": "app/icons/mono-512.png",  "sizes": "512x512", "type": "image/png", "purpose": "monochrome" },
    { "src": "app/icons/icon.svg",      "sizes": "any",     "type": "image/svg+xml", "purpose": "any" }
  ],
  "screenshots": [
    { "src": "app/screenshots/library-wide.png",   "sizes": "1280x800", "type": "image/png", "form_factor": "wide",   "label": "Library" },
    { "src": "app/screenshots/queue-wide.png",     "sizes": "1280x800", "type": "image/png", "form_factor": "wide",   "label": "Activity queue" },
    { "src": "app/screenshots/library-narrow.png", "sizes": "540x1170", "type": "image/png", "form_factor": "narrow", "label": "Library" },
    { "src": "app/screenshots/queue-narrow.png",   "sizes": "540x1170", "type": "image/png", "form_factor": "narrow", "label": "Activity queue" }
  ],
  "shortcuts": [
    { "name": "Activity",  "short_name": "Activity", "url": "{base}activity/queue",
      "icons": [{ "src": "app/icons/shortcut-activity-96.png", "sizes": "96x96", "type": "image/png" }] },
    { "name": "Calendar",  "short_name": "Calendar", "url": "{base}calendar",
      "icons": [{ "src": "app/icons/shortcut-calendar-96.png", "sizes": "96x96", "type": "image/png" }] },
    { "name": "Add media", "short_name": "Add",      "url": "{base}add",
      "icons": [{ "src": "app/icons/shortcut-add-96.png", "sizes": "96x96", "type": "image/png" }] },
    { "name": "System",    "short_name": "System",   "url": "{base}system/status",
      "icons": [{ "src": "app/icons/shortcut-system-96.png", "sizes": "96x96", "type": "image/png" }] }
  ]
}
```

**The `crossorigin` gotcha.** Arronix is behind authentication. The manifest link element must be:

```html
<link rel="manifest" href="manifest.webmanifest" crossorigin="use-credentials" />
```

Without `use-credentials` the browser fetches the manifest **without cookies**, gets a `401`, and silently
declines to offer installation. This has bitten every authenticated PWA and the symptom — "the install button
never appears, but everything else works" — gives no clue as to the cause. Alternatively, and preferably in
addition: allow `GET /manifest.webmanifest`, `/app/icons/**` and `/service-worker.js` anonymously. They contain
no secrets, and anonymous access removes an entire class of install failure.

**`display_override`.** `window-controls-overlay` first gives the desktop install a native-feeling titlebar and
reclaims ~32 px of vertical space, which matters on a queue table. It degrades to `standalone` everywhere it is
not supported. `minimal-ui` last, so a browser that supports neither still installs rather than falling back to
`browser`.

**Shortcuts** are honored by Android and by desktop Chromium (three to four entries) and **ignored by iOS**.
They cost four 96×96 icons and one manifest block; take them.

**Share target** — deferred to the last work package, not rejected. Sharing an IMDb/TMDb/TVDB URL from a phone
browser into Arronix's add flow is a real workflow, and it is a `GET` share target with no new server surface:

```json
"share_target": {
  "action": "{base}add",
  "method": "GET",
  "params": { "title": "title", "text": "text", "url": "url" }
}
```

It requires the `/add` route to accept and interpret those query parameters (a URL from a known metadata site
resolves to an external id; free text becomes a search term). iOS does not support Web Share Target at all.

### 3.2 Icon and screenshot assets to produce

Source: `Logo/Arronix.svg`. This is a real work package ([WP-P01](#wp-p01--brand-and-icon-asset-production))
because a PWA with a wrong-shaped icon looks broken on exactly the device the whole delivery targets.

| Asset | Size(s) | Notes |
|---|---|---|
| `favicon.ico` | 16, 32, 48 (multi-res) | Legacy tab icon. |
| `icon.svg` | vector | `purpose: any`; smallest and sharpest where supported. |
| `icon-{192,256,384,512}.png` | as named | `purpose: any`. Transparent background permitted. |
| `maskable-{192,512}.png` | as named | `purpose: maskable`. **All meaningful content inside the central 80%** (the safe zone is a circle of radius 40% of the shorter side); the outer 20% is bleed that Android will crop. This is *not* the same artwork as `icon-*` — it needs its own composition with a filled background. |
| `mono-512.png` | 512 | `purpose: monochrome`. Solid silhouette with alpha, for themed Android icons. |
| `apple-touch-icon.png` | 180×180 | **No alpha channel** — iOS composites transparency onto black. Referenced by `<link rel="apple-touch-icon">`, not from the manifest. |
| `notification-icon-192.png` | 192×192 | Large icon in the Android notification shade. |
| `notification-badge-96.png` | 96×96 | **Monochrome silhouette with alpha.** Android renders only the alpha channel as a small mask; a full-color badge appears as a gray blob. |
| `shortcut-{activity,calendar,add,system}-96.png` | 96×96 | Manifest shortcut icons. |
| `library-{wide,narrow}.png`, `queue-{wide,narrow}.png` | 1280×800 / 540×1170 | Install-dialog screenshots. Chromium needs at least one `wide` **and** one `narrow` to show the richer install UI; supply them once the corresponding views exist. |

Produce them from the SVG with a scripted, reproducible pipeline checked into `scripts/`, not by hand — they
will be regenerated every time the mark changes.

### 3.3 The secure-context problem

Service workers, the Push API and the Notification API all require a **secure context**. The specification
treats `https://` origins, plus `http://localhost`, `http://127.0.0.1` and `http://[::1]`, as potentially
trustworthy. It does **not** extend that to private IPv4 ranges, and the proposal to do so never shipped.

Concretely, for a self-hosted app:

| Origin | Secure context? | Consequence |
|---|---|---|
| `http://localhost:8989` | **Yes** | Full PWA and full push work in development, with no certificate. |
| `http://192.168.1.10:8989` | **No** | No service worker, no install, no push. The single most common way people actually reach their NAS. |
| `http://nas.local:8989` (mDNS) | **No** | `.local` gets no exemption. |
| `https://arronix.example.com` (public CA) | **Yes** | Everything works, including iOS. |
| `https://nas.tailnet-name.ts.net` (Tailscale) | **Yes** | Everything works, including iOS. |
| `https://192.168.1.10:8989` (self-signed) | **Yes, if the certificate is trusted** | Works on desktop after trust distribution; painful on iOS. |

**None of this breaks the app.** Because the client is WASM served as ordinary static files, an insecure-context
user gets a fully functional Arronix — they simply do not get install, offline shell or push. That is a real
consolation and it shapes the design: **every PWA feature must be feature-detected and degrade silently, and
the app must never nag an insecure-context user about a thing they may not be able to fix.** Surface it once,
in Settings → System → App, as an explanation with a link to the setup documentation.

### 3.4 The practical TLS options

| Option | How | Works on iOS? | Cost to the user | Verdict |
|---|---|---|---|---|
| **Reverse proxy + public CA cert via DNS-01** | Caddy/Traefik/nginx obtains a cert for a domain the user owns; an A record (public or split-horizon) points at the LAN IP. No inbound port forwarding is required, because DNS-01 does not need HTTP reachability. | **Yes** | Owns a domain; runs one more container; edits DNS once | **Primary recommendation** |
| **Tailscale Serve / `tailscale cert`** | Tailscale issues a real cert for `<host>.<tailnet>.ts.net` and terminates TLS. MagicDNS resolves it on every enrolled device. | **Yes** | Installs Tailscale on the server and on each client | **Recommended zero-config alternative**, and it solves remote access at the same time |
| **Cloudflare Tunnel / equivalent** | A tunnel daemon exposes the app on a hostname with a valid cert; no ports opened. | **Yes** | Third-party dependency in the path of a private media app | Acceptable; document, do not recommend |
| **Host-terminated TLS with a user-supplied cert** | Arronix loads a PEM/PFX and serves HTTPS itself. `Arronix.Common/Security/` already exists for "server certificate material handling" per the extraction plan. | Depends on the cert's issuer | Must obtain the cert some other way | **Supported**, and the natural pairing with `tailscale cert` or an external ACME client |
| **Self-signed / private CA + trust distribution** | Generate a cert, install the CA on every client. | Technically, after installing a configuration profile **and** enabling it under Settings → General → About → Certificate Trust Settings | High, per device, repeated on every device the household owns | **Not recommended** |
| **mDNS `.local` + certificate** | Public CAs cannot issue for `.local` (reserved), so this collapses into the row above. | — | — | **Not viable** |
| **Browser insecure-origin flags** | `chrome://flags/#unsafely-treat-insecure-origin-as-secure` | No — does not exist on iOS | Per browser, per profile | Development only; never document as a solution |

### 3.5 Recommendation

1. **Arronix terminates TLS from a user-supplied certificate**, and ships **no ACME client**. Certificate
   acquisition is a solved problem with better tools than a media manager will ever contain, and shipping one
   means owning renewal failures at 3 a.m.
2. **Document two blessed paths**: reverse proxy with a DNS-01 wildcard for users who own a domain; Tailscale
   Serve for users who do not. Both give a real certificate that iOS accepts, which is the binding constraint.
3. **Explicitly document that self-signed will make iOS push painful**, rather than letting users discover it
   after buying into the setup.
4. **Ship a `SecureContextHealthContributor`** (`IHealthContributor`, `src/Arronix.Abstractions/Health/`) that
   reports `HealthStatus.Degraded` with `HealthSeverity.Info` when the configured external URL is `http://` and
   is not loopback, with a `RemediationHint` pointing at the setup document. `HealthCheck.Degraded(...)` in
   `src/Arronix.Abstractions/Health/HealthCheck.cs` already has exactly the shape needed.
5. **Feature-detect in the client**: `window.isSecureContext && 'serviceWorker' in navigator` gates the whole
   PWA surface. Nothing throws, nothing warns repeatedly, and the Settings page explains the situation once.

---

## 4. Push notifications

### 4.1 Decision: Web Push is a notification provider

The *arrs have a mature provider family for exactly this — one interface, N transports, per-connection event
filters, a test button, settings validation and failure backoff. The relevant reference files:

- `/Users/adam/Code/GitHub/Arronix/_reference/Radarr/src/NzbDrone.Core/Notifications/INotification.cs`
- `/Users/adam/Code/GitHub/Arronix/_reference/Radarr/src/NzbDrone.Core/Notifications/NotificationBase.cs`
- `/Users/adam/Code/GitHub/Arronix/_reference/Radarr/src/NzbDrone.Core/Notifications/NotificationService.cs`
- `/Users/adam/Code/GitHub/Arronix/_reference/Radarr/src/NzbDrone.Core/Notifications/NotificationDefinition.cs`
- `/Users/adam/Code/GitHub/Arronix/_reference/Radarr/src/NzbDrone.Core/ThingiProvider/ProviderFactory.cs`

**Web Push should be a member of that family.** Building it as a bespoke subsystem means reimplementing, badly
and separately: per-event opt-in, the test-connection button, settings validation, failure backoff and
disable-on-repeated-failure, the settings UI, and the "which connections fire for this event" fan-out. Every one
of those already has to exist for Discord and Pushover.

**Three things are rejected from the *arr model**, and they are exactly the things Web Push breaks:

1. **Persisting subscriptions in the settings JSON blob.** Radarr has one persistence slot per connection.
   PushBullet and Pushover work around this by letting the *remote service* own the device registry and storing
   chosen device ids in settings. Web Push has no such remote registry — the browser hands us an endpoint and
   two keys, per device, dynamically, and they expire independently. **Subscriptions get their own table.**
2. **`ProviderFactory.Active()` filtering on `Settings.Validate()`.** In Radarr an invalid-settings provider
   silently vanishes from the enabled list. If "zero subscriptions registered" were a validation failure, the
   Web Push provider would disappear the moment the last device unsubscribed — and reappear only after a
   registration that the UI no longer offers, because the provider is gone. **Zero subscriptions must be
   valid.**
3. **Reflection-derived `Supports*` flags.** `NotificationBase.HasConcreteImplementation` throws
   `MissingMethodException` on a rename and mis-reports when an override lives on an abstract intermediate base.
   Replace with an explicit `[Flags] NotificationEventKind SupportedEvents { get; }`.

### 4.2 The contract

New folder `src/Arronix.Abstractions/Notifications/`, marked `[Experimental]` with a new
`ExperimentalContracts.Notifications = "ARX0013"` identifier added to
`src/Arronix.Abstractions/Experimental/ExperimentalContracts.cs` (identifiers are permanent and never reused —
`ARX0001`–`ARX0012` are taken).

```csharp
// src/Arronix.Abstractions/Notifications/NotificationEventKind.cs
[Flags]
public enum NotificationEventKind
{
    None                      = 0,
    Grabbed                   = 1 << 0,
    Imported                  = 1 << 1,
    Upgraded                  = 1 << 2,
    ImportFailed              = 1 << 3,
    DownloadFailed            = 1 << 4,
    ManualInteractionRequired = 1 << 5,
    ItemAdded                 = 1 << 6,
    ItemDeleted               = 1 << 7,
    FilesRenamed              = 1 << 8,
    HealthDegraded            = 1 << 9,
    HealthRestored            = 1 << 10,
    UpdateAvailable           = 1 << 11,
    UpdateInstalled           = 1 << 12,
}
```

```csharp
// src/Arronix.Abstractions/Notifications/INotificationProvider.cs
[Experimental(ExperimentalContracts.Notifications, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface INotificationProvider
{
    /// <summary>Stable provider identity, e.g. "discord", "pushover", "web-push".</summary>
    string ProviderId { get; }

    string Name { get; }

    /// <summary>Which events this transport can render. Declared, not reflected.</summary>
    NotificationEventKind SupportedEvents { get; }

    Task NotifyAsync(NotificationEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Live connectivity test. Returns health results rather than a validation result so that the
    /// existing <see cref="Health.HealthCheck"/> vocabulary — status, severity, remediation hint —
    /// is reused instead of importing a second one.
    /// </summary>
    Task<IReadOnlyList<HealthCheck>> TestAsync(CancellationToken cancellationToken = default);
}
```

Reusing `HealthCheck` for `TestAsync` is deliberate: `src/Arronix.Abstractions/Health/HealthCheck.cs` already
carries `Status`, `Severity`, `Message` and `RemediationHint`, which is strictly more than a FluentValidation
`ValidationResult` conveys, and it keeps Abstractions at zero package references.

```csharp
// src/Arronix.Abstractions/Notifications/NotificationEnvelope.cs
public sealed record NotificationEnvelope(
    NotificationEventKind Kind,
    DateTimeOffset OccurredAt,
    NotificationSubject Subject)
{
    public string? CorrelationId { get; init; }

    /// <summary>Flat, display-ready facts: "quality", "indexer", "downloadClient", "size", "reason".</summary>
    public IReadOnlyDictionary<string, string> Facts { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Set for Health* events; null otherwise.</summary>
    public HealthCheck? Health { get; init; }
}

// src/Arronix.Abstractions/Notifications/NotificationSubject.cs
public sealed record NotificationSubject(string Title)
{
    public MediaKindId? MediaKind { get; init; }
    public MediaItemId? ItemId { get; init; }

    /// <summary>Per-kind qualifier: "S03E04 — The Sound of Thunder", "(2019)", "Disc 2".</summary>
    public string? Subtitle { get; init; }

    /// <summary>Host-relative image path, e.g. "/mediacover/tv/812/poster.jpg". Never absolute.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Host-relative deep link, e.g. "/tv/812/history". Never absolute, never external.</summary>
    public string? DeepLinkPath { get; init; }
}
```

### 4.3 Bridging per-kind payloads to a media-agnostic contract

This is the crux, and Radarr shows why: **8 of its 11 notification events carry a `Movie` in the signature**.
`GrabMessage`, `DownloadMessage`, `MovieDeleteMessage`, `MovieFileDeleteMessage` and
`ManualInteractionRequiredMessage` are all movie-typed; only `ApplicationUpdateMessage`, `OnHealthIssue` and
`OnHealthRestored` are media-agnostic. Radarr does not have this problem because Radarr has exactly one media
kind. Arronix does.

The bridge is a **per-media-kind renderer**, implemented by the plugin that owns the kind, resolved by the host:

```csharp
// src/Arronix.Abstractions/Notifications/IMediaNotificationRenderer.cs
[Experimental(ExperimentalContracts.Notifications, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaNotificationRenderer
{
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Turns "event X happened to item Y" into one line of display text plus a deep link and poster
    /// path. The plugin owns the answer to "how is one of my items described in a sentence"; the host
    /// owns everything else about the notification.
    /// </summary>
    ValueTask<NotificationSubject> RenderAsync(
        NotificationEventKind kind,
        MediaItemId itemId,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken = default);
}
```

The flow:

```text
TV plugin raises  MediaImportedEvent { kind:"tv", itemId:812, facts:{quality:"1080p WEB-DL", …} }
                  (IEventPublisher — src/Arronix.Abstractions/Events/IEventPublisher.cs)
                                    │
                          host NotificationDispatcher                          [host-owned]
                                    │
                  resolve IMediaNotificationRenderer for MediaKindId("tv")
                                    │
                  ► NotificationSubject {
                        Title       = "The Expanse",
                        Subtitle    = "S05E03 — Mother",
                        ImagePath   = "/mediacover/tv/812/poster.jpg",
                        DeepLinkPath= "/tv/812/history" }
                                    │
                  fan out to every INotificationProvider whose definition
                  has Imported enabled and whose tag filter matches
                                    │
             ┌──────────────────────┼──────────────────────┐
        Discord                 Pushover               web-push
```

Three properties this gives:

- **The notification contract stays media-agnostic.** No `Movie`, no `Series`, no `Album` anywhere in
  `Arronix.Abstractions.Notifications`.
- **Media semantics stay in the plugin**, satisfying `ARCHITECTURE.md` §1 ("all media semantics live in
  plugins") for a subsystem where the *arrs put them in Core.
- **A host fallback renderer exists** for a kind whose plugin supplies none: `Title` from the media store's
  canonical title, no subtitle, no poster. A missing renderer degrades the copy; it never suppresses the
  notification.

### 4.4 Which events warrant a push

The governing rule: **push what the user must act on, or would want to know without opening the app. Anything
the user just did is not push-worthy.**

| Event | Push default | Reasoning |
|---|---|---|
| `ManualInteractionRequired` | **ON**, urgency `high` | Definitionally blocked on a human. The best push this app can send. |
| `ImportFailed` | **ON**, urgency `high` | Actionable and silent otherwise — the failure mode people discover a week later. |
| `DownloadFailed` | **ON** | Same. Collapse repeats per download id. |
| `HealthDegraded` | **ON for `Error`/`Critical`; `Warning` opt-in** | Mirrors Radarr's `IncludeHealthWarnings` gate, which exists because warnings are chatty. Collapse per `CheckId`. |
| `Imported` / `Upgraded` | **ON**, urgency `normal` | The payoff event. Genuinely wanted on a phone. Must be collapsible — a season pack import is 10 events in 90 seconds; coalesce into one *"The Expanse — 10 episodes imported"*. |
| `UpdateAvailable` / `UpdateInstalled` | **ON**, urgency `low`, TTL 24 h | Low frequency, real value. |
| `Grabbed` | **OFF** | An RSS sync can grab twenty things at once. It is progress, not news. Opt-in for users who want it. |
| `HealthRestored` | **OFF** by default | Mostly noise — but when enabled it should *replace* the earlier notification via the same `tag`, not add one. |
| `ItemAdded`, `ItemDeleted`, `FilesRenamed` | **OFF**, not offered | The user performed these seconds ago in the UI they are looking at. |

Coalescing is a host concern, not a transport concern: the dispatcher batches same-kind, same-subject-root
events inside a short window (5 s) before handing an envelope to any provider, so Discord benefits too.

### 4.5 VAPID keys and payload encryption

**Standards:** RFC 8030 (Web Push protocol), RFC 8291 (message encryption, `aes128gcm`), RFC 8292 (VAPID),
RFC 8188 (the `aes128gcm` content encoding).

**Implementation: BCL only** (resolution #14). Everything needed is in the framework on `net10.0`:

| Step | API |
|---|---|
| Generate the VAPID key pair | `ECDsa.Create(ECCurve.NamedCurves.nistP256)` |
| Export/import the private key | `ExportPkcs8PrivateKey()` / `ImportPkcs8PrivateKey()` |
| Sign the VAPID JWT (ES256) | `ecdsa.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)` — JWS ES256 wants raw `r‖s`, which is exactly what `IeeeP1363FixedFieldConcatenation` produces |
| ECDH against the subscription's `p256dh` | `ECDiffieHellman.DeriveRawSecretAgreement(peerPublicKey)` (available since .NET 8; before it, this required a documented hack) |
| Key derivation | `HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, salt, info)` |
| Content encryption | `AesGcm` with a 16-byte tag |
| base64url | `Base64Url` helpers on `System.Buffers.Text` |

That is roughly 250 lines including the RFC 8188 record framing. RFC 8291 §5 publishes a complete worked
example (subscription keys, salt, expected ciphertext) — **use it as the unit-test vector**, which makes this
package independently testable long before any host or browser exists ([WP-P08](#wp-p08--web-push-crypto-and-sender)).

**Request shape:**

```http
POST /wpush/v2/AbCd… HTTP/1.1
Host: <push service>
TTL: 3600
Urgency: normal
Topic: imp-tv-812
Content-Encoding: aes128gcm
Content-Type: application/octet-stream
Authorization: vapid t=<JWT>, k=<base64url VAPID public key>
Content-Length: 1234

<encrypted record>
```

- `TTL` is required by RFC 8030. Use 3600 for imports and grabs (an hour-old grab notification is litter),
  86400 for health and update events.
- `Topic` (base64url, ≤32 characters) collapses undelivered messages **at the push service**, so a phone that
  has been off for two hours receives one health notification, not forty. Pair it with a matching client-side
  `tag` — the two collapse at different layers and both are needed.
- `Urgency` (`very-low` | `low` | `normal` | `high`) influences delivery on battery-constrained devices.
- The JWT: `{"aud": "<scheme://host of the endpoint>", "exp": <now + ≤24h>, "sub": "<contact>"}`. `sub` must be
  a `mailto:` or `https:` URI; default it to `https://github.com/Arronix/ArronixCore`, overridable in config
  with the operator's `mailto:`.

**Payload budget:** push services must accept at least 4096 octets of *encrypted* payload; after the RFC 8188
header, the AEAD tag and the padding delimiter the practical plaintext ceiling is just under 4 KB. **Budget
3 KB of JSON and never put an image in the payload** — the poster is a URL the service worker fetches (from the
image cache, which will usually already hold it).

**Response handling:**

| Status | Action |
|---|---|
| `201` / `200` | Success. `LastSuccessAt = now`, `ConsecutiveFailures = 0`. |
| `404`, `410 Gone` | The subscription is dead. **Delete the row.** Do not retry, do not disable-and-keep. |
| `429` | Honor `Retry-After`; requeue. Acquire from the shared `IRateLimiter` (`src/Arronix.Abstractions/Throttling/IRateLimiter.cs`) keyed by push-service host — this is `ARCHITECTURE.md` §12's "shared across plugins hitting the same remote", applied to FCM/APNs/Mozilla. |
| `413` | Payload too large — a bug. Emit telemetry with a fingerprint, drop, do not retry. |
| `400` | Malformed request or bad VAPID JWT — a bug. Telemetry, drop. |
| `5xx` | Retry with jittered exponential backoff, then escalate `ConsecutiveFailures`. Mirror the *arrs' `NotificationStatusService` escalation (5-minute floor, five escalation levels). |

**The sender** is a hosted background service **[host-owned]** draining a bounded `Channel<PushEnvelope>`, with
one HTTP request per subscription (Web Push has no batch API worth using) and bounded per-host concurrency.
Every request goes through `IHttpGateway` (`src/Arronix.Abstractions/Http/IHttpGateway.cs`) so that proxy
configuration, TLS policy, rate limiting and redaction apply uniformly rather than being reimplemented.

### 4.6 VAPID key custody

- **Generated once**, on first host start, if absent.
- **Stored** as PKCS#8 in the host config store under `IAppPaths.AppDataFolder` with `0600` permissions applied
  via `IFilePermissions` (`src/Arronix.Abstractions/FileSystem/IFilePermissions.cs`, implemented by the
  platform packs). `AppDataFolder` is host-only by design — extraction-plan resolution #2 exists precisely so
  that no plugin can read the host's config paths, which is the property this key needs.
- **Never** in `appsettings.json`, never in an environment variable printed by `docker inspect`, never in the
  support bundle.
- **The public key** is served by `GET /api/v1/push/vapid` (anonymous is fine — it is public by definition) as
  base64url of the raw 65-byte uncompressed P-256 point, which is the form `applicationServerKey` wants.
- **Rotation invalidates every existing subscription silently.** Therefore: no automatic rotation, ever. If an
  operator rotates deliberately, the host must mark every subscription `RequiresResubscribe` and the client
  must re-subscribe on next launch. Surface it — a rotation that silently kills push for the household is
  indistinguishable from a bug.
- **Backup-critical.** Restoring a database backup without the key file leaves every stored subscription
  undeliverable. Include the key in the backup set and say so in the restore documentation.

### 4.7 Subscription lifecycle

**Storage** **[host-owned]**, its own table:

| Column | Notes |
|---|---|
| `Id` | `Guid.CreateVersion7()` |
| `UserId` | The owning account; a pseudo-user when authentication is disabled for local addresses |
| `Endpoint` | **Unique index.** The capability-bearing URL — treat as a secret |
| `P256dh`, `Auth` | The browser's public key and auth secret from `subscription.toJSON().keys` |
| `ExpirationTime` | Nullable; from the subscription. Almost always null in practice |
| `DeviceLabel` | User-editable; defaults to a parsed user-agent summary ("iPhone · Safari") |
| `UserAgent` | Raw, for support |
| `EventMask` | `NotificationEventKind` — **per device**, not per provider. See below |
| `Enabled`, `CreatedAt`, `LastSeenAt`, `LastSuccessAt`, `ConsecutiveFailures` | |

`EventMask` being per-device rather than per-connection is the one place this design departs from the *arr
provider model, and it is the right departure: "notify my phone about failures but my desktop about everything"
is the actual requirement, and modeling it as N provider connections each with one subscription would be
absurd. The provider-level `NotificationDefinition` flags remain the outer gate; the per-device mask is an inner
filter.

**Lifecycle:**

1. **Subscribe** — client JS calls
   `registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey })`. `userVisibleOnly:
   true` is mandatory in Chromium and is the right constraint anyway: every push shows a notification.
2. **Register** — `POST /api/v1/push/subscriptions` with the session cookie and an antiforgery token, body
   `{ endpoint, keys: { p256dh, auth }, expirationTime, deviceLabel }`. Upsert on `Endpoint`.
3. **Remember** — store the registered endpoint in the client's local storage. On every launch, compare
   `pushManager.getSubscription()?.endpoint` with it; if they differ or the local record is missing,
   re-register. **Do not rely on `pushsubscriptionchange`** — it is unevenly implemented and easy to miss; the
   launch-time reconciliation is what actually keeps subscriptions alive.
4. **Handle `pushsubscriptionchange`** anyway, where supported: re-subscribe in the worker and `POST` the new
   subscription with `oldEndpoint` so the server can swap rather than duplicate.
5. **Expire** — `404`/`410` from the push service deletes the row immediately (§4.5).
6. **Prune** — a subscription with `ConsecutiveFailures > 10` is disabled and shown as "stale" in the device
   list; one with no success in 90 days is deleted.
7. **Logout** — an explicit logout deletes the subscription for that browser (a shared-device signal). Session
   *expiry* does not.

### 4.8 The permission prompt: the earned moment

**Never prompt on first load.** A cold `Notification.requestPermission()` is denied by reflex, and denial is
close to irreversible — Safari in particular offers no second chance without a trip to Settings, and Chromium
will auto-block after repeated dismissals across sites.

The earned moment, in order of preference:

1. **In Settings → Notifications**, behind an explicit "Enable push on this device" button. This is the honest
   default and always available.
2. **Contextually, after the user demonstrates the need**: when they open the same failed-import twice, or when
   a `ManualInteractionRequired` item has been waiting more than an hour, show a dismissible inline card —
   *"Arronix can tell you about this on your phone."* One button. Dismissal is remembered for 30 days.
3. **Never** as an interstitial, never on navigation, never more than once per 30 days unprompted.

The prompt chain, with the pre-checks that must precede the browser prompt:

```text
isSecureContext ?            no → Settings shows "requires HTTPS" + link to setup docs.  Stop.
'serviceWorker' in navigator?no → not supported on this browser.                          Stop.
'PushManager' in window ?    no → not supported.                                          Stop.
iOS/iPadOS and not standalone?  → show the "Add to Home Screen" instruction card.         Stop.  (§4.9)
Notification.permission === 'denied' ?  → show per-browser un-block instructions.         Stop.
                                        ▼
                        user clicks "Enable"  (a real user gesture — required by Safari)
                                        ▼
                        Notification.requestPermission()
                                        ▼
                        pushManager.subscribe(...)  →  POST /api/v1/push/subscriptions
                                        ▼
                        send a test push immediately, and say so
```

Sending a test push on success is not a nicety: it is the only way the user learns that the OS-level
notification settings for the installed app are also correct.

### 4.9 iOS and Safari — the biggest gotcha in this document

For a homelab app whose users are mostly on phones, this is the constraint that decides whether push ships or
merely exists.

- **Web Push on iOS/iPadOS requires the web app to be installed to the Home Screen.** In a Safari tab, the Push
  API is unavailable; there is nothing to prompt. This has been the rule since it shipped in iOS 16.4 and
  remains so.
- **There is no programmatic install.** `beforeinstallprompt` is Chromium-only. On iOS the user must tap Share
  → **Add to Home Screen**. Arronix must therefore ship an **iOS install card** with the actual gesture
  illustrated, shown when `/(iPad|iPhone|iPod)/.test(navigator.userAgent)` and
  `!window.navigator.standalone && !matchMedia('(display-mode: standalone)').matches`.
- **`display` must be `standalone` or `fullscreen`** in the manifest for the home-screen app to be treated as
  installed. Ours is `standalone`.
- **The permission prompt must be triggered by a user gesture** and cannot be re-shown after denial without the
  user going to Settings → Arronix → Notifications.
- **The certificate must be genuinely trusted.** This is where §3's TLS recommendation earns its place: a
  self-signed certificate on iOS requires installing a configuration profile *and* separately enabling full
  trust under Settings → General → About → Certificate Trust Settings, per device. Recommending Tailscale or a
  DNS-01 certificate is not fastidiousness — it is what makes iOS push achievable at all.
- **Icons:** iOS uses `apple-touch-icon` (180×180, no alpha) and ignores `maskable`. It also ignores manifest
  `shortcuts` and Web Share Target.
- **Storage:** Safari's ITP evicts service workers and storage for sites not visited in ~7 days. Home-screen
  web apps are treated as installed applications and are not subject to the same eviction, which is one more
  reason installation is the prerequisite rather than a nicety.
- **Consequence for the design:** the notification settings page must branch on platform. On iOS, before
  install, it shows the install instructions — **not** a disabled "Enable" button, which reads as "broken".

### 4.10 Per-device subscription management UI

Settings → Notifications → **This device** and **Registered devices**.

```text
This device                                                    ● Enabled
  iPhone · Safari 26 · registered 3 Aug, last push 2 hours ago
  Notify me about:
    [x] Import failed          [x] Manual interaction needed
    [x] Imported               [ ] Grabbed
    [x] Health errors          [ ] Health warnings
    [x] Updates
  [ Send test notification ]   [ Disable on this device ]

Registered devices                                             3 devices
  ● Desktop · Chrome · Linux      last push 14 minutes ago       [Rename] [Remove]
  ● iPhone · Safari               last push 2 hours ago          [Rename] [Remove]
  ⚠ Old phone · Chrome · Android  12 failures, no success in 40 days
       Stale — the browser is probably gone.                     [Remove]
```

Rules:

- **The endpoint is never displayed in full.** Show the push-service host and the last six characters
  (`fcm.googleapis.com · …Xk29Ab`). It is a capability-bearing URL (§4.11).
- Every device is removable from any device — losing a phone must not mean losing the ability to revoke it.
- "Send test notification" is per device and exercises the real path, including encryption and the push
  service.
- The stale state is a first-class row with an explanation, not a silent absence.

### 4.11 Health, telemetry and redaction

- **`WebPushHealthContributor`** (`IHealthContributor`) reports `Degraded` when: the VAPID key is missing or
  unreadable; more than half of all subscriptions have failed in the last 24 h; every subscription is stale;
  or the configured external URL is not a secure context. Each carries a `RemediationHint`.
- **Telemetry** (`ITelemetryEmitter`, `src/Arronix.Abstractions/Telemetry/ITelemetryEmitter.cs`) on send
  failure, with `Fingerprint = ["push", <push-service host>, <status code>]` so that a thousand `410`s group
  into one issue. `TelemetryEvent.Tags` must **never** carry an endpoint.
- **Redaction rules** contributed via `IRedactionRuleProvider`
  (`src/Arronix.Abstractions/Diagnostics/IRedactionRuleProvider.cs`) matching the push-service endpoint shapes
  — `fcm.googleapis.com/fcm/send/…`, `*.push.apple.com/…`, `updates.push.services.mozilla.com/…`,
  `*.notify.windows.com/…` — plus the `p256dh`/`auth` JSON keys. These are host-owned rules, registered by the
  host, not by a plugin.

### 4.12 Security, and how this satisfies ARCHITECTURE §11

**A plugin must not be able to mint pushes to arbitrary endpoints.** The mechanism, in order:

1. **The `web-push` provider id is reserved and host-registered.** The plugin loader rejects any plugin
   declaring it, with a dedicated `CoreErrorCode`. Providers are not equal citizens: some are host-owned.
2. **Subscription endpoints never cross the plugin boundary.** They are not in provider settings, not in any
   contract in `Arronix.Abstractions`, and not in any DTO a plugin can receive. A plugin's entire relationship
   with push is that it raises a domain event via `IEventPublisher`; the host decides whether that becomes a
   push.
3. **There is no `SendPush` API at any level.** The only plugin-facing notification surface is
   `INotificationProvider`, which a plugin implements to add *its own* transport to *its own* configured
   endpoint — which the user configured. Its outbound HTTP still goes through the capability-gated
   `ScopedHttpGateway`, so it is rate-limited, redacted and observable.
4. **The VAPID private key is under `AppDataFolder`**, which `IPluginPaths` deliberately does not expose
   (extraction-plan resolution #2).
5. **Endpoints are treated as secrets end to end**: unique-indexed but never logged, never in telemetry tags,
   truncated in the UI, excluded from support bundles, and covered by redaction rules (§4.11). Anyone holding
   an endpoint plus its keys can push to that device until it is revoked — that is the whole threat model, and
   it is why the endpoint, not the key material, is the thing to guard.
6. **The subscribe endpoint is CSRF-protected** (antiforgery token + `SameSite=Strict` session cookie) and
   rate-limited. The attack it prevents is not endpoint theft — subscriptions are origin-scoped, so a hostile
   page cannot obtain one for our origin — but **registration injection**: an attacker adding *their* device to
   *your* account and thereby receiving your notification content.
7. **Payloads are end-to-end encrypted** under RFC 8291. The push service — Google, Apple, Mozilla — cannot
   read them, so media titles do not leak to a third party. Be precise about what does leak: the push service
   sees the endpoint, the timing, the approximate size and the `Topic`. A `Topic` of `imp-tv-812` is metadata;
   prefer opaque topics (a truncated hash) over descriptive ones.
8. **Not encrypted at rest** (resolution #16), because the same store holds indexer API keys and
   download-client passwords in the clear. Encrypting one column would be theater. If whole-store encryption
   arrives later, subscriptions come along for free.

### 4.13 The service-worker push handlers

```js
// push: a notification MUST be shown for every push (userVisibleOnly), including on failure.
self.addEventListener('push', event => {
  event.waitUntil((async () => {
    let p;
    try { p = event.data.json(); }
    catch { p = { title: 'Arronix', body: 'Something happened.', tag: 'generic', url: '/' }; }

    await self.registration.showNotification(p.title, {
      body:  p.body,
      tag:   p.tag,                    // client-side collapsing; pairs with the Topic header
      renotify: p.urgency === 'high',
      icon:  base('app/icons/notification-icon-192.png'),
      badge: base('app/icons/notification-badge-96.png'),
      image: p.image ? base(p.image) : undefined,
      timestamp: p.ts,
      requireInteraction: p.urgency === 'high',
      data:  { url: p.url },
      actions: p.actions ?? [{ action: 'open', title: 'Open' }],
    });
  })());
});

// notificationclick: focus an existing window if there is one, else open.
self.addEventListener('notificationclick', event => {
  event.notification.close();
  const target = base(event.notification.data?.url ?? '/');
  event.waitUntil((async () => {
    const clientList = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const c of clientList) {
      if (new URL(c.url).pathname.startsWith(new URL(self.registration.scope).pathname)) {
        await c.focus();
        return c.navigate(target);
      }
    }
    await self.clients.openWindow(target);
  })());
});
```

The payload the sender produces, budgeted well under 3 KB:

```json
{
  "v": 1,
  "kind": "ImportFailed",
  "title": "Import failed",
  "body": "The Expanse — S05E03 · 1080p WEB-DL",
  "tag": "import-failed:tv:812",
  "url": "/tv/812/history",
  "image": "/mediacover/tv/812/poster.jpg",
  "ts": 1786000000000,
  "urgency": "high"
}
```

Two deliberate omissions: **no image bytes** (the worker fetches the URL, usually from `arronix-images`), and
**no action that mutates server state**. A notification action that fires a `fetch` from the worker may run
without a valid session and has no UI in which to report failure; actions therefore only ever navigate.

---

## 5. Implementation plan

Fourteen work packages. **Dep** names the unified-host milestone each one waits on. Four of them —
WP-P01, WP-P02, WP-P07, WP-P08 — have **no host dependency and can start immediately**, which matters because
they are also the three that historically block a PWA from shipping (icons, TLS, and crypto nobody wants to
write at the end).

| WP | Name | Dep | Can start now? |
|---|---|---|---|
| WP-P01 | Brand and icon asset production | none | **Yes** |
| WP-P02 | TLS and secure-context documentation | none | **Yes** |
| WP-P03 | Client skeleton, manifest, install detection | `Arronix.Client` exists | No |
| WP-P04 | Connectivity monitor and disconnected surface | `Arronix.Client` + `/app/ping` | No |
| WP-P05 | Service worker v1: precache, exclusions, offline shell | WP-P03 | No |
| WP-P06 | Update detection, build headers, reload prompt | WP-P05 + API build header | No |
| WP-P07 | Notification contracts in `Arronix.Abstractions` | none (contracts only) | **Yes** |
| WP-P08 | Web Push crypto and sender | WP-P07 | **Yes** |
| WP-P09 | VAPID custody, subscription registry, push API | WP-P08 + storage layer | No |
| WP-P10 | Notification provider family and dispatcher | WP-P07 + provider registry | No |
| WP-P11 | Client push subscription, earned prompt, device UI | WP-P05 + WP-P09 | No |
| WP-P12 | iOS install flow and platform branching | WP-P11 | No |
| WP-P13 | Plugin descriptor assets, runtime cache, image cache | plugin loader + media covers | No |
| WP-P14 | Share target | WP-P03 + `/add` route | No |

---

### WP-P01 — Brand and icon asset production

**Dep:** none. **Start immediately.**

**Creates:** `scripts/generate-pwa-icons.*` and the asset set in §3.2, produced from `Logo/Arronix.svg`:
`favicon.ico`; `icon-{192,256,384,512}.png`; `icon.svg`; `maskable-{192,512}.png` (separate composition, 80%
safe zone); `mono-512.png`; `apple-touch-icon.png` (180, **no alpha**); `notification-icon-192.png`;
`notification-badge-96.png` (monochrome, alpha only); `shortcut-{activity,calendar,add,system}-96.png`.

**Done when:** the generator is reproducible from the SVG in one command; the maskable icons survive a
crop-to-circle test at 40% radius; `apple-touch-icon.png` has no alpha channel.

**Note:** screenshots (§3.2) wait for the corresponding views and land in WP-P03.

---

### WP-P02 — TLS and secure-context documentation

**Dep:** none. **Start immediately.**

**Creates:** `docs/setup/https.md` — the §3.4 option table written as instructions: reverse proxy with a DNS-01
wildcard (Caddy and nginx examples), Tailscale Serve, host-terminated TLS with a supplied certificate, and an
explicit "why self-signed will hurt on iOS" section.

**Specifies (for the host delivery):** the `SecureContextHealthContributor` from §3.5.

**Done when:** a reader with a domain and a reader without one can each reach a green padlock on an iPhone by
following one path end to end.

---

### WP-P03 — Client skeleton, manifest, install detection

**Dep:** `src/Arronix.Client` exists as a Blazor WASM project with `UrlBase` handling.

**Specifies:** `wwwroot/manifest.webmanifest` (§3.1, with `{base}` substitution); `<link rel="manifest"
crossorigin="use-credentials">` plus `apple-touch-icon`, `theme-color` and `apple-mobile-web-app-*` head tags;
the boot splash using `--blazor-load-percentage`; `PwaInterop.cs` + `wwwroot/js/pwa.js` covering
`beforeinstallprompt` capture, `appinstalled`, and `display-mode: standalone` detection; Settings → System →
App showing install state, secure-context state and service-worker state.

**Done when:** Chromium offers installation over HTTPS; the installed window opens at `start_url` in
`standalone`; the manifest fetch carries credentials; everything degrades silently over plain HTTP.

---

### WP-P04 — Connectivity monitor and disconnected surface

**Dep:** `Arronix.Client` + `GET /app/ping` **[host-owned]**.

**This is the highest-value work package in the delivery and it deliberately does not depend on the service
worker or on TLS.** On a warm tab the client is already running when the server dies, so this alone fixes the
NAS-reboot experience for every user, including those stuck on plain HTTP.

**Specifies:** `IConnectivityMonitor` and the §1.5.1 state machine; the `DelegatingHandler` that feeds it;
`RemoteData<T>` (§1.5.2) and the shared `Unavailable`/`Stale` view components; the banner; the probe with
jittered backoff, `visibilitychange` and `online` triggers; `HubConnection` retry policy tuned for
minutes-long outages.

**Done when:** stopping the server mid-session produces the banner within 5 s and never a raw exception;
restarting it recovers with no user action; foregrounding a backgrounded phone recovers within ~1 s.

---

### WP-P05 — Service worker v1

**Dep:** WP-P03.

**Specifies:** `<ServiceWorkerAssetsManifest>` and the `<ServiceWorker>` item (§2.1); a no-op
`wwwroot/service-worker.js` for development; `wwwroot/service-worker.published.js` implementing §2.2 precache,
§2.6 exclusions and the scope-aware navigation rule; the §2.7 failure handling; the server cache headers in
§2.5 **[host-owned]**.

**Done when:** the acceptance test from §1.5.4 passes — **stop the server, launch the installed app, and get a
branded Arronix window in the `Unreachable` state**; `/api/**` and `/hubs/**` never appear in any cache; a
second cold start is served entirely from cache.

---

### WP-P06 — Update detection and reload prompt

**Dep:** WP-P05 + `X-Arronix-Build` on API responses **[host-owned]**.

**Specifies:** the build headers and the minimum-compatible-client declaration; the `HostUpgraded` hub message;
`registration.update()` on hub message, on launch and on `visibilitychange` (throttled); the
`updatefound` → `waiting` → toast → `SKIP_WAITING` → `controllerchange` → reload chain with its reload guard
(§2.5); the `Incompatible` blocking modal.

**Done when:** publishing a new build makes a running client show the toast within seconds; accepting it
reloads once, never twice; old shell caches are gone after activation.

---

### WP-P07 — Notification contracts

**Dep:** none — contracts only. **Start immediately.**

**Specifies (all under `src/Arronix.Abstractions/Notifications/`, every type `[Experimental]`):**
`NotificationEventKind.cs`, `INotificationProvider.cs`, `NotificationEnvelope.cs`, `NotificationSubject.cs`,
`IMediaNotificationRenderer.cs`. **Edits:** `Experimental/ExperimentalContracts.cs` to add
`Notifications = "ARX0013"`; `docs/contracts/stability.md` version history.

**Tier discipline:** by the extraction plan's three-tier rule, `INotificationProvider` and
`IMediaNotificationRenderer` are **Tier A** — they physically must cross the plugin boundary. Everything about
the subscription registry is **Tier C**: host-owned, and never promoted.

**Done when:** Abstractions still has zero package references; contract tests assert `[Experimental]` on every
new public type.

---

### WP-P08 — Web Push crypto and sender

**Dep:** WP-P07. **Start immediately** — this is pure, testable, host-independent code.

**Specifies:** RFC 8291 `aes128gcm` encryption over `ECDiffieHellman.DeriveRawSecretAgreement` + `HKDF` +
`AesGcm`; RFC 8292 VAPID JWT signing over `ECDsa` with `IeeeP1363FixedFieldConcatenation`; the RFC 8030 request
builder (`TTL`, `Topic`, `Urgency`); the §4.5 response-status state machine; the hosted `Channel`-based sender
over `IHttpGateway` with `IRateLimiter` keyed by push-service host.

**Done when:** the RFC 8291 §5 worked example round-trips byte-for-byte in a unit test; every response status
in §4.5 has a test; **zero non-Microsoft package references**.

---

### WP-P09 — VAPID custody, subscription registry, push API

**Dep:** WP-P08 + storage layer.

**Specifies:** key generation on first start, PKCS#8 under `AppDataFolder` at `0600` via `IFilePermissions`;
the subscriptions table (§4.7) with the unique endpoint index; `GET /api/v1/push/vapid`;
`POST|GET|DELETE /api/v1/push/subscriptions`, `PATCH .../{id}` for label and `EventMask`,
`POST .../{id}/test`; antiforgery and rate limiting on subscribe; the §4.11 redaction rules; inclusion of the
key in the backup set.

**Done when:** a subscription survives a host restart; a `410` deletes exactly one row; no endpoint appears in
any log at any level; the support bundle contains no endpoint.

---

### WP-P10 — Notification provider family and dispatcher

**Dep:** WP-P07 + the host provider registry.

**Specifies:** the provider registry, definitions with per-event flags and tag filters, status/backoff
(modeled on `NotificationStatusService`), the test endpoint, and the settings-descriptor surface; the
dispatcher that resolves `IMediaNotificationRenderer` by `MediaKindId`, applies the §4.4 defaults, coalesces
within a 5-second window, and fans out; the host fallback renderer; registration of `WebPushNotificationProvider`
as a **reserved, host-owned** provider id (§4.12).

**Done when:** a plugin declaring `web-push` is rejected at load with a specific error; an event with no
renderer for its kind still produces a sensible notification.

---

### WP-P11 — Client subscription, earned prompt, device management

**Dep:** WP-P05 + WP-P09.

**Specifies:** the §4.13 `push` and `notificationclick` handlers; the subscribe/reconcile flow (§4.7 steps 1–4)
including launch-time endpoint reconciliation; the §4.8 prompt chain and the contextual earned-moment card with
its 30-day suppression; the §4.10 device management UI with endpoint truncation;
`navigator.storage.persist()` at the same moment.

**Done when:** a real push from a real push service renders on Android and desktop; clicking it focuses an
existing window rather than opening a second; the app never prompts unbidden on first load.

---

### WP-P12 — iOS install flow

**Dep:** WP-P11.

**Specifies:** iOS + non-standalone detection; the Add-to-Home-Screen instruction card with the illustrated
gesture; platform branching in the notification settings page so iOS-before-install shows instructions rather
than a disabled button; the post-install re-entry that offers the permission prompt at the right moment.

**Done when:** an iPhone, from a fresh Safari visit over a valid HTTPS origin, can reach a delivered push
following only on-screen instructions.

---

### WP-P13 — Plugin descriptor assets, runtime cache, image cache

**Dep:** plugin loader + media cover endpoints.

**Specifies:** `/app/asset-manifest.json` and the `descriptorSetHash` (§2.4); the `PluginSetChanged` hub
message and the `RESYNC_ASSETS` worker message; the `arronix-runtime` stale-while-revalidate cache; the
`arronix-images` LRU cache with its IndexedDB index, 300-entry / 100 MB cap and quota back-off (§2.3); the
`/api/v1/plugins/*/assets/**` content-type allowlist and the §1.3 descriptor-rendering security rules.

**Done when:** loading and unloading a plugin changes the nav with no reload and no new service worker; the
image cache stays under its cap under a synthetic scroll of 2000 posters.

---

### WP-P14 — Share target

**Dep:** WP-P03 + an `/add` route that accepts query parameters.

**Specifies:** the §3.1 `share_target` block; `/add?url=…|text=…|title=…` handling that resolves a known
metadata-site URL to an external id and otherwise treats the text as a search term.

**Done when:** sharing an IMDb URL from Android Chrome opens Arronix's add flow pre-filled.

---

## 6. Deferred and out of scope

| Item | Why |
|---|---|
| **Genuine offline operation** | The headline exclusion. The server holds the library, queue, history and configuration; there is no read worth serving and no write that means anything. The deliverable is a client that boots and explains itself, not one that works. Revisit only if someone produces a concrete user story that survives the question *"and then what do you do with it?"* |
| **Offline mutation queue** | A queued "search for this release" replayed 40 minutes later is worse than a refused one: the indexer state, the queue and the user's intent have all moved. Writes are disabled while unreachable, visibly. |
| **Background Sync API** | Chromium-only; Safari and Firefox have declined to implement it. It exists to serve the mutation queue above, which we are not building. |
| **Periodic Background Sync** | Chromium-only, gated behind a site-engagement heuristic the app cannot control, and pointless here: the *server* does the periodic work, and it tells us over the hub when we are connected and over Web Push when we are not. |
| **Badging API (`navigator.setAppBadge`)** | **Deferred, not rejected.** "3 items need manual interaction" on the app icon is genuinely good, and it works on installed iOS web apps. It needs the push pipeline to set it from the worker, so it belongs immediately after WP-P11 — not before. |
| **Caching API responses for a read-only offline library** | Resolution #4. Arronix is not a media *browsing* app — that is Plex/Jellyfin's job. Revisit only with a real user story. |
| **Persisting `Stale` view data across sessions** | The in-session `Stale` state (§1.5.2) covers most of the value for free. Cross-session persistence needs an eviction policy, a staleness policy and a privacy story for a device that may be shared. Leave the `RemoteData<T>` seam in place so it can be added without touching views. |
| **File-handler registration (`.nzb`, `.torrent`)** | Chromium-only, and there is no file-ingestion endpoint to hand a file to. Revisit if manual import over HTTP is ever built. |
| **Protocol handlers (`web+arronix://`)** | No compelling flow. Deep links are ordinary HTTPS URLs already. |
| **Declarative Web Push** (Safari's service-worker-free JSON push) | Would simplify the iOS path, but it is Safari-only, recent, and does not remove the need for the standard path on every other platform. Re-evaluate at implementation time — **verify the exact content type and payload schema against current Safari documentation** before relying on any detail of it. |
| **Notification Triggers API** | Abandoned by its proposers. |
| **Native wrappers (MAUI/Blazor Hybrid, Capacitor)** | Solves nothing the PWA does not, and adds two app-store relationships to a self-hosted project that has no organization to hold them. |
| **Push fan-out policy beyond per-user devices** | No multi-user requirement is established. Per-user, per-device is the model; household-wide broadcast is a configuration question for later. |
| **Encrypting subscription rows at rest** | Resolution #16 — theater while indexer API keys sit beside them in the clear. Comes free if whole-store encryption is ever adopted. |
| **Automatic VAPID key rotation** | Rotation silently invalidates every subscription. Manual, deliberate, and loudly surfaced, or not at all. |
| **`window-controls-overlay` layout work** | Declared in `display_override` so it is available, but designing a custom titlebar region is a visual-design task with no functional dependency. |
| **A CI check for the PWA acceptance criteria** | Consistent with the extraction plan's CI deferral. The §1.5.4 cold-start-with-server-down test is the one worth automating first when CI exists. |

---

## 7. Open questions

1. **Route conventions.** `/app/ping`, `/app/asset-manifest.json`, `/api/v1/push/**`, `/hubs/**` and
   `/mediacover/**` are assumed. Reconcile against `docs/design/unified-host-runtime.md` when it lands; the
   service-worker exclusion list is generated from them and must not be hand-maintained in two places.
2. **Are media cover URLs content-addressed?** §2.3's cache-first strategy needs artwork replacement to change
   the URL. If the host cannot supply a version token, the image cache degrades to stale-while-revalidate.
3. **Multi-user.** Is Arronix single-account like the *arrs, or multi-user? The subscription table assumes a
   `UserId`; if there is only ever one account it becomes a constant, but the column should exist regardless.
4. **Authentication mode.** Cookie sessions versus bearer tokens changes the `crossorigin="use-credentials"`
   requirement (§3.1) and the logout-deletes-subscription rule (§4.7 step 7).
5. **Does the descriptor set include per-kind *layout*, or only fields?** The answer decides how far the generic
   renderer in §1.3 has to go, and therefore how much of the plugin UI story lands in this delivery versus the
   client delivery.
6. **SDK-version facts** flagged in the box in §2.1 — the boot-manifest filename, publish asset extensions and
   `BlazorCacheBootResources` — must be re-verified against the SDK pinned in `global.json` before WP-P05
   begins.
