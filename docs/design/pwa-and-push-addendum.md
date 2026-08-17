# Arronix — PWA & Web Push: Reconciliation Addendum (v0.1)

> **Status:** Addendum. It reconciles `docs/design/pwa-and-push.md` (design v0.1, 1,468 lines, written
> *before* the host specification existed) against `docs/design/unified-host-runtime.md` (Authoritative
> Specification, v0.3.0 milestone, 2,981 lines).
>
> **This document does not edit `pwa-and-push.md`.** That document remains the design of record for the
> service worker, the manifest, TLS/secure-context, the disconnected experience, the Web Push protocol
> implementation and the iOS story. Everything below either **closes** one of its §7 open questions,
> **corrects** a statement the specification has since falsified, or **names a gap the specification has
> not filled**. Where the two disagree, this addendum states which wins and why.
>
> **Reading order:** `pwa-and-push.md` first (it is self-contained), then this. Section numbers prefixed
> `PWA §` refer to that document; unprefixed `§` refers to `unified-host-runtime.md`.
>
> **Ownership:** this delivery writes no files under `src/`. Every file named below is a *specification*
> for the delivery that owns that directory.
>
> **Empirical basis for §5:** commands run against this repo's `global.json`
> (`10.0.100-rc.1.25451.107`, `rollForward: latestFeature`, `allowPrerelease: true`), which resolves to
> **SDK 10.0.301 / runtime 10.0.9** in the repository root. Every build fact below was executed, not
> recalled.

---

## 0. Executive verdict — separating conflict from drift

The brief asked for the difference between a **renaming** and a **real conflict**. The answer is lopsided:
the PWA document's *architecture* survives the specification almost entirely intact, and its
"descriptor-driven" vocabulary is a rename in every place but three. Its *contracts* do not survive: the
whole of PWA §4.2 is superseded.

**Five genuine conflicts, in descending order of consequence:**

| # | Conflict | Which document wins | Where |
|---|---|---|---|
| **C1** | **`IMediaNotificationRenderer` has no counterpart in the specification, and the specification has no seam that does its job.** §5.5:2143 asserts *"The originating plugin renders `Summary`"* but no interface anywhere in §2.11, §5 or §6 has a method that returns a `RenderedSummary`. `IMediaItemSource` (§2.11:829-835 + §6.5:2656-2658) exposes `QueryAsync`, `GetAsync`, `ResolveExternalAsync`, `ProposeAsync`, `CommitAsync` — and nothing that renders. This is a hole in the **specification**, not in the PWA document. | Neither — **spec gap**. Recommendation in [§4.2](#42-c1--the-missing-renderer-the-headline-finding). | PWA §4.3 vs §5.5, §2.11 |
| **C2** | **Notification contracts collide head-on.** PWA §4.2 creates `src/Arronix.Abstractions/Notifications/` under a new `ExperimentalContracts.Notifications = "ARX0013"`. **`ARX0013` is `Arronix.Abstractions.Shape`** (§7.2:2843). Notifications live in `Arronix.Abstractions.Providers` under `ARX0015`. The interface, the enum's shape, the enum's members, the payload record and the test-result type are all different. | **Specification**, on every point. | PWA §4.2 vs §5.5, §7.2 |
| **C3** | **`ArtworkRef.Url` is an absolute-capable `Uri`; `NotificationSubject.ImagePath` was "host-relative, never absolute".** §6.3:2431 declares `ArtworkRef(string Role, Uri Url, int? Width, int? Height)`, and metadata sources produce them (§5.6:2181). Nothing in the specification says artwork is host-served. There is **no `/mediacover/**` route and no artwork route of any kind** in §6.6. | **PWA document's intent wins; specification's typing must change.** Absolute third-party artwork URLs make the *browser* fetch posters from TMDb/fanart.tv on the user's IP — a privacy regression against a self-hosted threat model, an `img-src` CSP widening, and a cache key the service worker cannot content-address. Proposal in [§3.5](#35-media-cover-urls-and-the-artwork-proxy-open-question-2). | PWA §2.3, §4.2 vs §6.3, §6.6 |
| **C4** | **VAPID private key custody.** PWA resolution #15 says *never in settings* — a PKCS#8 file under `IAppPaths.AppDataFolder` at `0600`. §5.5:2151 says *"VAPID keys in settings fields marked `SettingSensitivity.Secret`"*. Additionally, **`IAppPaths` does not exist**: it is planned as Tier B in `Arronix.Common` (`docs/arronix-common-extraction-plan.md:279`, `:968`) and no work package in §7 creates it. | **Specification.** Reverse PWA resolution #15. Reasoning in [§4.7](#47-c4--vapid-custody-reversing-pwa-resolution-15). | PWA §4.6 vs §5.5 |
| **C5** | **Plugin-supplied binary assets and named icons.** PWA §1.3 specifies `/api/v1/plugins/{pluginId}/assets/{name}` for plugin logos and *"Icons are referenced by name from a host-owned icon set"*. The specification has **no plugin asset route**, no logo field on `ProviderDescriptor` (§5.1:1825-1837 — `InfoLink` only), and `IntentVocabularyTests` (§7.5:2901) **fails the build if any exported Abstractions member contains the substring `icon`**. §6.2:2314 lists `Icon = "trash"` as rejected-presentational. | **Specification.** Both features are deleted; the PWA document's mitigations are satisfied vacuously. | PWA §1.3, §2.4 vs §6.2, §7.5 |

**Two of the PWA document's six open questions cannot be closed.** The specification is **completely
silent on authentication and on multi-user** — no cookie, no session, no `UserId`, no auth annotation on
any route in §6.6, no mention in the resolution table or the deferred list. See
[§3.6](#36-multi-user-and-auth-mode-still-open-questions-3-and-4). This is escalated, not assumed away.

**What converged without anyone coordinating it:** PWA resolution #2 ("plugin UI is data, never code") and
PWA §1.3's recommendation to strengthen `ARCHITECTURE.md` §11 and amend §15 are *exactly* §6.2's governing
principle, resolution #37 and deviation 13. Two independent deliveries reached the same architecture from
different starting points; that is the strongest evidence either has that it is right.

---

## 1. How the contested calls were resolved

Same format as `docs/arronix-common-extraction-plan.md`. Every row below was genuinely contested — by the
PWA document, by the specification, or by the obvious third option.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| A1 | Is "descriptor-driven" a term that must be retired? | Retire it (the spec's principle is stronger) vs keep it (it is accurate) | **Keep the word, narrow the claim** | The specification's own vocabulary is *full* of `Descriptor` — `ActionDescriptor`, `StateDescriptor`, `FieldDescriptor`, `ProviderDescriptor`, `MediaKindDescriptor`, `WorkbenchDescriptor` (§6.3, §6.4, §5.1) — so the noun is not the problem; the problem is that "descriptor-driven UI" describes a *mechanism* where the settled principle constrains *what may be declared*. See [§2](#2-terminology-descriptor--intent). |
| A2 | Where do the notification contracts live? | PWA: new `Abstractions/Notifications/` + `ARX0013` · Spec: `Abstractions/Providers/` + `ARX0015` | **Specification, unconditionally** | `ARX0013` is already assigned to Shape (§7.2:2843); shipping PWA WP-P07 as written breaks `ExperimentalSurfaceTests`, `ContractAreaTests` and WP-3's file list simultaneously. |
| A3 | `INotificationProvider` or `INotifier : IProvider`? | PWA's standalone interface vs the spec's provider-spine member | **Specification** | PWA's interface carries no `ProviderInvocation`, so its settings must live on the instance — which is precisely the stateful `ProviderFactory.GetInstance` race resolution #28 exists to make unrepresentable. |
| A4 | `TestAsync` returns `IReadOnlyList<HealthCheck>` or `ValidationOutcome`? | PWA: `HealthCheck` (has `RemediationHint`) · Spec: `ValidationOutcome` | **Specification, with the loss recorded** | One `IProvider.TestAsync` for five families beats a sixth signature for one; the genuine loss is `RemediationHint`, and the fix is that *health* keeps `HealthCheck` while *test* returns validation — two questions, two vocabularies. See [§4.8](#48-testasync-what-was-actually-lost). |
| A5 | `[Flags] NotificationEventKind` or closed `NotificationEvent` + a list? | PWA's flags enum (a per-device mask is one `int`) vs the spec's non-flags enum | **Specification** | The per-device mask is Tier C host-owned storage (PWA resolution #12 already says so), so it may be a host-side bitmask over `(int)NotificationEvent` and needs no contract shape at all. |
| A6 | Does `IMediaNotificationRenderer` ship? | PWA: yes, a plugin-implemented seam · Spec: silent, but asserts plugins render | **THIRD — host derivation now, the seam deferred with a named trigger** | Four plugins ship this milestone and **none of them needs prose to work**: `ItemView.Title` + `Prominence`-ranked `FieldDescriptor`s + `CoordinateSpace` already carry everything a summary needs, and resolution #35's rule ("a declaration that can be derived is a declaration that can disagree") applies unchanged. Promoting a seam against zero *needing* implementers is what the three-tier rule exists to stop. |
| A7 | Where does the poster URL come from? | Absolute `ArtworkRef.Url` straight to the browser vs a host artwork proxy | **Host artwork proxy, content-addressed** | Letting the browser fetch `image.tmdb.org` reveals the user's library to a third party from the user's own IP, widens `img-src`, and gives the image cache a key it cannot invalidate — three problems one proxy solves. |
| A8 | Is `web-push` a reserved provider id needing loader enforcement? | PWA resolution #13: yes, reject the plugin with a `CoreErrorCode` · Spec: ids are `{pluginId}:{localId}`, minted by the registry | **Specification — the check becomes unnecessary** | §5.1:1809-1812 makes the collision structurally impossible; PWA WP-P10's done-when ("a plugin declaring `web-push` is rejected at load") is now untestable. A *different* question opens: what `PluginId` does a host-owned provider carry. See [§4.6](#46-the-reserved-provider-id-and-the-host-plugin-id). |
| A9 | VAPID private key: file under `AppDataFolder`, or a `Secret` settings field? | PWA resolution #15 vs §5.5:2151 | **Specification. PWA resolution #15 is reversed.** | `SettingSensitivity.Secret` already buys the sentinel round-trip, the automatic `RedactionRule` and the UI intent from **one declaration** (§5.2:1929-1936); a bespoke key file buys `0600`, which the database file needs anyway and which `IAppPaths` — an unbuilt type — was going to supply. |
| A10 | Does `HostUpgraded` become a ninth `EventKind`? | PWA §2.5: yes, a hub message · alternative: header + reconnect | **THIRD — no new `EventKind`; `X-Arronix-Build` header + `HubConnection.Reconnected`** | The one client that most needs the upgrade message is the *old* client, which cannot parse an enum member added after it shipped; and a server that just restarted will always fire `Reconnected`, so the announcement is free. See [§3.2](#32-the-hub-shape-message-names-and-the-build-header). |
| A11 | Does `descriptorSetHash` survive? | PWA §2.4's own hash vs the spec's existing ETag | **Specification's ETag on `/api/v1/kinds`** (§6.6:2682, §6.7:2756) | A second freshness token for the same payload is a second thing that can be wrong; `EventKind.PluginStateChanged` (§6.4:2574) is already the trigger and `If-None-Match` is already the mechanism. |
| A12 | Does the `arronix-runtime` cache tier survive? | PWA §2.2/§2.4's third cache vs deleting it | **Delete it** | It exists to cache `/app/asset-manifest.json` and plugin logos; the specification has neither, and plugin **unload** is explicitly not implemented this milestone (§8:2947), so the tier has no content and no trigger. |
| A13 | `/app/ping` or reuse `GET /health`? | Reuse the spec's existing unauthenticated health route vs a dedicated probe | **Dedicated `GET /ping` → 204** | `/health` returns **503 when degraded** (§4.9:1782) and fans out to every contributor with a 10 s timeout — a client polling it at a 1 s cadence would read "one indexer is unreachable" as "the host is restarting", which is precisely the distinction PWA §1.5.1 exists to draw. |
| A14 | Does the manifest need `{base}` substitution? | PWA §3.1's publish-time substitution vs relative URLs | **Relative URLs; delete the substitution** | Verified: the .NET 10 PWA template itself ships `"id": "./"`, `"start_url": "./"`; and the specification has **no `UrlBase` concept at all** (`Arronix:Api:ClientRoot` is a filesystem path, §6.1:2288), so there is nothing to substitute *from*. |
| A15 | Do the push endpoints force a `PackageReference` into `Arronix.Api`? | Yes (antiforgery/rate limiting) vs no | **No — verified** | Antiforgery, rate limiting, static files and SignalR server are all in the `Microsoft.AspNetCore.App` shared framework, so `ApiProjectShapeTests`' empty-package assertion (§7.5:2899) survives the push API intact. |
| A16 | Does the Web Push crypto need a NuGet package? | PWA resolution #14 says BCL-only | **Confirmed empirically** — see [§5](#5-build-facts-re-verified) | Every one of the six APIs PWA §4.5 names was executed on net10.0 and returned present, including `Base64Url`, which is in `System.Private.CoreLib`. |
| A17 | Does `RenderedSummary` fit in a push payload? | Yes vs it needs trimming | **THIRD — it is a superset and must be *projected*, not serialized** | `Title`/`Subtitle`/`Artwork`/`DeepLink` map 1:1 onto title/subtitle/artwork/deep-link, but `Fields[]` and `Artwork[]` are unbounded against a ~3 KB plaintext ceiling. See [§4.4](#44-rendersummary--push-payload-the-projection). |
| A18 | Is the PWA document's UI vocabulary (`toast`, `modal`, `banner`, `chip`) a violation? | Yes vs no | **No** | `IntentVocabularyTests` (§7.5:2901) scopes its deny-list to *exported members of `Arronix.Abstractions`*. Every one of those words lives in `Arronix.Client`, where naming a widget is correct — that is the only assembly permitted to know one exists. |

---

## 2. Terminology: "descriptor" → intent

### 2.1 The mapping — renames, not redesigns

PWA §1.3 says: *"a plugin declares nav entries, settings field schemas, per-media-kind list/detail view
descriptors, naming tokens and capability flags as data."* Every clause maps onto a settled type. Nothing
in that sentence has to be re-argued; it has to be re-spelled.

| PWA document says | Settled vocabulary | Where | Rename or more? |
|---|---|---|---|
| "descriptor" (generic) | **intent declaration**; the bundle is `PluginIntentSurface` | §6.3:2443 | Rename. The spec's own type names keep `Descriptor`. |
| "the descriptor set" | `MediaKindDescriptor` per kind; `GET /api/v1/kinds` for the set | §6.4:2475, §6.6:2681 | Rename. |
| "descriptor registry" | `IIntentRegistry` (Tier B, host-side) | §4.5:1553, §7 WP-8 | Rename. |
| "nav entries" | `BrowseAxis` + `BrowseAxisKind`; the nav is **derived by the client** from the kind list | §6.3:2364-2383 | Rename **plus a narrowing**: a plugin cannot name a nav item; it declares how its kind is traversed and the client builds navigation. |
| "settings field schemas" | `SettingsField` + `SettingRole` + `SettingSensitivity` | §5.2:1899-1936 | Exact rename. `ProviderForm.razor` renders all five families from `SettingsField[]` alone (§6.7:2763). |
| "per-media-kind list/detail view descriptors" | `LevelPresentation` + `FieldDescriptor.Prominence` + `BrowseAxisKind` | §6.4:2487, resolution #33 | Rename **plus a narrowing**: "list" and "detail" are presentations. The declaration is an importance rank on a field; the client decides that `Primary` fills a card face and `Detail` fills a detail pane. |
| "naming tokens" | `NamingToken[]`, sourced from the shape, manifest cross-checked at load | resolution #44, §9 deviation 2 | Rename. |
| "capability flags" | `Capability` / `CapabilitySet` (declared in the manifest) **and** `Affordance` (derived by the host) | §3.2:955-1009, §6.4:2499-2511 | Rename **plus a split**: what a plugin *may do* is declared; what a level *affords* is derived (resolution #35). |
| "the descriptor render path" | `Arronix.Client/Rendering/` + the five lookup tables | §6.7:2739-2748 | Rename. |
| "descriptors are untrusted input" | Still true, and now *bounded* by `IntentSurfaceValidator` at load | §6.6:2715-2719, §6.8:2783 | Rename. The residual blast radius is named identically in both documents: "a misleading label". |
| `descriptorSetHash` | the **ETag** on `GET /api/v1/kinds{,/{kind}}` | §6.6:2682 | Replacement — see [§3.4](#34-descriptorsethash--etag--pluginstatechanged). |

### 2.2 Where it is a real conflict, not a rename

Three places. All three are PWA §1.3's *security mitigations*, and in all three the specification's answer
is stronger, which is why the mitigation disappears rather than moving.

**(a) Named icons.** PWA §1.3: *"Icons are referenced by name from a host-owned icon set, never by URL."*
That is a rule about a field that may not exist. `IntentVocabularyTests` (§7.5:2901) fails the build if any
exported Abstractions type **or member** contains the substring `icon`, and §6.2:2314 replaces
`Icon = "trash"` with `Consequence.Destructive` + `ConfirmationRequirement.TypeToConfirm`. **Delete the
rule.** A plugin does not choose an icon by name, by URL, or at all; the client chooses one from
`Consequence`, `StateTone`, `Affordance` and `BrowseAxisKind`. This is a genuine strengthening: PWA §1.3
still allowed a plugin to influence pixels, and the specification does not.

**(b) Plugin binary assets.** PWA §1.3 and §2.4 specify
`GET /api/v1/plugins/{pluginId}/assets/{name}` with a content-type allowlist, `nosniff`, inline
disposition and a script-forbidding CSP — a well-designed mitigation for a route the specification does
not have. `ProviderDescriptor` (§5.1:1825-1837) has `InfoLink : Uri?` and no logo. `GET /api/v1/plugins`
returns `PluginStatusView` (§6.4:2535), which is status text. **Delete the route and its mitigations.**
A plugin that wants to show its own imagery has exactly one sanctioned mechanism —
`ExternalSurfaceDescriptor` (§6.3:2439), which *points at* a page the plugin hosts itself, outside the
client's origin and outside the capability model, and is explicitly not an embedding point.

**(c) Link-scheme validation.** PWA §1.3 requires rejecting `javascript:`, `data:`, `blob:` at assembly and
again in the client. This survives — but it is now `IntentSurfaceValidator`'s job, already specified:
*"`UriTemplate`s whose scheme is not `http`/`https`"* → `CoreErrorCode.PluginShapeInvalid` (§6.6:2717-2719).
**Keep the client-side second check** (defense in depth costs one function) and drop the server-side half
as already-owned. `RenderedSummary.DeepLink` and `ActionDescriptor` carry no URI at all, so the surface is
narrower than the PWA document assumed: `ExternalSurfaceDescriptor.UriTemplate` and `ArtworkRef.Url` are
the only two places a plugin-authored URI reaches the browser, and [§3.5](#35-media-cover-urls-and-the-artwork-proxy-open-question-2)
removes the second.

**Not a conflict, stated so nobody re-litigates it:** the `MarkupString`/`innerHTML` prohibition (PWA §1.3)
is unchanged and correct. §6.5:2675 independently reaches the same rule for the same reason:
*"`FieldValueKind.MultilineText` carries plain text only … admitting markup would be the first UI leak and
the first XSS surface."*

---

## 3. The open questions, closed

PWA §7 logs six. Four close cleanly, one closes with a proposal that depends on a spec change, and one
cannot close at all.

### 3.1 Route conventions (open question 1)

The specification's surface is §6.6:2681-2704. It uses exactly three prefixes: `/api/v1/…`, `/health`,
`/hub/events`. **There is no `/app/**` namespace and no `/hubs/**`.** The service worker's exclusion list
is derived from this table and must not be maintained in two places.

| PWA document assumed | Settled | Status |
|---|---|---|
| `GET /app/ping` | **`GET /ping` → 204 No Content** | New; host-owned; PWA-specified. Resolution A13: do **not** collapse into `/health`. |
| `GET /app/asset-manifest.json` | — | **Deleted.** Resolution A12. |
| `GET /api/v1/ui/descriptors` | `GET /api/v1/kinds` and `GET /api/v1/kinds/{kind}` (both ETagged) | Exists. §6.6:2681-2682. |
| `/hubs/**` | **`/hub/events`** (singular, one hub) | Exists. §6.6:2704. |
| `/mediacover/**` | — | **Does not exist.** Proposal: `GET /artwork/{hash}`. See [§3.5](#35-media-cover-urls-and-the-artwork-proxy-open-question-2). |
| `/api/v1/plugins/{id}/assets/**` | — | **Deleted.** [§2.2(b)](#22-where-it-is-a-real-conflict-not-a-rename). |
| `GET /api/v1/push/vapid` | `GET /api/v1/providers/definitions/{id}` | **Deleted** — the VAPID public key is a `SettingSensitivity.Public` field on the Web Push provider definition, and `Public` fields *are* returned by `GET` (§6.4:2578). One fewer endpoint, and it exercises machinery that must work anyway. |
| `/api/v1/push/subscriptions**` | — | New; the only genuinely new endpoint family. Blocked — see [§6](#6-work-package-status). |
| `/login`, `/logout` | — | **Unspecified.** [§3.6](#36-multi-user-and-auth-mode-still-open-questions-3-and-4). |

**The revised service-worker passthrough list** — this replaces PWA §2.6's `PASSTHROUGH` array verbatim:

```js
const SCOPE = new URL(self.registration.scope);   // still the right way to learn the base path
const BASE  = SCOPE.pathname;

// Paths the worker must never touch, relative to BASE. Derived from unified-host-runtime.md §6.6.
const PASSTHROUGH = [
  /^api\//,                        // every REST route            — PWA resolutions #4, #5
  /^hub\//,                        // SignalR: /hub/events. NOT /hubs/ — the spec has one hub, singular.
  /^ping$/,                        // the reachability probe
  /^health$/,                      // the unversioned machine-readable health route (§4.9)
  /^artwork\//,                    // handled by an explicit cache-first rule, not by the generic path
  /^service-worker(-assets)?\.js$/,
];
```

Two corrections to PWA §2.6 beyond the list itself:

- The rule `if (event.request.method !== 'GET') return;` remains load-bearing and now also covers the
  SignalR `negotiate` POST at `/hub/events/negotiate`. Keep both rules 2 and 4 for the reason PWA §2.6
  already gives.
- `/login` and `/logout` are removed from the list **because they are not known to exist**, not because
  they are safe to cache. If an auth surface lands, re-add it and re-run the acceptance test.

### 3.2 The hub: shape, message names, and the build header

**Shape.** One hub, `/hub/events`, one message type `EventEnvelope` (§6.4:2555-2569), with SignalR groups
per media kind plus a `system` group for plugin/health/job traffic (§6.6:2707-2708). The discriminant is a
closed `EventKind` enum with eight members (§6.4:2571-2575) — deliberately **not** `[JsonPolymorphic]`
(resolution #6). PWA §2.4's `PluginSetChanged` and PWA §2.5's `HostUpgraded` are therefore not "messages"
in any sense the specification recognizes; they would be enum members.

**`PluginSetChanged` → already exists.** `EventKind.PluginStateChanged = 6` with `EventEnvelope.PluginId`
populated. No new member. See [§3.4](#34-descriptorsethash--etag--pluginstatechanged).

**`HostUpgraded` → do not add it (resolution A10).** The argument is not stylistic:

> `EventKind` is a closed enum compiled into `Arronix.Client`. §6.7:2750-2753 makes an unhandled value a
> `CS8509` build failure — a real benefit for `BrowseAxisKind`, where a value the client cannot render is
> genuinely unrenderable. But an *upgraded server* sending an *old client* a kind it does not know is the
> normal case on the wire, and the single message an old client most needs is the one announcing that it is
> old. A ninth member would be undeliverable to exactly its intended audience.

**The mechanism that works instead, with no new wire surface:**

1. **`X-Arronix-Build: <buildId>` on every API response**, and `X-Arronix-Min-Client-Build` alongside it.
   Headers are not contracts; they cost nothing in `Arronix.Abstractions` and belong to `Arronix.Api`
   (spec WP-11, `Endpoints/` + one middleware). The client sends `X-Arronix-Client-Build` on every request
   via the `DelegatingHandler` PWA §1.5 already specifies.
2. **`HubConnection.Reconnected`** — a host that restarted *always* fires it. The client re-probes
   `/ping`, reads the build header on the first API call after reconnect, and enters `UpdateReady` or
   `Incompatible` from PWA §1.5.1 unchanged. A server upgrade with no restart cannot happen.
3. **`registration.update()`** on that mismatch, on launch, and on throttled `visibilitychange` — exactly
   PWA §2.5, minus the hub message.

**One honest correction to the specification this forces:** the client's `switch` over `EventKind` needs a
`_ => Ignore` arm for forward compatibility, which slightly weakens §6.7's blanket `CS8509` claim.
The claim holds for `FieldValueKind`, `BrowseAxisKind`, `StateTone`, `Consequence` and `Affordance` — the
*descriptor* enums, which arrive over the same versioned `MediaKindDescriptor` the client fetched. It does
not hold for `EventKind`, which arrives on a live socket from a process the client did not negotiate a
version with. **Recommend `unified-host-runtime.md` §6.7 be amended to scope the claim to the descriptor
enums.** This is a two-line change, and the alternative is a runtime exception on a hub message.

### 3.3 Wire DTO locations

The placement rule is settled (§6.4:2463): *a type lives in `Arronix.Abstractions` **iff** it crosses an
assembly-isolation boundary — the plugin boundary or the client boundary.* Applied to every type the PWA
document introduces:

| PWA type | Verdict | Home |
|---|---|---|
| `RemoteData<T>` (PWA §1.5.2) | **Client-only.** Never serialized, never crosses a boundary. | `src/Arronix.Client/Data/RemoteData.cs` — as written. |
| `NotificationEnvelope`, `NotificationSubject` (PWA §4.2) | **Superseded.** | `NotificationMessage` + `RenderedSummary`, §5.5:2111 / §6.3:2418. |
| `NotificationEventKind` (PWA §4.2) | **Superseded.** | `NotificationEvent`, §5.5:2102. |
| `INotificationProvider`, `IMediaNotificationRenderer` | **Superseded / deferred.** | [§4.1](#41-c2--the-contract-collision-item-by-item), [§4.2](#42-c1--the-missing-renderer-the-headline-finding). |
| The push payload JSON (PWA §4.13) | **Host-only, Tier C.** It crosses a boundary the specification does not model — host → service worker, JS on the other side — so a C# contract buys nothing. | `src/Arronix.Host/Notifications/WebPush/PushPayload.cs`. |
| The subscription row (PWA §4.7) | **Host-only, Tier C**, never promoted — as PWA WP-P07's tier note already says. | `src/Arronix.Host/Notifications/WebPush/`. |
| **The device-list view (new)** | **Wire.** PWA §4.10's device-management UI needs one, and `Arronix.Client` may reference nothing but Abstractions, so a mirrored type would be a guaranteed-divergence bug. | **New:** `src/Arronix.Abstractions/Wire/PushDeviceView.cs` (`ARX0017`). Takes the specification's stated "~12 wire types" (resolution #32) to 13. |

The one new wire type, written so the endpoint-is-a-secret rule (PWA §4.11, §4.12) is **structural** rather
than a convention the UI is trusted to follow:

```csharp
namespace Arronix.Abstractions.Wire;

/// <remarks>
/// The push endpoint is a capability-bearing URL: anyone holding it plus the subscription keys can push to
/// that device until it is revoked. It is therefore ABSENT from this type rather than redacted in it —
/// the UI cannot leak a field that never reaches it. <see cref="PushServiceHost"/> and
/// <see cref="EndpointSuffix"/> are what PWA §4.10 renders ("fcm.googleapis.com · …Xk29Ab").
/// </remarks>
public sealed record PushDeviceView
{
    public required Guid DeviceId { get; init; }
    public required string Label { get; init; }              // user-editable; defaults to a UA summary
    public required string PushServiceHost { get; init; }
    public required string EndpointSuffix { get; init; }      // last 6 characters, never more
    public required bool Enabled { get; init; }
    public required bool IsCurrentDevice { get; init; }
    public required IReadOnlyList<NotificationEvent> SubscribedEvents { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public int ConsecutiveFailures { get; init; }
    /// <summary>Populated when the device is stale; rendered as PWA §4.10's first-class stale row.</summary>
    public string? StaleReason { get; init; }
}
```

Note `SubscribedEvents` is `IReadOnlyList<NotificationEvent>`, not a mask — resolution A5. The `long`
bitmask is a storage detail inside `Arronix.Host` and never reaches a contract.

### 3.4 `descriptorSetHash` → ETag + `PluginStateChanged`

PWA §2.4's two-tier invalidation model is correct and survives. Its *runtime* tier re-binds onto machinery
the specification already has, and loses its third cache along the way.

| Tier | PWA §2.4 mechanism | Settled mechanism |
|---|---|---|
| **Build** | new `assetsManifest.version` → new worker → install → prompt → reload | **Unchanged**, and verified working — see [§5](#5-build-facts-re-verified). |
| **Runtime** | `descriptorSetHash` changes → `PluginSetChanged` hub message → refetch `/api/v1/ui/descriptors` → `postMessage('RESYNC_ASSETS')` → worker re-syncs `arronix-runtime` | `EventEnvelope { Kind = PluginStateChanged, PluginId }` on `/hub/events` → `DescriptorCache` refetches `GET /api/v1/kinds` with `If-None-Match` → `304` or a new `MediaKindDescriptor[]` → re-render. **No `postMessage`, no `RESYNC_ASSETS`, no `arronix-runtime` cache.** |

Why the third cache dies: it existed to hold `/app/asset-manifest.json` and plugin logos, neither of which
exists ([§2.2(b)](#22-where-it-is-a-real-conflict-not-a-rename)). And the trigger is thinner than the PWA
document assumed — **plugin unload is explicitly not implemented in this milestone** (§8:2947: *"unload is
neither implemented nor tested in this milestone, and that is recorded rather than implied"*). So in
v0.3.0 the runtime tier fires on **quarantine** (`PluginState.Quarantined`, §4.8:1756) and on host
restart, not on live load/unload. PWA WP-P13's done-when — *"loading and unloading a plugin changes the nav
with no reload"* — is **not testable this milestone** and must be restated as: *"quarantining a plugin
removes its kind from the nav with no reload and no new service worker."*

`DescriptorCache` caching `MediaKindDescriptor` "in the service worker keyed by ETag" (§6.7:2756) is worth
one caution: that is an `/api/**` URL, which PWA resolutions #4 and #5 forbid the worker from touching, and
this addendum keeps that prohibition. **Cache the descriptors in `Arronix.Client` (in memory, plus
`localStorage` if a cold-start optimization is wanted later), not in the Cache API**, and let the ETag ride
on the ordinary `HttpClient` request. Cached JSON in a service worker is invisible to C# and untestable
from C# — the original reason — and nothing about descriptors changes it. **Recommend amending §6.7:2756**
accordingly.

### 3.5 Media cover URLs and the artwork proxy (open question 2)

**Question:** *are media cover URLs content-addressed?* **Answer: there are no media cover URLs.**
§6.6 has no artwork route. `ArtworkRef(string Role, Uri Url, int? Width, int? Height)` (§6.3:2431) is
produced by metadata sources (`MetadataSearchHit`, §5.6:2179-2181) and carried on `RenderedSummary`, and
nothing constrains its origin. A TMDb or fanart.tv absolute URL is the expected value.

**Why that cannot ship as-is** — three independent reasons, of which the first is the serious one:

1. **Privacy.** The browser fetching `https://image.tmdb.org/t/p/w500/xyz.jpg` for every poster tells a
   third party, from the user's residential IP and with the user's `Referer`, exactly what is in a private
   media library. That is a regression against the threat model the whole self-hosted product assumes, and
   §4.12/§6.8's careful reasoning about what leaks to a push service is undercut by leaking far more to a
   metadata CDN on every scroll.
2. **Content-security policy.** It forces `img-src` open to an unbounded set of third-party origins, which
   in turn weakens the CSP that PWA §1.3 relies on.
3. **Cacheability.** PWA §2.3's cache-first + LRU strategy — *"the one place caching genuinely pays"* —
   needs a URL that changes when the artwork changes. A third-party URL may or may not oblige, and the
   worker cannot tell which.

**Proposal — a host artwork proxy. [Depends on a specification change; escalated, not assumed.]**

```text
GET /artwork/{hash}      → 200 image/*,  Cache-Control: public, max-age=31536000, immutable
                         → 404 when unknown; never a redirect to the upstream origin
```

- `Arronix.Host/Media/ArtworkProxy.cs` (Tier B). At projection time — the same place `MediaKindDescriptor`,
  `ItemDetail` and `NotificationMessage` are built — every `ArtworkRef.Url` is rewritten to
  `/artwork/{hash}` where `hash = base64url(SHA-256(originalUri.AbsoluteUri))[..16]`, and the
  hash→upstream mapping is held beside it.
- Fetching goes through `IHttpGateway` (`src/Arronix.Abstractions/Http/IHttpGateway.cs`), so proxy
  configuration, TLS policy, rate limiting and redaction apply once, uniformly — the same argument PWA §4.5
  makes for the push sender.
- Content-type allowlist `image/png|jpeg|webp|avif`, `X-Content-Type-Options: nosniff`,
  `Content-Disposition: inline`, a size cap, and **no** SVG (the format that carries script).
- `Arronix.Api` needs no package for any of this.

**Is it content-addressed?** It is *URL*-addressed, which is content-addressed exactly insofar as the
upstream URL is. TMDb and fanart.tv embed a content hash in the path, so in practice yes. **State the
caveat rather than hide it:** a metadata source that serves mutable bytes at a stable URL degrades this
class to stale-while-revalidate, which is what PWA §2.3's own fallback clause already anticipated. That is
the honest answer to open question 2 and it needs no new upstream cooperation.

**Two secondary recommendations to `unified-host-runtime.md`:**

- `ArtworkRef.Url` should be typed `string` (as `RenderedSummary.DeepLink` is) or the projection must emit
  a relative `Uri`; today the record's typing implies an absolute URI while the design needs a
  host-relative one, and `DeepLink`/`Url` disagree with each other for no stated reason.
- The rewrite must be **unconditional at the projection boundary**, not a client-side courtesy. If a raw
  `ArtworkRef.Url` can reach the browser at all, one un-rewritten path re-opens all three problems.

### 3.6 Multi-user and auth mode — **still open** (questions 3 and 4)

**The specification is silent.** Verified by exhaustive search of all 2,981 lines: no `auth`, no `Auth` in
a security sense, no `cookie`, no `Cookie` outside `IProviderSessionStore`'s doc comment, no `UserId`, no
`user`, no session, no bearer, no API key. §6.6's route table carries **no authorization annotation on any
endpoint**. The deferred list (§8) does not mention authentication, so it is not deliberately deferred —
it is unaddressed. The word does not appear in the 50-row resolution table.

**This cannot be closed here and is escalated.** It is not a PWA question; it is a host question with three
PWA consequences:

| Consequence | If cookie sessions | If bearer tokens / API key |
|---|---|---|
| `<link rel="manifest" crossorigin="use-credentials">` (PWA §3.1) | **Required** — without it the manifest fetch is anonymous, gets a 401, and the browser silently declines to offer installation. The symptom gives no clue. | Not required; but `manifest.webmanifest`, `service-worker.js` and the icon set must then be anonymously readable anyway. |
| "logout deletes this browser's subscription" (PWA §4.7 step 7) | Well-defined — an explicit logout is a shared-device signal. | Ill-defined; a token has no logout. The rule becomes "revoke from the device list", which is weaker. |
| The subscription `UserId` column (PWA §4.7) | The owning account. | A constant. |

**Assumptions this addendum proceeds under, stated so they are falsifiable:**

- **Single account** (the *arr model). The `UserId` column ships regardless, because adding a column to a
  table that does not exist yet is free and adding it to a populated one is not — PWA open question 3 said
  the same and it is still right.
- **Cookie session, `SameSite=Strict`.** Therefore `crossorigin="use-credentials"` on the manifest link,
  **and** anonymous `GET` on `manifest.webmanifest`, `service-worker.js`, `service-worker-assets.js`,
  `/ping` and the icon set — belt and braces, because those five contain no secrets and anonymous access
  removes an entire class of silent install failure.
- **`/artwork/{hash}` is authenticated.** It reveals library contents. This costs one thing worth naming:
  the service worker's `push` handler shows a notification whose `image` the OS fetches — on Chromium the
  fetch is credentialed from the SW's cookie jar, which works; on a device where it is not, the
  notification degrades to no image rather than failing. Acceptable, and it must be an acceptance test.

**Until auth is specified, WP-P09 and everything downstream of it stay blocked** — see
[§6](#6-work-package-status).

### 3.7 Does the descriptor set include per-kind layout, or only fields? (open question 5)

**Closed: neither, and the third answer is better than both.** §6.3's falsification test —
*"does a CLI, a TUI, a mobile app and a voice assistant each have an obvious, non-arbitrary way to honor
this declaration?"* — rules out layout categorically (`ColumnWidth`, `Order`, `Component = "SeasonGrid"`
are all in the rejected column, §6.2:2310-2318). What a plugin declares is:

- **fields** with a `Prominence` rank (resolution #33),
- **traversal** via `BrowseAxis` + `BrowseAxisKind` — five kinds, mapping to five presenter components in
  `Arronix.Client/Browse/Presenters/` (§6.7:2731),
- **state semantics** via `StateDescriptor` + `StateTone`,
- **actions** with `Consequence` + `ConfirmationRequirement`,
- and, for the three screens a declarative vocabulary genuinely cannot express, a **`WorkbenchDescriptor`**
  (§6.5) — a declared editable row/column grid over a plugin-supplied proposal.

**The answer to the question behind the question** — *how much of the plugin UI story lands in the PWA
delivery?* — is: **none of it.** `Arronix.Client`'s component tree, the five lookup tables and the
workbench are spec WP-12. The PWA delivery owns `wwwroot/*` and the connectivity/PWA/push services, which
§6.7:2727 already assigns to this document by name. That boundary is clean and needs no negotiation.

---

## 4. Web Push as a notification provider — every mismatch

PWA resolution #11 — *"Web Push is a provider, not a subsystem"* — is **confirmed and strengthened**.
§5.5:2150-2155 says it independently and goes further: *"Web Push is an ordinary member of this family with
no special handling … `RenderedSummary` is precisely the payload a push notification needs (title /
subtitle / artwork / deep link), so **no push-specific contract exists**."*

That agreement makes the disagreements below unusually cheap to fix: they are all in the contracts PWA §4.2
proposed, and all of those contracts are now deleted.

### 4.1 C2 — the contract collision, item by item

**PWA WP-P07 must not be executed as written.** Every file it creates collides with spec WP-3, and its
first edit collides with spec WP-1.

| PWA §4.2 proposes | Settled | Verdict |
|---|---|---|
| `src/Arronix.Abstractions/Notifications/` | `src/Arronix.Abstractions/Providers/` | **Delete the namespace.** WP-3 owns those files (§7:2800). |
| `ExperimentalContracts.Notifications = "ARX0013"` | `Shape = "ARX0013"`; providers are `ARX0015` (§7.2:2843-2847) | **Hard collision.** Executing it breaks `ExperimentalSurfaceTests`, `ContractAreaTests` and `stability.md` at once. Notification types take `ARX0015`. |
| `interface INotificationProvider` with `ProviderId`, `Name`, `SupportedEvents`, `NotifyAsync`, `TestAsync` | `interface INotifier : IProvider` (§5.5:2095-2100); `Id`, `Family`, `TestAsync`, `GetOptionsAsync` come from `IProvider` (§5.1:1885-1893); `Name`/`Description`/`Settings` come from `ProviderDescriptor` | **Superseded.** The PWA interface conflates identity, descriptor and behavior; the spine separates them. |
| `Task NotifyAsync(NotificationEnvelope, CancellationToken)` | `Task NotifyAsync(ProviderInvocation, NotificationMessage, CancellationToken)` | **Superseded, and this is the substantive one.** Without `ProviderInvocation` the provider must hold its `Definition`, which is the stateful shape resolution #28 exists to make unrepresentable (§5.1:1869-1874). |
| `[Flags] enum NotificationEventKind` (13 members) | `enum NotificationEvent` (15 members, not flags) | **Superseded.** See [§4.3](#43-the-event-vocabulary-and-the-revised-push-default-table). |
| `NotificationEventKind SupportedEvents { get; }` | `IReadOnlyList<NotificationEvent> SupportedEvents { get; }` | **Superseded.** Both replace ~50 reflected `SupportsOnXxx` booleans with declared data, which was PWA §4.1's rejection #3 — **agreed independently**, differently encoded. |
| `record NotificationEnvelope { Kind, OccurredAt, Subject, CorrelationId, Facts, Health }` | `record NotificationMessage { Event, OccurredAt, CorrelationId, Summary, MediaKind, Item, Files, ReplacedFiles, Quality, Download, Health, Context }` (§5.5:2111-2132) | **Superseded, and strictly richer.** PWA's `Facts` → `Context` (namespaced, plugin-owned, opaque, keyed by shape field ids); plus `Files`, `ReplacedFiles`, `Quality`, `DownloadAttribution` which PWA had no slot for. `Health` is identical. |
| `record NotificationSubject { Title, MediaKind, ItemId, Subtitle, ImagePath, DeepLinkPath }` | `RenderedSummary { Title, Subtitle, Body, Fields[], Artwork[], DeepLink }` (§6.3:2418-2431) | **Superseded, and this is the good news** — see [§4.4](#44-rendersummary--push-payload-the-projection). |
| `Task<IReadOnlyList<HealthCheck>> TestAsync(...)` | `Task<ValidationOutcome> TestAsync(ProviderInvocation, CancellationToken)` | **Superseded** — see [§4.8](#48-testasync-what-was-actually-lost). |
| `interface IMediaNotificationRenderer` | *(nothing)* | **Spec gap** — see [§4.2](#42-c1--the-missing-renderer-the-headline-finding). |
| FluentValidation avoided; `HealthCheck` reused | FluentValidation removed; first-party `ValidationOutcome` (resolution #30) | **Agreed independently.** Both documents reach "Abstractions keeps zero package references" from the same premise. |

**Net effect on the work plan:** PWA WP-P07 ("no dependency, start immediately") is **canceled** and its
content absorbed into spec WP-3. This is the single largest scheduling change in this addendum, and it goes
the wrong way: a package the PWA document listed as immediately startable now must **not** start, because
it would write files another work package owns.

### 4.2 C1 — the missing renderer (the headline finding)

**The specification asserts a behavior it provides no seam for.** §5.5:2143 states rule 1 as:

> *"**The originating plugin renders `Summary`.** The host owns the product name (from
> `HostIdentityOptions`), so the `_BRANDED` constants … disappear entirely."*

And §5.5:2116-2118 annotates `NotificationMessage.Summary`:

> *"Rendered **BY THE ORIGINATING MEDIA PLUGIN**, carried by the host, consumed by the notifier … it is also
> the type the client's activity feed renders, so there is exactly one plugin-side rendering path, not two."*

**There is no such path.** Search the whole of `Arronix.Abstractions` as specified — §2.11's four behavior
seams, §6.5's two additions to `IMediaItemSource`, §5's provider interfaces — and no member anywhere
returns a `RenderedSummary`. `IMediaItemSource` returns `ItemView` (§2.11:852-865), which has `Title`,
`Fields`, `Coordinates`, `ExternalIds` and no summary. A plugin *cannot* render `Summary` with the contracts
as written, so `NotificationMessage.Summary` is `required` and unfillable, and WP-10's
`NotificationDispatcher` has nothing to call.

This is precisely the hole `IMediaNotificationRenderer` (PWA §4.3) was designed to fill, and the PWA
document's diagnosis of *why* it is needed is exact and unchallenged: **8 of Radarr's 11 notification events
carry a `Movie` in the signature**; Radarr does not have this problem because Radarr has one media kind.

**Two ways to close it. Recommend the first.**

**Option 1 — host derivation from declared data (recommended; no contract change).** Everything a summary
needs is already declared:

| `RenderedSummary` slot | Derived from |
|---|---|
| `Title` | `ItemView.Title` — or, for a child unit, the ancestor's title via `ItemView.Parent` |
| `Subtitle` | the item's `CoordinateSet` rendered through the level's canonical `CoordinateSpace` (§2.3) — this is exactly what "S05E03" *is* — plus `SequenceException` labels ("Specials") |
| `Body` | absent, or the `NotificationEvent`'s own sentence |
| `Fields[]` | `ItemView.Fields` filtered to `FieldDescriptor.Prominence == Primary`, weight-mapped to `SummaryFieldWeight` (resolution #33 built this rank for exactly this reuse) |
| `Artwork[]` | fields whose `FieldValueKind` is `Artwork` (the kind `IntentSurfaceValidator` already knows about, §6.6:2719), rewritten through the artwork proxy |
| `DeepLink` | host-owned route template `/{kind}/items/{id}` — the host owns its own routes |

This lands in `Arronix.Host/Providers/NotificationDispatcher.cs` (spec WP-10) as `SummaryProjector`, needs
no new contract, no new capability and no new registration, and it satisfies resolution #35's rule
verbatim: *a declaration that can be derived is a declaration that can disagree.* It also gives the host
fallback PWA §4.3 already required (*"a missing renderer degrades the copy; it never suppresses the
notification"*) **as the primary path rather than the fallback**, which is a better place for it.

The honest cost, stated: the host produces *"The Expanse — season 5, episode 3"* where a plugin would
produce *"The Expanse — S05E03 · Mother"*. That is a copy quality difference, not a capability difference,
and it affects the notification body only.

**Option 2 — the seam, if copy quality is judged to matter.** It must live in `Providers` (it takes a
`NotificationEvent`, which lives there; `Providers` already depends on `Shape` for `RenderedSummary`,
`MediaKindId`, `MediaItemRef` and `QualityTier`, and the reverse dependency would be new):

```csharp
// src/Arronix.Abstractions/Providers/IMediaSummaryRenderer.cs        [ARX0015, one file added to WP-3]
/// <remarks>
/// The plugin owns the answer to "how is one of my items described in a sentence"; the host owns
/// everything else about the notification. Registered through the media-kind capability the plugin
/// already holds — a kind's owner is the only party that could implement it.
/// </remarks>
public interface IMediaSummaryRenderer
{
    MediaKindId MediaKind { get; }

    ValueTask<RenderedSummary> RenderAsync(
        NotificationEvent occurrence,
        MediaItemRef item,
        IReadOnlyDictionary<string, string> context,
        CancellationToken cancellationToken = default);
}
```

with `IPluginRegistry AddSummaryRenderer(IMediaSummaryRenderer renderer);` gated on the **existing**
`Capability.MediaKind` (§3.2:963) — no new capability, no new gate, one row in `CapabilityMatrix`. The
precedent for adding a method-shaped seam to an already-registered surface is §6.5:2656-2658, which does
exactly this to `IMediaItemSource`.

**Recommendation: ship Option 1 now; defer Option 2 with the trigger "a media plugin whose derived summary
is demonstrably wrong, not merely plainer".** That is the three-tier rule applied honestly — four plugins
ship this milestone and none of them *needs* the seam — and it means the notification pipeline is not
blocked on a contract decision.

**This decision is owned by `unified-host-runtime.md`, not by this addendum.** The gap is in that
document; it is recorded here because the PWA delivery cannot proceed past WP-P10 without it being closed
one way or the other.

### 4.3 The event vocabulary and the revised push default table

`NotificationEvent` (§5.5:2102-2109) has 15 members. PWA's `NotificationEventKind` had 13. They are not the
same 13, and **PWA §4.4's push-default table names two events that do not exist**.

| PWA §4.4 row | Settled member | Note |
|---|---|---|
| `ManualInteractionRequired` | `ManualInteractionRequired` | ✓ |
| `ImportFailed` | `ImportFailed` | ✓ |
| **`DownloadFailed`** | — | **Missing.** Nearest is `GrabFailed`, which is not the same event: the *arrs distinguish "sending the release to the client failed" from "the client reported the download failed". Flagged for the spec owner; see below. |
| `HealthDegraded` / `HealthRestored` | `HealthDegraded` / `HealthRestored` | ✓ |
| `Imported` / `Upgraded` | `Imported` / `Upgraded` | ✓ |
| **`UpdateAvailable`** | — | **Missing.** Only `ApplicationUpdated` exists (the *installed* half). |
| `UpdateInstalled` | `ApplicationUpdated` | Renamed. |
| `Grabbed` | `Grabbed` | ✓ |
| `ItemAdded` / `ItemDeleted` | `ItemAdded` / `ItemDeleted` | ✓ |
| `FilesRenamed` | `Renamed` | Renamed. |
| — | `Retagged`, `FileDeleted`, `GrabFailed`, `AcquisitionComplete` | New to the PWA design. `AcquisitionComplete` (Sonarr's `OnImportComplete`, generalized) is the season-pack completion event and is **the better coalescing anchor** than PWA §4.4's 5-second window — see below. |

**Two requests to `unified-host-runtime.md`, with evidence, for the spec owner to accept or reject:**

1. **Add `DownloadFailed`.** All four surveyed *arrs raise both `OnGrabFailure`-shaped and
   `OnDownloadFailure`-shaped notifications, and PWA §4.4 defaults it ON because it is *"actionable and
   silent otherwise — the failure mode people discover a week later."* Adding an enum member is additive.
2. **Add `UpdateAvailable`**, or accept that PWA §4.4's `UpdateAvailable` row is deleted. `ApplicationUpdated`
   alone cannot express "there is an update you have not taken", which for a self-hosted app that does not
   auto-update is the more useful of the two.

**The revised default set** — this replaces PWA §4.4's table:

| `NotificationEvent` | Push default | Urgency | TTL | Collapse `Topic` / client `tag` |
|---|---|---|---|---|
| `ManualInteractionRequired` | **ON** | `high` | 86400 | per item |
| `ImportFailed` | **ON** | `high` | 86400 | per item |
| `GrabFailed` | **ON** | `normal` | 3600 | per item |
| `DownloadFailed` *(if added)* | **ON** | `normal` | 3600 | per download id |
| `HealthDegraded` | **ON** for `HealthSeverity` ≥ Error; Warning opt-in | `normal` | 86400 | per `CheckId` |
| `Imported` / `Upgraded` | **ON** | `normal` | 3600 | per parent item |
| `AcquisitionComplete` | **ON** | `normal` | 3600 | per acquisition |
| `ApplicationUpdated` | **ON** | `low` | 86400 | single |
| `UpdateAvailable` *(if added)* | **ON** | `low` | 86400 | single |
| `Grabbed` | **OFF** (opt-in) | `low` | 3600 | per parent item |
| `HealthRestored` | **OFF** (opt-in; **replaces** the earlier notification via the same tag) | `low` | 86400 | per `CheckId` |
| `ItemAdded`, `ItemDeleted`, `FileDeleted`, `Renamed`, `Retagged` | **OFF**, not offered | — | — | — |

The governing rule is unchanged and still right: *push what the user must act on, or would want to know
without opening the app; anything the user just did is not push-worthy.*

**One improvement the specification enables.** PWA §4.4 coalesces with a 5-second dispatcher window because
*"a season pack import is 10 events in 90 seconds"*. `AcquisitionComplete` is that event, declared. **Keep
the window** (it is transport-independent and Discord benefits too) but prefer `AcquisitionComplete` as the
notification of record when a plugin raises it, and suppress the per-unit `Imported` pushes it summarizes.
Coalescing stays a host concern in `NotificationDispatcher` (spec WP-10), never a transport concern.

### 4.4 `RenderedSummary` → push payload: the projection

**The brief's question — can the specification's notification payload carry what a push needs? — answers
yes, precisely.** The mapping is 1:1 and needs no new contract:

| Push needs | `RenderedSummary` slot |
|---|---|
| title | `Title` |
| subtitle | `Subtitle` |
| artwork | `Artwork[]` → the `Role == "poster"` entry, else the first |
| deep link | `DeepLink` (relative, app-internal, "never an absolute origin" — §6.3:2425) |

The only mismatch is **size**: `Fields[]` and `Artwork[]` are unbounded, and Web Push gives roughly 3 KB of
plaintext after the RFC 8188 header, the AEAD tag and the padding delimiter. So the sender **projects**;
it never serializes `RenderedSummary` onto the wire.

```csharp
// src/Arronix.Host/Notifications/WebPush/PushPayload.cs        [Tier C, host-only, never promoted]
internal sealed record PushPayload(
    int V, string Kind, string Title, string Body, string Tag,
    string? Url, string? Image, long Ts, string Urgency);
```

The projection, stated as rules so it is testable:

- `Title` ← `Summary.Title`, truncated to 80 UTF-16 units at a grapheme boundary.
- `Body` ← `Summary.Subtitle`, then up to **two** `Summary.Fields` where `Weight == Primary`, joined
  ` · `, truncated to 160.
- `Image` ← the poster `ArtworkRef.Url` **after** artwork-proxy rewriting ([§3.5](#35-media-cover-urls-and-the-artwork-proxy-open-question-2)).
  Never bytes — the worker fetches the URL and usually finds it already in `arronix-images`.
- `Url` ← `Summary.DeepLink`. Rejected client-side if it is not same-origin and path-only.
- `Tag` ← `{event}:{mediaKind}:{itemRef}`; `Topic` ← the **first 16 base64url characters of
  SHA-256(tag)**. PWA §4.12 point 7 is right that `imp-tv-812` is metadata handed to Google; an opaque
  topic collapses identically and reveals nothing.
- `Ts` ← `OccurredAt.ToUnixTimeMilliseconds()`.
- **Hard assertion:** the encrypted record is ≤ 3,072 bytes, checked in a unit test with a pathological
  `RenderedSummary` (40 fields, 8 artwork refs, a 4,000-character title). If it exceeds, drop fields, then
  the image, then truncate the body — never fail the notification.

`Summary.Body`, `Summary.Fields[2..]` and every non-poster `ArtworkRef` are **deliberately dropped**. They
exist for Discord embeds and the client's activity feed, which have no size ceiling. That is the payoff of
resolution #25: one rendering, three consumers, each projecting what it can carry.

### 4.5 The per-device event mask without a `[Flags]` enum

PWA §4.7's `EventMask` column was typed as `NotificationEventKind` — a flags enum, one `int`. With a
non-flags `NotificationEvent` this needs restating, and the restatement is free because the subscription
table is Tier C host-owned (PWA resolution #12, unchanged and correct):

- **Storage:** a `long` bitmask over `1L << (int)NotificationEvent`. 15 members today; a `long` gives 49
  members of headroom, and the enum is closed so growth is a deliberate act.
- **Wire:** `IReadOnlyList<NotificationEvent>` on `PushDeviceView` ([§3.3](#33-wire-dto-locations)). Never
  the mask — the numeric value of an enum member is not a contract.
- **Semantics unchanged:** the provider-level `ProviderDefinition` remains the outer gate (its
  `INotifier.SupportedEvents` and its per-definition enabled-event set), and the per-device mask is an
  inner filter. PWA §4.7's justification stands verbatim: *"notify my phone about failures but my desktop
  about everything" is the actual requirement, and modeling it as N provider connections each with one
  subscription would be absurd.*
- **One migration hazard removed:** the mask is never persisted as a flags *value* that would shift meaning
  if the enum were reordered. Store the member **names**, or store the mask and pin the enum's numeric
  values — the specification already assigns explicit values (`Grabbed = 0 … ApplicationUpdated = 14`), so
  the mask is safe as long as that stays true. Record it as a constraint on `NotificationEvent`: **its
  numeric values are load-bearing and may never be reordered.**

### 4.6 The reserved provider id and the host plugin id

PWA resolution #13 — *"`web-push` is a reserved, host-registered provider id; the plugin loader rejects any
plugin declaring it with a dedicated `CoreErrorCode`"* — is **obsolete, because the collision it prevents
cannot occur.** §5.1:1807-1812:

```csharp
public readonly record struct ProviderId
{
    public string Value { get; }          // "{pluginId}:{localId}"
    public static ProviderId Create(PluginId plugin, string local);
}
```

*"The id is minted by the registry, never by the plugin."* A plugin declaring `LocalId = "web-push"` gets
`ProviderId "tv:web-push"`, which is a different provider with no access to anything. **PWA WP-P10's
done-when — "a plugin declaring `web-push` is rejected at load with a specific error" — is void and must be
deleted.** The security property PWA §4.12 wanted is delivered structurally instead, which is better.

**But a question opens that neither document asked: what `PluginId` does a host-owned provider carry?**
Every `ProviderId` is plugin-qualified, and Web Push comes from no plugin. Three options, one
recommendation:

| Option | Verdict |
|---|---|
| Make Web Push a built-in plugin | **No.** It would need `Capability.Notification` and a manifest, and a "plugin" that ships inside the host is a fiction the loader would have to maintain. |
| Allow an unqualified `ProviderId` | **No.** It breaks `TryParse`'s single format and the "always plugin-qualified" invariant that makes cross-plugin collision impossible. |
| **A reserved `PluginId` for the host** | **Yes.** `PluginId.Host` with value `"arronix"`, rejected by `PluginManifestValidator` for any loaded plugin, giving `ProviderId "arronix:web-push"`. One reserved word, one validator rule, one test. |

**Escalated to `unified-host-runtime.md`:** §5.1 does not say whether a host-registered provider is
possible, and `IPluginRegistry.AddNotifier` (§3.4:1094) is the plugin-facing surface. WP-10's
`ProviderRegistry` needs an internal registration path for host-owned providers, or Web Push has nowhere to
live. This is small, but it is currently unspecified.

The rest of PWA §4.12 survives unchanged and, in two places, is strengthened:

- **Point 2** (subscription endpoints never cross the plugin boundary) — now *structural*: endpoints appear
  in no type in `Arronix.Abstractions`, and [§3.3](#33-wire-dto-locations)'s `PushDeviceView` omits the
  endpoint entirely rather than redacting it.
- **Point 3** (no `SendPush` API at any level) — unchanged. A plugin's entire relationship with push is
  raising a domain event; the host decides whether it becomes a push.
- **Point 4** (the VAPID key is under `AppDataFolder`, unreachable via `IPluginPaths`) — **replaced** by
  [§4.7](#47-c4--vapid-custody-reversing-pwa-resolution-15). The new mechanism is at least as strong:
  `ProviderDefinition.Settings` is host-owned storage, never handed to a plugin, and a `Secret` field never
  leaves the host outward at all.
- **Points 5–8** (endpoints as secrets, CSRF on subscribe, RFC 8291 end-to-end encryption, not encrypted at
  rest) — unchanged.

### 4.7 C4 — VAPID custody: reversing PWA resolution #15

**PWA resolution #15 is reversed. The specification wins.**

| | PWA resolution #15 | §5.5:2151 |
|---|---|---|
| Where | PKCS#8 file under `IAppPaths.AppDataFolder`, `0600` via `IFilePermissions` | `ProviderDefinition.Settings`, fields marked `SettingSensitivity.Secret` |
| Redaction | a hand-written `IRedactionRuleProvider` rule | **automatic** — one `SettingSensitivity` declaration produces the serialization rule, the `RedactionRule` and the UI intent (§5.2:1929-1936) |
| Never returned outward | by convention | **mechanical** — `Secret` fields round-trip as `"********"` and a write carrying the sentinel means "unchanged" (§6.4:2578-2581) |
| Backup | a new file that must be added to the backup set and documented | the host database, already in the backup set |
| Dependency | **`IAppPaths`, which does not exist** — planned Tier B in `Arronix.Common` (extraction plan `:279`, `:968`) and created by no work package in §7 | none new |

Three reasons, in order:

1. **It depends on a type nobody is building.** `src/Arronix.Common/` has no `Hosting/` directory today and
   §7's twenty work packages do not add one. PWA §4.6 as written cannot be implemented this milestone or
   next.
2. **The specification's mechanism is strictly more automatic.** Redaction, non-emission and UI intent all
   fall out of one enum value that has to be declared anyway. A key file gets none of them free.
3. **`0600` was the only thing the file bought**, and the database file needs the same protection from the
   storage layer regardless. Protecting one secret twice while its neighbors — indexer API keys,
   download-client passwords — sit beside it unprotected is the same argument PWA resolution #16 already
   makes for *not* encrypting the subscription rows. The PWA document should apply its own reasoning here.

**What survives from PWA §4.6, unchanged and still required:**

- Generated once on first start if absent; `ECDsa.Create(ECCurve.NamedCurves.nistP256)`,
  `ExportPkcs8PrivateKey()`, stored base64 in the `Secret` field.
- **Never** in `appsettings.json`, never in an environment variable, never in the support bundle.
- The **public** key is a `SettingSensitivity.Public` field, so `GET /api/v1/providers/definitions/{id}`
  already serves it — base64url of the raw 65-byte uncompressed P-256 point, the form `applicationServerKey`
  wants. `GET /api/v1/push/vapid` is deleted.
- **No automatic rotation, ever.** Rotation silently invalidates every subscription. A deliberate rotation
  marks every subscription `RequiresResubscribe` and is surfaced loudly.
- **Backup-critical**, and now free: restoring the database restores the key with it. The failure mode PWA
  §4.6 warned about — a restored database whose subscriptions are all undeliverable because the key file
  was not in the backup — **cannot happen** under the settings-field model. That is the strongest argument
  of the three and it is worth stating in the restore documentation as a non-problem.
- A `WebPushHealthContributor` check for "the definition exists but the key field is empty or unparseable",
  with a `RemediationHint`.

### 4.8 `TestAsync`: what was actually lost

The specification settles `Task<ValidationOutcome> TestAsync(ProviderInvocation, CancellationToken)` on
`IProvider` (§5.1:1889), one signature for five families. PWA §4.2 argued for
`Task<IReadOnlyList<HealthCheck>>` on the grounds that `HealthCheck` carries `Status`, `Severity`, `Message`
**and `RemediationHint`** — *"strictly more than a FluentValidation `ValidationResult` conveys"*.

That argument was aimed at FluentValidation and lands on the wrong target: `ValidationOutcome` is
first-party (resolution #30), and `ValidationFailure(string? FieldId, string Message, ValidationSeverity,
CoreErrorCode)` (§5.2:1947-1950) carries a **field id** and a **`CoreErrorCode`** that `HealthCheck` does
not. What it lacks is `RemediationHint`.

**Resolution: accept the specification, and split the question rather than the type.**

- **Test** answers *"is this configuration valid and does the endpoint respond?"* → `ValidationOutcome`,
  field-attributed, rendered next to the offending input by `ProviderForm.razor`. `POST
  /api/v1/providers/definitions/{id}/test` already returns exactly this (§6.6:2697).
- **Health** answers *"is this subsystem well?"* → `HealthCheck` with `RemediationHint`, via
  `IHealthContributor`. PWA §4.11's `WebPushHealthContributor` is unchanged and keeps every hint it
  specified: VAPID key missing or unreadable; >50% of subscriptions failing in 24 h; every subscription
  stale; the configured external URL not a secure context.

The residual gap is narrow — a *test* failure whose fix needs a sentence of prose — and the fix costs
nothing: put the remediation in `ValidationFailure.Message`. **Do not add `RemediationHint` to
`ValidationFailure`**; a second remediation vocabulary in a second type is how two vocabularies become
three.

### 4.9 Confirmed compatible — stated so it is not re-checked

- **`Arronix.Api` zero `PackageReference` survives the push API.** Antiforgery, rate limiting, static files
  and SignalR server are all in `Microsoft.AspNetCore.App`. `ApiProjectShapeTests` (§7.5:2899) passes.
- **`Arronix.Client` package set is unchanged.** Push subscription, permission prompting and the service
  worker are `IJSRuntime` interop over hand-written JS. `Microsoft.AspNetCore.SignalR.Client` (§7.3:2869)
  is already registered in `Directory.Packages.props` at 10.0.10.
- **`Arronix.Abstractions` keeps zero package references.** Nothing this addendum adds to it
  (`PushDeviceView`, and `IMediaSummaryRenderer` if Option 2 is ever taken) needs one.
- **The provider status/back-off machinery PWA §4.5 wanted** — the 5-minute floor, five escalation levels,
  the initial-failure grace window, the 15-minute startup grace — is spec WP-10's `ProviderStatusStore`
  (§5.3:1977-1981), reproducing the surveyed `ProviderStatusServiceBase` behavior exactly. PWA §4.5's
  `5xx` row gets it for free and should stop describing it.
- **`IRateLimiter` keyed by push-service host** (PWA §4.5, `429` row) is unchanged; §4.2's
  `PartitionedRateLimiter` is the implementation and `Arronix.Host` already takes
  `System.Threading.RateLimiting` (§7.3:2867).
- **`IHttpGateway`, `ITelemetryEmitter`, `IRedactionRuleProvider`, `IHealthContributor`, `IRateLimiter`,
  `IFilePermissions`** — all six exist in `src/Arronix.Abstractions/` today, verified. Only `IAppPaths` does
  not, and [§4.7](#47-c4--vapid-custody-reversing-pwa-resolution-15) removes the dependency on it.

---

## 5. Build facts, re-verified

PWA §2.1 and §7 open question 6 flag six SDK-version-sensitive facts. All were executed against this repo's
resolved SDK, not recalled. Method: `dotnet new blazorwasm --pwa -f net10.0` + `dotnet publish -c Release`,
plus a `net10.0` console probe over the crypto surface, plus direct inspection of the SDK targets.

**Resolved toolchain.** `global.json` pins `10.0.100-rc.1.25451.107` with `rollForward: latestFeature` and
`allowPrerelease: true`. In the repository root `dotnet --version` resolves to **10.0.301**; runtime
**10.0.9**. *(Outside the repo the machine's default is 11.0.100-preview.5 — an easy way to test the wrong
SDK by accident. Always run from the repo root.)*

| # | Claim in `pwa-and-push.md` | Verdict | Evidence |
|---|---|---|---|
| 1 | `<ServiceWorkerAssetsManifest>` + `<ServiceWorker Include= PublishedContent=>` still work | **CONFIRMED** | The tooling moved to `Sdks/Microsoft.NET.Sdk.StaticWebAssets/targets/Microsoft.NET.Sdk.StaticWebAssets.ServiceWorker.targets` (tasks `GenerateServiceWorkerAssetsManifest`, `UpdateServiceWorkerFileWithVersion`). A `--pwa` publish emits `wwwroot/service-worker-assets.js` with `self.assetsManifest = { version, assets: [{hash, url}] }` — 95 entries. **PWA §2.1 stands unchanged.** |
| 2 | The boot manifest is a discrete `blazor.boot.json` | **FALSIFIED** | **No `blazor.boot.json` exists in a net10.0 publish.** `_framework/` contains `dotnet.<fp>.js`, `dotnet.runtime.<fp>.js`, `dotnet.native.<fp>.js`, `blazor.webassembly.<fp>.js` and nothing else JSON-shaped; the boot config is inlined. Consequence: resolution #8's rejected alternative ("poll `blazor.boot.json`") is not merely worse, it is **impossible**. Nothing in the design depended on it — PWA §2.1's stated insulation ("depends on `service-worker-assets.js` and nothing else") paid off. |
| 3 | Publish asset extensions include `.blat` and `.dll` | **FALSIFIED (partially)** | Actual extension census of `_framework/` (129 files): **36 `.wasm`** (assemblies, Webcil-in-wasm, fingerprinted), **3 `.dat`** (ICU: `icudt_CJK`, `icudt_EFIGS`, `icudt_no_CJK`), **4 `.js`**, 43 `.br`, 43 `.gz`. **No `.blat`, no `.dll`, no `.pdb` in Release.** PWA §2.2's "Framework payload" row should read `.wasm`, `.js`, `.dat`; the extension list in the template's own `offlineAssetsInclude` is stale and must not be copied. |
| 4 | `BlazorCacheBootResources` still exists (PWA §2.7's last row) | **EFFECTIVELY GONE — the mitigation is unnecessary** | The property is defaulted only when *not* targeting net10.0: `<BlazorCacheBootResources Condition="… and '$(_TargetingNET100OrLater)' != 'true'">true</BlazorCacheBootResources>` (`Microsoft.NET.Sdk.BlazorWebAssembly.6_0.targets:57`), and it is *consumed* only in the `5_0` targets (`:318`, `:561`). On net10.0 the framework does not run a rival boot-resource cache, so **the "~2× storage for `_framework`" failure mode does not arise. Delete that row from PWA §2.7.** |
| 5 | The SDK may or may not stamp a version into the published worker | **CONFIRMED — it does** | `UpdateServiceWorkerFileWithVersion` prepends `/* Manifest version: wviCgsIn */` as line 1 of the published worker. Consequence, and it is load-bearing: **the worker file's bytes change on every asset change**, so the browser's byte-comparison update check fires reliably without any help. PWA §2.5's `Cache-Control: no-cache` on `/service-worker.js` and `/service-worker-assets.js` remains required for the check to see fresh bytes. |
| 6 | Web Push crypto is BCL-only on net10.0 (PWA resolution #14) | **CONFIRMED, all six** | Executed on runtime 10.0.9: `ECDiffieHellman.DeriveRawSecretAgreement` ✓; `HKDF.DeriveKey` ✓ (2 overloads); `AesGcm` ✓; `ECDsa.SignData(…, DSASignatureFormat)` ✓ with `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` ✓; `ECDsa.ExportPkcs8PrivateKey` ✓; `ECDiffieHellman.ImportSubjectPublicKeyInfo` ✓; **`System.Buffers.Text.Base64Url` ✓, in `System.Private.CoreLib`** — no package, not even a shared-framework reference. **PWA WP-P08's "zero non-Microsoft package references" is achievable exactly as designed.** |

**Three additional findings the PWA document did not flag:**

7. **The `RuntimeIdentifier` hazard is real and blocking** (§1:83-85, §7.6:2913). `src/Directory.Build.props`
   sets `RuntimeIdentifier = <host RID>` whenever one is not specified, and `RuntimeIdentifiers` (line 12)
   lists nine RIDs, **none of them `browser-wasm`**. `Arronix.Client.csproj` must set
   `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` **and** `<RuntimeIdentifiers></RuntimeIdentifiers>`
   explicitly. This is spec WP-12's problem, but the PWA delivery owns `wwwroot/*` inside that project and
   will hit it first — **verify with `dotnet publish` before writing a line of the service worker**, because
   a wrongly-RID'd publish produces no `service-worker-assets.js` at all and the failure looks like a
   service-worker bug.

8. **SRI integrity applies to identity-encoded bytes, and the manifest excludes the compressed variants.**
   The 95 manifest entries carry `sha256-…` over the **uncompressed** asset; `.br` and `.gz` appear in the
   publish output but **never in the manifest**. The browser decompresses before checking integrity, so
   content negotiation is fine — but this is exactly why PWA resolution #10's proxy hazard is real: a proxy
   that recompresses or minifies changes the decoded bytes and every `cache.addAll` fails. **Keep
   `integrity` and fail loudly** — confirmed as the right call, now with the mechanism understood.

9. **`{ updateViaCache: 'none' }` on registration** is what the .NET 10 template itself ships
   (`index.html:35`) and PWA §2.5 does not mention. It is belt-and-braces against a misconfigured
   `Cache-Control` on `/service-worker.js` and costs one option. **Adopt it**, and keep the server headers
   anyway.

10. **The manifest needs no `{base}` substitution** (resolution A14). The template ships `"id": "./"`,
    `"start_url": "./"` — relative, resolved against the manifest's own URL, correct under any sub-path.
    The specification has no `UrlBase` concept to substitute from (`Arronix:Api:ClientRoot`, §6.1:2288, is a
    filesystem path). **Rewrite PWA §3.1's manifest with relative URLs and delete the `{base}` placeholder
    machinery.** The service worker's `SCOPE`/`BASE` derivation from `self.registration.scope` (PWA §2.6) is
    unaffected and remains the right technique.

---

## 6. Work package status

Fourteen packages from PWA §5. **Two were listed as immediately startable and are not; two genuinely are;
one is canceled outright.**

| WP | Status | Why |
|---|---|---|
| **WP-P01** Brand and icon assets | **UNBLOCKED — start now** | No dependency of any kind. Unaffected by everything above; the manifest/notification icon set is client-owned and the specification says nothing about it. |
| **WP-P02** TLS and secure-context docs | **UNBLOCKED — start now**, minor revision | Add: `SecureContextHealthContributor` reads the external URL from `HostOptions` (spec WP-8, `Configuration/HostOptions.cs`) — **name that as its one host dependency**, which the PWA document left implicit. |
| **WP-P03** Client skeleton, manifest, install detection | **REVISE, then unblocked with spec WP-12** (wave 4) | Delete `{base}` substitution (finding 10); use relative manifest URLs; add `updateViaCache:'none'` (finding 9); verify the `browser-wasm` RID first (finding 7). §6.7:2727 already assigns `wwwroot/*` content to this document, so ownership is clean. |
| **WP-P04** Connectivity monitor and disconnected surface | **REVISE (route), then unblocked with spec WP-12** | `/app/ping` → **`/ping`**; do not fold into `/health` (resolution A13). Everything else stands. **Still the highest-value package in the delivery** and still independent of the service worker and of TLS. |
| **WP-P05** Service worker v1 | **REVISE, then unblocked after WP-P03** | New passthrough list ([§3.1](#31-route-conventions-open-question-1)); asset-class table corrected to `.wasm`/`.js`/`.dat` (finding 3); `arronix-runtime` cache deleted (resolution A12); PWA §2.7's `BlazorCacheBootResources` row deleted (finding 4). The §1.5.4 acceptance test is unchanged and remains the gate. |
| **WP-P06** Update detection, build headers, reload prompt | **REVISE, then blocked on spec WP-11** | No `HostUpgraded` `EventKind` (resolution A10). Mechanism: `X-Arronix-Build` + `X-Arronix-Min-Client-Build` response headers from `Arronix.Api` + `HubConnection.Reconnected`. Also carries the §6.7 amendment request ([§3.2](#32-the-hub-shape-message-names-and-the-build-header)). |
| **WP-P07** Notification contracts | **CANCELED** | Absorbed into spec **WP-3** (`src/Arronix.Abstractions/Providers/`). Executing it as written breaks `ARX0013`, `ExperimentalSurfaceTests`, `ContractAreaTests` and WP-3's file list. Replaced by a **review** obligation: WP-3's `INotifier.cs` and `NotificationMessage.cs` must be checked against [§4.1](#41-c2--the-contract-collision-item-by-item) and [§4.3](#43-the-event-vocabulary-and-the-revised-push-default-table) before it builds. |
| **WP-P08** Web Push crypto and sender | **UNBLOCKED — start now**, and now *more* independent | Its only Abstractions touchpoints are `IHttpGateway`, `IRateLimiter` and `ITelemetryEmitter`, all present today; **it no longer depends on WP-P07 at all**. All six BCL APIs verified (finding 6). Lands in `src/Arronix.Host/Notifications/WebPush/` — a tree spec WP-10 does not claim, so no ownership conflict. RFC 8291 §5's worked example remains the acceptance test. |
| **WP-P09** VAPID custody, subscription registry, push API | **BLOCKED past this milestone**, and halved | Blocked on **persistence** (§8:2934 — there is none in v0.3.0; `InMemoryMediaStore` only) *and* on **auth** ([§3.6](#36-multi-user-and-auth-mode-still-open-questions-3-and-4)). The VAPID-custody half is **deleted** ([§4.7](#47-c4--vapid-custody-reversing-pwa-resolution-15)) and `GET /api/v1/push/vapid` with it. What remains is the subscription table and `/api/v1/push/subscriptions**`. |
| **WP-P10** Provider family and dispatcher | **MOSTLY ABSORBED — retitle and shrink** | The registry, definitions, status/back-off, test endpoint and settings surface are spec **WP-10** (`Arronix.Host/Providers/`). What stays PWA-owned: the **`SummaryProjector`** ([§4.2](#42-c1--the-missing-renderer-the-headline-finding) Option 1), the coalescing window and its `AcquisitionComplete` preference, and the [§4.3](#43-the-event-vocabulary-and-the-revised-push-default-table) default set. **Delete its done-when about rejecting a plugin that declares `web-push`** (resolution A8). |
| **WP-P11** Client subscription, earned prompt, device UI | **BLOCKED** on WP-P09 | Unchanged in substance. Adds `PushDeviceView` ([§3.3](#33-wire-dto-locations)) to spec WP-6's file list. PWA §4.8's prompt chain and §4.10's UI stand verbatim. |
| **WP-P12** iOS install flow | **BLOCKED** on WP-P11 | Entirely unaffected by the specification. PWA §4.9 remains the most operationally important section in either document. |
| **WP-P13** Plugin assets, runtime cache, image cache | **MOSTLY DELETED — retitle to "Artwork proxy and image cache"** | Deleted: `/app/asset-manifest.json`, `descriptorSetHash`, `RESYNC_ASSETS`, the `arronix-runtime` cache, the plugin asset route and its content-type allowlist. Survives: the `arronix-images` LRU cache with its IndexedDB index, 300-entry / 100 MB cap and quota back-off — now **blocked on the artwork proxy** ([§3.5](#35-media-cover-urls-and-the-artwork-proxy-open-question-2)), a spec change. Its done-when is restated: *quarantining* a plugin changes the nav with no reload (plugin unload is not implemented this milestone, §8:2947). |
| **WP-P14** Share target | **BLOCKED, and harder than assumed** | There is **no `/add` route** in §6.6. Adding media is an `ActionDescriptor` with `ActionScope.Kind` posted to `/api/v1/kinds/{kind}/actions/{actionId}` — which requires knowing *which kind* an arbitrary shared URL belongs to. `IMediaItemSource.ResolveExternalAsync(ExternalId)` exists per kind, so a **host cross-kind resolve endpoint** would be the missing piece. Stays deferred; the dependency is now named rather than assumed. |

---

## 7. Ordered work-package list

Ordering is by "can it start" and then by value, not by number.

**Now, in parallel, with no dependency on anything in this milestone:**

1. **WP-P01** — brand and icon asset production. Reproducible generator from `Logo/Arronix.svg`.
2. **WP-P02** — `docs/setup/https.md`, plus the `SecureContextHealthContributor` specification.
3. **WP-P08** — Web Push crypto and sender. Pure, testable, host-independent; RFC 8291 §5 vectors; zero
   non-Microsoft packages (verified).

**Immediately after spec WP-1 lands (the five `ExperimentalContracts` constants), as a review not a build:**

4. **WP-P07R** *(replaces WP-P07)* — review spec WP-3's `INotifier.cs` / `NotificationMessage.cs` against
   [§4.1](#41-c2--the-contract-collision-item-by-item); file the two `NotificationEvent` requests from
   [§4.3](#43-the-event-vocabulary-and-the-revised-push-default-table); pin the enum's numeric values as
   load-bearing ([§4.5](#45-the-per-device-event-mask-without-a-flags-enum)).

**Blocking decisions to escalate before wave 4, in priority order:**

5. **C1 — the missing renderer.** Owned by `unified-host-runtime.md`. Recommend Option 1 (host derivation)
   and defer the seam. Nothing downstream of WP-P10 can start until this is decided.
6. **C3 — the artwork proxy.** Owned by `unified-host-runtime.md` §6.6 (route) and §6.3 (`ArtworkRef.Url`
   typing). Blocks the image cache and the push payload's `image` field.
7. **Authentication and multi-user.** Owned by a document that does not exist. Blocks WP-P09 → P11 → P12.
8. **A host-owned provider's `PluginId`** ([§4.6](#46-the-reserved-provider-id-and-the-host-plugin-id)).
   Small; blocks Web Push having anywhere to register.

**In spec wave 4, alongside WP-12 (`src/Arronix.Client/`):**

9. **WP-P03** — client skeleton, manifest, install detection. *(RID check first.)*
10. **WP-P04** — connectivity monitor and the disconnected surface. **The highest-value package in the
    delivery**; it needs no service worker and no TLS, so it fixes the NAS-reboot experience for every user
    including those stuck on plain HTTP.
11. **WP-P05** — service worker v1, against the revised exclusion list and asset classes.

**After spec WP-11 (`src/Arronix.Api/`):**

12. **WP-P06** — build headers, `registration.update()` chain, `Incompatible` gate.
13. **WP-P10R** — `SummaryProjector`, coalescing window, push default set.

**After persistence and authentication exist (not this milestone):**

14. **WP-P09** — subscription registry and `/api/v1/push/subscriptions**`.
15. **WP-P11** — client subscription, earned prompt, device management UI, `PushDeviceView`.
16. **WP-P12** — iOS install flow and platform branching.
17. **WP-P13R** — artwork proxy consumption and the `arronix-images` LRU cache.

**Deferred indefinitely:** WP-P14 (share target).

---

## 8. Deferred and out of scope — additions to PWA §6

`pwa-and-push.md` §6 is unchanged and still correct. This addendum adds seven rows.

| Item | Why |
|---|---|
| **`IMediaSummaryRenderer`** (the plugin-side summary seam) | Deferred with a named trigger: *a media plugin whose host-derived summary is demonstrably wrong, not merely plainer*. Zero plugins need it this milestone; promotion against zero needing implementers is what the three-tier rule exists to stop. See [§4.2](#42-c1--the-missing-renderer-the-headline-finding). |
| **Plugin-supplied UI assets of any kind** | Rejected permanently, not deferred. A plugin's only sanctioned way to show its own imagery is `ExternalSurfaceDescriptor` — pointing at a page it hosts itself, outside the client's origin and outside the capability model. |
| **The `arronix-runtime` service-worker cache** | Deleted. Its two contents (`/app/asset-manifest.json`, plugin logos) do not exist, and its trigger (live plugin load/unload) is not implemented this milestone (§8:2947). |
| **`descriptorSetHash`** | Deleted. Superseded by the ETag on `GET /api/v1/kinds`. A second freshness token for one payload is a second thing that can be wrong. |
| **A `HostUpgraded` member on `EventKind`** | Rejected. The client that most needs the message is the one that cannot parse it. `X-Arronix-Build` + `HubConnection.Reconnected` is complete and costs no wire surface. |
| **Caching `MediaKindDescriptor` in the Cache API** (§6.7:2756) | Recommend against. It is an `/api/**` URL, which resolutions #4/#5 forbid the worker from touching; cache it in `Arronix.Client` and let the ETag ride on the ordinary `HttpClient` request. **Recommend amending §6.7.** |
| **`RemediationHint` on `ValidationFailure`** | Rejected. Test answers "is this valid"; health answers "is this well". Two questions, two vocabularies, and a third would be one too many. |

---

## 9. Dependencies on decisions that do not yet exist

Stated plainly so nothing below is silently assumed. Each names the document that owns it.

| # | Missing | Owner | What it blocks |
|---|---|---|---|
| 1 | **How a plugin (or the host) produces a `RenderedSummary`** | `unified-host-runtime.md` §5.5 / §2.11 | Every notification, every provider, the client activity feed. `NotificationMessage.Summary` is `required` and currently unfillable. |
| 2 | **An artwork route and the `ArtworkRef.Url` origin rule** | `unified-host-runtime.md` §6.3, §6.6 | The image cache, the push payload `image` field, the `img-src` CSP, and a privacy property the product depends on. |
| 3 | **Authentication and the account model** | unwritten | `crossorigin="use-credentials"`, the logout rule, the subscription `UserId`, and whether any route in §6.6 is protected at all. |
| 4 | **Registration path for a host-owned provider, and its `PluginId`** | `unified-host-runtime.md` §5.1, §5.3 | Web Push having anywhere to live. |
| 5 | **Persistence** | `unified-host-runtime.md` §8 (Storage-Layer milestone) | The subscription registry, therefore all of push end-to-end. |
| 6 | **Sub-path / reverse-proxy hosting (`UrlBase`)** | unwritten | Nothing hard — the service worker derives its base from `self.registration.scope` and the manifest uses relative URLs — but the *server* must serve `index.html` and `service-worker.js` correctly under a prefix, and no design says it does. |
| 7 | **`IAppPaths`** | `docs/arronix-common-extraction-plan.md` (Tier B, planned, unbuilt) | Nothing, after [§4.7](#47-c4--vapid-custody-reversing-pwa-resolution-15) removes the dependency. Recorded because `pwa-and-push.md` §4.6 still names it. |

---

## 10. Recommended amendments to other documents

None of these are edited by this delivery; they are filed.

| Document | Amendment | Reason |
|---|---|---|
| `unified-host-runtime.md` §5.5 | Add the summary-production seam, or state that the host derives `Summary` | The document asserts a behavior it provides no mechanism for. [§4.2](#42-c1--the-missing-renderer-the-headline-finding) |
| `unified-host-runtime.md` §6.3 | `ArtworkRef.Url` → `string`, host-relative, matching `RenderedSummary.DeepLink` | The two disagree today for no stated reason, and the design needs the relative form. [§3.5](#35-media-cover-urls-and-the-artwork-proxy-open-question-2) |
| `unified-host-runtime.md` §6.6 | Add `GET /ping` → 204 and `GET /artwork/{hash}` | The service-worker exclusion list is generated from this table and must not be maintained twice. [§3.1](#31-route-conventions-open-question-1) |
| `unified-host-runtime.md` §6.7 | Scope the `CS8509` claim to the descriptor enums; move descriptor caching out of the service worker | `EventKind` arrives on a live socket from an unnegotiated peer; and `/api/**` is not worker-cacheable. [§3.2](#32-the-hub-shape-message-names-and-the-build-header), [§3.4](#34-descriptorsethash--etag--pluginstatechanged) |
| `unified-host-runtime.md` §5.5 | Add `DownloadFailed`; add `UpdateAvailable` or accept its deletion. Record that `NotificationEvent`'s numeric values are load-bearing | Two events the surveyed *arrs raise and PWA §4.4 defaults ON have no member; the per-device mask depends on stable ordinals. [§4.3](#43-the-event-vocabulary-and-the-revised-push-default-table), [§4.5](#45-the-per-device-event-mask-without-a-flags-enum) |
| `unified-host-runtime.md` §7 (WP-6) | Add `Wire/PushDeviceView.cs` | The device list crosses the client boundary; a mirrored type would be a guaranteed-divergence bug. [§3.3](#33-wire-dto-locations) |
| `pwa-and-push.md` | Resolutions **#13** and **#15** are reversed; **#8**'s mechanism changes; §2.1's SDK box, §2.2's asset extensions, §2.7's last row, §3.1's `{base}`, §4.2 in full and §4.4's table are superseded by this addendum | Recorded here rather than edited there, per the ownership note. |
