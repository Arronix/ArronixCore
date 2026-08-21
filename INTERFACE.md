# Arronix Interface Contract

## 1. Purpose

ArronixCore owns the contracts and host runtime for a plugin-composed media automation system. It defines how media domains, format capabilities, external providers, and generic orchestration meet without making the universal core own any particular medium or vendor.

Its external authoring promise is that a third party can implement a complete media domain through a small, intuitive, strongly typed SDK while Arronix derives the common application machinery. That small surface must retain the domain's full expressive shape and practical *arr-class feature coverage; it must not expose Host mechanics or purchase simplicity through lossy descriptors.

## 2. Responsibilities

Current ownership:

- media-neutral identities, plugin lifecycle, provider families, acquisition records, release-policy primitives, and presentation descriptors;
- plugin discovery, compatibility and capability admission;
- generic host orchestration, storage seams, scheduling, provider dispatch, and API projection;
- derivation of standard media operations, generic presentation, and runtime dispatch from typed owner-authored definitions;
- an authoring contract whose cognitive load is proportional to a plugin's genuine semantic differences rather than the size of the platform;
- reference media and format extensions.

Potential future ownership includes persistent storage, authenticated multi-user hosting, dynamic .NET client definition loading, and independently distributed format packages.

The core does not own movie, television, music, book, video, audio, document, codec, catalog-vendor, indexer-vendor, downloader-vendor, or notifier-vendor semantics.

## 3. Domain model

- `MediaType<TItem,TTarget,TRelease,TParser>` is the abstract instance definition pairing a media-owned durable item, acquisition intent, interpreted release, and parser. Its primary constructor owns the stable kind identifier, singular/plural names, non-empty format composition, minimum-availability selection, and file binding; `OnePerItem` is the default file relationship.
- `MediaType<TItem,TTarget,TRelease,TParser>` is the current typed media-definition boundary. Its typed override values declare identity, groups, additional selections, semantic searches, matching, policy, querying, naming, summaries, intent exceptions, workbenches, and derivations without a whole-definition replay builder, action transcript, or capability badge. `TParser : IReleaseParser<TRelease>` is the executable parsing declaration. The outstanding SDK gaps in `CONTEXT.md` prevent treating this shape as frozen or independently validated.
- `IMediaEntity` supplies the minimum common entity floor. `MediaItem<TReleaseTimeline,TReleaseStage>` is the directly usable common item. `MediaItem<TItem,TReleaseTimeline,TReleaseStage>` preserves an extending item's exact type in collection relationships. `MediaCollection<TItem>` is the common group.
- `IReleaseTimeline<TReleaseStage>` exposes a media-owned lifecycle's current typed stage without flattening its milestones or availability behaviour.
- `CompiledShapeCatalog` is generated compiler/Host binding SPI containing closed getters. It is not part of the semantic authoring vocabulary or a wire schema; normal extension authors do not implement it, and Host projects its validated fields into ordinary descriptors for generic consumers.
- `ReleaseTarget<TItem>` directly represents a one-item request. `Release<TRepresentation>` directly carries the common title, year, edition, and format-owned representation. Media-specific target or release classes exist only for additional coverage or release facts.
- `IRepresentation` is a format-owned artifact description carried by a release.
- `ReleaseListing` is raw output from an `IIndexer`.
- `TargetMatch<TTarget>` describes typed coverage of an acquisition target.
- `ReleaseOption<TTarget,TRelease>` joins a listing, interpreted release, and target match.
- `ReleasePolicy<TRelease>` provides hard admission, lexicographic preference, and bounded facet comparison.
- `ICataloger<TItem>` supplies authoritative shaped items. Its `ICataloger` floor recognizes identifiers from its own namespace when they occur in release text. `ICurator<TItem>` proposes shaped items for inclusion.
- `IIndexer`, `IDownloader`, and `INotifier` remain media-neutral provider families.
- `ItemInfo` is the common title/overview value and `Localized<T>` attaches a language to a typed payload. `Rating`, `RatingScale`, and `ContentCertification` preserve common semantics without naming a media kind or vendor.
- `WorkbenchProposal<TRow>` and `WorkbenchCommit<TRow>` retain workflow-owned rows; their non-generic counterparts are wire projections.
- `ILanguageDefinition` owns one language's title comparison, provider-query, file-name spelling, and sort transformations.
- `StandardMediaAction` names platform operations independently of their string wire identifiers. Host derives their `ActionDescriptor` values from a media type's compiled shape.
- `ActionRequest` carries typed item references plus descriptor-defined textual parameter values across Client and API.

## 4. Public interfaces

The external surface has distinct audiences even where migration currently places their public types in one assembly:

- the semantic authoring SDK used by media, format, language, and provider authors;
- the operational provider SPI used for capability-scoped invocation;
- generated/compiler/Host binding SPI used to cross assembly and type-erasure boundaries; and
- serializable HTTP, SignalR, and Client wire contracts.

All runtime, SDK, plugin, Host, API, Client, and test projects currently target `net11.0` and require exact
.NET 11 Preview 7 SDK `11.0.100-preview.7.26381.103` pinned by `global.json`. `Arronix.Generators` alone targets
`netstandard2.0` as an analyzer dependency; it is not a runtime compatibility escape hatch.

Public visibility does not make generated getters, binder visitors, erased registrations, `System.Type`
carriers, expression trees, or runtime descriptors part of the supported authoring vocabulary. Normal plugin
authors must not implement or reason about those mechanics. The typed CLR definitions remain the sole
semantic source; generated binding data and serializable descriptors are one-way projections.

Media plugins register with:

```csharp
registry.AddMediaType<TMediaType>();
```

`TMediaType` derives from `MediaType<TItem,TTarget,TRelease,TParser>`, supplies its invariant identity,
names, formats, minimum availability, and file binding to the base constructor, and overrides only optional
or repeatable media-specific members. Registration
captures one definition instance. `TParser` implements the static
`IReleaseParser<TRelease>.Parse(ReleaseParseContext)` contract, which returns the exact `TRelease` type.

Provider plugins register implementation types, including shaped pairs:

```csharp
registry.AddCataloger<TItem, TCataloger>(descriptor);
registry.AddCurator<TItem, TCurator>(descriptor);
```

That repeated `TItem` is current registration SPI, not the ergonomic end state. An author should state a
closed media/provider relationship once; compiler or Host infrastructure should infer or generate the
erased registration without another manually synchronized type argument.

Language plugins register implementation types for Host DI activation:

```csharp
registry.AddLanguage<TLanguage>();
```

The Host activates provider types through DI only after manifest and capability admission.

Installed format capabilities must contribute representation recognition, probing, and policy for their
owned facts; language capabilities contribute linguistic mechanics. A media parser owns media structure
and opts into those capabilities without copying their vocabulary or orchestrating their runtime
implementation. This executable composition is an incomplete migration boundary today.

Standard action descriptors are always derived for typed media. Execution is resolved separately through
Host capabilities. Monitoring is currently executable; an unimplemented standard operation returns 501
and is never reported as accepted.

## 5. Invariants

- Universal contracts and Host remain media- and vendor-neutral.
- The public SDK is minimal through complete common primitives, typed defaults, generic relationships, and derivation. It is never made small by erasing owner-shaped data, requiring string dictionaries, or moving implementation mechanics into plugin code.
- Plugin authors state their domain types and genuine differences once. Host derives standard behaviour and may erase the result internally, but no runtime or wire projection becomes a second source of semantic truth.
- An abstraction is not complete until its meaning is preserved through authoring, registration, DI activation, runtime execution, persistence where relevant, wire projection, Client use, tests, and documentation. A descriptor or passing unit test does not prove production wiring.
- Full *arr feature coverage and observable ecosystem compatibility are acceptance constraints. Intentional differences must be explicit and tested; difficult legacy behaviour may not be silently discarded to simplify the common model.
- The complete public contract is one pre-1.0 surface. Stability is expressed by the assembly version and plugin manifest range; no per-type experimental marker, diagnostic registry, or compiler opt-in exists.
- A media type owns its entity shape; catalogers and curators return that exact type.
- A cataloger owns its external identifier namespace and marker spellings. Media parsers consume validated `ExternalIdReading` values and never duplicate vendor marker regular expressions.
- Every item and group exposes `Key`, `ExternalIds`, `Title`, `TitleLanguage`, `Overview`, and `Artwork` through `IMediaEntity`; media-specific facts extend that floor as ordinary typed properties.
- The common item class remains fully visible and strongly typed. Release dates and availability behaviour live in a media-owned lifecycle object; typed release stage, catalog presence, and plural collection membership are common item facts.
- Common closed item, collection, target, and release classes carry the complete reusable shape. A media type may still publish an empty nominal item closure when that stable domain name is what its cataloger, curator, target, collection, and future extensions pair with; `Movie` does exactly this without repeating a property.
- Media schema discovery is a compile-time operation. Renaming or changing a projected property recompiles generated closed getters; Host does not reflect across plugin properties to invent the schema at startup.
- Reusable CLR type names are not semantic identifiers. Item level and token words come from the media type's declared name; grouping axes come from declared group semantics.
- A media kind identifier is explicit durable identity. It is not inferred from singular or plural display wording.
- Cross-type relationships are closed generic values. Host may erase them only while deriving its internal runtime projection.
- Media authoring exports no whole-definition replay builder or `IUses...` capability badges. Optional and repeatable relationships are empty or populated typed collections on the definition. The current public release-policy builder is migration debt; format and media policy fragments must eventually compose without author-driven compiler choreography.
- Parser regression corpora are test assets. They are not plugin contracts, runtime model members, or admission inputs.
- Format-specific facts and vocabularies live in format assemblies.
- No string dictionary may substitute for a known media-owned schema.
- No executable policy, `System.Type`, or delegate crosses a wire descriptor.
- Plugin contribution identity is plugin-qualified and deterministic.
- Selection is independent of provider result order.
- Unknown evidence remains unknown; it is never promoted to a favorable default.
- Audio and other multi-valued artifact facts are not collapsed before policy sees them.
- Typed media parsers do not carry language-specific word lists or transliteration rules.
- Media search declarations do not carry source-protocol category numbers or vendor routing codes.
- Artwork may be carried by a typed workbench row. A consumer chooses its presentation; the semantic contract does not infer layout.
- Media extensions do not declare the platform action catalogue or its wire keys.
- Client binds affordances through `StandardMediaAction`; only the API route and action request serialize string identifiers.
- A plugin manifest owns package identity, compatibility, dependencies, capabilities requiring admission, and explicit security grants. It is not a second media schema; derivable kinds, fields, tokens, policies, and actions are generated or mechanically validated rather than manually restated.
- Every known compatibility omission has a stable semantic ledger entry with provenance. Published semantics,
  bindings, and source digests are compared with the prior committed ledger. A skipped fixture, renamed test,
  coordinated ledger-and-source weakening, or replacement without independently anchored executable evidence
  must fail. The same-build solution binlog is the authoritative practical record of actual `Csc` source and
  embedded inputs plus effective warning policy and configuration. Every execution and required sentinel binds
  an exact NUnit leaf to the declared CLR method in NUnit's exact executed assembly and its associated Portable
  PDB; visible method sequence points and exact embedded source bytes for primary and support documents must
  match the locked repository files. These layers establish same-build provenance, not cryptographic or hermetic
  build attestation. Test output cannot certify itself by echoing a ledger digest.
  Replacement topology and outcome are
  independent contracts: the outcome must match the source disposition and make its claimed requirement,
  ownership, or scope transition. Verified acyclic one-to-one replacement chains close transitively without
  changing prior edges; partitions cannot close until aggregate composition has an explicit semantic contract.
  Required decisions are pinned current owner-decision sources attached to the decided requirement and already
  present in the prior ledger, not free-form approvals added during closure.
- The current full-rail skip count is exactly 302. It may only decrease relative to the prior ledger, and a newly
  passing baseline case cannot silently regress to skipped later. The four published Movie parser rows `t012`,
  `t013`, `t023`, and `t027` remain isolated from the ordinary representative corpus as immutable support data.
- The required proof-sentinel registry has eight columns and exactly three durable rows at G01. It is append-only
  by stable identifier: published rows may not be removed or rebound, and transient implementation outcomes do
  not qualify as permanent sentinels. A new row is valid only when its exact NUnit leaf passes from its compiled
  pinned primary and support sources.

## 6. Side effects

The loader reads plugin manifests and assemblies, creates isolated load contexts, and may quarantine invalid plugins. Host provider calls may perform network and filesystem activity only through capability-scoped services. The current media store and queue are in memory; restart durability is not promised.

## 7. Dependency boundaries

- `Arronix.Abstractions` depends on nothing.
- format assemblies depend on Abstractions.
- language implementation assemblies depend on Abstractions.
- media extensions depend at runtime on Abstractions and the format capabilities they compose. They may take `Arronix.Generators` only as an analyzer with `ReferenceOutputAssembly=false`.
- `Arronix.Common` and `Arronix.Plugins` depend toward Abstractions.
- Host depends on Abstractions, Common, and Plugins, but not on a media or format implementation.
- Client depends on Abstractions only.
- vendor provider implementations must not be placed in Host, Abstractions, a format assembly, or a media definition.

Direct plugin-to-plugin type sharing is not yet a supported stable contract. This is a blocking SDK gap:
separate provider packages need stable media-definition package identity and dependency/version rules, one
CLR type identity across Host and dynamically loaded Client code, and compiler-checked pairing without
duplicate loads or a fallback to string projection.

## 8. Lifecycle and execution

While a media extension builds, `Arronix.Generators` emits its closed item, group, and workbench-row projections. At startup the loader validates manifests and static references, constructs a capability-scoped context, invokes the plugin module, seals registration, and hands admitted registrations to Host. Host validates and derives its kind-blind model from those generated projections, activates provider and language implementation types through DI, then publishes admitted kinds/providers/languages atomically. Failure quarantines the plugin; it does not terminate Host.

Replacement-grade execution must continue from those admitted types through catalog/curation, acquisition
targeting, semantic queries, raw indexer listings, media interpretation plus format/language contributions,
typed coverage matching, policy and cutoff selection, download, import/probing, library persistence, naming,
events, notification, and one-way API/Client projection. That lifecycle is the acceptance path, not a claim
that every stage is currently implemented.

## 9. Anti-goals

- reproducing a specific *arr application's class graph or wire API;
- discarding observable *arr behaviour or Scene/PTP ecosystem semantics merely because their historical implementation is coupled; behavioural compatibility is a goal even when source and wire compatibility are not;
- forcing each media plugin to restate common platform operations or reconstruct Host machinery;
- reducing SDK surface area by flattening typed domain ownership into descriptors, reflection conventions, or string-keyed extension points;
- declaring a locally represented feature complete while production execution bypasses it;
- feature-specific workarounds that weaken a shared invariant and become mandatory precedent for later plugins;
- a universal field-bag media schema;
- Host-owned codec or release-name vocabulary;
- globally ordered evidence sources;
- treating common release labels as one scalar quality ladder;
- constructing provider instances inside plugin registration modules;
- manually restating derived media schema, tokens, policies, or actions in a plugin manifest;
- freezing a Movies- or video-shaped abstraction before Television and a non-video kind pressure-test it;
- vendor-specific identifier marker syntax or endpoints in a media type definition;
- language-specific articles, stop words, or transliteration tables in a media type definition;
- a second per-type stability taxonomy layered over ordinary pre-1.0 semantic versioning.

## 10. Agent guidance

Read `CONTEXT.md`, `docs/design/typed-media-north-star.md`, `docs/design/typed-media-roadmap.md`, and the relevant owner-ledger entries before changing media, quality, provider, plugin, or client boundaries. Use Television as the pressure test for any abstraction that appears sufficient for Movies, and use Books and Music to distinguish true platform commonality from video-family coincidence. Trace every claimed abstraction vertically through registration, DI, runtime use, wire projection, and tests before calling it complete. Preserve typed owner boundaries even when a string projection is easier. Treat owner questions and candidate examples as requests for verification rather than approvals unless the owner explicitly adopts them. Update this file with any public semantic change, `HISTORY.md` for the decision, and `CONTEXT.md` for the resulting current state. Use the SDK pinned by `global.json` and run `eng/ci/run-tests.sh`; report the exact enabled, passed, failed, skipped, inconclusive, and ratchet counts.

Acceptance requires an independently authored media extension which uses only the supported SDK and chosen
format/language packages, states every semantic relationship once, and does not implement generated bridges
or manual wire schema. Television must validate aggregate/unit and set coverage; Music or Books must reject
video-family assumptions. Feature-coverage claims require a maintained behavioural matrix, production-path
tests, representative Scene/PTP regression cases, and explicit intentional divergences. Arronix is not
replacement-ready while typed provider pairing, release interpretation/selection, persistence, or standard
action execution remains unwired.
