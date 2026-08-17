# Adversarial review: is the Arronix media abstraction sufficient?

**Subject:** `src/Arronix.Plugin.Movies` (15,936 lines of C# across 16 files, plus a 68-line manifest), reviewed
against `_reference/Radarr/src/NzbDrone.Core` and against the contracts in `src/Arronix.Abstractions`.

**Scope of this document.** It reports where the abstraction failed. It does not review code quality, and it
does not praise. Where the abstraction held, that is stated in one line and moved past.

**The single most important distinction in this document** is between *"the abstraction is wrong"* — a
contract that cannot express something real, and that will be wrong for TV, music and books too — and
*"the abstraction is fine, this plugin is incomplete"*. They are filed in separate sections and never mixed.
A finding in §3 is a contract bug. A finding in §4 is work.

---

## 0. Headline

**The abstraction can describe a movie manager completely and cannot operate one at all.**

Fourteen declared actions (`MoviesIntent.cs:117-160`) are unperformable: `IMediaItemSource`
(`Arronix.Abstractions/Shape/IMediaItemSource.cs`) has exactly two write members, both bound to a
`WorkbenchId`, and no contract anywhere takes an `ActionId`. `ActionEndpoints.cs:132` answers 501 by design.
The one write path that *is* reachable — `CommitAsync` — turns out to be validation-only in practice
(`MoviesItemSource.cs:325-365` returns "N row(s) are coherent and ready to apply"; `WorkbenchEndpoints.cs:177-189`
returns 202 and nothing applies anything). So the platform can render a complete movie manager and cannot
search, refresh, rescan, monitor, rename, add, remove or exclude a single film.

That is a contract gap, not plugin incompleteness: there is no member the plugin could have implemented.

The second-order damage is worse than the missing verb. Because the workbench is the only executable seam,
two of the four declared workbenches (`add-from-catalog`, `complete-collection`, `MoviesIntent.cs:858-948`)
exist only because a grid has a dispatch path and an action does not. The workbench primitive is being used
as a general-purpose action carrier, and its own semantics — one `Consequence` for the whole commit, no
scoping to a pre-made selection — are wrong for that job.

---

## 1. Method

Read in full: all 16 plugin source files; `Arronix.Abstractions/{Shape,Intent,Providers,Naming,Quality,Parsing,Plugins,DTOs}`;
`Arronix.Api/Endpoints/{Action,Workbench,Item}Endpoints.cs`; `Arronix.Host/Media/ShapeValidationRules.cs`.
Read in Radarr: `Movies/**` (3,721 lines), `MovieStats/**`, `MetadataSource/SkyHook/SkyHookProxy.cs`,
`MediaFiles/{MovieFile.cs,MovieImport/**}`, `Organizer/**`, `Parser/**`, `Qualities/**`,
`DecisionEngine/Specifications/RssSync/AvailabilitySpecification.cs`.

Cross-checked against the three sibling plugins (`Arronix.Plugin.Tv` 5,896 lines, `Arronix.Plugin.Music`
4,103, `Arronix.Plugin.Books` 4,391) to test leak claims — a thing is only "leaked" if a sibling has
written it too, or would have to.

---

## 2. What Radarr does that this plugin structurally cannot

Each item names the Radarr behaviour, the contract that blocks it, and what the plugin does instead.

### 2.1 A collection is not a thing. It is two strings on a movie. — **BLOCKED, severe**

Radarr's `MovieCollection` (`Movies/Collections/MovieCollection.cs:15-29`) is a row carrying
`Title, CleanTitle, SortTitle, TmdbId, Overview, Monitored, QualityProfileId, RootFolderPath, SearchOnAdd,
MinimumAvailability, LastInfoSync, Images, Added, Tags`. `RefreshCollectionService.SyncCollectionMovies`
(`Movies/RefreshCollectionService.cs:124-157`) reads six of those to add missing members with the
*collection's* profile, *collection's* root folder and *collection's* minimum availability.

What blocks it: `GroupingAxis` (`Arronix.Abstractions/Shape/GroupingAxis.cs:388-447`) declares that a group
exists, is monitorable, has its own metadata and outlives its members — and declares nothing about *what*
that metadata is. There is no `Fields`, no `ExternalIds`, no identifier type, and `IsMonitorable` is one
bool covering six different pieces of state.

Consequences, all present in the code:

| Consequence | Evidence |
|---|---|
| Collection fields are a naming convention in a static class the shape never sees | `MoviesShape.cs:266-294` (`MoviesCollectionFields`, 9 constants, 47 lines) |
| A collection cannot be a `MetadataNode`, so a collection fetch returns its members as flat roots | `Providers/MoviesCataloger.cs:1505-1508`, `:1021` |
| A collection has no `MediaItemRef`, so `ResolveExternalAsync("tmdb-collection:119")` returns *the earliest member* | `MoviesItemSource.cs:265-296` — the method's own doc calls this "a guess dressed as an answer" |
| A "collection was completed" notification has no `DeepLink` | `Providers/MoviesNotificationRenderer.cs` (`RenderCollection`) |
| The collection external-surface link is declared at the *movie* level | `MoviesIntent.cs:753-760` |
| Nothing enumerates the groups on an axis, so "show me my collections" cannot be built | no contract member returns groups; `ItemView` reports no membership |
| The group key has no defined form, so the source accepts *either* the TMDb id or the title and guesses | `MoviesItemSource.cs` `IsInCollection` |
| `collection.refresh` is declared `ActionScope.Kind` because no scope names a group | `MoviesIntent.cs:461-465` |
| `complete-collection` declares `TargetLevelId = movie` while being about a collection | `MoviesIntent.cs:907-913` |
| The `tmdb-collection` scheme is emitted on `ItemView.ExternalIds` and in the manifest but is *not* on the level's declaration | `MoviesShape.cs:725-729` vs `plugin.json:10` |

This is one root cause with eleven downstream symptoms, and it is the largest single structural failure
in the review. **The abstraction is wrong.**

### 2.2 Minimum availability is declared, and cannot be resolved, applied, or ordered. — **PARTIAL, severe**

Radarr: `MovieStatusType` is an *ordered* enum (`Movies/MovieStatusType.cs:5-10`, `Deleted=-1 … Released=3`);
`Movie.IsAvailable(delay)` (`Movies/Movie.cs:77-117`) is a date computation branching on the chosen value;
`AvailabilitySpecification` (`DecisionEngine/Specifications/RssSync/AvailabilitySpecification.cs:22-38`)
refuses a *grab* — and explicitly does not refuse a user-invoked search.

Three separate contract failures land on this one feature:

1. **`SelectionFacetKind` has no ordered enumeration.** `Enumerated` is set membership, `Threshold` is a
   number (`Arronix.Abstractions/Shape/SelectionFacet.cs:645-655`). The order survives only as
   `MoviesStatuses.Order` (`MoviesShape.cs:328-336`), which the host cannot see. A generic client renders
   four radio buttons where the domain has a threshold.
2. **`FacetApplication` has no acquisition member** (`SelectionFacet.cs:680-687`: `Materialization | Visibility`).
   An unavailable movie is neither uncreated nor hidden — its row exists, the user sees it, only a grab is
   refused. The plugin picks `Visibility` and documents it as "the less destructive of the two wrong answers"
   (`MoviesShape.cs:817-820`).
3. **The resolved facet value never reaches the extension.** Neither `ItemQuery` nor `IMediaItemSource.GetAsync`
   carries a resolved `SelectionPolicy`, and `SelectionPolicy` — which *is* passed to
   `ICataloger.GetAsync` — has `AllowedValues`/`Thresholds`/`Flags` and no ordered map.

The load-bearing consequence: `releaseDate` is declared as a catalog field (`MoviesShape.cs:1119-1127`) but
Radarr's `GetReleaseDate()` (`Movies/Movie.cs:121-143`) *branches on the entry's minimum availability*. The
plugin answers a facet-independent form. **For any entry configured off the default, the platform's release
date is wrong**, and there is nowhere to fix it. **The abstraction is wrong.**

### 2.3 Alternative titles have no provenance, and translations are correlated by array index. — **DEGRADED**

Radarr's `AlternativeTitle` (`Movies/AlternativeTitles/AlternativeTitle.cs:5-35`) carries
`SourceType { Tmdb, Mappings, User, Indexer }`. That is not decoration: a metadata refresh replaces the
`Tmdb`-sourced titles and must not delete a title the *user* added or an *indexer* contributed. The plugin's
`alternativeTitles` is a flat multivalued text field (`MoviesShape.cs:918-928`) with no provenance slot,
because `FieldDescriptor` has no sub-fields. A refresh either wipes user edits or never converges.

`MovieTranslation` is the tuple `(Language, Title, CleanTitle, Overview)` repeated
(`Movies/Translations/MovieTranslation.cs:7-11`). `FieldValue.Items` is a homogeneous list, so the plugin
splits it into three parallel multivalued fields correlated **by index** (`MoviesShape.cs:929-957`) —
`translatedTitles[i]` belongs with `translationLanguages[i]` — and *the correlation is undeclarable*. Any
consumer that filters one list desynchronises all three. Same shape of problem for `Ratings`: five
`(value, votes)` tuples become seven scalar fields.

Radarr's four-layer fallback (`Movies/MovieService.cs:126-157`: own/original title → roman-rewritten →
alternative titles → translations, each year-filtered) *is* reproduced in `MoviesReleaseMatcher.cs`, but only
by reaching past `IReleaseMatcher`'s inputs into the plugin's own `MovieRecord`. **The abstraction is wrong**
(`FieldDescriptor` needs repeated composites); **the plugin is complete** on the matching behaviour.

### 2.4 There is no file record in the abstraction at all, so `edition` has nowhere to live. — **BLOCKED, severe**

Radarr's `MovieFile` (`MediaFiles/MovieFile.cs:15-28`) carries
`RelativePath, Path, Size, DateAdded, OriginalFilePath, SceneName, ReleaseGroup, Quality, IndexerFlags,
MediaInfo, Edition, Languages`.

`Arronix.Abstractions` has `MediaFileId` — an opaque `long` (`Shape/MediaFileId.cs:23`) — **and nothing else**.
There is no file record type anywhere in the contract surface (verified by grep). Consequences:

- `{Edition Tags}` belongs to no partition. `MoviesShape.cs:606-618` documents this exactly: an edition is a
  movie noun so the host cannot own the token; a file is not an item so the level's field list cannot declare
  it; `FormatFamily` declares extensions and a ladder but no per-file facets. `FileScopedKindTokenNames` is a
  one-element list that exists to name the hole.
- Extended vs Theatrical — the single most common reason a user wants a *specific* release of a film they
  already have — has no member on `ParsedRelease` and is carried as `AdditionalMetadata["movies.edition"]`
  (`MoviesReleaseParser.cs:30`).
- `IRenamePolicy.GenerateFileNameAsync(MediaItemId, string, CancellationToken)`
  (`Naming/IRenamePolicy.cs:22-26`) has no file parameter at all, against Radarr's
  `BuildFileName(Movie, MovieFile, NamingConfig, customFormats)`. **24 of the plugin's 39 tokens are file
  properties and are unreachable through the interface.** The plugin adds a `MovieFileFacts` overload that
  works (`MoviesNaming.cs:71-113`); the interface implementation silently degrades to the movie-only subset.

**The abstraction is wrong.** This is the second-largest structural gap.

### 2.5 `MovieStats` — **NOT BLOCKED, and not the plugin's business**

`MovieStatistics` (`MovieStats/MovieStatistics.cs:10-13`) is `MovieId, MovieFileCount, SizeOnDisk,
ReleaseGroupsString`. Every one of those is host state by the abstraction's own rule (`ItemView`'s remarks:
"monitoring state, progress, the selected variant, file counts and affordances… are host state"). Radarr's
`MovieStatisticsRepository` is two SQL aggregates over `Movies` and `MovieFiles`. The correct answer is that
the host computes this generically for every kind; nothing is missing from the plugin and nothing is wrong
with the contract. **No finding.**

### 2.6 The TMDb id-redirect path — **DECLARED, UNCONSUMED**

Radarr sets `AllowAutoRedirect = true` on all seven movie/collection requests
(`MetadataSource/SkyHook/SkyHookProxy.cs:66,80,94,109,142,175,204`) and keeps no chain, so a library row can
silently start pointing at a different work when TMDb merges duplicates. The plugin declares
`LevelIdentity.SupportsIdentifierRedirects = true` (`MoviesShape.cs:723`) with a comment saying exactly this.
The flag is right. **Nothing in the host consumes it**, and there is no contract member through which an
extension reports "identifier A now resolves to identifier B". The declaration is currently decorative.
**The abstraction is incomplete** (a small, cheap gap): needs a `RedirectedTo` on the resolve path.

### 2.7 Manual import — **DECLARED, PROPOSED, NEVER COMMITTED**

Radarr's `IManualImportService` (`MediaFiles/MovieImport/Manual/ManualImportService.cs:24-27`) is
`GetMediaFiles`, `GetMediaFiles(path, downloadId, movieId, filterExisting)`, `ReprocessItem(...)` — 509 lines,
plus 559 lines of import specifications (`MediaFiles/MovieImport/Specifications/`) and eight aggregators.

The plugin declares the workbench (`MoviesIntent.cs:770-810`) and produces a proposal. Three things then fail:

1. **`CommitAsync` cannot import.** `MoviesItemSource.cs:325-365` validates and returns
   `"N row(s) are coherent and ready to apply"`. `WorkbenchEndpoints.cs:177-189` returns 202. Nothing applies.
2. **The "which movie is this file?" column offers nothing.** `WorkbenchColumn.OptionSourceId` is declarable
   and unanswerable — the host resolves choices from the column's own declaration, identical for every row,
   and no extension-side method resolves one. `WorkbenchEndpoints.cs:38-42` documents this itself.
   `MoviesWorkbenches.CatalogOptionSource` (`MoviesIntent.cs:95-105`) is declared to state an intent the
   contract has not caught up with. **Row-scoped choices are the entire point of manual import.**
3. **`ReprocessItem` has no analogue.** Radarr re-runs the decision when the user overrides the movie,
   quality, languages or release group. `ProposeAsync` takes a flat `IReadOnlyDictionary<string,string>` and
   is called once.

**The abstraction is wrong** on (1) and (2); (3) follows from them.

### 2.8 Also blocked, briefly

- **Import-list exclusions** (`ImportLists/ImportExclusions/`, 178 lines). `exclude`/`exclude.clear` are
  declared and inert (§0). No contract carries an exclusion set. Curators must consult it; `ICurator` cannot.
- **Root-folder / path assignment.** Radarr's `MoviePathBuilder` (60 lines) resolves a movie's folder from the
  root folder plus the naming config. `ILibraryLayout.GenerateFolderPathAsync(MediaItemId, LibraryPathSpec, …)`
  covers it, but `LibraryPathSpec.CustomTokens` is an untyped `IReadOnlyDictionary<string,string>` and the
  plugin reads `groupByCollection` out of it (`MoviesNaming.cs`, `MoviesLibraryLayout`) because a two-level
  layout has no other expression. An unvalidated second token source.
- **`MonitoringOptions` / apply-once monitor policies.** `MonitorDimension`'s remarks say these are "actions
  that write dimensions". They are declared as actions and the actions do not run (§0).
- **Credits (cast/crew).** 178 lines in Radarr. Not implemented; `FieldDescriptor` could not carry the
  `(person, character, order, department)` tuple anyway — same root cause as §2.3.
- **Recommendations.** `MovieMetadata.Recommendations` is `List<int>` of TMDb ids; `FieldValueKind.Reference`
  exists but there is no discovery surface to put them on.

---

## 3. THE ABSTRACTION IS WRONG — ranked by consequence

Collated from the three build agents' notes, verified against the code, de-duplicated, re-ranked. Each entry
gives the exact contract change.

### A1. No action seam. Fourteen of fourteen declared actions are unperformable.

*Evidence:* `Arronix.Abstractions/Shape/IMediaItemSource.cs` (two write members, both workbench-bound);
`Arronix.Api/Endpoints/ActionEndpoints.cs:41,132`; `MoviesIntent.cs:113-116`.

*Change:* add one member to `IMediaItemSource`.

```csharp
Task<ActionResult> PerformAsync(
    string actionId,
    IReadOnlyList<MediaItemRef> items,
    IReadOnlyDictionary<string, string> parameters,
    CancellationToken cancellationToken = default);
```

Also promote `ActionRequest` out of `Arronix.Api` (it is `internal` there; `ActionEndpoints.cs` flags this
itself) so a client does not reproduce it by hand. Closes the gap for every kind at once.

### A2. A grouping axis has no metadata, no identity and no scope. (§2.1)

*Change, three parts:*
1. `GroupingAxis` gains `IReadOnlyList<FieldDescriptor> Fields` and `IReadOnlyList<ExternalIdScheme> ExternalIds`.
2. Replace the `IsMonitorable` bool with `GroupPolicies` flags (`Monitored | QualityProfile | RootFolder |
   SelectionFacets | Tags | SearchOnAdd`) — Radarr needs all six and the bool says one.
3. A reference type that can address a group. Either widen `MediaItemRef` to
   `(kind, level?, axisId?, id)`, or add `MediaGroupRef` and accept it in `ResolveExternalAsync`,
   `AcquisitionScopeKind`, `ActionScope`, `WorkbenchDescriptor.Target` and `MetadataNode`.

Without (3) the other two do not help: there is still nothing to hang a group's state on.

### A3. `IRenamePolicy` has no file parameter; 24 of 39 tokens are unreachable. (§2.4)

*Evidence:* `Naming/IRenamePolicy.cs:22-26`; `MoviesNaming.cs:71-90`; `MoviesShape.cs:578-604`.
The plugin's own test `RenamePolicyTests.RendersOnlyTheMovieSubsetThroughTheContractMethod` demonstrates it:
the contract method yields `The Godfather (1972)`, the file-bearing overload yields
`The Godfather (1972) Bluray-1080p`.

*Change:* introduce a file record in the contract — this is A3 and §2.4's fix together:

```csharp
public sealed record MediaFileFacts(
    MediaFileId Id, string Path, string? SceneName, long SizeBytes,
    QualityTier Quality, string? ReleaseGroup,
    IReadOnlyList<Language> Languages,
    IReadOnlyDictionary<string, string> TechnicalFacets,   // codec, bit depth, dynamic range, channels
    IReadOnlyDictionary<string, string> KindFacets);        // "edition" lives here, declared by FormatFamily
```

and change `GenerateFileNameAsync(MediaItemId, MediaFileFacts?, string, CancellationToken)`. Add
`FormatFamily.TechnicalFacets: IReadOnlyList<FieldDescriptor>` so `{Edition Tags}` and every `{MediaInfo …}`
token has a declaration to derive from (naming-and-tokens D14/D15 already assume this member exists).

### A4. `FormatFamily.Ladder` forbids equal ranks, so quality *groups* are unrepresentable — and `CutoffPolicy` gives the wrong answer.

*Evidence:* `Arronix.Host/Media/ShapeValidationRules.cs:508` (*"Rank N is used by 2 tiers; ranks order the
ladder and must be distinct."*); Radarr gives WEBDL-1080p and WEBRip-1080p the same weight deliberately.
Workaround: `QualityTier.Rank` carries a ladder *position* and the real weight travels in
`AdditionalAttributes["movies.weight"]` (`MoviesQualityModel.cs:245`).

*This is not cosmetic.* `CutoffPolicy.MeetsCutoff` (`DTOs/CutoffPolicy.cs:21`) compares `Rank` and therefore
answers **false** for "WEBRip-1080p meets a WEBDL-1080p cutoff", where Radarr and the plugin's own model both
say true. Pinned by `QualityLadderTests.TheContractsOwnCutoffCheckDisagreesWithTheDomainForAGroupedRung`.
Any host code path that uses the DTO rather than the model is silently wrong.

*Change:* `QualityTier` gains `int Weight` (defaulting to `Rank`); `CutoffPolicy.MeetsCutoff` compares
`Weight`. `Rank` stays a distinct ordering key. Two-line change, removes a live correctness bug.

### A5. `QualityTier` cannot carry a revision; proper/repack/real is an entire upgrade axis with no surface.

*Evidence:* `DTOs/QualityTier.cs:5-14`; `IQualityModel.IsUpgrade(QualityTier, QualityTier)` cannot receive one.
Workaround: three keys in `AdditionalAttributes` (`MoviesQualityModel.cs:257-263`), plus a *lossy* flattening
at the `ParsedRelease` boundary — `REPACK2` and `REPACK` produce an identical `MovieRevision`
(`UpgradeDecisionTests.LosesTheRevisionDetailAtTheParsedReleaseBoundary`). A PROPER-of-a-REPACK losing to a
plain REPACK is a real, silent wrong grab.

*Change:* `QualityTier` gains `QualityRevision? Revision` where
`QualityRevision(int Version, int Real, bool IsRepack)`. `CutoffPolicy` gains
`ProperHandling { PreferProper | AcceptProper | IgnoreProper }` — Radarr's `ProperDownloadTypes` has no
equivalent at all, so "cutoff met, but still take the PROPER" is currently inexpressible.

### A6. `FilterOption` declares operators `ItemQuery` cannot carry, so the extension invents a private grammar.

*Evidence:* `Intent/FilterOption.cs:24-53` declares `Between, GreaterThan, LessThan, Contains, IsNull`;
`Shape/ItemQuery.cs:40-42` is `fieldId → IReadOnlyList<string>`, which expresses set membership and nothing
else. The plugin invents `>=8.7`, `1990..1999`, `~text`, `!value`, `-`, `+`
(`MoviesItemSource.cs:77-127`, `MovieFilterTerm.Parse`). It works, it is understood by this assembly alone,
no generic client can be expected to produce it, and the host cannot validate a grammar it does not have.

*Change:*

```csharp
public readonly record struct FilterTerm(string FieldId, FilterOperators Operator, IReadOnlyList<string> Values);
// ItemQuery.Filters : IReadOnlyList<FilterTerm>
```

Deletes ~120 lines from every plugin that implements filtering, and makes the declared `FilterOperators`
honest for the first time.

### A7. `ItemQuery` has no grouping slot, so two assemblies agree on a magic string through a comment.

*Evidence:* `Arronix.Api/Endpoints/ItemEndpoints.cs:49,54` sets `"arronix.group-axis"` / `"arronix.group"`;
`MoviesItemSource.cs:155,158` hard-codes the same two literals because `Arronix.Api` is an assembly an
extension may not reference. **Any drift is a silent behaviour change, not a compile error.**

*Change:* `ItemQuery` gains `string? GroupingAxisId` and `string? GroupKey`. Also define what a group key
*is* (see A2): today the plugin accepts either the TMDb id or the collection title and guesses.

### A8. `ParsedRelease` carries about half of a movie parse; the rest is an untyped dictionary.

*Evidence:* `DTOs/ParsedRelease.cs:9-19` — nine flat members, all strings. The plugin writes twelve keys into
`AdditionalMetadata` (`MoviesReleaseParser.cs:27-79`), of which these are **decision inputs, not diagnostics**:
edition, revision (×3, lossily flattened), alternative titles (`Title` is one string; `Sydney AKA Hard Eight`
produces three), release hash, hardcoded-subs marker, sub-group, MULTI, stated-vs-resolved resolution.

Worse: `ParsedRelease.Quality` and `.Resolution` are two strings for a three-axis value
(`source × resolution × modifier`), so the modifier is smuggled into the source token as `dvd-screener`,
`bluray-remux`, `bluray-disk`, `tv-raw` — a private vocabulary agreed between two files that works only
because they are in the same assembly. It also forces the parser to emit a resolution the release never
stated (`("tv","480p")` for a bare `HDTV`) purely so the pair resolves, which is why
`movies.statedResolution` exists at all.

And `ReleaseCandidate.AdditionalData` holds any indexer-reported IMDb/TMDb id — which is the
*highest-precedence input* to Radarr's `FindMovie` — as an untyped string the matcher only sees because the
host chose to lift it into `MatchRequest.ExternalIds`. Nothing in the contract obliges it.

*Change:* `ParsedRelease` gains `IReadOnlyList<string> Titles` (replacing the single `Title`),
`QualityDescriptor Quality { Source, Resolution, Modifier }`, `QualityRevision? Revision`, and
`IReadOnlyDictionary<string,string> KindFacets` explicitly documented as *presentation and matching hints
only*. `ReleaseCandidate` gains `IReadOnlyList<ExternalId> ExternalIds`.

### A9. `NamingToken` is a four-field positional record, so the modifier vocabulary does not exist.

*Evidence:* `DTOs/NamingToken.cs:8-12` — `(Name, Description, ExampleValue, IsRequired)`. No `CanonicalName`,
`ValueKind`, `Origin`, `LevelId`, `Multivalued`, `Elasticity`. Consequences:

- The clean/the/first-character cross-product is written out longhand: **7 title tokens where one token and
  three modifiers would do** (`MoviesShape.cs:1243-1271`).
- **24 of 39 tokens are host-global** (`{Quality …}`, `{Release Group}`, `{MediaInfo …}`, `{Original …}`,
  `{Custom Format(s)}` — `MoviesShape.cs:578-604`). Every kind would spell them identically. There is
  nowhere to put them but `MediaShape.Tokens`, so the plugin declares 24 tokens it does not own, or its own
  default template fails validation.
- `IsRequired` is one flat bool but the real rule is a per-slot boolean expression: a file template needs
  *(title AND year AND NOT original) OR original*; a folder template needs a title and must **not** contain
  any file-derived token. Neither is declarable, so both live in
  `MoviesRenamePolicy.ValidateTemplate`/`ValidateFolderTemplate` (`MoviesNaming.cs:774,828`) where nothing can
  check them, and `MoviesShape.FolderIllegalTokenNames` (`MoviesShape.cs:626-651`) is a hand-maintained
  23-element list that will drift.

*Change:* (a) a host-global token registry — `IPluginRegistry` or `IPluginContext` exposes the platform's own
token set, and `MediaShape.Tokens` carries only kind tokens; (b) `NamingToken` becomes a record with
`CanonicalName`, `ValueKind`, `Origin { Item | File | Quality | Probe | Group }`, `LevelId?`,
`IReadOnlyList<TokenModifier> Modifiers`, and `TokenSlots AllowedIn { File | Folder }` replacing `IsRequired`.

### A10. There is no seam for `RenderedSummary`. The notification design's central claim has no contract.

*Evidence:* grep across `Arronix.Abstractions` finds `RenderedSummary` in exactly three places — its own
definition, `NotificationMessage.Summary` and `EventEnvelope.Summary` — **both consuming**. `IPluginRegistry`
has 24 `Add*` members and none of them is a summary renderer. `IMediaItemSource` has no render method.
`Providers/MoviesNotificationRenderer.cs` (507 lines) is a complete, correct implementation of a contract
that does not exist: public API nothing can call.

*Change:*

```csharp
public interface IItemSummaryRenderer {
    MediaKindId MediaKind { get; }
    Task<RenderedSummary> RenderAsync(MediaItemRef item, string eventId, CancellationToken ct = default);
}
IPluginRegistry AddSummaryRenderer(IItemSummaryRenderer renderer);
```

Plus, on `RenderedSummary`: `IReadOnlyList<SummaryLink> Links` (a Discord embed wants IMDb/TMDb anchors;
today they go into `SummaryField(Label, Value)` with a URL as the value and no consumer can tell them apart
from a field that merely looks like a URL). And change `NotificationMessage.Context` from
`IReadOnlyDictionary<string,string>` to `IReadOnlyDictionary<string,FieldValue>` — today every value is
pre-formatted text, so the extension has already chosen number and date formats on the reader's behalf.

### A11. `ICurator.MediaKind` is on the implementation, so one curator serves exactly one kind.

*Evidence:* `Providers/ICurator.cs:29`. `ProviderDefinition.MediaKinds` already exists for narrowing, and
`ICataloger` correctly takes the kind per-call via `MetadataQuery.Kind`. A real list (Trakt, IMDb, Plex
watchlist) mixes movies and series; its media kind is a property of *the content it returned*. The plugin
filters `MediaType != "movie"` and silently drops the rest (`Providers/MoviesCurator.cs:891`) — **a TV plugin
will write the mirror-image code against the same list and each will discard the other's items.**

*Change:* remove `MediaKind` from `ICurator`; `CuratedItem.Level` already carries the discrimination.

### A12. `SettingsField` cannot declare a single value constraint, and cannot express OAuth.

*Evidence:* `Providers/SettingsField.cs` — 16 members, none of them `Pattern`, `Minimum`, `Maximum`, `Step`,
`Placeholder` or `Hidden`. Every one of the 12 rules in Radarr's `TMDbFilterSettingsValidator` is a rule about
one field's own text (0–10 range, `^[1-9][0-9]*$`, a certification whitelist, a comma-or-pipe id list) and all
12 are re-implemented by hand in `MoviesCuratorFilter.Validate`. The host cannot pre-validate a form and the
UI cannot give inline feedback.

`TMDbUserSettings` — watchlist/favorites/rated, **the most-requested import list in Radarr** — is not
implementable at all: `SettingRole` has no `OAuthTrigger`, there is no `Hidden` flag for a machine-written
account id or a cached token, and no contract says "the provider needs a browser redirect and will write two
settings back". `Sensitivity = Credential` gets storage and redaction right and says nothing about
*acquisition*.

*Change:* `SettingsField` gains `string? Pattern`, `double? Minimum`, `double? Maximum`, `bool Hidden`,
`string? Placeholder`, and `DefaultValue` becomes `IReadOnlyList<string>`. Add `SettingRole.OAuthTrigger`
plus a two-method `IOAuthCapableProvider { Uri BeginAuthorization(...); IReadOnlyDictionary<string,string> CompleteAuthorization(...) }`.

### A13. `MatchOutcome` cannot say *why* it matched.

*Evidence:* `Shape/IReleaseMatcher.cs` — `MatchConfidence` is a five-point scale. The load-bearing distinction
is categorical: Radarr's `MovieMatchType.Id` vs `.Title`, which downstream import decisions branch on.
`Exact` is not a synonym for "by identifier". The plugin added `MovieMatchKind` and a `MatchWithReason`
overload no host caller can reach; pinned by
`ReleaseMatcherTests.ReportsWhyItMatchedOnlyThroughAnExtensionLocalOverload`.

*Change:* `MatchOutcome` gains `MatchBasis { Identifier | Title | Coordinate | Scope | Manual }`.

### A14. `AcquisitionRequest` carries no profile, so translated-title search is unreachable.

*Evidence:* `Shape/IReleaseQueryPlanner.cs` — `AcquisitionRequest` is `{MediaKind, SearchKindId, Units, Origin}`.
Radarr adds a search query for every translation *whose language the quality profile accepts*; the filter is
what makes it affordable (thirty translations otherwise means thirty queries per source per sweep). The plugin
publishes translations as `ReleaseQuery.Aliases` and does not fan out. **This is the one piece of movie query
construction the contract makes impossible**, and it is not movie-specific — the same filter applies to TV
scene names and music artist aliases.

*Change:* `AcquisitionRequest` gains `IReadOnlyList<Language> AcceptedLanguages` (or a resolved profile
reference). Separately, **define `ReleaseQuery.Aliases`**: today "alternative spellings" does not say whether
a source issues one request per alias, ORs them, or ignores them, so the planner emits N queries *and*
populates `Aliases`, duplicating the information and leaving fan-out policy undefined.

### A15. Smaller, verified, each with a one-line fix

| # | Finding | Evidence | Change |
|---|---|---|---|
| a | `IReleaseParser.Parse(string)` has no folder/file flag, though `IReleaseMatcher` has `MatchSource` and cannot pass it on. Radarr's folder-only pattern (`year - title`) is unreachable through the stable contract. | `Parsing/IReleaseParser.cs`; `MovieTitleParser.TryParse(text, isFolder, …)` | add `MatchSource source` to `Parse` |
| b | `ReleaseQuery.FreeText` is `required` and non-nullable, so an RSS browse and a search-for-nothing are indistinguishable. | `IReleaseQueryPlanner.cs`; `QueryPlannerTests.PlansASingleUnnamedBrowseForASweep` | make it `string?`, or add `ReleaseQueryKind` |
| c | `SearchTerm` has no member for a *title variant*, so display title, original title and every alternative title are emitted as three indistinguishable `WorkTitle` arguments — one of which is canonical. | `Shape/SearchTerm.cs:24-51` | `SearchArgument` gains `string? VariantId` |
| d | `SearchTermBinding.ComponentId` is doing double duty — the *scheme* (`imdb`/`tmdb`) for `ExternalIdentifier`, a coordinate component for `Ordinal`. This collapse is the best thing in the search vocabulary and it is undocumented and unenforced. | `Providers/SearchProfile.cs` | document it; validate it |
| e | `ICataloger` declares `CatalogerCapabilities.BulkFetch` and has no bulk member. A refresh over 3,000 movies is 3,000 `GetAsync` calls. | `Providers/ICataloger.cs`; `MoviesCatalogRequests.MovieBulk` exists and nothing can call it | add `GetManyAsync` |
| f | `ICurator` has no removal semantics and no pagination contract; "the list was longer than my page limit" is reported by setting `AnyFailure = true`, conflating truncation with transport failure. | `Providers/ICurator.cs:49-64` | replace `bool AnyFailure` with `Completeness { Authoritative \| Truncated \| PartiallyFailed }` |
| g | `IProvider.GetOptionsAsync` sees only *saved* settings, so any option source depending on a credential the user is currently typing is unimplementable. | `Providers/IProvider.cs` | pass the unsaved form values |
| h | `ProviderDescriptor` cannot say "these two providers are the same service" — the cataloger and three curators need the same base URL and credential typed into four unrelated definitions. | `Providers/ProviderDescriptor.cs` | a shared-account / connection concept |
| i | `ArtworkRef.Width`/`Height` are structurally always `null` — a catalog publishes a role and a URL, not dimensions. | `Shape/RenderedSummary.cs:96` | replace with a size-hint enum |
| j | `Language(Code, Name)` has no room for Radarr's `Original` pseudo-language, so the plugin mints `new Language("original", "Original")` — a non-ISO code in a field that looks like one. | `DTOs/Language.cs` | add an `IsOriginal` flag or a well-known sentinel |
| k | `MatchRequest.Parsed` supplies a `ParsedRelease` the matcher cannot use, so it re-parses `Text` and ignores it — a direct consequence of A8. | `MoviesReleaseMatcher.cs` | fixed by A8 |
| l | `CoordinateConfidence.Verified` is the honest value for a singleton and reads as a lie: nothing was verified, there was only ever one point. | `Shape/CoordinateSet.cs` | add `NotApplicable` |
| m | An extension cannot declare an index. Radarr issues three indexed SQL queries per lookup key (`Movies/MovieService.cs:159-185`); the plugin scans in memory. Fine for 52 seeded films, wrong for 80,000. | `IMediaItemSource` / `MediaShape` | a declared match-key set, or accept that the extension owns storage |
| n | `ValidationFailure.Code` defaults to `InvalidConfiguration`, so the default is a lie for every missing-value failure. | `Providers/ValidationOutcome.cs` | no default |

---

## 4. THE ABSTRACTION IS FINE — this plugin is incomplete

Filed separately and deliberately. None of these needs a contract change.

1. **No `IImportPipeline` registration.** `IPluginRegistry.AddImportPipeline` exists (`IPluginRegistry.cs:94`)
   and `MoviesPluginModule` does not call it. Radarr's genuinely movie-specific import specifications are
   `NotSampleSpecification` (runtime-derived sample detection — the *runtime* is movie metadata) and
   `MatchesFolderSpecification`; the rest of the 559 lines are agnostic. Roughly 150 lines of real work,
   fully expressible today.
2. **No `INotifier` implementation.** The renderer exists and cannot be registered (A10), but the
   `INotifier` family itself is untouched. Once A10 lands there is a `Registration` to write.
3. **Credits and recommendations are absent** (§2.8). Credits additionally need A2-style composite fields;
   recommendations need only a discovery surface that already exists in principle.
4. **`MovieLanguageReader` is a deliberate stand-in.** Radarr's `LanguageParser` is ~600 lines and is
   duplicated near-verbatim in Sonarr, so the plugin ports a compact version keeping only the genuinely
   non-obvious rule (German `DL` implies dual-audio). The omission is honest and reported. It is not a
   contract problem — but it should end up in core, not here (§5).
5. **The `<…>` optional group and `{span}` template forms are not implemented.** A template using `<…>`
   degrades to having the brackets stripped. Reported by the build agent; not a contract issue.
6. **One `[Ignore]`d test**, `QueryPlannerTests.PlansNoTextQueryForAMovieWithNoYear`, because
   `MoviesCatalog`'s constructor is private and every seeded film carries a year. Fixture work.
7. **The seed is 2,356 of the plugin's 15,936 lines** (`Seed/MoviesSeedData.cs` 1,959 + `Seed/MoviesSeed.cs`
   397). That is a fixture standing in for a real catalog, and it is the right call for a reference
   implementation — but any line-count comparison must exclude it.

---

## 5. What leaked

### 5.1 Media-agnostic code sitting in this plugin

Verified by asking, for each: *would the TV, music and books plugins write this identically?* The three
siblings are in the tree (`Arronix.Plugin.Tv` 5,896 lines, `Music` 4,103, `Books` 4,391), and
`Arronix.Plugin.Tv/TvReleaseParser.cs` has already written its own release-group parsing — the duplication is
not hypothetical, it has happened.

| Leaked | Where | Size | Why it is not movie semantics |
|---|---|---|---|
| **The whole template engine** | `MoviesNaming.cs:144-644` (`MovieNameFormatter`) | **500** | Grammar, affix elision, `:spec` truncation, list filtering, separator collapsing, illegal-character and colon policy, reserved-device-name escaping, diacritic folding. None of it mentions a movie. It is here only because `IRenamePolicy` hands over a raw string and asks for a finished name. |
| **Release-group parsing** | `MoviesReleaseParser.cs:1032-1157` | **126** | Anime sub-group, exact-name exception list, bracket-suffix list, obfuscation-suffix strip, trailing `-Group` with negative lookbehind. The exception lists are *group names*, not media. Already duplicated in `TvReleaseParser.cs`. |
| **Language detection** | `MoviesReleaseParser.cs:1922-2098` | **177** | Radarr's version is duplicated near-verbatim in Sonarr. Zero movie semantics. |
| **Codec reading** | `MoviesReleaseParser.cs:1837-1921` | **85** | Video-container facts. Shared with TV outright. |
| **Quality *token scanning*** | `MoviesReleaseParser.cs:406-1031` (`MovieQualityTokenParser`) | ~626, of which ~400 agnostic | The regex vocabulary (`BRDISK`, `RawHD`, `848x480`, `bluray1080p`, container fallback) is universal; only the *resolution table* that names rungs of a specific ladder is per-kind. The shape wants a core token scanner plus a per-kind resolution map, and there is no seam for it. |
| **Hashed-release rejection** | `MoviesReleaseParser.cs:1734,1758` and around | ~40 | `^[0-9a-zA-Z]{32}`, `^abc$`, `^b00bs$`. A bad release name is bad for every kind. |
| **Paged fetch with truncation detection** | `Providers/MoviesCurator.cs` (twice) | ~120 | `while (page <= maxPages)` + total-pages + dedupe + partial-failure. Written twice here and will be written again by every curator in every plugin. Wants `PagedFetch<T>(fetchPage, maxPages) → (items, completeness, warnings)`. |
| **Byte and duration formatting** | `Providers/MoviesNotificationRenderer.cs` | ~40 | Present only because `RenderedSummary` demands pre-formatted text. Fixed by the `FieldValue` change in A10. |
| **Search eligibility** | *deliberately not implemented* | — | `source.Terms ⊇ kind.RequiredTerms && source.Categories ∩ kind.Categories ≠ ∅` is identical for every kind. `MoviesIndexerProfiles.Select` correctly answers only the provider-side question. Correctly left to core. |

**Total unambiguously leaked: ~888 lines, plus ~400 more in the quality token parser — call it 1,300 of the
plugin's 13,580 non-seed lines, or 9.6%.** Every one of these is duplicated or duplicable across four plugins,
so the true platform cost is roughly 4×.

The pattern is uniform and worth naming: **every leak is a case where a contract passes a raw string across
the seam and asks for a finished answer.** `IRenamePolicy` passes a template string. `IReleaseParser` passes a
release name. `ICurator` passes nothing and expects a whole list. In each case the extension has to
re-implement the generic half to reach the specific half.

### 5.2 Does the core know about movies?

**No, and this is the abstraction's clearest success.** A grep of `Arronix.{Abstractions,Common,Host,Api,Plugins}`
for `movie|film` returns exactly two hits, both doc-comment examples
(`Identity/MediaKindId.cs:4`, `Media/IMediaKind.cs:6`). Nothing in the core dispatches on, branches on, or
special-cases a movie.

Two qualifications, both worth stating rather than claiming the coupling is gone:

- **The newznab 2xxx band is where the coupling went.** `MoviesShape.Categories` (`MoviesShape.cs:517-527`)
  is eight integers that mean "movies" to every release source in existence. The host carries and compares
  the number and never interprets it, which is a much better place for the coupling — a new media kind needs
  no host change — but "the search vocabulary names no media kind" is true of the C# and not of the
  semantics.
- **The core leaks *into* the plugin.** `arronix.group-axis` and `arronix.group` are declared in `Arronix.Api`
  (`ItemEndpoints.cs:49,54`), an assembly the plugin may not reference, and reproduced as literals in
  `MoviesItemSource.cs:155,158`. That is a core→plugin leak of exactly the kind the architecture test is
  supposed to prevent, and the mechanical enforcement does not catch it because it is a string. (A7.)

### 5.3 A latent defect the review found, already fixed, worth recording

`MoviesShape.cs:508-527` documents that the published declaration once carried **no search categories at
all**: `private static readonly MediaShape Declaration = Build();` was declared *before* the `Categories`
list, static field initializers run in textual order, and both search kinds were constructed with
`Categories = null` — which silently wins over the `= []` default. Nothing observed it. Any host computing
eligibility by category intersection would have found nothing.

**The contract-level finding:** `SearchKind.Categories` is `required` but its emptiness is never validated,
and `ShapeValidationRules` does not check it. A shape whose search kinds match no category is a shape whose
searches can never dispatch. That should be a load-time refusal.

---

## 6. Did the intent model hold?

**Yes, on its own terms — and the terms are narrower than they look.**

*The test it passes.* No presentation concept leaked into the declarations. The build agent reports never
having to name a widget, and reading `MoviesIntent.cs` confirms it: there is no colour, no control, no
layout, no size, no icon. Three declarations that would obviously have become widgets were re-cast and the
re-casting is honest:

- A **release calendar** is `BrowseAxisKind.Sequence` over a date field (`MoviesIntent.cs:264-287`). Three of
  them, because "when can I watch it" and "when was it in cinemas" are different questions.
- A **poster wall** is `Prominence.Primary` + `FieldSemantics.Artwork` on `poster` (`MoviesShape.cs:1063-1070`).
- A **release table** is a workbench with typed columns; `FieldValueKind.ByteSize` carries the size and the
  consumer formats it.

`StateTone` survives its own risk: it states valence, not appearance, and without it a consumer would need
compile-time knowledge of a kind's state names.

*Where it did not hold.* Five failures, none of them a widget — all of them a **missing semantic**:

1. **"This field has a value" cannot be declared.** `ActionDescriptor.EnabledWhenFieldId`
   (`Intent/ActionDescriptor.cs:76-79`) names a *two-state* field. "Set wanted for the whole collection" is
   enabled when `collection` is non-empty, and `collection` is text. The plugin declares it anyway and
   comments the lie (`MoviesIntent.cs:512-515`). *Change:* `EnabledWhen { FieldId, Predicate { IsTrue |
   HasValue | Equals } , string? Value }`.
2. **An ordered enumeration cannot be declared.** `ActionParameter.Choices` is a flat list; minimum
   availability is `status >= chosen` over five ordered values (`MoviesIntent.cs:550-561`). Same root cause as
   A2/§2.2's `SelectionFacetKind`. *Change:* `SelectionFacetKind.Ordered`, and `ActionParameter` gains
   `bool ChoicesAreOrdered`.
3. **A group cannot be a subject.** `ActionScope` is `Item | Selection | Level | Kind | System`;
   `AcquisitionScopeKind` is `Single | SequenceSpan | Ancestor`. Neither names a `GroupingAxis` (§2.1, A2).
4. **`WorkbenchSubject` has no member for a catalog candidate.** `LooseFiles | ReleaseCandidates |
   LibraryItems` (`Intent/WorkbenchDescriptor.cs:92-102`). Finding a movie and adding it — *the single most
   common action in a movie manager* — is none of the three. The plugin uses `LibraryItems` and documents the
   consequence: a consumer grouping surfaces by subject files "Add movies" next to "Bulk edit"
   (`MoviesIntent.cs:853-857`). *Change:* add `CatalogCandidates`.
5. **A per-item address cannot be declared.** `ExternalSurfaceDescriptor` takes a URI *template*
   (`Intent/ExternalSurfaceDescriptor.cs`). The `website` field already holds a whole address; `"{website}"`
   would substitute a URL into a URL and validate by accident. Five of six surfaces work; the movie's own
   official site is simply not declarable (`MoviesIntent.cs:762-765`). *Change:* allow
   `UriTemplate = null` with a `SourceFieldId` naming a `FieldValueKind.Link` field.

*And the structural one.* The intent model held for **description** and collapsed for **execution**: 14 of 14
actions are inert (A1), and the workbench is being used as a substitute action carrier for two of its four
instances. A vocabulary that can only describe is half a vocabulary.

---

## 7. Is the 1:1 degenerate case free of ceremony?

**Yes — measurably, and by a wide margin. The design's claim holds. But `MoviesShape.cs` is still 28%
workaround, and that is a different tax with a much larger bill.**

`MoviesShape.cs` is 1,379 lines. Measured:

| Category | Lines | % | What it is |
|---|---:|---:|---|
| Genuine movie declaration | 530 | 38% | `MoviesFields` (162) + `BuildFields` (368). 41 field descriptors, every one a real movie property. |
| Genuine structural declaration | ~120 | 9% | `MoviesIds`, the level, format family, collection axis, two facets, two search kinds. |
| **Hierarchy ceremony** | **~30** | **2.2%** | The singleton `CoordinateSpace` (`:677-687`, 11 lines) + `CoordinateSpaceIds` (1) + `FileBinding`'s `AnchorLevelId == UnitLevelId` and `SpanConstraints = []` (~4) + `Parent = null`, `SequenceAxes = []`, `Variant = null` (3) + the `OnlySpaceId` constant and its doc (~4) + `MediaLevelRoles`' four-flag assignment (~4). |
| **Workaround** | **~383** | **28%** | 24 host-global token declarations (~170, A9) + the four partition lists at `:507-657` (~151) + `MoviesCollectionFields` (47, A2) + `MoviesStatuses.Order` (~15, §2.2). |
| Docs, usings, boilerplate | ~316 | 23% | |

**The multi-level model costs a 1:1 kind about 30 lines out of 1,379 — 2.2%.** The runtime tax is equally
small: `CoordinateSet.Of(new CoordinateReading(OnlySpaceId, Coordinate.Singleton, Verified))` appears exactly
**once** in the matcher (`MoviesReleaseMatcher.cs:64-65`, a static field) and **once** in the item source
(`MoviesItemSource.cs:880-884`). There is no assignment problem, no window, no pack expansion, no "which of
these numbers is the season". The matcher never *reasons* about coordinates; it stamps the one that exists.
`MediaLevelRoles` being a flags enum is what lets one level carry four roles at once, and a position enum
could not have expressed it.

**So the honest verdict: the model does not tax simple shapes. The workarounds tax them thirteen times
harder than the model does.** Fixing A9 (token registry + modifiers), A2 (`GroupingAxis.Fields`) and
§2.2 (`SelectionFacetKind.Ordered`) would remove ~383 lines from this one file and the same class of lines
from every other plugin.

---

## 8. Line-count reality check

**Target:** ~5–6k lines of genuinely movie-specific code (the residue after the ~8,700 lines of
decision-engine / import / quality / profile machinery move to core).

**Measured, Radarr:**

| Area | Lines | Genuinely movie-specific? |
|---|---:|---|
| `Movies/**` | 3,721 | Mostly yes. ~1,100 is repository/service plumbing the host owns (`MovieRepository` 392, `MovieMetadataRepository` 93, `MovieCollectionRepository` 66, event classes ~250). Residue ≈ **2,600**. |
| `MetadataSource/**` | 1,093 | Yes ≈ **1,000** (SkyHookProxy 724 + resources). |
| `Organizer/**` | 1,190 | ~40% movie-specific. `FileNameBuilder` 781 is mostly the generic engine. Residue ≈ **450**. |
| `Parser/**` | 3,650 | ~35%. `LanguageParser`, `RomanNumeralParser`, group parsing are agnostic. Residue ≈ **1,250**. |
| `Qualities/**` | 885 | ~90% is the ladder table itself. Residue ≈ **750**. |
| `IndexerSearch/**` | 354 | ~**200**. |
| `MediaFiles/MovieImport/**` | 2,984 | ~10%. Two specs and the aggregators. Residue ≈ **300**. |
| `MovieStats/**` | 163 | **0** — host state (§2.5). |
| `ImportLists/ImportExclusions/**` | 178 | ~**100**. |
| **Radarr total residue** | | **≈ 6,650** |

**Measured, plugin:** 15,936 C# lines − 2,356 seed = **13,580 implementation lines**, of which ~1,300 leaked
agnostic code (§5.1) and ~383 shape workarounds (§7) → **≈ 11,900 lines of nominally movie-specific work**.

**So the plugin is roughly 1.8× Radarr's residue, not smaller.** That is not padding. Three honest reasons:

1. **XML documentation is mandatory** (`TreatWarningsAsErrors`, `AnalysisLevel=6.0-all`). Radarr has almost
   none. Doc comments account for a large share of the delta — `MoviesShape.cs` alone is 23% docs.
2. **The plugin carries its own catalog transport, wire DTOs and mapper** (`Providers/MoviesCataloger.cs`
   2,008 lines vs Radarr's 724-line `SkyHookProxy`), because `ICataloger` is a richer contract and the
   offline transport had to be written twice — once as the interface, once as the seeded implementation.
3. **Every workaround costs its explanation.** A2, A4, A5, A6, A8 and A9 each cost the plugin both the
   workaround code and the comment justifying it.

**What is nevertheless genuinely missing** (§4, restated as a bill): an import pipeline (~150), a notifier
registration (~100, blocked on A10), credits (~150, blocked on §2.3), recommendations (~80), the full
language parser (~400, but it should go to core not here), the `<…>` template group (~60). **≈ 940 lines**,
none of it structural.

**Brevity is therefore not the story.** The story is that ~14% of the plugin (leak + workaround) exists
because of contract gaps, and the same 14% will be paid again by TV, music and books.

---

## 9. The four changes that matter

If only four things are done, do these. They are ordered by consequence, and each one closes a whole class of
finding rather than one instance.

1. **`IMediaItemSource.PerformAsync`** (A1). Turns a description of a movie manager into a movie manager.
   Unblocks 14 actions here and every action in every other plugin, and removes the pressure that distorted
   two of the four workbenches.
2. **A group is a first-class subject** (A2 + §2.1): `GroupingAxis.Fields`/`ExternalIds`, a `GroupPolicies`
   flags set, and a reference type that can address a group. Closes eleven downstream symptoms with one
   change, and is the only finding in this review with double-digit blast radius.
3. **A file record in the contract** (A3 + §2.4 + A5 + A8's quality triple): `MediaFileFacts`,
   `FormatFamily.TechnicalFacets`, `QualityTier.Weight` and `QualityTier.Revision`, and a structured
   `ParsedRelease.Quality`. Makes 24 of 39 tokens reachable, fixes the live `MeetsCutoff` correctness bug
   (A4), and drains most of the `AdditionalMetadata` dictionary.
4. **Typed queries and a token registry** (A6 + A7 + A9). Deletes the invented filter grammar, deletes the
   two magic strings shared through a code comment, and deletes ~380 lines of shape workaround per plugin.

Everything else in §3 is real and none of it is as expensive as these four.
