# Contract cache lifecycle in the browser — preparation for G07.3

**Status:** research only. This document prepares G07.3; it does not close it, does not change `CONTEXT.md` or
`INTERFACE.md`, and does not touch product code. G07.2 (generated client metadata, typed deserialization,
typed rendering) is still **not started** — `INTERFACE.md` line 364 and `docs/design/typed-media-roadmap.md`
both record it that way as of this audit — so nothing here assumes typed projection exists. Every claim below
is read from the code and tests present on this branch at commit `ce643da45`, not from the roadmap's
description of intent.

G07.3's stated outcome is: "installing, updating, and removing a package, and holding a tab open across those
changes, all behave predictably in the browser and fail visibly rather than projecting partial data." Its exit
gate names four exercises — update, removal, stale cache, stale tab — plus the one sentence that decides the
shape of all of them: *a tab holding a withdrawn contract refuses to project it and says so, rather than
continuing to render from a contract the host no longer admits.*

## 1. What G07.1 already built that G07.3 inherits

G07.1 (`docs/research/g07/client-contract-loading.md`) did not stop at "publish and load once." It already
built most of the machinery a lifecycle story needs; G07.3's job is narrower than a fresh read of its exit
gate suggests. Four mechanisms already exist and are already proven:

1. **Content addressing, not versioning.** `GET /api/v1/client-contracts/{package}/{contentHash}/{fileName}`
   names bytes, not a mutable slot (`src/Arronix.Api/Endpoints/ClientContractEndpoints.cs:56-65`). An
   installation that changes mints new addresses; it never changes what an old one means. This is why the
   store (`src/Arronix.Client/wwwroot/js/contract-store.js`) can be a flat content-hash-keyed map with no
   invalidation protocol of its own — every entry is either the bytes it names or absent.
2. **The manifest is the single source of truth, recomputed, never cached.** `ClientContractCatalog.Manifest()`
   (`src/Arronix.Plugins/Registry/ClientContractCatalog.cs:123-149`) reads the plugin runtime registry's Active
   set fresh on every call, under `PluginPublicationGate.EnterRead()`, and is served with
   `Cache-Control: no-cache, no-store, must-revalidate`
   (`src/Arronix.Api/Endpoints/ClientContractEndpoints.cs:67-81`). There is no server-side manifest cache to go
   stale and no second registry to fall out of step with the first.
3. **410 Gone is already distinguished from 404 Not Found on the byte route.** `ClientContractCatalog.Open`
   returns `Superseded` when a package still offers a file by that name but under a different content hash, and
   `NotOffered` when nothing does (`src/Arronix.Plugins/Registry/ClientContractCatalog.cs:152-187`). The
   endpoint renders those as `410`/`404` respectively
   (`src/Arronix.Api/Endpoints/ClientContractEndpoints.cs:117-127`). This is exactly the signal an "update
   in flight" or "removed" distinction needs; G07.3's gap is that the *client* does not yet read it (§4.1).
4. **The "same simple name, different bytes" case is already Terminal, already tested, already proven in a
   real load context.** `MediaContractLoader.PreflightAsync` refuses a resident name whose newly published
   description does not match (`src/Arronix.Client/Contracts/MediaContractLoader.cs:340-353`), and
   `ContractCommitTests.AVerifiedInstallationIsLoadedReusedAndThenTerminalWhenTheHostMovesOn`
   (`src/Arronix.Client.Tests/Contracts/ContractCommitTests.cs:200-221`) drives it end to end, including a real
   `AssemblyLoadContext.Default.LoadFromStream` and a follow-up load that proves Terminal is sticky even after
   the host reverts to what the page already holds. **Update-in-place, for an assembly this tab is currently
   using, is not a G07.3 gap. It is closed G07.1 work.** What G07.3 actually owns is narrower: the assembly
   that stops being required at all, and everything downstream of that.
5. **The publication boundary that would make a "torn read" possible does not exist.** Host publication and
   withdrawal are atomic through one shared gate (G02 completion notes, `docs/design/typed-media-roadmap.md`
   lines 216-219), so `Manifest()` can never observe "package A upgraded, package B mid-upgrade" as two
   half-applied facts in one call. A "failed/partial update" cannot appear as a torn manifest; it can only
   appear as two well-formed manifests in a row, which is an ordinary update-or-removal case, not a new
   category. §5.7 states this precisely so it is not accidentally re-litigated during implementation.
6. **The PWA service worker and the contract store cannot collide.** `service-worker.published.js` passes
   every `/api/`-prefixed request straight to the network (`networkOnlyPaths`, lines 29-31, 65-67) and only
   ever deletes caches under its own `arronix-cache-` prefix on activation (lines 50-58); the contract store
   lives under `arronix-client-contracts-v1` (`contract-store.js:15`) and is never touched by a shell update.
   This must stay true — see invariant I-7 in §6.

## 2. The four coupled lifecycles

There is no single state machine here; there are four, coupled through what one reads from another. Confusing
them is the likely failure mode of an under-designed G07.3, so they are named separately.

- **M1 — a package's server-observed client facet.** What `Manifest()` says about one `PluginId`, as it would
  be observed by polling it repeatedly. The server holds no history; M1 is a description of the sequence of
  answers, not server-side state.
- **M2 — one assembly's residency inside one tab.** The real subject of G07.3. Owned by the `_resident`
  dictionary and `_terminal` field inside one `MediaContractLoader` instance
  (`src/Arronix.Client/Contracts/MediaContractLoader.cs:42-44`).
- **M3 — one tab's overall compatibility.** `ContractLoadReport.Compatibility`
  (`src/Arronix.Client/Contracts/ContractLoadReport.cs:186-204`). Coarser than M2; already closed for the
  cases G07.1 owns.
- **M4 — the browser's content store.** `ContractStore` / `contract-store.js`. Purely a byte cache; must never
  gate correctness, only speed.

### 2.1 M1 — a package's client facet, as observed over successive reads

| State | Meaning | Evidence today |
| --- | --- | --- |
| `Unpublished` | The package identifier appears in neither `Packages` nor `Refused`. | Absence is the whole representation; nothing asserts it directly, but `PackagedClientContractTests.AStoppedInstallationOffersNothing` (`src/Arronix.Host.Tests/Runtime/PackagedClientContractTests.cs:214-235`) proves a stopped host's manifest has zero packages. |
| `Offered(hash)` | Present in `Packages`, serving real bytes at that package's current content hash(es). | The whole G07.1 server-half matrix. |
| `Withheld(reason)` | Present in `Refused`, with `Reason`, `MissingAssemblies`/`UnadmittedFiles`, and `CausedBy` (`src/Arronix.Abstractions/Wire/ClientContractManifest.cs:98-103`). | `ClientFacetResolverTests` (`src/Arronix.Plugins.Tests/Registry/ClientFacetResolverTests.cs`) proves the withholding rule to a fixed point. |

Transitions a client can observe between two manifest reads: `Unpublished → Offered` (install),
`Offered(h1) → Offered(h2)` (update: same identifier, new content hash on one or more assemblies, and/or a
changed `ClosureHash` because a *dependency* updated even though this package's own bytes did not — proven
distinct in `PackagedClientContractTests` around line 206, which asserts an unrelated package's closure hash
is stable when a sibling changes), `Offered → Withheld` (a declaration or a dependency broke),
`Offered → Unpublished` (uninstall or quarantine), `Withheld → Offered` (fixed), `Withheld → Unpublished`
(uninstalled while broken), `Unpublished → Withheld` (installed already broken). All of M1 is already provable
today with `IClientContractCatalog.Manifest()` calls around a `PluginRuntimeRegistry` fixture; it needs no new
server mechanism. What is missing is the *client* reacting correctly to each transition, which is M2.

### 2.2 M2 — one assembly's residency inside one tab (the actual subject of G07.3)

States, keyed by simple assembly name inside one `MediaContractLoader`:

| State | `_resident` holds it? | `Find()` serves it? | Reported to `ContractsPage`? |
| --- | --- | --- | --- |
| `Untouched` | no | no | not applicable |
| `Verified` | no | no | yes, as `Verified` |
| `Loaded` / `AlreadyLoaded` (current) | yes | yes | yes |
| `NameAlreadyResident` (Terminal) | yes (the old bytes) | no — tab-level `_terminal` blocks `Find()` universally | yes, as `NameAlreadyResident`, and the tab is `Terminal` |
| `RuntimeRefused` (Terminal) | no | no — tab-level `_terminal` blocks `Find()` universally | yes, as `RuntimeRefused` |
| **`Orphaned`** *(does not exist yet — the G07.3 gap)* | **yes** | **yes, incorrectly** | **no — silently absent** |

The last row is the finding this whole document turns on, so it is worth stating exactly, in code terms.
`RequiredAssemblies(manifest)` (`MediaContractLoader.cs:508-536`) is built only from the packages the *current*
manifest lists. When package `movies` was Active, offered, loaded, and is then withdrawn (uninstalled,
quarantined, or its facet withheld by a dependency loss), the next `LoadAsync` call:

- never adds `Arronix.Media.Movies` to `required` (line 218), so `PreflightAsync` is never called for it;
- never visits it in the commit loop (lines 255-319), so it is never re-verified, never re-committed, and
  never flagged;
- leaves it exactly as it was in `_resident` (line 42), because nothing in `LoadCoreAsync` ever removes an
  entry from that dictionary;
- omits it from the rendered report entirely, because `Project()` (lines 539-556) walks `manifest.Packages`,
  and the withdrawn package's `LoadedContractPackage` panel simply does not exist in the new report — not
  "shown as withdrawn," *absent*, the same as a package this host never had;
- and `Find("Arronix.Media.Movies")` (lines 110-117) still returns the resident `Assembly` object, because its
  two conditions — `Report?.CanProject == true` and dictionary membership — are both still true if every
  *currently required* package (e.g. just `arronix.format.video`) verified cleanly.

So today: a tab that loaded Movies, then had Movies withdrawn server-side, reports itself fully `Compatible`
after its next reload, shows no trace that Movies was ever there, and a caller asking `Find("Arronix.Media.Movies")`
still gets a live, resident, loadable-looking assembly reference back. This is precisely the failure the exit
gate names — "continuing to render from a contract the host no longer admits" — except it is worse than a
rendering bug, because there is currently no way for any caller to even detect the condition. `Compatible`
today means "everything the current manifest requires is resident"; it has never meant, and cannot by
construction mean, "everything this tab has ever loaded is still valid." G07.3 has to introduce a state that
carries that second fact, because nothing upstream of `Find()` can currently ask for it.

Transitions:

- `Untouched → Verified → Loaded`: the ordinary first pass (already proven).
- `Loaded/AlreadyLoaded → AlreadyLoaded`: unchanged manifest entry, next pass (already proven, `Resident`
  source).
- `Loaded/AlreadyLoaded → NameAlreadyResident`: manifest still names this simple name, with different bytes
  (already proven — this is "update," and it is correctly Terminal).
- `Loaded/AlreadyLoaded → Orphaned` *(new)*: manifest's required set no longer names this simple name at all —
  package withdrawn, quarantined, or its facet withheld (this is "removal," and it is currently silent, not
  Terminal, not even visible).
- `Orphaned → AlreadyLoaded` *(already correct, once `Orphaned` exists)*: the package returns, offering the
  exact same content hash/identity/module/length this tab already verified. No re-verification is owed here —
  content-hash equality already is the proof, and `Matches()` (`MediaContractLoader.cs:624-628`) already
  implements exactly this reunion for any name still present in `required`. The only work is making the
  *interim* state visible and non-serving instead of invisible and serving.
- `Orphaned → NameAlreadyResident` *(new, and worth stating so it is not missed)*: the package returns, but
  under a *different* content hash than this tab has resident. This is the same Terminal outcome as ordinary
  update — the sequence "withdraw, then republish something else under the same name" must not be treated more
  leniently than "republish something else directly," because the tab's problem (an unloadable, wrong,
  resident assembly) is identical either way.

### 2.3 M3 — one tab's overall compatibility

`ContractCompatibility` (`ContractLoadReport.cs:128-164`): `Unknown → {Compatible, ContractIdentityMismatch,
Unreachable, ManifestInvalid, Refused, Terminal}`. `Terminal` is the only absorbing state — checked and
short-circuited before any network call on every subsequent `LoadAsync` (`MediaContractLoader.cs:205-214`).
Every other value is recomputed fresh each call. This machine needs **no new states** for G07.3. `Compatible`'s
existing, narrow definition — "everything currently required is resident" — is correct and should not be
widened to also mean "nothing orphaned exists," because that would make an ordinary, harmless removal of an
unrelated package (one this tab never touched) look like a fault. The fix belongs entirely in M2 and in what
`Find()` and the report expose, not in adding a seventh `ContractCompatibility` value.

### 2.4 M4 — the browser content store

Per content-hash key: `Absent → Present` (write after a Network-sourced commit,
`MediaContractLoader.cs:315-318`) `→ Absent` (`DiscardIfStoredAsync` on failed verification,
`MediaContractLoader.cs:492-498`, or a full `ClearAsync()` from the "Discard stored bytes" button,
`ContractsPage.razor:239-252`). There is no state between entries a live installation names and entries it
does not — nothing ever asks "is anything in the store that nothing currently requires?" `ContractStore`
exposes the primitives an eviction policy needs (`KeysAsync`, `RemoveAsync`) but nothing calls them for that
reason today. This is store-only housekeeping: removing an entry here can only make a future load slower
(fall back to network, "exactly as correct" per the store's own documented philosophy,
`contract-store.js:1-14`), never wrong, because nothing is trusted without being re-hashed regardless of
where the bytes came from (`MediaContractLoader.cs:405-421`).

## 3. Concrete seams

Each of these is a specific place in the existing code where G07.3's first slices attach. None is exercised
here — this section is a map, not a patch.

1. **`Find()` and `_resident` have no concept of "still current."**
   `src/Arronix.Client/Contracts/MediaContractLoader.cs:42, 110-117, 508-536`. `Find` must be able to refuse a
   name that is physically resident but no longer named by the last manifest this loader read at a matching
   hash. This needs the loader to retain, per resident entry, whether the most recent `LoadAsync` re-confirmed
   it — not just whether it was ever loaded.
2. **`Project()` drops packages the caller has no other way to learn about.**
   `MediaContractLoader.cs:539-556`. It walks `manifest.Packages` only, so a withdrawn package's entry — the
   one thing an operator or a future rendering surface would need to explain "why did this disappear" —
   is never emitted. A stale-but-resident entry needs to survive into the report under its own heading, the
   way `Refused` already survives from the *server's* side (`ContractLoadReport.cs:172` /
   `ContractsPage.razor:74-118`).
3. **Byte-fetch failure collapses `404`, `410`, and genuine transport failure into one `Unavailable`.**
   `MediaContractLoader.cs:365-391`. `GetByteArrayAsync` throws on any non-2xx and the catch block
   (`.PreflightAsync`, lines 388-391) reports `ContractLoadOutcome.Unavailable` regardless of which. The server
   already computed the right distinction (`ClientContractOutcome.Superseded` → `410`,
   `NotOffered` → `404`, `ClientContractEndpoints.cs:102-128`) and throws it away at the transport boundary.
   This matters specifically for the race in §5.7: manifest read, then the host republishes before the byte
   fetch completes — today that reads as an opaque "Unavailable," when it is actually "re-read the manifest,
   the address moved."
4. **No selective eviction exists; only total `ClearAsync()`.**
   `ContractStore.cs:154-170` / `ContractsPage.razor:239-252`. The primitives (`KeysAsync`, `RemoveAsync`) are
   present; nothing computes "keys present, minus keys the current manifest names" and drives them.
5. **Nothing observes server-side change except a manual click.**
   `ContractsPage.razor:29, 219-232` — "Reload contracts" is the only trigger for a fresh `LoadAsync`.
   `DescriptorCache` already solved the equivalent problem for descriptor data by invalidating on
   `EventKind.PluginStateChanged` over the live event stream (`src/Arronix.Client/Services/DescriptorCache.cs:129-135`);
   nothing analogous exists for contracts. A cheap "is this stale" check should not require a full
   hash-and-metadata verification pass — `InstallationHash` already exists for exactly this purpose
   (`ClientContractManifest.cs:112-115`: "one value that changes whenever anything a client would load
   changes, so a client can decide whether to re-read the rest") and nothing currently reads it for that
   purpose outside the loader's own internal comparison.
6. **`ClientContractHealthContributor` cannot see, and should not be asked to see, browser-tab state.**
   `src/Arronix.Host/Health/ClientContractHealthContributor.cs`. It reports only server-side withheld facets
   (M1's `Withheld`). It has, and should keep, zero visibility into any tab's M2/M3 state, because that state
   is private per tab and the server holds no session affinity to it. This asymmetry is intentional, not a
   gap — stated here so it is not "fixed" by accident during implementation (see invariant I-6).

## 4. Acceptance and mutation matrix

Each row states the scenario, the required all-or-nothing/visible-failure behavior, whether it is proven today
(and by what), and — where it is not proven — the gap and the mutation that should be written to guard the fix
once it lands. "Proven" means an existing, named, currently-passing test; "gap" means no code path produces
the required behavior yet.

### 4.1 Install

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| A1 | Clean store, first load, package never seen before | Fetched over network, verified byte-for-byte before load, `Loaded`, written to store | **Proven** — G07.1 browser matrix case 1; `ContractCommitTests` part two |
| A2 | Warm store, same content hash already cached | Read from store, still hashed and identity-checked before load, zero network byte requests | **Proven** — G07.1 browser matrix case 2 |
| A3 | Two packages, one depends on the other; dependency must load first | Closure order from the manifest is followed, not recomputed from reference tables | **Proven** — `PackagedClientContractTests`, `RequiredAssemblies` |
| A4 | Install of a package whose declared facet is invalid (unadmitted file, unreachable reference) | Never appears in `Packages`; appears in `Refused` with a reason; nothing is fetched for it | **Proven** — `ClientFacetResolverTests` |

### 4.2 Update (content hash, closure hash, and — once G07.2 lands — generated-metadata/schema hash)

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| U1 | A package this tab has **not** loaded is updated | Next `LoadAsync` fetches and verifies the new bytes normally; no terminal state | **Proven** by construction — a fresh name has no `_resident` entry to conflict with |
| U2 | A package this tab **has** loaded is updated (same simple name, new content hash) | Terminal, with a stated reason, on the pass that discovers it; sticky across further loads including a revert | **Proven** — `ContractCommitTests.AVerifiedInstallationIsLoadedReusedAndThenTerminalWhenTheHostMovesOn` |
| U3 | A dependency updates; a dependant's own bytes are unchanged | The dependant's `ClosureHash` changes; the dependant's own assembly content hash does not | **Proven** — `PackagedClientContractTests` closure-hash-stability assertion |
| U4 | *(post-G07.2)* Generated-metadata hash or projection-schema hash changes without the assembly's content hash changing | Should be structurally impossible if metadata is embedded in the same content-addressed assembly bytes G07.1 already verifies; if G07.2 instead publishes either hash as a sibling fact, G07.3 must extend Preflight to check it, but the *lifecycle* (install/update/remove/evict) rule is unchanged — it is still one address, one verification pass | **Not applicable yet** — flagged so G07.3 is not re-scoped when G07.2 lands; see §7 |
| U5 | Manifest is re-read mid-load: host updates *between* the manifest fetch and a byte fetch in the same `LoadAsync` pass | The byte address for the old manifest's entry is `410 Gone`; the loader should recognize "superseded, not merely unreachable" and could recover by re-reading the manifest rather than reporting an opaque failure | **Gap** — seam 3; currently collapses to generic `Unavailable` |

Mutation for U2 (already guarded, restated for completeness): skip the `NameAlreadyResident` check in
`PreflightAsync` → `ContractCommitTests` fails immediately, because the second load of the mutated build would
report `Compatible` instead of `Terminal`.

### 4.3 Removal / withdrawal

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| R1 | Server-side: package is stopped/uninstalled; `Manifest()` no longer lists it | Manifest simply omits it; no crash, no stale row | **Proven** — `PackagedClientContractTests.AStoppedInstallationOffersNothing` |
| R2 | Client-side, this tab **never loaded** the withdrawn package | Next `LoadAsync` is `Compatible` over whatever remains; nothing to say about a package this tab never touched | **Proven by construction** |
| R3 | Client-side, this tab **has loaded and is currently using** the withdrawn package | `Find()` for that name must refuse once the current manifest stops naming it; the report must say the package is gone rather than silently dropping its panel; the tab's overall `Compatibility` for the *rest* of the installation remains `Compatible` — a removal elsewhere must not manufacture a fault | **Gap** — seam 1/2; this is the central finding of §2.2 |
| R4 | The withdrawn package returns later, at the exact hash this tab already verified | Recognized without a byte refetch or re-verification; returns to a serving state | **Already correct once R3 exists** — `Matches()` already implements the equality check; only the interim visibility is missing |
| R5 | The withdrawn package returns later, at a **different** hash than this tab holds | Terminal, identical in kind to U2 — a stale resident assembly is a stale resident assembly regardless of the path that produced it | **Gap**, same seam as R3 |

Mutation for R3 (to write once implemented): stop tagging a `_resident` entry as stale when its name drops out
of `RequiredAssemblies` → the browser-matrix case exercising R3 should fail, because `Find()` would keep
answering non-null for a package the manifest no longer lists.

### 4.4 Eviction

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| E1 | Store holds a content hash no live package names (left over from an earlier install this tab warmed the cache for) | Eventually removed from the store; **never** removed from a runtime-resident assembly, because that removal is impossible and must not be attempted | **Gap** — seam 4 |
| E2 | Eviction runs while another tab is mid-fetch of the same hash from the store | The other tab's already-read bytes are unaffected; a miss simply falls back to network, which is "slower and exactly as correct" by the store's own stated contract | **Provable by design, not yet exercised** — no code path exists to race against |
| E3 | Manual "Discard stored bytes" | Full store wipe; already-resident runtime assemblies in *this* tab are explicitly unaffected (already documented behavior) | **Proven / documented** — `ContractStore.ClearAsync`, `ContractsPage.razor:234-238` remarks |

Mutation for E1 (to write once implemented): have the eviction pass remove a hash that a live package *does*
name → the next load of that package must still verify and load correctly from network (proves eviction never
silently corrupts an address a client would still want) — this is the negative-space guard that stops an
eviction rule from becoming a data-loss rule.

### 4.5 Stale cache / stale tab

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| S1 | A tab opened before an update never calls `LoadAsync` again | It keeps rendering from what it already resolved to be `Compatible`; this is honest, not a bug — nothing observed the change, so nothing can be said about it | **Correct by the current design's own terms**; the risk is purely UX (no signal exists to prompt a reload) |
| S2 | A tab is prompted (by any means) to re-check and does | `LoadAsync` re-reads the manifest; if the tab holds nothing that conflicts, it becomes `Compatible` again cleanly; if it holds a now-orphaned or now-conflicting assembly, R3/U2 apply | **Half-proven** — U2's branch is proven; R3's branch is the gap |
| S3 | A tab's own manifest read is itself stale relative to a *second*, even newer manifest published between two of its own polls | Irrelevant to the loader — it only ever compares "what I hold" against "what I just read"; a skipped intermediate state is invisible and does not need to be, because each read is self-consistent (M1 has no torn state to skip) | **Correct by construction** — see §1 item 5 |
| S4 | An operator wants to know "am I stale" cheaply, without a full verify pass | `InstallationHash` already exists for exactly this; nothing currently exposes a cheap poll/compare of it outside the loader's own full pass | **Gap** — seam 5 |

### 4.6 Withdrawn addresses (the byte route itself)

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| W1 | Address for a hash a package still offers under a different current hash | `410 Gone`, distinct from `404` | **Proven** — G07.1 server-half matrix, `ClientContractEndpoints.cs:117-122` |
| W2 | Address for a package this host never installed, or no longer has Active at all | `404 Not Found` | **Proven** — G07.1 server-half matrix |
| W3 | Client reaction to `410` specifically (as opposed to `404` or a network failure) | Should be distinguishable from an ordinary transport failure, ideally triggering a manifest re-read rather than a bare failure report | **Gap** — seam 3, same as U5 |

### 4.7 Failed / partial updates

As established in §1 item 5, the server side cannot produce a torn manifest — publication is atomic. What
remains under this heading is entirely client-side concurrency within one tab:

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| F1 | Two overlapping calls to `LoadAsync` on one loader instance (e.g., a user clicks "Reload contracts" while a load from page-init is still in flight) | Serialized, never interleaved; the second call observes whatever the first left behind, not a torn mix of both | **Proven** — `_gate` (`SemaphoreSlim(1,1)`, `MediaContractLoader.cs:43, 126-138`) serializes every `LoadAsync` call |
| F2 | Host republishes between this tab's manifest fetch and its byte fetch for one entry | That one entry's fetch returns `410`; today reported as opaque `Unavailable`; should be recoverable by re-reading the manifest rather than terminal | **Gap** — same as U5/W3 |
| F3 | The commit phase itself is interrupted partway (one entry loads, the next is refused by the runtime) | Everything before the failure stays exactly `Verified` (not `Loaded`); everything after is never attempted; the tab is `Terminal`; nothing partially resident is reported as usable | **Proven** — `ContractCommitTests` part one, and the G07.1 "all or nothing" mutation table |

### 4.8 Concurrent tabs

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| C1 | Two tabs write the same content hash to the shared store concurrently | Idempotent — identical bytes under an identical key; no corruption possible by construction, because the key *is* the hash of the value | **Provable by design**; not separately exercised, and probably does not need to be — it follows from the store contract, not from timing |
| C2 | Tab A causes eviction (once E1 exists) of a hash that tab B is mid-fetch of from the store | B's in-flight read is unaffected; a miss on B's *next* fetch simply falls back to network | **Design-level guarantee to state explicitly once eviction exists**, per §2.4 |
| C3 | Tab A is Terminal (holds a stale resident assembly); tab B, opened fresh afterward | A's Terminal state is entirely local — `_resident`/`_terminal` live on the `MediaContractLoader` instance, which is per-tab (registered via `TryAddSingleton` inside one Blazor WASM app instance, `ArronixClientServiceCollectionExtensions.cs:58-59`, i.e., per page load, not per origin) — B loads cleanly against the current installation with no cross-tab interference | **Proven by construction** — there is no shared client-side state between tabs other than the store, and the store cannot make a load incorrect (§2.4) |
| C4 | Two tabs, one still on the old build of a package (never reloaded), one freshly loaded on the new build | Both are individually self-consistent and honest about what they hold; this is the ordinary, permanent consequence of "a browser cannot unload," not a bug to fix — see S1 | **Correct by design; this is the platform constraint, not a defect** |

### 4.9 Non-unloadable assemblies

This is not a separate mechanism to build; it is the axiom every other row above already assumes, restated so
it cannot be quietly violated by a future change:

| # | Scenario | Required behavior | Status |
| --- | --- | --- | --- |
| N1 | Any code path, present or future, that might be tempted to "fix" a stale or conflicting resident assembly in place | Must be refused at the design level. The only correct recovery from `NameAlreadyResident` (M2 Terminal) is a full page reload; there is no partial remediation | **Proven / enforced by the existing Terminal design** — restated as a constraint on G07.3, not a new build item |
| N2 | Store eviction (once it exists) must never be phrased or implemented as "unloading" | Eviction removes bytes from `Cache Storage`; it has and can have zero effect on `AssemblyLoadContext.Default` | **Design constraint for seam 4** |

## 5. Invariants G07.3 must preserve

- **I-1 (all-or-nothing, restated for lifecycle).** `ContractLoadReport.CanProject` stays true only when every
  *currently required* assembly is resident and current. An orphaned-but-resident entry existing somewhere in
  memory must never make `CanProject` false for an otherwise-healthy installation, and must never let `Find()`
  hand it out either. Both halves matter: G07.3 must not trade a silent-serving bug for a false-alarm bug.
- **I-2 (verified is not loaded — extends to "resident is not current").** The same discipline G07.1 applied to
  `Verified` vs. `Loaded` applies to a new distinction between "resident" and "resident and still named by the
  installation this tab most recently confirmed." Neither may be conflated with the other in the report.
- **I-3 (nothing is loaded to find out whether it should have been).** Untouched by G07.3 — no new code path
  should ever hand bytes to the runtime before the full required closure verifies, and eviction/staleness work
  never touches the runtime at all, only the store and the loader's own bookkeeping.
- **I-4 (a superseded address is recoverable; a resident conflict is not).** `410` on the wire and `Orphaned`/
  `NameAlreadyResident` in the loader are different severities and must stay different: the former means
  "re-read the manifest," the latter means "reload the page." Collapsing them (seam 3) is exactly the bug to
  avoid introducing while fixing it.
- **I-5 (the manifest is untrusted input, including across two reads).** `ContractManifestValidator` already
  proves one manifest whole before anything acts on it. G07.3 must not add any logic that compares *two*
  manifests (this read vs. the last one) without the same discipline — e.g., a diff that assumes package order
  is stable, or that a `PluginId` reappearing means the same installation, would reintroduce exactly the class
  of bug §1's item 5 already closed for a single read.
- **I-6 (server health has no visibility into tab state, on purpose).** `ClientContractHealthContributor`
  reports M1 only. Do not wire tab-local M2/M3 state into it; the server has no channel to learn it, and
  inventing one (e.g., a tab reporting its own staleness back over the event hub) is a scope decision, not a
  natural extension — flag it to the owner if it comes up rather than building it.
- **I-7 (the service worker's cache and the contract store stay disjoint).** Any future service-worker change
  must keep matching only its own `arronix-cache-` prefix when deleting caches on activation
  (`service-worker.published.js:50-58`); a wildcard broadened to "everything but the current shell cache"
  would silently sweep `arronix-client-contracts-v1` on every deploy and turn every install into a cold-store
  install.
- **I-8 (identifiers are identifiers, still).** Any new client-side bookkeeping keyed by package identity uses
  `PluginId`, not raw text, consistent with the rest of the design (`docs/research/g07/client-contract-loading.md`
  §2, "Identifiers are identifiers").

## 6. First five implementation slices after G07.2

These are ordered by dependency, not by roadmap gate — each is small enough to land and prove independently,
and none requires G07.2's generated metadata or typed rendering to exist first, so in principle slices 1–3
could start before G07.2 closes if the owner wanted to decouple them; they are sequenced after it here only
because that is the roadmap's stated order and because a real Movie rendering surface (G07.2's outcome) is
what makes "a tab silently keeps rendering a withdrawn contract" a user-visible failure worth a UI treatment,
rather than only a diagnostic-page curiosity.

1. **Make an orphaned resident assembly a distinct, visible, non-serving state.** Add the `Orphaned` outcome
   from §2.2 to `ContractLoadOutcome`; have `LoadCoreAsync` mark a `_resident` entry orphaned when its name
   drops out of `RequiredAssemblies(manifest)` on a pass that otherwise completes; make `Find()` refuse an
   orphaned name; make `Project()` (or a sibling method) emit orphaned entries into the report under their own
   heading instead of letting their package vanish. Targets: `MediaContractLoader.cs` (`Find`, `LoadCoreAsync`,
   `Project`), `ContractLoadReport.cs` (`ContractLoadOutcome`, a rendering-side label). Proof: a new
   `ContractCommitTests`-style narrative — load two packages, then load again against a manifest that omits
   one of them entirely (not a hash change) — and the hermetic case from R3/R5 in §4.3.
2. **Distinguish `410`/`404`/transport failure in `PreflightAsync`, and make a superseded byte fetch
   recoverable.** Stop discarding the HTTP status behind `GetByteArrayAsync`'s exception; give `Unavailable` a
   real `Superseded` sibling outcome (or a flag on it) for the `410` case, and have the loader's caller (or the
   loader itself, on its next natural call) prefer "re-read the manifest" over "report a hard failure" when it
   sees one. Targets: `MediaContractLoader.PreflightAsync` (the fetch try/catch), a small addition to
   `ContractLoadOutcome`. Proof: a `StubHandler`-based hermetic test that answers the byte route with `410`
   after the manifest already promised that hash (this exercises U5/W3/F2 together, since they are one
   mechanism).
3. **Selective store eviction driven by the manifest just verified.** Add a policy step — callable from
   `MediaContractLoader` after a successful `LoadAsync`, or from a small owner (e.g., a `ContractStoreJanitor`)
   the Contracts page or app shell drives — that computes "hashes the just-read manifest names" and removes
   any `ContractStore` key outside that set, using the existing `KeysAsync`/`RemoveAsync` primitives. Must
   never be reachable from anything that also touches `_resident` or the runtime. Targets: a new small type
   plus `ContractStore.cs` (no new store primitives needed, just a driver). Proof: E1's mutation in §4.4 —
   evict something live and prove the next load still succeeds from network — plus a positive case proving an
   orphaned hash is actually gone from `KeysAsync()` afterward.
4. **A cheap, event-driven staleness signal, separate from the verifying load.** Mirror
   `DescriptorCache.OnReceived` (`Services/DescriptorCache.cs:129-135`): a small service subscribes to the
   existing event stream, and on `EventKind.PluginStateChanged` compares the last known `InstallationHash`
   (cheap: already present on every manifest read, no byte verification needed) against a fresh manifest-only
   fetch, and raises an "this host has changed" signal a page can render as a banner — distinct from, and far
   cheaper than, triggering a full `LoadAsync`. This is what makes S1/S4 in §4.5 actionable instead of merely
   theoretically correct. Targets: a new `Arronix.Client/Services` type alongside `DescriptorCache`, wired the
   same way in `ArronixClientServiceCollectionExtensions.cs`. Proof: a hermetic test on the new service using
   the existing `EventStream` test doubles, asserting the signal fires on `PluginStateChanged` and not on
   unrelated event kinds.
5. **Extend the real-browser and hermetic evidence to cover true removal and eviction, not just update.**
   `eng/proofs/g07-client-contracts.sh` currently proves install and the server-side withdrawal case
   (`AStoppedInstallationOffersNothing` is a Host-test, not part of the browser script). Add a second server
   run to the script — start with both packages, restart with only one — and extend the browser matrix with
   the removal case: a tab that already loaded Movies, then reloads against the shrunk manifest, must show
   Movies' entry as orphaned/refused rather than absent, and `#contract-proof` must say so in text a driver can
   assert on. Add the corresponding hermetic cases for slices 1–3. This slice is last because it is the thing
   that turns slices 1–4 from "code that exists" into "G07.3 exit-gate evidence," exactly as the G07.1 doc's
   own evidence section (§4) did for that gate.

## 7. Explicitly out of scope here, and owner-relevant flags

- **G07.2's generated-metadata and projection-schema hashes.** Not designed here. §4.2 row U4 records the one
  structural claim needed so G07.3 is not accidentally re-scoped: if those hashes are embedded in the same
  content-addressed assembly bytes G07.1 already verifies, the lifecycle rules in this document need no
  change at all — they are address-based, not fact-count-based. If G07.2 instead publishes either hash as an
  independent, separately-served fact, G07.3's Preflight step (not its lifecycle model) grows one more check;
  flag this to the owner when G07.2's actual shape is decided, not before.
- **A cross-tab or server-remembered "this tab is stale" signal.** Invariant I-6 argues against building one
  by default. If a future requirement wants the server to know a tab is running stale content (e.g. for a
  fleet-health view), that is a new capability, not a natural extension of `ClientContractHealthContributor`,
  and should be raised as its own decision rather than folded into G07.3's slices.
- **Package signing / provenance.** Still unbuilt, as recorded at G03 and restated in the G07.1 residual gaps
  (`docs/research/g07/client-contract-loading.md` §5.4). Nothing in this document changes that; content hashes
  say which bytes, not who vouched for them, and the lifecycle rules here do not depend on that changing.
- **`IClient` plugin lifecycle and trust model.** Explicitly out of scope for all of G07, per the roadmap's own
  framing note (`docs/design/typed-media-roadmap.md` lines 68-72) and `docs/owner-ledger.md` O-16. This
  document's "concurrent tabs" analysis is about ordinary browser tabs running the one first-party client, not
  about a future world of independently loaded client plugins.

## 8. References

- `docs/design/typed-media-roadmap.md` — G07, G07.1, G07.2, G07.3 sections.
- `docs/research/g07/client-contract-loading.md` — the G07.1 record this document treats as its baseline.
- `CONTEXT.md`, `INTERFACE.md` — current architecture and public surface (unread lines cited by section above).
- `docs/owner-ledger.md` O-16, O-21 — client trust model and dynamic loading decisions still open.
- Source: `src/Arronix.Client/Contracts/*.cs`, `src/Arronix.Client/wwwroot/js/contract-store.js`,
  `src/Arronix.Client/wwwroot/service-worker.published.js`, `src/Arronix.Client/Pages/ContractsPage.razor`,
  `src/Arronix.Client/Services/DescriptorCache.cs`, `src/Arronix.Plugins/Registry/ClientContractCatalog.cs`,
  `src/Arronix.Host/Health/ClientContractHealthContributor.cs`, `src/Arronix.Api/Endpoints/ClientContractEndpoints.cs`,
  `src/Arronix.Abstractions/Wire/ClientContractManifest.cs`.
- Tests: `src/Arronix.Client.Tests/Contracts/*.cs`, `src/Arronix.Plugins.Tests/Registry/ClientFacetResolverTests.cs`,
  `src/Arronix.Host.Tests/Runtime/PackagedClientContractTests.cs`.
- Harness: `eng/proofs/g07-client-contracts.sh`, `eng/proofs/g07-read-manifest.py`.
