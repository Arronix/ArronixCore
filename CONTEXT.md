# Arronix Current Context

## Purpose and state

Arronix is a clean-sheet, pre-alpha media automation platform. A media kind owns its typed domain while the host owns generic orchestration. The product goal is a consolidated, extensible replacement for the *arr applications with their practical feature coverage preserved, not merely a plugin framework or a simplified demonstration of their common path. The repository builds as one .NET 11 Preview 7 solution pinned to SDK `11.0.100-preview.7.26381.103`; persistence, authentication, production provider packages, and an end-to-end acquisition flow are not complete.

The plugin SDK is part of the product. A third party should be able to own a Sonarr-, Radarr-, Lidarr-, Readarr-, or new-media-style application through a small set of intuitive, strongly typed abstractions. Small means that Arronix derives common behaviour and supplies sound defaults; it never means erasing media shape, moving semantics into string bags, or making an author understand Host implementation mechanics.

The contract line is `0.8.0`. First-party plugin manifests declare `>=0.8 <0.9`. The whole public surface is pre-1.0; it has no second per-type experimental tier or compiler opt-in mechanism.

## Current architecture

- `Arronix.Abstractions` contains media-neutral contracts and has no project or package references.
- `Arronix.Common` contains reusable host-side implementations.
- `Arronix.Plugins` owns manifests, capability admission, assembly isolation, registration capture, and the
  post-admission seam through which Host returns the inventory it actually admitted.
- `Arronix.Host` owns DI composition, generic engines, registries, scheduling, storage, and plugin activation.
- `Arronix.Api` and `Arronix.Client` are the HTTP and Blazor WebAssembly edges.
- `Arronix.Format.Video` owns video representation types, vocabulary, extensions, and default release-policy contributions.
- `Arronix.Language.Reference` is a separately loadable language capability with English, German, and French implementations.
- `Arronix.Generators` emits closed entity readers and descriptor projections while a media extension compiles. It is an analyzer-only build dependency and is never loaded as a plugin runtime dependency.
- `Arronix.Compatibility.Ratchet` validates the canonical compatibility ledger against fresh test results and its prior committed form. The ledger under `verification/compatibility` gives every known omission a stable semantic identity, immutable published binding and expectation, provenance source, and explicit replacement path. The same-build solution binlog is the authoritative practical record of the actual `Csc` inputs, embedded inputs, warning policy, and build configuration. Each registered execution must resolve from its exact NUnit leaf to the declared CLR method in NUnit's exact executed assembly; the associated Portable PDB must bind that method to the primary and support documents, whose embedded source bytes must exactly match the locked repository files. This is layered same-build provenance, not a cryptographic or hermetic-build attestation claim. Replacement topology is independent of its semantic outcome; outcome must match the source disposition and requirement transition. Acyclic one-to-one replacement chains close to a fixed point, so a published witness can later be retired without rewriting earlier edges. Partition records remain non-closing until aggregate semantic composition is modeled. Required owner decisions resolve to pinned prior-ledger sources attached to the decided requirement. Passing tests do not self-attest proof through an output marker.
- Media extensions may reference Abstractions and the format capabilities they compose. Host and Client do not reference Video.
- `eng/ci/run-tests.sh` is the local and hosted-CI proof rail: locked restore, one Release warnings-as-errors solution build with its binlog retained as compiler-input evidence, a non-empty NUnit result from every discovered test project, exact NUnit-leaf/method/assembly/PDB/source binding, an exact 302-skip ratchet, and current-plus-prior compatibility validation. The eight-column required-test registry is append-only and contains only three durable proof sentinels. Package lock files and the consumed-only central package graph are checked in.

## Active invariants

- Plugin authors declare owned domain types and genuine differences. Arronix derives common actions, orchestration, projections, validation, and runtime dispatch from that source. The obvious authoring path must also be the ownership-correct path.
- Authoring stays rich while its surface stays small. Strong generic relationships, complete common base types, constructor-owned invariants, typed defaults, and compiler-generated projections reduce ceremony; field dictionaries, duplicated descriptors, reflection conventions, builder transcripts, repeated type arguments, and implementation-specific callbacks do not.
- Type erasure is a one-way internal compilation step. A typed media, format, language, or provider relationship may become a kind-blind Host or wire projection only after its compile-time relationship has been validated; the erased representation never becomes an alternative authoring model.
- A media type derives from `MediaType<TItem,TTarget,TRelease,TParser>`; the first three types are respectively the durable catalog/library entity, ephemeral acquisition intent, and interpreted publication. `TParser : IReleaseParser<TRelease>` binds the media-owned parser at compile time.
- The `MediaType` primary constructor captures its stable kind identifier, singular/plural display names, non-empty format composition, typed minimum-availability selection, and file binding. File binding defaults to `OnePerItem`; other relationships must state their shape explicitly. Kind identity is never derived from mutable display wording. The remaining optional or repeatable media-specific declarations are virtual values for identity, groups, additional selections, searches, matching, release policy, query planning, naming, summaries, intent exceptions, workbenches, and derivations. Parsing is the deliberate exception: `IReleaseParser<TRelease>.Parse` is static abstract because the parser type itself is the executable declaration. `CompiledShapes` is the generator-owned abstract member; authors do not implement it. There are no public whole-media replay builders, per-kind action transcripts, parse-declaration DSLs on typed media, `IUses...` capability badges, or test corpora on the runtime contract.
- `IMediaEntity` is the minimum interface shared by items and groups. `MediaItem<TReleaseTimeline,TReleaseStage>` is directly usable; `MediaItem<TItem,TReleaseTimeline,TReleaseStage>` retains the exact derived item type for relationships. `MediaCollection<TItem>` is the common group class.
- `ReleaseTarget<TItem>` is the concrete one-item acquisition target. `Release<TRepresentation>` is the concrete common release carrying title, year, edition, and format-owned representation. Media types use the closed common types directly unless they add real coverage or release facts. Television's set-shaped target and coordinate-bearing release are the counterexamples which justify media-owned types.
- The common item class exposes identity, titles and translations, years, description, runtime, principal organization, certification, genres, keywords, website, preview, artwork, popularity, ratings, a media-owned lifecycle object, its typed release stage, catalog presence, and plural typed collection membership. `IReleaseTimeline<TReleaseStage>` makes the lifecycle-to-stage relationship compiled rather than conventional.
- Entity and workbench-row descriptor shapes are generated from their CLR definitions at build time. Host consumes generated closed getters and validates the result; it does not discover media schema with property or attribute reflection.
- `ItemInfo` is the common localized title/overview payload. `Localized<ItemInfo>` is used when no media-specific localized facts exist.
- The public contract has no second non-generic `IMediaType`. Host-only `IMediaTypeRuntime` is the kind-blind bridge.
- `ICataloger<TItem>` and `ICurator<TItem>` return the media type's own shaped items. Field dictionaries are not an alternative contract. The non-generic `ICataloger` floor recognizes its own external-identifier markers locally; media parsers receive those typed readings and do not embed catalog-vendor marker vocabulary.
- Provider registrations carry implementation types. The host activates admitted providers through DI with the capability-scoped `IPluginContext`.
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
- The packaged Movies extension survives the complete loader pipeline. `IPluginAdmissionCheck.Admit` returns a `PluginAdmissionResult` carrying an `AdmittedInventory`: one entry per `MediaKindId` Host actually registered, each holding that admitted kind's own derived `NamingToken` collection read back from its `RegisteredMediaKind`. Late declaration agreement, scheduled-job kind association, cross-installation duplicate-kind checking, and token ownership all consume that inventory. Legacy `IMediaShapeProvider` registrations retain a transitional path used only when no Host admission ran; after admission the inventory is the authority. Token ownership is claimed per admitted kind through `TokenClaimRequest`, so an extension supplying several kinds cannot claim the cross product of its kinds and its whole vocabulary. Any quarantine after the claim releases it, and Host withdrawal gives back token ownership alongside kinds, providers, languages, jobs, and health contributors. The real packaged-output Host test proves Active state, the admitted kind, exact per-kind token ownership, that the admitted item type came from the extension's own `PluginLoadContext` rather than a direct binder call, atomic withdrawal on a planted late token conflict and on a restaged manifest declaring an unsupplied kind, and release on `StopAsync`.
- `src/Arronix.Plugin.Movies/plugin.json` carries only manifest-owned concerns: manifest schema version, package identity, operator-facing name, version, description, the Arronix contract range, the entry assembly, and explicit capability grants. Its `mediaKinds`, `identifiers`, `tokens`, and `policies` are gone, and `actions` never existed. Manifest validation permits those omissions; a manifest that does state kinds or tokens is still held to them exactly, in both directions, against the admitted projection. Capabilities and security grants remain explicit manifest-owned least-privilege requests, because a privilege cannot be derived from code that has not been allowed to run.
- Books, Music, and Television manifests still carry `mediaKinds`, `identifiers`, and `tokens`. Those are transitional declarations belonging to their legacy media paths and are removed with their conversions, not here.
- The typed Movie parser currently materializes common release text facts but does not compose format-owned recognizers into a populated `Video` representation. No production typed cataloger supplies catalog-owned embedded-identifier readings. The manifest's related capability claims therefore outrun the executable production path.
- Typed release policy and deterministic selection exist, but production acquisition does not yet materialize typed releases and call the selector end to end.
- No production typed cataloger or curator ships yet. Their typed registration, DI activation, and catalog-owned identifier-reading boundary exist and are tested.
- Typed workbench proposal/commit values and generic standard rows exist, but the current `IMediaItemSource` execution seam still projects proposals and commits through the kind-blind wire form.
- Standard action dispatch is capability-based. Host currently executes `SetMonitoring` against `IMediaStore`; operations needing acquisition scheduling, catalog refresh, filesystem mutation, removal, or exclusion storage return an explicit 501 until those capabilities exist.

## Completion and continuity discipline

- A model, descriptor, generated projection, unit test, or registration seam is not by itself a completed feature. Completion requires the same semantics to survive authoring, generic capture, plugin admission, DI activation, Host execution, persistence where applicable, API/wire projection, Client consumption, tests, and boundary documentation. Any bypass or parallel legacy path must be reported as active migration work.
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
- G02 is complete: the real packaged Movies extension reaches `Active` through discovery, typed admission,
  late agreement, token ownership, and publication; its derived tokens are owned once each by the admitted
  kind; a late failure withdraws every committed contribution atomically; the acceptance path is the real
  isolated load context rather than a direct binder call; and its manifest is no longer a second media
  definition. It does not claim that the parser, selector, providers, persistence, or Client vertical works.
- G03 is active: establish package dependency/version rules and one exact CLR identity before any separately
  shipped typed provider is treated as viable.

The later gates cover provider pairing, hidden binding SPI, dynamic typed Client loading, compatibility
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
- `AddCataloger<TItem,TCataloger>` and `AddCurator<TItem,TCurator>` repeat an item relationship already closed by the implementation contract. Registration ergonomics should infer or generate the erased pairing.
- `FileBindingDefinition` currently expresses only `None` and `OnePerItem`; Television must settle the typed multi-unit/file cardinality instead of using a parallel legacy seam.
- `NormalizationOptions` and `IDiacriticFoldingProvider` remain for legacy implementations; new language-specific comparison/query/naming/sort behaviour belongs in `ILanguageDefinition` plugins.
- The generator rejects non-partial media declarations through compiler diagnostic `CS0260`; it does not yet emit a dedicated Arronix diagnostic explaining the authoring requirement.
- The current one-command full-solution run (2026-08-24) reports 2,127 passed, 302 skipped, zero failed, and zero inconclusive from 2,429 total cases across 11 test projects. Of the skips, 301 are Movies cases and one is an architecture case; all are registered in the compatibility ledger. This verifies the checked-in solution graph and enabled tests, not the unwired production capabilities above; every later passing-suite claim must report its observed skip count and ratchet result.
