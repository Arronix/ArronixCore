# Arronix Current Context

## Purpose and state

Arronix is a clean-sheet, pre-alpha media automation platform. A media kind owns its typed domain while the host owns generic orchestration. The product goal is a consolidated, extensible replacement for the *arr applications with their practical feature coverage preserved, not merely a plugin framework or a simplified demonstration of their common path. The repository builds as one .NET 11 Preview 7 solution pinned to SDK `11.0.100-preview.7.26381.103`; persistence, authentication, broad production-provider coverage, and an end-to-end acquisition flow are not complete.

The plugin SDK is part of the product. A third party should be able to own a Sonarr-, Radarr-, Lidarr-, Readarr-, or new-media-style application through a small set of intuitive, strongly typed abstractions. Small means that Arronix derives common behaviour and supplies sound defaults; it never means erasing media shape, moving semantics into string bags, or making an author understand Host implementation mechanics.

The plugin-consumable `Arronix.Abstractions` contract line is `0.8.0`. First-party plugin manifests declare
`>=0.8 <0.9`. That SDK surface is pre-1.0; it has no second per-type experimental tier or compiler opt-in.

## Current architecture

- `Arronix.Abstractions` contains media-neutral contracts and has no project or package references.
- `Arronix.Common` contains reusable host-side implementations.
- `Arronix.Plugins` owns manifests, package dependency resolution, shared contract admission, capability
  admission, assembly isolation, registration capture, and the attempt-scoped transaction through which Host
  prepares an admitted inventory for the loader to publish.
- `Arronix.Host` owns DI composition, generic engines, registries, scheduling, storage, and plugin activation.
- `Arronix.Api` and `Arronix.Client` are the HTTP and Blazor WebAssembly edges.
- The video package is two assemblies. `Arronix.Format.Video` is the shared domain surface and owns video's owner semantics: the representation and quality facts a `Release<Video>` carries, the format family a media type names in its constructor, and the release preferences video contributes to a dependant's compiled policy. `Arronix.Format.Video.Contributions` is the isolated half and owns video's executable work, currently the release-term recognition vocabulary. A media declaration references only the domain assembly.
- The movies package is two assemblies. `Arronix.Media.Movies` is the shared half and owns `Movie`, `MovieReleaseStage`, and `MovieReleaseTimeline`; `Arronix.Plugin.Movies` is the isolated entry assembly and owns the `Movies` definition, `MovieReleaseParser`, the plugin module, and the generated projections.
- `Arronix.Language.Reference` is a separately loadable language capability with English, German, and French implementations.
- `Arronix.Provider.Tmdb` is the first production provider package. It depends on Abstractions and the Movies
  domain only, closes `ICataloger<Movie>` and `ICurator<Movie>`, and owns all TMDb settings, transport, DTO,
  identity, marker, and mapping vocabulary.
- `Arronix.Generators` emits closed entity readers and descriptor projections while a media extension compiles. It is an analyzer-only build dependency and is never loaded as a plugin runtime dependency.
- `Arronix.Compatibility.Ratchet` validates the canonical compatibility ledger against fresh test results and its prior committed form. The ledger under `verification/compatibility` gives every known omission a stable semantic identity, immutable published binding and expectation, provenance source, and explicit replacement path. The same-build solution binlog is the authoritative practical record of the actual `Csc` inputs, embedded inputs, warning policy, and build configuration. Each registered execution must resolve from its exact NUnit leaf to the declared CLR method in NUnit's exact executed assembly; the associated Portable PDB must bind that method to the primary and support documents, whose embedded source bytes must exactly match the locked repository files. This is layered same-build provenance, not a cryptographic or hermetic-build attestation claim. Replacement topology is independent of its semantic outcome; outcome must match the source disposition and requirement transition. Acyclic one-to-one replacement chains close to a fixed point, so a published witness can later be retired without rewriting earlier edges. Partition records remain non-closing until aggregate semantic composition is modeled. Required owner decisions resolve to pinned prior-ledger sources attached to the decided requirement. Passing tests do not self-attest proof through an output marker.
- Media extensions may reference Abstractions, the format capabilities they compose, and their own media domain assembly. An extension does not reference another kind's media domain. Host and Client reference neither half of Video.
- `Arronix.Format.Video` ships as the installed package `arronix.format.video`: one shared contract
  assembly, no entry assembly, no capability. Movies and Television require it by identifier and carry no
  private copy of it.
- First-party package payloads are staged by publishing each extension into a cleared directory, so a
  staged payload is the computed runtime closure rather than a listing of a build directory that MSBuild
  never prunes. A planted stale assembly in a project's real build output is proved unable to enter a staged
  payload through the actual publish, and inverting that staging to a recursive copy makes the proof fail.
- `eng/ci/run-tests.sh` is the local and hosted-CI proof rail: locked restore, one Release warnings-as-errors solution build with its binlog retained as compiler-input evidence, a non-empty NUnit result from every discovered test project, exact NUnit-leaf/method/assembly/PDB/source binding, an exact 302-skip ratchet, and current-plus-prior compatibility validation. The eight-column required-test registry is append-only and contains only three durable proof sentinels. Package lock files and the consumed-only central package graph are checked in.

## Active invariants

- Plugin authors declare owned domain types and genuine differences. Arronix derives common actions, orchestration, projections, validation, and runtime dispatch from that source. The obvious authoring path must also be the ownership-correct path.
- Authoring stays rich while its surface stays small. Strong generic relationships, complete common base types, constructor-owned invariants, typed defaults, and compiler-generated projections reduce ceremony; field dictionaries, duplicated descriptors, reflection conventions, builder transcripts, repeated type arguments, and implementation-specific callbacks do not.
- Type erasure is a one-way internal compilation step. A typed media, format, language, or provider relationship may become a kind-blind Host or wire projection only after its compile-time relationship has been validated; the erased representation never becomes an alternative authoring model.
- A media type derives from `MediaType<TItem,TTarget,TRelease,TParser>`; the first three types are respectively the durable catalog/library entity, ephemeral acquisition intent, and interpreted publication. `TParser : IReleaseParser<TRelease>` binds the media-owned parser at compile time.
- The `MediaType` primary constructor captures its stable kind identifier, singular/plural display names, non-empty format composition, typed minimum-availability selection, and file binding. File binding defaults to `OnePerItem`; other relationships must state their shape explicitly. Kind identity is never derived from mutable display wording. The remaining optional or repeatable media-specific declarations are virtual values for identity, groups, additional selections, searches, matching, release policy, query planning, naming, summaries, intent exceptions, workbenches, and derivations. Parsing is the deliberate exception: `IReleaseParser<TRelease>.Parse` is static abstract because the parser type itself is the executable declaration. `CompiledShapes` is the generator-owned abstract member; authors do not implement it. There are no public whole-media replay builders, per-kind action transcripts, parse-declaration DSLs on typed media, `IUses...` capability badges, or test corpora on the runtime contract.
- `IMediaEntity` is the minimum interface shared by items and groups. `MediaItem<TReleaseTimeline,TReleaseStage>` is directly usable; `MediaItem<TItem,TReleaseTimeline,TReleaseStage>` retains the exact derived item type for relationships. `MediaCollection<TItem>` is the common group class.
- `ReleaseTarget<TItem>` is the concrete one-item acquisition target. `Release<TRepresentation>` is the concrete common release carrying title, year, edition, and format-owned representation. Media types use the closed common types directly unless they add real coverage or release facts. Television's set-shaped target and coordinate-bearing release are the counterexamples which justify media-owned types.
- The common item class exposes external identifiers, titles and translations, years, description, runtime, principal organization, certification, genres, keywords, website, preview, artwork, popularity, ratings, a media-owned lifecycle object, its typed release stage, catalog presence, and plural typed collection membership. It carries no durable key: `MediaItemId` is host-assigned at materialization and is not a media entity fact. `IReleaseTimeline<TReleaseStage>` makes the lifecycle-to-stage relationship compiled rather than conventional.
- Entity and workbench-row descriptor shapes are generated from their CLR definitions at build time. Host consumes generated closed getters and validates the result; it does not discover media schema with property or attribute reflection.
- `ItemInfo` is the common localized title/overview payload. `Localized<ItemInfo>` is used when no media-specific localized facts exist.
- The plugin SDK has no second non-generic `IMediaType`. Host-only `IMediaTypeRuntime` is the kind-blind bridge.
- `ICataloger<TItem>` returns the media type's own shaped items. Field dictionaries are not an alternative contract. The non-generic `ICataloger` floor declares the external identifier scheme the cataloger is the authority for and recognizes its own markers locally. Host captures the declaration once at registration and rejects readings in another scheme; media parsers receive validated typed readings and do not embed catalog-vendor marker vocabulary.
- Identity at the catalog boundary has one rule. A cataloger owns an item's identity in its own declared
  scheme, and every item it returns states exactly one identifier in that scheme. A curator returns
  references to catalog identities, never items, and its own entry identifier is a separate type that cannot
  stand in for one. Host alone assigns `MediaItemId`, at materialization, and holds it as host state scoped
  by kind and level, so a reload that rebuilds a kind's runtime does not reissue it. Routing is by scheme,
  never by provider identifier or implementation type. See `docs/research/g04/media-item-identity.md`.
- A provider author names the media item type once, in the contract the implementation closes. Registration
  reads that pairing back from the contracts the implementation actually implements, by one-time type
  inspection; a family marker on the closed contracts makes the wrong family a compiler error but carries no
  values, because a directly implementable interface cannot hold a claim the platform can trust. The
  registration called fixes the family; Host mints the qualified identifier. No provider, declaration, or
  consumer restates any of the three.
- A media item type identifies exactly one media kind. Two admitted kinds closed over the same item type are
  refused at kind admission, independently of any provider, because paired providers and identifier
  recognition both resolve a kind from that type.
- Provider registrations carry implementation types. After admission, Host constructs them through an exact
  public `(IPluginContext)` constructor, or a public parameterless constructor when no context is needed. Plugin
  activation never resolves from Host DI. Before any implementation in a package is constructed, Host proves
  every registration's contract, item type and implementation agree, and that an installed kind supplies
  that item type; the two refusals carry distinct codes because they are distinct operator problems, and
  neither is the contract-version code. A post-construction contract check remains as defense in depth.
- Host admission prepares complete kind, provider, language, job, and health candidates without publishing
  them. A successful preparation carries one exact `IPluginAdmissionAttempt`; after every remaining loader
  check passes, one shared Host-owned publication gate commits its Host receipt, per-kind token claims, and Active
  runtime result as one change. Refusal, exception, or late collision rolls back only that attempt.
- Naming-token identity is its invariant-lowercased alphanumeric spelling. Declaration agreement, reserved
  names, ownership collisions, parsing, and rendering all use `Arronix.Common.Naming.NamingTokenName`; case,
  spaces, punctuation, and supplementary-plane letter case cannot create a second owner.
- `ReleaseListing` is raw indexer output. It is not an interpreted release or a selection candidate.
- `TargetMatch<TTarget>` retains typed Covered and Missing targets and an explicit Satisfied, Partial, Superset, or Rejected disposition.
- Release selection is deterministic: hard requirements, lexicographic typed preferences, bounded facets, acquisition preference, then stable release identity. Provider result order is irrelevant.
- A format capability owns its representation vocabulary. Video codecs, dynamic-range identifiers, defects, extensions, and title vocabulary do not belong in universal contracts or Host.
- Video lineage models the observable release channel, acquired carrier, acquisition method, and transformations after that carrier. It does not claim a studio-master history.
- Direct-copy is channel-relative: a Remux copies disc streams and a WEB-DL copies the hosted stream artifact. Neither means globally untouched.
- Audio tracks remain a collection. No scanner collapses them to a synthetic richest track.
- Provenance is an interpretation sidecar. Ordinary properties and nullability express facts and absence; there is no universal `Evidence<T>` wrapper or globally ordered evidence source.
- Release policy ownership is layered: format capabilities contribute defaults for format-owned facts, media types contribute media-owned preferences, and user configuration must remain final authority. The typed compiler currently assembles hard-coded fragments; runtime user-policy composition is not wired yet.
- Descriptors are derived discovery and generic-presentation data. They must not contain executable types, delegates, or policy objects.
- Ratings, rating scales, localized owner-shaped values, content certifications, monitoring scope, and typed standard workbench rows are common media-neutral values. Media types decide whether and how they use them.
- Language-specific comparison, query, file-name spelling, and sort rules are registered `ILanguageDefinition` implementations. Media types state language; they do not own articles, conjunctions, stop words, or transliteration tables. Unknown-language text receives invariant mechanics only.
- Typed workbench rows are the semantic values. The field-valued workbench proposal is a wire projection, not an alternative domain schema. Artwork is valid semantic row data and does not prescribe layout.
- Search, refresh, rescan, monitoring, availability, rename, add, remove, exclusion, and group operations are platform-standard actions derived by Host. A media type supplies only facts that specialize them, including names, identity roles, availability, and grouping capabilities.
- `StandardMediaAction` is the semantic action identity. String action and parameter identifiers are stable wire spellings only; Client does not repeat them as private conventions.
- `ActionRequest` is one shared typed wire contract. Its subjects are `MediaItemRef` values; only descriptor-defined parameter values cross as keyed text.

## Active migration state

The typed-media work is an active, large migration. Movies is the current structural and authoring pressure
test; its conversion does not prove a minimal independent SDK, end-to-end acquisition, or *arr feature
coverage.

- Movies publicly defines the intentionally empty nominal type `Movie : MediaItem<Movie,MovieReleaseTimeline,MovieReleaseStage>`. The base already is the complete movie item shape; the nominal closure gives `ICataloger<Movie>`, `ICurator<Movie>`, targets, collections, and future movie-specific extensions one stable domain type. Its common target and release remain `ReleaseTarget<Movie>` and `Release<Video>`.
- Movies uses `MediaCollection<Movie>` directly. Its `Collections` property projects as a many-to-many reference field while the declared semantic group name keeps the stable `collection` axis identifier.
- Movies closes `MediaType<Movie,ReleaseTarget<Movie>,Release<Video>,MovieReleaseParser>` with its kind, display names, Video format use, and minimum-availability rule in the base constructor, then returns its remaining relationships and presentation/workflow choices as typed override values. This is not yet a proven deltas-only surface: generic manual-import/search/catalog workbenches, default browse/title-sort behaviour, and release-policy builder choreography still need classification and possible Host derivation. `MovieReleaseParser` implements the typed static parser directly in C#; Host compiles the definition once into its permanent internal kind-blind bridge, while projection to legacy `ParsedRelease` is temporary.
- Movie release-name regression cases live in `Arronix.Plugin.Movies.Tests` and are executed directly by the parser tests. The four published compatibility rows `t012`, `t013`, `t023`, and `t027` are isolated in the immutable `MovieTitleCompatibilityCorpus`; the ordinary representative corpus may evolve without silently rewriting those ledger bindings. Production media definitions and Host runtime models do not carry test fixtures.
- Semantic searches contain only cross-provider terms. Newznab/Torznab category numbers and equivalent protocol mappings belong to provider/protocol plugins.
- Television has typed target/release and coverage pressure tests, but its production shape/parser/matcher/naming implementation still uses the legacy imperative seams.
- Books and Music still use legacy imperative media seams.
- Legacy `IQualityModel`, ladder DTOs, and the untyped `ParsedRelease.Quality` path remain for unconverted implementations and compatibility adapters. Ladder-shaped residue also remains in shared `FieldValue`, `FormatFamily`, `MediaFileFacts`, `NotificationMessage`, Host registration, and storage surfaces; the migration has not yet isolated or removed all of it.
- The movies and video packages are split into a shared contract assembly plus an isolated executable
  assembly. `Arronix.Media.Movies` carries `Movie`, `MovieReleaseStage`, and `MovieReleaseTimeline`;
  `Arronix.Format.Video` carries video's owner semantics. A separately shipped provider compiles
  `ICataloger<Movie>` and `ICurator<Movie>` against Abstractions plus the movies media domain and links no
  part of the Movies extension; Movies and Television compile their typed releases against the one video
  domain assembly and reference neither the other's media domain nor video's executable half, which no
  media extension payload carries. At runtime the installation admits each declared contract once into one
  Host-owned collectible context and hands every publisher and every dependant in its declared closure the
  same `Assembly` object, so two independently installed dependants see one `Video` type and a separately
  packaged provider closes `ICataloger<Movie>` over the registered movies kind's own item type. Global
  admission is not global visibility: a package binds only to contracts published by itself or by a package
  in its exact transitive dependency closure.
- The packaged Movies extension survives the complete loader pipeline. `IPluginAdmissionCheck.Prepare` returns
  an attempt whose `AdmittedInventory` has one entry per prepared `MediaKindId`, carrying that kind's derived
  `NamingToken` collection read from its `RegisteredMediaKind`. Late declaration agreement, scheduled-job
  kind association, duplicate-kind checking, and token ownership consume that inventory. Legacy
  `IMediaShapeProvider` registrations remain transitional inputs to the same mandatory Host admission path.
  Token claims remain per kind, never the cross product of an extension's kinds and vocabulary.
- Publication and teardown are exact lifetime transitions. A failed or repeated load cannot replace an
  existing Active authority. Normal Host shutdown stops and drains the scheduler before atomically
  withdrawing the exact Host receipt, token plan, and runtime result; owned provider and language instances,
  direct registrations, the module, and its collectible load context are then released in deterministic
  async-preferred order. A job which genuinely overruns its declared shutdown deadline keeps the owning
  plugin Active and rooted rather than unloading executing code. A completed stop leaves a reference-free
  `Stopped` result.
- The packaged-output proofs exercise real isolated assemblies rather than a direct binder: Movies reaches
  Active with its admitted item type in its own `PluginLoadContext`; a separate G02 fixture contributes a
  real kind, notifier, language, scheduled job, health contributor, and token vocabulary; late rejection
  leaves every registry empty; ordinary stop and scheduler drain release every object; overrun defers that
  release; and a throwing load-context unloading handler is logged without aborting the next package.
- `src/Arronix.Plugin.Movies/plugin.json` carries only current manifest-owned concerns: manifest schema version,
  package identity, operator-facing name, version, description, the Arronix contract range, the entry assembly,
  its published shared contract assemblies, its direct package dependencies, and requested capabilities. Its
  media-derived `mediaKinds`, `tokens`, and `policies` are gone; `actions` never existed; and
  cataloger-owned external identifier schemes are not baked into Movies. Manifest
  validation permits those omissions; a manifest that does state kinds or tokens is still held to them
  exactly, in both directions, against the admitted projection. Capabilities are explicit because privilege
  cannot be derived from code before it is allowed to run. Operator-specific host/root access grants are
  runtime configuration.
- Loader containment has two rules and is applied at every boundary that can produce the failure. Staging or
  loading a file is bounded, so that boundary uses a closed allowlist and an unnamed failure type stops
  admission. A package's own code — its module, constructors, property getters, disposers and load-context
  unloading handlers — may throw anything, so that boundary contains everything except conditions in which
  the process is no longer sound. Neither absorbs cancellation, exhausted memory or stack, corrupted memory
  or a structured native failure, and both read the whole exception chain. Listing a package's folder and
  running its teardown are both covered: a folder that cannot be enumerated refuses that package and leaves
  the load pass running, and a process-fatal or canceled disposer propagates instead of being filed as a
  cleanup note, with the package's contract hold retained.
- Books, Music, and Television manifests still carry `mediaKinds`, `identifiers`, and `tokens`. Those are transitional declarations belonging to their legacy media paths and are removed with their conversions, not here.
- The typed Movie parser currently materializes common release text facts and consumes catalog-owned external
  identifier readings, including the installed TMDb provider's `{tmdb-...}` markers. It does not yet compose
  format-owned recognizers into a populated `Video` representation, so the complete release-interpretation
  path remains unwired.
- Typed release policy and deterministic selection exist, but production acquisition does not yet materialize typed releases and call the selector end to end.
- The production TMDb package supplies a typed Movie cataloger and curator through constrained Host activation.
  Its cataloger returns exact `Movie` values; its curator returns TMDb catalog references. Missing or
  incompatible Movies quarantines only the provider package.
- `CatalogDispatcher` fetches through the cataloger that owns a reference's scheme, assigns durable identity,
  and materializes a curated list through the catalogs its references name. The installed TMDb proof executes
  that full generic path, but no API/Client user workflow reaches it yet. `CatalogIdentity` and `IMediaStore`
  remain in memory, and a merge does not move library rows already keyed by a superseded reference.
- Typed workbench proposal/commit values and generic standard rows exist, but the current `IMediaItemSource` execution seam still projects proposals and commits through the kind-blind wire form.
- Standard action dispatch is capability-based. Host currently executes `SetMonitoring` against `IMediaStore`; operations needing acquisition scheduling, catalog refresh, filesystem mutation, removal, or exclusion storage return an explicit 501 until those capabilities exist.

## Completion and continuity discipline

- A model, descriptor, generated projection, unit test, or registration seam is not by itself a completed feature. Completion requires the same semantics to survive authoring, generic capture, plugin admission, constrained activation, Host execution, persistence where applicable, API/wire projection, Client consumption, tests, and boundary documentation. Any bypass or parallel legacy path must be reported as active migration work.
- Full *arr feature coverage and observable Scene/PTP ecosystem compatibility are acceptance concerns. Reuse the established behaviour and regression corpora as evidence; record intentional differences explicitly rather than simplifying difficult cases out of the model.
- Prefer a coherent common abstraction over feature-local adapters. A repeated workaround is evidence that the common primitive or ownership boundary is incomplete; it must not become precedent merely because it lets one feature ship sooner.
- `CONTEXT.md`, `INTERFACE.md`, `HISTORY.md`, `docs/design/typed-media-north-star.md`, `docs/design/typed-media-roadmap.md`, and `docs/owner-ledger.md` are the durable context for bounded work. A fresh task reads the relevant parts before proposing or changing a boundary; it does not ask the owner to reconstruct hundreds of turns of prior reasoning.
- Distinguish owner assertions and approvals from hypotheses, examples, and requests to investigate. A forcefully phrased question is not an approval. Agent proposals and intermediate mechanisms do not become architecture unless the owner adopts them.
- Record unresolved contradictions and missing vertical wiring honestly. Do not close, omit, or relabel them because the local feature works, the implementation is old, or the next task would be easier without them.

## Active execution gate

The dependency-ordered migration is maintained in `docs/design/typed-media-roadmap.md`. It replaces the former
flat outstanding-action list: only one gate is active, every gate has a production-path exit test, and later
work does not substitute for closing an earlier dependency.

- R00 is the checkpoint at `018e2b0d1`, with 1,998 passed, 302 skipped, and zero failed tests.
- G01 is complete: the locked clean-repository rail reports 2,112 passed, 302 registered skips, zero failed,
  and zero inconclusive across 11 test projects; the ledger contains 302 cases, 129 requirements, and 12 sources,
  while three durable sentinels guard the proof rail itself.
- G02 is complete: the real packaged Movies extension reaches `Active` through discovery, typed preparation,
  late agreement, one atomic publication, and exact stop; its derived tokens are owned once each by the
  admitted kind; no contribution becomes visible before every loader check passes; failed reload cannot
  replace an existing runtime; the acceptance path uses real isolated packaged output; and Movies' manifest
  is no longer a second media definition. It does not claim that the parser, selector, production providers,
  persistence, or Client vertical works.
- G03 is complete: packages declare dependencies, one resolver produces one resolved graph, the installation
  admits each shared contract once into one Host-owned collectible context, and a separately packaged
  provider closes `ICataloger<Movie>` over the same runtime type the registered movies kind publishes. See
  `docs/research/g04/integrated-package-runtime.md` for the evidence and the residual risks.
- G04 is complete. Its provider-pairing half establishes that provider family, item type and identifier
  each have exactly one authority; the closed pairing is read from the contracts an implementation actually
  implements, once, by type inspection when the registration is built; Host proves every registration's
  contract, item type and implementation agree, and that an admitted kind supplies that item type, before it
  constructs anything in the package, refusing with `PluginProviderContractInvalid` or
  `PluginMediaPairingUnsatisfied`; two kinds closed over one item type are refused at kind admission with
  `MediaItemTypeConflict`; and consumers receive `ProviderCatalogEntry`, carrying the minted identifier and
  the family the registration fixed, instead of reconstructing either.
- Its identity half is answered and built. The owner settled the rule on 2026-08-25: a cataloger owns an
  item's identity in its own declared scheme, a curator returns references rather than items, and Host alone
  assigns `MediaItemId` at materialization. `IMediaEntity` no longer carries a key, `CatalogIdentity` is host
  state, and `CatalogDispatcher` routes by scheme and materializes. The invariant and its current limits are
  in `docs/research/g04/media-item-identity.md`: a merge resolves the superseded reference but moves no
  library rows and nothing persists. The full close rail is
  2,603 passed, 302 registered skips, zero failed and zero inconclusive across 11 test projects.
- G05 is complete. The independently packaged TMDb cataloger returns the installed Movies package's exact
  `Movie`; its curator returns catalog references which `CatalogDispatcher` resolves through that cataloger;
  Host assigns the durable reference; marker readings reach the Movie parser; and real package admission
  isolates missing or incompatible Movies failures to TMDb. The provider owns its HTTP, credential, DTO,
  settings, identity, marker, and mapping vocabulary. See `docs/research/g05/tmdb-provider-pressure-test.md`.
- G06 is active: separate semantic authoring SDK from generated and Host binding SPI without losing the
  compiled typed relationship or introducing a second authoring path.

The later gates cover the hidden binding SPI, dynamic typed Client loading, compatibility
evidence, format/language/media interpretation, typed matching and policy, TV/Music/Books pressure tests,
durable state and acquisition, standard workflows, legacy removal, independent SDK proof, provider coverage,
and replacement readiness. Their order and acceptance criteria live only in the roadmap to avoid another
duplicated checklist drifting from current state.

## Technical debt

- `MediaKindModel`, legacy shapes, `ParsedRelease`, and quality ladders remain during migration.
- Lifecycle internals currently project as one composite field. The typed `Status` projection is top-level, but selected lifecycle milestones are not yet independent top-level sort/filter axes.
- The format-capability loading and versioning lifecycle needs a first-class manifest/package design before independently distributed format packages are promised.
- `ReleasePolicy<T>.Compile` still exposes builder callback choreography to media authors. Format defaults and media policy fragments must compose without making authors drive an internal compiler mechanism.
- Movies still declares likely-standard workbenches, browse defaults, and title ordering. Audit them against Television, Music, and Books and derive any behaviour which is not a genuine media difference.
- `IClosedCataloger` and `IClosedCurator` are public because an interface cannot inherit a less accessible
  one. They are binding SPI an author never implements, and G06 owns moving or hiding that class of surface.
  Deriving the pairing from the implemented contract needs one-time type inspection at registration; that is
  erasure at the sanctioned boundary, not authoring vocabulary, and it is stated in their documentation
  rather than hidden.
- `FileBindingDefinition` currently expresses only `None` and `OnePerItem`; Television must settle the typed multi-unit/file cardinality instead of using a parallel legacy seam.
- `NormalizationOptions` and `IDiacriticFoldingProvider` remain for legacy implementations; new language-specific comparison/query/naming/sort behaviour belongs in `ILanguageDefinition` plugins.
- The generator rejects non-partial media declarations through compiler diagnostic `CS0260`; it does not yet emit a dedicated Arronix diagnostic explaining the authoring requirement.
- The current one-command full-solution run (2026-08-25) reports 2,749 passed, 302 skipped, zero failed,
  and zero inconclusive from 3,051 total cases across 12 test projects. Of the skips, 301 are Movies cases
  and one is an architecture case; all are registered in the compatibility ledger. This verifies the current
  solution graph and enabled tests, not the unwired production capabilities above; every later passing-suite
  claim must report its observed skip count and ratchet result.
- The Movies test project imports the movies media domain through one project-level `global using`. The
  regression sources that name `Movie` are locked by the compatibility ledger, so the import is stated once
  in `GlobalUsings.cs` rather than repeated per file; no locked source changed and no ledger transition was
  required.
- `--locked-mode` restore validates the package graph but not the project entries a lock file records, so a
  renamed or deleted project can stay named in one and still restore cleanly. An architecture rule now
  checks each lock file against the evaluated transitive project closure in both directions.
- `MovieReleaseParser.EditionRegex` is a `public const string`. C# inlines a `const` into referencing
  assemblies at compile time, so it must become `static readonly` before anything outside its own package
  reads it. Nothing outside the package does today.
- General hot-unload containment remains later work: consumers outside the scheduler may retain raw provider,
  language, health-contributor, or job references after withdrawal; a hostile `DisposeAsync` can decline to
  complete; and actual collectible-context garbage collection is not promised merely because Arronix has
  dropped its own roots and called `Unload`.
- One shared contract context holds every admitted contract, so a package's release is a claim on that
  context rather than the collection of its own assembly. Side-by-side contract versions are unsupported and
  contract replacement is restart-scoped. A reload involving shared contracts is refused rather than
  reconciled: an admitted store takes a second resolved graph only when neither shares anything.
- Prerelease range semantics are unresolved and unchanged: `>=0.1 <0.2` admits `0.2.0-preview.1` by ordinary
  precedence, because `VersionRange` has no npm-style prerelease exclusion. That is the same rule the
  contract range has always used, and changing it belongs in `VersionRange` rather than in the dependency
  layer. Recorded rather than decided.
- The shared-assembly source-text screen is a heuristic. The structural rules — no executable platform type,
  no mutable or editable static state, nothing running on load, a reference closure limited to the universal
  contracts — are what prove the boundary; the spelling screen catches literal usage only.
