# Arronix.Abstractions API Stability Policy

## Version: 0.3.0

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

The consequence for plugins is that a pre-1.0 range spanning more than one MINOR line asserts a promise the
contracts do not make. The loader refuses such a range — see [Plugin loading](#plugin-loading).

## Contract Compatibility

Plugins declare their compatible `Arronix.Abstractions` version range using npm/yarn-style semver ranges in their `plugin.json` manifest:

```json
{
  "contracts": {
    "arronix": ">=0.3 <0.4"
  }
}
```

The host runtime will reject plugin loading if the installed `Arronix.Abstractions` version does not satisfy the plugin's declared range.

## API Stability Levels

### The Stable Surface

**Below 1.0.0 the stable surface is empty by policy.** The intended steady state is that every type exported
by `Arronix.Abstractions` carries `System.Diagnostics.CodeAnalysis.ExperimentalAttribute`. A type without it
is an omission to be closed, not a promise that has been made.

Two reflection-driven fixtures hold the line:

- `src/Arronix.Abstractions.Tests/Experimental/ExperimentalSurfaceTests.cs`
- `src/Arronix.Architecture.Tests/Contracts/ExperimentalMarkingTests.cs`

Each carries a written-out list of the types inherited from the initial release that are still unmarked.
That list is a **ratchet, not a guarantee**: below 1.0.0 it may only shrink, and nothing is ever added to
it. A type leaves the list in one of exactly two ways — it gains the experimental marker like every other
contract, or it is deleted — and either way its entry is removed in the same change, which is what forces
the decision through review rather than letting it happen by omission.

Reading that list as a stability promise is the error this document was rewritten to stop. It records what
shipped before the experimental mechanism existed; it does not record a commitment.

### Experimental APIs

APIs that are still evolving are marked with `System.Diagnostics.CodeAnalysis.ExperimentalAttribute`.
These APIs:

- May change signature or behavior at any time, in any release
- Are promoted to stable at 1.0.0, not before
- Must be opted into per file by any consumer, including this repository

The mechanism is implemented, and every type added since 0.1.0 carries it.

#### Why the BCL attribute rather than a project-local marker

`ExperimentalAttribute` has shipped in the base class library since .NET 8, so using it costs
`Arronix.Abstractions` nothing — the contract library keeps its **zero package references**. A
project-local marker attribute would have cost the same and bought less:

| | BCL `[Experimental]` | Project-local marker |
|---|---|---|
| Consuming an unstable contract | **compile error**, must be opted into explicitly | compiles silently |
| Opt-in is visible in review | yes — a `#pragma` line in the consuming file | nothing to see |
| Tooling support | first-class in the compiler and IDE | none |
| Package cost | none (in-box) | none |

A marker that nobody has to acknowledge is documentation, not a policy. The tier has to be enforced by the
compiler rather than by good intentions, because its purpose is to make the answer to "which files depend on
a contract whose shape has not settled?" available by `grep` at any moment.

#### Diagnostic identifiers

The diagnostic id names the **contract area**, not the individual type, so a consumer opts in to one
area at a time. Identifiers are permanent: when an area is promoted the attribute is removed, but the
identifier is never reused for something else.

| Id | Area | Namespace |
|---|---|---|
| `ARX0001` | Caching | `Arronix.Abstractions.Caching` |
| `ARX0002` | Diagnostics | `Arronix.Abstractions.Diagnostics` |
| `ARX0003` | Errors | `Arronix.Abstractions.Errors` |
| `ARX0004` | Events | `Arronix.Abstractions.Events` |
| `ARX0005` | File system | `Arronix.Abstractions.FileSystem` |
| `ARX0006` | Health contribution | `Arronix.Abstractions.Health` |
| `ARX0007` | Hosting | `Arronix.Abstractions.Hosting` |
| `ARX0008` | Outbound HTTP | `Arronix.Abstractions.Http` |
| `ARX0009` | Naming | `Arronix.Abstractions.Naming` |
| `ARX0010` | Serialization | `Arronix.Abstractions.Serialization` |
| `ARX0011` | Telemetry | `Arronix.Abstractions.Telemetry` |
| `ARX0012` | Throttling | `Arronix.Abstractions.Throttling` |
| `ARX0013` | Media shape | `Arronix.Abstractions.Shape` |
| `ARX0014` | Plugin model | `Arronix.Abstractions.Plugins` |
| `ARX0015` | Provider model | `Arronix.Abstractions.Providers` |
| `ARX0016` | Interface intent | `Arronix.Abstractions.Intent` |
| `ARX0017` | Wire contracts | `Arronix.Abstractions.Wire` |
| `ARX0019` | Declarative media kinds | `Arronix.Abstractions.Definition` |
| `ARX0020` | Typed media model | `Arronix.Abstractions.Media` |

`ARX0018` is unassigned: two in-flight designs contend for it and neither has landed, so the
declarative-media-kind area took the next uncontended identifier rather than racing for the contested
one. Identifiers are permanent either way; `ARX0018` remains available to whichever contender lands
first.

`ARX0015` covers the whole provider namespace. The seven v0.2.0 provider contracts that once shared it
with the experimental surface were **deleted** — see the 0.4.0 entry below — so every type in
`Arronix.Abstractions.Providers` is now experimental and every file that touches one must opt in.

#### How to consume an experimental contract

Opt in **per file**, naming the single area the file depends on:

```csharp
// Implements the outbound HTTP gateway contract, which is experimental until 1.0.
#pragma warning disable ARX0008

using Arronix.Abstractions.Http;
```

Do **not** add the identifier to a project-wide `NoWarn`. A blanket suppression removes the only
signal that tells a reviewer which files will break when an experimental contract changes shape, and
it silently absorbs new dependencies nobody agreed to take. The per-file form keeps the answer to
"what depends on unstable contracts?" a single `grep` away.

Note that `[SuppressMessage]` does **not** work here: the compiler reports this as an error rather
than an analyzer diagnostic, and only `#pragma warning disable` and `NoWarn` suppress it.

#### Plugin loading

The Plugin Loader must refuse to satisfy an experimental contract for a plugin whose declared
`contracts.arronix` range extends past the current MINOR. A plugin declaring `">=0.2 <1.0"` is
asserting compatibility with versions that have not been written yet, and an experimental contract
carries no such promise. Below 1.0.0 this is what keeps a plugin's declared range no wider than the promise
the contracts actually make; from 1.0.0 it is what lets a contract be admitted to `Arronix.Abstractions`
before its shape has fully settled.

## Removing a Contract

**Below 1.0.0: delete it.** Delete the type, its tests, its registration plumbing and its documentation
entries in the same change, and record the deletion in the version history below. Do not mark it
`[Obsolete]`, do not leave a forwarding type, and do not add a bridge or an adapter that keeps the old shape
reachable. Two shapes for one concept is the cost this policy exists to avoid; one compile error is not.

Worked example — a contract in `Arronix.Abstractions` that no plugin implements and that the host reaches
through a handful of call sites:

1. Delete the type.
2. Fix the call sites the compiler names.
3. If the type appears on the residual unmarked-surface list in either fixture named under
   [The Stable Surface](#the-stable-surface), remove that entry in the same change.
4. Add a version-history line: what was deleted, and what replaced it.

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

### 0.6.0

**Released**: [Current]

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

Five new contract areas. Every type in them is marked `[Experimental]` with its area's identifier and is
governed by the rules in the [Experimental APIs](#experimental-apis) section.

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

**Consequence of the experimental gate, spelled out.** Everything added in 0.3.0 is experimental, so the
gate described under [Plugin loading](#plugin-loading) means every plugin that uses any of it must declare
`">=0.3 <0.4"` and re-declare at each MINOR. That is the revocability this policy is written to buy.

### 0.2.0

**Released**: [Current]

Purely additive: nothing from 0.1.0 changed shape in this release.

Every type below is marked `[Experimental]` and is governed by the rules in the
[Experimental APIs](#experimental-apis) section.

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
- **DTOs**: `ReleaseCandidate`, `ParsedRelease`, `MatchDecision`, `ImportDecision`, `LibraryPathSpec`, `NamingToken`, `QualityTier`, `Language`, `CutoffPolicy`
- **Health**: `HealthCheck`, `HealthStatus`, `HealthSeverity`, `CoreErrorCode`

## Plugin Development Guidelines

### Version Range Selection

Below 1.0.0 a plugin declares exactly one MINOR line, because one MINOR line is the whole of what the
contracts promise:

- **Against the current contracts**: `">=0.3 <0.4"`
- **Against the next release**: re-declare when you rebuild against it

Ranges that reach past the current MINOR — `">=0.1 <1.0"`, `">=0.3 <1.0"` — assert compatibility with
versions that have not been written, and are refused. From 1.0.0, a range spanning a MAJOR line becomes the
normal form.

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
