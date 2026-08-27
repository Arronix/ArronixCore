# Arronix Current Context

## Purpose and state

Arronix is a clean-sheet, pre-alpha media automation platform. A media kind owns its typed domain while the host owns generic orchestration. The product goal is a consolidated, extensible replacement for the *arr applications with their practical feature coverage preserved, not merely a plugin framework or a simplified demonstration of their common path. The repository builds as one .NET 11 Preview 7 solution pinned to SDK `11.0.100-preview.7.26381.103`; persistence, authentication, broad production-provider coverage, and an end-to-end acquisition flow are not complete.

The plugin SDK is part of the product. A third party should be able to own a Sonarr-, Radarr-, Lidarr-, Readarr-, or new-media-style application through a small set of intuitive, strongly typed abstractions. Small means that Arronix derives common behaviour and supplies sound defaults; it never means erasing media shape, moving semantics into string bags, or making an author understand Host implementation mechanics.

The plugin-consumable `Arronix.Abstractions` contract line is `0.9.0`. First-party plugin manifests declare
`>=0.9 <0.10`. That SDK surface is pre-1.0; it has no second per-type experimental tier or compiler opt-in.

## Current architecture

- `Arronix.Abstractions` contains media-neutral contracts and has no project or package references.
- `Arronix.Common` contains reusable host-side implementations.
- `Arronix.Plugins` owns manifests, package dependency resolution, shared contract admission, capability
  admission, assembly isolation, registration capture, and the attempt-scoped transaction through which Host
  prepares an admitted inventory for the loader to publish.
- `Arronix.Host` owns DI composition, generic engines, registries, scheduling, storage, and plugin activation.
- `Arronix.Api` and `Arronix.Client` are the HTTP and Blazor WebAssembly edges. The Client references
  Abstractions only and acquires installed media contract assemblies from the host it was served by, at
  run time, over content-addressed HTTP.
- The video package is two assemblies. `Arronix.Format.Video` is the shared domain surface and owns video's owner semantics: the representation and quality facts a `Release<Video>` carries, the format family a media type names in its constructor, and the release preferences video contributes to a dependant's compiled policy. `Arronix.Format.Video.Contributions` is the isolated half and owns video's executable work, currently the release-term recognition vocabulary. A media declaration references only the domain assembly.
- The movies package is two assemblies. `Arronix.Media.Movies` is the shared half and owns `Movie`, `MovieReleaseStage`, and `MovieReleaseTimeline`; `Arronix.Plugin.Movies` is the isolated entry assembly and owns the `Movies` definition, `MovieReleaseParser`, the plugin module, and the generated projections.
- `Arronix.Language.Reference` is a separately loadable language capability with English, German, and French implementations.
- `Arronix.Provider.Tmdb` is the first production provider package. It depends on Abstractions and the Movies
  domain only, closes `ICataloger<Movie>` and `ICurator<Movie>`, and owns all TMDb settings, transport, DTO,
  identity, marker, and mapping vocabulary.
- `Arronix.Generators` emits closed entity readers and descriptor projections while a media extension compiles. It is an analyzer-only build dependency and is never loaded as a plugin runtime dependency.
- `Arronix.Sdk` is the packaging-only extension-authoring metapackage. It depends on
  `Arronix.Abstractions`, carries `Arronix.Generators` under `analyzers/dotnet/cs`, and contributes no
  runtime assembly. Extension authors take this one package plus the format and language packages they
  compose.
- `Arronix.Compatibility.Ratchet` validates the canonical compatibility ledger against fresh test results and its prior committed form. The ledger under `verification/compatibility` gives every known omission a stable semantic identity, immutable published binding and expectation, provenance source, and explicit replacement path. The same-build solution binlog is the authoritative practical record of the actual `Csc` inputs, embedded inputs, warning policy, and build configuration. Each registered execution must resolve from its exact NUnit leaf to the declared CLR method in NUnit's exact executed assembly; the associated Portable PDB must bind that method to the primary and support documents, whose embedded source bytes must exactly match the locked repository files. This is layered same-build provenance, not a cryptographic or hermetic-build attestation claim. Replacement topology is independent of its semantic outcome; outcome must match the source disposition and requirement transition. Acyclic one-to-one replacement chains close to a fixed point, so a published witness can later be retired without rewriting earlier edges. Partition records remain non-closing until aggregate semantic composition is modeled. Required owner decisions resolve to pinned prior-ledger sources attached to the decided requirement. Passing tests do not self-attest proof through an output marker.
- Media extensions may reference Abstractions, the format capabilities they compose, and their own media domain assembly. An extension does not reference another kind's media domain. Host and Client reference neither half of Video.
- `Arronix.Format.Video` ships as the installed package `arronix.format.video`: one shared contract
  assembly, no entry assembly, no capability. Movies and Television require it by identifier and carry no
  private copy of it.
- First-party package payloads are staged by publishing each extension into a cleared directory, so a
  staged payload is the computed runtime closure rather than a listing of a build directory that MSBuild
  never prunes. A planted stale assembly in a project's real build output is proved unable to enter a staged
  payload through the actual publish, and inverting that staging to a recursive copy makes the proof fail.
- `eng/proofs/fixtures/g07/movie.json` is the serialized entity the G07 browser proof reads. It is written
  by the movies contract's own `Serialize` and compared to it byte for byte, so it is generated evidence
  rather than a hand-maintained document; both its images are inline rasters, so the proof fetches nothing
  and reaches nowhere. The proof script stages it into the published client's static root as proof-only
  input: there is no test API endpoint, and neither Host nor Client gains a dependency on movies.
- `eng/ci/run-tests.sh` is the local and hosted-CI proof rail: locked restore, one Release warnings-as-errors solution build with its binlog retained as compiler-input evidence, a non-empty NUnit result from every discovered test project, exact NUnit-leaf/method/assembly/PDB/source binding, an exact 302-skip ratchet, and current-plus-prior compatibility validation. The eight-column required-test registry is append-only and contains only three durable proof sentinels. Package lock files and the consumed-only central package graph are checked in.

## Active invariants

- Plugin authors declare owned domain types and genuine differences. Arronix derives common actions, orchestration, projections, validation, and runtime dispatch from that source. The obvious authoring path must also be the ownership-correct path.
- Authoring stays rich while its surface stays small. Strong generic relationships, complete common base types, constructor-owned invariants, typed defaults, and compiler-generated projections reduce ceremony; field dictionaries, duplicated descriptors, reflection conventions, builder transcripts, repeated type arguments, and implementation-specific callbacks do not.
- Type erasure is a one-way internal compilation step. A typed media, format, language, or provider relationship may become a kind-blind Host or wire projection only after its compile-time relationship has been validated; the erased representation never becomes an alternative authoring model.
- A media type derives from `MediaType<TItem,TTarget,TRelease,TParser>`; the first three types are respectively the durable catalog/library entity, ephemeral acquisition intent, and interpreted publication. `TParser : IReleaseParser<TRelease>` binds the media-owned parser at compile time.
- The `MediaType` primary constructor captures its stable kind identifier, singular/plural display names, non-empty format composition, typed minimum-availability selection, and file binding. File binding defaults to `OnePerItem`; other relationships must state their shape explicitly. Kind identity is never derived from mutable display wording. The remaining optional or repeatable media-specific declarations are virtual values for identity, groups, additional selections, searches, matching, release policy, query planning, naming, summaries, intent exceptions, workbenches, and derivations. Parsing is the deliberate exception: `IReleaseParser<TRelease>.Parse` is static abstract because the parser type itself is the executable declaration. `CompiledShapes` is a generator-supplied public override marked `EditorBrowsable(Never)`; its visibility preserves the immutable G01 executable-generator sentinel, while ordinary authors neither implement nor call it. Capture is an explicit hidden-interface operation and is absent from concrete media types' public surface. There are no public whole-media replay builders, per-kind action transcripts, parse-declaration DSLs on typed media, `IUses...` capability badges, or test corpora on the runtime contract.
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
- A package declares a client facet: `clientContracts` names zero or more of its own published shared
  contract assemblies a browser may download, validated as a subset so an entry assembly or an unpublished
  file can never appear. `IClientContractCatalog` projects the Active plugin registry into that offer,
  carrying each assembly's SHA-256 content hash, complete CLR identity, module version identifier and length,
  and each package's transitive client closure in load order with one hash over it. The bytes served are the
  bytes admitted, retained from admission rather than re-read. `GET /api/v1/client-contracts` publishes the
  manifest uncacheable; `GET /api/v1/client-contracts/{package}/{contentHash}/{fileName}` serves one assembly
  immutable, and a superseded address is `410 Gone` rather than `404`. A facet is servable only if a browser
  can bind everything its assemblies name and every required facet it declares is itself served; withholding
  cascades to a fixed point, and the facets a host withholds are published in the same document and reported
  by a health contributor.
- The Client verifies in two passes, and the first never touches the runtime. It validates the untrusted
  manifest whole, then for every assembly in the required closure checks length, SHA-256, and — by reading
  the PE metadata of those exact bytes — the declared CLR identity, the declared module version identifier
  and the declared reference to its own universal contract. Only if the entire closure passes does anything
  load; a browser cannot unload, so half an installation is not a smaller installation. After each load a
  second proof compares what the runtime produced with what was published and requires the loaded assembly's
  contract reference to resolve, by object identity, to the client's own `Arronix.Abstractions`. An entry
  that verified and was not loaded reports `Verified`, never `Loaded`, and `CanProject` is true only when
  every required assembly is resident. The client keeps bytes in a content-hash-keyed browser store and
  discovers nothing by enumerating a loaded assembly — an architecture rule rejects type, property, field,
  method and member enumeration and `Activator.CreateInstance` anywhere in its source.
- A page holding a verified installation reads one serialized entity through the contract it admitted. The
  contract is chosen from what the page admitted, named by declaring assembly and entry point together, and
  re-proved by contract object identity before the fetch, after it, and before any value is handed back; the
  address is a path on the serving host and the response is length-bounded. The projection is proved before
  it is rendered — iteratively, bounded in depth, cycles, total values and total rendered characters, with
  every list charged before it is iterated — and what a consumer receives is what that proof captured, not
  the contract objects it was captured from. The schema itself is read once and whole when the contract is
  admitted, roots, components and choices together, and the published schema hash is taken over that frozen
  copy, so what was hashed is what is rendered; the identity a projected field must carry is still the
  contract's own admitted descriptor. One total covers a rendering, which is a schema plus one projection's
  values, so each projection continues from what freezing its schema spent. Every projected field must carry the schema's own descriptor at
  its own position; `FieldValue` is held to being a real tagged value, with absent distinct from a present
  empty list in the model and in the document; links are absolute http/https and artwork is that or a strict
  inline raster whose decoded bytes match the container it claims. A payload failure never rewrites the
  installation report the loader already decided. The client spells no media kind, no media assembly and no
  installation's own path, including no default payload address — an architecture rule holds it there.
- Residency and currency are separate client facts. A resident assembly the installation just read no longer
  names is orphaned: it stays in the page because a browser cannot unload, stops being served by `Find` and
  `ContractsOf`, and is reported under its own heading with the package that last published it and what that
  identifier means to the host now — withheld with the host's own reason, still offered without that file, or
  not mentioned at all. An orphan never makes an otherwise healthy installation incompatible. Reunion at the
  exact resident description fetches nothing; a different description is the same terminal
  `NameAlreadyResident` as replacing an assembly in place. `410 Gone`, `404 Not Found` and a transport failure
  are three distinct outcomes, none of them terminal. An unchanged `InstallationHash` decides whether to look
  and never decides what is true, so an unchanged installation costs one manifest read and no bytes.
  `EventKind.PluginStateChanged` drives that re-read on a connected tab through the loader's own serialized
  entry point and sheds the bytes the new installation no longer names, and store eviction discards only
  hashes the verified installation does not name.
- One client load is one transition. Orphan and owner bookkeeping is computed before the cancellable fetches
  and applied only with the report it publishes, so an abandoned load leaves residency and the report
  describing one installation. Reading an installation, shedding the bytes it no longer names and emptying
  the store are one serialized transaction with no second door: an architecture rule holds every operation
  that changes what a page holds — and every store read a page could pair with an installation — to the
  layer that owns it, naming operations rather than types so typed projection may still read what a loaded
  contract declares. Those operations are named for what they act on, because the rule reads text rather
  than symbols and a generic name would refuse a stream read or a cache clear that never touched the store;
  it matches the member rather than the call, so handing one on as a method group is the same door. Beside
  it, a component's markup and code-behind may not name the loader — its report is live state a render would
  pair with a committed snapshot — and the transaction is reached only from the view that shows what it
  produced. Each step is contained separately and failures are values kept in occurrence order,
  not one latest message. A transaction is numbered under the lease that runs it and seals its report, its
  post-sweep keys and its failures as one value handed back to its caller. What a view shows is that value,
  published by the one compare-and-swap that decides it — so a caller the scheduler resumed late cannot land
  an older installation on newer state, and what a page shows only ever moves forward — and read once per
  render. A
  subscriber refusing that announcement is reported beside the record rather than inside it, because the
  refusal happens after the value was handed over. Every event handler in this path observes the work it
  starts rather than discarding its task, because each records the failures it contains and only an unsound
  process could ride a dropped one. Cancellation means the caller's own token — a request timeout is an
  ordinary outcome that replaces the report it failed to read — and no boundary in the client's contract
  path contains an exhausted heap or stack, corrupted memory or a structured native failure.
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
- G06 is complete. `Arronix.Sdk` is the ordinary author package: it brings the runtime contract plus the
  generator as an analyzer, contributes no runtime assembly, and exposes no Host or Client dependency.
  Generated shapes, capture visitors, erased registrations, expression carriers, and provider-family markers
  remain CLR-visible only where a cross-assembly bridge requires it; they are hidden from completion lists,
  concrete author values implement bridge members explicitly, `CompiledShapes` is generated and hidden from
  ordinary completion lists, and a concrete media type publicly exposes no `Capture`. `ARX1003` reports a missing `partial` modifier at the media
  declaration, and `ARX1004` reports a reference set the platform types cannot be read from — an incomplete
  contract, or a second one — at the declaration that needed them, and nowhere else. A package-only external
  project has restored and compiled against `Arronix.Sdk 0.9.0` with no
  repository project reference, and the packed SDK contains only its readme, metapackage marker, and analyzer.

- G07 is split into three numbered sub-gates. G07.1 is complete: a package declares a client facet, the
  running installation publishes each offered assembly's exact bytes, identity, module and closure, the
  hosted server serves them content-addressed, and a real browser running the ordinary published client
  loads them from a clean store, reuses a warm store without refetching, and refuses a foreign contract
  line, a corrupted byte, a falsified identity, a falsified build and a malformed manifest — in every case
  before the runtime sees the payload. See `docs/research/g07/client-contract-loading.md`.
- G07.2 is complete. A
  page holding a verified installation reads one serialized entity through the contract it admitted, proves
  the projection before rendering it, and draws a typed Movie with artwork, ratings, lifecycle/status and
  collections, with no media or format assembly in the Client build. Both halves have been run: the server
  half is 25 checks in `eng/proofs/g07-client-contracts.sh`, and the browser half is
  `eng/proofs/g07-browser-proof.mjs` against the ordinary published client, whose observed run — including
  the inline poster decoding at the size it states, and the two defects that run found — is recorded in
  `docs/research/g07/client-payload-projection.md`. Independent review accepted the exact pushed commit after
  reproducing the mutation table and re-running the full .NET 11 rail. This gate claims nothing about cache
  update, removal and stale-tab behaviour (G07.3), about the external-consumer vertical (G07A), or about
  provider-result ingestion.
- G07.3 is open. Its hermetic lifecycle core is built — orphaning and its refusal, exact reunion, terminal
  replacement, the `410`/`404`/transport separation, selective store eviction, the unchanged-installation
  early-out, and the `PluginStateChanged`-driven re-read — but the gate names update, removal, stale cache
  and stale tab as exercises in a real browser, and none has run there. The gate stays open until they have.
- G07A's package, admission, identity and contract-loading legs were built ahead of the gate that carries
  them, and its browser-rendering leg is now built too. Two extensions authored outside the solution — a
  short-film media package in a domain plus entry assembly, and a separately built cataloger for it —
  compile against packed `Arronix.Sdk`, `Arronix.Abstractions` and `arronix.format.video` through an
  isolated local feed and a per-consumer empty package cache, with no project reference, source include,
  visibility grant, Host or Client dependency or warm cache. Both reach `Active` in an unmodified
  `Arronix.Api`; Host publishes the external kind's descriptor including `premiere`, a field only the
  external item declares; the separately built cataloger pairs against the admitted item type; the external
  domain assembly's own generated client contract is published and a real browser loads it, alongside the
  `arronix.format.video` contract its domain assembly depends on, from a clean store; the 37-check proof
  exercises that one load and does not repeat it against a warm store. A serialized `ShortFilm`, written by
  `eng/proofs/g07a-fixture` through that domain assembly's own generated entry point, now flows through the
  exact same unchanged generic
  `ContractPayloadLoader` and `ContractPayloadPanel` G07.2 built for Movies: the browser fetches it, projects
  it, and renders `premiere` and `lifecycle` — including a `premiered` date — with no first-party ShortFilm
  or Northmark knowledge anywhere in Host, Client, Api or the generators. Proving this found and fixed a real
  defect: `ContractPayloadLoader`'s only constructor is `internal`, and it was registered with the
  reflection-based `TryAddSingleton<ContractPayloadLoader>()`, which cannot see it — the payload panel had
  never actually resolved at runtime, in either installation, until the registration became a same-assembly
  factory. See `docs/research/g07/g07a-external-consumer.md`. G07A is not self-declared closed here; it
  remains pending independent acceptance.

The five platform services a running host needs are done and are no longer between G07.1 and G07.2: an
ordinary server composes `ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`, `IHostRuntimeInfo` and
`IOperatingSystemInfo`, and activates the independently published Video and Movies packages installed beside
it. The open G07 sub-gate is G07.3. Later gates cover compatibility evidence,
format/language/media interpretation, typed matching and policy, TV/Music/Books pressure tests,
durable state and acquisition, standard workflows, legacy removal, independent SDK proof, provider coverage,
and replacement readiness. Their order and acceptance criteria live only in the roadmap to avoid another
duplicated checklist drifting from current state.

## Technical debt

- `MediaKindModel`, legacy shapes, `ParsedRelease`, and quality ladders remain during migration.
- Lifecycle internals currently project as one composite field. The typed `Status` projection is top-level, but selected lifecycle milestones are not yet independent top-level sort/filter axes.
- The format-capability loading and versioning lifecycle needs a first-class manifest/package design before independently distributed format packages are promised.
- `ReleasePolicy<T>.Compile` still exposes builder callback choreography to media authors. Format defaults and media policy fragments must compose without making authors drive an internal compiler mechanism.
- Movies still declares likely-standard workbenches, browse defaults, and title ordering. Audit them against Television, Music, and Books and derive any behaviour which is not a genuine media difference.
- A media kind cannot currently be as small as its declaration suggests. `MediaType` supplies default
  `Matching` and `Querying` values which carry no key layer and no query tier, and admission validates that
  each carries at least one, so a kind with no matching or query differences still declares both. Their
  presence also makes Host demand the `matching` and `indexing` capabilities the author never asked for.
  Found by the G07A external consumer, whose `Shorts` kind declares one trivial layer and one trivial tier
  purely to satisfy this; it belongs with the G17A audit of Movies' likely-standard declarations rather than
  being patched per kind.
- `IClosedCataloger` and `IClosedCurator` remain public CLR types because a public closed provider contract
  cannot inherit a less accessible marker. They are `EditorBrowsable(Never)` binding SPI which authors never
  implement directly. Deriving the pairing from the implemented contract still needs one-time type inspection
  at registration; that is erasure at the sanctioned boundary, not authoring vocabulary.
- `FileBindingDefinition` currently expresses only `None` and `OnePerItem`; Television must settle the typed multi-unit/file cardinality instead of using a parallel legacy seam.
- `NormalizationOptions` and `IDiacriticFoldingProvider` remain for legacy implementations; new language-specific comparison/query/naming/sort behaviour belongs in `ILanguageDefinition` plugins.
- The five platform services are implemented and an ordinary `Arronix.Api` server now activates the
  independently published Video and Movies packages installed beside it; `Arronix.Api.Tests` stages both
  from their own publish and proves it through the real `Program` composition. Their residual debt is what
  the threat model already names and this work did not take: T-18, no quota on `ICacheProvider`,
  `IPluginPaths.TempFolder` or `IEventPublisher` publication rate, so a package can still exhaust memory,
  disk or the bus; and T-19, `IHostRuntimeInfo` tells a package more about the process than a package has
  been shown to need. Both are WP-T5/WP-T1 work, not gaps in what landed here.
- The G07 browser half is repeatable but not hermetic. `eng/proofs/g07-browser-proof.mjs` drives it and
  writes machine-readable evidence, so it is no longer a manual reading of a page, but it needs Playwright's
  Chromium and runs beside `eng/ci/run-tests.sh` rather than inside it: a browser download does not belong
  in the clean-repository proof.
- The trimmed client cannot start on .NET 11 preview 7: a trimmed publish fails during
  `WebAssemblyHost.RunAsyncCore` with an `InvalidCastException` from `PersistentServicesRegistry`, before any
  Arronix code runs. The pristine base commit `f943b8a0f` fails trimmed and starts untrimmed under the same
  SDK on a fresh origin, and a default template application publishes and starts trimmed, so trimming is the
  variable and the defect predates G07. `Arronix.Client.csproj` sets `PublishTrimmed=false`, so an ordinary
  `dotnet publish` produces an application that starts, at the cost of a much larger download.
  `TrimmerRootAssembly` for `Arronix.Abstractions` remains, inert, because a dynamically loaded contract
  binds to members the trimmer never saw. Restoring trimming needs the framework failure diagnosed first.
- `src/Arronix.Api/appsettings.json` had never declared `Arronix:Identity:ApplicationName`, which
  `HostIdentityOptions` requires, so the server failed options validation at startup. One line was added.
  The API had also only ever been started through `dotnet run --project`, which changes into the project
  directory before it launches its child process; started as the built assembly directly, with the process's
  own directory as its current directory, `appsettings.json` is not found there and the server fails the same
  validation again. `eng/proofs/g07a-external-consumer.sh` now starts it that way — the real server process,
  not a build wrapper's child — and sets `ASPNETCORE_CONTENTROOT` explicitly. `eng/proofs/g07-client-contracts.sh`
  still starts it through `dotnet run --project` and has not been changed.
- The current combined G07.2, G07.3-core and G07A merge rail reports 3,524 passed, 302 skipped, zero failed
  and zero inconclusive across 14 test projects. Of the skips, 301 are Movies cases and one is an architecture
  case; all are registered in the compatibility ledger. Every later
  passing-suite claim must report its observed skip count and ratchet result.
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
- The client's unchanged-`InstallationHash` early-out is a cost gate with no observable effect. Removing it
  entirely leaves every test passing, because the reuse path it precedes already returns before any fetch or
  hash for a resident entry, so the two do the same work. It is kept because it states the rule where a
  future per-entry check would otherwise make a `PluginStateChanged` storm expensive, and because its safety
  property — that an equal hash permits the question rather than answering it — is mutation-guarded. Its
  cost claim is not.
- `ClientContractPackage.ClosureHash` covers each assembly's identity and content hash and not its file name
  or its declarations, so `InstallationHash` can stand still while a host restates what a payload declares.
  The client is not exposed, because every reuse and early-out path compares the published declarations it
  holds rather than the hash. Widening the server-side hash is a separate decision and belongs with the
  generated-metadata work rather than here.
- The shared-assembly source-text screen is a heuristic. The structural rules — no executable platform type,
  no mutable or editable static state, nothing running on load, a reference closure limited to the universal
  contracts — are what prove the boundary; the spelling screen catches literal usage only.
