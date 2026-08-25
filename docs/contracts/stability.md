# Arronix.Abstractions API Stability Policy

## Version: 0.9.0

This document defines the stability guarantees and versioning policies for the `Arronix.Abstractions` contract layer.

## Pre-1.0: Nothing Here Is Stable Yet

**This clause governs the whole document. Every rule below is read through it.**

While the assembly version is below 1.0.0, `Arronix.Abstractions` is **unstable in the semantic-versioning
sense** — which is exactly what [Semantic Versioning 2.0.0 §4](https://semver.org/#spec-item-4) says
major-version-zero is for: initial development, anything may change at any time, and the public API should
not be considered stable.

Concretely, below 1.0.0:

- **Any contract may be changed, renamed or deleted in a MINOR release.** There is no MAJOR boundary to wait
  for. Waiting for one would mean waiting for 1.0.0 — that is, freezing whatever happened to be written
  first for the entire pre-release life of the project.
- **A contract with no implementer outside this repository is deleted outright, not deprecated.** This is
  the zero-consumer test, and it is load-bearing: if nothing outside this repository implements a contract,
  removing it costs a compile error inside this repository and nothing else. A compile error the compiler
  finds for you is not a migration burden.
- **`[Obsolete]` is not used.** There is no deprecation window, no notice period, no forwarding type, no
  bridge, no adapter and no dual contract. Nothing has ever shipped against these contracts, so there is
  nobody to give notice to.
- **A shape that is wrong is corrected now.** "It is stable, so this needs a major version" is not a reason
  to leave a wrong shape in place; below 1.0.0 that sentence has no referent, because there is no consumer
  for whom the correction is breaking. A stringly-typed key bag beside a record that "cannot change", a
  wrapper DTO beside an unused one, a sentinel value chosen because reshaping would be "breaking" — all of
  these are defects, not compromises.
- **The stable surface starts empty.** See [The Stable Surface](#the-stable-surface).

Arronix is a clean-sheet rewrite. Migration from a specific version of a specific existing application is
the job of a separate, later, version-specific migration script — never of a second contract living beside
the first, and never of a compatibility shape carried into this assembly.

**The guarantees in the rest of this document take effect at 1.0.0.** They are the right policy for a
contract library that has external consumers. They were wrong applied to one that has none, and applying
them anyway is what produced the deletions recorded in the [0.4.0](#040) entry below.

## Semantic Versioning

`Arronix.Abstractions` follows [Semantic Versioning 2.0.0](https://semver.org/), *including* its
major-version-zero clause:

| | Below 1.0.0 (where the project is now) | From 1.0.0 |
|---|---|---|
| **MAJOR** | not reached; 1.0.0 is the first | breaking changes to public APIs |
| **MINOR** | additions, changes, renames and deletions — anything | additions made in a backwards-compatible manner |
| **PATCH** | fixes that change no signature | backwards-compatible bug fixes |

Plugins declare the versions they have actually tested. The loader checks that declaration against the
installed contract version and imposes no second stability classification.

## Contract Compatibility

Plugins declare their compatible `Arronix.Abstractions` version range using npm/yarn-style semver ranges in their `plugin.json` manifest:

```json
{
  "contracts": {
    "arronix": ">=0.9 <0.10"
  }
}
```

The host runtime will reject plugin loading if the installed `Arronix.Abstractions` version does not satisfy the plugin's declared range.

## API Stability

Below 1.0.0, the public contract is one pre-release surface. There is no per-type stable or experimental
tier, no compiler opt-in, and no diagnostic-id registry. The assembly version and the plugin manifest range
are the compatibility contract.

A consumer chooses a range that reflects versions it has actually tested. The loader accepts a plugin when
the installed `Arronix.Abstractions` version satisfies that declared range; it does not impose an additional
range-width policy. First-party plugins use `>=0.9 <0.10` because they are built and tested against the 0.9
line, not because types inside that line carry a second stability classification.

At 1.0.0, the ordinary Semantic Versioning guarantees described above begin. If a finer-grained preview API
mechanism is ever needed, it requires a new explicit design rather than reviving compiler-error attributes
and file-scoped suppressions.

## Removing a Contract

**Below 1.0.0: delete it.** Delete the type, its tests, its registration plumbing and its documentation
entries in the same change, and record the deletion in the version history below. Do not mark it
`[Obsolete]`, do not leave a forwarding type, and do not add a bridge or an adapter that keeps the old shape
reachable. Two shapes for one concept is the cost this policy exists to avoid; one compile error is not.

Worked example — a contract in `Arronix.Abstractions` that no plugin implements and that the host reaches
through a handful of call sites:

1. Delete the type.
2. Fix the call sites the compiler names.
3. Add a version-history line: what was deleted, and what replaced it.

That is the whole procedure, and it lands in the next MINOR release. A contract that *does* have an
implementer outside this repository does not exist yet; when one does, this section grows a second half.

**From 1.0.0:** a contract that external plugins may implement is removed at a MAJOR boundary with the
replacement named in the release notes. What notice mechanism that uses is deliberately left undesigned
until then. Designing a notice period now would mean designing it against zero consumers, which is how this
document went wrong the first time.

## Additive Changes

From 1.0.0, the following changes are **non-breaking** and may occur in MINOR releases. Below 1.0.0 they
need no special handling, because a MINOR release may make any change at all.

- Adding new interfaces
- Adding new methods to existing interfaces (with default implementations where possible)
- Adding new properties to existing interfaces (with default implementations where possible)
- Adding new optional parameters to existing methods
- Adding new DTOs, records, or enums
- Adding new enum values (where appropriate)
- Adding new attributes for metadata

## Breaking Changes

The following changes are **breaking**:

- Removing or renaming public types, interfaces, methods, or properties
- Changing method signatures (parameter types, return types)
- Removing or renumbering enum values
- Changing the behavior of existing methods in incompatible ways
- Changing from nullable to non-nullable or vice versa

From 1.0.0 they occur only in MAJOR releases. **Below 1.0.0 every one of them is permitted in a MINOR
release** — that is the clause at the top of this document, and it is the only reading consistent with
SemVer 2.0.0 §4. Below 1.0.0 the list above is a description of what to write down in the version history,
not a list of what to avoid.

## Version History

### 0.9.0

**Released**: [Current]

**The semantic authoring SDK is separated from generated and Host binding mechanics.**

- Added `Arronix.Sdk`, the packaging-only package extension authors reference. It depends on
  `Arronix.Abstractions`, embeds `Arronix.Generators` as an `analyzers/dotnet/cs` asset, and has no runtime
  assembly. The generator is not independently packable.
- `MediaType<TItem,TTarget,TRelease,TParser>` no longer publicly exposes `Capture`. The generator supplies
  the public `CompiledShapes` override retained by the immutable G01 proof, but marks it non-authoring;
  capture is an explicit hidden-interface operation,
  and Host receives the exact generated catalog through the erased double-dispatch registration.
- Generated catalogs, registration and binder interfaces, visitor interfaces, provider-family markers,
  provider/language type registrations, erased `Type` carriers, and erased expression members are marked
  non-authoring. Concrete semantic values implement visitor dispatch and type carriers explicitly, so
  normal IntelliSense shows the typed values an author actually supplies.
- Added compiler diagnostic `ARX1003` at the media declaration when it is not `partial`; the diagnostic
  tells the author how to enable generation and explicitly says not to implement the Host projection.
- The contract assembly and first-party manifests move together to `0.9.0` / `>=0.9 <0.10`.

**Telemetry participation is split from telemetry collection.**

- Added `Capability.TelemetryProcessing` and its wire name `telemetry-processing`. The vocabulary addition
  is additive — the schema's capability list gains one entry — and a manifest that registers neither seam
  is unaffected.
- `AddTelemetryEnricher` and `AddTelemetryEventFilter` now require it, and the two halves of the contract
  now agree: the public documentation described both as ungated while the capability matrix charged
  `TelemetrySink` for them. A manifest registering either must declare `telemetry-processing`, which
  implies nothing and needs no operator grant.
- `Capability.TelemetrySink` is unchanged: the whole post-redaction stream, the `network` implication, and
  the operator naming the extension in `Arronix:Plugins:TrustedSinks`.
- `CapabilitySet` holds 32 bit positions rather than 16, and refuses a value the vocabulary does not
  declare. The representation is private; the change is what keeps the next capability additive.

### 0.8.0

**Released**: 0.8.0

**Typed media ownership and format-owned representation replace the quality-axes experiment.**

- Removed the per-type `ExperimentalAttribute` policy, its `ExperimentalContracts` diagnostic registry,
  file-scoped `ARX` suppressions, reflection-based enforcement fixtures, and the loader's additional
  minor-range-width gate. Pre-1.0 status is expressed once by the assembly version; plugin compatibility is
  expressed by the manifest range and checked against the installed version.

- `MediaType<TItem,TTarget,TRelease,TParser>` now keeps the durable item, ephemeral acquisition target,
  interpreted release, and typed parser as one compile-time contract. Its primary constructor captures
  the explicit stable kind identifier, singular/plural names, non-empty format composition, typed
  minimum-availability selection, and file binding; `OnePerItem` is the file default, and identity is not
  inferred from display wording. A media plugin supplies an ordinary derived class and overrides typed
  values for identity, groups, additional selections, searches, matching, release
  policy, querying, naming, intent, actions, workbenches and derivations. `TParser` implements the static
  `IReleaseParser<TRelease>.Parse` contract directly rather than replaying a parse-declaration DSL.
  There is no fluent media builder, static-abstract media-type seam, or type-class capability-badge graph.
  The public non-generic shadow model is removed; Host uses a private runtime bridge after double-dispatch
  registration through `AddMediaType<TMediaType>()`.
- `MediaItem<TLifecycle>` owns the common durable item surface while each media type owns its exact public
  item subtype where provider pairs require that identity. Lifecycle is an object, not parallel status and
  release-date fields or an enum-keyed property bag; catalog workflow state remains a separate concern.
- Group declarations are plural relationships. A media item may belong to zero, one or many groups, and
  a kind can declare several independent group relationships.
- Parser regression corpora are test assets rather than members of the production media contract. The
  unused `CorpusCase`, `MediaType.Corpus`, and `MediaKindModel.Corpus` surface is removed.
- Raw indexer output is `ReleaseListing`. Typed `TargetMatch<TTarget>`,
  `ReleaseOption<TTarget,TRelease>`, and `ReleasePolicy<TRelease>` separate provider statements, coverage,
  and deterministic selection.
- Typed provider registration names the implementation once. Host derives the closed
  `ICataloger<TItem>` or `ICurator<TItem>` pairing from the implemented contract, takes the family from the
  registration method, and mints provider identity from the plugin and declared local name before DI
  activation.
- Media item identity moved off the contracts. `IMediaEntity` no longer declares `Key`: `MediaItemId` is
  host-assigned when a catalog item is materialized, and no provider contract carries one. `ICataloger` adds
  `CatalogScheme`, the external identifier scheme it is the authority for, and every item it returns states
  exactly one identifier in that scheme; scheme tokens are canonical lower-case and compared ordinally.
  `ICurator<TItem>` returns `CuratedReference` values — a catalog
  identity with an optional curator-owned `CuratedEntryId` — instead of items. See
  `docs/research/g04/media-item-identity.md`.
- The universal `Evidence<T>`, axis, point, quality-family, video-token, and video-evidence contracts are
  deleted. `ARX0021` is retired.
- Video moves to `Arronix.Format.Video`, which owns `Video`, observable channel-relative
  lineage, open codec and dynamic-range identifiers, all audio tracks, extensions, token interpretations,
  and its default release-policy contribution. Host and Abstractions contain no video scanner vocabulary.
- A Remux means a direct copy of disc streams; a WEB-DL means a direct copy of the hosted stream artifact.
  Neither makes a claim about upstream studio-master lineage.
- Compatibility `ParsedRelease` and `QualityTier` remain temporarily for unconverted media plugins, but
  their video-specific codec, audio-codec, resolution, source, bit-depth, size, and attribute slots are
  removed.
- The contract assembly and first-party manifests move together to `0.8.0` / `>=0.8 <0.9`.
- Common media values now include structural rating scales, rating voice, localized owner-shaped values,
  content certifications, monitoring scope, and typed generic workbench rows.
- `ARX0022` introduces `ILanguageDefinition` and type-based language registration. Language-specific
  comparison, query, file-name and sort rules live in language plugins rather than media parse declarations.

This entry supersedes the 0.7 quality-axes design below. The 0.7 entry remains as history, not current
architecture.

### 0.7.0

**Released**: 0.7.0

**New contract area — Quality axes** (`ARX0021`, `Arronix.Abstractions.Quality`), per
`docs/design/quality-axes.md`. Quality stops being a rung on a ladder and becomes a point in a small space
of typed, orderable **axes**: the format family declares the axes and reads evidence onto them, the **user**
owns a policy that says which axes matter and in what order, and every ranking, cutoff, upgrade,
equivalence and label is a function of those two. The single most important line in the area is a member
that is *absent*: `IQualityType` cannot compare two points, because that question has no answer without a
policy.

- **Evidence**: `Evidence<TValue>`, `EvidenceSet<TValue>`, `EvidenceSource`. Not `TValue?`, because a
  nullable carries no provenance — and provenance decides trust — and because a nullable makes "absent"
  comparable by accident. An empty set and an absent set are different claims: "we looked and found
  nothing" versus "we did not look".
- **Axis declaration**: `[Axis]`, `AxisOrdering`, `AxisForm`, `QualityAxis`, `QualityAxisId`,
  `FormatFamilyId`. The attribute states a fact about one property in isolation and never takes an
  identifier string; anything relating two axes is on a builder or in a policy.
- **The kind-blind value**: `AxisValue`, `AxisReading`, `QualityPoint`. A point carries no rank, no weight
  and no name — a name is a rendering and a rank is a policy's opinion. An `AxisValue` is identified by the
  point it names, never by how it is spelled.
- **Seams**: `IQualityFacts`, `IQualityType<TFacts>` (static-abstract authoring seam),
  `IQualityType` (the runtime model the host and client hold), `IQualityTypeBuilder<TFacts>`,
  `IQualityRefinement<TFacts>` (a media kind's own dialect, contributed after the family has read).
- **Evidence input**: `ReleaseEvidence`, `MediaProbe`, `ResolutionClaimForm`, `ScanType`, `LanguageClaim`.
  Strings enter `Read` and typed axes come out; that is the whole of the parse boundary.
- **Policy**: `QualityPolicy`, `IQualityPolicyBuilder` with `IAxisPreferenceBuilder`,
  `IFacetScoreBuilder` and `IAxisRequirementBuilder`; `AxisPreference`, `PreferenceUnknown`,
  `PreferenceUnknownMode`, `AxisRequirement`, `RequirementMode`, `UnknownEvidence`, `UnknownEvidenceMode`,
  `CutoffPredicate`, `AxisFloor`, `FacetScoring`, `FacetScore`, `QualityJudgment`, `Eligibility`,
  `GrabDecision`, `GrabVerdict`.
- **Rendering and size**: `QualityLabelRule`, `QualityLabelDetail`, `SizeExpectation`, `SizeVerdict`.

Three guarantees the area is built around, each with a test that would go red if it were lost:

1. **A preference cannot cycle.** `PreferenceUnknown` offers `Lowest` and `Assume` and nothing else. A
   third mode that let a preference *skip* an axis would make comparison lexicographic-with-skipping, which
   is not transitive — three points silent on three different axes produce a strict preference cycle, and
   because a grab happens whenever the comparison says "better", a cycle is an unbounded download loop.
   `UnknownEvidence` keeps all four modes, because a requirement and a floor order nothing.
2. **A claim never outranks a measurement.** The provenance rule lives on `QualityPolicy.Decide` and not on
   `Compare`: it is pairwise and asymmetric, so folding it into the ordering would destroy the transitivity
   everything else rests on. Without it, a title over-claiming a resolution we have since measured is
   re-grabbed on every pass, forever.
3. **A bonus can never overturn an ordering.** `FacetScoring` is consulted only when the precedence list
   leaves a tie, cannot refuse anything, and is bounded — which is what replaces token scoring and its
   magic rejection value.

**Not breaking.** Everything in this entry is an addition. `IQualityModel`, `QualityTier`, `CutoffPolicy`,
`QualityRevision`, `ProperHandling`, `QualityDeclaration`, `RungFallback`, `TierDefault` and
`CrossFamilyRule` are untouched and remain the live path: each still has consumers in the host and in the
media extensions, and under the pre-1.0 policy a contract is deleted when its last consumer converts, which
is a later milestone and not this one.

### 0.6.0

**Released**: 0.6.0

**New contract area — Typed media model** (`ARX0020`, `Arronix.Abstractions.Media`), per
`docs/design/typed-media-model.md`. A media kind is authored as typed entities plus property attributes
plus a fluent configuration, and the host *derives* the existing `MediaShape` / `MediaLevel` /
`FieldDescriptor` / `FileBinding` / `CoordinateSpace` / `GroupingAxis` / `PluginIntentSurface` objects from
them. The descriptors keep their meaning and every engine, the binder and the client are untouched — this is
the split a typed data mapper has between entity types and the model it derives, not a translation layer.

- **Markers and seams**: `IMediaItem`, `IMediaGroup<TMember>`, `IMediaType<TItem>` (static-abstract
  authoring seam), `IMediaType` (the non-generic runtime model the host and client hold).
- **Model carrier**: `MediaKindModel` — the per-kind engine inputs a later iteration types. It carries no
  structure and no intent section, because both are derived; and no strategy or required-vocabulary
  section, because a strategy is a method and the compiler already knows which vocabulary a typed kind
  uses. `TemplateRequirement` and `INamingTemplateFacts` carry a file-template rule as a predicate, which
  is what a per-token "is required" flag could not express.
- **Attribute vocabulary** (21, all sealed, all property-targeted, none inherited): `[Identity]`,
  `[Title]`, `[Searchable]`, `[Sortable]`, `[Filterable]`, `[Groupable]`, `[Disambiguation]`, `[Status]`,
  `[Timestamp]`, `[Artwork]`, `[Size]`, `[Progress]`, `[Count]`, `[Multiline]`, `[Ratio]`, `[Editable]`,
  `[Derived]`, `[Prominence]`, `[Unit]`, `[Display]`, `[Ignore]`. No attribute takes an identifier string:
  anything that names something else relates two things and belongs on the builder, as an expression.
- **Value types**: `ExternalIdSet`, `ArtworkSet`, `ArtworkImage` — one set-valued property in place of one
  property per catalog and one per artwork role.
- **Builder**: `IMediaTypeBuilder<TItem>` and the sub-builders for files, format families, identity roles,
  groups, selection policies, search kinds, matching, querying, naming, summaries, intent, actions,
  workbenches, quality and parsing, plus `IDeclaredSelection`, `KeyExpansion`, `Agreement`, `ReadingFact`
  and `FileFact`.

**Contract amendments the typed surface requires**, all additive:

- `LevelIdentity` gains `RequiredRoles` and `AdmittedRoles` (new `IdentifierRole`). A kind declares which
  identifier *roles* it needs; the host composes the concrete `ExternalIdScheme` list from the installed
  catalogers. This is what takes catalog names out of a media kind's structure.
- `SelectionFacet`'s `FacetApplication` gains `Acquisition`: excluded items exist, are visible, and are
  refused acquisition. Neither of the previous two members was right for an availability threshold.
- `ActionScope` gains `Group` and `ActionDescriptor` gains `TargetGroupAxisId`, so an action over a
  grouping axis can name the axis instead of being filed under the whole kind.

**Not breaking.** Every change in this entry is an addition; `MediaKindDefinition` and
`IPluginRegistry.AddMediaKind` are untouched and remain the live path for the kinds that have not converted.

---

**Amendment (same version, 2026-08-17) — the declarative aggregate is removed.**

The statement above did not survive the pass. Once `Arronix.Plugin.Movies` registered as a typed kind,
`MediaKindDefinition` had **no producer anywhere in the repository**: the three unconverted kinds — Tv,
Music, Books — are *imperative* extensions on the per-seam path, not declarative ones, so nothing was
relying on it. Carrying it would have been carrying dead contract.

**Removed** from `Arronix.Abstractions` (`ARX0019`):

- `MediaKindDefinition` — superseded by the derived triple `MediaShape` + `PluginIntentSurface` +
  `MediaKindModel` (`ARX0020`), which carries the same sections minus structure, intent, strategies and
  required vocabulary.
- `StrategyBinding`, `StrategyRequirement`, `RequiredVocabulary` — a typed kind names no host strategy by
  string, so there is no binding to make and no vocabulary to negotiate. The two match strategies the
  engines have are derived: entry resolution had one implementation, and unit assignment follows from
  whether a kind's unit rules expand a span.
- `NotificationDeclaration.DeepLinkTemplate`, `.Occasions`, `.LinkTemplates`, `.ArtworkRoleOrder` and
  `GroupSummaryRule.ArtworkRoleOrder`, with `LinkTemplate` and `OccasionPhrase` — closing P2-6, P2-7 and
  P2-9. All had zero consumers in the host.

**Removed** from `IPluginRegistry` (`ARX0014`): `AddMediaKind(MediaKindDefinition)`, replaced by
`AddMediaType<TItem, TType>()`. The per-seam registrations are untouched and remain the live path for the
three imperative kinds.

**Added** to `Arronix.Abstractions.Media` (`ARX0020`): `IMediaTypeRegistration`, `IMediaTypeBinder<TResult>`
and `MediaTypeRegistration.For<TItem, TType>()` — how a captured type pair crosses a kind-blind loader
without either side naming it.

**Breaking**, and permitted under the pre-1.0 policy: every affected contract is `[Experimental]`, and the
removed surface had no implementer outside the repository. See `docs/design/typed-media-model.md` §9.

### 0.5.0

**Released**: [Current]

**New contract area — Declarative media kinds** (`ARX0019`, `Arronix.Abstractions.Definition`), per
`docs/design/declarative-media-kinds.md` as amended by its adversarial critique. One aggregate,
`MediaKindDefinition`, is the complete declaration of a media kind: `ParseDeclaration` (normalization
options, pre-rewrites, ordered title patterns with capture bindings and guards, per-kind token tables
with occurrence selection, the ordered rung-resolution table), `MatchDeclaration` (entry-resolution
cascade, match layers with an on-match space hint, unit-resolution rules including title lookup,
confidence rules, variant choice over host feature catalogs), `QueryDeclaration` (query tier and alias
templates, coordinate grammar, origin limits, credit substitutions), `QualityDeclaration`,
`NamingDeclaration`, `CatalogDeclaration` (request/response maps, derivations, identifier rules, delta
and paging policy), `NotificationDeclaration`, `StrategyBinding`/`RequiredVocabulary`/`CorpusCase`, and
the closed predicate vocabulary (`TagPredicate`, `PredicateAtom`, `PredicateOp`). Registered through the
new `IPluginRegistry.AddMediaKind`, gated by the existing `MediaKind` capability; no capability member
was added.

**Contract amendments the declarative surface requires** (the movies-plugin-review's structural fixes,
landed here because a declaration cannot reference slots that do not exist):

- `QualityTier` gains `Weight` (semantic weight; equal weights form a quality group), `GroupName`,
  `Revision`, and per-minute size limits; `CompareTo` and `CutoffPolicy.MeetsCutoff` now compare
  effective weight, which corrects the recorded grouped-rung cutoff defect. New shape types
  `QualityRevision` and `ProperHandling`; `CutoffPolicy` gains `ProperHandling`.
- New `MediaFileFacts` record; `IRenamePolicy.GenerateFileNameAsync` now takes the file being named.
  `FormatFamily` gains `TechnicalFacets` (new `TechnicalFacet`/`TechnicalFacetCase`).
- `GroupingAxis` gains `Fields` and `ExternalIds`.
- `FieldDescriptor` gains `Components` with new `FieldValueKind.Composite` (repeated tuples are one
  composite field, never index-correlated parallel lists); `FieldValue` gains `OfComposite`.
- `SelectionFacet` gains `ValuesAreOrdered` (an ordered enumeration is a threshold over named values).
- `MatchOutcome` gains `Basis` (new `MatchBasis` enum); `AcquisitionRequest` gains `AcceptedLanguages`.

**Breaking, deliberately and without a migration path**, per the pre-1.0 clause: implementers of
`IPluginRegistry` and `IRenamePolicy`, and construction sites of the amended records, update to the new
shapes in the same change.

### 0.4.0

**Released**: [Current]

**The stability policy itself was rewritten in this release**, and the pre-1.0 clause at the top of this
document is the result. The previous text granted full 1.0-grade guarantees to a 0.x contract library with
zero external consumers, and then forbade deleting an unused contract: removal was MAJOR-only, and at 0.3.0
the next MAJOR is 1.0.0, so whatever happened to be written first was frozen for the entire pre-release life
of the project. The policy was then cited by name to justify permanent workarounds — untyped key bags beside
records that "cannot change" before 1.0, wrapper DTOs beside unused ones, sentinel values chosen because
reshaping was classed as breaking, and bridges beside contracts that could not express their own domain — all
for consumers that do not exist. It also contradicted SemVer 2.0.0 §4, which reserves major-version-zero for
exactly this phase. The deprecation ladder is deleted, `[Obsolete]` is not used below 1.0.0, and a contract
with no implementer outside this repository is now deleted rather than deprecated.

**Breaking, deliberately and without a migration path.** Arronix is a clean-sheet rewrite; nothing has
ever shipped against these contracts, so there is no deprecation period, no bridge and no dual surface.
Migration from specific versions of specific existing applications is a job for dedicated migration scripts,
not for carrying two vocabularies forever.

**The v0.2.0 provider trio is deleted.** `IIndexerProvider`, `IMetadataProvider` and
`IDownloadClientAdapter`, together with the DTOs that existed only to serve them —
`IndexerCapabilities`, `MetadataSearchResult`, `MetadataItem`, `DownloadStatus` — are gone, as is the
host-side `StableProviderBridge` that adapted them onto the current contracts. The provider namespace no
longer mixes stable and experimental types: every type in it is experimental and needs an `ARX0015`
opt-in.

**The five provider families are renamed** to a consistent set of singular agent nouns in en-US.
A `Cataloger` answers what a thing *is* — canonical facts from an external authority, populating the
catalog record. A `Curator` answers which things you *want* — an external list that selects what belongs
in the library.

| Was | Now |
| --- | --- |
| `ProviderFamily.DownloadClient` | `ProviderFamily.Downloader` |
| `ProviderFamily.MetadataSource` | `ProviderFamily.Cataloger` |
| `ProviderFamily.ImportList` | `ProviderFamily.Curator` |
| `IDownloadClient` | `IDownloader` |
| `IMetadataSource` | `ICataloger` |
| `IImportListSource` | `ICurator` |
| `DownloadClientItem` | `DownloadItem` |
| `DownloadClientInfo` | `DownloaderInfo` |
| `MetadataSourceCapabilities` | `CatalogerCapabilities` |
| `ImportListFetch` | `CuratedListFetch` |
| `ImportListItem` | `CuratedItem` |
| `DownloadClientRegistration` | `DownloaderRegistration` |
| `MetadataSourceRegistration` | `CatalogerRegistration` |
| `ImportListRegistration` | `CuratorRegistration` |
| `Capability.ImportList` (wire `import-list`) | `Capability.Curation` (wire `curation`) |
| `MatchSource.DownloadClientTitle` | `MatchSource.DownloaderTitle` |
| `CoreErrorCode.DownloadClientConnectionFailed` | `CoreErrorCode.DownloaderConnectionFailed` |
| `CoreErrorCode.MetadataProviderConnectionFailed` | `CoreErrorCode.CatalogerConnectionFailed` |
| `CoreErrorCode.MetadataProviderSearchFailed` | `CoreErrorCode.CatalogerSearchFailed` |
| `LevelIdentity.HasCatalogueRecord` | `LevelIdentity.HasCatalogRecord` |

`CoreErrorCode` numeric values are unchanged; only the member names moved. `IndexerProfile`,
`SearchProfile` and `SelectionPolicy` are deliberately untouched.

User-facing labels keep the wording users already know: the settings screen still reads
**Indexers**, **Download Clients**, **Metadata** and **Import Lists**. Only the identifiers changed.

### 0.3.0

**Released**: [Current]

Purely additive: nothing from 0.1.0 or 0.2.0 changed shape in this release.

Five new contract areas were introduced under the per-type experimental scheme that existed at the time.
That scheme was removed on the 0.8 line; these identifiers are retained here only as historical record.

- **Media shape** (`ARX0013`): the parameterized description of how a media kind is structured, and the
  four seams a media extension implements. `MediaLevelId`, `MediaItemRef`, `MediaFileId`, `ExternalId`,
  `ExternalIdScheme`, `OrdinalPath`, `CoordinateKind`, `Coordinate`, `CoordinateConfidence`,
  `CoordinateReading`, `CoordinateSpace`, `CoordinateComponent`, `CoordinateSet`, `SequenceAxis`,
  `SequenceException`, `MediaLevelRoles`, `LevelIdentity`, `MediaLevel`, `VariantSelection`,
  `SelectionTrigger`, `MonitorDimension`, `MonitorDimensionKind`, `AcquisitionScope`,
  `AcquisitionScopeKind`, `FileBinding`, `SpanConstraint`, `SpanRule`, `GroupingAxis`, `GroupingArity`,
  `MemberPosition`, `GroupLifetime`, `FormatFamily`, `SelectionFacet`, `SelectionFacetKind`,
  `ThresholdDirection`, `FacetApplication`, `FacetValue`, `FieldDescriptor`, `FieldValueKind`,
  `FieldSemantics`, `Prominence`, `FieldValue`, `SearchTerm`, `CategoryId`, `SearchKind`, `MediaShape`,
  `RenderedSummary`, `SummaryField`, `SummaryFieldWeight`, `ArtworkRef`, `IMediaShapeProvider`,
  `IReleaseMatcher`, `MatchRequest`, `MatchSource`, `MatchOutcome`, `IReleaseQueryPlanner`,
  `AcquisitionRequest`, `SearchOrigin`, `ReleaseQueryPlan`, `ReleaseQueryTier`, `ReleaseQuery`,
  `SearchArgument`, `IMediaItemSource`, `ItemQuery`, `ItemView`, `ItemPage`
- **Plugin model** (`ARX0014`): the extension entry point, the closed registration surface and the
  capability vocabulary. `PluginId`, `Capability`, `CapabilityNames`, `CapabilitySet`, `IPluginModule`,
  `IPluginContext`, `IPluginRegistry`, `PluginCapabilityException`, `WellKnownJobResults`
  (`WellKnownJobParameters` was in this area until 0.4.0, when it was deleted and its four keys became
  typed members of `JobExecutionContext`)
- **Provider model** (`ARX0015`): added alongside the then-unmarked 0.1.0 provider surface, which was
  deleted in 0.4.0. Several of these types were renamed in 0.4.0; the names below are the current ones.
  `ProviderId`, `ProviderFamily`, `ProviderDescriptor`, `ProviderPreset`, `ProviderDefinition`,
  `DefinitionState`, `ProviderMessage`, `ProviderMessageSeverity`, `ProviderInvocation`,
  `IProviderSessionStore`, `IProvider`, `SettingsField`, `SettingRole`, `SettingSensitivity`,
  `ValidationOutcome`, `ValidationFailure`, `ValidationSeverity`, `DownloadProtocol`, `IIndexer`,
  `ReleaseQueryResult`, `ReleaseFetch`, `IndexerProfile`, `IndexerPrivacy`, `SearchProfile`,
  `SearchTermBinding`, `INotifier`, `NotificationEvent`, `NotificationMessage`, `DownloadAttribution`,
  `FileSummary`, `ICataloger`, `CatalogerCapabilities`, `MetadataQuery`, `MetadataSearchHit`,
  `MetadataGraph`, `MetadataNode`, `SelectionPolicy`, `ICurator`, `CuratedListFetch`,
  `CuratedItem`, `IDownloader`, `GrabRequest`, `DownloadItem`, `DownloadItemStatus`,
  `DownloaderInfo`, `IndexerRegistration`, `DownloaderRegistration`, `NotifierRegistration`,
  `CatalogerRegistration`, `CuratorRegistration`, `ProviderDefinitionChanged`,
  `ProviderChangeKind`
- **Interface intent** (`ARX0016`): what an extension declares about how its media kind is worked with.
  Declarations only — no interface technology and no control vocabulary appears anywhere in this area,
  which is what lets a command line, a text interface or a hands-free client consume the same
  declarations. `ActionDescriptor`, `ActionScope`, `Consequence`, `ConfirmationRequirement`,
  `ActionParameter`, `BrowseAxis`, `BrowseAxisKind`, `SortOption`, `SortDirection`, `FilterOption`,
  `FilterOperators`, `StateDescriptor`, `StateTone`, `ExternalSurfaceDescriptor`, `WorkbenchDescriptor`,
  `WorkbenchSubject`, `WorkbenchColumn`, `WorkbenchProposal`, `WorkbenchRow`, `WorkbenchCommit`,
  `PluginIntentSurface`
- **Wire contracts** (`ARX0017`): what the host publishes to a client that may reference nothing but this
  assembly. `MediaKindDescriptor`, `LevelPresentation`, `Affordance`, `ItemDetail`, `MonitorState`,
  `ItemDetailPage`, `ProgressSummary`, `ActionResult`, `PluginStatusView`, `JobView`, `QueueEntryView`,
  `HealthSnapshotView`, `EventEnvelope`, `EventKind`

Additions to areas that already existed:

- **Telemetry** (`ARX0011`): `TelemetryEmitterExtensions` — `Debug`, `Info`, `Warn` and `Error` helpers
  over the existing emitter. There is deliberately no separate diagnostic contract for extensions.
- **Health**: `CoreErrorCode` gains `PluginManifestInvalid` (2003), `PluginIdConflict` (2004),
  `PluginCapabilityUnsatisfied` (2005), `PluginTokenConflict` (2006),
  `PluginPolicyDeclarationInvalid` (2007), `PluginIsolationViolation` (2008), `PluginShapeInvalid`
  (2009), `PluginDisabled` (2010) and `MediaKindConflict` (3002). No existing member was renumbered or
  renamed.

**The placement rule used for these areas**, stated so that it needs no further judgment: a type belongs
in `Arronix.Abstractions` if and only if it crosses an **assembly-isolation boundary** — the plugin
boundary or the client boundary. Everything else stays in an implementation assembly. This is the
three-tier promotion rule's physical-necessity clause with "plugin boundary" widened to
"assembly-isolation boundary", and it is a **recommended amendment** to
`docs/arronix-common-extraction-plan.md`. The justification is structural rather than stylistic: a client
that may reference only this assembly would otherwise have to mirror the wire types, and a mirrored type
is a guaranteed-divergence defect.

Types deliberately **not** admitted, with the trigger that would admit them: the plugin manifest and
the version-range grammar (nothing constructs a manifest in code, and only the loader parses a range);
the media store (no plugin implementer, and the plugin-side seam is `IMediaItemSource` instead); a job
failure-classification contract and a typed schedule (both would shape a small vocabulary against zero
implementers, and both are addable later). Note that this is an argument about designing against imaginary
requirements, not about irreversibility: below 1.0.0 anything admitted here can be withdrawn by deleting it.

**Historical consequence of the former experimental gate.** At the time, every plugin using the 0.3.0
surface had to declare `">=0.3 <0.4"` and re-declare at each MINOR. The gate and that range-width policy
were removed on the 0.8 line.

### 0.2.0

**Released**: [Current]

Purely additive: nothing from 0.1.0 changed shape in this release.

Every type below was introduced under the per-type experimental scheme that existed at the time. That
scheme was removed on the 0.8 line; the identifiers remain here only as historical record.

Types were added on evidence, not on anticipation. A contract qualifies when it has a demonstrated
second implementer, is explicitly promised by `ARCHITECTURE.md`, or physically must cross the plugin
boundary. Everything else stays a host-side interface in the implementation assembly, where admitting
it later costs nothing.

- **Caching** (`ARX0001`): `ICacheProvider`, `ICache`, `ICache<TValue>`, `ISelfRefreshingCache<TValue>`
- **Diagnostics** (`ARX0002`): `IProgressReporter`, `ProgressLevel`, `IRedactionRuleProvider`, `RedactionRule`
- **Errors** (`ARX0003`): `ArronixException`
- **Events** (`ARX0004`): `IDomainEvent`, `IEventPublisher`, `IEventHandler<TEvent>`
- **File system** (`ARX0005`): `IFileSystem`, `IFilePermissions`, `IFileTransferService`,
  `FileTransferMode`, `IStorageMount`, `IStorageMountProvider`, `StorageMountOptions`,
  `DestinationExistsException`, `PlatformPath`, `PlatformPathKind`
- **Health** (`ARX0006`): `IHealthContributor` — reuses the existing `HealthCheck`, `HealthStatus` and
  `HealthSeverity` verbatim rather than re-declaring them
- **Hosting** (`ARX0007`): `IPluginPaths`, `IOsVersionProbe`, `OsVersionDescriptor`,
  `IOperatingSystemInfo`, `IHostRuntimeInfo`, `IServiceLifecycleController`, `ServiceLifecycleStatus`,
  `INativeLibraryResolver`
- **Outbound HTTP** (`ARX0008`): `IHttpGateway`, `OutboundHttpRequest`, `OutboundHttpResponse`,
  `OutboundHttpResponse<TResource>`, `HttpHeaderCollection`, `IOutboundHttpInterceptor`,
  `HttpGatewayException`, `HttpRateLimitedException`, `UnexpectedHtmlResponseException`,
  `ICertificateValidationPolicy`
- **Naming** (`ARX0009`): `IDiacriticFoldingProvider`
- **Serialization** (`ARX0010`): `IJsonSerializer`
- **Telemetry** (`ARX0011`): `ITelemetrySink`, `ITelemetryEmitter`, `ITelemetryEnricher`,
  `ITelemetryEventFilter`, `TelemetryEvent`, `TelemetrySeverity`
- **Throttling** (`ARX0012`): `IRateLimiter`, `IRateLimitLease`

### 0.1.0 (Initial Release)

**Released**: [Current]

The initial contract set. These types predate the experimental mechanism, which is the only reason they
carry no marker — it is a residue, not a promise. See [The Stable Surface](#the-stable-surface) for how the
residue is tracked and what removes an entry from it.

- **Identity**: `MediaKindId`, `MediaItemId`, `ReleaseId`
- **Media**: `IMediaKind`, `IMediaIdResolver`
- **Parsing**: `IReleaseParser`
- **Quality**: `IQualityModel`
- **Import**: `IImportPipeline`
- **Naming**: `IRenamePolicy`, `ILibraryLayout`
- **Providers**: *(the initial provider trio was deleted in 0.4.0; see that entry)*
- **Scheduling**: `IScheduledJob`, `IBackgroundTaskRegistry`
- **DTOs**: `ReleaseListing`, `ParsedRelease`, `MatchDecision`, `ImportDecision`, `LibraryPathSpec`, `NamingToken`, `QualityTier`, `Language`, `CutoffPolicy`
- **Health**: `HealthCheck`, `HealthStatus`, `HealthSeverity`, `CoreErrorCode`

## Plugin Development Guidelines

### Version Range Selection

A plugin declares the versions it has actually tested. First-party plugins currently use
`">=0.9 <0.10"`. The loader checks only that its installed contract version satisfies the declared range;
it does not reject a wider range independently of that compatibility check.

### Handling a Contract Change

Below 1.0.0, any MINOR release may change contracts:

1. Read the version-history entry for the new release
2. Update the plugin to the new shapes the compiler points at
3. Update the `contracts.arronix` range in `plugin.json` to the new MINOR line
4. Test against the new version
5. Release a new plugin version

From 1.0.0 the same procedure applies, but only at a MAJOR boundary.

## Testing Compatibility

The host runtime provides tools to test plugin compatibility:

- **Contract validation**: Automatic validation during plugin load
- **Version checking**: Runtime checks for compatible version ranges
- **Interface completeness**: Validation that declared capabilities have corresponding implementations

## Questions or Feedback

For questions about API stability or to report compatibility issues:

- Open an issue in the [ArronixCore repository](https://github.com/Arronix/ArronixCore)
- Tag with `contracts` and `api-stability` labels
