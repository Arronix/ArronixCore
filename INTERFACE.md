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
- reference media, format, and language extensions, plus the independently packaged TMDb provider.

Potential future ownership includes persistent storage, authenticated multi-user hosting, dynamic .NET client definition loading, and independently distributed format packages.

The core does not own movie, television, music, book, video, audio, document, codec, catalog-vendor, indexer-vendor, downloader-vendor, or notifier-vendor semantics.

## 3. Domain model

- `MediaType<TItem,TTarget,TRelease,TParser>` is the abstract instance definition pairing a media-owned durable item, acquisition intent, interpreted release, and parser. Its primary constructor owns the stable kind identifier, singular/plural names, non-empty format composition, minimum-availability selection, and file binding; `OnePerItem` is the default file relationship.
- `MediaType<TItem,TTarget,TRelease,TParser>` is the current typed media-definition boundary. Its typed override values declare identity, groups, additional selections, semantic searches, matching, policy, querying, naming, summaries, intent exceptions, workbenches, and derivations without a whole-definition replay builder, action transcript, or capability badge. `TParser : IReleaseParser<TRelease>` is the executable parsing declaration. The outstanding SDK gaps in `CONTEXT.md` prevent treating this shape as frozen or independently validated.
- `IMediaEntity` supplies the minimum common entity floor. `MediaItem<TReleaseTimeline,TReleaseStage>` is the directly usable common item. `MediaItem<TItem,TReleaseTimeline,TReleaseStage>` preserves an extending item's exact type in collection relationships. `MediaCollection<TItem>` is the common group.
- `IReleaseTimeline<TReleaseStage>` exposes a media-owned lifecycle's current typed stage without flattening its milestones or availability behaviour.
- `CompiledShapeCatalog` is generated compiler/Host binding SPI containing closed getters. It is not part of the semantic authoring vocabulary or a wire schema. The generated `CompiledShapes` override remains public to preserve the published executable-generator proof, but is marked `EditorBrowsable(Never)` and is never author-implemented. Capture is an explicit hidden-interface operation and is absent from concrete media definitions' public surface. Host projects the validated fields into ordinary descriptors for generic consumers.
- `ReleaseTarget<TItem>` directly represents a one-item request. `Release<TRepresentation>` directly carries the common title, year, edition, and format-owned representation. Media-specific target or release classes exist only for additional coverage or release facts.
- `IRepresentation` is a format-owned artifact description carried by a release.
- `ReleaseListing` is raw output from an `IIndexer`.
- `TargetMatch<TTarget>` describes typed coverage of an acquisition target.
- `ReleaseOption<TTarget,TRelease>` joins a listing, interpreted release, and target match.
- `ReleasePolicy<TRelease>` provides hard admission, lexicographic preference, and bounded facet comparison.
- `ICataloger<TItem>` supplies authoritative shaped items. Its `ICataloger` floor declares the external identifier scheme it is the authority for and recognizes identifiers from that scheme when they occur in release text. Host captures that declaration at registration and rejects a reading in another scheme. `ICurator<TItem>` proposes `CuratedReference` values — a catalog identity, and optionally the curator's own `CuratedEntryId` — never items. Each contract answers its own closed pairing, so `TItem` is named once by the author and read back by registration.
- `ProviderCatalogEntry` is what a consumer configures a provider from: the host-minted `ProviderId`, the family its registration fixed, and the extension's own `ProviderDescriptor`.
- `IIndexer`, `IDownloader`, and `INotifier` remain media-neutral provider families.
- `ItemInfo` is the common title/overview value and `Localized<T>` attaches a language to a typed payload. `Rating`, `RatingScale`, and `ContentCertification` preserve common semantics without naming a media kind or vendor. `RatingScale` names its `(minimum, maximum)` constructor as the one a deserializer rebuilds it through: it is a validated interval with no settable member, so a reader that used the implicit parameterless constructor would produce zero-to-zero and the rating carrying it would then fail its own validation.
- A derived value is not serialized. `MediaItem.Status`, `MovieReleaseTimeline.AvailableOn` and `Stage`, `Rating.NormalizedValue`, and `RatingScale.IsValid` are computed from values a payload already carries, so writing them beside those values would give an untrusted payload two ways to state one thing. A consumer reads them off the item it deserialized, which recomputes them. A get-only value a constructor writes — a rating's source, value, scale, voice and sample size — is authoritative and is still serialized.
- `WorkbenchProposal<TRow>` and `WorkbenchCommit<TRow>` retain workflow-owned rows; their non-generic counterparts are wire projections.
- `ILanguageDefinition` owns one language's title comparison, provider-query, file-name spelling, and sort transformations.
- `StandardMediaAction` names platform operations independently of their string wire identifiers. Host derives their `ActionDescriptor` values from a media type's compiled shape.
- `ActionRequest` carries typed item references plus descriptor-defined textual parameter values across Client and API.
- `ClientContractManifest`, `ClientContractPackage`, `ClientContractAssembly`, and `ClientContractRefusal` state what a browser may load from a running installation and what it may not: the universal contract identity it must already carry, one hash over the whole installation, per package its client-safe assemblies with their exact content hash, CLR identity, module version identifier and length plus its transitive client dependency closure in load order and one hash over it, and per withheld facet the reason, the admitted assemblies it cannot reach, the declared files nothing admitted, and every required package whose withdrawal caused it. Package identifiers are `PluginId`, not text.

## 4. Public interfaces

The external surface has distinct audiences even where migration currently places their public types in one assembly:

- the semantic authoring SDK used by media, format, language, and provider authors;
- the operational provider SPI used for capability-scoped invocation;
- generated/compiler/Host binding SPI used to cross assembly and type-erasure boundaries; and
- serializable HTTP, SignalR, and Client wire contracts.

All runtime, SDK, plugin, Host, API, Client, and test projects currently target `net11.0` and require exact
.NET 11 Preview 7 SDK `11.0.100-preview.7.26381.103` pinned by `global.json`. `Arronix.Generators` alone targets
`netstandard2.0` as an analyzer dependency; it is not a runtime compatibility escape hatch.

Extension authors reference the packaging-only `Arronix.Sdk`. It supplies `Arronix.Abstractions` as its sole
runtime dependency and carries `Arronix.Generators` only under `analyzers/dotnet/cs`; the SDK contributes no
runtime assembly. Authors then add only the format, language, and media-domain packages their extension
actually composes.

An admitted package is handed five platform services and nothing more of the host: `ICacheProvider`,
`ITelemetryEmitter`, `IEventPublisher`, `IHostRuntimeInfo` and `IOperatingSystemInfo`. Three of them are
wrapped in that package's authority — its own cache namespace, its own attribution, its own publication and
subscription rights. The other two are immutable, shared, read-only facts about the process and the machine,
and answer only what the host can establish: a fact it cannot establish is reported absent rather than
guessed. All five are real implementations an ordinary server composes, not seams a test fills in.

The CLR types needed to cross generated and Host assembly boundaries remain public only where the runtime
requires that visibility. They and their erased members are `EditorBrowsable(Never)`, and concrete semantic
values implement visitor dispatch and `System.Type` carriers explicitly. Normal plugin authors neither see
nor implement generated getters, binder visitors, erased registrations, provider-family markers, expression
carriers, or runtime descriptors. Typed CLR definitions remain the sole semantic source; generated binding
data and serializable descriptors are one-way projections.

The public `IPluginAdmissionCheck` and `IPluginAdmissionAttempt` types are a cross-assembly Host/loader
transaction seam, not extension-authoring SDK. `Arronix.Plugins` is Host infrastructure; a plugin package
receives `Arronix.Abstractions` through `Arronix.Sdk` and references the format or language contracts it
composes, never that loader assembly.

Media plugins register with:

```csharp
registry.AddMediaType<TMediaType>();
```

`TMediaType` derives from `MediaType<TItem,TTarget,TRelease,TParser>`, supplies its invariant identity,
names, formats, minimum availability, and file binding to the base constructor, and overrides only optional
or repeatable media-specific members. It is declared `partial`; otherwise the packaged generator reports
`ARX1003` at that declaration. Registration captures one definition instance through the hidden generated
bridge. `TParser` implements the static
`IReleaseParser<TRelease>.Parse(ReleaseParseContext)` contract, which returns the exact `TRelease` type.

Provider plugins register implementation types, including shaped pairs:

```csharp
registry.AddCataloger<TCataloger>(descriptor);
registry.AddCurator<TCurator>(descriptor);
```

The media relationship is stated once, in the contract the implementation closes, and there is no second
type argument to keep in step. `IClosedCataloger` and `IClosedCurator` are family markers the closed
contracts carry: the constraint they form makes a provider of the wrong family a compiler error at the
registration call site. They carry no values, because a marker can be implemented directly and a value it
carried would be an unverifiable claim — so the pairing itself is read, once, from the closed contracts the
implementation actually implements, when the registration is built. An implementation that closed no
contract of that family, or several, is refused there. One class may serve both families; each registration
reads only its own family's contract. The declaration carries neither the family nor a qualified identifier:
the registration called fixes the family, and Host mints the identifier from the contributing extension and
the declaration's `LocalId`.

`Arronix.Provider.Tmdb` is the first concrete package using this surface. It closes
`ICataloger<Movie>` and `ICurator<Movie>` against `Arronix.Media.Movies`; the cataloger returns exact Movie
values and the curator returns TMDb catalog references. The package never receives or assigns a
`MediaItemId`.

Language plugins register implementation types for constrained Host activation:

```csharp
registry.AddLanguage<TLanguage>();
```

Only after manifest and capability admission, Host constructs provider and language implementations through
an exact public `(IPluginContext)` constructor, or a public parameterless constructor when no context is needed.
The Host service provider is never an activation source. Before any implementation in a package is
constructed, Host checks every provider registration in it twice over: that the recorded contract is one
closed construction of its family's contract, closed over the recorded item type, and implemented by the
recorded implementation and no sibling of it; and that some active media kind supplies that exact item type.
A registration that does not describe one coherent relationship refuses the package with
`PluginProviderContractInvalid`; a coherent one whose item type nothing supplies refuses it with
`PluginMediaPairingUnsatisfied`. They are different operator problems — a defect in the extension's code
and a defect in the installation — and neither is `PluginContractMismatch`, which stays reserved for
contract version and assembly identity incompatibility. A post-construction contract check remains as
defense in depth.

Installed format capabilities must contribute representation recognition, probing, and policy for their
owned facts; language capabilities contribute linguistic mechanics. A media parser owns media structure
and opts into those capabilities without copying their vocabulary or orchestrating their runtime
implementation. This executable composition is an incomplete migration boundary today.

Standard action descriptors are always derived for typed media. Execution is resolved separately through
Host capabilities. Monitoring is currently executable; an unimplemented standard operation returns 501
and is never reported as accepted.

A package declares which of its published shared contract assemblies a browser may download, as
`clientContracts` in its manifest. The list is validated as a subset of `contractAssemblies`, so an entry
assembly can never appear in it and neither can a file the package does not publish or a duplicate; a package
that offers a client nothing is ordinary. `IClientContractCatalog` projects the Active plugin registry — one
snapshot, under that registry's own publication read gate — into `ClientContractManifest`, and serves the exact admitted
bytes of one offered assembly, or refuses with `NotOffered` or `Superseded`. A facet is servable only when a
browser can bind everything its assemblies name and every required facet it declares is itself served;
withholding cascades to a fixed point, and withheld facets are published on the same manifest and reported by
a health contributor rather than left to be inferred from an absence. Two routes publish it: `GET /api/v1/client-contracts`, uncacheable, and
`GET /api/v1/client-contracts/{package}/{contentHash}/{fileName}`, immutable. The byte route is content
addressed, so an address names bytes and an installation that changes mints new addresses rather than
changing what an old one means; a stale address is `410 Gone`, because the file is still published elsewhere
and re-reading the manifest is the recovery.

The Client loads those assemblies into its own default load context at run time, in two passes. The first
touches no runtime: it validates the manifest as untrusted input, then for every assembly in the required
closure checks length, SHA-256, and — read from the PE metadata of those exact bytes — the declared CLR
identity, the declared module version identifier, and the declared reference to the client's own universal
contract. Nothing loads unless the entire closure passes, because a browser cannot unload and half an
installation is not a smaller one. The second pass loads, and after each load proves what the runtime
produced against what was published, including that the loaded assembly's contract reference resolves by
object identity to the client's own compiled contract assembly. An entry that verified and was not loaded is
reported as verified, never as loaded, and a typed projection is permitted only when every required assembly
is resident. What a media contract *contains* is not discovered by enumeration.

Residency and currency are separate facts. A resident assembly the installation just read no longer names is
orphaned: it stays in the page, because a browser cannot unload, and stops being served — `Find` and
`ContractsOf` refuse it, and it is reported under its own heading carrying the package that last published
it and what that identifier means to the host now, withheld with the host's own reason, still offered
without that file, or not mentioned at all. An orphan never makes an otherwise healthy installation
incompatible. The same package returning at the exact content hash, identity, module and declarations the
page holds is reunited without a byte fetched; returning under any different description is the same
terminal `NameAlreadyResident` as replacing it in place. `410 Gone`, `404 Not Found` and a transport
failure are three outcomes, not one: the first says the address moved and a manifest re-read recovers, and
none of the three is a statement about what the page holds. An unchanged `InstallationHash` decides whether
to look and never decides what is true — every required assembly must still match the description this
manifest states, over values already in memory, so an unchanged installation costs one manifest read and no
bytes. `EventKind.PluginStateChanged` drives that re-read on a connected tab, through the loader's own
serialized entry point rather than a second manifest reader, and the same trigger sheds the bytes the new
installation no longer names. Store eviction discards content hashes the verified installation does not
name, never touches residency, and is refused outright against a manifest that was not proved whole.

One load is one transition: the orphan and owner bookkeeping a pass computes is applied only with the report
it publishes, so a caller that abandons a load mid-fetch leaves residency and the report describing one
installation. A finished load notifies, because the consumer showing a report is often not the one that
asked for it. Reading and sweeping are one serialized transaction owned by a single reloader every caller
shares — an operator's reload and an extension-state event cannot overlap, so no older sweep evicts what a
newer read just fetched — and it announces inside that same lease, telling each subscriber in turn, so one
refusal costs the others nothing and no later reload overtakes the notification. Cancellation is the
caller's token and nothing else: a
request timeout is an ordinary `Unreachable` or `Unavailable` outcome that replaces the report it failed to
read, and no boundary in this path contains an unsound process.

## 5. Invariants

- Universal contracts and Host remain media- and vendor-neutral.
- The public SDK is minimal through complete common primitives, typed defaults, generic relationships, and derivation. It is never made small by erasing owner-shaped data, requiring string dictionaries, or moving implementation mechanics into plugin code.
- Plugin authors state their domain types and genuine differences once. Host derives standard behaviour and may erase the result internally, but no runtime or wire projection becomes a second source of semantic truth.
- An abstraction is not complete until its meaning is preserved through authoring, registration, constrained activation, runtime execution, persistence where relevant, wire projection, Client use, tests, and documentation. A descriptor or passing unit test does not prove production wiring.
- Full *arr feature coverage and observable ecosystem compatibility are acceptance constraints. Intentional differences must be explicit and tested; difficult legacy behaviour may not be silently discarded to simplify the common model.
- The plugin-consumable `Arronix.Abstractions` SDK is one pre-1.0 surface. Stability is expressed by its
  assembly version and plugin manifest range; no per-type experimental marker, diagnostic registry, or
  compiler opt-in exists. Public visibility on cross-assembly Host infrastructure does not make it SDK.
- A media type owns its entity shape. Catalogers return that exact type; curators pair with it but return only
  catalog references, leaving the owning cataloger authoritative for the item.
- A provider's media relationship has one authority: the closed contract its implementation actually
  implements, read by one-time type inspection when the registration is built and re-checked at admission.
  A marker interface names the family for the compiler; it never carries the relationship, because a value
  on a directly implementable interface is a claim rather than a fact. Its family has one authority: the
  registration it is admitted through. Its identity has one authority: the qualified identifier Host mints
  from the contributing extension and the declared local name. No provider, declaration, or consumer
  restates any of the three, and a provider needing its own identifier during a call reads it from the
  invocation's definition.
- Telemetry an extension raises is copied into host-owned values, cut to size and redacted before anything
  else in the pipeline sees it, and a live `Exception` never travels: it is rendered to a summary of three
  strings named for the failure that was raised. A live exception is a handle onto the assembly that defines
  it, so carrying one past that boundary would keep an extension loaded for the life of the process.
- Redaction is additive and owner-qualified. An extension's rules are admitted with its package and can only
  mask more; no rule of any origin can reveal what another masked, and a pattern that cannot be compiled
  under a non-backtracking engine with a match timeout fails admission rather than running.
- An extension participates in telemetry only where it is entitled to: its enrichers and filters see the
  events it raised and no others, and contributing a sink — which reads the whole post-redaction stream —
  requires both the declared capability and an operator naming that package in `TrustedSinks`.
- An extension subscribes to its own event types and to a closed platform allow-list, proved against the
  assemblies its package actually owns. Absent ownership information refuses a non-platform subscription
  rather than permitting it, and delivery is by the event's runtime type.
- A cache belongs to the namespace it was resolved through. An active extension's types, values and
  delegates may be held inside that releasable namespace and nowhere else, and releasing it drops them —
  a provider outlives every extension, so anything of an extension's that outlived its namespace would
  outlive the extension.
- Contributing a telemetry sink and shaping an extension's own telemetry are two privileges. A sink reads
  the whole post-redaction stream, so it implies the network privilege and an operator grants it as well as
  the package declaring it. An enricher or a filter is offered only the events its own extension raised and
  no sink to send them to, so it implies neither; and a contributed filter cannot suppress an event at
  error severity or above.
- A media item type identifies exactly one media kind. Two admitted kinds closed over one item type are
  refused at kind admission, whether or not any provider pairs with it, because every lookup keyed on an
  item type would otherwise depend on iteration order.
- Admission is against the exact active media type. A cataloger or curator whose closed item type no active
  media kind supplies is refused before its implementation is constructed. The non-generic `ICataloger`
  floor remains only for kind-blind external-identifier recognition.
- Durable media item identity is host-owned. A cataloger owns an item's identity in the scheme it declares,
  captured once and enforced as lower-case at activation, and every item it returns states exactly one
  identifier in that scheme. Host assigns `MediaItemId` when a catalog item is materialized into local
  library state, and no cataloger or curator contract reaches one. It is host state scoped by media kind and
  level — an item's and a group's identifiers are separate key spaces — with the host's lifetime rather than
  a media runtime's, and
  projection takes the reference as an argument rather than deriving one. Repeated fetches, aliases and
  redirects resolve to one reference; identifiers assigned separately and later found to name one item merge
  onto the lower assignment, and the superseded reference resolves through `CatalogIdentity.Canonical`,
  which moves no library rows. Catalog work routes by scheme, never by provider identifier or implementation
  type, and a catalog reference carries neither. A reference in a scheme no installed cataloger owns is
  refused with `CatalogSchemeUnowned`; an item stating other than exactly one identifier in its own
  cataloger's scheme is refused with `CatalogIdentityInvalid`. The record is
  `docs/research/g04/media-item-identity.md`.
- A cataloger owns its external identifier namespace and marker spellings. Media parsers consume validated `ExternalIdReading` values and never duplicate vendor marker regular expressions.
- Every item and group exposes `ExternalIds`, `Title`, `TitleLanguage`, `Overview`, and `Artwork` through `IMediaEntity`; media-specific facts extend that floor as ordinary typed properties. `IMediaEntity` declares no durable key, so an item a cataloger shapes is complete without one.
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
- The plugin manifest is schema version 1 and owns its schema version, package identity and description,
  contract range, optional entry assembly, published shared contract assemblies, direct package
  dependencies, and requested capabilities. It is not a second media schema: derivable kinds, fields,
  tokens, policies, and actions are generated or mechanically validated rather than manually restated. A
  manifest may omit every derivable media fact; one which does state kinds or tokens is held to them exactly
  and in both directions against the prepared projection. Capabilities stay explicit because least privilege
  is stated before extension code runs, and operator-specific network/filesystem access is runtime
  configuration rather than a manifest grant.
- A package carries zero or one entry assembly and zero or more shared contract assemblies, and must carry
  at least one of the two. A contract-only package declares no capability, because a package that runs no
  code can hold no privilege. An entry assembly may not also be declared as a shared contract.
- One dependency is one edge: an exact package identifier and one compatible version range. There are no
  named facets, no separate contract and plugin edges, and no transitive restatement — the closure is
  derived. Omitting a list member and writing `null` for it are different statements: an explicit null is a
  declaration defect reported against its own member path.
- Once Host preparation runs, its admitted projection is the authority on what an extension is prepared to
  supply. Host answers the loader with an inventory keyed per media kind, each entry carrying that kind's own
  derived naming tokens; late agreement, scheduled-job kind association, duplicate-kind checking, and token
  ownership read it rather than the manifest. Legacy shape-provider registrations remain a transitional path
  for a loader running without Host preparation.
- Naming-token ownership is per media kind. A kind claims the tokens it derived, once each; an extension supplying several kinds never claims the cross product of its kinds and its combined vocabulary.
- Naming-token equality is the invariant-lowercased alphanumeric spelling shared by parsing, declaration
  agreement, reserved-name validation, ownership, and rendering. Separator or case variants cannot bypass a
  reservation or acquire another owner.
- Successful Host preparation carries one exact attempt receipt but publishes nothing. The loader commits
  the prepared Host values, per-kind token plan, and Active runtime result under one shared publication gate
  only after all remaining checks pass. Rollback and stop remove exact receipts rather than values which
  merely share an identifier.
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

The loader reads plugin manifests and assemblies, creates isolated load contexts, and may quarantine invalid
plugins. A quarantined attempt publishes nothing and releases every object it created. A failure attributable
to one package — including a folder the loader cannot even list, and a disposer that throws while that package
is being torn down — refuses or is recorded against that package alone and leaves every other package
answered for; only a condition in which the process is no longer sound propagates, and a package whose
teardown reported a failure keeps its hold on the installation's shared contracts. Host shutdown drains
scheduled plugin work, withdraws the exact active attempt and its naming-token ownership, disposes owned
instances, and requests collectible-context unload; a job still executing after its bounded deadline keeps
that plugin rooted and Active. The sanctioned provider SPI supplies network and filesystem access through
capability-scoped services. Extensions run in process, so this is an admission and audit boundary rather than
a sandbox against direct BCL use. The current media store and queue are in memory; restart durability is not
promised.

## 7. Dependency boundaries

- `Arronix.Abstractions` depends on nothing.
- a package may ship zero or more shared contract assemblies and zero or one isolated executable entry assembly. A shared contract assembly depends on Abstractions and on other shared contract assemblies only; it carries typed owner semantics and pure deterministic behavior, never `IPluginModule`, parser or registration execution, provider implementations, I/O, mutable process state, or module initializers. Its intended lifetime is one copy per installation in a Host-owned collectible contract context, released once every dependant has withdrawn.
- the video package is `Arronix.Format.Video` (video's owner semantics: the representation and quality facts a `Release<Video>` carries, the format family a media type names in its constructor, and the release preferences it contributes to a dependant's compiled policy) plus `Arronix.Format.Video.Contributions` (video's executable work, currently the release-term recognition vocabulary). Public domain types are spelled in `Arronix.Format.Video`; executable-only types are spelled in `Arronix.Format.Video.Contributions`, and no type is declared in both. The movies package is `Arronix.Media.Movies` (the shared `Movie`, `MovieReleaseStage`, and `MovieReleaseTimeline`) plus `Arronix.Plugin.Movies` (the isolated definition, parser, module, and generated projections).
- language implementation assemblies depend on Abstractions and publish no shared contract assembly.
- media extensions reference `Arronix.Sdk` for authoring and depend at runtime on Abstractions, the domain assembly of each format capability they compose, and their own media domain assembly. An extension never references another kind's media domain, and never a format capability's executable half: everything a media declaration needs from a format is domain semantics, so no media extension payload carries a format's executable assembly. `Arronix.Sdk` supplies `Arronix.Generators` only as an analyzer asset; a shared contract assembly takes no analyzer at all.
- `Arronix.Common` and `Arronix.Plugins` depend toward Abstractions.
- Host depends on Abstractions, Common, and Plugins, but not on a media or format implementation.
- Client depends on Abstractions only.
- vendor provider implementations must not be placed in Host, Abstractions, a format assembly, or a media definition.
- `Arronix.Provider.Tmdb` depends only on Abstractions and `Arronix.Media.Movies`. It ships its executable
  provider assembly independently, declares a runtime dependency on package `movies`, and owns every TMDb
  endpoint, credential field, DTO, identifier, marker, transport, and mapping rule.

A separately shipped provider compiles against a media domain without taking the extension: an
`ICataloger<Movie>` or `ICurator<Movie>` needs Abstractions and `Arronix.Media.Movies`, two media packages
compile their typed releases against the one `Arronix.Format.Video`, and each package domain type is
declared exactly once across the solution.

Package-to-package type sharing is now a runtime fact as well as a compile-time one. `Arronix.Format.Video`
ships as the installed package `arronix.format.video`, which publishes one shared contract assembly and runs
no code; Movies and Television require it by identifier and carry no private copy. The installation admits
each declared contract once, into one Host-owned collectible context, and hands every publisher and every
dependant in its declared closure the same `Assembly` object. Two independently installed dependants
therefore see one `Video` type, and a separately packaged provider closes `ICataloger<Movie>` over the same
runtime type the registered movies kind publishes as its item.

Global admission is not global visibility. A package binds only to contracts published by itself or by a
package in its exact transitive dependency closure; a contract whose metadata reaches outside that closure
is refused before it loads, and a runtime request for an admitted contract a package did not declare is
refused rather than resolved from its own payload or the default context. A package carrying a private copy
of an admitted contract is refused with both module identifiers and both content hashes.

One CLR identity now reaches a browser as well. A client-safe contract assembly is downloaded from the
running host, verified against the content hash, CLR identity and module version identifier that host
published, and loaded into the browser's default context, where its reference to `Arronix.Abstractions`
resolves to the client's own compiled copy by object identity. The Client's build still declares exactly one
project reference, on Abstractions, and roots that assembly for the trimmer because a dynamically loaded
contract binds to members the trimmer never saw.

Still open: typed deserialization and rendering of a loaded contract through generated metadata (G07.2),
cache update, removal and stale-tab behaviour (G07.3), signing and package integrity, side-by-side contract
versions, and any reload that involves shared contracts
— an admitted store takes a second graph only when neither shares anything, because matching identifiers,
versions and file names do not prove matching bytes, identities or dependency closures.

## 8. Lifecycle and execution

While a media extension builds, `Arronix.Generators` emits its closed item, group, and workbench-row
projections. At startup the loader validates manifests and static references, constructs a capability-scoped
context, invokes the plugin module, and seals registration. Host then validates and derives an attempt-local
kind-blind model, activates provider and language types through their supported plugin constructors without
consulting Host DI, and returns the authoritative inventory
it is prepared to publish. Nothing is visible yet. The loader uses that inventory for declaration agreement,
scheduled-job association, naming-token ownership, and installation-wide identity. Only after all checks pass
does one shared gate publish the token plan, exact Host attempt, and Active runtime result. Any failure
quarantines and completely releases that attempt without terminating Host.

Admission runs over the whole installation before any package's code exists: declarations are proved into
one immutable installed-package snapshot each, operator configuration becomes a typed availability state,
the one resolver produces the one resolved graph, and the installation's shared contracts are staged,
metadata-validated in graph order and loaded as one transaction over a provisional context. A publisher that
fails at the real load boundary takes its whole set with it, and every package that required it is refused
over the declared edge whether or not its own metadata named the failed assemblies.

Packages are then admitted in graph order. A package pins the exact receipts of its dependencies before it
reads a byte, so a withdrawal racing its preparation is refused rather than leaving it executing against a
released package. Publication is one transaction under one gate: Host authorization, token claims, the Host
attempt, the package edges and the runtime result commit together or not at all, and the transition to
shutdown takes the same write lease so a package cannot root itself after Stop has begun.

Teardown reverses the order packages were actually published in. Under the gate a package is hidden and
marked withdrawing while keeping every pin it holds; outside it, its instances and load context are released
and then its hold on the installation contract context — which it gives up while its dependencies are still
pinned, because its own contract assembly may reference theirs. Only a release that reported no failure
re-enters the gate to remove its edges and unpin. A disposer, unload, contract-release, authority-mismatch,
in-flight-work or dependent-pin failure retains the exact identifier, receipt, edges and pins and yields
`StopIncomplete`, including on repeated stops; an empty active set is not proof that nothing remains held,
and a clean stop is not reported until the shared contract context has been released.

A package-level lease owns the exact package receipt and the contract hold, and wraps an optional executable
runtime lease. A contract-only package is a first-class active package: rooted, diagnosable,
dependency-bearing and withdrawable, with no load context, registration ledger or Host admission attempt
invented for it. One global contract context cannot physically unload one assembly, so a package's release
is modelled as giving up a claim on that context, and unloading is requested only after every holder is
gone. `AssemblyLoadContext.Unload()` is a request; collection is never claimed.

The telemetry pipeline and the extension runtime are stopped by an explicit handshake rather than by
hosted-service order, because a generic host may stop services concurrently. Extension participation is
closed first, while those extensions can still be found and leased: no contributed enricher, filter or sink
is entered for an event after that cutoff, the pipeline stops awaiting the ones it had started, and the
contributed sinks are given one final flush. An event accepted before the cutoff but not yet begun is
written off and counted rather than waited for, and still reaches the host's own sinks. A call the pipeline
abandoned at its attempt bound may still be running; it holds its own package's lease until it returns,
which is what keeps that package loaded underneath it. Only then do the extensions go, and only after they
report gone does the queue close and the host's own sinks flush — so an extension's cleanup telemetry is
still delivered.

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
