# Typed media execution roadmap

**Status:** active

**Current gate:** G07 — prove exact typed media loading in Blazor

**Baseline:** R00 at commit `018e2b0d1` (`Checkpoint typed media SDK and Movies migration`)

## Purpose

This is the dependency-ordered route from the current typed-Movies checkpoint to the Arronix product
described by the [typed-media north star](typed-media-north-star.md). It turns the remaining work into
bounded gates which can be taken one at a time without losing the whole-system destination.

The documents have distinct jobs:

- [`../../CONTEXT.md`](../../CONTEXT.md) states what is true now and names the active gate.
- [`../../INTERFACE.md`](../../INTERFACE.md) states the current external semantic boundary.
- [`../owner-ledger.md`](../owner-ledger.md) preserves controlling owner intent and unresolved decisions.
- [`typed-media-north-star.md`](typed-media-north-star.md) describes the intended authoring model.
- this roadmap controls execution order and the proof required to advance.
- the other subsystem design documents are research inputs. They are not executable plans and must be
  revalidated against the four documents above before implementation; several still describe superseded
  evidence, quality-ladder, builder, or experimental-contract approaches.

## Destination

The migration is complete only when all of these are true together:

1. An independent extension author can define a complete media kind and its paired providers with ordinary
   typed CLR contracts, stating each owned relationship once and knowing nothing about Host erasure,
   generated binding SPI, or Client projection internals.
2. Media, format, language, and provider packages are independently installable while sharing exactly one
   compatible CLR identity for every common domain type in Host and the Blazor client.
3. Movies is one complete production vertical from catalog discovery through durable library state,
   interpretation, matching, policy, acquisition, download, import, naming, actions, workbenches, API, and
   Client consumption.
4. Television, Music, and Books each complete the same supported vertical. Television proves set coverage,
   nested units, and non-trivial file cardinality; Music and Books prove the common model is not merely the
   video family renamed.
5. Format and language extensions contribute their own recognition, probing, comparison, and defaults.
   Media extensions contain only media-owned structure and genuine policy differences. Provider and vendor
   vocabulary remains in provider packages.
6. Observable *arr behaviour and Scene/PTP ecosystem semantics are either preserved by executable tests or
   recorded as explicit, owner-approved divergences. A skip, descriptor, generated getter, or uncalled
   implementation is never counted as feature completion.
7. Transitional imperative media shapes, scalar quality ladders, `ParsedRelease` adapters, public binding
   machinery, duplicated manifest schema, and parallel kind-blind semantic implementations are gone.

## How to execute the roadmap

- Only one gate is active at a time. Work on a later gate may be researched, but it is not merged as a
  substitute for closing the active gate.
- A gate may use several small commits. If it grows beyond one coherent outcome, split it into numbered
  sub-gates before implementation; do not silently broaden it.
- Every gate ends with its production-path proof, the full solution build and test result, the observed skip
  count, and any relevant architecture or packaged-plugin tests. New or unregistered skips fail the gate.
- Update `CONTEXT.md` when the current truth or active gate changes. Update `HISTORY.md` for a material
  decision and `INTERFACE.md` in the same change as any public semantic boundary change.
- Keep the compatibility matrix live throughout the work. Restoring a regression, replacing it with an
  ownership-correct clean-room test, and accepting a deliberate divergence are three different outcomes.
- Do not make an early gate appear complete by routing the new type through a legacy semantic engine. An
  explicit temporary adapter may remain while its removal gate is recorded, but it proves only migration
  continuity.
- The roadmap has no schedule estimates. Dependencies and acceptance evidence, rather than apparent local
  convenience, decide the order.

Some gates deliberately contain an owner semantic checkpoint. In particular, the approved four-part Video
quality starting point, the later channel-relative direct-copy correction, and the current larger
`VideoLineage` implementation must be reconciled rather than treating every intermediate ontology as approved.
The transfer artifact at G22 also changes a public provider contract. The earlier direction that clients may
themselves be plugins remains real but underspecified; no `IClient` lifecycle or trust model is invented here.

## Sequence at a glance

| Phase | Gates | Proof gained |
| --- | --- | --- |
| 0. Honest foundation | G01–G03 | Continuous proof, real package admission, and one CLR type identity |
| 1. Consumable SDK | G04–G07B | Typed provider pairing, hidden binding SPI, external package proof, typed Client loading, and a narrow durable catalog slice |
| 2. Typed release decisions | G08–G12 | Trustworthy compatibility evidence and an executed typed interpretation/match/policy path |
| 3. Cross-media pressure | G13–G17A | TV, Music, and Books constrain the common abstractions before the cross-media schema and defaults freeze |
| 4. Durable Host/media verticals | G18–G26C | Restart-safe state, acquisition, download, import, operations, workbenches, API, and Client across all four media kinds |
| 5. Closure and replacement proof | G27–G32 | Compatibility burn-down, external SDK proof, one authoring path, providers, and operability |

The strict dependency spine is:

```text
continuous proof
  -> real package admission
  -> shared package and CLR identity
  -> typed provider pairing
  -> typed interpretation, matching and policy
  -> TV and non-video pressure tests
  -> durable typed state
  -> acquisition, transfer and import
  -> actions, workbenches, API and Client
  -> compatibility closure
  -> complete external-author proof
  -> legacy removal and external-proof rerun
  -> production providers, operability and replacement readiness
```

## R00 — Recorded baseline

**Status:** complete.

The current checkpoint contains the typed `MediaType<TItem,TTarget,TRelease,TParser>` direction, the common
item and collection shape, generated closed projections, derived standard action declarations, the nominal
`Movie` closure, `ReleaseTarget<Movie>`, `Release<Video>`, and the current Movies parser/policy seams.

Baseline proof recorded on 2026-08-21:

- commit `018e2b0d1`;
- 1,998 enabled tests passed;
- 302 tests skipped: 301 Movies cases and one architecture case;
- zero failures;
- the real packaged Movies extension still fails the complete loader path, so this checkpoint proves neither
  an installable SDK nor a production Movies vertical.

## Phase 0 — Honest foundation

### G01 — Put the proof rails in CI

**Status:** complete.

**Outcome:** the clean-repository result and every known omission are continuously visible.

Implement:

- restore, Release build with warnings treated as errors, the full test suite, architecture tests, generator
  tests, and real packaged-plugin tests in repository CI;
- create a machine-readable skipped-test ledger containing stable case identity, current owner, reason,
  semantic requirement, expected outcome, evidence source, and the gate expected to restore, replace, or
  disposition it;
- create the feature-coverage and ecosystem-compatibility matrix skeleton, separating source-*arr regression,
  generated clean-room cases, Scene/PTP field observations, platform/filesystem behaviour, and deliberate
  Arronix differences;
- add stable semantic IDs, explicit one-to-one or one-to-many replacement mappings, and sentinel/mutation
  checks so renaming a parameterized case, weakening an assertion, or replacing a body with a vacuous pass
  cannot evade the ledger;
- add ratchets so the total skip count cannot rise and a semantic requirement cannot disappear without an
  explicit outcome. A candidate divergence awaiting approval remains missing proof.

Exit gate:

- a clean clone produces a reproducible green run;
- CI reports enabled, passed, failed, and skipped counts explicitly;
- all 302 current skips are registered and no new unregistered skip can merge;
- a deliberately removed, weakened, renamed, or newly skipped fixture makes the gate fail.

Completion proof recorded on 2026-08-21:

- exact .NET 11 Preview 7 SDK `11.0.100-preview.7.26381.103` is pinned, all 25 solution projects restore from
  checked-in lock files, and the Release build treats warnings as errors;
- the one-command rail discovers and runs all 11 test projects: 2,112 passed, 302 skipped, zero failed, and
  zero inconclusive from 2,414 total cases;
- the compatibility ledger contains 302 stable cases covering 129 semantic requirements and 12 provenance
  sources, with no replacement or divergence falsely counted as proof;
- the ratchet mutation suite and four generator tests exercise malformed inputs, prior-ledger rewriting,
  coordinated fixture weakening, disappearance, unregistered skips, replacement evidence, deterministic
  generation, and representative executable generated shapes;
- the real packaged Movies output is driven through the complete loader and proves the current late token
  agreement quarantine plus atomic typed-kind withdrawal rather than bypassing it through a direct binder;
- every discovered test project must emit a non-empty NUnit document, CI compares with the PR base or pre-push
  ledger, and the same-build solution binlog is the practical authoritative record of actual `Csc` sources,
  embedded inputs, warning policy, and configuration;
- each registered execution binds its exact NUnit leaf to the declared CLR method in the exact executed assembly;
  the matching Portable PDB must bind that method to primary and support documents whose embedded source bytes
  exactly match the locked files, including async and iterator state machines;
- the eight-column append-only sentinel registry contains only three durable invariants. The packaged Movies
  quarantine remains an ordinary integration test because G02 is expected to change that transient outcome;
- the four published Movie parser rows `t012`, `t013`, `t023`, and `t027` live in a dedicated immutable support
  document rather than the ordinary representative corpus; and
- intentional binding, compiler-input, source-remapping, support-source, sentinel-removal, and skip-regression
  mutations exit non-zero. These checks provide practical same-build provenance rather than cryptographic or
  hermetic-build causality.

### G02 — Make the real packaged Movies extension load

**Status:** complete.

**Outcome:** the actual Movies and Video build outputs survive discovery, admission, typed binding,
activation, publication, and withdrawal through the real plugin pipeline.

Implement:

- package the real first-party outputs in an integration fixture and invoke the complete loader and Host
  admission path; direct `MediaTypeBinder` calls are not acceptance evidence;
- make post-admission token agreement consume the admitted typed projection instead of looking only for the
  legacy `IMediaShapeProvider` path;
- remove manually duplicated media schema, token, policy, and action transcripts from `plugin.json`, or
  generate any inventory genuinely needed before code load from the typed source;
- retain current manifest ownership of package identity, contract compatibility, and admission-relevant
  capabilities; leave explicit package dependencies to G03 and operator access grants to runtime configuration;
- keep Host preparation invisible through every late validation, then publish the complete attempt atomically
  or roll it back without visibility.

Exit gate:

- Movies finishes `Active` and `movies` is present in `MediaKindRegistry`;
- every derived token is claimed once by its real owner;
- a deliberately invalid late check quarantines the extension without ever exposing its prepared kind,
  tokens, or registrations;
- the integration test fails if a direct-binder shortcut is reintroduced;
- the manifest is not a second hand-maintained media definition.

Completion proof recorded on 2026-08-24:

- `IPluginAdmissionCheck.Prepare` returns a `PluginAdmissionResult` carrying one exact attempt receipt and an
  `AdmittedInventory`: one entry per `MediaKindId` Host is prepared to register, each holding that kind's own
  derived `NamingToken` collection read back off its `RegisteredMediaKind` rather than derived a second time;
- late declaration agreement, scheduled-job kind association, cross-installation duplicate-kind checking, and
  token ownership consume that inventory. Legacy `IMediaShapeProvider` registrations remain transitional
  inputs to the same mandatory Host admission path;
- token ownership is claimed per admitted kind through `TokenClaimRequest`, so an extension supplying several
  kinds cannot claim the cross product of its kinds and its combined vocabulary;
- no prepared value is visible during late checks. One shared gate publishes the per-kind token plan, exact
  Host attempt, and Active runtime result only after every check passes; refusal and exception release the
  complete attempt, and a failed reload cannot replace an existing Active authority;
- Host stop runs after scheduler cancellation and drain, atomically withdraws the exact Host/token/runtime
  receipt, records a root-free `Stopped` result, and then disposes owned objects in deterministic
  async-preferred order. A genuine deadline overrun retains the plugin Active rather than unloading its code;
- manifest validation permits a media extension to omit derivable kinds and tokens. Requested capabilities
  remain explicit manifest-owned least-privilege requests; operator access grants remain Host configuration;
- the real packaged Movies output reaches `Active`, publishes `movies`, owns exactly its derived tokens once
  each, and proves its admitted item type resolved inside the extension's own `PluginLoadContext`. A planted
  conflicting token claim and a restaged manifest declaring an unsupplied kind each quarantine the package
  and leave nothing behind;
- `src/Arronix.Plugin.Movies/plugin.json` no longer contains `mediaKinds`, `identifiers`, `tokens`, or
  `policies`, and has never contained `actions`. It carries manifest schema version, package identity,
  operator-facing name, version, description, the Arronix contract range, the entry assembly, and explicit
  capability grants. The manifest mirror fixture is replaced by one proving those derived facts are absent
  and that the manifest carries nothing outside what it owns; and
- the separately packaged G02 fixture proves a real kind, provider, language, job, health contribution,
  runtime-envelope propagation, late rollback, scheduler drain, overrun deferral, exact cleanup order,
  stopped-root removal, and containment of a throwing load-context unloading handler; and
- the full rail reports 2,168 passed, 302 registered skips, zero failed, and zero inconclusive from 2,470
  cases across all 11 test projects; compatibility and required-sentinel ratchets pass.

Deliberately out of scope: the Books, Music, and Television manifests keep their `mediaKinds`, `identifiers`,
and `tokens`. Those declarations belong to the legacy media paths those kinds still use and are removed with
their conversions, not by this gate.

This gate does **not** claim the parser, selector, providers, persistence, or Client vertical is complete.

### G03 — Define package dependencies and one CLR type identity

**Status:** complete.

**Outcome:** independently shipped media, format, language, and provider packages can close generic contracts
over the exact same domain types.

Implement:

- add explicit package dependency identifiers, compatible version ranges, deterministic load ordering, and
  cycle/missing/incompatible diagnostics;
- decide the authoritative client-safe contract facet versus isolated executable plugin facets. Sharing
  `Movie` or `Video` must not accidentally globalize parser, module, provider, or other executable code which
  has a different isolation, security, update, or unload lifecycle;
- resolve shared media and format contract assemblies from the already admitted dependency rather than
  loading private duplicates into each plugin load context;
- define dependency-aware quarantine, withdrawal, and unload closure semantics;
- enforce assembly identity and package integrity without reconstructing a type from a media-kind string;
- replace the obsolete assumption that extensions form an edgeless dependency graph.

Exit gate:

- an independent fixture provider implementing `ICataloger<Movie>` observes a `typeof(Movie)` reference equal
  to the registered Movies item type;
- two independent fixture dependants share the same admitted Video contract identity; real Television must
  prove the same path at G13 without expanding this gate into its conversion;
- contract-facet bytes, assembly/version/signing identity, dependency closure, and executable-facet isolation
  are deterministic inputs to later Client loading;
- missing dependency, incompatible range, cycle, and duplicate-copy attempts fail before activation with
  actionable diagnostics;
- an admitted dependency cannot be unloaded while dependants remain active, and withdrawal is deterministic.

Completion proof recorded on 2026-08-24:

- one immutable `InstalledPackage` is the canonical installed-package declaration, carrying exact package
  identity, version, source, folder, optional entry assembly, published shared contract assemblies, typed
  direct requirements and a closed availability state. Every collection is a read-only copy, the mutable
  declaration is not retained, and the object has reference identity only, so two installation attempts of
  one identifier can never compare equal;
- one `PackageDependencyResolver` produces one immutable `ResolvedPackageGraph`. There is no edgeless
  fallback: a host with no resolution authority does not compose. Duplicate, missing, incompatible, cyclic,
  disabled and transitively ineligible packages are refused with deterministic member-path diagnostics; the
  whole rendered result is invariant over 5,040 permutations of a mixed installation, 720 of a cyclic one
  and 720 of one carrying an unavailable package;
- operator-disabled is a typed closed state entering resolution before any assembly is opened, and an
  undefined state value is refused rather than treated as another kind of unavailability. A disabled package
  keeps `PluginDisabled` and its own defect list; its dependants are refused with the real root cause named;
- `Arronix.Format.Video` ships as the installed package `arronix.format.video` — one shared contract
  assembly, no entry assembly, no capability. Movies publishes `Arronix.Media.Movies.dll` and requires the
  video package; neither Movies nor Television carries a private copy of it;
- the installation admits each declared contract once into one Host-owned collectible context, staged and
  metadata-validated in graph order and loaded as one transaction. A publisher failing at the real load
  boundary takes its whole set with it, and every package that required it is refused over the declared
  package edge whether or not its own metadata named the failed assemblies;
- global admission is not global visibility: a package binds only to contracts published by itself or by a
  package in its exact transitive dependency closure, enforced in metadata before load and by a
  package-scoped resolver at runtime. An out-of-closure request is refused rather than resolved from a
  private copy or the default context;
- a separately packaged provider implements `ICataloger<Movie>` and its closed `Movie` is reference-equal to
  the registered movies kind's item type; two independently packaged dependants receive the same admitted
  `Video` assembly and type; the contract-only video package is rooted and cannot be released while a
  dependant lives; teardown is observed to release a dependant's code before its dependency's, and a clean
  stop is not reported until the contract context is released;
- a package carrying a private copy of an admitted contract — including a different build of the same
  identity — is refused with both module identifiers and both content hashes;
- a stale assembly planted in a project's real build output cannot enter a staged payload through the actual
  publish, and inverting that staging to a recursive copy makes the proof fail;
- the full rail reports 2,536 passed, 302 registered skips, zero failed and zero inconclusive from 2,838
  cases across 11 test projects; compatibility and required-sentinel ratchets pass.

Deliberately not claimed: one CLR identity across Host and dynamically loaded Client code (G07 owns the
browser half), signing and package integrity, side-by-side contract versions, and reload involving shared
contracts. Television declares its video dependency and its payload is proved free of private copies, but its
runtime proof is deferred to G13 rather than fabricated here.

## Phase 1 — Make the SDK genuinely consumable

### G04 — Close the typed provider-pairing contract

**Outcome:** a provider states its media item relationship once, and catalog materialization plus durable
identity have one explicitly approved semantic contract.

Resolve and implement:

- who creates `MediaItemId`, how catalog authority and identity collision are resolved, and how an
  `ICataloger<Movie>` supplies the exact Movie-shaped facts when a durable key is not yet known;
- an approved typed construction/materialization contract. A candidate/draft wrapper, a factory, or a changed
  identity lifecycle may be considered, but this roadmap does not pre-approve one or create a parallel schema;
- preservation of the cataloger's exact media-shaped contract and ordinary CLR types throughout materialization;
- registration which infers or generates the already-closed `ICataloger<TItem>` or `ICurator<TItem>` pairing
  rather than repeating `TItem` in a second generic argument;
- the closed registration as the single authority for provider family and the Host-qualified registration as
  the single authority for provider identity, removing implementation- and descriptor-side restatements
  rather than adding agreement checks between several supposed authorities;
- admission against the exact active media type, while retaining the non-generic cataloger floor only for
  genuinely kind-blind external-identifier recognition.

Landed: both halves. A provider names its item type once, in the contract it closes; family, item type and
identity each have one authority; and admission refuses an incoherent or unpairable provider contract before
any implementation in the package is constructed. Durable identity was settled by the owner on 2026-08-25 —
a cataloger owns catalog identity in its own declared scheme, a curator returns references rather than
items, and Host alone assigns `MediaItemId` at materialization, as host state scoped by kind and level. The
invariant and its current limits are `docs/research/g04/media-item-identity.md`; the materialization seam is
`CatalogDispatcher`. G04 is closed. G05 now exercises the seam with a production provider package;
persistence remains later work because no part of the store has it.

Exit gate:

- a cataloger or curator author names `Movie` once;
- durable identity has one documented authority, collision, merge, and retry rule reflected in `INTERFACE.md`;
- Host can produce a fully valid `Movie` without a public field bag or temporarily invalid domain object;
- any approved construction type is an exact typed lifecycle boundary, not a second field vocabulary;
- admission rejects a provider whose closed item contract has no active matching media package.

### G05 — Ship one independent typed cataloger and curator proof

**Outcome:** media-shaped provider data is supplied by a separately installed vendor/provider package, not
by Movies or Host.

Implement:

- one independently packaged cataloger with realistic HTTP-boundary tests and one curator/discovery source;
- provider code grouped by vendor/reason-for-change, with shared vendor transport and separate typed media
  bindings where one vendor serves several media kinds;
- provider-owned settings, DTOs, identity namespace, marker recognition, authority, and transport behaviour;
- constrained Host activation through the admitted capability-scoped plugin context, never through Host DI;
- identifier recognition which is local, deterministic, and performs no network call.

TMDb is a sensible first concrete Movies cataloger, but nothing in the provider contract, Movies, Host, or
Abstractions may name it. A test provider may prove mechanics, but must not be reported as production
provider coverage.

Landed: `Arronix.Provider.Tmdb` is independently versioned and depends on the Movies package while compiling
only against Abstractions and the Movies domain. Its cataloger returns the installed package's exact `Movie`,
its curator returns TMDb catalog references, and Host assigns durable identity when those values materialize.
The real loader proof covers constrained HTTP invocation, shared Movie identity, repeat materialization,
curator-to-catalog resolution, marker readings reaching the Movie parser, and isolated quarantine when Movies
is absent or incompatible. Architecture gates keep all vendor vocabulary in the provider package. See
`docs/research/g05/tmdb-provider-pressure-test.md`. G05 is closed; credentialed live coverage remains G30.

Exit gate:

- both packages are installed and versioned separately, then activate against their declared Movies package
  dependency while returning the approved exact typed Movie shape;
- absence or incompatibility of Movies quarantines only the affected typed binding with an actionable reason;
- owned embedded identifiers become typed readings consumed by the Movie parser;
- Movies, Host, Video, and Abstractions contain no vendor endpoint, DTO, credential, or marker vocabulary;
- fake-transport tests exercise the real provider implementation rather than replacing its domain boundary.

### G06 — Separate authoring SDK from generated and Host binding SPI

**Outcome:** the public surface a normal extension author sees contains semantic contracts only.

**Status:** complete on the `0.9` contract line. `Arronix.Sdk` is a packaging-only author dependency over
Abstractions plus the analyzer asset; generated/capture/erased mechanics are hidden from ordinary completion
lists and concrete semantic values; `ARX1003` reports a non-partial media declaration; architecture tests
enforce the dependency and surface boundary; and a package-only external project restores and compiles from
the packed SDK with no repository reference. The retained end-to-end external install/browser proof remains
G07A rather than being implied by this package-boundary proof.

Implement:

- move, hide, generate, or mark as advanced the `CompiledShapes`, capture visitors, erased registrations,
  `Type` carriers, expression reducers, and heterogeneous binding mechanics required across assemblies;
- retain compile-time generation and one-way Host erasure without asking the author to implement a generated
  abstract member or understand the bridge;
- package the generator/analyzers and any binding companion correctly for ordinary package references;
- add SDK architecture tests which reject Host, Client, runtime-erasure, and internal projection dependencies.

Exit gate:

- an extension package references only supported SDK plus chosen format/language packages;
- its media module performs one media registration and providers perform one closed registration each;
- public authoring examples contain no binding visitor, descriptor replay, reflection convention, or manual
  wire schema;
- compiler diagnostics explain missing owned declarations at the authoring site.

### G07 — Prove exact typed media loading in Blazor

**Outcome:** the generic Client can consume dynamically installed media shapes without reducing them to a
string bag or statically referencing Movies/Video.

Implement:

- a package identity, exact content hash, assembly/version/signing identity, dependency-closure hash,
  generated-metadata hash, and cache/update protocol for client-safe media contract assemblies;
- exact typed deserialization and rendering of a Movie plus its common and owner-shaped values;
- trimming/AOT-safe discovery using generated metadata rather than property reflection;
- install, update, removal, stale-tab, and incompatible-client behaviour;
- a clear package-facet rule: provider/server-only assemblies are not downloaded to the browser, while the
  CLR assembly defining the shared domain type retains exactly one compatible identity.

Exit gate:

- a hosted server exposes real admitted package metadata and an actual WASM browser loads the package from a
  clean cache over serialized network payloads;
- assembly-reference inspection proves that the Client build has no static Movies or Video project/package
  dependency, yet it renders a typed Movie, artwork, ratings, lifecycle, and collections;
- server and Client agree on the exact contract bytes, assembly/signing identity, dependency closure,
  generated metadata, and projection schema hash;
- generic descriptors remain one-way presentation/wire data rather than an alternative media definition;
- update, removal, stale-cache, and incompatibility behaviour is exercised in the real browser and fails
  visibly instead of silently projecting partial data.

The gate is larger than one coherent outcome, so it is split into the three numbered sub-gates below. They
are dependency-ordered: nothing can be deserialized before the exact assembly is loaded, and nothing can be
loaded before the server publishes the identity it must be loaded against. G07 is closed only when all three
are closed; G07A and G07B remain later gates and none of their work is folded into these.

#### G07.1 — Publish, cache, and load the exact client-safe contract package

**Status:** complete. The real hosted server publishes and serves the facet content-addressed, and a real
browser running the ordinary published client loads it from a clean store, reuses a warm store without
refetching a byte, and refuses a foreign contract line, one flipped byte, a falsified identity, a falsified
build and a malformed manifest — in every case before the runtime is handed the payload, with nothing
projected. Every numbered exit condition below is met.

The shipping publish disables trimming, which is recorded technical debt rather than an open exit condition:
this gate never required an optimized publish, and the client the browser matrix proves is the client an
ordinary `dotnet publish` produces. See `docs/research/g07/client-contract-loading.md`.

Implement:

- a declared client-safe facet on an installed package, validated as a subset of its shared contract
  assemblies, so that an entry assembly or an undeclared file can never be served to a browser;
- a projection of the running installation carrying, per client-safe assembly, its exact SHA-256 content
  hash, its assembly/version/culture/public-key identity, and its module version identifier, and per
  package, its transitive client dependency closure and one closure hash over it;
- content-addressed serving of that metadata and those bytes through the hosted server path, from the
  admitted bytes rather than from a re-read of a file the package owns;
- a browser-side contract cache keyed by content hash, and a loader which verifies the hash it received,
  loads the exact assembly, and proves the loaded module identifier against the published one;
- one compatible contract identity between server and browser, with an incompatible client refused visibly
  and completely rather than projecting part of an installation.

Exit gate:

- a hosted server publishes the metadata from real admitted packages and refuses every file outside the
  declared client-safe facet;
- an actual WASM browser loads the closure from a clean cache over serialized network payloads, and a
  second navigation is served from that cache without refetching the bytes;
- content hash, assembly identity, and module version identifier agree between server and browser;
- the Client build declares no static Movies or Video dependency;
- an incompatible contract identity fails visibly with nothing projected;
- nothing reaches the browser runtime until the whole required closure has been verified from its own bytes,
  and an entry that verified without being loaded says so.

#### G07.2 — Generated client metadata, typed deserialization, and typed rendering

**Status:** implemented; open pending independent review. Every numbered exit condition below is met against
the real hosted server and a real browser, and the observed run is recorded in
`docs/research/g07/client-payload-projection.md`. The gate is not marked complete here because the review of
that work has not reported; the remaining gate is that review, not any unbuilt part of the outcome.

**Outcome:** the browser reads an actual typed Movie graph out of the assembly it loaded, and renders its
common and Movies-owned values, using generated metadata rather than property reflection.

Implement:

- generated client contract metadata in the client-safe assembly: a declared entry point, trimming/AOT-safe
  serialization metadata for the item graph, and a projection schema, with a generated-metadata hash and a
  projection-schema hash the server publishes and the browser agrees on;
- typed deserialization of a Movie graph, and rendering of common plus Movies-owned values including
  artwork, ratings, lifecycle and status, and collections;
- the serialized fixture G07A later reuses.

Exit gate:

- discovery, deserialization, and rendering use generated metadata; the Client performs no open-ended
  property reflection over a loaded assembly;
- generated-metadata and projection-schema hashes agree between server and browser;
- the browser renders a typed Movie with artwork, ratings, lifecycle/status, and collections, with no static
  Movies or Video dependency in the Client build;
- generic descriptors remain one-way presentation/wire data rather than an alternative media definition.

Beyond those conditions, this gate settled three rules the outcome turned out to need. A projection is
contract-produced output, so it is proved before it is rendered — iteratively, bounded in depth, cycles,
total values and total rendered characters — and what a consumer receives is what that proof captured, not
the graph it was captured from. A payload failure never rewrites the installation report the loader already
decided. And the Client spells no media kind and no installation's own path, including no default payload
address, which is what lets the same unchanged panel take an external consumer's contract at G07A.

#### G07.3 — Contract cache lifecycle in the browser

**Status:** not started.

**Outcome:** installing, updating, and removing a package, and holding a tab open across those changes, all
behave predictably in the browser and fail visibly rather than projecting partial data.

Implement:

- update to a new content hash, removal of an uninstalled package, and eviction of the entries neither
  names;
- stale-cache and stale-tab behaviour, including the honest consequence that an assembly already loaded into
  a browser page cannot be unloaded from it.

Exit gate:

- update, removal, stale cache, and stale tab are each exercised in the real browser;
- a tab holding a withdrawn contract refuses to project it and says so, rather than continuing to render
  from a contract the host no longer admits.

### G07A — Prove a package-only external consumer early

**Outcome:** package assets, analyzer packaging, generated accessibility, and admission work without a source
or project-reference escape hatch.

Implement a small consumer outside the Arronix solution using only packed SDK, generator, format, and language
artifacts. It defines a trivial typed media kind and one typed provider, packages them, installs them into an
unmodified Host, and loads its client-safe typed contract in the unmodified browser Client using the serialized
G07 fixture. Provider-result ingestion and durable item rendering remain G19/G28 work.

Exit gate:

- the consumer has no repository project reference, source include, `InternalsVisibleTo`, Host/Client
  dependency, or manual generated bridge;
- restore, compile, package, admission, exact type identity, and browser rendering work from a clean package
  cache, with the browser proof limited to contract loading and fixture projection;
- normal author-facing diagnostics identify any incomplete declaration;
- this smoke proof is retained permanently, while G28 later proves a complete independent vertical.

Do not add a temporary external item source, test-only production endpoint, or premature generic catalog
workflow merely to make this smoke test render provider results.

### G07B — Persist one narrow Movies catalog vertical

**Outcome:** provider pairing, identity materialization, and typed item queries are proven against real durable
state before the same assumptions are copied into three more media kinds.

Implement only the narrow EF Core/LINQ slice needed for:

- configured provider definitions;
- typed Movie catalog rows and external-identity authority/merge behaviour;
- basic library presence and user monitoring facets;
- catalog search/fetch, add, refresh, restart, API browse, and typed Client browse.

Exit gate:

- a real G05 provider result becomes a valid Movie, survives process restart, refreshes provider-owned facts,
  and preserves user-owned state;
- identity collision, redirect, deleted-record, and retry behaviour match the G04 contract;
- `HostItemSource` no longer returns an empty placeholder for Movies;
- there is no raw SQL, provider-shaped persistence API, silent in-memory fallback, or temporary per-kind item
  source.

Do not design cross-media groups, unit/file links, profiles, operation records, or import state here. G13–G17A
must constrain those relationships before G18 hardens the shared schema.

## Phase 2 — Execute typed release decisions

### G08 — Make compatibility evidence trustworthy enough to drive the rewrite

**Outcome:** every currently skipped Movies case and every compatibility claim has a known evidentiary status.

Implement:

- classify the 301 Movies skips as restorations, ownership-correct clean-room replacements, explicit candidate
  divergences awaiting approval, or still-blocked evidence gaps;
- separate Movie grammar, Video vocabulary, language rules, catalog-owned identifiers, release-envelope facts,
  and platform/filesystem behaviour so each case moves to its real owner;
- pin the source *arr repositories and revisions, inventory their feature families and observable behaviours,
  define an evidence refresh policy, and obtain an independent completeness audit so a green but incomplete
  matrix cannot satisfy replacement readiness;
- complete the owed independent Spec Editor pass, have a clean implementer re-derive the generator, check in
  reproducible tooling and regenerated hashes, and version/review the corpus schema against the current Movie
  and Video model;
- enforce the mechanical clean-room wall: role/access attestations, quarantined inputs, a sanitization gate,
  independent gatekeeper fingerprint/similarity review, and proof that no superseded corpus row was simply
  reproduced. Revalidate and adopt any still-correct controls from the clean-room plan rather than treating that
  older document as executable by default;
- keep generated clean-room cases and observed Scene/PTP field releases as separate evidence sets;
- record provenance by evidence class: upstream regression uses repository/revision/test identity; generated
  evidence uses sanitized spec rule, generator version, seed, and schema; field observation uses source class,
  date, and bounded provenance; deliberate Arronix evidence uses the owner decision and rationale;
- define non-zero, owner-reviewed Scene/PTP field-corpus criteria covering provenance, observation date, source
  diversity, structural categories, and a bounded representative set independent of generated grammar;
- add representative cross-kind negative-routing and hostile-input cases.

Exit gate:

- every one of the 301 cases has an explicit disposition and target owner;
- the source feature inventory is revision-pinned and independently checked for completeness;
- the clean-room corpus can be regenerated by the independent process with matching schema/version/hashes;
- independent similarity/fingerprint and sanitization gates pass and no superseded row is reproduced;
- every required negative class is independently specified/generated and assigned to the G10 runtime slice,
  where 100% rejection becomes mandatory;
- no same-session generated corpus is represented as independent clean-room or field evidence;
- the field corpus is non-empty, provenance-bearing, and described only as compatibility with that versioned
  observed set, never as an official or universal Scene/PTP ontology;
- a compatibility report can distinguish implemented parity, intentional difference, and missing proof;
- the ledger is sufficient to reject a locally elegant rewrite which loses established behaviour.

This gate maps and validates the target. It need not make all 301 cases pass yet; closure occurs continuously
and is finally enforced at G27.

G09–G17A are provisional contract gates. Before each begins, its required compatibility slice is explicitly
assigned. Every requirement in that slice must pass executable evidence or have an owner-approved divergence
with a permanent regression test; only requirements explicitly assigned to a later owner/gate may remain open.
Illustrative examples are never sufficient, and zero new regressions are allowed. The public contracts are not
stable until Television, Music, and Books have fed their counterexamples back through the same gates.

### G09 — Define typed interpretation composition

**Outcome:** one release reading composes common envelope facts with catalog-, language-, media-, and
format-owned contributions without copying vocabularies between owners.

Implement:

- a typed composition boundary for catalog external-ID readings, language mechanics, media structure, and
  each declared `FormatUse<TRepresentation>` recognizer;
- an explicit ownership/composition decision for community source/distributor release tags; they must be
  independently contributable and may not become a Host, Movies, or universal hard-coded vocabulary;
- common release-envelope facts such as source listing identity, release group, hashes, and parse diagnostics
  in an appropriate common sidecar;
- owner-attributed spans, provenance, ambiguity, and rejection diagnostics;
- explicit regex/time-budget failure diagnostics rather than treating a timeout as an ordinary mismatch;
- preservation of `ReleaseParseResult<TRelease>` through Host's one-way erasure boundary.

Exit gate:

- the same input can be traced to the owner which established each fact;
- Movies contains no codec, container, HDR, audio, language, distributor, or catalog-vendor vocabulary;
- Host contains no Video vocabulary;
- absence and ambiguity remain typed unknowns; there is no universal `Evidence<T>` wrapper, global evidence
  ranking, or string fact bag;
- every case in G09's assigned contribution ownership/provenance slice passes or has an owner-approved tested
  divergence; an unresolved case must be reassigned explicitly to a named later owner/gate before G09 closes.

### G10 — Complete Video representation facts and Movie interpretation

**Outcome:** familiar release labels decode into independent observable facts rather than one scalar quality
ladder.

Implement:

- first reconcile the owner-approved `SourceOrigin`/`Fidelity`/optional `Resolution`/`Revision` starting model
  with the later correction that direct copy is relative to an acquired channel artifact, not globally
  untouched. Keep each additional fact only when compatibility evidence requires it and its owner is clear;
- the resulting Video-owned representation facts, which may include observable release channel, acquired
  carrier, acquisition method, transformations after that carrier, resolution/scan, codec/container, dynamic
  range, audio tracks, subtitles, and defects without pretending they form one total order;
- the common revision/edition/envelope facts which are not Video properties;
- channel-relative direct copy: Remux and WEB-DL may copy their acquired carrier without claiming global
  untouched lineage;
- ordered collections for multi-valued tracks and open/unknown values where the ecosystem is not closed;
- Movie-owned title, year, edition, and structural grammar combined with the Video contribution.

Exit gate:

- representative CAM/TS, broadcast, WEB-DL, WEBRip, Remux, BDRip, disc encode, and unknown releases illustrate
  populated `Release<Video>` values with independent facts, while the complete Video/Movie interpretation
  slice in the compatibility ledger executes;
- no term is forced into one misleading `Quality` enum or total order;
- audio tracks and other multi-valued observations survive intact for later policy;
- malformed or unsupported facts remain unknown with diagnostics rather than receiving a favourable default;
- 100% of the independently specified negative-class slice rejects at runtime;
- no requirement assigned to the Video/Movie interpretation slice remains unresolved; any still-open case has
  an explicit later owner/gate and continues to count as missing proof there.

### G11 — Make target construction and matching typed end to end

**Outcome:** matching relates the exact `TItem`, `TTarget`, and `TRelease` instead of flattening releases to a
closed title/year vocabulary.

Implement:

- typed acquisition-target construction from a durable item and current user/library state;
- matching over exact item, target, release, external identity, language-normalized titles, years, editions,
  scope, and ambiguity rules;
- direct `TargetMatch<TTarget>` production with Covered and Missing targets plus explicit disposition;
- cross-kind routing which proves a plausible title for one media kind cannot be claimed by another;
- retained folder/listing provenance and reasoned rejection output.

Exit gate:

- interpreted Movie releases match concrete `ReleaseTarget<Movie>` values;
- exact, ambiguous, wrong-year, wrong-identifier, wrong-kind, and unknown cases have typed dispositions and
  stable reasons;
- no typed Movie path traverses legacy `ParsedRelease`, `ReadingFact`, or `MatchOutcome` semantics;
- the contract has a provisional set-coverage shape ready for Television to validate, not a claim that Movie
  tests have already proven it;
- the complete Movie matching/routing slice executes with no hidden deferrals.

### G12 — Compose declarative profiles, policy, lifecycle, and deterministic selection

**Outcome:** release targeting is a reusable typed constraint/preference algebra, with format facts, media
facts, and user intent retaining their owners.

Implement:

- composable typed hard requirements, lexicographic preferences, bounded facets, cutoffs, and explicit
  unknown handling;
- fixed ownership order: format defaults, media-specific differences, then user requirements/preferences as
  final authority;
- persistence-neutral profile definitions suitable for Movies, TV, Books, and Music;
- a reviewed quality/defect/revision matrix, including `Repack`, `Proper`, `Real`, resolution, channel,
  transformation, and known defects;
- separation of factual lifecycle milestones/stage from time-dependent eligibility and user monitoring policy;
- a closed typed runtime proof of the deterministic `ReleaseSelector` with stable final identity tie-breaking.

The exact fact model and preference matrix are owner semantic approval checkpoints. The framework and
ownership layering can be implemented first; neither the current large ontology nor historical scalar ladders
become approved defaults by surviving in the tree.

Exit gate:

- Movies no longer drives a public policy compiler/builder merely to opt into Video defaults;
- user policy demonstrably overrides media and format defaults;
- a higher-resolution defective or camera release cannot win because resolution happened to be compared first;
- every permutation of provider result order selects the same candidate;
- unknown facts are neither silently admitted nor rewarded;
- the complete classified Movie policy/selection slice executes. G20, not this gate, proves the production
  coordinator calls the selector.

## Phase 3 — Pressure-test before freezing durable shape

### G13 — Convert Television's typed domain and file/unit model

**Outcome:** Television becomes the first structural counterexample to Movies.

Implement:

- typed Series, Episode, and any demonstrated season aggregate/group, plus lifecycle, external identity,
  artwork, ratings, and status shapes using common primitives where their meaning is genuinely shared. The
  pressure test decides whether Season is a durable entity, group, coordinate partition, or derived view;
- explicit item/unit/file cardinality capable of one file covering several episodes and of season or series
  membership without string descriptors;
- a set-shaped acquisition target and coordinate model for series, season, episode, absolute, and aired order;
- generated projections and one typed registration through the real packaged path.

Exit gate:

- nested units and plural groups survive authoring, admission, Host projection, API projection, and typed Client
  rendering in contract/projection tests;
- multi-episode file relationships are representable without a parallel legacy seam;
- real Television and Movies resolve the same admitted Video contract bytes;
- any common primitive discovered here is applied back to Movies rather than hidden in a TV adapter.

This is a structural pressure gate, not a runnable TV product vertical. It must not add a temporary TV item
source or claim restart-safe execution before the shared durable machinery and G26A.

### G14 — Run Television through interpretation, matching, and policy

**Outcome:** the decision model handles sets and coverage, not merely one-item releases.

Implement:

- TV-owned season/episode/pack grammar composed with the same Video and language contributions;
- `Satisfied`, `Partial`, `Superset`, and `Rejected` target coverage, including multi-episode and season packs;
- TV-specific policy differences layered over the same Video defaults and user authority;
- deterministic selection across overlapping packs, existing coverage, missing units, and upgrades.

Exit gate:

- representative episode, multi-episode, season pack, complete-series, anime/absolute, and ambiguous releases
  illustrate typed releases, typed coverage, and deterministic decisions while the complete classified TV
  interpretation/matching/policy slice executes;
- no TV path converts through Movie target semantics, scalar quality, or a private matcher;
- shared decision primitives remain media-neutral after the counterexample;
- every required correction is fed back through G09–G12 and their Movie tests before this gate closes.

### G15 — Remove only mechanically provable duplication after Television

**Outcome:** obvious compiler/Host mechanics are derived without prematurely treating video-family behaviour as
universal media policy.

Derive only relationships whose commonality follows from the platform contract itself:

- closed provider/media registration and package metadata already derivable from types;
- generated projection/binding mechanics and platform-standard action identity;
- common entity/file/group mechanics whose meaning is fixed by their declared generic contract.

Exit gate:

- Movies and Television no longer repeat those mechanics;
- search, naming, matching, query, workbench, summary, browse, and ordering defaults are not promoted merely
  because two video media happen to share them;
- no reduction is achieved with reflection, string keys, marker interfaces, or builder callbacks;
- every broader candidate remains queued for the four-media audit at G17A.

### G16 — Add Music as the multi-file and Audio pressure test

**Outcome:** Audio representation and work/recording/release structure prove the model under a genuinely
multi-file medium.

Implement a typed Music domain covering artists, works/releases, recordings/tracks, editions/pressings where
needed, Audio format composition, grouping, targets, release grammar, policy, naming, and file/unit binding.

Exit gate:

- one album-style acquisition can cover several typed tracks and express independent file relationships for
  later persistence;
- Audio track/codec/channel facts remain format-owned and are not collapsed to a richest scalar value;
- common file, group, target, matching, and selection contracts require no Music-only escape hatch;
- every required correction is fed back through Movies, Television, and G09–G12 before this gate closes.

### G17 — Add Books as the Document and cross-format pressure test

**Outcome:** ebook/document semantics reject video assumptions, while optional audiobook composition tests a
single media domain using more than one format family.

Implement a typed Books domain with its real work/edition/release relationships, Document representation,
catalog pairing, language behaviour, target, parser, matching, policy, naming, grouping, and file binding. If
audiobooks are included, compose the Audio capability proven at G16 rather than putting audio vocabulary in
Books. Decide the semantic owner of standards such as ISBN during this pressure test; a provider may own its
accepted marker spellings and mappings without automatically owning the underlying namespace.

Exit gate:

- a Book/Edition traverses the authoring, package, provider, interpretation, match, and policy contract path;
- Document and Audio facts retain their format owners and cross-format policy remains typed;
- no Video origin, resolution, codec, episode, or one-file assumption leaks into common contracts;
- every required correction is fed back through Movies, Television, Music, and G09–G12 before this gate closes.

### G17A — Derive common defaults and shrink all four media definitions

**Outcome:** the SDK asks each media type only for facts which differ, after both video and non-video
counterexamples exist.

Audit standard workbenches, summaries, browse defaults, safe title ordering, identity search, monitoring,
naming/path safety, query-tier mechanics, matching layers, and file/group/selection declarations. Hoist or
derive only behaviour which retains the same semantics across the proven counterexamples; keep genuine media
differences as ordinary typed overrides.

Exit gate:

- Movies, Television, Music, and Books contain owned deltas rather than Host orchestration transcripts;
- common defaults remain typed and overridable without reflection, string keys, marker interfaces, or builder
  callbacks;
- the full SDK contract suite and every applicable compatibility slice pass after the changes;
- the public common surface remains a candidate until complete vertical and external-author proof.

Beyond the narrow Movies catalog slice at G07B, no cross-media relational schema is frozen before G13–G17A
have constrained item, unit, group, target, release, and file cardinality. Television, Music, and Books use
contract/projection fixtures here; do not add temporary per-kind item sources or report these gates as runnable
verticals.

## Phase 4 — Build durable Host/media verticals

### G18 — Harden durable typed state across the pressure-test shapes

**Outcome:** the narrow G07B store evolves into one restart-safe catalog/library model for Movie, Television,
Music, and Books without predeclaring operation or import semantics.

Implement:

- the selected EF Core/LINQ-only persistence boundary and migrations, revalidating older storage design notes
  against the now-proven Movie, TV, Book, and Music shapes;
- apply the approved catalog entity and external-identity authority/merge/redirect/deletion semantics to each
  exact item shape;
- separate catalog facts, library presence, monitoring intent, lifecycle evaluation, exclusions, groups,
  selections/profiles/cutoffs, provider configuration, and typed item/unit relationships;
- typed ingress and queries; serialized storage representations remain internal implementation details;
- upgrade, corruption, transaction, concurrency, and process-restart behaviour with no silent in-memory
  fallback.

Exit gate:

- typed items for each pressure-test shape can be stored, queried, related, and recovered after restart;
- Host, API, and providers use no raw SQL or provider-shaped public persistence contract;
- factual refresh cannot overwrite user-owned monitoring/profile state;
- TV nested units, Music tracks, Book editions, and their groups survive a fresh process and database;
- operation/acquisition snapshots remain owned by G21 and media-file/import records by G23 rather than being
  guessed into this schema.

### G19 — Complete cross-media catalog, refresh, curator, and group workflows

**Outcome:** the narrow Movie workflow becomes one generic typed capability which can turn provider data for
all four media kinds into durable library state through Host, API, and Client.

Implement:

- catalog search/fetch, identity resolution, add, refresh, redirects, collection synchronization, and curator
  discovery using the typed provider contracts;
- the approved G04 identity and authority/merge behaviour;
- separate persistence of availability and monitoring choices;
- generic presentation of the exact typed item, translations, ratings, artwork, lifecycle, and collections.

Exit gate:

- typed fixture/providers for Movies, Television, Music, and Books can each search, add, restart, and browse the
  exact media item without a per-kind Host workflow;
- refresh updates provider-owned facts while preserving user-owned state;
- collection/curator results retain their exact media relationships;
- Client rendering loads each admitted typed contract through the G07 mechanism.

### G20 — Build the closed typed acquisition coordinator

**Outcome:** one production service reopens the admitted `TItem`, `TTarget`, and `TRelease` relationship once
and executes query, interpretation, matching, policy, and selection.

Implement:

- target construction from durable item/library/profile state;
- semantic query-tier planning and configured indexer dispatch;
- one independently installed production-shaped indexer package/fixture with provider-owned category mapping,
  capability eligibility, configuration, failure, timeout, and ordering tests;
- a Host-attested listing envelope retaining the exact provider implementation, configured definition,
  invocation/session provenance, and source-specific fetch handle rather than trusting a provider-supplied
  `IndexerId` string;
- raw listing interpretation through the media, format, language, and catalog contributions;
- typed target matching, policy/cutoff evaluation, deterministic selection, and retained rejection reasons;
- distinct automatic acquisition and interactive preview modes.

Exit gate:

- a `MediaItemRef` produces either a selected typed `ReleaseOption<ReleaseTarget<Movie>,Release<Video>>` or an
  explicit reasoned no-selection result;
- the service calls installed indexers and the real typed parser/matcher/policy/selector;
- the selected `ReleaseOption` retains its Host-attested source envelope intact for G22;
- the first admissible query tier stops later tiers according to declared semantics;
- no production decision passes through `TypedReleaseParserAdapter`, legacy quality, or a private workbench path.

### G21 — Make standard operations durable and observable

**Outcome:** long-running requests represent real restart-safe work rather than an HTTP `202` fiction.

Implement:

- Host-owned command/operation records with correlation, state, attempts, idempotency, cancellation, failure,
  concurrency, throttling, and restart recovery;
- durable acquisition-decision state containing selected Host-attested listing provenance, target/item references,
  interpreted release and trace, policy/profile version, package identities, downloader definition, and later
  external transfer identity. Use a versioned typed snapshot or sufficient immutable typed source facts for
  deterministic reconstruction, never an undocumented string bag;
- package pinning/compatibility rules for pending operations when a media, format, language, or provider
  dependency is updated or removed;
- media lifecycle/domain events named for what happened (`Imported`, `Upgraded`, `FileDeleted`, and similar),
  with notifiers as optional consumers rather than facts named after notification;
- a transactional outbox and per-destination delivery ledger with after-commit publication, restart recovery,
  retry/backoff, idempotency, poison handling, and explicit provider disable/removal semantics;
- typed internal commands parsed once from wire parameters at the API boundary;
- event/state projection usable by API, Client, and notifiers;
- explicit enqueue failure instead of silent acceptance.

Exit gate:

- an internal durable test command proves enqueue, execution, cancellation, failure, retry, and recovery without
  falsely advertising `Search` before a downloader handoff exists;
- repeated delivery does not duplicate work;
- queued/running/completed/failed state survives restart and is visible to Client;
- provider limits are enforced without coupling scheduler state into domain contracts;
- a selected decision can survive restart and either resume with compatible pinned packages or fail visibly
  when its dependency closure is unavailable;
- committed lifecycle events survive restart and each notifier destination can be reconciled independently
  without repeating the domain transition.

### G22 — Define the transfer artifact and downloader handoff

**Outcome:** the selected listing crosses from an authenticated indexer/source to a compatible downloader
without leaking credentials or leaving one of the current fetch/download contracts dead.

Resolve and implement:

- one explicit transfer artifact model covering authenticated NZB content and torrent/magnet-style references;
- source-owned authenticated fetch, downloader capability/eligibility selection, and idempotent dispatch;
- separately installed downloader implementation, durable external transfer identity, and progress/failure state;
- an idempotency key derived from the durable acquisition and selected listing, plus reconciliation of an
  uncertain downloader response before retry;
- safe repeatable source fetch where transport uncertainty makes an exact-once network claim impossible;
- a durable completed-transfer/import-source envelope containing downloader definition and job identity,
  payload inventory, content identity, Host-accessible or explicitly mapped locations, move/copy/delete rights,
  and containment/symlink validation.

Exit gate:

- the Host-attested source envelope routes fetch to the exact configured source which produced the listing;
- retries yield at most one accepted downloader job for the acquisition, even when fetch or handoff responses
  are lost;
- no source credential is exposed in a URL passed to another plugin;
- failed or repeated handoff remains safe and diagnosable;
- Client observes real transfer progress through durable operation state;
- restart between completion and import, repeated completion, disappeared jobs, remote-path mapping, and
  multi-file payloads all produce deterministic import-source state or explicit recoverable failure;
- `Search` can now enqueue selection plus a real handoff and return an observable operation identifier. It is
  not yet an import-complete operation until G23.

The exact transfer artifact is a semantic contract checkpoint and must be reviewed before freezing it.

### G23 — Complete probing, import, naming, and file persistence

**Outcome:** a completed transfer becomes a validated, correctly named library artifact through a durable
staged import state machine.

Implement:

- format-owned probing and reconciliation between claimed and observed representation facts;
- one typed acquisition-batch plan over the completed payload, accepted release, target, item/unit coverage,
  and existing files. It must express one file to many episodes, many files to many tracks, multipart books,
  partial failure policy, and duplicate-completion idempotency;
- post-probe revalidation of target coverage and every hard policy constraint before filesystem or database
  mutation, with mismatches quarantined for reasoned review;
- media-owned naming templates over typed values plus common path-safety mechanics;
- durable intent, temporary destination, verification, database commit, finalization, and
  reconciliation/compensation around collision-safe move/copy/link behaviour and typed file/unit records;
- rescan/recovery, replacement/upgrade, failed-import, and downloader-completion semantics.

Exit gate:

- a completed Movie download becomes a correctly named file linked to its Movie after restart;
- failure injection at every filesystem/database boundary recovers or compensates after restart without a
  false accepted file record or an abandoned final artifact;
- rescan reconstructs the same association from durable identity and probed facts;
- the Movie import path consumes neither `ParsedRelease` nor ladder quality;
- the current per-file legacy `IImportPipeline` is replaced for typed media or proven unreachable from every
  admitted typed path until its G29 removal.

### G24 — Back every standard action with a real capability

**Outcome:** Host derives a stable common action catalogue and executes every advertised operation through the
capability which owns its side effects.

Implement actions only after their substrate exists:

- monitoring and availability after durable library/profile state;
- direct search after G22;
- missing search and upgrade/cutoff-unmet search after G23 supplies durable file and probed representation state;
- refresh after G19;
- rescan and rename after G23;
- remove and exclude after durable storage/filesystem rules;
- group monitoring and refresh after collection synchronization.

Exit gate:

- no descriptor advertises an operation which returns 501;
- Movies contains no action list, key transcript, or execution callback;
- destructive operations have transaction, filesystem-failure, confirmation, and recovery tests;
- action availability explains a missing runtime capability rather than pretending success.

### G25 — Replace workbench bridges with typed producers and committers

**Outcome:** interactive workflows are typed views over the same capabilities as automatic operations, not a
second kind-blind implementation.

Implement:

- closed typed producer/committer registration attached to the admitted media runtime;
- one projection of typed rows to field/wire values at the API boundary;
- add-from-catalog from catalog capability, interactive search from G20, manual import from G23, and
  complete-group from curator/group semantics;
- tamper-resistant commit identity and restart-safe outcomes.

Exit gate:

- Movies has no standard workbench override merely to restate common workflows;
- proposals retain typed item, target, release, policy result, artwork, and provenance until wire projection;
- committing a row calls the same durable add/acquire/import capability as its standard action;
- `WorkbenchBroker` contains no parallel kind-blind semantic engine.

### G25A — Close the automatic acquisition control loops

**Outcome:** the production path handles the lifecycle around the golden selection/download/import path rather
than requiring a user to push every transition manually.

Implement:

- scheduled wanted, missing, and upgrade/cutoff-unmet detection from durable item/profile/file state;
- availability/delay/pending handling and bounded re-evaluation;
- downloader polling and reconciliation for stalled, disappeared, failed, completed, and externally removed jobs;
- reasoned blocklisting keyed to the failed source/listing/acquisition facts, plus bounded re-search which cannot
  loop forever or immediately select the same rejected release;
- restart-safe continuation through selection, handoff, completion, import, event outbox, and notification
  delivery.

Exit gate:

- scheduled and manual acquisition use the same typed coordinator and durable operation state;
- delay expiry, failure, disappearance, retry, blocklist, and alternative-selection scenarios are deterministic
  under restart and clock control;
- a committed import completes the operation and publishes its domain event exactly once, while recoverable
  failure remains visible and actionable;
- no polling/retry rule is hidden in a provider or media plugin.

### G26 — Close the packaged process-level Movies vertical

**Outcome:** the entire Movies lifecycle works in fresh processes through production Host orchestration, using
real package admission and deterministic provider packages.

The acceptance test must:

1. install real packaged Movies, Video, required language, cataloger, indexer, downloader, and notifier outputs;
2. admit and activate them through the real loader;
3. search a cataloger, add a Movie, and restart;
4. browse the typed item in the generic Client;
5. run interactive and automatic searches;
6. interpret a populated `Release<Video>`, match a typed target, and select deterministically;
7. fetch and dispatch the transfer, simulate completion, probe, name, and import the file;
8. exercise delay, failed/disappeared transfer, blocklist, bounded re-search, and upgrade paths;
9. restart and prove item state, file/unit links, operation history, Client projection, and transactional-outbox
   delivery of committed `Imported`/`Upgraded` events to a deterministic notifier.

Exit gate:

- the process-level test passes under provider-result ordering perturbation, controlled time, restart, and
  failure injection;
- every stage uses the typed production implementation, not a direct binder or legacy semantic shortcut;
- the Movies feature matrix names what is now complete and what still differs from Radarr/ecosystem behaviour;
- API status and Client affordances reflect committed state, not merely queued intent.

This proves the production orchestration and media path. Deterministic providers do not count as production
external-service coverage; that is earned provider by provider at G30.

G26A–G26C each mirror G26's boundary: real admitted media, format, and language packages; separately packaged
deterministic cataloger, indexer, downloader, and notifier implementations; real API and browser Client
traversal; all applicable advertised actions/workbenches; controlled-time, restart, ordering, and failure
proof. A missing common runtime behaviour is implemented in the shared capability and fed back through the
earlier verticals, never hidden in a media-specific process test.

### G26A — Close the packaged Television vertical

**Outcome:** the structural TV counterexample survives the complete durable runtime.

Run a process-level package test from typed catalog/provider data through persistent Series/Episode state,
set-shaped targets, Video interpretation, coverage matching, policy, search, transfer, multi-episode or pack
import, naming, actions, workbenches, API, Client, and restart.

Exit gate:

- episode, multi-episode, season-pack, and existing-coverage cases use the same production Host capabilities as
  Movies without a TV-only orchestration seam;
- one file can durably cover the demonstrated unit set and reconstruct that relationship after restart;
- every applicable action and workbench executes through the shared durable capability;
- every divergence from Sonarr/ecosystem behaviour remains visible in the versioned matrix.

### G26B — Close the packaged Music vertical

**Outcome:** the Audio, multi-track, and multi-file counterexample survives the complete durable runtime.

Run a process-level package test from typed catalog/provider data through persistent music entities, target,
Audio interpretation, matching/policy, acquisition, track/file import, naming, actions, workbenches, API,
Client, and restart.

Exit gate:

- an album-style acquisition persists its typed track coverage and independent file relationships;
- no scalar richest-audio or one-file assumption reappears in orchestration, persistence, or Client projection;
- every applicable action and workbench executes through the shared durable capability;
- every divergence from Lidarr/ecosystem behaviour remains visible in the versioned matrix.

### G26C — Close the packaged Books vertical

**Outcome:** Document and any composed Audio book path survive the complete durable runtime.

Run a process-level package test from typed catalog/provider data through persistent work/edition state,
Document or Audio interpretation, typed matching/policy, acquisition/import, naming, actions, workbenches,
API, Client, and restart.

Exit gate:

- edition identity, format choice, grouping, and file relationships remain typed through the production path;
- cross-format behaviour uses installed format capabilities instead of Book-owned codec/container vocabulary;
- every applicable action and workbench executes through the shared durable capability;
- every divergence from Readarr/ecosystem behaviour remains visible in the versioned matrix.

## Phase 5 — Close compatibility, migration, and product readiness

### G27 — Burn down the compatibility ledger

**Outcome:** core/media semantics can make evidence-backed compatibility claims instead of relying on a green
subset. Production-vendor coverage remains separately gated by G30 and the final G32 matrix.

Continuously restore cases during earlier gates, then close the ledger here:

- every in-scope source-*arr feature requirement and every upstream, generated, field-observed, hostile-input,
  and platform/filesystem case has passing executable evidence or an explicit owner-approved divergence with a
  permanent regression test;
- anything excluded from replacement scope is an explicit owner-approved scope exclusion with rationale, not a
  documented limitation relabelled as completion;
- generated clean-room, differential source, field-observed Scene/PTP, hostile input, and platform filesystem
  matrices remain separately reportable;
- naming, parsing, matching, quality/policy, import, path, action, and lifecycle differences are visible;
- skip counts are zero unless an external/platform limitation is specifically documented and approved.

Exit gate:

- no feature-coverage claim relies on an unclassified skip or vanished test;
- unresolved and missing-proof counts are zero for the entire in-scope core/media inventory;
- the compatibility report is generated from executable evidence;
- intentional Arronix differences are documented as semantics, not hidden as implementation convenience.

### G28 — Prove the complete SDK outside the repository

**Outcome:** the authoring promise is validated by a consumer which cannot reach implementation internals.

Implement an independently built extension and paired provider set using only published/package-reference SDK,
generator, format, and language artifacts. It should install into an unmodified Host and appear in an
unmodified Client.

Exit gate:

- the author states only owned item/target/release/parser facts and genuine differences;
- relationships are stated once and compiler checked;
- no project reference, source link, `InternalsVisibleTo`, Host/Client dependency, reflection convention,
  manual manifest schema, or generated-bridge implementation is required;
- catalog, interpretation, selection, persistence, one action, and one workbench traverse the complete path.

### G29 — Remove transitional semantic paths

**Outcome:** there is one authoring model and one production semantic path, after the replacement path has been
proven from published artifacts.

Remove after all migrated consumers and G28 are proven:

- legacy imperative media shapes and `MediaKindModel` authoring;
- `ParsedRelease`, `IQualityModel`, scalar ladders, `ReadingFact`, and their adapters;
- legacy language/normalization and quality-shaped shared DTO residue;
- descriptor builders, capability badges, manifest semantic transcripts, public binding SPI, and runtime schema
  reflection superseded by the typed/generated path;
- duplicate workbench, matching, item-source, import, and action implementations.

Exit gate:

- repository search and architecture tests prove the obsolete seams are absent;
- Movies, Television, Books, and Music all use the same supported SDK path;
- the package-only G07A and complete G28 consumers still pass after removal;
- removal does not increase skips or reduce the compatibility matrix;
- migration documentation is removed from `CONTEXT.md` and retained only as concise history.

### G30 — Build production provider packages one capability at a time

**Outcome:** the generic platform is useful against real services without absorbing vendor semantics.

Maintain a provider coverage matrix and ship each provider as its own checkpoint:

- vendor-grouped catalogers and curators paired to the exact media types they serve;
- indexers/protocol adapters with explicit category mapping and source capabilities;
- downloaders with compatible transfer artifacts and state mapping;
- notifiers, including Web Push and Webhook, driven by committed lifecycle events as ordinary `INotifier`
  implementations rather than Host features;
- authentication, configuration, throttling, retry, health, and secret handling owned at the provider boundary.

Indexer management is shared Host capability, not a retained Prowlarr-style application-sync role. Concrete
SABnzbd, qBittorrent, Deluge, Discord, Telegram, and equivalent integrations remain plugins.

Exit gate for each provider:

- its real transport implementation passes contract, fixture, and failure tests;
- every provider advertised as supported passes a credentialed live smoke/reconciliation test on a scheduled
  or release-time path. Fixture-only implementations remain explicitly mechanical/experimental proofs and
  cannot satisfy production-provider coverage;
- no vendor name or DTO leaks into universal contracts, Host orchestration, or a media definition;
- a disabled or unavailable provider removes only its own capability and gives a visible reason.

### G31 — Complete product operability

**Outcome:** a replacement-grade deployment can be installed, secured, upgraded, observed, and recovered.

Implement and prove:

- authentication and authorization for API, Client, plugin administration, destructive actions, and secrets;
- plugin install/update/remove, package integrity/signing policy, compatibility diagnostics, and rollback;
- database migration, backup/restore, media-root safety, crash recovery, and disaster-recovery documentation;
- structured logs, metrics, tracing, health, audit events, and actionable provider/operation diagnostics;
- supported-platform CI and filesystem/path/case/unicode behaviour;
- upgrade tests across supported contract, plugin, and database versions.

Separately resolve the recorded possibility that clients themselves may be plugins. That requires an explicit
owner-approved client contract, lifecycle, packaging, and trust model; dynamic media-type loading at G07 does
not silently answer it.

Exit gate:

- a fresh installation and an upgrade both complete without source-tree assumptions;
- backup/restore and failed-upgrade recovery are exercised;
- security boundaries and side effects match `INTERFACE.md` and the threat model;
- operational failures are visible and never silently fall back to transient state.

### G32 — Declare replacement readiness only from the complete matrix

**Outcome:** Arronix can honestly claim the intended *arr-class replacement scope.

Exit gate:

- Movies, Television, Books, and Music have complete supported verticals with documented limits;
- every advertised complete vertical has at least one credentialed, live-verified cataloger, indexer, and
  downloader combination plus its required format/language packages. Extension routes may add vendors but
  cannot substitute for this minimum runnable provider set;
- every other required provider family has a production implementation or an explicit supported extension route;
- the independently audited, revision-pinned feature and ecosystem inventory has no unclassified gap, and
  provider-inclusive live evidence is current under its declared refresh policy;
- the independent SDK proof, clean install, upgrade, restart, failure, and platform suites pass;
- `CONTEXT.md`, `INTERFACE.md`, public SDK guidance, compatibility report, and operational documentation agree;
- any remaining intentional difference is an owner-approved tested divergence or scope exclusion, not an
  unwired path, classified-but-unresolved gap, documented limit, or lost regression.

Until this gate passes, describe Arronix by the capabilities which work end to end rather than as a completed
replacement.

## Current next task

The **five platform services** are done. `ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`,
`IHostRuntimeInfo` and `IOperatingSystemInfo` have production implementations that an ordinary
`Arronix.Api` server composes, and that server activates independently published Movies and Video packages
installed beside it: `Arronix.Api.Tests` stages both from their own publish, stands the real `Program` up
over them, and asserts the active set, the kinds, the client manifest and its content addresses, and
telemetry reaching a sink registered after `AddArronixHost`. The
threat model's WP-T2 telemetry items are closed with them; T-18 quotas and T-19 host-fact disclosure remain
open and belong to WP-T5 and WP-T1.

**G07.2 is implemented and awaiting independent review.** A browser holding a proved installation reads one
serialized entity through the contract it admitted, proves the projection before rendering it, and draws a
typed Movie with artwork, ratings, lifecycle/status and collections — with no media or format assembly in
the Client build and no media kind spelled in its source. The server half, the browser half and the full
rail are recorded in `docs/research/g07/client-payload-projection.md`. Nothing further is queued behind it
until that review reports.

The next gate after it is **G07.3**: cache update, removal, eviction, stale cache and stale tab in the real
browser. G07.2 built the withdrawal rules its own correctness needed — an offer re-proved by contract object
identity before a fetch, after it and before any value is handed back, and a rendered projection invalidated
once its contract is no longer admitted — and those are a starting point for G07.3 rather than a substitute
for it.

Do not fold provider-result ingestion, durable catalog state, or the complete external-consumer vertical
into G07; G07A and G07B own those proofs. The G06 SDK package boundary and completed TMDb/provider ownership
rules remain mandatory regression evidence.
