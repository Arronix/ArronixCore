# Arronix Unified Host Runtime — Authoritative Specification (v0.3.0 milestone)

> **Status:** Specification. This is the single document implementers build from. It supersedes the two
> independent proposals (`design-minimal.md`, `design-typed.md`), which are inputs, not authorities.
>
> **Scope:** `Arronix.Abstractions` 0.2.0 → **0.3.0** (additive), plus five new projects
> (`Arronix.Plugins`, `Arronix.Host`, `Arronix.Api`, `Arronix.Client`, four `Arronix.Plugin.*`) and
> three new test projects. Purely additive: nothing under `src/NzbDrone.*`, `src/Sonarr.*`,
> `src/ServiceHelpers`, `frontend/` or `src/Sonarr.sln` is touched.
>
> **Empirical basis:** the six survey reports (4,366 lines) covering Sonarr/TV, Radarr, Lidarr, Readarr,
> Prowlarr and the current Arronix tree. Every non-obvious decision below cites the finding that forced it.
>
> **Governing documents:** `ARCHITECTURE.md` §4/§5/§6/§9/§10/§11, `docs/arronix-common-extraction-plan.md`
> (three-tier promotion rule), `docs/contracts/stability.md` (experimental gate).

---

## 1. Resolution table — how the two proposals were resolved

Two independent architectures were produced: a **minimal-core / evidence-driven** stance (**M**) and a
**strongly-typed-seams** stance (**T**). This specification does not average them. For each disagreement one
side is chosen and the reason is recorded on one line. Where **both are wrong**, the third option is stated
and marked **THIRD**.

Format matches `docs/arronix-common-extraction-plan.md` §"How the three proposals were resolved".

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Are level roles a closed enum or a flag set? | M: `[Flags] MediaLevelRoles`. T: single-valued `LevelRole {Catalog,Work,Variant,Part}` + a separate `ShapeRoles` record. | **M** | T's own kind table writes Radarr's `movie` as "Catalog+Work, **fused**", which a single-valued enum cannot represent — the flag set is the only shape that survives its author's own acceptance test. |
| 2 | Is the file bound to one level or two? | M: `UnitFileBinding.UnitLevelId` (one). T: `FileBinding {AnchorLevel, UnitLevel}` (two). | **T** | Lidarr's `TrackFile.AlbumId` with `Track.TrackFileId` (`lidarr-music.md` §1.2) anchors the file two levels above the unit it satisfies, and file ownership must survive a variant switch; one level is lossy. |
| 3 | How is unit↔file cardinality declared? | M: `AtMostOneFilePerUnit` + `AtMostOneUnitPerFile` + `OrdinalIsMeaningful`. T: `BindingArity` 4-value enum. | **M** | The two booleans *are* the storage uniqueness constraints, written in the language the store enforces them in; the enum is an isomorphic re-encoding that needs a translation table. |
| 4 | Is `Season` a level? | Both: no — a sequence coordinate plus a per-`(parent,coordinate)` policy record. | **Agreed (T's `SequenceAxis` type)** | Sonarr ran `Season` as a table for 20 migrations and dropped it (migration 020/021); T's `SequenceAxis` with `HasPolicyRecord` and `SequenceException(0,"Specials")` is the faithful encoding. |
| 5 | Is addressing declared per level or negotiated per item/release? | Both refute "one scheme per level"; M uses a coordinate **bag** with string components, T a closed `AddressingScheme` union with typed `Coordinate` values. | **THIRD** | The host must *order* coordinates (browse by season, completeness gaps, next-airing), so M's `Dictionary<string,string>` is wrong; T's abstract-record union with `Match<T>` is behavior in a contract library and needs polymorphic JSON to reach the client. Adopted: **typed tagged structs** — `CoordinateSpace` declarations + a `Coordinate` tagged struct over `OrdinalPath`/`DateOnly`/`string`/none. |
| 6 | Discriminated unions on the wire | M: strings everywhere. T: `[JsonPolymorphic]`/`[JsonDerivedType]` abstract records. | **THIRD** | Polymorphic JSON across two independently-versioned assemblies (plugin *and* browser) is the versioning trap M names, but stringly-typed values lose ordering. Rule adopted: **closed vocabularies are enums; union values are tagged records/structs with a `Kind` discriminator and nullable payload slots. No `[JsonPolymorphic]` anywhere in Abstractions.** |
| 7 | Is the catalog/library split part of the shape? | M: no — "storage normalization, not a shape level". T: yes — `LevelIdentity {HasCatalogRecord, HasLibraryRecord, SupportsIdentifierRedirects}`. | **T** | Three of four *arrs invented it independently (`MovieMetadata`/`ArtistMetadata`/`AuthorMetadata`) and every downstream FK points at the metadata id; whether a level admits rows that are *not* library members is not derivable and the store cannot guess it. |
| 8 | How does a plugin execute media semantics? | M: three plain interfaces. T: `IMediaDomain` + an abstract generic base class `MediaDomain<TItem,TRelease>` in Abstractions. | **M** | A base class ships behavior from a pure contract library, and every member it adds is a member every implementer inherits whether it wants to or not; the generic safety it buys is safety *within one plugin assembly*, which the plugin already has for free with its own private types. |
| 9 | Which behavior seams exist at all? | M: shape provider, coordinate resolver, query planner. T: those plus addressing policy, completeness policy, post-import mutator, library backend, specification framework. | **THIRD** | Four seams, not three and not seven: `IMediaShapeProvider`, `IReleaseMatcher`, `IReleaseQueryPlanner`, `IMediaItemSource`. Coordinate *ordering* falls out of typed coordinates; *completeness* is computable from `FileBinding` + `SelectionSpec` + `SequenceException`; tag writeback happens inside the post-placement commit hook, which `acquisition-pipeline.md` §9 renames to `IImportPlanner.CommitAsync` when `IImportPipeline` is deleted. |
| 10 | Who owns item rows in a milestone with no persistence? | Neither confronts it: M has plugins declare a shape and never hold data; T has an in-memory `IMediaStore` nothing writes to. | **THIRD** | With no persistence and no metadata fetch, both designs ship a UI that can only ever render empty lists. Adopted: **`IMediaItemSource` — the plugin projects its own catalog; the host stores only library state (monitoring, links, selected variant) and merges.** This is the surveyed catalog/library split made into the runtime topology. |
| 11 | Does the plugin see `IServiceCollection`? | Both: no; a closed registration surface. | **Agreed (T's naming)** | `Arronix.Abstractions` may take no packages, so `IServiceCollection` is unreachable by construction — the constraint forces admission-control instead of post-hoc filtering, which is what makes §11 mechanical. |
| 12 | `Configure` signature | M: `Configure(IPluginRegistrationBuilder)` with `.Context`. T: `Configure(IPluginContext)` with `.Registry`. | **T** | The context is the root of everything a plugin may reach; hanging registration off it makes "what can a plugin see?" one interface read, and "builder" wrongly implies a `Build()` step. |
| 13 | Capabilities: strings or enum? | M: `string` consts + host-side validator, because stable `IMediaKind.Capabilities` is `IReadOnlyList<string>`. T: `Capability` enum + `CapabilitySet` bitset, with the host projecting `IMediaKind`. | **T** | Plugins never implement `IMediaKind` — the host does, projecting from shape + manifest — so the stringly-typed list becomes a *projection* rather than a source of truth, and the implication closure (§11.1) becomes a bitset operation rather than string arithmetic. |
| 14 | Which capabilities exist? | M: the 14 from the extraction plan. T: those + `MediaKind`, `ImportList`, `ProviderDefinitions`, `LibraryBackend`, `PostImportMutation`. | **THIRD** | Adopt `MediaKind` and `ImportList` (both have a demonstrated gated registration this milestone); **drop `PostImportMutation`** (it gates nothing new — the file write already goes through `storage`/`import`); **defer `ProviderDefinitions` and `LibraryBackend`** (zero implementers); **drop `process` and `distribution`** so the forward check stays exact. |
| 15 | Capability implication closure | M/ARCH §11.1: `indexing\|metadata\|download\|notification ⇒ network`. T additionally: `import\|libraryBackend\|postImportMutation ⇒ storage`. | **M, extended only to `importList ⇒ network`** | An importer needing both file *placement* and file *reading* should say so; silently granting `storage` from `import` widens least privilege for ergonomics, which is the wrong trade. No orchestrator approval is therefore required. |
| 16 | Contract-range grammar | M: `= > >= < <=`, whitespace-AND, `\|\|`, partials, `x`/`*`; reject `^ ~` and hyphen. T: all of npm bar hyphen ranges and `x`/`X`. | **THIRD** | Take M's rejection of `^`/`~` (0.x semantics have been ambiguous for a decade; every governing document writes `>=A <B`) **and** T's rejection of wildcards ("four ways to write the 0.3 line is three too many"). Grammar = comparators + whitespace-AND + `\|\|` + partial versions. Nothing else. |
| 17 | What happens when the `stability.md` experimental gate fails? | M: load failure. T: plugin loads at `ContractTier.Stable`, gets no seams, fails the forward check later. | **M, with T's diagnostic** | "Loaded and inert" is a state no operator can interpret; reject at validation with `PluginContractMismatch` and a message naming the range to declare (`">=0.3 <0.4"`). |
| 18 | Are `SemanticVersion` / `VersionRange` contracts? | M: held host-side. T: promoted to Abstractions. | **M** | Only the loader parses a range; a plugin doing semver arithmetic against its host is a smell, and `IPluginContext` can expose plain version strings. |
| 19 | Is `PluginManifest` a contract? | Both: promoted to Abstractions. | **THIRD — held host-side** | Nothing constructs a manifest in C#: it is JSON, parsed by the loader, and everything a plugin legitimately reads from it (`PluginId`, version, granted capabilities) is already on `IPluginContext`. The client gets a `PluginStatusView` wire DTO instead. |
| 20 | Static reference-graph validation | M: absent. T: `PEReader`/`MetadataReader` over the `AssemblyRef` table before any ALC exists. | **T** | It is the cheapest and strongest control in the loader — invariant 2 checked without executing a single type initializer — and `System.Reflection.Metadata` is verified present in the `net10.0` shared framework, so it costs no package. |
| 21 | ALC resolution for blocked assemblies | Both: **throw**, never return `null`. | **Agreed** | Returning `null` falls back to the Default context and silently *succeeds*, granting the plugin `Arronix.Common`; this is the one ordering that works and it gets a test. |
| 22 | Schedule grammar | M: `manual` / `every <duration>` / `daily HH:mm`, explicitly not cron. T: `manual\|startup\|PT15M\|cron(5 fields)` plus a typed `JobSchedule` union in Abstractions. | **THIRD** | M's grammar plus T's `startup` literal (Sonarr/Radarr genuinely run tasks at startup); **no cron** (no surveyed *arr uses it; adding it later is additive); **no typed contract** — `RegisterJob(job, string)` already fixes the shape and a parallel typed API would be a second source of truth (which is the reason; the method's version status is not). |
| 23 | Plugin-supplied failure classification | M: published keys in the `JobExecutionResult.ResultData` bag + host-side classifier. T: `IJobFailureClassifier` promoted to Abstractions. | **M — on merit** | A five-value enum guessed from four surveyed applications, promoted against zero implementers, is exactly what the three-tier rule exists to stop. The keys stay because *the vocabulary is not settled*, not because the record could not be changed — below 1.0.0 it can be. `WellKnownJobResults` says so in its own remarks, and names the trigger: the first job the five classes fail promotes the enum and deletes the keys. |
| 24 | Correlation id, progress, media kind and item list on `JobExecutionContext` | M: reserved `Parameters` keys. T: scoped DI + reserved keys. | ~~**M**~~ → **THIRD — typed members on the record (0.4.0)** | M was chosen only because `JobExecutionContext` was classed as stable and therefore unchangeable before 1.0; that classification is withdrawn (`docs/contracts/stability.md`, resolution 1). All four are platform facts, so they are typed members: `CorrelationId`, `Progress`, `MediaKind`, `Items`. `WellKnownJobParameters` is **deleted** — a published string key over an `object` bag is not a weaker typed member, it is an unchecked one. T's scoped DI still loses: plugins have no DI scope here, so it reaches only host-internal code. `JobExecutionResult.ResultData` survives on merit, not on version grounds — see row 23. |
| 25 | Notification payload | M: `NotificationEnvelope` with a single `Message` string + open `EventTypeId` strings. T: `NotificationMessage` with a structured `RenderedSummary` + closed `NotificationEvent` enum. | **T** | Discord embeds, Web Push payloads and the client's activity feed all want title/subtitle/artwork/deep-link; a single string forces every provider to re-derive structure, and the host *dispatches* on the event so an open vocabulary would be silently ignored. |
| 26 | Indexer search-parameter vocabulary | M: newznab wire strings carried verbatim into the media-kind declaration (`InteropSearchMode`, `InteropName`). T: a closed 9-member `SearchTerm` enum derived from the intersection of five parallel param enums. | **T for the vocabulary, THIRD for the matching** | `prowlarr-indexers.md` §3.1's own recommendation is to read the intersection; but neither side should name the other's nouns, so an indexer declares `SearchProfile {Terms, Categories}` with **no media-kind reference** and a kind declares `SearchKind {RequiredTerms, Categories}`. Eligibility is set intersection. The newznab `t=` mode never appears in a shape. |
| 27 | Newznab category ids | Both: carried verbatim as interop identifiers, `>=100000` reserved. | **Agreed** | A deployed cross-media taxonomy every indexer on earth already speaks; replacing it buys nothing and costs a mapping table per indexer. |
| 28 | Provider instance model | M: `IProviderFactory<T>.Create(definition)` → fresh instance + host-side instance cache. T: stateless singleton + `ProviderInvocation<TSettings>` on every call. | **THIRD** | T's statelessness (shared mutable state *unrepresentable*, fixing the surveyed `GetInstance` race) with M's non-generic declarative settings: `ProviderInvocation { Definition, Session, CorrelationId }`, settings read from `Definition.Settings`. No generics, no instance cache, no mutation. |
| 29 | Settings schema: attribute or declarative? | M: declarative only. T: both — a `SettingAttribute` plus a declarative twin, reflected by `SettingsSchemaReader` in Common. | **M** | A plugin's reflection over its own types crosses no boundary and needs no contract; two representations of one schema guarantee drift, and the attribute path forces the *host* to reflect over plugin types, which the closed registration surface exists to avoid. |
| 30 | FluentValidation in the provider contract | Both: removed, first-party `ValidationOutcome`. | **Agreed** | `Arronix.Abstractions` has zero package references and must keep them; a plugin compiled against FluentValidation vN cannot negotiate with a host on vN+1. |
| 31 | Superseding the stable `IIndexerProvider` | M: sibling contract + `[Obsolete]` at 0.4, removed at 1.0. T: new contract + a host-side bridge, nothing deprecated. | ~~**T**~~ → **REVERSED by the product owner (0.4.0): neither. The legacy trio is deleted outright.** | The original choice of T assumed the legacy contracts were worth keeping alive. The product owner has ruled they are not: Arronix is a clean-sheet rewrite, nothing has ever shipped against `IIndexerProvider`, `IMetadataProvider` or `IDownloadClientAdapter`, and there is therefore no compatibility to preserve. All three are deleted, along with their DTOs and the `StableProviderBridge` that existed only to serve them — no `[Obsolete]`, no shim, no dual contract. Migration from specific *arr versions will be handled later by dedicated migration scripts, which is the right place for version-specific knowledge; carrying two provider vocabularies through the codebase forever is not. The five renamed families (`Indexer`, `Downloader`, `Notifier`, `Cataloger`, `Curator`) are now the only provider surface. |
| 32 | Where do host↔client DTOs live? | M: only if a plugin produces/consumes them; queue rows and job status are client-mirrored. T: all wire DTOs in Abstractions. | **T, using M's own proposed amendment** | `Arronix.Client` may reference nothing but Abstractions, so a mirrored type is a guaranteed-divergence bug; the promotion rule's clause 3 is widened from "plugin boundary" to **"assembly-isolation boundary"**, which M itself recommends. Volume is held to ~12 wire types. |
| 33 | Field importance | M: `SummaryFieldIds[]` / `DetailFieldIds[]` lists on a browse descriptor. T: `Prominence {Primary,Secondary,Detail,Diagnostic}` on the field. | **T** | An importance rank on the field is DRY, orders naturally, and is meaningful to a card face, a table's default columns and a `--verbose` flag alike; two parallel id lists drift. |
| 34 | State valence | M: `StateTone {Neutral,Positive,Attention,Problem,InProgress}`. T: reuse `HealthSeverity` on enumeration members. | **M** | `HealthSeverity` has no way to say "have it" or "in progress"; dropping tone entirely would force the client to recognize `"missing"`/`"have"`, i.e. compile-time knowledge of a media kind. |
| 35 | Affordances (`Monitorable`, `Selectable`, `Browsable`, …) | T: declared by the plugin. M: absent. | **THIRD — derived by the host** | Every affordance is computable from the validated shape (`Selectable` ⟸ `VariantAxis`; `Monitorable` ⟸ the level has monitor dimensions; `Browsable` ⟸ the level has a child) or is a host feature (tags, root folders, exclusions); a declaration that can be derived is a declaration that can disagree. |
| 36 | Can the descriptor UI express a real media-management UI? | M: "yes, but manual N:M file→unit assignment is clunky" — recorded as the weakest point. T: "no bespoke visualizations", escape hatch is `Navigate(Uri)`. | **THIRD — both under-deliver; a bounded escape hatch is specified** | Manual import (N loose files → M units, with a plugin-supplied proposal — Lidarr's Munkres output) is a daily core workflow, not an exotic one, and "an action with a collection parameter of triples" is not a UI. Adopted: **`WorkbenchDescriptor`** — a declared row/column editable grid over a plugin-supplied proposal, rendered by one generic client component. It also covers interactive release search and bulk edit. See §6.5. |
| 37 | ARCHITECTURE §15 "Dynamic UI component registry" | Both: rejected. | **Agreed — reject permanently, recommend amending §15** | Plugin-supplied components put plugin code in the browser, entirely outside the capability model, and weld the client's UI technology into the plugin contract so a CLI or mobile client becomes structurally impossible. |
| 38 | Does `Arronix.Api` reference `Arronix.Client`? | M: yes (`Components.WebAssembly.Server` + `UseBlazorFrameworkFiles`). T: no — MSBuild copy + hand-rolled content types. | **THIRD** | T is right that a project reference makes "Api is NOT Blazor" unverifiable, but an MSBuild publish-output copy is fiddly for zero benefit here. Adopted: **Api serves a configured client root** (`Arronix:Api:ClientRoot`), no reference, no Blazor package, **zero `PackageReference` at all** — the strongest possible form of the invariant-4 test, and the client stays independently hostable. |
| 39 | Branded id types | M: reuse the 0.1.0 style (implicit `string` conversions). T: six new branded ids with no implicit conversions. | **THIRD — exactly two, and one style** | `MediaLevelId` and `PluginId` get brands (they appear in cross-referencing positions where confusion is real and where the validation gate resolves them); `axisId`/`spaceId`/`fieldId`/`facetId`/`actionId` stay `string` dictionary keys where a brand buys nothing. Six new id structs is ceremony; zero is a latent bug class. **One style, not two:** the implicit conversions are removed from `MediaKindId`, `MediaItemId` and `ReleaseId` too, so every identity type in the assembly behaves the same way (§2.2, audit row 12). Keeping two conventions was only ever a consequence of treating the first three as unchangeable. |
| 40 | Parse-don't-validate | M: a 12-rule `MediaShapeValidator`, id lookups remain downstream. T: `ValidatedShape` — one gate turns id-references into object-references. | **T, with M's rule list** | After the gate no host lookup can fail and no id can dangle; M's numbered rules become `ValidatedShape.TryValidate`'s defect list, so both halves are kept and nothing is lost. |
| 41 | `IMediaStore` promotion | Both: held host-side (Tier B). | **Agreed** | Zero plugin implementers; ARCHITECTURE §5 names it a Storage-Layer concern; and designing a plugin-facing store against no plugin that wants one is designing against an imaginary requirement. *(Not because promotion is irreversible — below 1.0.0 admission to Abstractions is undone by deleting the type. See `docs/contracts/stability.md`.)* |
| 42 | Failed plugins | Both: quarantine, never fatal — the deliberate inversion of Prowlarr's `RemoveMissingImplementations`. | **Agreed** | Under a plugin model, delete-on-missing means uninstalling a plugin destroys the user's configuration. |
| 43 | Token collisions (ARCH §9 "under evaluation") | M: host-global → reject; cross-kind same token → allow; same kind → reject by ordinal. T: always namespaced in storage, bare form legal when unambiguous. | **M** | Two plugins claiming one media kind is already rejected at load, so the ambiguity T's namespacing solves cannot arise; three rules beat a storage-format change. |
| 44 | Token declaration | M: `string[]` in the manifest. T: `NamingToken[]` (the currently-unused 0.1.0 DTO). | **T, sourced from the shape — and `NamingToken` is widened in place** | Inventing a third representation is the smell, and the *shape* is the source of truth, with the manifest's `tokens[]` cross-checked against it at load (a §4.1 step-4 check that can actually be performed). `NamingToken` is a four-member positional record that **nothing returns and nothing consumes**, so it is *widened* to carry what validation needs (`CanonicalName`, `ValueKind`, `Origin`, `LevelId`, `Elasticity`, `IsInjective`, `Depth`) rather than wrapped by a second type — see `naming-and-tokens.md` resolution 5. Widening changes its equality and its `ToString`, which would matter if anything depended on either; nothing does, and below 1.0.0 nothing is frozen. |
| 45 | Level hierarchy: chain or tree? | M: a linear list, root→leaf. T: `Parent` pointers, validated acyclic. | **THIRD** | Carry T's parent pointer (costs nothing, forecloses nothing) but validate M's constraint (**each level has at most one child**) in this milestone; relaxing the rule later is a validator change, not a contract change. |
| 46 | Legacy compatibility API (ARCH §7 phase 1) | M: dropped. T: silent. | **M** | New platform, no legacy upgrade path (product decision); a compatibility shim would wrap nothing. |
| 47 | Plugin diagnostics/logging | T: no new contract — `ITelemetryEmitter` at `Debug`/`Info` plus extension methods. M: silent. | **T** | Abstractions cannot reference `Microsoft.Extensions.Logging.Abstractions`, and a parallel `IPluginLogger` would be a rival logging vocabulary; one pipeline, one correlation id, zero new contracts. |
| 48 | OpenAPI / Swagger | M: `Swashbuckle.AspNetCore.{SwaggerGen,Annotations}`. T: requests registration of `Swashbuckle.AspNetCore.Swagger` + `.SwaggerUI`. | **THIRD — deferred entirely** | Verified: `SwaggerGen` alone cannot *serve* a document and the serving packages are not registered; `Microsoft.AspNetCore.OpenApi` is not in the ASP.NET Core shared framework either. Dropping OpenAPI from this milestone removes the only package request in either design. |
| 49 | New contract areas | M: 5 (`ARX0013`–`ARX0017`). T: 6 (adds `ARX0018` Scheduling). | **M's count, T's ordering** | No scheduling contract is added (see #22, #23), so five areas: `ARX0013` Shape, `ARX0014` Plugins, `ARX0015` Providers, `ARX0016` Intent, `ARX0017` Wire. |
| 50 | Selection facets (the third profile axis) | M: enumerated values only. T: closed union `Enumerated \| Threshold \| Flag`. | **T's coverage, this document's encoding** | Readarr ships 4 flags + 2 thresholds + 1 enumerated (`MinPopularity`, `MinPages`, `SkipMissingDate`…); M's enumerated-only cannot express `MinPages`. Encoded as a tagged record per #6, and M's `FacetApplication {Materialization, Visibility}` naming is kept because it says *when* the filter applies. |

**Build risks neither design identified** (found by reading `src/Directory.Build.props`, §7.6):

- `src/Directory.Build.props` assigns a **default `RuntimeIdentifier` of the host RID** when one is not
  specified. `Arronix.Client` must set `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` and clear
  `<RuntimeIdentifiers/>` explicitly, or the WASM build is wrong.
- Non-`Sonarr*` projects output to `_temp/bin/$(Configuration)/$(MSBuildProjectName)/`; `*.Tests` output to
  `_tests/` because `SonarrOutputType` keys on `.Contains('.Test')`.
- `System.Reflection.Metadata` and `System.Collections.Immutable` **are** in the `net10.0`
  `Microsoft.NETCore.App` ref pack (verified) — the reference-graph inspector needs no package.
- SignalR **server** is in the ASP.NET Core shared framework — `Arronix.Api` needs no package for it.

---

## 2. The media-shape model

Namespace `Arronix.Abstractions.Shape`, diagnostic **`ARX0013`**, files under `src/Arronix.Abstractions/Shape/`.
Every type is `sealed record`, `readonly record struct` or `enum`, marked
`[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]`.

### 2.1 Verdict on the hypothesis

The hypothesis — *"one parameterized shape: a hierarchy of node roles (Root/Group/Unit), an addressing
scheme per level, which level is the acquisition unit, which is the file-bearing unit, the cardinality
between file and unit, and orthogonal grouping axes"* — is **directionally right and wrong in four places**:

| Element | Verdict | Decisive evidence |
|---|---|---|
| One parameterized shape | **CONFIRMED** | All four file↔unit relations are projections of `(unitId, fileId, ordinal?)` — `sonarr-tv-and-crosscut.md` §A3. |
| Roles `Root/Group/Unit` | **REFUTED** — "Group" fuses two opposites, and roles are not mutually exclusive | Sonarr *demoted* `Season` (migrations 020/021) while Lidarr *promoted* `AlbumRelease` and Readarr `Edition`: sequence axes stay coordinates, variant axes get entities. And Radarr's `movie` carries every role at once. |
| An addressing scheme **per level** | **REFUTED** — addressing is per *item* and per *release*, and multi-valued | `SeriesTypes` is on the individual `Series` row (`Tv/Series.cs:44`); `Episode` carries six coordinate fields, three nullable, one with a confidence flag; `ParsingService`'s branch predicates read the parsed **release** (`IsDaily => !string.IsNullOrWhiteSpace(AirDate)`). |
| A singular acquisition unit / file-bearing unit | **REFUTED** | Lidarr: membership=`Artist`, acquisition=`Album`, selection=`AlbumRelease`, **file anchor=`Album`**, completeness=`Track` — five levels, six roles. Plus season packs and discographies, which are not levels at all. |
| Cardinality as a 3-valued enum | **REFUTED** | Four observed states, N:M not structurally forbidden, and Sonarr forbids a file spanning *seasons* while permitting it to span *episodes* — a constraint on a coordinate axis that no cardinality number can express. |
| Orthogonal grouping axes | **CONFIRMED, and stronger** | `SeriesBookLink` is true M:N with a **string** `Position` ("2.5", "1-3", ""), `IsPrimary`, refcounted lifetime and an orphan sweeper. |

**Four things the hypothesis omits, all load-bearing:** the catalog/library split; variant-selection
semantics with an import-time auto-switch; format families with their own quality ladder; and the sub-item
selection policy (the third profile axis).

### 2.2 Identity and value primitives

```csharp
// src/Arronix.Abstractions/Shape/MediaLevelId.cs
namespace Arronix.Abstractions.Shape;

/// <remarks>
/// Deliberately without two-way implicit <c>string</c> conversions: they would make a level id and a
/// grouping-axis id mutually assignable, which is the exact confusion this brand exists to prevent.
/// <c>MediaKindId</c>, <c>MediaItemId</c> and <c>ReleaseId</c> carry those conversions today only because
/// they were written first. They are being brought into line — the conversions are removed from all three
/// so the identity vocabulary is uniform (audit row 12). An earlier draft of this remark said the three
/// "cannot be changed, so the inconsistency is documented rather than propagated"; that premise is
/// withdrawn (`docs/contracts/stability.md`), and a deliberate permanent inconsistency in the identity
/// vocabulary was never worth the price of not editing three structs. The compiler names every affected
/// call site.
/// </remarks>
public readonly record struct MediaLevelId
{
    public string Value { get; }
    public static MediaLevelId FromString(string value);   // throws on null/whitespace
    public override string ToString();
}

// src/Arronix.Abstractions/Shape/MediaItemRef.cs
/// <summary>The host's only handle on a plugin's data. Kind + level + id, always together.</summary>
public readonly record struct MediaItemRef(MediaKindId Kind, MediaLevelId Level, MediaItemId Id);

/// <summary>A file identity. Files are host-owned, so this is an opaque surrogate.</summary>
public readonly record struct MediaFileId(int Value)
{
    public static MediaFileId FromInt(int value);
    public override string ToString();
}

// src/Arronix.Abstractions/Shape/ExternalId.cs
/// <remarks>
/// Closes the hard blocker in <c>sonarr-tv-and-crosscut.md</c> §B5: S/R use <c>int</c> external ids,
/// L/K use <c>string</c>, and no shared contract can span that with a primitive. The scheme is an OPEN
/// value — the host carries, compares and groups by it, and never branches on it.
/// </remarks>
public readonly record struct ExternalId(string Scheme, string Value)
{
    public static ExternalId Of(string scheme, string value);
    public static ExternalId Of(string scheme, long value);
    public static bool TryParse(string text, out ExternalId id);   // "tvdb:81189"
    public override string ToString();                             // "tvdb:81189"
}

public sealed record ExternalIdScheme
{
    public required string Scheme { get; init; }     // "tvdb", "musicbrainz", "isbn13"
    public required string Name { get; init; }
    public bool IsPrimary { get; init; }
}
```

```csharp
// src/Arronix.Abstractions/Shape/OrdinalPath.cs
/// <remarks>
/// A lexicographically-comparable ordinal tuple of up to four components, stored inline. Deliberately NOT a
/// collection: <c>IReadOnlyList&lt;long&gt;</c> inside a record gives reference equality, which is a silent
/// dedupe bug. Serializes as its <see cref="ToString"/> form ("3.14") and round-trips through
/// <see cref="TryParse"/>.
/// </remarks>
public readonly record struct OrdinalPath : IComparable<OrdinalPath>
{
    public static OrdinalPath Empty { get; }
    public static OrdinalPath Of(long a);
    public static OrdinalPath Of(long a, long b);
    public static OrdinalPath Of(long a, long b, long c);
    public static OrdinalPath Of(long a, long b, long c, long d);
    public int Length { get; }
    public long this[int index] { get; }
    public int CompareTo(OrdinalPath other);        // lexicographic; shorter-is-lesser on a shared prefix
    public override string ToString();              // "3.14"
    public static bool TryParse(ReadOnlySpan<char> text, out OrdinalPath value);
}
```

### 2.3 Coordinates — the bag, typed

A level **admits** a set of declared coordinate spaces; a unit carries a **bag of readings**, zero or more
populated. Resolution is negotiated at runtime by the plugin (§2.9), never by a schema field.

```csharp
// src/Arronix.Abstractions/Shape/Coordinate.cs

/// <summary>Closed. A client or CLI switches exhaustively; a missing case is CS8509.</summary>
public enum CoordinateKind
{
    /// <summary>An ordinal tuple: [season, episode], [disc, track], [absolute].</summary>
    Ordinal = 0,
    /// <summary>A calendar date. Sonarr's daily-series addressing.</summary>
    Date = 1,
    /// <summary>A label that may not parse as a number: "A1", "2.5", "1-3", "". Equatable, not orderable.</summary>
    Label = 2,
    /// <summary>The level addresses itself; there is exactly one. A movie.</summary>
    Singleton = 3,
}

/// <remarks>
/// A tagged struct rather than a closed record union: this value crosses BOTH the plugin boundary and the
/// browser boundary, and polymorphic JSON across two independently-versioned assemblies is the versioning
/// trap this design refuses. Exactly one payload slot is populated per <see cref="Kind"/>.
/// </remarks>
public readonly record struct Coordinate
{
    public CoordinateKind Kind { get; init; }
    public OrdinalPath Ordinals { get; init; }
    public DateOnly? Date { get; init; }
    public string? Label { get; init; }

    public static Coordinate OfOrdinals(OrdinalPath path);
    public static Coordinate OfDate(DateOnly value);
    public static Coordinate OfLabel(string value);
    public static Coordinate Singleton { get; }
    public override string ToString();
}

public enum CoordinateConfidence
{
    /// <summary>Extrapolated or guessed. Sonarr's <c>UnverifiedSceneNumbering</c>.</summary>
    Unverified = 0,
    /// <summary>Derived from another coordinate by a declared mapping.</summary>
    Derived = 1,
    /// <summary>Asserted by a release title or by a file.</summary>
    Asserted = 2,
    /// <summary>Confirmed against the catalog record.</summary>
    Verified = 3,
}

public readonly record struct CoordinateReading(
    string SpaceId, Coordinate Value, CoordinateConfidence Confidence);

/// <summary>The bag <c>sonarr-tv-and-crosscut.md</c> §A2 proves a fixed schema cannot express.</summary>
public sealed record CoordinateSet
{
    public static CoordinateSet Empty { get; }
    public static CoordinateSet Of(params CoordinateReading[] readings);
    public required IReadOnlyList<CoordinateReading> Readings { get; init; }
    public bool TryGet(string spaceId, out CoordinateReading reading);
    public CoordinateSet With(CoordinateReading reading);
}
```

```csharp
// src/Arronix.Abstractions/Shape/CoordinateSpace.cs
public sealed record CoordinateSpace
{
    public required string SpaceId { get; init; }        // "aired", "absolute", "scene", "airdate", "medium-track"
    public required string Name { get; init; }
    public required CoordinateKind Kind { get; init; }
    /// <summary>One entry per tuple component, outermost first. Empty unless Kind == Ordinal.</summary>
    public IReadOnlyList<CoordinateComponent> Components { get; init; } = [];
    /// <summary>The space identity and completeness are measured in. Exactly one per level that has any.</summary>
    public bool IsCanonical { get; init; }
    /// <summary>Valid only when the source string came from a release name — Sonarr's <c>sceneSource</c>.</summary>
    public bool IsProvenanceSensitive { get; init; }
    /// <summary>Values may be extrapolated rather than mapped — Sonarr's <c>UnverifiedSceneNumbering</c>.</summary>
    public bool MayBeUnverified { get; init; }
    /// <summary>True when the sequence has no holes and a gap means "missing".</summary>
    public bool IsDense { get; init; }
}

public sealed record CoordinateComponent(string ComponentId, string Name, bool Required);
```

**Scene numbering is not a sixth scheme.** It is a non-canonical, provenance-sensitive,
possibly-unverified space over the same `Ordinal` kind — which is exactly what Sonarr models with
`SceneSeasonNumber`/`SceneEpisodeNumber`/`SceneAbsoluteEpisodeNumber` + `UnverifiedSceneNumbering`,
populated by `XemService` which extrapolates and flags its own confidence.

### 2.4 Sequence axes — the demoted `Season`

```csharp
// src/Arronix.Abstractions/Shape/SequenceAxis.cs
/// <remarks>
/// <c>sonarr-tv-and-crosscut.md</c> §A1's conclusion as a type: "Season is two different things fused
/// together — (1) a grouping coordinate carried on the leaf unit, (2) a monitoring/selection policy record
/// attached to the parent." This is (1) plus a flag for (2). It is NOT a level and never gets a row.
/// </remarks>
public sealed record SequenceAxis
{
    public required string AxisId { get; init; }          // "season", "disc"
    public required string Name { get; init; }            // "Season", "Disc"
    public required string PluralName { get; init; }
    /// <summary>The coordinate space this axis names a component of. Must be Kind == Ordinal.</summary>
    public required string SpaceId { get; init; }
    public required int ComponentIndex { get; init; }
    /// <summary>A per-(parent, coordinate) policy record exists: a monitored bit, artwork, a folder override.</summary>
    public bool HasPolicyRecord { get; init; }
    /// <summary>Values outside the dense run that mean something else. Season 0 = specials.</summary>
    public IReadOnlyList<SequenceException> Exceptions { get; init; } = [];
}

/// <remarks>Replaces every hard-coded <c>SeasonNumber &gt; 0</c> test in Sonarr's naming and statistics.</remarks>
public readonly record struct SequenceException(long Value, string Name, bool ExcludedFromCompleteness);
```

### 2.5 Levels, roles and identity

```csharp
// src/Arronix.Abstractions/Shape/MediaLevelRoles.cs
/// <remarks>
/// A CLOSED flags enum, unlike the open string vocabularies elsewhere, because the host DISPATCHES on it:
/// an unrecognized role would be silently ignored, which is worse than a MINOR bump. Several roles land on
/// one level routinely — Radarr's <c>movie</c> carries all five.
/// </remarks>
[Flags]
public enum MediaLevelRoles
{
    None             = 0,
    /// <summary>What a user "adds". Owns path, profiles, tags, root folder. Exactly one per shape.</summary>
    LibraryEntry     = 1 << 0,
    /// <summary>What a search or a grab targets.</summary>
    AcquisitionUnit  = 1 << 1,
    /// <summary>Competing manifestations of the parent, with at-most-one-selected semantics.</summary>
    VariantAxis      = 1 << 2,
    /// <summary>What "have / missing" counts.</summary>
    CompletenessUnit = 1 << 3,
    /// <summary>Participates in the unit↔file join.</summary>
    FileBearing      = 1 << 4,
}

// src/Arronix.Abstractions/Shape/LevelIdentity.cs
/// <remarks>
/// The catalog/library split three of four *arrs invented independently (<c>MovieMetadata</c>,
/// <c>ArtistMetadata</c>, <c>AuthorMetadata</c>). It is not a hierarchy level; it is a facet of a level, and
/// no store can guess it: whether rows may exist for entities the user has not added determines every
/// downstream foreign key.
/// </remarks>
public sealed record LevelIdentity
{
    /// <summary>A globally-deduplicated, foreign-id-keyed record shared across library entries.</summary>
    public required bool HasCatalogRecord { get; init; }
    /// <summary>User-owned state: monitoring, profiles, tags, added-at. (Path lives on the LibraryEntry.)</summary>
    public required bool HasLibraryRecord { get; init; }
    /// <summary><c>OldForeignArtistIds</c>-style redirect chain on identifier merges.</summary>
    public bool SupportsIdentifierRedirects { get; init; }
    public IReadOnlyList<ExternalIdScheme> ExternalIds { get; init; } = [];
}

// src/Arronix.Abstractions/Shape/MediaLevel.cs
public sealed record MediaLevel
{
    public required MediaLevelId Id { get; init; }
    public required string Name { get; init; }         // "Season" is NOT a level — see SequenceAxis
    public required string PluralName { get; init; }
    /// <summary>Null only for the root. Validated acyclic; in 0.3.0 also validated to be a linear chain.</summary>
    public MediaLevelId? Parent { get; init; }
    public MediaLevelRoles Roles { get; init; } = MediaLevelRoles.None;
    public required LevelIdentity Identity { get; init; }
    /// <summary>Coordinate spaces a unit at this level MAY carry. Zero or more populated per unit.</summary>
    public IReadOnlyList<string> CoordinateSpaceIds { get; init; } = [];
    public IReadOnlyList<SequenceAxis> SequenceAxes { get; init; } = [];
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = [];
    public IReadOnlyList<MonitorDimension> MonitorDimensions { get; init; } = [];
    public IReadOnlyList<string> FormatFamilyIds { get; init; } = [];
    /// <summary>Non-null if and only if <see cref="Roles"/> includes <c>VariantAxis</c>.</summary>
    public VariantSelection? Variant { get; init; }
}
```

### 2.6 Variant selection, monitoring, acquisition scope

```csharp
// src/Arronix.Abstractions/Shape/VariantSelection.cs
/// <remarks>
/// Lidarr and Readarr each hand-roll the at-most-one invariant in a repository
/// (<c>Ensure.That(all.Count(x =&gt; x.Monitored) == 1)</c>). Declaring it lets the host enforce it for any
/// kind. <c>OnImport</c> is the behavior <c>lidarr-music.md</c> §2.1 calls "the crux" — importing files
/// silently redefines which edition the library considers canonical. Declaring it makes the surprise
/// visible instead of emergent.
/// </remarks>
public sealed record VariantSelection
{
    /// <summary>Permission for the host to switch the selection automatically. <c>AnyReleaseOk</c>/<c>AnyEditionOk</c>.</summary>
    public bool AutoSwitchByDefault { get; init; } = true;
    public SelectionTrigger Triggers { get; init; } = SelectionTrigger.Manual;
    /// <summary>Completeness is counted against the SELECTED variant, not the union of all of them.</summary>
    public bool CompletenessIsVariantRelative { get; init; } = true;
}

[Flags]
public enum SelectionTrigger { Manual = 0, OnImport = 1, OnRefresh = 2 }

// src/Arronix.Abstractions/Shape/MonitorDimension.cs
/// <remarks>
/// Music needs THREE orthogonal monitoring axes (<c>lidarr-music.md</c> §3): want-this-artist's-future-output,
/// want-this-item, and which-pressing-defines-complete. The third is <see cref="VariantSelection"/>; the first
/// two are dimensions. Apply-once policies (All/Future/Missing/Latest/First/None) are NOT dimensions — they
/// are actions, declared as <c>ActionDescriptor</c>s in §6.
/// </remarks>
public sealed record MonitorDimension
{
    public required string DimensionId { get; init; }    // "wanted", "future-items"
    public required string Name { get; init; }
    public required MonitorDimensionKind Kind { get; init; }
    public IReadOnlyList<FacetValue> Choices { get; init; } = [];
    public string? DefaultChoice { get; init; }
}

public enum MonitorDimensionKind { Toggle = 0, Enumerated = 1 }

// src/Arronix.Abstractions/Shape/AcquisitionScope.cs
/// <remarks>
/// "Which level is the acquisition unit" is under-specified: Sonarr searches for an episode, a SEASON PACK
/// (a sequence span, not a level) and a full series. The core says "span"; the plugin says "season" /
/// "discography" — Prowlarr's proven <c>PackSeedTime</c> pattern.
/// </remarks>
public sealed record AcquisitionScope
{
    public required AcquisitionScopeKind Kind { get; init; }
    /// <summary>Set iff Kind == SequenceSpan. Names a <see cref="SequenceAxis.AxisId"/>.</summary>
    public string? SequenceAxisId { get; init; }
    /// <summary>Set iff Kind == Ancestor.</summary>
    public MediaLevelId? AncestorLevelId { get; init; }
}

public enum AcquisitionScopeKind { Single = 0, SequenceSpan = 1, Ancestor = 2 }
```

### 2.7 File binding — the join, with an anchor

```csharp
// src/Arronix.Abstractions/Shape/FileBinding.cs
/// <remarks>
/// The storage representation is ALWAYS the join <c>(unitId, fileId, ordinal?)</c>; the booleans are
/// uniqueness constraints the host enforces, never a schema switch. Picking any one *arr's FK direction as
/// the platform default would break the other two.
/// <para>
/// <see cref="AnchorLevelId"/> and <see cref="UnitLevelId"/> differ in exactly one surveyed kind and it is
/// not an accident: Lidarr's <c>TrackFile.AlbumId</c> anchors the file at the acquisition unit while
/// <c>Track.TrackFileId</c> satisfies the completeness unit, so file ownership survives a variant switch.
/// </para>
/// </remarks>
public sealed record FileBinding
{
    /// <summary>Where the file row hangs. Album (Lidarr) / Movie / Episode / Edition.</summary>
    public required MediaLevelId AnchorLevelId { get; init; }
    /// <summary>What the file satisfies. Track / Movie / Episode / Edition.</summary>
    public required MediaLevelId UnitLevelId { get; init; }
    /// <summary>R, S, L = true; K = false (a multi-part audiobook).</summary>
    public bool AtMostOneFilePerUnit { get; init; }
    /// <summary>R, K = true; S, L = false (a multi-episode file, a single-file album rip).</summary>
    public bool AtMostOneUnitPerFile { get; init; }
    /// <summary>K only — Readarr's <c>Part</c>/<c>PartCount</c>.</summary>
    public bool OrdinalIsMeaningful { get; init; }
    public IReadOnlyList<SpanConstraint> SpanConstraints { get; init; } = [];
}

/// <remarks>
/// Sonarr's <c>InvalidSeasonException</c> declared instead of thrown: a file may span episodes but never
/// seasons. The host can now enforce it for any kind without knowing what a season is.
/// </remarks>
public readonly record struct SpanConstraint(string SpaceId, string ComponentId, SpanRule Rule);

public enum SpanRule { MustNotSpan = 0, MaySpan = 1 }
```

### 2.8 Grouping axes, format families, selection facets, fields

```csharp
// src/Arronix.Abstractions/Shape/GroupingAxis.cs
public sealed record GroupingAxis
{
    public required string AxisId { get; init; }            // "collection", "book-series"
    public required string Name { get; init; }
    public required string PluralName { get; init; }
    public required MediaLevelId MemberLevelId { get; init; }
    public required GroupingArity Arity { get; init; }
    public required MemberPosition Position { get; init; }
    /// <summary><c>SeriesBookLink.IsPrimary</c> — single-valued naming tokens need a designated member.</summary>
    public bool HasPrimaryMember { get; init; }
    /// <summary><c>MovieCollection.Monitored</c> plus its own quality profile and root folder.</summary>
    public bool IsMonitorable { get; init; }
    /// <summary>A Goodreads series can itself be an import-list source.</summary>
    public bool IsDiscoverySource { get; init; }
    public required GroupLifetime Lifetime { get; init; }
    /// <summary>The group has its own metadata fetch path — Readarr's <c>IProvideSeriesInfo</c>.</summary>
    public bool HasOwnMetadata { get; init; }
}

public enum GroupingArity { ManyToMany = 0, ManyToOne = 1 }
public enum MemberPosition { None = 0, Ordinal = 1, Labeled = 2 }
public enum GroupLifetime
{
    /// <summary>Dies when its last member leaves. Readarr's Series, with orphan-link cleanup.</summary>
    RefCounted = 0,
    /// <summary>Exists independently with its own state. Radarr's MovieCollection.</summary>
    Independent = 1,
}

// src/Arronix.Abstractions/Shape/FormatFamily.cs
/// <remarks>
/// Readarr needed an ebook-vs-audiobook dimension, did not have one, and simulated it with quality-ID bands
/// (0–4 text, 10–13 audio) and TWO <c>Unknown</c> sentinels interleaved in one ordered ladder — so any
/// audiobook counts as an upgrade over an EPUB. One ladder per family makes that unrepresentable.
/// The extension set is the declared discriminator because it is empirically what every *arr branches on.
/// </remarks>
public sealed record FormatFamily
{
    public required string FamilyId { get; init; }                    // "video", "ebook", "audiobook"
    public required string Name { get; init; }
    public required IReadOnlyList<string> FileExtensions { get; init; }
    /// <summary>Reuses the STABLE 0.1.0 DTO, so CutoffPolicy and IQualityModel keep working unchanged.</summary>
    public required IReadOnlyList<QualityTier> Ladder { get; init; }
    public required QualityTier Unknown { get; init; }
    public bool CoexistsWithOtherFamilies { get; init; }
    public bool SupportsEmbeddedMetadata { get; init; }
}

// src/Arronix.Abstractions/Shape/SelectionFacet.cs
/// <remarks>
/// The THIRD profile axis, after quality ("how good") and custom formats ("which releases"):
/// "which sub-items of this library item exist at all". Lidarr's and Readarr's <c>MetadataProfile</c>.
/// </remarks>
public sealed record SelectionFacet
{
    public required string FacetId { get; init; }      // "primary-type", "min-pages", "skip-parts-and-sets"
    public required string Name { get; init; }
    public required MediaLevelId AppliesToLevelId { get; init; }
    public required SelectionFacetKind Kind { get; init; }
    /// <summary>Kind == Enumerated.</summary>
    public IReadOnlyList<FacetValue> Values { get; init; } = [];
    public bool MultiValued { get; init; }
    public IReadOnlyList<string> DefaultAllowed { get; init; } = [];
    /// <summary>Kind == Threshold. Readarr's MinPopularity / MinPages.</summary>
    public ThresholdDirection ThresholdDirection { get; init; }
    public double? DefaultNumber { get; init; }
    public string? Unit { get; init; }
    /// <summary>Kind == Flag. Readarr's SkipMissingDate / SkipMissingIsbn / SkipPartsAndSets.</summary>
    public bool DefaultFlag { get; init; }
    public FacetApplication Application { get; init; }
}

public enum SelectionFacetKind { Enumerated = 0, Threshold = 1, Flag = 2 }
public enum ThresholdDirection { AtLeast = 0, AtMost = 1 }

/// <remarks>
/// Materialization = excluded rows are never created and existing ones are removed on refresh (Lidarr and
/// Readarr's destructive default, and a known source of user surprise). Visibility = rows exist but are
/// hidden. Making the choice explicit is the direct answer to that surprise.
/// </remarks>
public enum FacetApplication { Materialization = 0, Visibility = 1 }

public readonly record struct FacetValue(string Value, string Name);
```

```csharp
// src/Arronix.Abstractions/Shape/FieldDescriptor.cs
public sealed record FieldDescriptor
{
    public required string FieldId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required FieldValueKind ValueKind { get; init; }
    public FieldSemantics Semantics { get; init; } = FieldSemantics.None;
    public Prominence Prominence { get; init; } = Prominence.Detail;
    public bool Multivalued { get; init; }
    public bool Editable { get; init; }
    public string? Unit { get; init; }                              // "minutes", "pages"
    /// <summary>Populated when ValueKind == Enumerated.</summary>
    public IReadOnlyList<FacetValue> Choices { get; init; } = [];
}

/// <summary>Closed. The client renders every kind or the build fails.</summary>
public enum FieldValueKind
{
    Text = 0, MultilineText = 1, Integer = 2, Decimal = 3, Boolean = 4,
    Date = 5, Instant = 6, Duration = 7, ByteSize = 8, Ratio = 9,
    Ordinal = 10, Enumerated = 11, Reference = 12, ExternalIdentifier = 13,
    Link = 14, FilePath = 15, Language = 16, Quality = 17, Artwork = 18, Count = 19,
}

[Flags]
public enum FieldSemantics
{
    None = 0, Identity = 1, Title = 2, SortKey = 4, Sortable = 8, Filterable = 16,
    Groupable = 32, Searchable = 64, Progress = 128, Status = 256, Timestamp = 512,
    Size = 1024, Artwork = 2048, Disambiguation = 4096,
}

/// <remarks>
/// An importance rank, not a layout instruction. A card face reads <c>Primary</c>; a table's default column
/// set reads <c>Primary|Secondary</c>; a CLI's <c>--verbose</c> reads <c>Detail</c>; a support bundle reads
/// <c>Diagnostic</c>. Rejected in its place: <c>ColumnWidth</c>, <c>Order</c>, <c>Inline</c>.
/// </remarks>
public enum Prominence { Primary = 0, Secondary = 1, Detail = 2, Diagnostic = 3 }

// src/Arronix.Abstractions/Shape/FieldValue.cs
/// <remarks>
/// One tagged record rather than eighteen polymorphic subtypes. Exactly one payload slot is populated per
/// <see cref="Kind"/>; <c>Items</c> carries a multivalued field; <c>Kind == Absent</c> means "no value".
/// Chosen over a string bag because the client must format dates, sizes and durations correctly, and over a
/// polymorphic union because this value crosses two independently-versioned assembly boundaries.
/// </remarks>
public sealed record FieldValue
{
    public required FieldValueKind Kind { get; init; }
    public bool IsAbsent { get; init; }
    public string? Text { get; init; }
    public long? Number { get; init; }
    public double? Real { get; init; }
    public bool? Flag { get; init; }
    public DateTimeOffset? Instant { get; init; }
    public DateOnly? Date { get; init; }
    public TimeSpan? Duration { get; init; }
    public OrdinalPath? Ordinals { get; init; }
    public MediaItemRef? Reference { get; init; }
    public ExternalId? External { get; init; }
    public Uri? Link { get; init; }
    public QualityTier? Quality { get; init; }          // STABLE DTO
    public Language? Language { get; init; }            // STABLE DTO
    public IReadOnlyList<FieldValue>? Items { get; init; }

    public static FieldValue Absent(FieldValueKind kind);
    public static FieldValue OfText(string value);
    /* … one factory per kind … */
}
```

### 2.9 Search declaration — Prowlarr's problem, solved by set intersection

```csharp
// src/Arronix.Abstractions/Shape/SearchTerm.cs
/// <remarks>
/// The INTERSECTION of Prowlarr's five parallel search-param enums (~40 members), per
/// <c>prowlarr-indexers.md</c> §3.1: "read the intersection, not the union". <c>ExternalIdentifier</c>
/// collapses seven wire parameters (imdbid, tmdbid, tvdbid, tvmazeid, traktid, rid, doubanid) because they
/// are all namespaced external ids; <c>Ordinal</c> collapses season/ep/track because they are all
/// intra-work coordinates. Nine members, closed, and not one media noun.
/// </remarks>
public enum SearchTerm
{
    FreeText = 0,            // q — universal
    WorkTitle = 1,           // title / BookTitle / Album
    Creator = 2,             // Artist / Author
    Issuer = 3,              // Label / Publisher
    Genre = 4,
    Year = 5,
    ExternalIdentifier = 6,
    Ordinal = 7,
    Date = 8,
}

/// <summary>A newznab category id, carried verbatim. The <c>&gt;= 100000</c> band is provider-specific.</summary>
public readonly record struct CategoryId(int Value)
{
    public bool IsProviderSpecific => Value >= 100_000;
    public static CategoryId FromInt(int value);
    public override string ToString();
}

// src/Arronix.Abstractions/Shape/SearchKind.cs
/// <remarks>
/// A media kind's declaration of one way it can be searched for. It names NO indexer concept — no
/// "tvsearch", no wire parameter name. Eligibility against an indexer is pure set intersection (§5.4).
/// </remarks>
public sealed record SearchKind
{
    public required string SearchKindId { get; init; }        // "unit", "sequence-span", "ancestor"
    public required string Name { get; init; }
    public required MediaLevelId TargetLevelId { get; init; }
    public required AcquisitionScope Scope { get; init; }
    /// <summary>An indexer must support every one of these to be eligible.</summary>
    public IReadOnlyList<SearchTerm> RequiredTerms { get; init; } = [];
    public IReadOnlyList<SearchTerm> OptionalTerms { get; init; } = [];
    /// <summary>Host-side category gate, applied BEFORE dispatch (ReleaseSearchService.cs:186).</summary>
    public required IReadOnlyList<CategoryId> Categories { get; init; }
}
```

### 2.10 The shape

```csharp
// src/Arronix.Abstractions/Shape/MediaShape.cs
/// <remarks>
/// Pure data: no delegates, no <c>Type</c>, no interfaces. That is what makes it simultaneously (a) the
/// plugin's declaration, (b) the API wire contract, and (c) the client's rendering input — one declaration,
/// three consumers, zero duplication. It is the single most valuable structural property of this design.
/// </remarks>
public sealed record MediaShape
{
    public required MediaKindId Kind { get; init; }
    public required string Name { get; init; }
    public required string PluralName { get; init; }
    /// <summary>Root first. Validated acyclic and, in 0.3.0, linear (each level has at most one child).</summary>
    public required IReadOnlyList<MediaLevel> Levels { get; init; }
    public required FileBinding FileBinding { get; init; }
    public IReadOnlyList<CoordinateSpace> CoordinateSpaces { get; init; } = [];
    public IReadOnlyList<GroupingAxis> GroupingAxes { get; init; } = [];
    public required IReadOnlyList<FormatFamily> FormatFamilies { get; init; }
    public IReadOnlyList<SelectionFacet> SelectionFacets { get; init; } = [];
    public IReadOnlyList<SearchKind> SearchKinds { get; init; } = [];
    /// <summary>Reuses the 0.1.0 DTO, which nothing currently returns and which is widened in place
    /// (#44) rather than wrapped. §9's source of truth.</summary>
    public required IReadOnlyList<NamingToken> Tokens { get; init; }
}
```

### 2.11 The four behavior seams

Exactly four interfaces cross the plugin boundary for media semantics. `radarr-movies.md` §6.1 isolates
where media semantics enter the generic pipeline — *"exactly four places"*: query construction, title
parsing, matching, and the specification set. Title parsing is `IReleaseTitleParser` (amended — see below). The
specification set is host-owned (§4.7). That leaves three, plus the catalog projection (#10).

> **Amended.** `IReleaseParser` cannot produce a `CoordinateSet`, so with it as the only title seam
> `MatchRequest.Coordinates` below is always `Empty`. `parsing-and-test-corpus.md` §3.5 replaces it with
> `IReleaseTitleParser` producing `ReleaseReading`, and deletes `IReleaseParser` and `ParsedRelease` in the
> same work package that lands the four plugin implementations. There is no bridge and the two never
> coexist beyond that milestone.

```csharp
// src/Arronix.Abstractions/Shape/IMediaShapeProvider.cs
public interface IMediaShapeProvider
{
    MediaShape Shape { get; }
}

// src/Arronix.Abstractions/Shape/IReleaseMatcher.cs
/// <remarks>
/// The single most media-specific step in the product — Radarr's <c>IParsingService.Map</c>, Sonarr's
/// <c>ParsingService.GetEpisodes</c> switchboard, Lidarr's Munkres assignment. The host never attempts it.
/// One method serves both the grab path and the import path; they differ only in <see cref="MatchSource"/>,
/// which is Sonarr's <c>sceneSource</c> generalized.
/// </remarks>
public interface IReleaseMatcher
{
    MediaKindId MediaKind { get; }
    Task<MatchOutcome> MatchAsync(MatchRequest request, CancellationToken cancellationToken = default);
}

public sealed record MatchRequest
{
    public required MediaKindId MediaKind { get; init; }
    /// <summary>The raw string: a release title, a file name or a folder name.</summary>
    public required string Text { get; init; }
    /// <summary>The stable parser's output, when one is registered. Null otherwise.</summary>
    public ParsedRelease? Parsed { get; init; }
    /// <summary>Coordinates already read from the text or from file tags.</summary>
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;
    /// <summary>The library entry the caller believes this belongs to, when known.</summary>
    public MediaItemRef? Scope { get; init; }
    public required MatchSource Source { get; init; }
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];
}

/// <remarks>Provenance. <c>ReleaseName</c> is Sonarr's <c>sceneSource == true</c>.</remarks>
public enum MatchSource { ReleaseName = 0, FileName = 1, FolderName = 2, DownloadClientTitle = 3, Tags = 4 }

public sealed record MatchOutcome
{
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    /// <summary>Reuses the STABLE 0.1.0 enum verbatim.</summary>
    public required MatchConfidence Confidence { get; init; }
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;
    public string? RejectionReason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
```

```csharp
// src/Arronix.Abstractions/Shape/IReleaseQueryPlanner.cs
/// <remarks>
/// Replaces the 7 / 1 / 2 / 2 / 5 <c>IIndexer.Fetch</c> overload set. Overload resolution is a compile-time
/// mechanism and cannot survive a plugin boundary. Titles, aliases, translations, VA rewriting and subtitle
/// stripping are all this method's business and none of them are the host's.
/// </remarks>
public interface IReleaseQueryPlanner
{
    MediaKindId MediaKind { get; }
    Task<ReleaseQueryPlan> PlanAsync(AcquisitionRequest request, CancellationToken cancellationToken = default);
}

public sealed record AcquisitionRequest
{
    public required MediaKindId MediaKind { get; init; }
    public required string SearchKindId { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required SearchOrigin Origin { get; init; }
}

public enum SearchOrigin { Rss = 0, Automatic = 1, UserInvoked = 2, Interactive = 3, ReleasePush = 4 }

/// <summary>
/// Ordered tiers; the first tier that yields any release wins. Generalizes Prowlarr's
/// <c>IndexerPageableRequestChain</c> ("try id-search, else fall back to text search").
/// </summary>
public sealed record ReleaseQueryPlan(IReadOnlyList<ReleaseQueryTier> Tiers);
public sealed record ReleaseQueryTier(IReadOnlyList<ReleaseQuery> Queries);

public sealed record ReleaseQuery
{
    public required MediaKindId MediaKind { get; init; }
    public required string SearchKindId { get; init; }
    public required string FreeText { get; init; }
    /// <summary>Scene titles, translations, VA rewrites — supplied by the plugin, never by the host.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<SearchArgument> Arguments { get; init; } = [];
    public IReadOnlyList<CategoryId> Categories { get; init; } = [];
    public int Limit { get; init; } = 100;
    public int Offset { get; init; }
    public required SearchOrigin Origin { get; init; }
}

public readonly record struct SearchArgument(SearchTerm Term, FieldValue Value, string? ComponentId = null);
```

```csharp
// src/Arronix.Abstractions/Shape/IMediaItemSource.cs
/// <remarks>
/// <para><b>Why this exists.</b> With no persistence and no metadata pipeline in this milestone, a design in
/// which plugins only declare a shape ships a UI that can render nothing but empty lists. This is the
/// catalog/library split (§2.5) made into the runtime topology: <b>the plugin projects its own catalog;
/// the host stores only library state</b> (monitoring, unit↔file links, selected variant, group membership)
/// and merges the two on read.</para>
/// <para>It qualifies for promotion on the strictest reading of the three-tier rule: four demonstrated
/// implementers ship in this same milestone.</para>
/// </remarks>
public interface IMediaItemSource
{
    MediaKindId MediaKind { get; }
    Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default);
    Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default);
    Task<MediaItemRef?> ResolveExternalAsync(ExternalId externalId, CancellationToken cancellationToken = default);
}

public sealed record ItemQuery
{
    public required MediaKindId Kind { get; init; }
    public required MediaLevelId Level { get; init; }
    public MediaItemRef? Parent { get; init; }
    public string? TextSearch { get; init; }
    /// <summary>fieldId → allowed values, as canonical strings. Interpreted by the source.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();
    public string? SortFieldId { get; init; }
    public bool SortDescending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record ItemView
{
    public required MediaItemRef Ref { get; init; }
    public MediaItemRef? Parent { get; init; }
    public required string Title { get; init; }
    public string? SortTitle { get; init; }
    public required IReadOnlyDictionary<string, FieldValue> Fields { get; init; }
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];
    /// <summary>Ordering key for levels whose canonical coordinate space is <c>Label</c>.</summary>
    public long SortIndex { get; init; }
    public bool HasChildren { get; init; }
    public string? FormatFamilyId { get; init; }
}

public sealed record ItemPage(IReadOnlyList<ItemView> Items, int Page, int PageSize, int TotalCount);
```

**Note what is deliberately absent from `ItemView`:** monitoring state, progress, the selected variant, file
counts and affordances. All five are **host** state or host derivations, merged onto the wire type
(`ItemDetail`, §6.4) at projection time. A plugin cannot report a monitoring state it does not own.

### 2.12 Acceptance test — the four surveyed kinds, cell by cell

This table is the shape model's acceptance test. Every cell is derived from a survey citation. **No cell
needs an escape hatch outside the declared vocabulary.**

| | **TV** | **Movies** | **Music** | **Books** |
|---|---|---|---|---|
| Levels | `series` → `episode` | `movie` | `artist` → `album` → `release` → `track` | `author` → `book` → `edition` |
| Roles | `series`: LibraryEntry<br>`episode`: AcquisitionUnit \| CompletenessUnit \| FileBearing | `movie`: LibraryEntry \| AcquisitionUnit \| CompletenessUnit \| FileBearing | `artist`: LibraryEntry<br>`album`: AcquisitionUnit \| FileBearing<br>`release`: VariantAxis<br>`track`: CompletenessUnit \| FileBearing | `author`: LibraryEntry<br>`book`: AcquisitionUnit<br>`edition`: VariantAxis \| CompletenessUnit \| FileBearing |
| `LevelIdentity.HasCatalogRecord` | `series` false *(Sonarr's gap — Arronix fixes it: true)* | `movie` true | `artist` true, `album` true | `author` true, `book` true |
| Canonical coordinate space | `aired` = Ordinal[season, episode], dense | `only` = Singleton | `medium-track` = Ordinal[medium, track], dense | `edition` = Label on `edition`; Singleton on `book` |
| Alias spaces | `absolute` = Ordinal[n]; `airdate` = Date; `scene` = Ordinal[season, episode] (provenance-sensitive, may-be-unverified); `scene-absolute` = Ordinal[n] | — | `absolute` = Ordinal[n]; `vinyl` = Label ("A1") | — |
| Sequence axes | `season` on `episode` (space `aired`, index 0), `HasPolicyRecord = true`, exception `(0, "Specials", ExcludedFromCompleteness)` | — | `medium` on `track` (space `medium-track`, index 0), `HasPolicyRecord = false` | — |
| `FileBinding` | Anchor = Unit = `episode`; `AtMostOneFilePerUnit = true`, `AtMostOneUnitPerFile = false`; span constraint `(aired, season, MustNotSpan)` | Anchor = Unit = `movie`; both true | **Anchor = `album`**, Unit = `track`; `AtMostOneFilePerUnit = true`, `AtMostOneUnitPerFile = false` | Anchor = Unit = `edition`; `AtMostOneFilePerUnit = false`, `AtMostOneUnitPerFile = true`, `OrdinalIsMeaningful = true` |
| Acquisition scopes | Single, SequenceSpan(`season`), Ancestor(`series`) | Single | Single, Ancestor(`artist`) | Single, Ancestor(`author`) |
| Variant selection | — | — | on `release`: AutoSwitchByDefault = true, Triggers = `OnImport \| OnRefresh`, CompletenessIsVariantRelative = true | on `edition`: AutoSwitchByDefault = true, Triggers = `Manual`, CompletenessIsVariantRelative = true |
| Monitor dimensions | `series`: `future-items` (Enumerated: All/None/New); `episode`: `wanted` (Toggle) | `movie`: `wanted` (Toggle) | `artist`: `future-items`; `album`: `wanted` | `author`: `future-items`; `book`: `wanted` |
| Grouping axes | — | `collection`: ManyToOne, Independent, Monitorable, Position = None | — | `book-series`: ManyToMany, RefCounted, Position = Labeled, HasPrimaryMember, DiscoverySource, HasOwnMetadata |
| Format families | `video` (one ladder) | `video` | `audio` | **`ebook` + `audiobook`** — two ladders, two cutoffs |
| Selection facets | `season-kind` Enumerated{regular, specials}, Visibility | — | `primary-type`, `secondary-type`, `release-status` — all Enumerated, MultiValued, Materialization | `language` Enumerated; `min-popularity`, `min-pages` Threshold(AtLeast); `skip-missing-date`, `skip-missing-isbn`, `skip-parts-and-sets`, `skip-series-secondary` Flag — all Materialization |
| Search kinds | `unit` (Ordinal + ExternalIdentifier, cats 5000s), `season-pack` (Ordinal, cats 5000s), `daily` (Date), `series` (WorkTitle) | `movie` (WorkTitle + Year + ExternalIdentifier, cats 2000s) | `album` (WorkTitle + Creator, cats 3000s), `discography` (Creator) | `book` (WorkTitle + Creator + ExternalIdentifier, cats 7000s), `author` (Creator) |

Two cells are worth reading twice. **Lidarr's `TrackFile.AlbumId`** — a file anchored two levels above the
unit it satisfies — falls out naturally from separating `AnchorLevelId` from `UnitLevelId`; the original
hypothesis, having a single "file-bearing unit", could not express it. And **TV's three numbering schemes
with one file satisfying N units** are a coordinate *bag* of four spaces plus two booleans and a span
constraint — not three levels, not three plugins, and not a schema field.

### 2.13 Where the model is honestly thin

Recorded rather than glossed:

1. **Radarr's `MovieMetadata`/`Movie` normalization is expressed as `LevelIdentity` flags, not as two
   levels.** That is right (both rows are "the movie"), but it means the *store* must implement the split;
   the shape only declares that it exists. `InMemoryMediaStore` does implement it (§4.6).
2. **`CoordinateSpace.IsDense` drives "what is missing" for ordinal spaces only.** A `Label` space cannot
   report gaps. No surveyed kind needs it to.
3. **Cross-level acquisition** (a release satisfying units at two different levels simultaneously) is
   representable in `MatchOutcome.Units` but nothing validates it against `FileBinding`. Deferred (§8).

---

## 3. The plugin model

Namespace `Arronix.Abstractions.Plugins`, diagnostic **`ARX0014`**, files under
`src/Arronix.Abstractions/Plugins/`.

### 3.1 `IPluginModule` and `PluginId`

```csharp
// src/Arronix.Abstractions/Plugins/PluginId.cs
public readonly record struct PluginId
{
    public string Value { get; }
    /// <summary>Pattern <c>^[a-z][a-z0-9]*(\.[a-z0-9]+)*$</c> — lowercase, dot-separated, reverse-DNS friendly.</summary>
    public static bool TryParse(string value, out PluginId id);
    public static PluginId FromString(string value);
    public override string ToString();
}

// src/Arronix.Abstractions/Plugins/IPluginModule.cs
public interface IPluginModule
{
    /// <summary>Must equal the manifest id. Cross-checked at load.</summary>
    PluginId Id { get; }

    /// <summary>
    /// Called exactly once, after validation and before activation. Registration only: no I/O, no network,
    /// no long work. A throwing <c>Configure</c> quarantines the plugin; it never fails the host.
    /// </summary>
    void Configure(IPluginContext context);
}
```

A plugin assembly must expose **exactly one** public, parameterless-constructible `IPluginModule`. Zero or
more than one is `CoreErrorCode.PluginLoadFailure` — ambiguity is a defect, not a feature.

### 3.2 Capabilities

```csharp
// src/Arronix.Abstractions/Plugins/Capability.cs
public enum Capability
{
    // Media — ARCHITECTURE §4.1 + extraction plan §4
    Indexing = 0, Metadata = 1, Parsing = 2, Matching = 3, Quality = 4,
    Renaming = 5, Import = 6, Download = 7, Notification = 8,
    // Added on survey evidence
    /// <summary>Contributing a media shape, an item source and the media seams. Without it there is no
    /// forward check that a plugin calling itself a media plugin registered anything.</summary>
    MediaKind = 9,
    /// <summary>A fifth provider family present in all four *arrs (90/126/67/46 files) and absent from the
    /// plan's vocabulary entirely.</summary>
    ImportList = 10,
    // Infrastructure
    Network = 11, Storage = 12, TelemetrySink = 13,
}

public static class CapabilityNames
{
    public const string Indexing = "indexing";
    public const string Metadata = "metadata";
    public const string Parsing = "parsing";
    public const string Matching = "matching";
    public const string Quality = "quality";
    public const string Renaming = "renaming";
    public const string Import = "import";
    public const string Download = "download";
    public const string Notification = "notification";
    public const string MediaKind = "media-kind";
    public const string ImportList = "import-list";
    public const string Network = "network";
    public const string Storage = "storage";
    public const string TelemetrySink = "telemetry-sink";

    public static bool TryParse(string name, out Capability capability);
    public static string ToWireName(Capability capability);
}

/// <summary>A set over a <c>ushort</c> bitmask. Value-equal, allocation-free.</summary>
public readonly record struct CapabilitySet
{
    public static CapabilitySet None { get; }
    public static CapabilitySet Of(params Capability[] capabilities);
    public bool Has(Capability capability);
    public bool IsSupersetOf(CapabilitySet other);
    public CapabilitySet Union(CapabilitySet other);
    /// <summary>
    /// ARCHITECTURE §11.1: <c>network</c> is implied by any of indexing | metadata | download |
    /// notification, extended here to import-list (an import list is an outbound HTTP consumer by
    /// construction). Nothing else is implied — an importer that needs to read the filesystem declares
    /// <c>storage</c> explicitly, because silently widening least privilege for ergonomics is the wrong trade.
    /// </summary>
    public CapabilitySet WithImplied();
    public IEnumerable<Capability> Enumerate();
    public override string ToString();
}
```

**Reconciliation with the stable `IMediaKind`.** Plugins never implement `IMediaKind`. The **host** does,
projecting from the shape and the manifest: `Id` ← `MediaShape.Kind`, `Name` ← `MediaShape.Name`,
`Version` ← manifest version, `Capabilities` ←
`CapabilitySet.WithImplied().Enumerate().Select(CapabilityNames.ToWireName)`, `SupportedIdentifiers` ←
the union of every level's `LevelIdentity.ExternalIds`, `NamingTokens` ← `MediaShape.Tokens.Select(t => t.Name)`.
The stable stringly-typed list becomes a **projection** rather than a source of truth. Extend, do not fork.

### 3.3 `IPluginContext` — the whole of what a plugin can reach

```csharp
// src/Arronix.Abstractions/Plugins/IPluginContext.cs
/// <remarks>
/// Deliberately a locator, which is normally an anti-pattern. Here it is the enforcement point: "what can a
/// plugin reach?" is answerable by reading one interface, and <c>TryGet*</c> makes every gated dependency
/// visible at the call site rather than hidden in a constructor signature.
/// </remarks>
public interface IPluginContext
{
    PluginId PluginId { get; }
    /// <summary>The plugin's own version, verbatim from its manifest.</summary>
    string PluginVersion { get; }
    /// <summary>The host's <c>Arronix.Abstractions</c> version, verbatim.</summary>
    string HostContractVersion { get; }
    CapabilitySet Capabilities { get; }

    IPluginRegistry Registry { get; }

    // Ungated and universal — extraction plan §4's mapping table
    IPluginPaths Paths { get; }                 // ARX0007, pre-scoped to this plugin's tree
    ICacheProvider Cache { get; }               // ARX0001, partitions namespaced by PluginId
    IJsonSerializer Json { get; }               // ARX0010
    ITelemetryEmitter Telemetry { get; }        // ARX0011 — also the plugin's diagnostic surface, §3.7
    IEventPublisher Events { get; }             // ARX0004, publication namespaced by PluginId
    IHostRuntimeInfo Runtime { get; }           // ARX0007
    IOperatingSystemInfo OperatingSystem { get; }
    TimeProvider Clock { get; }                 // BCL — no package cost

    // Capability-gated. TryGet returns false when the capability was not granted; Require throws
    // ArronixException(CoreErrorCode.PluginCapabilityMissing) naming the capability and the contract.
    bool TryGetHttp(out IHttpGateway? gateway);                       // network
    bool TryGetRateLimiter(out IRateLimiter? limiter);                // network
    bool TryGetCertificatePolicy(out ICertificateValidationPolicy? policy);  // network
    bool TryGetFileSystem(out IFileSystem? fileSystem);               // storage
    bool TryGetFileTransfer(out IFileTransferService? transfer);      // import

    IHttpGateway RequireHttp();
    IFileSystem RequireFileSystem();
    IFileTransferService RequireFileTransfer();
}
```

Every gated instance handed out is a **scoping decorator** built in `Arronix.Plugins` (§4.2):
`ScopedHttpGateway` stamps the plugin id into the rate-limit partition and the User-Agent;
`ScopedFileSystem` rejects any path outside the plugin's granted roots. Both are documented as guaranteed
behavior by the existing 0.2.0 contracts and **neither exists today** — this milestone builds them.

### 3.4 `IPluginRegistry` — the closed registration surface

```csharp
// src/Arronix.Abstractions/Plugins/IPluginRegistry.cs
/// <remarks>
/// The ONLY way a plugin contributes anything. There is deliberately no <c>Add&lt;T&gt;(object)</c>, no
/// <c>IServiceCollection</c> and no assembly scan. Registering something you did not declare is not a thing
/// you can express — enforcement moves from filtering to admission.
/// </remarks>
public interface IPluginRegistry
{
    // --- media kind ------------------------------------------------- required capability
    IPluginRegistry AddMediaShape(IMediaShapeProvider provider);              // MediaKind
    IPluginRegistry AddMediaItemSource(IMediaItemSource source);              // MediaKind
    IPluginRegistry AddReleaseMatcher(IReleaseMatcher matcher);               // Matching
    IPluginRegistry AddReleaseQueryPlanner(IReleaseQueryPlanner planner);     // Indexing
    IPluginRegistry AddReleaseParser(IReleaseParser parser);                  // Parsing
    IPluginRegistry AddQualityModel(IQualityModel model);                     // Quality
    IPluginRegistry AddImportPipeline(IImportPipeline pipeline);              // Import
    IPluginRegistry AddRenamePolicy(IRenamePolicy policy);                    // Renaming
    IPluginRegistry AddLibraryLayout(ILibraryLayout layout);                  // Renaming
    IPluginRegistry AddMediaIdResolver(IMediaIdResolver resolver);            // Metadata

    // --- providers, one method per family ---------------------------
    IPluginRegistry AddIndexer(IndexerRegistration registration);             // Indexing
    IPluginRegistry AddDownloadClient(DownloadClientRegistration r);          // Download
    IPluginRegistry AddNotifier(NotifierRegistration r);                      // Notification
    IPluginRegistry AddMetadataSource(MetadataSourceRegistration r);          // Metadata
    IPluginRegistry AddImportListSource(ImportListRegistration r);            // ImportList

    // --- platform participation -------------------------------------
    IPluginRegistry AddScheduledJob(IScheduledJob job, string schedule);      // (ungated)
    IPluginRegistry AddHealthContributor(IHealthContributor contributor);     // (ungated)
    IPluginRegistry AddTelemetryEnricher(ITelemetryEnricher enricher);        // (ungated)
    IPluginRegistry AddTelemetryEventFilter(ITelemetryEventFilter filter);    // (ungated)
    IPluginRegistry AddTelemetrySink(ITelemetrySink sink);                    // TelemetrySink
    IPluginRegistry AddRedactionRules(IRedactionRuleProvider provider);       // (implied by the secret's owner)
    IPluginRegistry AddDiacriticFolding(IDiacriticFoldingProvider provider);  // Parsing | Renaming
    IPluginRegistry AddOutboundHttpInterceptor(IOutboundHttpInterceptor i);   // Indexing
    IPluginRegistry AddEventHandler<TEvent>(IEventHandler<TEvent> handler)    // (ungated, subscription-filtered)
        where TEvent : IDomainEvent;

    // --- declared UI intent (data only; never code) -----------------
    IPluginRegistry AddIntentSurface(PluginIntentSurface surface);            // (ungated)
}
```

**Instances are passed already-constructed.** The plugin builds its own objects from `IPluginContext`; the
host never activates a plugin type through a container. This is the direct fix for the surveyed race in
`ProviderFactory.GetInstance`, which mutates a container-resolved singleton — *"otherwise capability gating
per §11 is racy"*.

**Cost, stated plainly.** Adding a seam requires a new `IPluginRegistry` method — a host change, additive
under the stability policy. Plugins lose host constructor injection. A plugin that wants DI ergonomics may
reference `Microsoft.Extensions.DependencyInjection` **privately** and build its own container seeded from
`IPluginContext`; that is legal (it is not a forbidden Arronix assembly) — though the four reference plugins
in this milestone take **zero** package references and do not need it.

> **Deviation from ARCHITECTURE §4, declared.** §4 says a plugin provides *"assemblies implementing
> `IPluginModule` … **and service registrations**"*. Plugins do not register services into the host
> container. §11's stated intent ("least privilege via DI registration filtering") is satisfied by a
> strictly stronger mechanism, and by one that is *possible*: `Arronix.Abstractions` may take no packages,
> so `IServiceCollection` is unreachable from a contract by construction.

### 3.5 The manifest (host-side)

`plugin.json`, shipped as `<Content CopyToOutputDirectory="PreserveNewest">` beside the plugin assembly.
Parsed into `Arronix.Plugins.Manifest.PluginManifest` — **held host-side**: nothing constructs a manifest in
C#, and everything a plugin legitimately reads is already on `IPluginContext`.

```jsonc
{
  "schemaVersion": 0,
  "id": "tv",
  "name": "TV",
  "version": "0.1.0",
  "contracts": { "arronix": ">=0.3 <0.4" },
  "entryAssembly": "Arronix.Plugin.Tv.dll",
  "mediaKinds": ["tv"],
  "identifiers": ["tvdb", "tmdb", "imdb"],
  "capabilities": ["media-kind", "parsing", "matching", "indexing", "quality", "renaming", "import"],
  "tokens": [
    { "name": "{Series Title}", "description": "The series title", "exampleValue": "The Expanse", "isRequired": true }
  ],
  "policies": {
    "parsing":  ["SceneNumbering", "MultiEpisode", "YearDisambiguation"],
    "matching": ["ExactTitleMatch", "SequenceWindow", "AirdateGuard"],
    "quality":  ["SourceTier", "CodecBitDepth", "Cutoff"],
    "import":   ["LibraryLayout", "UpgradeRules"],
    "naming":   ["StandardPattern", "SequencePattern"]
  }
}
```

Validation is `JsonSerializerOptions { UnmappedMemberHandling = Disallow, RespectRequiredConstructorParameters = true }`
over `required` members plus explicit semantic checks — that **is** schema validation, it cannot drift from
the types, and it needs no package that is not registered. A generated `plugin.schema.json` ships as an
editor aid only, never as the runtime gate.

> **Deviation from ARCHITECTURE §4.1, declared.** The example writes `"tokens": ["{SeriesTitle}"]` — bare
> strings. Here `tokens` is an array of the **stable, currently-unreturned** `NamingToken` DTO, because §9
> requires the host to validate templates and present token help, which a bare string cannot carry. The
> reader accepts a bare string and widens it to `NamingToken { Name = s }`, so the documented example still
> parses. Additive superset, no break.
>
> **Second deviation.** §4.2 names five policy categories; the §4.1 example lists four. `PolicyGraph`
> carries all five. **Recommend amending the §4.1 example** to include `naming`.

### 3.6 Contract ranges and the experimental gate

`SemanticVersion`, `VersionRange` and `VersionRangeParser` live in `src/Arronix.Plugins/Versioning/` —
**not** in Abstractions. Only the loader parses a range.

**Grammar — a documented subset of npm/yarn, deliberately small:**

| Form | Example | Supported |
|---|---|---|
| comparators `= > >= < <=` | `>=0.3` | yes |
| whitespace conjunction | `>=0.3 <0.4` | yes |
| `\|\|` disjunction | `>=0.3 <0.4 \|\| >=0.5 <0.6` | yes |
| partial versions | `0.3` → `0.3.0` | yes |
| caret `^`, tilde `~` | `^0.3.0` | **rejected** |
| wildcards `x`, `*` | `0.3.x` | **rejected** |
| hyphen ranges | `0.3.0 - 0.4.0` | **rejected** |
| prerelease | `0.3.0-beta.1` | parsed, SemVer 2.0 precedence, no prerelease ranges |

`^` and `~` are rejected because their 0.x semantics have been ambiguous for a decade and every governing
document writes `>=A <B`. Wildcards are rejected because four ways to write "the 0.3 line" is three too
many. An unparseable range is a **load failure** (`PluginManifestInvalid`), never a lenient reinterpretation.

**The experimental gate**, mandated by `stability.md`:

```
permitted = range.UpperBoundExclusive is { } upper
            && upper <= new SemanticVersion(host.Major, host.Minor + 1, 0);
```

Failing it is a **load rejection** with `CoreErrorCode.PluginContractMismatch` and a message naming the
range to declare. **Consequence accepted knowingly:** everything added in this milestone is experimental, so
every real plugin must pin to the current MINOR. The four first-party plugins ship
`"contracts": { "arronix": ">=0.3 <0.4" }`. This is exactly the revocability the stability policy was
written to buy, and it is why contracts may be promoted before their shape has settled.

### 3.7 Plugin diagnostics — no new contract

There is deliberately **no `IPluginLogger`**. `Arronix.Abstractions` cannot reference
`Microsoft.Extensions.Logging.Abstractions`, and inventing a parallel logging vocabulary would be worse than
the problem. `ITelemetryEmitter` (0.2.0, `ARX0011`) is the plugin's diagnostic surface, with ergonomic
extension methods added in this milestone (`Arronix.Abstractions/Telemetry/TelemetryEmitterExtensions.cs`:
`Debug`, `Info`, `Warn`, `Error` — extension methods need no package reference). The host's emitter
implementation fans out to `ILogger` **and** to registered sinks: one pipeline, one correlation id, zero new
contracts, and no plugin takes a hard logging-framework reference.

---

## 4. The host runtime

Two projects: `src/Arronix.Plugins/` (loader and isolation) and `src/Arronix.Host/` (registries, scheduler,
health, storage seam). Both are host-side implementation assemblies; **no plugin may reference either**, and
that is enforced twice — statically over the assembly-reference table before load, and dynamically by the
`AssemblyLoadContext`.

### 4.1 `src/Arronix.Plugins/` — loading and isolation

```text
src/Arronix.Plugins/
├── Arronix.Plugins.csproj
├── GlobalUsings.cs
├── Composition/ArronixPluginsServiceCollectionExtensions.cs   // AddArronixPlugins(IConfiguration)
├── Configuration/PluginRuntimeOptions.cs                      // section "Arronix:Plugins"
├── Manifest/{PluginManifest,ContractRequirements,PolicyGraph,PluginManifestReader,PluginManifestValidator}.cs
├── Manifest/plugin.schema.json                                // editor aid only, never the runtime gate
├── Versioning/{SemanticVersion,VersionRange,VersionRangeParser}.cs
├── Loading/{PluginLoadContext,PluginReferenceInspector,PluginLoader,PluginCandidate,PluginLoadResult,PluginState}.cs
├── Registration/{PluginRegistry,PluginRegistrationLedger,CapabilityMatrix,PluginContext}.cs
├── Scoping/{ScopedFileSystem,ScopedHttpGateway,PartitionedCacheProvider,PartitionedRateLimiter,
│            FilteredEventPublisher,PluginPaths,PluginTelemetryEmitter}.cs
└── Registry/{IPluginRuntimeRegistry,PluginRuntimeRegistry,TokenRegistry}.cs
```

#### The `AssemblyLoadContext`

```csharp
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly string[] BlockedPrefixes =
        ["Arronix.Common", "Arronix.Plugins", "Arronix.Host", "Arronix.Api", "Arronix.Client",
         "NzbDrone", "Sonarr"];

    public PluginLoadContext(PluginId id, string entryAssemblyPath)
        : base(name: $"arronix-plugin:{id}", isCollectible: true) { … }

    protected override Assembly? Load(AssemblyName name)
    {
        // 1. HARD DENY, BEFORE ANY FALLBACK. Returning null here would fall back to the Default context and
        //    SUCCEED, silently granting the plugin the host's implementation assemblies. This ordering is
        //    the only one that works, and it gets a test.
        if (BlockedPrefixes.Any(p => name.Name!.StartsWith(p, StringComparison.Ordinal)))
            throw new PluginIsolationException(name.Name);      // CoreErrorCode.PluginIsolationViolation

        // 2. The one shared contract assembly, plus the shared framework: return null so Default wins.
        //    If the plugin loaded its own copy of Abstractions, its IPluginModule would be a DIFFERENT Type
        //    than the host's and no cast would ever succeed. This is the most common ALC bug and it is
        //    silent; it gets its own test.
        if (name.Name is "Arronix.Abstractions" || IsSharedFramework(name)) return null;

        // 3. The plugin's own dependency closure, loaded FROM STREAM so the file is not locked and the
        //    context stays collectible (Lidarr's proven lesson, PluginLoader.cs:37-48).
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName) { /* resolver, then INativeLibraryResolver */ }
}
```

`isCollectible: true` from day one: hot unload is §15 future work, but collectibility costs nothing now and
forecloses nothing later. **Unload is not exercised in this milestone** and that is recorded.

**Zero assembly scanning, ever.** The loader loads exactly `manifest.EntryAssembly`, enumerates its
*exported* types, and requires exactly one `IPluginModule`. Lidarr is the deliberate negative example: it has
a real `PluginLoadContext` but *"no capability declaration and no export restriction — a plugin assembly is
simply added to the DI scan, so it sees every `NzbDrone.Core` type"*, and its fallback is *"retry startup
with plugins disabled if startup throws"*. Arronix must not need that fallback.

#### Static reference-graph validation — invariant 2, made mechanical

`PluginReferenceInspector` opens the entry assembly with
`System.Reflection.PortableExecutable.PEReader` + `System.Reflection.Metadata.MetadataReader` and reads the
`AssemblyRef` table. A reference to any blocked assembly is `CoreErrorCode.PluginIsolationViolation` at
**discovery** time, before an `AssemblyLoadContext` is even created. This is metadata reading, not loading:
no type initializers, no module initializers, no code execution.

**Verified:** `System.Reflection.Metadata.dll` and `System.Collections.Immutable.dll` are present in the
`net10.0` `Microsoft.NETCore.App` reference pack. **No package registration is required.**

#### The load pipeline

States: `Discovered → Validated → Loaded → Registered → Active`, plus terminal
`Quarantined(CoreErrorCode, defects)`. **A plugin never partially activates.**

| # | Step | Failure code |
|---|---|---|
| 1 | Enumerate `{root}/*/plugin.json` | — |
| 2 | Parse manifest (`UnmappedMemberHandling.Disallow`, `required` members) | `PluginManifestInvalid` 2003 |
| 3 | Semantic checks: id pattern, version parses, ≥1 capability, tokens well-formed, policy ids non-empty and unique within a category, categories drawn from the closed set of five | `PluginManifestInvalid` |
| 4 | Duplicate `PluginId` across discovered plugins | `PluginIdConflict` 2004 |
| 5 | Contract range vs host `Arronix.Abstractions` version, then the experimental gate (§3.6) | `PluginContractMismatch` 2001 |
| 6 | **Static reference-graph check** | `PluginIsolationViolation` 2008 |
| 7 | Create the ALC, load the entry assembly from stream | `PluginLoadFailure` 2000 |
| 8 | Locate exactly one `IPluginModule`, construct it | `PluginLoadFailure` |
| 9 | `Configure(context)` — every gated `Add*` rejects an undeclared capability (**reverse** direction of §4.1 step 3) | `PluginCapabilityMissing` 2002 |
| 10 | Forward check: every declared capability has ≥1 matching registration in the ledger | `PluginCapabilityUnsatisfied` 2005 |
| 11 | `ValidatedShape.TryValidate` per registered media kind (§4.4) | `PluginShapeInvalid` 2009 |
| 12 | `IntentSurfaceValidator` per registered intent surface (§6.6) | `PluginShapeInvalid` |
| 13 | Manifest `tokens[]` cross-checked against `MediaShape.Tokens` (§4.1 step 4, the part that *is* checkable) | `PluginPolicyDeclarationInvalid` 2007 |
| 14 | Token collision resolution (§4.1 step 5, rules below) | `PluginTokenConflict` 2006 |
| 15 | Provider id collision resolution | `PluginIdConflict` |
| 16 | Commit registrations into the host registries; publish `PluginActivated` | — |

**New `CoreErrorCode` values** — appended. Existing values are not renumbered, because the numbers appear in
support threads and log archives, not because renumbering is forbidden (`stability.md` permits it below
1.0.0). The 2000 block was already reserved for the loader:

```
PluginManifestInvalid = 2003          PluginIdConflict = 2004
PluginCapabilityUnsatisfied = 2005    PluginTokenConflict = 2006
PluginPolicyDeclarationInvalid = 2007 PluginIsolationViolation = 2008
PluginShapeInvalid = 2009             PluginDisabled = 2010
MediaKindConflict = 3002
```

**Step 4 (policy chains) is knowingly partial.** Cross-checking policy ids against "provided handlers" is
impossible until the Policy Engine exists (deferred by the extraction plan §9). This milestone validates
self-consistency plus the token cross-check. **Completion condition:** when `IPolicyStep`/`IPolicyChain`
land, `IPluginRegistry` gains `AddPolicyStep(category, policyId, step)` and the ledger comparison becomes
exact — one method, one comparison, no redesign.

**Step 5 (token collisions).** ARCHITECTURE §9 says the strategy is "under evaluation". Decided, three rules:

1. A token colliding with a **host global token** (`{Quality}`, `{Quality Full}`, `{Release Group}`,
   `{Original Title}`, `{Original Filename}`, `{MediaInfo …}`, `{Custom Formats}`, `{Preferred Words}`,
   `{Ext}`) → **reject the plugin.** The host's meaning is not negotiable.
2. The same token declared by two plugins for **different media kinds** → **allowed.** §9's own goal is
   *"stable meaning per token string within a media context."*
3. The same token for the **same media kind** cannot arise — two plugins claiming one `MediaKindId` is
   already `MediaKindConflict` at step 15.

#### The capability matrix

`Registration/CapabilityMatrix.cs` — one `FrozenDictionary<Type, Capability[]>`, faithful to extraction plan
§4. It is the single table that makes §11 mechanical, and `CapabilityMatrixTests` asserts every gated
contract in it has an `IPluginRegistry` method and vice versa.

| Contract | Required capability |
|---|---|
| `IHttpGateway`, `IRateLimiter`, `ICertificateValidationPolicy` | `network` |
| `IOutboundHttpInterceptor` | `indexing` |
| `IFileSystem` | `storage` (handed over as `ScopedFileSystem`) |
| `IFileTransferService` | `import` |
| `ITelemetrySink` | `telemetry-sink` |
| `IRedactionRuleProvider` | implied by the capability owning the secret |
| `IDiacriticFoldingProvider` | `parsing` \| `renaming` |
| `IMediaShapeProvider`, `IMediaItemSource` | `media-kind` |
| `IReleaseMatcher` | `matching` |
| `IReleaseQueryPlanner` | `indexing` |
| `IReleaseParser` | `parsing` |
| `IQualityModel` | `quality` |
| `IImportPipeline` | `import` |
| `IRenamePolicy`, `ILibraryLayout` | `renaming` |
| `IMediaIdResolver` | `metadata` |
| `IndexerRegistration` | `indexing` |
| `DownloadClientRegistration` | `download` |
| `NotifierRegistration` | `notification` |
| `MetadataSourceRegistration` | `metadata` |
| `ImportListRegistration` | `import-list` |
| **ungated** | `ITelemetryEmitter`, `ITelemetryEnricher`, `ITelemetryEventFilter`, `IHealthContributor`, `IPluginPaths`, the `ICacheProvider` family, `IProgressReporter`, `IEventPublisher`/`IEventHandler`, `IJsonSerializer`, `ArronixException`, `IOperatingSystemInfo`, `IHostRuntimeInfo`, `IScheduledJob`, `PluginIntentSurface` |

`network` is expanded from `indexing | metadata | download | notification | import-list` before matching
(§11.1).

### 4.2 Scoping decorators

| Decorator | Enforces |
|---|---|
| `ScopedFileSystem` | Every path argument checked against the plugin's granted roots; outside → `UnauthorizedAccessException`, regardless of platform permissions. The 0.2.0 `IFileSystem` XML docs already *promise* this; nothing delivered it until now. |
| `ScopedHttpGateway` | Stamps `{pluginId}` into `OutboundHttpRequest.RateLimitKey` when the caller left it null, appends the plugin id to the User-Agent product comment, and applies a per-plugin host allow/deny list from configuration. |
| `PartitionedCacheProvider` | Prefixes every partition with the plugin id — the "resolved partition is namespaced by the extension identifier" rule the `ICacheProvider` docs assert. |
| `PartitionedRateLimiter` | Composes the caller identity into the effective partition key, so §12's "shared across plugins hitting the same remote" holds while a plugin cannot starve another. |
| `FilteredEventPublisher` | A plugin sees platform events plus its own namespace, never another plugin's — the subscriber filter the `Events` contract documents and nothing implemented. |
| `PluginPaths` | One instance per loaded plugin; `DataFolder`/`CacheFolder`/`TempFolder` created **before** activation; the host `AppDataFolder` is never exposed. |

### 4.3 `src/Arronix.Host/` — layout and composition

```text
src/Arronix.Host/
├── Arronix.Host.csproj
├── GlobalUsings.cs
├── Composition/ArronixHostServiceCollectionExtensions.cs      // the table of contents (WP-8 owns this file)
├── Composition/{MediaRegistration,StorageRegistration,SchedulingRegistration,
│                ProviderRegistration,HealthRegistration}.cs   // one per subsystem, owned by its WP
├── Configuration/{HostOptions,SchedulerOptions,HealthOptions,LibraryOptions}.cs
├── Runtime/{PluginBootstrapper,PluginRuntimeState}.cs
├── Media/{IMediaKindRegistry,MediaKindRegistry,RegisteredMediaKind,ValidatedShape,ShapeDefect,
│          MediaKindProjection,AffordanceCalculator,CompletenessCalculator}.cs
├── Storage/{IMediaStore,InMemoryMediaStore,LibraryFacet,UnitFileLink,GroupMembership,MediaFileRecord}.cs
├── Scheduling/{JobScheduler,JobQueue,JobQueueEntry,BackgroundTaskRegistry,JobSchedule,JobScheduleParser,
│               ConcurrencyGovernor,BackoffLadder,FailureClass,FailureClassifier,CorrelationContext}.cs
├── Providers/{ProviderRegistry,ProviderDefinitionStore,ProviderStatusStore,ProviderTestService,
│              ProviderSessionStore,IndexerDispatcher,NotificationDispatcher}.cs
├── Health/{IHealthAggregator,HealthAggregator,HealthSnapshot,PluginHealthContributor,
│           SchedulerHealthContributor,ProviderHealthContributor}.cs
├── Intent/{IIntentRegistry,IntentRegistry,IntentSurfaceValidator,WorkbenchBroker}.cs
└── FileSystem/HostFileSystem.cs
```

```csharp
public static IServiceCollection AddArronixHost(this IServiceCollection services, IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);

    services.AddArronixCommon(configuration);       // options, TimeProvider, archives/hashing/naming, serialization
    services.AddArronixHostOptions(configuration);  // Arronix:Host, :Scheduler, :Health, :Library
    services.AddHostFileSystem();                   // see the note below
    services.AddArronixPlugins(configuration);      // loader, registry, scoping decorators
    services.AddMediaRegistry();
    services.AddInMemoryStorage();
    services.AddScheduling();
    services.AddProviderRegistry();
    services.AddHealthAggregation();
    services.AddHostedService<PluginBootstrapper>();
    return services;
}
```

Mirrors the established `AddArronixCommon` idiom exactly: a thin public entry point that is a table of
contents, one `internal static class <Subsystem>Registration` per subsystem in its own file, explicit and
exhaustive, **no assembly scanning** — *"a host that cannot enumerate what it registered cannot withhold
anything from an extension either."*

> **Known coupling risk, resolved.** `Arronix.Common` has **no `IFileSystem` implementation**, so resolving
> `IFileHasher` from `AddArronixCommon` alone fails at activation (`arronix-current-state.md` §5.5).
> `Arronix.Host` therefore ships a minimal `HostFileSystem : IFileSystem` (~22 members over `System.IO`,
> Tier B) and registers it before `AddArronixPlugins`. Separately, **the loader reads manifests and
> assemblies through `System.IO` directly**, never through `IFileHasher`, so plugin loading can never be
> blocked by an unbuilt Common subsystem.

All options bind under the `Arronix:` prefix through `AddValidatedOptions<T>`, matching Common.

### 4.4 `ValidatedShape` — parse, don't validate

```csharp
// src/Arronix.Host/Media/ValidatedShape.cs   (Tier B — host-side, NOT promoted)
public sealed class ValidatedShape
{
    public static bool TryValidate(MediaShape shape, out ValidatedShape? validated,
                                   out IReadOnlyList<ShapeDefect> defects);

    public MediaKindId Kind { get; }
    public MediaShape Declaration { get; }            // the wire form, kept verbatim for serialization
    public IReadOnlyList<MediaLevel> Levels { get; }  // topologically ordered, root first
    public MediaLevel LibraryEntry { get; }           // OBJECT, not an id
    public MediaLevel AcquisitionUnit { get; }
    public MediaLevel? VariantLevel { get; }
    public MediaLevel CompletenessUnit { get; }
    public MediaLevel FileAnchor { get; }
    public MediaLevel FileUnit { get; }
    public MediaLevel LevelOf(MediaLevelId id);       // TOTAL: the id came from this shape
    public IReadOnlyList<MediaLevel> PathTo(MediaLevelId id);
    public CoordinateSpace? CanonicalSpaceOf(MediaLevel level);
    public FormatFamily? FamilyForExtension(string extension);
    public SearchKind RequireSearchKind(string searchKindId);
}

public readonly record struct ShapeDefect(string Path, string Message, CoreErrorCode Code);
```

**This is the load-bearing safety move.** Every id-to-object cross reference is resolved once, at load, and
after the gate `LevelOf` is total, `AcquisitionUnit` is non-null, the level graph is acyclic and
topologically ordered, and every `SequenceAxis` points at a real ordinal component. Downstream host code —
scheduler, importer, API, projection — **cannot write a lookup that fails, because there are no lookups
left.**

`TryValidate` rules — sixteen, each traceable to a survey finding, all mechanical, none advisory:

1. `MediaKindId` unique across every loaded plugin (`MediaKindConflict`).
2. `Levels` non-empty; `MediaLevelId` unique within the shape.
3. Exactly one root (`Parent == null`); the parent graph is acyclic and topologically sortable.
4. **Each level has at most one child** — the linearity constraint of this milestone (#45).
5. Exactly one level carries `LibraryEntry`.
6. At least one level carries `AcquisitionUnit`; at least one carries `CompletenessUnit`; at least one
   carries `FileBearing`.
7. A `VariantAxis` level's parent carries `AcquisitionUnit`; `MediaLevel.Variant` is non-null **iff** the
   level carries `VariantAxis`; at most one level carries `VariantAxis`.
8. `FileBinding.AnchorLevelId` and `UnitLevelId` both resolve, both carry `FileBearing`, and `UnitLevelId`
   is a descendant-or-self of `AnchorLevelId`.
9. `OrdinalIsMeaningful` requires `AtMostOneFilePerUnit == false` (an ordinal only means something when a
   unit spans files).
10. Every `MediaLevel.CoordinateSpaceIds` entry exists in `MediaShape.CoordinateSpaces`.
11. At most one `IsCanonical` space among the spaces a given level references; a level with any space has
    exactly one canonical space.
12. Every `SequenceAxis.SpaceId` resolves to a space of `Kind == Ordinal` on the same level, and
    `ComponentIndex` is within that space's `Components`.
13. Every `SpanConstraint.SpaceId`/`ComponentId` resolves.
14. `GroupingAxis.MemberLevelId` and `SelectionFacet.AppliesToLevelId` resolve;
    `GroupingAxis.HasPrimaryMember` requires `Arity == ManyToMany`.
15. `FormatFamily.FileExtensions` are pairwise **disjoint** across families; every `Ladder` is non-empty
    with rank-distinct tiers; `Unknown` is not in the ladder. *(Readarr's lesson: extension sets are the de
    facto discriminator and overlap makes a family undecidable.)*
16. `FieldId` unique per level; exactly one field per level carries `FieldSemantics.Title`;
    `SearchKind.TargetLevelId` resolves; `SearchKind.Scope` references a declared `SequenceAxis` when
    `Kind == SequenceSpan` and a real ancestor when `Kind == Ancestor`.

Failure → `CoreErrorCode.PluginShapeInvalid` with the **full** defect list. No partial shapes, ever.

### 4.5 Registries

```csharp
// src/Arronix.Host/Media/IMediaKindRegistry.cs   (Tier B — host-side, NOT promoted)
public interface IMediaKindRegistry
{
    bool TryGet(MediaKindId kind, out RegisteredMediaKind? registered);
    RegisteredMediaKind Require(MediaKindId kind);        // throws MediaKindNotFound
    IReadOnlyList<RegisteredMediaKind> All { get; }
}

public sealed class RegisteredMediaKind
{
    public PluginId Plugin { get; }
    public MediaKindId Kind { get; }
    public ValidatedShape Shape { get; }
    public IMediaItemSource Items { get; }
    public IReleaseMatcher? Matcher { get; }
    public IReleaseQueryPlanner? QueryPlanner { get; }
    public IReleaseParser? Parser { get; }
    public IQualityModel? Quality { get; }
    public IImportPipeline? Import { get; }
    public IRenamePolicy? Naming { get; }
    public PluginIntentSurface Intent { get; }
    public IMediaKind Projection { get; }                 // the STABLE 0.1.0 contract, built by the host
    public MediaKindDescriptor Descriptor { get; }        // the §6 wire bundle, built once and cached
}
```

Sibling registries, all Tier B: `IProviderRegistry` (five families), `IBackgroundTaskRegistry` (the **stable**
contract, implemented), `IHealthRegistry`, `IIntentRegistry`, `ITokenRegistry`.

**Held host-side deliberately.** A plugin declares a shape; it never queries another kind's shape, because
§4 forbids direct cross-plugin interaction. Promotion trigger: a plugin that must read a foreign shape —
which would itself need review.

### 4.6 The storage seam — in-memory now, shape fixed now

**No persistence, no EF Core.** But the *shape* of the seam is fixed in this milestone, because getting it
wrong later is expensive. `IMediaStore` is **held host-side (Tier B)**: zero plugin implementers,
ARCHITECTURE §5 names it a Storage-Layer concern, and the promotion rule says hold.

The store holds **library state only**. The catalog comes from `IMediaItemSource` (§2.11).

```csharp
// src/Arronix.Host/Storage/IMediaStore.cs   (Tier B)
public interface IMediaStore
{
    // Library facet: the user-owned half of the catalog/library split.
    ValueTask<LibraryFacet?> FindLibraryAsync(MediaItemRef reference, CancellationToken ct);
    ValueTask UpsertLibraryAsync(LibraryFacet facet, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<MediaItemRef, LibraryFacet>> FindLibraryManyAsync(
        IReadOnlyList<MediaItemRef> references, CancellationToken ct);

    // Files and the join. NEVER an FK on either side.
    ValueTask<MediaFileId> UpsertFileAsync(MediaFileRecord file, CancellationToken ct);
    ValueTask LinkAsync(UnitFileLink link, CancellationToken ct);
    ValueTask UnlinkAsync(UnitFileLink link, CancellationToken ct);
    ValueTask<IReadOnlyList<UnitFileLink>> LinksForUnitAsync(MediaItemRef unit, CancellationToken ct);
    ValueTask<IReadOnlyList<UnitFileLink>> LinksForFileAsync(MediaFileId file, CancellationToken ct);

    // Grouping axes.
    ValueTask SetGroupMembershipAsync(GroupMembership membership, CancellationToken ct);
    ValueTask<IReadOnlyList<GroupMembership>> MembersOfAsync(MediaItemRef group, CancellationToken ct);
}

public sealed record LibraryFacet
{
    public required MediaItemRef Ref { get; init; }
    /// <summary>dimensionId → chosen value.</summary>
    public IReadOnlyDictionary<string, string> Monitor { get; init; }
        = new Dictionary<string, string>();
    /// <summary>Set on the parent of a VariantAxis level. The at-most-one invariant is enforced HERE,
    /// which is what Lidarr and Readarr each hand-rolled in a repository.</summary>
    public MediaItemRef? SelectedVariant { get; init; }
    public bool AutoSwitchVariant { get; init; } = true;
    public string? Path { get; init; }                 // LibraryEntry levels only
    public string? RootFolderPath { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset? AddedAt { get; init; }
}

/// <summary>
/// The single most consequential storage decision, taken before persistence exists so the eventual
/// relational schema cannot be biased toward any one *arr's FK direction. All four surveyed apps are
/// degenerate projections of this triple.
/// </summary>
public readonly record struct UnitFileLink(MediaItemRef Unit, MediaFileId File, int? Ordinal);

public readonly record struct GroupMembership(
    MediaItemRef Group, MediaItemRef Member, string? Position, long SortIndex, bool IsPrimary);

public sealed record MediaFileRecord
{
    public required MediaFileId Id { get; init; }
    /// <summary>The anchor. FileBinding.AnchorLevelId, which may differ from the unit level.</summary>
    public required MediaItemRef Anchor { get; init; }
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public QualityTier? Quality { get; init; }
    public string? FormatFamilyId { get; init; }
    public IReadOnlyList<Language> Languages { get; init; } = [];
}
```

`InMemoryMediaStore` uses `ConcurrentDictionary` throughout, mints `MediaItemId`/`MediaFileId` from an
`Interlocked` counter, and **enforces the shape's invariants from day one**:
`AtMostOneFilePerUnit`/`AtMostOneUnitPerFile` on `LinkAsync`, `OrdinalIsMeaningful` on the ordinal, span
constraints against the unit's coordinates, and the `SelectedVariant` at-most-one rule on
`UpsertLibraryAsync`. One code path, four kinds. Everything except the interface and the records is
`internal`; the future relational store replaces `InMemoryMediaStore` and nothing else moves.

**Completeness is host-computed** (`CompletenessCalculator`), which is why no `ICompletenessPolicy` contract
exists: count `CompletenessUnit` descendants that have at least one link, restricted to the selected variant
when `VariantSelection.CompletenessIsVariantRelative`, excluding coordinates matching a
`SequenceException.ExcludedFromCompleteness`. Every surveyed rule — including Lidarr's variant-relative
counts and Sonarr's specials exclusion — falls out of declarations.

### 4.7 Scheduler and job queue (ARCHITECTURE §6)

The **stable** `IScheduledJob` / `IBackgroundTaskRegistry` are implemented, not superseded. One scheduler,
one queue.

**Schedule grammar** — defined here because both governing documents defer it.
`RegisterJob(IScheduledJob job, string schedule)` is stable and unchanged; the host parses the string.

```
manual                      never runs automatically; trigger-only
startup                     once, when the host has finished activating plugins
every PT15M | every 00:15   fixed interval (ISO-8601 duration or TimeSpan literal)
daily 03:30                 once per day at local wall-clock time
```

**Not cron.** No surveyed *arr uses it (`Jobs/TaskManager.cs` schedules in minutes); cron would add a
parser, a time-zone question and a DST question for zero demonstrated demand. Adding `cron <expr>` later is
purely additive — the cheapest reversal available.

```csharp
public sealed record JobQueueEntry
{
    public required Guid EntryId { get; init; }
    public required string JobId { get; init; }
    public required PluginId Owner { get; init; }
    public required string CorrelationId { get; init; }
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }
    public required IReadOnlyList<string> ThrottleKeys { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public int Attempt { get; init; }
    public int Priority { get; init; }
    /// <summary>§6: "queue entries reference abstract media identifiers; plugin resolvers rehydrate."</summary>
    public MediaKindId? MediaKind { get; init; }
    public IReadOnlyList<MediaItemRef> Items { get; init; } = [];
}
```

**Concurrency — three simultaneous limits, all-or-none:**

1. **Global** — `SchedulerOptions.MaxConcurrentJobs`.
2. **Per job** — the stable `IScheduledJob.MaxConcurrency`, per job id.
3. **Capability-scoped throttles (§6)** — `ThrottleKeys` synthesized by the host from the owning plugin's
   capabilities and the target kind: `cap:indexing`, `plugin:tv`, `kind:tv`. Limits come from
   `SchedulerOptions.ThrottleLimits : IDictionary<string,int>` (defaults: `indexing` 4, `metadata` 2,
   `import` 2, `download` 8).

`ConcurrencyGovernor` holds a `SemaphoreSlim` per key and acquires keys **in ordinal order**, so deadlock is
impossible. Provider-level rate limiting stays with `IRateLimiter` (host-owned singleton, partition key
composed by the host) — §12's "shared across plugins hitting the same remote" is the limiter's job, not the
scheduler's.

**Backoff.** The ladder proven byte-identical in all four *arrs — `0, 1m, 5m, 15m, 30m, 1h, 3h, 6h, 12h,
24h` (`ThingiProvider/Status/EscalationBackOff.cs`) — with **full jitter** (`delay = Random.Shared.NextDouble()
* periods[level]`). Full jitter rather than `base + random(0, base)` because the scheduler's failure mode is
N plugins retrying one dead remote in lockstep, and full jitter is the only variant that fully decorrelates.

**Plugin-supplied failure classification (§6), via the stable result bag:**

```csharp
// src/Arronix.Host/Scheduling/FailureClass.cs   (Tier B)
public enum FailureClass { Permanent = 0, Transient = 1, RateLimited = 2, Configuration = 3, Canceled = 4 }
```

A job classifies its own failure by writing `JobExecutionResult.ResultData[WellKnownJobResults.FailureClass]
= "transient"` — using the record's existing `object` bag, which is what it is for. If a job says
nothing, `FailureClassifier` classifies by exception: `HttpRateLimitedException` → `RateLimited` (honoring
`GetRetryAfter(utcNow)` in preference to the ladder); `HttpGatewayException` 5xx → `Transient`, 4xx →
`Permanent`; `OperationCanceledException` → `Canceled`; `ArronixException` by `CoreErrorCode` band;
everything else → `Permanent`.

**Correlation, progress, media kind and item list are typed members on `JobExecutionContext`** (resolution
24, revised in 0.4.0). An earlier draft routed all four through published string keys in the context's
`Parameters` bag, on the grounds that the record was stable and could not gain a member before 1.0. That
premise is withdrawn — below 1.0.0 any contract here may be reshaped (`docs/contracts/stability.md`) — and
the keys are deleted along with the file that declared them:

```csharp
// src/Arronix.Abstractions/Scheduling/IScheduledJob.cs
public record JobExecutionContext(
    string JobId,
    DateTime ScheduledTime,
    DateTime StartTime,
    string CorrelationId,
    IReadOnlyDictionary<string, object> Parameters,   // caller-supplied arguments ONLY
    IProgressReporter? Progress = null,
    MediaKindId? MediaKind = null,
    IReadOnlyList<MediaItemRef>? Items = null);
```

**The dividing line is now stated once and holds everywhere:** anything the *platform* supplies to a run is
a typed member; `Parameters` carries only what the *caller* passed. A published string key over an `object`
bag is not a weaker typed member, it is an unchecked one, and nothing rides one here because a record could
not be changed.

The one bag that survives is on the *result*:

```csharp
// src/Arronix.Abstractions/Plugins/WellKnownJobResults.cs   (consts only — no new behavior)
public static class WellKnownJobResults
{
    public const string FailureClass = "arronix.failure-class";  // permanent|transient|rate-limited|configuration|canceled
    public const string RetryAfter   = "arronix.retry-after";    // ISO-8601 duration
}
```

and it survives **on merit, not on a version boundary**: the five failure classes are inferred from four
surveyed applications and no Arronix job has yet disagreed with them, so promoting the enumeration into the
contract assembly would fix a guess in the most expensive place to be wrong. The classifier owns the
vocabulary until a job exists that the vocabulary fails — at which point the enum is promoted and these two
keys are deleted, in one change, with nothing left behind.

### 4.8 Plugin lifecycle

`PluginBootstrapper : IHostedService`. `StartAsync` runs the §4.1 pipeline; `StopAsync` reverses it.

- Plugins have **no inter-plugin dependencies** (§4 forbids direct cross-plugin calls), so activation order
  is by plugin-id ordinal — deterministic, no topological sort.
- **Plugins are activated after `IServiceProvider` is built and never mutate it.** This removes the entire
  two-phase-container class of bug, where plugins must load before `Build()` and therefore before
  configuration, logging and the filesystem are available.
- A plugin failing at any stage is **quarantined, never fatal**. It surfaces as a permanent
  `HealthCheck.Unhealthy` and in `GET /api/v1/plugins` with its `CoreErrorCode`, message and defect list.
  This is the deliberate inversion of Prowlarr's `RemoveMissingImplementations`, which *deletes* stored
  definitions whose implementation vanished — under a plugin model that means uninstalling a plugin destroys
  the user's configuration.
- Zero plugins loading is a valid state; the host starts and serves an empty `/api/v1/kinds`.
- Teardown: `IPluginModule` gets no `Stop`. A module implementing `IAsyncDisposable` is awaited; the host
  owns disposal of everything registered through `IPluginRegistry`.

```csharp
public enum PluginState { Discovered, Validated, Loaded, Registered, Active, Quarantined, Stopped }

public sealed record PluginRuntimeState(
    PluginId Id, PluginState State, string? Name, string? Version,
    CapabilitySet Granted, CoreErrorCode? ErrorCode, string? Message,
    IReadOnlyList<string> Defects, DateTimeOffset ChangedAt);
```

### 4.9 Health aggregation (ARCHITECTURE §10)

```csharp
public sealed record HealthSnapshot(HealthStatus Status, DateTimeOffset CheckedAt, IReadOnlyList<HealthCheck> Checks);
public interface IHealthAggregator { Task<HealthSnapshot> CollectAsync(CancellationToken ct = default); }
```

Reuses the **stable** `HealthCheck` / `HealthStatus` / `HealthSeverity` verbatim. Rules:

- Fan out to every `IHealthContributor` with a per-contributor timeout (`HealthOptions.ContributorTimeout`,
  default 10 s) and full exception isolation; a throwing or timing-out contributor yields a synthetic
  `HealthCheck.Unhealthy` attributed to it rather than degrading the aggregate call.
- Plugin contributors are **wrapped** so `CheckId` becomes `{pluginId}/{checkId}` — cross-plugin id collision
  is structurally impossible.
- Overall `Status` is the maximum severity across checks.
- Results are cached with a TTL, invalidated on plugin state changes and provider status changes.
- Host-supplied contributors: plugin load status (one permanent `Unhealthy` per quarantined plugin),
  orphaned provider definitions, scheduler backlog depth, storage-mount availability.
- `GET /health` returns the snapshot as machine-readable JSON (§10) and maps `Status` to 200 / 200 / 503.

---

## 5. The provider model

**Replaces `Arronix.Abstractions.Providers`; there is exactly one provider vocabulary.** The 0.1.0 trio —
`IIndexerProvider`, `IndexerCapabilities`, `IMetadataProvider`, `MetadataSearchResult`, `MetadataItem`,
`IDownloadClientAdapter` and `DownloadStatus` — **is deleted** (resolution 31, as reversed by the product
owner in 0.4.0). The five families declared below — shipped as `IIndexer`, `IDownloader`, `INotifier`,
`ICataloger`, `ICurator` (see the rename note in §7.2) — under diagnostic **`ARX0015`**, are the whole
provider surface.

There is no bridge. An earlier draft of this section specified `StableProviderBridge`, a host-side wrapper
that presented a registered `IIndexerProvider` as an `IIndexer` (deriving `SearchProfile` from
`IndexerCapabilities`) and an `IDownloadClientAdapter` as a downloader, so that "both work". That
class and the trio it served are gone. Nothing ever shipped against the trio, and Arronix is a clean-sheet
rewrite: there is no compatibility to preserve, and carrying two provider vocabularies through the codebase
forever was the entire cost of preserving nothing. Migration from specific *arr versions is a separate,
later, version-specific script — which is where version-specific knowledge belongs, and is not a second
contract living in this assembly. `docs/contracts/stability.md` now states the general rule: below 1.0.0,
any contract may be changed, renamed or deleted, and a contract with no implementer outside this repository
is deleted rather than deprecated.

`sonarr-tv-and-crosscut.md` §B4 makes this the highest-confidence extraction in the codebase: **12 of 18
`ThingiProvider/` files are byte-identical across all four apps**, including all of `ProviderFactory` and the
entire failure-escalation subsystem, and *every* divergence is a BOM, a blank line, an indentation of a dead
comment, or a lambda parameter name.

### 5.1 The spine

```csharp
namespace Arronix.Abstractions.Providers;

public readonly record struct ProviderId
{
    public string Value { get; }                      // "{pluginId}:{localId}"
    public PluginId Plugin { get; }
    public string Local { get; }
    public static ProviderId Create(PluginId plugin, string local);
    public static bool TryParse(string value, out ProviderId id);
    public override string ToString();
}

public enum ProviderFamily { Indexer = 0, DownloadClient = 1, Notifier = 2, MetadataSource = 3, ImportList = 4 }

/// <remarks>
/// Prowlarr resolves implementations by <c>Type.Name</c>, case-insensitively — "a fragile identity that
/// Arronix should replace with a stable plugin-qualified provider id, since a unified host will have name
/// collisions across plugins that Prowlarr never faced". The id is minted by the registry, never by the
/// plugin.
/// </remarks>
public sealed record ProviderDescriptor
{
    public required string LocalId { get; init; }
    public required ProviderFamily Family { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<SettingsField> Settings { get; init; }
    /// <summary>Open tokens ("torrent", "usenet", "deemix"), never an enum — Lidarr had to migrate
    /// (Migration/043) precisely because Radarr's enum was closed.</summary>
    public IReadOnlyList<DownloadProtocol> Protocols { get; init; } = [];
    public IReadOnlyList<ProviderPreset> Presets { get; init; } = [];
    public Uri? InfoLink { get; init; }
}

public readonly record struct DownloadProtocol(string Value)
{
    public static readonly DownloadProtocol Torrent = new("torrent");
    public static readonly DownloadProtocol Usenet  = new("usenet");
    public override string ToString();
}

public sealed record ProviderPreset(string PresetId, string Name, IReadOnlyDictionary<string, string> Settings);

public sealed record ProviderDefinition
{
    public required int Id { get; init; }                  // host-assigned
    public required ProviderId Provider { get; init; }
    public required ProviderFamily Family { get; init; }
    public required string Name { get; init; }             // user-chosen
    public bool Enabled { get; init; } = true;
    public required IReadOnlyDictionary<string, string> Settings { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>User narrowing within the descriptor's reach. Empty means "every media kind".</summary>
    public IReadOnlyList<MediaKindId> MediaKinds { get; init; } = [];
    public int Priority { get; init; } = 25;
    /// <summary>Quarantine-and-warn: a definition whose provider vanished is retained, not deleted.</summary>
    public DefinitionState State { get; init; } = DefinitionState.Active;
    public ProviderMessage? Message { get; init; }
}

public enum DefinitionState { Active = 0, Orphaned = 1 }
public sealed record ProviderMessage(string Text, ProviderMessageSeverity Severity);
public enum ProviderMessageSeverity { Info = 0, Warning = 1, Error = 2 }

/// <remarks>
/// EVERY provider call takes its invocation. Providers are STATELESS: this is the direct fix for
/// <c>ProviderFactory.GetInstance</c>, which assigns <c>Definition</c> onto a container-resolved singleton —
/// "otherwise capability gating per §11 is racy". Shared mutable per-definition state is not discouraged
/// here; it is unrepresentable.
/// </remarks>
public readonly record struct ProviderInvocation(
    ProviderDefinition Definition, IProviderSessionStore Session, string CorrelationId);

/// <summary>Cookies, last-seen release, a capability snapshot. Host-owned, scoped per definition.</summary>
public interface IProviderSessionStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string? value, TimeSpan? lifetime = null, CancellationToken ct = default);
}

public interface IProvider
{
    ProviderId Id { get; }
    ProviderFamily Family { get; }
    Task<ValidationOutcome> TestAsync(ProviderInvocation invocation, CancellationToken ct = default);
    /// <summary>Dynamic option source — Prowlarr's <c>RequestAction("getUrls")</c>, generalized.</summary>
    Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation, string optionSourceId, CancellationToken ct = default);
}
```

### 5.2 Settings schema — declarative only, semantic not presentational

```csharp
public sealed record SettingsField
{
    public required string FieldId { get; init; }
    public required string Name { get; init; }
    public required FieldValueKind ValueKind { get; init; }
    public required SettingRole Role { get; init; }
    public SettingSensitivity Sensitivity { get; init; } = SettingSensitivity.Public;
    public bool Required { get; init; }
    public bool Advanced { get; init; }
    public bool Multivalued { get; init; }
    public string? HelpText { get; init; }
    public string? HelpWarning { get; init; }
    public Uri? HelpLink { get; init; }
    public string? DefaultValue { get; init; }
    public string? Section { get; init; }
    public string? Unit { get; init; }
    public IReadOnlyList<FacetValue> Choices { get; init; } = [];
    /// <summary>An identifier the client resolves via <c>GET /api/v1/providers/definitions/{id}/options/{sourceId}</c>.
    /// An id, never a URL and never a component name.</summary>
    public string? OptionSourceId { get; init; }
}

public enum SettingRole
{
    Value = 0, Endpoint = 1, Path = 2, Duration = 3, ByteSize = 4, Count = 5, Flag = 6,
    Enumeration = 7, Ordering = 8, TagSet = 9, CategorySet = 10,
    QualityProfileRef = 11, MediaKindRef = 12, RootFolderRef = 13, DownloadProtocolRef = 14,
    NamingTemplate = 15, SelectionPolicyRef = 16,
}

/// <remarks>
/// One declaration, three obligations discharged: (a) the UI's "this is a secret credential" intent;
/// (b) the API's serialization rule — Credential/Secret are never emitted outward, they round-trip as the
/// sentinel <c>"********"</c> and a write carrying the sentinel means "unchanged"; (c) an automatic
/// <c>RedactionRule</c> contributed to the §9/§10 telemetry pipeline. That is the strongest single argument
/// for putting it in Abstractions.
/// </remarks>
public enum SettingSensitivity { Public = 0, UserName = 1, Credential = 2, Secret = 3 }

public sealed record ValidationOutcome
{
    public static ValidationOutcome Success { get; }
    public required IReadOnlyList<ValidationFailure> Failures { get; init; }
    public bool IsValid { get; }                     // no Error-severity failures
    public static ValidationOutcome Failed(params ValidationFailure[] failures);
    public ValidationOutcome Concat(ValidationOutcome other);
}

public sealed record ValidationFailure(
    string? FieldId, string Message,
    ValidationSeverity Severity = ValidationSeverity.Error,
    CoreErrorCode Code = CoreErrorCode.InvalidConfiguration);

public enum ValidationSeverity { Warning = 0, Error = 1 }
```

**No attribute path.** The Prowlarr survey recommends both an attribute path (compiled providers) and a
declarative path (Cardigann-style data-defined ones). Only the declarative path is adopted: an attribute is
a plugin's own reflection over its own types, it never crosses the boundary, and two representations of one
schema guarantee drift. It also means the *host* never reflects over plugin types — which is the whole point
of a closed registration surface. A data-defined provider that materializes hundreds of indexers from
versioned YAML works with no further design (`IProviderDefinitionSource` is deferred, §8, but the schema it
would emit already exists).

**FluentValidation is out of the contract**, per `radarr-movies.md` §4.4: *"a hard third-party dependency
baked into the provider contract … a plugin compiled against FluentValidation vN cannot negotiate against a
host on vN+1."* Abstractions has zero package references and keeps them.

### 5.3 Registrations

```csharp
public sealed record IndexerRegistration(ProviderDescriptor Descriptor, IIndexer Provider);
public sealed record DownloadClientRegistration(ProviderDescriptor Descriptor, IDownloadClient Provider);
public sealed record NotifierRegistration(ProviderDescriptor Descriptor, INotifier Provider);
public sealed record MetadataSourceRegistration(ProviderDescriptor Descriptor, IMetadataSource Provider);
public sealed record ImportListRegistration(ProviderDescriptor Descriptor, IImportListSource Provider);
```

Provider status and escalating back-off (`ProviderStatusBase`, `EscalationBackOff`,
`ProviderStatusServiceBase` — byte-identical in all four *arrs) go to `Arronix.Host/Providers/` as
`ProviderStatusStore` (**Tier B, not promoted**: no plugin reads it). It reproduces the surveyed behavior
exactly, including the initial-failure grace window, the 15-minute startup grace and `RecordConnectionFailure`
(record without escalating). Two independent back-off domains — scheduler and provider — as in every *arr.

`ProviderRegistry` publishes `IDomainEvent`s so cross-cutting concerns attach without the provider knowing
(Prowlarr's `ProviderAddedEvent<IIndexer>` pattern, generalized). `Family` is a value, not a generic type
argument — which is what lets the event cross a plugin boundary at all:

```csharp
public sealed record ProviderDefinitionChanged(
    Guid EventId, DateTimeOffset OccurredAt, string? CorrelationId,
    ProviderFamily Family, int DefinitionId, ProviderChangeKind Change) : IDomainEvent;

public enum ProviderChangeKind { Added = 0, Updated = 1, Removed = 2, StatusChanged = 3, Orphaned = 4 }
```

### 5.4 Indexers — Prowlarr's problem, solved by set intersection

*One indexer serves N media kinds and the per-kind search parameters differ.*

```csharp
public interface IIndexer : IProvider
{
    Task<IndexerProfile> DescribeAsync(ProviderInvocation invocation, CancellationToken ct = default);
    Task<ReleaseQueryResult> SearchAsync(
        ProviderInvocation invocation, ReleaseQuery query, CancellationToken ct = default);
    Task<ReleaseFetch> FetchAsync(ProviderInvocation invocation, Uri link, CancellationToken ct = default);
}
```

**One method, not an overload set.** `IIndexer.Fetch` has 7 overloads in Sonarr, 1 in Radarr, 2 in Lidarr,
2 in Readarr and 5 in Prowlarr — *"the arity of the overload set is itself per-kind … overload resolution
is a compile-time mechanism and cannot survive a plugin boundary at all."* Adding a sixth media kind must
not edit a core interface.

```csharp
public sealed record IndexerProfile
{
    public required IReadOnlyList<SearchProfile> SearchProfiles { get; init; }
    public required IReadOnlyList<CategoryId> Categories { get; init; }
    public bool SupportsRss { get; init; }
    public bool SupportsPagination { get; init; }
    public int MaxPageSize { get; init; } = 100;
    public int DefaultPageSize { get; init; } = 100;
    public IReadOnlyList<string> Flags { get; init; } = [];   // open: "freeleech", "internal", "scene"
    public IndexerPrivacy Privacy { get; init; }
    public static IndexerProfile Union(IndexerProfile a, IndexerProfile b);   // Prowlarr's Concat, for aggregates
}

public enum IndexerPrivacy { Public = 0, SemiPrivate = 1, Private = 2 }

/// <remarks>
/// Note what is NOT here: no media-kind reference and no newznab search-mode string. An indexer declares
/// what it can be asked; a media kind declares what it needs to ask. Neither names the other's nouns.
/// </remarks>
public sealed record SearchProfile
{
    public required string ProfileId { get; init; }
    public required IReadOnlyList<SearchTerm> Terms { get; init; }
    public required IReadOnlyList<CategoryId> Categories { get; init; }
    /// <summary>Wire names are the indexer protocol's business: (Ordinal, "season") → "&amp;season=".</summary>
    public IReadOnlyList<SearchTermBinding> Bindings { get; init; } = [];
    public bool SupportsRawSearch { get; init; }
}

public readonly record struct SearchTermBinding(SearchTerm Term, string? ComponentId, string WireName);

public sealed record ReleaseQueryResult(
    IReadOnlyList<ReleaseCandidate> Releases,   // the STABLE 0.1.0 DTO
    bool IsPartialResult,
    IReadOnlyList<string> Warnings);

public sealed record ReleaseFetch(ReadOnlyMemory<byte> Content, string? FileName, string? ContentType);
```

**Eligibility is pure data intersection; neither side knows the other's nouns:**

```
kind declares   : SearchKind    { RequiredTerms[], OptionalTerms[], Categories[] }
indexer declares: SearchProfile { Terms[], Categories[] }

eligible  ⟺  kind.RequiredTerms ⊆ profile.Terms
          ∧  kind.Categories ∩ profile.Categories ≠ ∅
```

**Two gates, at the two places Prowlarr proves they belong:**

- **Category gate, host-side, before any network call** (`ReleaseSearchService.cs:186`) — an indexer whose
  supported categories do not intersect the query's is not dispatched to at all. Cheap; avoids the network
  entirely when nothing can match.
- **Term gate, provider-side** (`HttpIndexerBase.cs:105-117`) — a query using an undeclared term
  short-circuits to an empty result rather than issuing a malformed request.

Three of Prowlarr's seven `Applications/` adapters gate on **categories only**, so a media kind may declare
zero `RequiredTerms` and be perfectly served by free-text plus categories. The model allows that.

Newznab category ids are carried **verbatim as interop identifiers** (1000 games / 2000 movies / 3000 audio
/ 4000 software / 5000 TV / 6000 adult / 7000 books / 8000 other), with the `>= 100000` band reserved for
provider-specific categories exactly as Prowlarr does and excluded from aggregation. This is a working,
deployed cross-media taxonomy every indexer on earth already speaks; replacing it would be a decade of lost
interop for no gain.

`ReleaseQueryPlan`'s tiered fallback (§2.11) is the media-agnostic form of
`IndexerPageableRequestChain`: walk tiers in order, the first tier producing any release wins, page within a
tier while pages are full.

### 5.5 Notifications — Sonarr's problem

*`INotification`'s contract is generic but its payload is per-media-kind.*

The evidence is exact and mechanical: stripping the usings and **two** properties from `GrabMessage.cs` in
all four apps yields the identical MD5 `773844405df9ac99258fb2e93dcd48e5`. The envelope is byte-identical;
exactly two slots carry the media. And ~50 hand-rolled `SupportsOnXxx` booleans across the four apps are
*"exactly the mechanism ARCHITECTURE §11 formalizes, invented independently four times."*

```csharp
public interface INotifier : IProvider
{
    /// <summary>Replaces ~50 SupportsOnXxx properties with one declared set.</summary>
    IReadOnlyList<NotificationEvent> SupportedEvents { get; }
    Task NotifyAsync(ProviderInvocation invocation, NotificationMessage message, CancellationToken ct = default);
}

public enum NotificationEvent
{
    Grabbed = 0, Imported = 1, Upgraded = 2, Renamed = 3, Retagged = 4,
    ItemAdded = 5, ItemDeleted = 6, FileDeleted = 7,
    GrabFailed = 8, ImportFailed = 9, ManualInteractionRequired = 10,
    AcquisitionComplete = 11,                     // Sonarr's OnImportComplete, generalized
    HealthDegraded = 12, HealthRestored = 13, ApplicationUpdated = 14,
}

public sealed record NotificationMessage
{
    public required NotificationEvent Event { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string CorrelationId { get; init; }
    /// <summary>Rendered BY THE ORIGINATING MEDIA PLUGIN, carried by the host, consumed by the notifier.
    /// This is the whole solution: the notifier never interprets media context. It is also the type the
    /// client's activity feed renders, so there is exactly one plugin-side rendering path, not two.</summary>
    public required RenderedSummary Summary { get; init; }
    public MediaKindId? MediaKind { get; init; }
    public MediaItemRef? Item { get; init; }
    public IReadOnlyList<FileSummary> Files { get; init; } = [];
    public IReadOnlyList<FileSummary> ReplacedFiles { get; init; } = [];
    public QualityTier? Quality { get; init; }               // STABLE DTO
    public DownloadAttribution? Download { get; init; }
    public HealthCheck? Health { get; init; }                // STABLE DTO, identical in all four apps
    /// <summary>Namespaced, plugin-owned, OPAQUE to the host and to the notifier. Keys are the shape's
    /// field ids, so a generic provider renders {label: value} with no media knowledge and a kind-aware one
    /// reads its own slice. This is what keeps invariant 1 and invariant 2 simultaneously satisfiable.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
}

public sealed record DownloadAttribution(
    string ClientName, DownloadProtocol Protocol, string DownloadId, ProviderId? Indexer);

public sealed record FileSummary(
    string RelativePath, long Size, QualityTier? Quality, IReadOnlyList<Language> Languages);
```

Three rules make this work:

1. **The originating plugin renders `Summary`.** The host owns the product name (from
   `HostIdentityOptions`), so the `_BRANDED` constants that are the only real per-app value in
   `NotificationBase` disappear entirely.
2. **`Context` is opaque to `Arronix.Abstractions`.**
3. **`SupportedEvents` is declared data**, cross-checked at registration. An unknown event is not
   expressible; an unsupported one is simply not dispatched.

**Web Push is an ordinary member of this family with no special handling:**
`ProviderDescriptor { Family = Notifier, Protocols = ["webpush"] }`, VAPID keys in settings fields marked
`SettingSensitivity.Secret`, subscription rows owned by the API. `RenderedSummary` is precisely the payload
a push notification needs (title / subtitle / artwork / deep link), so **no push-specific contract exists**.
The service worker, `manifest.webmanifest`, TLS/secure-context and the Web Push protocol are owned by
`docs/design/pwa-and-push.md` and are not duplicated here.

### 5.6 Metadata sources, import lists, download clients

> **Renamed in 0.4.0 — the names below are historical.** This section is the original design record and its
> reasoning still stands, but **every type name in it has changed**. Do not copy a signature from here.
> Read the shipped contracts in `src/Arronix.Abstractions/Providers/` instead. The mapping:
>
> | Named here | Shipped as |
> |---|---|
> | `IMetadataSource` | `ICataloger` |
> | `IImportListSource` | `ICurator` |
> | `IDownloadClient` | `IDownloader` |
> | `MetadataSourceCapabilities` | `CatalogerCapabilities` |
> | `ImportListFetch` / `ImportListItem` | `CuratedListFetch` / `CuratedItem` |
> | `DownloadClientItem` / `DownloadClientInfo` | `DownloadItem` / `DownloaderInfo` |
> | `MetadataSourceRegistration` / `ImportListRegistration` / `DownloadClientRegistration` | `CatalogerRegistration` / `CuratorRegistration` / `DownloaderRegistration` |
> | `AddMetadataSource` / `AddImportListSource` / `AddDownloadClient` | `AddCataloger` / `AddCurator` / `AddDownloader` |
> | `Capability.ImportList` (wire `import-list`) | `Capability.Curation` (wire `curation`) |
>
> The full table, including the deletions, is in [`docs/contracts/stability.md`](../contracts/stability.md)
> under 0.4.0. Sections of `file-management.md`, `acquisition-pipeline.md` and
> `frontend-decommissioning.md` cite this section by its old names and are stale in the same way.

```csharp
public interface IMetadataSource : IProvider
{
    MetadataSourceCapabilities Capabilities { get; }
    /// <summary>DISCRIMINATED BY LEVEL — this is the direct fix for Readarr's
    /// <c>ISearchForNewEntity</c> returning <c>List&lt;object&gt;</c>, and the level comes free from the
    /// shape model. The shape paying a dividend in an unrelated subsystem is the best evidence it is right.</summary>
    Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(
        ProviderInvocation invocation, MetadataQuery query, CancellationToken ct = default);
    Task<MetadataGraph?> GetAsync(
        ProviderInvocation invocation, ExternalId id, SelectionPolicy policy, CancellationToken ct = default);
    /// <summary>Declared, not assumed: Radarr has GetChangedMovies, Lidarr GetChangedAlbums, S and K neither.</summary>
    Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation, DateTimeOffset since, CancellationToken ct = default);
}

[Flags]
public enum MetadataSourceCapabilities { None = 0, Search = 1, DeltaSync = 2, Discovery = 4, BulkFetch = 8 }

public sealed record MetadataQuery(MediaKindId Kind, MediaLevelId? Level, string Text, ExternalId? Id);
public sealed record MetadataSearchHit(
    ExternalId Id, MediaLevelId Level, string Title, int? Year, string? Disambiguation,
    IReadOnlyList<ArtworkRef> Artwork);

/// <summary>Level-keyed drafts plus parent edges — the generalization of
/// <c>Tuple&lt;Series, List&lt;Episode&gt;&gt;</c> / <c>Tuple&lt;string, Album, List&lt;ArtistMetadata&gt;&gt;</c>,
/// which "rhyme exactly" across all four apps.</summary>
public sealed record MetadataGraph(IReadOnlyList<MetadataNode> Nodes);
public sealed record MetadataNode(
    MediaLevelId Level, ExternalId Id, ExternalId? ParentId, string Title,
    IReadOnlyDictionary<string, FieldValue> Fields, CoordinateSet Coordinates);

/// <summary>The resolved third profile axis, passed into the fetch exactly as
/// <c>GetArtistInfo(id, metadataProfileId)</c> does. Facet ids come from MediaShape.SelectionFacets.</summary>
public sealed record SelectionPolicy
{
    public static SelectionPolicy None { get; }            // the reserved, immutable empty policy
    public required string PolicyId { get; init; }
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedValues { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, double> Thresholds { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, bool> Flags { get; init; } = new Dictionary<string, bool>();
}
```

Two host rules attach to `SelectionPolicy`, both taken from the survey and both **mandatory**:

- **"Local wins" is not optional.** The host never removes an item that was added manually or that has files
  on disk, whatever the policy says (`MetadataProfileService.FilterByPredicate` + `localHash`). That is the
  safety valve that stops a profile tweak deleting a library, and the host enforces it for every kind.
- **A reserved, immutable `None` policy exists per kind**, cannot be edited or deleted, and deletion of an
  in-use policy is refused.

```csharp
public interface IImportListSource : IProvider
{
    MediaKindId MediaKind { get; }
    TimeSpan MinimumRefreshInterval { get; }
    /// <summary>A result ENVELOPE, not a bare list — S and R evolved to this to carry partial-failure
    /// information; L and K still return a bare list and lose it.</summary>
    Task<ImportListFetch> FetchAsync(ProviderInvocation invocation, CancellationToken ct = default);
}

public sealed record ImportListFetch(
    IReadOnlyList<ImportListItem> Items, bool AnyFailure, IReadOnlyList<string> Warnings);

public sealed record ImportListItem
{
    public required MediaLevelId Level { get; init; }
    public required IReadOnlyList<ExternalId> ExternalIds { get; init; }
    public string? Title { get; init; }
    public int? Year { get; init; }
    public DateTimeOffset? ReleaseDate { get; init; }
    /// <summary>Generalizes Sonarr's <c>List&lt;Season&gt;</c> — the ONLY place the Season concept escapes
    /// into a cross-cutting provider contract, and it escapes as a coordinate, never as an entity reference.</summary>
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;
}

public interface IDownloadClient : IProvider
{
    DownloadProtocol Protocol { get; }
    Task<string> DownloadAsync(ProviderInvocation i, GrabRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DownloadClientItem>> GetItemsAsync(ProviderInvocation i, CancellationToken ct = default);
    Task RemoveItemAsync(ProviderInvocation i, string downloadId, bool deleteData, CancellationToken ct = default);
    Task MarkImportedAsync(ProviderInvocation i, string downloadId, CancellationToken ct = default);
    Task<DownloadClientInfo> GetInfoAsync(ProviderInvocation i, CancellationToken ct = default);
}

public sealed record GrabRequest(
    ReleaseId Release, Uri DownloadUrl, string Title, MediaKindId? MediaKind,
    IReadOnlyList<MediaItemRef> Units, string? Category, DownloadProtocol Protocol, ProviderId? Indexer);

public sealed record DownloadClientItem(
    string DownloadId, string Title, DownloadItemStatus Status, long TotalSize, long RemainingSize,
    TimeSpan? RemainingTime, string? OutputPath, DownloadProtocol Protocol, bool CanMoveFiles,
    bool CanBeRemoved, string? ErrorMessage);

public enum DownloadItemStatus { Queued = 0, Paused = 1, Downloading = 2, Completed = 3, Failed = 4, Warning = 5 }
public sealed record DownloadClientInfo(string Name, IReadOnlyList<string> OutputRootFolders, bool RemovesCompleted);
```

`IDownloadClient` differs from the *arrs' by exactly the one line that carried a media type; every other
member is *character-for-character identical in all four apps*. `GrabRequest` is that one line, made
media-neutral.

---

## 6. The client/server split and the descriptor-driven UI

### 6.1 Topology

```text
Browser                                        Server process
┌──────────────────────────────┐              ┌─────────────────────────────────────────┐
│ Arronix.Client (Blazor WASM) │   REST       │ Arronix.Api (ASP.NET Core, NOT Blazor)  │
│  PWA, installable            │◄────────────►│  REST + SignalR + static files          │
│  refs Arronix.Abstractions   │   SignalR    │  refs Abstractions, Common, Plugins, Host│
│  ONLY                        │              │  ZERO PackageReference                  │
└──────────────────────────────┘              │                                          │
                                              │  Arronix.Host ── Arronix.Plugins ──┐     │
                                              │        AssemblyLoadContext per plugin    │
                                              │        Arronix.Plugin.{Tv,Movies,…}      │
                                              └─────────────────────────────────────────┘
```

**`Arronix.Api` does not reference `Arronix.Client`.** The conventional hosted-Blazor arrangement
(`Microsoft.AspNetCore.Components.WebAssembly.Server` + a project reference + `UseBlazorFrameworkFiles()`)
would put a Blazor package into `Arronix.Api` and make invariant 4 unverifiable by a package-set test.
Instead the Api serves a configured directory (`Arronix:Api:ClientRoot`, default `./wwwroot`) with
`UseStaticFiles` plus an explicit `FileExtensionContentTypeProvider` for `.wasm`, `.dat`, `.blat`, `.pdb`,
`.br`, `.gz`, `.webmanifest`, and a SPA fallback to `index.html`. `service-worker.js` is served from the app
root with `Cache-Control: no-cache` so its registration scope covers the whole origin.

*Trade-off, stated:* ~15 lines of content-type mapping the hosting package would have supplied, in exchange
for `Arronix.Api` having **zero `PackageReference`s at all** — the strongest possible form of the invariant-4
assertion — and a client that stays independently hostable (a CDN, a separate origin, a native shell).

### 6.2 The governing principle

> **A plugin contributes declarations, never code, to any user interface.**

`Arronix.Abstractions` may not name a UI technology or a widget. There is no `RenderFragment`, no component
`Type`, no markup, no CSS, no icon, no color, no width, no layout. The falsification test applied to every
member below is:

> *Does a CLI, a TUI, a mobile app and a voice assistant each have an obvious, non-arbitrary way to honor
> this declaration? If only a web page does, it is presentational and must be re-cast.*

Applied, it changed the design:

| Rejected (presentational) | Accepted (semantic) | Why |
|---|---|---|
| `Widget = "checkbox"` | `MonitorDimension { Kind = Toggle }` | A CLI prints `[x]`; a voice assistant says "monitored". |
| `Color = "red"` | `StateTone`, `HealthSeverity`, `Consequence` | Valence exists in the domain; color is a rendering of it. |
| `Icon = "trash"` | `Consequence.Destructive` + `ConfirmationRequirement.TypeToConfirm` | A CLI prompts; a TUI reddens; a mobile app swipes. |
| `ColumnWidth`, `Order` | `Prominence` | An importance rank is meaningful to a card face, a default column set and `--verbose` alike. |
| `Component = "SeasonGrid"` | `BrowseAxisKind.Sequence` over a declared `SequenceAxis` | The client chooses a grid; a CLI chooses `--season N`. |
| `SelectOptionsUrl` | `OptionSourceId` | A URL is transport; an id is intent. |
| `Inline = true` | `SummaryFieldWeight` | "Inline" is a layout word; "weight" is an importance word. |

> **Deviation from ARCHITECTURE §15, declared loudly.** §15 (Future Directions) names *"Dynamic UI component
> registry keyed by plugin capability & token exposure."* **This design rejects that outright and
> permanently.** A component registry (a) requires the client to load plugin assemblies — a capability-model
> catastrophe, since the browser is outside the host's enforcement entirely; (b) welds the client's UI
> technology into the plugin contract, so the UI can never be replaced; and (c) makes a CLI or a mobile
> client structurally impossible. **Recommend amending §15** to *"declarative intent vocabulary rendered by
> any front end."*

### 6.3 The intent vocabulary — `Arronix.Abstractions.Intent` (`ARX0016`)

Field semantics and setting semantics are **not re-declared here** — they are `FieldDescriptor` (§2.8) and
`SettingsField` (§5.2). That reuse is itself the point: one declaration, several consumers.

```csharp
namespace Arronix.Abstractions.Intent;

// --- (a) Actions: what may be done ------------------------------------------------
public sealed record ActionDescriptor
{
    public required string ActionId { get; init; }     // "search", "refresh", "monitor.set", "variant.select"
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required ActionScope Scope { get; init; }
    public MediaLevelId? TargetLevelId { get; init; }
    public required Consequence Consequence { get; init; }
    public required ConfirmationRequirement Confirmation { get; init; }
    /// <summary>A plain-language statement of consequence, shown or SPOKEN before a destructive action is
    /// committed. Not a dialog title — a sentence any front end can present.</summary>
    public string? ConsequenceStatement { get; init; }
    public bool LongRunning { get; init; }
    public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];
    /// <summary>A boolean field on the item gating availability. Null = always available.</summary>
    public string? EnabledWhenFieldId { get; init; }
}

public enum ActionScope { Item = 0, Selection = 1, Level = 2, Kind = 3, System = 4 }
public enum Consequence { Safe = 0, Costly = 1, Destructive = 2, Irreversible = 3 }
public enum ConfirmationRequirement { None = 0, Acknowledge = 1, TypeToConfirm = 2 }

public sealed record ActionParameter(
    string ParameterId, string Name, FieldValueKind ValueKind, bool Required,
    IReadOnlyList<FacetValue> Choices, string? DefaultValue = null, string? OptionSourceId = null);

// --- (b) Browsability: how a kind is traversed ------------------------------------
public sealed record BrowseAxis
{
    public required string AxisId { get; init; }
    public required string Name { get; init; }
    public required BrowseAxisKind Kind { get; init; }
    public MediaLevelId? LevelId { get; init; }
    public string? GroupingAxisId { get; init; }
    public string? SequenceAxisId { get; init; }
    public string? FieldId { get; init; }
    public bool IsDefault { get; init; }
}

public enum BrowseAxisKind
{
    Hierarchy = 0,   // descend the containment spine
    Sequence = 1,    // along a SequenceAxis (season, disc) or a Date field — this is how a CALENDAR is expressed
    Grouping = 2,    // along a GroupingAxis (collection, book series)
    Facet = 3,       // partition by a Groupable field (genre, status, quality)
    Flat = 4,        // no partition
}

public sealed record SortOption(string FieldId, string Name, SortDirection DefaultDirection);
public enum SortDirection { Ascending = 0, Descending = 1 }
public sealed record FilterOption(string FieldId, string Name, FilterOperators Operators);
[Flags] public enum FilterOperators
{ None = 0, Equals = 1, NotEquals = 2, Contains = 4, GreaterThan = 8, LessThan = 16, Between = 32, In = 64, IsNull = 128 }

// --- (c) State semantics: what an item's condition means --------------------------
public sealed record StateDescriptor
{
    public required string StateId { get; init; }   // "missing", "wanted", "downloading", "have", "upgradable"
    public required string Name { get; init; }
    public required StateTone Tone { get; init; }
    public string? Description { get; init; }
    /// <summary>The field whose value, when equal to <see cref="StateId"/>, means the item is in this state.</summary>
    public required string SourceFieldId { get; init; }
}

/// <remarks>
/// The riskiest term in the vocabulary, and it survives the test: it states VALENCE and URGENCY, not color.
/// A CLI maps it to a "!" prefix or an exit code; a screen reader to announcement priority; a voice
/// assistant to phrasing; a web client to a class name. Dropping it would force the client to recognize
/// "missing" and "have" — i.e. compile-time knowledge of a media kind, which is exactly what this design
/// forbids. Tone is the price of genuine kind-agnosticism and it is a cheap one.
/// </remarks>
public enum StateTone { Neutral = 0, Positive = 1, Attention = 2, Problem = 3, InProgress = 4 }

// --- (d) Rendered summaries ------------------------------------------------------
// NOTE ON PLACEMENT: RenderedSummary, SummaryField, SummaryFieldWeight and ArtworkRef live in
// Arronix.Abstractions.Shape (ARX0013), NOT here. They are what a MEDIA PLUGIN renders about its own item,
// they are consumed by BOTH the provider model (§5.5 notifications) and the client, and putting them in
// Intent would make Arronix.Abstractions.Providers depend on Arronix.Abstractions.Intent for no reason —
// which would also serialize two work packages that are otherwise independent (§7.1). They are reproduced
// here for readability only.
public sealed record RenderedSummary            // → src/Arronix.Abstractions/Shape/RenderedSummary.cs
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Body { get; init; }
    public IReadOnlyList<SummaryField> Fields { get; init; } = [];
    public IReadOnlyList<ArtworkRef> Artwork { get; init; } = [];
    /// <summary>Relative, app-internal: "/tv/items/42". Never an absolute origin.</summary>
    public string? DeepLink { get; init; }
}

public readonly record struct SummaryField(string Label, string Value, SummaryFieldWeight Weight);
public enum SummaryFieldWeight { Primary = 0, Secondary = 1 }
public sealed record ArtworkRef(string Role, Uri Url, int? Width, int? Height);

// --- (e) External surfaces: the honest, deliberately weak escape hatch -------------
/// <remarks>
/// An item has a representation the platform does not own. The template is an absolute http/https URI with
/// {fieldId} placeholders, validated at load. A web client opens a tab; a CLI runs xdg-open; a mobile app
/// opens a browser. A plugin can POINT at a UI; it can never INJECT one.
/// </remarks>
public sealed record ExternalSurfaceDescriptor(
    string SurfaceId, string Name, MediaLevelId LevelId, string UriTemplate);

// --- the bundle -------------------------------------------------------------------
public sealed record PluginIntentSurface
{
    public required MediaKindId MediaKind { get; init; }
    public IReadOnlyList<BrowseAxis> BrowseAxes { get; init; } = [];
    public IReadOnlyList<SortOption> Sorts { get; init; } = [];
    public IReadOnlyList<FilterOption> Filters { get; init; } = [];
    public IReadOnlyList<ActionDescriptor> Actions { get; init; } = [];
    public IReadOnlyList<StateDescriptor> States { get; init; } = [];
    public IReadOnlyList<ExternalSurfaceDescriptor> ExternalSurfaces { get; init; } = [];
    public IReadOnlyList<WorkbenchDescriptor> Workbenches { get; init; } = [];
}
```

Localization is **out of scope**: names are strings supplied by the plugin in the host's configured locale.
A future `ILocalizedStrings` seam is noted (§8), not built — it has zero implementers today.

### 6.4 Wire contracts — `Arronix.Abstractions.Wire` (`ARX0017`)

**The placement rule, stated so it needs no further judgment:**

> A type lives in `Arronix.Abstractions` **iff** it crosses an **assembly-isolation boundary** — the plugin
> boundary or the client boundary. Everything else stays host-side.

This is the three-tier rule's clause 3 widened from "plugin boundary" to "assembly-isolation boundary", which
is a **recommended amendment to `docs/arronix-common-extraction-plan.md`**. The justification is
structural: `Arronix.Client` may reference nothing but Abstractions, so a mirrored type is a
guaranteed-divergence bug, and the wire types are **not duplicates** — `MediaKindDescriptor` *contains* the
`MediaShape` and the `FieldDescriptor`s the plugin declared. One declaration, three consumers.

```csharp
namespace Arronix.Abstractions.Wire;

public sealed record MediaKindDescriptor
{
    public required MediaKindId Kind { get; init; }
    public required string Name { get; init; }
    public required string PluralName { get; init; }
    public required MediaShape Shape { get; init; }
    public required IReadOnlyList<LevelPresentation> Levels { get; init; }
    public required PluginIntentSurface Intent { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }   // wire names
    public required PluginId Plugin { get; init; }
}

public sealed record LevelPresentation
{
    public required MediaLevelId Level { get; init; }
    public required string Name { get; init; }
    public required string PluralName { get; init; }
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
    /// <summary>DERIVED BY THE HOST from the validated shape, never declared by the plugin — a declaration
    /// that can be derived is a declaration that can disagree.</summary>
    public required IReadOnlyList<Affordance> Affordances { get; init; }
    public required IReadOnlyList<ActionDescriptor> Actions { get; init; }
}

public enum Affordance
{
    Monitorable = 0,   // the level has monitor dimensions
    Searchable = 1,    // the level carries AcquisitionUnit
    Refreshable = 2,   // LevelIdentity.HasCatalogRecord
    Renamable = 3,     // the level carries FileBearing and a rename policy is registered
    Removable = 4,     // the level carries LibraryEntry
    Browsable = 5,     // the level has a child
    Selectable = 6,    // the level carries VariantAxis
    Taggable = 7,      // the level carries LibraryEntry
    Relocatable = 8,   // the level carries LibraryEntry
    Downloadable = 9,  // the level carries AcquisitionUnit and an indexer is configured
}

/// <summary>The plugin's <see cref="ItemView"/> plus the host state and derivations it cannot own.</summary>
public sealed record ItemDetail
{
    public required ItemView Item { get; init; }
    public required MonitorState Monitor { get; init; }
    public IReadOnlyDictionary<string, string> MonitorChoices { get; init; }
        = new Dictionary<string, string>();
    public ProgressSummary? Progress { get; init; }
    public MediaItemRef? SelectedVariant { get; init; }
    public IReadOnlyList<string> StateIds { get; init; } = [];
    public IReadOnlyList<Affordance> Affordances { get; init; } = [];
    public int FileCount { get; init; }
    public long SizeOnDisk { get; init; }
}

public enum MonitorState { Unmonitored = 0, Monitored = 1, Partial = 2, NotApplicable = 3 }
public sealed record ProgressSummary(int Have, int Want, int Total, long SizeOnDisk);
public sealed record ItemDetailPage(IReadOnlyList<ItemDetail> Items, int Page, int PageSize, int TotalCount);

public sealed record ActionResult(
    bool Accepted, string? CorrelationId, string? Message, ValidationOutcome? Validation);

public sealed record PluginStatusView(
    string Id, string? Name, string? Version, string State,
    IReadOnlyList<string> Capabilities, int? ErrorCode, string? Message,
    IReadOnlyList<string> Defects, DateTimeOffset ChangedAt);

public sealed record JobView(
    string JobId, string Name, string Description, string Owner, string Schedule,
    DateTimeOffset? LastRun, DateTimeOffset? NextRun, bool LastSucceeded, int Priority);

public sealed record QueueEntryView(
    Guid EntryId, string JobId, string Owner, string CorrelationId, int Attempt,
    DateTimeOffset EnqueuedAt, DateTimeOffset NotBefore, string? MediaKind);

public sealed record HealthSnapshotView(
    string Status, DateTimeOffset CheckedAt, IReadOnlyList<HealthCheck> Checks);
```

**The event envelope — one tagged record, no polymorphic JSON:**

```csharp
public sealed record EventEnvelope
{
    public required EventKind Kind { get; init; }
    public required DateTimeOffset At { get; init; }
    public string? CorrelationId { get; init; }
    public MediaKindId? MediaKind { get; init; }
    public MediaItemRef? Item { get; init; }
    public string? PluginId { get; init; }
    public string? JobId { get; init; }
    public string? State { get; init; }
    public double? CompletedFraction { get; init; }
    public string? Message { get; init; }
    public RenderedSummary? Summary { get; init; }
    public HealthSnapshotView? Health { get; init; }
}

public enum EventKind
{
    ItemChanged = 0, JobProgress = 1, JobCompleted = 2, HealthChanged = 3,
    QueueChanged = 4, ActivityRaised = 5, PluginStateChanged = 6, ProviderStatusChanged = 7,
}
```

**Secret handling on the wire, mechanical:** any `SettingsField` whose `Sensitivity` is `Credential` or
`Secret` is never returned by a `GET`; it round-trips as the sentinel `"********"`, and a `PUT` carrying the
sentinel means "unchanged". One rule in one middleware, derived from the same declaration that drives
redaction and the UI.

### 6.5 The workbench — the escape hatch, designed rather than denied

**Both input designs under-deliver here, and it matters.** Manual import — assigning N loose files to M
units, with a plugin-supplied proposal (Lidarr solves it with the Hungarian algorithm in
`Identification/Munkres.cs`, 504 lines) — is a **daily core workflow in all four *arrs**, not an exotic
feature. Expressing it as "an action with a collection parameter of `unitId:fileId:ordinal` triples" is not
a user interface, and saying so honestly is better than shipping a vocabulary that cannot do the job.

The missing primitive is a **declared, editable row/column grid over a plugin-supplied proposal**. It ships
no code and no markup; it is one generic client component; and it turns out to cover three of the hardest
real screens (manual import, interactive release search, bulk edit).

```csharp
namespace Arronix.Abstractions.Intent;

public sealed record WorkbenchDescriptor
{
    public required string WorkbenchId { get; init; }        // "manual-import", "interactive-search"
    public required string Name { get; init; }
    public string? Description { get; init; }
    /// <summary>What the workbench operates on; drives where a front end offers it.</summary>
    public required WorkbenchSubject Subject { get; init; }
    public MediaLevelId? TargetLevelId { get; init; }
    /// <summary>Columns, in order. A read-only column is a projection; an editable one is a decision the
    /// user makes, with <see cref="FieldDescriptor.Choices"/> or an option source supplying the values.</summary>
    public required IReadOnlyList<WorkbenchColumn> Columns { get; init; }
    /// <summary>Parameters the front end must collect before requesting a proposal (a folder path, a query).</summary>
    public IReadOnlyList<ActionParameter> Inputs { get; init; } = [];
    public required string CommitLabel { get; init; }
    public required Consequence CommitConsequence { get; init; }
    public required ConfirmationRequirement CommitConfirmation { get; init; }
    public bool AllowsRowExclusion { get; init; } = true;
}

public enum WorkbenchSubject { LooseFiles = 0, ReleaseCandidates = 1, LibraryItems = 2 }

public sealed record WorkbenchColumn
{
    public required FieldDescriptor Field { get; init; }
    public bool Editable { get; init; }
    /// <summary>An id the front end resolves through
    /// <c>GET /api/v1/kinds/{kind}/workbenches/{id}/options/{sourceId}?row={rowId}</c>.
    /// Row-scoped, because the legal units for file A are not the legal units for file B.</summary>
    public string? OptionSourceId { get; init; }
}

/// <summary>Produced by the plugin, rendered generically, edited by the user, posted back.</summary>
public sealed record WorkbenchProposal
{
    public required string WorkbenchId { get; init; }
    public required IReadOnlyList<WorkbenchRow> Rows { get; init; }
    public IReadOnlyList<ValidationFailure> Issues { get; init; } = [];
}

public sealed record WorkbenchRow
{
    public required string RowId { get; init; }
    public required IReadOnlyDictionary<string, FieldValue> Values { get; init; }
    /// <summary>The plugin's own confidence in its proposal. Reuses the STABLE 0.1.0 enum.</summary>
    public MatchConfidence Confidence { get; init; }
    public bool IncludedByDefault { get; init; } = true;
    public IReadOnlyList<ValidationFailure> Issues { get; init; } = [];
}

public sealed record WorkbenchCommit(
    string WorkbenchId, IReadOnlyList<WorkbenchRow> Rows, IReadOnlyList<string> ExcludedRowIds);
```

The plugin-side seam is **one method on an interface it already implements** — no new capability, no new
registration:

```csharp
// added to Arronix.Abstractions.Shape.IMediaItemSource
Task<WorkbenchProposal> ProposeAsync(
    string workbenchId, IReadOnlyDictionary<string, string> inputs, CancellationToken ct = default);
Task<ActionResult> CommitAsync(WorkbenchCommit commit, CancellationToken ct = default);
```

**It passes the falsification test.** A CLI runs `arronix import propose --folder X` (a table),
`arronix import set --row 3 --unit tv:episode:412`, `arronix import commit`. A TUI renders a grid. A voice
assistant reads row summaries and confirms. A screen reader gets a real table with headers. Nothing in it
names a widget, a color or a component.

**What is still not expressible, stated as a limit rather than sold as a feature:**

1. **A genuinely bespoke visualization** — a listening graph, a reading-order tree with custom geometry.
   Not expressible, by design. The escape hatch is `ExternalSurfaceDescriptor`: the plugin may *point at* a
   page it hosts itself, which runs outside the client's origin and outside the capability model. It is
   explicitly **not** an embedding point.
2. **Cross-kind composite views** ("everything releasing this week across TV, movies and music"). The
   **host** composes these from per-kind descriptors; a plugin cannot declare one. Acceptable: a
   cross-cutting view is by definition not any one plugin's business.
3. **Free-form rich text with plugin-controlled formatting.** `FieldValueKind.MultilineText` carries plain
   text only. Deliberate: admitting markup would be the first UI leak and the first XSS surface.

### 6.6 REST + SignalR surface (`Arronix.Api`)

```text
GET    /api/v1/kinds                                         → MediaKindDescriptor[]  (summary projection)
GET    /api/v1/kinds/{kind}                                  → MediaKindDescriptor    (ETag; the UI's whole schema)
GET    /api/v1/kinds/{kind}/levels/{level}/items             → ItemDetailPage
                                                               ?parent=&axis=&q=&filter=&sort=&desc=&page=&size=
GET    /api/v1/kinds/{kind}/items/{id}                       → ItemDetail
GET    /api/v1/kinds/{kind}/items/{id}/children?axis=        → ItemDetailPage
POST   /api/v1/kinds/{kind}/actions/{actionId}               → ActionResult  (202 for LongRunning)
GET    /api/v1/kinds/{kind}/groups/{axisId}                  → ItemDetailPage
GET    /api/v1/kinds/{kind}/workbenches/{id}/proposal        → WorkbenchProposal
GET    /api/v1/kinds/{kind}/workbenches/{id}/options/{src}   → FacetValue[]   ?row=
POST   /api/v1/kinds/{kind}/workbenches/{id}/commit          → ActionResult
GET    /api/v1/providers                                     → ProviderDescriptor[]   ?family=&kind=
GET    /api/v1/providers/definitions                         → ProviderDefinition[]   (secrets elided)
POST   /api/v1/providers/definitions                         → ProviderDefinition
PUT    /api/v1/providers/definitions/{id}
DELETE /api/v1/providers/definitions/{id}
POST   /api/v1/providers/definitions/{id}/test               → ValidationOutcome
GET    /api/v1/providers/definitions/{id}/options/{src}      → FacetValue[]
GET    /api/v1/plugins                                       → PluginStatusView[]
GET    /api/v1/jobs        POST /api/v1/jobs/{id}/trigger    → JobView[] / ActionResult
GET    /api/v1/queue                                         → QueueEntryView[]
GET    /api/v1/health                                        → HealthSnapshotView
GET    /health                                               → HealthSnapshotView (200/200/503, §10)
HUB    /hub/events                                           → EventEnvelope
```

One SignalR hub, with groups per media kind so a client browsing TV receives no movie churn, plus a
`system` group for plugin/health/job traffic.

> **Deviation from ARCHITECTURE §7 phase 1, declared.** The legacy Sonarr-v3/v5 compatibility surface is
> **not built.** Per the product decision (new platform, no legacy upgrade path) there is no legacy data to
> serve, so a compatibility layer would be a shim over nothing. Phases 2–4 are built. **Recommend amending
> §7.**

`IntentSurfaceValidator` runs at load and rejects: duplicate action / axis / state / workbench ids; field
ids that do not resolve against the shape; `UriTemplate`s whose scheme is not `http`/`https` or whose
placeholders name unknown fields; `StateDescriptor.SourceFieldId` values that do not exist; `BrowseAxis`
references to unknown levels, grouping axes or sequence axes; and workbench columns whose
`FieldDescriptor.ValueKind` is `Artwork`. Failure → `CoreErrorCode.PluginShapeInvalid`.

### 6.7 The generic-over-shape client

```text
src/Arronix.Client/
├── Arronix.Client.csproj        Sdk="Microsoft.NET.Sdk.BlazorWebAssembly"
├── GlobalUsings.cs · Program.cs · App.razor · _Imports.razor
├── wwwroot/{index.html, manifest.webmanifest, service-worker.js, css/}   ← content owned by pwa-and-push.md
├── Services/{ArronixApiClient.cs, DescriptorCache.cs, EventStream.cs, ActionDispatcher.cs}
├── Rendering/{FieldValueView.razor, FieldValueEditor.razor, ToneMap.cs, ConsequenceMap.cs, AffordanceMap.cs}
├── Browse/{KindWorkspace.razor, AxisSelector.razor, ItemCollection.razor,
│           Presenters/{HierarchyPresenter,SequencePresenter,GroupingPresenter,FacetPresenter,FlatPresenter}.razor,
│           ItemCard.razor, ItemDetail.razor, VariantSelector.razor, Breadcrumb.razor, FilterBar.razor}
├── Workbench/{WorkbenchView.razor, WorkbenchGrid.razor, WorkbenchCell.razor}
├── Providers/{ProviderCatalog.razor, ProviderDefinitionList.razor, ProviderForm.razor, SettingsFieldEditor.razor}
└── Shared/{ActionMenu.razor, ConsequencePrompt.razor, StateIndicator.razor, ProgressFeed.razor,
           PluginStatusPanel.razor, HealthPanel.razor}
```

**The entire intent→component coupling is five lookup tables, and they exist only here:**

| Table | Entries | Maps |
|---|---:|---|
| `FieldValueView` / `FieldValueEditor` | 20 | `FieldValueKind` → display and editor markup |
| `ToneMap` | 5 | `StateTone` → CSS class |
| `ConsequenceMap` | 4 × 3 | `Consequence` × `ConfirmationRequirement` → confirmation behavior |
| `AffordanceMap` | 10 | `Affordance` → which control appears |
| presenter selection | 5 | `BrowseAxisKind` → presenter component |
| **Total** | **~44** | **and not one of them names a media concept** |

Because every discriminant is a **closed enum in Abstractions** and the client **compiles against
Abstractions**, an unhandled value is a `CS8509` and `TreatWarningsAsErrors` makes it a build failure. This
is where the typed-seams stance genuinely pays: the client cannot silently ignore a kind it has not
implemented.

Boot sequence: `DescriptorCache` fetches `/api/v1/kinds` once, caches each `MediaKindDescriptor` in the
service worker keyed by ETag, and every component takes `MediaKindDescriptor` + `ItemDetail` as parameters.

**The falsifiable claim this design makes:** adding a fifth media kind — a new field, a new enumeration
value, a new action, a new browse axis, a new grouping axis, a new coordinate space, a new format family —
requires **zero client changes and zero client rebuilds**. Only a genuinely new *interaction pattern* does,
and the workbench exists precisely to make that set as small as it honestly can be.

`ProviderForm.razor` renders any provider's settings from `SettingsField[]` alone, so indexers, download
clients, notifiers, metadata sources and import lists share one form implementation. `WorkbenchGrid.razor`
reuses `FieldValueEditor` per cell, so the workbench costs one component, not a screen per kind.

### 6.8 The §11 security win

Because plugin UI is **declared data and never code**, four properties hold that hold in no *arr today:

1. **No plugin code executes in the browser, ever.** The browser is therefore entirely outside the
   capability model — there is nothing to gate because there is nothing to run.
2. **A gated contract cannot reach the client.** `Arronix.Client` references `Arronix.Abstractions` only;
   Abstractions holds *contracts*, and every implementation (`ScopedHttpGateway`, `ScopedFileSystem`,
   `PartitionedRateLimiter`) lives in assemblies the client cannot reference. The topology, not a runtime
   check, is the enforcement — exactly §11's stated mechanism, extended to a second boundary.
3. **The host is the sole publisher of intent.** Declarations are serialized *by the host*, so the host
   withholds what a plugin has no right to: an action, workbench or affordance the plugin's capabilities do
   not support is rejected at load, not filtered at render time.
4. **Secrets are structurally unable to leave.** One `SettingSensitivity` declaration produces a
   serialization rule, a redaction rule and a UI intent.

The residual blast radius of a malicious intent declaration is **a misleading label**. An XSS-class attack
would require the client to render declaration strings unescaped — the client's own bug, in one place,
rather than one per plugin — and `IntentSurfaceValidator` bounds even that.

---

## 7. Work packages

Rules of partition: each WP owns **exactly one directory tree** (or a disjoint set of files within one), and
**no file is written by two WPs**. WPs in the same wave build in parallel. Every WP verifies with
`dotnet build <its own csproj>` (never the solution — it is not wired) to **zero warnings** under
`TreatWarningsAsErrors=true` / `AnalysisLevel=6.0-all`.

| WP | Owns (directory) | Files it creates or edits | Depends on |
|---|---|---|---|
| **WP-1** | `src/Arronix.Abstractions/` (governance files only) + `src/Arronix.Abstractions.Tests/Experimental/` + `docs/contracts/` | **Edits:** `Arronix.Abstractions.csproj` (`Version` 0.2.0→0.3.0, `Description`), `Experimental/ExperimentalContracts.cs` (add all five constants `Shape=ARX0013`, `Plugins=ARX0014`, `Providers=ARX0015`, `Intent=ARX0016`, `Wire=ARX0017`), `Health/CoreErrorCode.cs` (add 2003–2010, 3002), `Telemetry/TelemetryEmitterExtensions.cs` (**new**), `docs/contracts/stability.md` (id-table rows + a 0.3.0 version-history section), `src/Arronix.Abstractions.Tests/Experimental/ExperimentalSurfaceTests.cs` (five `expectedByNamespace` rows; **delete the phantom `Arronix.Abstractions.Quality.QualityEvaluation` entry**) | — |
| **WP-2** | `src/Arronix.Abstractions/Shape/` | `MediaLevelId.cs`, `MediaItemRef.cs`, `MediaFileId.cs`, `ExternalId.cs`, `OrdinalPath.cs`, `Coordinate.cs`, `CoordinateSpace.cs`, `CoordinateSet.cs`, `SequenceAxis.cs`, `MediaLevelRoles.cs`, `LevelIdentity.cs`, `MediaLevel.cs`, `VariantSelection.cs`, `MonitorDimension.cs`, `AcquisitionScope.cs`, `FileBinding.cs`, `GroupingAxis.cs`, `FormatFamily.cs`, `SelectionFacet.cs`, `FieldDescriptor.cs`, `FieldValue.cs`, `SearchTerm.cs`, `SearchKind.cs`, `MediaShape.cs`, `RenderedSummary.cs` (also `SummaryField`, `SummaryFieldWeight`, `ArtworkRef` — see the placement note in §6.3), `IMediaShapeProvider.cs`, `IReleaseMatcher.cs`, `IReleaseQueryPlanner.cs`, `IMediaItemSource.cs`, `ItemQuery.cs`, `ItemView.cs` (also `ItemPage`) | WP-1 |
| **WP-3** | `src/Arronix.Abstractions/Providers/` (**new files only**; the seven stable files are not touched) | `ProviderId.cs`, `ProviderFamily.cs`, `ProviderDescriptor.cs`, `ProviderDefinition.cs`, `ProviderInvocation.cs`, `IProviderSessionStore.cs`, `IProvider.cs`, `SettingsField.cs`, `ValidationOutcome.cs`, `DownloadProtocol.cs`, `IIndexer.cs`, `IndexerProfile.cs`, `SearchProfile.cs`, `INotifier.cs`, `NotificationMessage.cs`, `IMetadataSource.cs`, `SelectionPolicy.cs`, `IImportListSource.cs`, `IDownloadClient.cs`, `Registrations.cs`, `ProviderEvents.cs` | WP-2 |
| **WP-4** | `src/Arronix.Abstractions/Intent/` | `ActionDescriptor.cs`, `BrowseAxis.cs`, `SortOption.cs`, `FilterOption.cs`, `StateDescriptor.cs`, `RenderedSummary.cs`, `ExternalSurfaceDescriptor.cs`, `WorkbenchDescriptor.cs`, `WorkbenchProposal.cs`, `PluginIntentSurface.cs` | WP-2 |
| **WP-5** | `src/Arronix.Abstractions/Plugins/` | `PluginId.cs`, `Capability.cs`, `CapabilityNames.cs`, `CapabilitySet.cs`, `IPluginModule.cs`, `IPluginContext.cs`, `IPluginRegistry.cs`, `PluginCapabilityException.cs`, `WellKnownJobResults.cs` | WP-2, WP-3, WP-4 |
| **WP-6** | `src/Arronix.Abstractions/Wire/` | `MediaKindDescriptor.cs`, `LevelPresentation.cs`, `Affordance.cs`, `ItemDetail.cs`, `ProgressSummary.cs`, `ActionResult.cs`, `PluginStatusView.cs`, `JobView.cs`, `QueueEntryView.cs`, `HealthSnapshotView.cs`, `EventEnvelope.cs` | WP-2, WP-4 |
| **WP-7** | `src/Arronix.Plugins/` | `Arronix.Plugins.csproj`, `GlobalUsings.cs`, `Composition/ArronixPluginsServiceCollectionExtensions.cs`, `Configuration/PluginRuntimeOptions.cs`, `Manifest/{PluginManifest,ContractRequirements,PolicyGraph,PluginManifestReader,PluginManifestValidator}.cs`, `Manifest/plugin.schema.json`, `Versioning/{SemanticVersion,VersionRange,VersionRangeParser}.cs`, `Loading/{PluginLoadContext,PluginIsolationException,PluginReferenceInspector,PluginLoader,PluginCandidate,PluginLoadResult,PluginState}.cs`, `Registration/{PluginRegistry,PluginRegistrationLedger,CapabilityMatrix,PluginContext}.cs`, `Scoping/{ScopedFileSystem,ScopedHttpGateway,PartitionedCacheProvider,PartitionedRateLimiter,FilteredEventPublisher,PluginPaths,PluginTelemetryEmitter}.cs`, `Registry/{IPluginRuntimeRegistry,PluginRuntimeRegistry,TokenRegistry}.cs` | WP-5, WP-6 |
| **WP-8** | `src/Arronix.Host/` (**root, composition, configuration, media, storage, runtime, filesystem**) | `Arronix.Host.csproj`, `GlobalUsings.cs`, `Composition/ArronixHostServiceCollectionExtensions.cs` (**the only file naming every subsystem — written once, here, with all five call lines**), `Composition/{MediaRegistration,StorageRegistration}.cs`, `Configuration/{HostOptions,LibraryOptions}.cs`, `Runtime/{PluginBootstrapper,PluginRuntimeState}.cs`, `Media/{IMediaKindRegistry,MediaKindRegistry,RegisteredMediaKind,ValidatedShape,ShapeDefect,MediaKindProjection,AffordanceCalculator,CompletenessCalculator}.cs`, `Storage/{IMediaStore,InMemoryMediaStore,LibraryFacet,UnitFileLink,GroupMembership,MediaFileRecord}.cs`, `FileSystem/HostFileSystem.cs`, `Intent/{IIntentRegistry,IntentRegistry,IntentSurfaceValidator,WorkbenchBroker}.cs` | WP-7 |
| **WP-9** | `src/Arronix.Host/Scheduling/` + `src/Arronix.Host/Composition/SchedulingRegistration.cs` + `src/Arronix.Host/Configuration/SchedulerOptions.cs` | `JobScheduler.cs`, `JobQueue.cs`, `JobQueueEntry.cs`, `BackgroundTaskRegistry.cs`, `JobSchedule.cs`, `JobScheduleParser.cs`, `ConcurrencyGovernor.cs`, `BackoffLadder.cs`, `FailureClass.cs`, `FailureClassifier.cs`, `CorrelationContext.cs` | WP-8 |
| **WP-10** | `src/Arronix.Host/Providers/` + `src/Arronix.Host/Health/` + `Composition/{ProviderRegistration,HealthRegistration}.cs` + `Configuration/HealthOptions.cs` | `Providers/{ProviderRegistry,ProviderDefinitionStore,ProviderStatusStore,ProviderTestService,ProviderSessionStore,IndexerDispatcher,NotificationDispatcher}.cs`, `Health/{IHealthAggregator,HealthAggregator,HealthSnapshot,PluginHealthContributor,SchedulerHealthContributor,ProviderHealthContributor}.cs` | WP-8 |
| **WP-11** | `src/Arronix.Api/` | `Arronix.Api.csproj` (`Microsoft.NET.Sdk.Web`, **zero `PackageReference`**), `GlobalUsings.cs`, `Program.cs`, `appsettings.json`, `Configuration/ApiOptions.cs`, `Endpoints/{KindEndpoints,ItemEndpoints,ActionEndpoints,WorkbenchEndpoints,ProviderEndpoints,PluginEndpoints,JobEndpoints,QueueEndpoints,HealthEndpoints}.cs`, `Hubs/EventHub.cs`, `Hubs/EventBroadcaster.cs`, `Serialization/ApiJsonOptions.cs`, `Security/SecretRedactionFilter.cs`, `Hosting/ClientStaticFiles.cs` | WP-9, WP-10 |
| **WP-12** | `src/Arronix.Client/` | `Arronix.Client.csproj` (`Microsoft.NET.Sdk.BlazorWebAssembly`; **must set `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` and clear `<RuntimeIdentifiers/>`** — see §7.6), `GlobalUsings.cs`, `Program.cs`, `App.razor`, `_Imports.razor`, `wwwroot/*`, `Services/*`, `Rendering/*`, `Browse/*`, `Workbench/*`, `Providers/*`, `Shared/*` (full list in §6.7) | WP-6 |
| **WP-13** | `src/Arronix.Plugin.Movies/` | `Arronix.Plugin.Movies.csproj` (Abstractions only, zero packages), `GlobalUsings.cs`, `plugin.json`, `MoviesPluginModule.cs`, `MoviesShape.cs`, `MoviesIntent.cs`, `MoviesItemSource.cs`, `MoviesReleaseParser.cs`, `MoviesReleaseMatcher.cs`, `MoviesQueryPlanner.cs`, `Seed/MoviesSeed.cs` | WP-5 |
| **WP-14** | `src/Arronix.Plugin.Books/` | same file shape, prefix `Books` — **proves `VariantAxis`, `GroupingAxis` (M:N, RefCounted, Labeled position, primary member), two `FormatFamily` ladders, `Threshold`/`Flag` selection facets, `UnitSpansManyFiles` with `OrdinalIsMeaningful`** | WP-5 |
| **WP-15** | `src/Arronix.Plugin.Music/` | same file shape, prefix `Music` — **proves four levels, `AnchorLevelId != UnitLevelId`, variant selection with `OnImport` trigger, variant-relative completeness, a `SequenceAxis` without a policy record** | WP-5 |
| **WP-16** | `src/Arronix.Plugin.Tv/` | same file shape, prefix `Tv` — **proves four coordinate spaces (one canonical, one provenance-sensitive + may-be-unverified), a `SequenceAxis` with a policy record and a `SequenceException`, a `SpanConstraint`, `SequenceSpan`/`Ancestor` acquisition scopes, 1-file:N-units** | WP-5 |
| **WP-17** | `src/Arronix.Plugins.Tests/` | `Arronix.Plugins.Tests.csproj`, `GlobalUsings.cs`, `Loading/{PluginLoadContextTests,PluginReferenceInspectorTests,PluginLoaderPipelineTests}.cs`, `Manifest/{ManifestReaderTests,ManifestValidatorTests}.cs`, `Versioning/{SemanticVersionTests,VersionRangeParserTests,ExperimentalGateTests}.cs`, `Registration/{CapabilityMatrixTests,BidirectionalCapabilityTests,RegistryAdmissionTests}.cs`, `Scoping/{ScopedFileSystemTests,ScopedHttpGatewayTests,PartitionedCacheTests}.cs` | WP-7 |
| **WP-18** | `src/Arronix.Host.Tests/` | `Arronix.Host.Tests.csproj`, `GlobalUsings.cs`, `Media/{ValidatedShapeTests,ShapeDefectCorpusTests,ReferenceShapeTests,AffordanceCalculatorTests,CompletenessCalculatorTests}.cs`, `Storage/{InMemoryMediaStoreTests,UnitFileLinkInvariantTests,VariantSelectionInvariantTests}.cs`, `Scheduling/{JobScheduleParserTests,ConcurrencyGovernorTests,BackoffLadderTests,FailureClassifierTests}.cs`, `Health/HealthAggregatorTests.cs`, `Providers/{ProviderStatusStoreTests,IndexerEligibilityTests}.cs` | WP-9, WP-10, WP-13..16 |
| **WP-19** | `src/Arronix.Api.Tests/` | `Arronix.Api.Tests.csproj`, `GlobalUsings.cs`, `ApiProjectShapeTests.cs` (**no `.razor` under `src/Arronix.Api`; the `PackageReference` set is empty**), `Serialization/WireRoundTripTests.cs` (every `FieldValue` kind, `Coordinate` kind and `EventKind` round-trips byte-identically under the server's and the client's `JsonSerializerOptions`), `Endpoints/{KindEndpointTests,ItemEndpointTests,SecretRedactionTests}.cs` | WP-11, WP-12 |
| **WP-20** | `src/Arronix.Abstractions.Tests/` (**new files only** — `Experimental/ExperimentalSurfaceTests.cs` belongs to WP-1) | `Governance/MediaNeutralityTests.cs` (invariant 1, over exported types **and members** of Abstractions, Common, Plugins, Host and Api — never plugin assemblies), `Governance/IntentVocabularyTests.cs` (the UI-technology deny-list), `Governance/ProjectTopologyTests.cs` (`Arronix.Client` and each `Arronix.Plugin.*` declare exactly one `ProjectReference` on `Arronix.Abstractions`; media plugins declare zero `PackageReference`s; no Arronix assembly named in a plugin's `GetReferencedAssemblies()` except Abstractions), `Governance/ContractAreaTests.cs` (every §7.2 area has a constant, a `stability.md` row and an `expectedByNamespace` row) | WP-1, WP-12, WP-13..16 |

### 7.1 Build waves (parallelism)

```
Wave 0:  WP-1
Wave 1:  WP-2
Wave 2:  WP-3   WP-4                                   (parallel)
Wave 3:  WP-5   WP-6                                   (parallel)
Wave 4:  WP-7   WP-12   WP-13   WP-14   WP-15   WP-16  (parallel — six disjoint trees)
Wave 5:  WP-8
Wave 6:  WP-9   WP-10   WP-17                          (parallel)
Wave 7:  WP-11
Wave 8:  WP-18  WP-19   WP-20                          (parallel)
```

Six work packages build simultaneously in wave 4. The only serialization points are WP-1 (the five
governance edits, which every contract area would otherwise contend on) and WP-8 (the host composition
root, which every host subsystem would otherwise contend on) — both written **once, in full**, so the
subsystem WPs add only their own `Composition/<Subsystem>Registration.cs`, exactly as `Arronix.Common`
already does.

### 7.2 Contract-governance edits (WP-1, four coordinated changes per area)

| Id | Area | Namespace |
|---|---|---|
| `ARX0013` | Media shape | `Arronix.Abstractions.Shape` |
| `ARX0014` | Plugin model | `Arronix.Abstractions.Plugins` |
| `ARX0015` | Provider model (**additive to a currently all-stable namespace**) | `Arronix.Abstractions.Providers` |
| `ARX0016` | Interface intent | `Arronix.Abstractions.Intent` |
| `ARX0017` | Wire contracts | `Arronix.Abstractions.Wire` |

For each: (1) add the constant to `Experimental/ExperimentalContracts.cs`; (2) apply
`[Experimental(ExperimentalContracts.<Area>, UrlFormat = ExperimentalContracts.UrlFormat)]` to every new
type; (3) add the id-table row **and** a 0.3.0 version-history entry to `docs/contracts/stability.md`;
(4) add the namespace→id row to `ExperimentalSurfaceTests.expectedByNamespace`. `ARX0015` is required
specifically because `Arronix.Abstractions.Providers` currently contains **only stable types**, so adding an
experimental one fails `EveryExperimentalTypeUsesTheDiagnosticIdOfItsOwnNamespace` until that row exists.

Consumers opt in **per file** with `#pragma warning disable ARXnnnn` immediately above the `using`, with a
comment saying why — the pattern already established in `Arronix.Common`. A project-wide `NoWarn` for an
`ARXnnnn` id is **forbidden by policy** and must never be used to clear a build.

### 7.3 Projects, references and packages

| Project | SDK | Version | ProjectReferences | PackageReferences (all already registered) |
|---|---|---|---|---|
| `Arronix.Abstractions` | `Microsoft.NET.Sdk` | **0.2.0 → 0.3.0** | *(none)* | **none — invariant, mechanically asserted** |
| `Arronix.Common` | `Microsoft.NET.Sdk` | 0.1.0 | Abstractions | *unchanged* — `ProjectShapeTests` asserts the exact 8-package set |
| `Arronix.Plugins` | `Microsoft.NET.Sdk` | 0.1.0 | Abstractions, Common | `Microsoft.Extensions.{DependencyInjection.Abstractions, Logging.Abstractions, Options, Options.DataAnnotations, Options.ConfigurationExtensions, Configuration.Abstractions, Configuration.Binder}` |
| `Arronix.Host` | `Microsoft.NET.Sdk` | 0.1.0 | Abstractions, Common, Plugins | the above **+** `Microsoft.Extensions.{Hosting.Abstractions, Caching.Memory, Diagnostics.Abstractions}`, `System.Threading.RateLimiting` |
| `Arronix.Api` | **`Microsoft.NET.Sdk.Web`** | 0.1.0 | Abstractions, Common, Plugins, Host | **none** — SignalR server, static files, hosting and configuration are all in the `Microsoft.AspNetCore.App` shared framework |
| `Arronix.Client` | **`Microsoft.NET.Sdk.BlazorWebAssembly`** | 0.1.0 | **Abstractions ONLY** | `Microsoft.AspNetCore.Components.WebAssembly`, `…WebAssembly.DevServer`, `Microsoft.AspNetCore.SignalR.Client` |
| `Arronix.Plugin.{Tv,Movies,Music,Books}` | `Microsoft.NET.Sdk` | 0.1.0 | **Abstractions ONLY** | **none** |
| `Arronix.{Plugins,Host,Api}.Tests` | `Microsoft.NET.Sdk` | — | the matching production project | mirror `Arronix.Common.Tests.csproj` exactly; `<TestProject>true</TestProject>` explicit; `Host.Tests` adds `Microsoft.Extensions.TimeProvider.Testing` |

**No new package registration is requested.** `Microsoft.NET.Sdk.Web` and
`Microsoft.NET.Sdk.BlazorWebAssembly` are SDKs, not packages. OpenAPI/Swagger is deferred (§8), which
removes the only package request either input design made.

Every new `.csproj` mirrors `src/Arronix.Common/Arronix.Common.csproj` exactly: explicit `TargetFramework`
`net10.0`, `RootNamespace`, `AssemblyName`, `Version`, `Description`, `Nullable=enable`,
`LangVersion=latest`, `EnforceCodeStyleInBuild=true`, **plus the three-line Sentry opt-out `PropertyGroup`
with its explanatory comment**. Every project gets a `GlobalUsings.cs` with the four System usings; test
projects add `global using NUnit.Framework;`.

### 7.4 Conventions (non-negotiable, from `arronix-current-state.md` §7)

File-scoped namespaces; `ImplicitUsings` off; `ArgumentNullException.ThrowIfNull` /
`ArgumentException.ThrowIfNullOrWhiteSpace` at every public entry; collection expressions (`[]`);
`TryAdd*` / `TryAddEnumerable`; `TimeProvider` injected, never `DateTime.UtcNow` in implementation code;
`sealed record` for DTOs, `readonly record struct` for identities; configuration under the `Arronix:`
prefix with one `SectionName` const per options type, registered through `AddValidatedOptions<T>`; and XML
docs whose `<remarks>` explains **why the seam exists** — that voice is consistent across every 0.2.0
contract and is part of the house style.

### 7.5 Governance tests added

| Test | Asserts | WP |
|---|---|---|
| `ExperimentalSurfaceTests` | five new `expectedByNamespace` rows; phantom `QualityEvaluation` removed | WP-1 |
| `ProjectTopologyTests` | `Arronix.Client` and each `Arronix.Plugin.*` declare exactly one `ProjectReference` (Abstractions) and zero packages (plugins); reflection over `GetReferencedAssemblies()` finds no other Arronix assembly | WP-20 |
| `ApiProjectShapeTests` | zero `.razor`/`.razor.cs` under `src/Arronix.Api`; the `PackageReference` set is **empty** | WP-19 |
| `MediaNeutralityTests` | no exported type or member of Abstractions/Common/Plugins/Host/Api contains `Series`, `Episode`, `Season`, `Movie`, `Album`, `Track`, `Artist`, `Book`, `Author`, `Edition`, `NzbDrone` or `Sonarr` (word-boundary, explicit empty allow-list). Plugin assemblies are excluded by design | WP-20 |
| `IntentVocabularyTests` | no exported type or member of Abstractions contains `razor`, `blazor`, `component`, `html`, `css`, `widget`, `render`, `button`, `checkbox`, `dropdown`, `modal`, `dialog`, `popup`, `icon`, `color`, `pixel`, `font`, `card`, `badge`, `tooltip`, `panel`, `sidebar`, `navbar`, `toast`, `spinner`. Allow-list: `ILibraryLayout`, `LibraryPathSpec` (both mean *folder* layout), `RenderedSummary` (a noun for plugin-rendered text, not a verb) | WP-20 |
| `PluginLoadContextTests` | an ALC asked for `Arronix.Common` **throws**; asked for `Arronix.Abstractions` returns `null` so the Default context wins (the silent-double-load bug) | WP-17 |
| `PluginReferenceInspectorTests` | a fixture assembly referencing `Arronix.Common` is rejected at discovery, before any ALC exists | WP-17 |
| `BidirectionalCapabilityTests` | registering a gated contract without its capability throws; declaring a capability with no matching registration fails the forward check | WP-17 |
| `CapabilityMatrixTests` | every gated contract in `CapabilityMatrix` has an `IPluginRegistry` method, and every gated `IPluginRegistry` method has a matrix row | WP-17 |
| `ReferenceShapeTests` | the four §2.12 shapes validate clean and reproduce the acceptance table cell for cell | WP-18 |
| `ShapeDefectCorpusTests` | ~30 malformed shapes each produce the expected `ShapeDefect` and only that one | WP-18 |
| `UnitFileLinkInvariantTests` | the store rejects a second file for a `AtMostOneFilePerUnit` unit, a second unit for a `AtMostOneUnitPerFile` file, an ordinal on a binding where `OrdinalIsMeaningful` is false, and a link violating a `SpanConstraint` | WP-18 |
| `WireRoundTripTests` | every `FieldValue` kind, `Coordinate` kind and `EventKind` round-trips byte-identically under the server's and the client's `JsonSerializerOptions` — the guard against the one duplication the client/server split forces | WP-19 |

### 7.6 Build risks to budget for

1. **`RuntimeIdentifier` pollution (blocking, WP-12).** `src/Directory.Build.props` assigns
   `RuntimeIdentifier = <host RID>` whenever one is not specified, and `RuntimeIdentifiers` does not list
   `browser-wasm`. `Arronix.Client.csproj` **must** set `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>`
   and `<RuntimeIdentifiers></RuntimeIdentifiers>` explicitly. Verify with `dotnet publish` early, not at the
   end of the WP.
2. **Analyzer noise in Razor-generated code (WP-12).** `TreatWarningsAsErrors=true` +
   `AnalysisLevel=6.0-all` against generated partials will surface CA1707/CA2007/CS8618. Plan a **targeted,
   documented, per-project `<NoWarn>`** listing each id with a one-line reason — never a blanket one, and
   never an `ARXnnnn` id.
3. **Output paths.** Non-`Sonarr*` projects build to `_temp/bin/$(Configuration)/$(MSBuildProjectName)/`;
   `*.Tests` land in `_tests/` because `SonarrOutputType` keys on `.Contains('.Test')`. The Api's
   `Arronix:Api:ClientRoot` default must account for this.
4. **`GenerateDocumentationFile=true`** is on for every project and `IDE0005` is enforced on build, so every
   new public type needs XML docs (CS1591 is suppressed, but IDE0005 unused-usings is not).

---

## 8. Deferred, with the trigger that un-defers it

| Deferred | Where it would live | Trigger |
|---|---|---|
| **Persistence / relational `IMediaStore`** | `Arronix.Host/Storage/` replaced wholesale | The Storage-Layer milestone. `UnitFileLink(unit, file, ordinal?)` and `LibraryFacet` are fixed **now** so the schema cannot be biased toward one *arr's FK direction; only the implementation moves. |
| **Promotion of `IMediaStore` to Abstractions** | `Arronix.Abstractions` | The first plugin that must persist its own domain entities. In this milestone plugins are stateless projections (§2.11) — the cheapest way to learn the store's real shape before freezing it. |
| **Policy Engine (`IPolicyStep`, `IPolicyChain`, chain runner)** | already deferred by extraction plan §9 | Completes §4.1 validation step 4 exactly, by adding one `IPluginRegistry.AddPolicyStep` method and one ledger comparison. No redesign. |
| **`IJobFailureClassifier` + `FailureClass` promotion** | `Arronix.Host/Scheduling/` today | A plugin needing classification the `ResultData` bag cannot express — e.g. classifying an exception it does not itself throw. |
| **A typed `FailureClass` enum in Abstractions** | `Arronix.Abstractions/Scheduling/` | The first job whose failure the five surveyed classes cannot express. Until then `WellKnownJobResults`' two keys carry it and `Arronix.Host/Scheduling/FailureClassifier` owns the vocabulary (§4.7). Deferred because the vocabulary is unsettled, **not** because the record is frozen — below 1.0.0 it is not. *(This row replaces "a correlation-bearing job context", which is no longer deferred: `JobExecutionContext` carries `CorrelationId`, `Progress`, `MediaKind` and `Items` as typed members and `WellKnownJobParameters` is deleted.)* |
| **`cron` schedules** | `Arronix.Host/Scheduling/JobScheduleParser` | A user requirement no `every`/`daily` form can express. Purely additive to the grammar. |
| **`ILibraryBackend` + `Capability.LibraryBackend`** | not built | A plugin that delegates file layout, format conversion and identifier enrichment to a third-party library manager (Readarr's Calibre). Real evidence, zero implementers today; withholding it costs nothing because `IImportPipeline` still works. |
| **`IProviderDefinitionSource` + `Capability.ProviderDefinitions`** | not built | A plugin shipping a versioned data-defined provider pack (Cardigann, 72+ indexers from YAML). The `SettingsField` schema such a source would emit already exists, so this is one interface and one enum member. |
| **`Capability.Process`, `Capability.Distribution`** | not built | A gated contract that needs them. Omitted so the forward capability check ("every declared capability has a matching registration") stays exact. |
| **A settings-schema attribute (`[FieldDefinition]`)** | not built | **Never.** A plugin's reflection over its own types crosses no boundary and needs no contract. |
| **Catalog refresh under a `SelectionPolicy`** | the contract exists (§5.6); the *pipeline* does not | The Metadata milestone. The facets it consumes ship now as declared data, so the shape does not have to change when the pipeline lands. |
| **`SemanticVersion` / `VersionRange` promotion** | `Arronix.Plugins/Versioning/` | Never, unless a plugin must parse ranges — which would itself need review. |
| **`IMediaKindRegistry`, `IHealthAggregator`, `ProviderStatusStore` promotion** | `Arronix.Host/` | A second implementation, or a plugin consumer. None exists. |
| **Plugin unload / hot reload** | `PluginLoadContext` is already `isCollectible: true` | ARCHITECTURE §15. Collectibility costs nothing now; unload is neither implemented nor tested in this milestone, and that is recorded rather than implied. |
| **OpenAPI / Swagger document** | `Arronix.Api` | A registered serving package (`Swashbuckle.AspNetCore.Swagger` + `.SwaggerUI`, or `Microsoft.AspNetCore.OpenApi`). Verified: `SwaggerGen` alone cannot serve a document, and none of the serving packages is registered. Dropping it removes the only package request either input design made. |
| **Legacy Sonarr-compatibility API (ARCH §7 phase 1)** | not built | **Never** — new platform, no legacy upgrade path. Recommend amending §7. |
| **`ILocalizedStrings`** | not built | A second locale. Names are plugin-supplied strings in the host's configured locale until then. |
| **Branching level hierarchies** | `ValidatedShape` rule 4 | A media kind whose levels genuinely branch. Relaxing the rule is a **validator** change, not a contract change — `MediaLevel.Parent` already carries the freedom. |
| **Cross-level acquisition validation** | `ValidatedShape` | A kind where one release satisfies units at two different levels simultaneously. Representable today in `MatchOutcome.Units`; simply unvalidated. |
| **A Roslyn analyzer for invariant 1 (`ARX1001`)** | a new analyzer project | `MediaNeutralityTests` catches it at build; an analyzer would catch it at *edit* time. The right long-term answer, not this milestone's. |
| **Signed plugin packages, remote registry, WASM/process isolation** | — | ARCHITECTURE §15. |

---

## 9. Deviations from `ARCHITECTURE.md` — the complete list

| # | § | Deviation | Justification |
|---|---|---|---|
| 1 | 4 | Plugins receive a closed `IPluginRegistry`, never `IServiceCollection` or "service registrations" | Abstractions may take no packages, so the container is unreachable from a contract; admission beats filtering for §11 |
| 2 | 4.1 | `tokens[]` is `NamingToken[]`, not `string[]` (bare strings are widened) | The stable `NamingToken` DTO exists and nothing returns it; §9 needs the description and example |
| 3 | 4.1 | `PolicyGraph` carries five categories | §4.2 names five; the §4.1 example omits `naming` — **recommend amending the example** |
| 4 | 4.1 step 4 | Policy cross-check is self-consistency plus a token cross-check | The Policy Engine is deferred; the completion condition is named and costs one method |
| 5 | 4.1 step 5 | Token collisions **decided**: host-global → reject; cross-kind → allow; same-kind → cannot arise | §9 says "under evaluation"; a loader cannot ship without a rule |
| 6 | 5 | `IMediaStore` held host-side; the plugin-side seam is `IMediaItemSource` | Promotion rule: zero plugin implementers for a store, four for a source |
| 7 | 6 | Failure classification rides the stable `ResultData` bag rather than a new contract | Zero contract cost against zero implementers; promotion is additive |
| 8 | 6 | Schedule grammar defined as `manual` / `startup` / `every <duration>` / `daily <HH:mm>`; **not cron** | No surveyed *arr uses cron; adding it later is additive |
| 9 | 7 | Phase 1 legacy compatibility surface **dropped** | No legacy upgrade path in this platform — **recommend amending §7** |
| 10 | 8 | The contract-range grammar is a documented **subset** of npm/yarn (no `^`, `~`, wildcards or hyphen ranges) | A range grammar is itself an unversioned contract; every form admitted must be parsed identically forever |
| 11 | 11 | Capabilities are a closed `Capability` enum; the stable stringly-typed `IMediaKind.Capabilities` becomes a host-built **projection** | Plugins never implement `IMediaKind`; extend, do not fork |
| 12 | 11 | Least privilege is a closed registration surface **plus static reference-graph validation**, not DI registration filtering | Strictly stronger and mechanically checkable before any plugin code runs |
| 13 | 15 | "Dynamic UI component registry" **rejected permanently**; declarative intent instead | A component registry puts plugin code outside the capability model and welds the UI technology into the plugin contract — **recommend amending §15** |
| 14 | 3 | `Arronix.Web` (implied by the `Directory.Packages.props` comment) is `Arronix.Api` + `Arronix.Client` | Product-owner decision; invariant 4 |

**Recommended amendment to `docs/arronix-common-extraction-plan.md`:** the three-tier rule's clause 3 should
read *"the type physically must cross an **assembly-isolation** boundary"* rather than *"the plugin
boundary"*. `Arronix.Client` may reference only `Arronix.Abstractions`, which is structurally identical to a
plugin, and the wire contracts in §6.4 qualify under the amended reading and only under it.

