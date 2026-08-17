# Open decisions and recorded defects

> **Purpose.** A register of things that need an owner decision, and of defects found and fixed, so neither
> is lost between sessions. Raised during the declarative media-kind milestone (2026-08-17), when
> `Arronix.Plugin.Movies` became the first pure `MediaKindDefinition` and the declarative path was wired
> end-to-end.
>
> **State when this was written:** build 0 errors / 0 warnings; **2,361 tests passing, 336 skipped, 0
> failing**. Everything below is recorded against that baseline.
>
> **Start at [Part 5](#part-5--pass-2-review-findings)** — the owner's review of the first declarative
> media kind. Those are the active work; Parts 1–4 are the standing register.

---

## Part 1 — Needs an owner decision

> **Read Part 6 first.** Three of these were artifacts of the declarations-are-data frame rather than
> questions about the domain: **D-2 and D-4 are dissolved** by the typed direction and must not be
> actioned, and **D-1 is transformed**. Only D-3, D-5 and D-6 are frame-independent.
>
> The lesson worth keeping: escalate a decision when it is about the **domain**; park it when it is about
> the **mechanism**, until the mechanism survives review. A constraint that generates work which then
> justifies the constraint is the pattern to watch for — it produced the EF Core drift and the
> `stability.md` over-reach as well.

### D-1 · `QualityRevision.CompareTo` diverges from the surveyed rule

> **⚠ TRANSFORMED by Part 6.** Under a typed media model the ladder is Movies' own typed data, so the
> question stops being "which order does the *contract* impose" and becomes "does the media type own its
> own revision comparison?" — possibly not a contract decision at all. Re-frame before answering.

**What.** The contract orders revisions **version → real → repack**. Radarr's `QualityRevisionComparer`
orders **real → version**, and *repack takes no part in ordering at all* — it breaks a preference tie only.

**Consequences.** `(1 real, v1)` should beat `(0 real, v9)` and does not. `(v2, repack)` should tie
`(v2, proper)` and does not.

**Blast radius (narrower than it first looks).** Weight is compared first everywhere, so this only decides
between two revisions of the *same* rung. Real-world REAL counts rarely exceed 1 and version rarely exceeds
3, so in practice it surfaces as *repack beats proper* where Radarr calls them equal.

**Where it is pinned.** `src/Arronix.Plugin.Movies.Tests/Quality/UpgradeDecisionTests.cs` — two tests state
the surveyed rule verbatim and are `[Ignore]`d with the divergence spelled out; a live sibling,
`OrdersTheRevisionAxisTheWayTheContractStatesIt`, pins what the contract actually does. The divergence is
therefore measured from both sides. **Do not "fix" the tests** — the decision is which rule is right.

**Options.** (a) Change `QualityRevision.CompareTo` to the surveyed order and un-ignore the two tests.
(b) Keep the contract's order deliberately and delete the two tests as documenting a rejected rule.

---

### D-2 · The `title-respace` strategy binding is a no-op

> **⚠ DISSOLVED by Part 6 — do not action.** The named-strategy mechanism exists only because declarations
> are inert data and need a way to reach host behavior. With the media type as typed C#, acronym-rejoining
> is a method in the Movies assembly: no strategy registry, no `StrategyBinding`, no id to resolve, nothing
> to decide. Answering this would have authorized building a registry the typed pass deletes.

**What.** `Arronix.Plugin.Movies` binds the named strategy `title-respace` → `dotted-title-respace` with
exception words `["a", "dr"]`. `HostVocabulary` knows the id and the validation gate resolves it — but
**no engine executes it.** `DeclarativeReleaseParser.CleanTitle` describes itself as "the strategy-free
baseline every kind gets".

**Why it matters more than its size.** This is the *only* escape hatch the entire declarative design uses —
the one genuine algorithm in what was 15.9k lines. The named-strategy mechanism is currently declared,
validated, and unimplemented, so the design's answer to "what about real algorithms?" is unproven in code.

**Observable signature** (five test rows show it in both directions): dots that must survive are lost
(`L.A.` → `L A`, `Dr.` → `Dr`); dots that must go are kept (`Evangelion 1.11` stays dotted).

**Decision.** Implement acronym-aware rejoining as a host engine feature (new work, not wiring), or drop the
binding and accept the baseline. Implementing it also proves the strategy mechanism end-to-end, which is
worth something beyond the feature itself.

---

### D-3 · 60 of ~180 quality-corpus rows are unreadable by the declaration carrying them

**What.** The Movies quality corpus contains episode-named releases with no year — `Movie Name S01E04 DSR
x264 2HD`, `Some.Movie.S02E15`. The movie declaration correctly declines them; they were carried over
verbatim from an imperative scanner that read any text regardless of shape. Same shape in
`ReleaseGroupParserTests`: 32 of 141 rows are television or year-less releases (those 32 are `Assert.Ignore`d
individually so the other 109 run and pass).

**Where.** `ReadsEveryCorpusCaseOntoItsDeclaredRung` is left `[Ignore]`d rather than narrowed to the ~120
that pass, because narrowing hides the question.

**Options — deliberately not equivalent.**
1. **Drop the rows** — takes the corpus under the floor that `CarriesTheWholeSurveyedQualityCorpus` defends.
2. **Rewrite them as film titles** — changes the surveyed data, which is the thing that made the corpus
   trustworthy.
3. **Move them to the host** — concedes that a *rung* corpus is kind-agnostic (quality tokens are host-owned
   per the parsing design, so this may in fact be correct — but it is a real architectural admission).

---

### D-4 · File-template validity is inexpressible (R-HARD, confirmed twice)

> **⚠ DISSOLVED by Part 6 — do not action.** This measured the declarative *surface*, not the domain rule.
> The disjunction is inexpressible because `NamingToken.IsRequired` is a flat boolean in a data
> declaration; in typed C# it is a predicate you write. The "R-HARD" label was an artifact of the frame.

**What.** Radarr's rule for a valid file template is a **disjunction over token presence**: *(a title-form
token AND the year)* XOR *(original title / original filename)*. `NamingDeclaration` carries templates,
selection rows, styles, spine and fallbacks; `NamingToken.IsRequired` is **one flat boolean per token**.
Neither the disjunction nor the exclusivity can be declared.

**Status.** ~10 lines of rule, currently recorded only as prose in the `RequiredTokenNames` remarks in
`src/Arronix.Plugin.Movies/MoviesShape.cs`. Identified independently by the rendered exhibit and by the
conversion.

**Decision.** Extend the naming declaration with a required-token *expression* (the `TagPredicate` vocabulary
already exists and would likely serve), or accept that template validation stays host-side and kind-blind.

---

### D-5 · Three renames made beyond the literal brief (flagged for veto)

From the provider-family rename. Numeric enum values were left unchanged; these are naming only.

| Was | Now | Note |
|---|---|---|
| `Capability.ImportList` | `Capability.Curation` | The only capability named after a family rather than an activity. **Its wire name is a manifest key, so plugin manifests change.** |
| `CoreErrorCode.DownloadClientConnectionFailed` | `DownloaderConnectionFailed` | |
| `CoreErrorCode.MetadataProviderConnectionFailed` / `…SearchFailed` | `CatalogerConnectionFailed` / `CatalogerSearchFailed` | The pair encoded the now-deleted `IMetadataProvider`. |

---

### D-6 · Accepted coverage loss, recorded for the record

The 7 `StableProviderBridgeTests` were deleted with the legacy provider bridge. They tested only
`StableProviderBridge.Describe`/`.Wrap` projecting a deleted DTO onto `IndexerProfile`; there is no successor
contract to re-point them at, so the coverage died with the mechanism. `IndexerProfile`/`SearchProfile`
remain covered by `IndexerEligibilityTests` and `QueryPlanningTests`.

---

## Part 2 — Defects found by the end-to-end test (all fixed)

The first test to drive the **real** Movies definition through the **real** DI path found four defects that
every prior gate had missed. Recorded because the pattern matters, not just the fixes.

| # | Defect | Fix | Why it matters |
|---|---|---|---|
| **F-1** | Movies' folder template used Radarr's nested-brace grammar `{tmdb-{Movie TmdbId}}`; the host parser refuses a nested brace | Rewritten as `<{{tmdb-{Movie TmdbId}}}>` — escaped braces survive as literal output, optional group elides when there is no identifier. Same rendered folder. | The declaration was carrying grammar the engine never accepted; nothing checked template *syntax*. |
| **F-2** | **The binder threw instead of refusing.** An engine rejecting its definition at construction escaped `TryRegister` as `ArgumentException` | `Build` now catches `ArgumentException`/`InvalidOperationException` (exactly what the engines document themselves as throwing) and records a defect at the engine's path | **The most serious of the four.** Admission runs inside extension loading, so this would have taken down the whole load pipeline instead of quarantining one extension — breaking the design's central resilience promise (the deliberate inversion of Prowlarr's delete-on-missing). |
| **F-3** | The **host's** intent validator was wrong, not the plugin: `IntentSurfaceValidator` demanded `SequenceAxisId` for every `BrowseAxisKind.Sequence`, but the contract states on three separate members that a sequence traversal may run over a date field via `FieldId` | Accepts exactly one of the two | Movies is the first kind to exercise it (its shape declares no sequence axes). A first real consumer found a latent host bug. |
| **F-4** | Two Movies workbenches declared artwork columns; the host rule that artwork is not a cell is categorical | Two poster columns and the dead `PosterColumn` constant removed | — |

### F-5 · Open follow-up: the validation gate has a coverage hole

`ValidatedDefinition.TryValidate` passed Movies with **zero defects** while F-1 and F-4 were both real. The
gate checks cross-references but **not template syntax**, and the intent surface is only validated later,
inside `MediaKindRegistry.TryRegister`. So `HostGateTests` was never going to catch either. Worth closing —
otherwise "the gate passed" means less than it appears to.

### F-6 · Latent deadlock hazard, noted not hit

`InMemoryMediaStore.LinkAsync` calls `registered.Items.GetAsync` from *inside* its `SemaphoreSlim` gate. The
current item source is empty and gate-free, so nothing deadlocks today — but **any future store-backed item
source must use only the gate-free reads**. Noted in `Engines/Items/HostItemSource.cs`.

---

## Part 3 — Skipped tests, and what unblocks them

336 skipped solution-wide (335 in Movies + 1 pre-existing elsewhere). Not one is skipped for convenience;
each carries an inline reason.

| Count | Blocked on | Unblocked by |
|---:|---|---|
| 83 | The empty item source — `ItemSourceTests` 20, `ReleaseMatcherTests` 26, `QueryPlannerTests` 16, `RenamePolicyTests` 21 | **The storage milestone**, not more visibility |
| 23 | `NameFormatterTests` — asserts the shared sanitizer, reachable only through a rename that has an item to render | Storage; also kind-agnostic and covered in the shared assembly |
| 6 | `ForeignReleaseTests` — every case defers to the catalog | A catalog/metadata pipeline |
| 5 | `MovieTitleParserTests` — two kept no data rows; three need a tag key or a folder-vs-file parse mode that was not verified | Investigation (left ignored rather than guessed) |
| ~40 | Individual `[TestCase]` rows via `Assert.Ignore`, each with a per-row reason, so surrounding rows keep running | Various (see D-3) |
| ~178 | Remaining engine-blocked stubs whose bodies were replaced with `Assert.Fail` during the conversion and whose `[TestCase]` rows did not survive | **Authoring assertions** — restoring these means writing them, not un-ignoring them |

**Important nuance about the last row.** During the conversion, 150 method bodies were replaced with
`Assert.Fail("Unreachable: see the fixture remarks.")` and `[TestCase]` parameters renamed to `arg0, arg1`.
So those tests could not simply be "re-pointed" at the new path. Where the data rows survived, restoring was
legitimate (203 were recovered that way); where they did not, restoring would have meant *inventing*
assertions, which was correctly declined.

---

## Part 4 — Queued documentation (no decision needed, just not yet done)

The following were decided in conversation on 2026-08-17 and are recorded in session memory, but are **not
yet reflected** in `ARCHITECTURE.md` / `GLOSSARY.md` / `.github/copilot-instructions.md`:

1. **The four-category plugin taxonomy** — core / media-kind (declarative) / integration (imperative,
   capability-gated) / client (content-only).
2. **Prowlarr dissolves into the host** — indexer management is a host subsystem consuming indexer
   connectors; its `Applications/` sync subsystem has nothing left to do under a shared host. Do not port it.
3. **Web Push is de-reserved** — it and Webhook are ordinary `INotifier` implementations, not host-registered
   ids. `docs/design/pwa-and-push.md` still records the old reservation and must be corrected.
4. **Client plugins** — `Arronix.Client` becomes the first-party client plugin; client plugins are the
   highest-trust artifact and stay first-party-only until signing exists.
5. **The declarative media-kind model itself** — `docs/design/declarative-media-kinds.md` exists; the
   architecture documents do not yet describe it.

---

## Part 5 — Pass 2 review findings

Owner review of `Arronix.Plugin.Movies`, the first pure `MediaKindDefinition` (2026-08-17), after reading
the whole plugin. All five are agreed as pass-2 work. **P2-1 is the owner's leading item.**

> **Status as of the pass-2 close (2026-08-17).** Statuses below are recorded per item and were checked
> against the working tree, not against the plan. Summary: **P2-1 structurally closed for Movies and now
> mechanically enforced; P2-2 and P2-4 closed; P2-6, P2-7 and P2-9 closed by deletion; P2-3 measurable;
> P2-5 open and unchanged.** See `docs/design/typed-media-model.md` §9 for what landed and what did not.

These are not defects in the conversion — the conversion faithfully carried across what the imperative
plugin did. They are defects in the *declaration surface* that the conversion made visible for the first
time, which is what the exercise was for.

---

### P2-1 · The media kind knows about TMDb and IMDb — it should not

> **STRUCTURALLY CLOSED for Movies, and now enforced (2026-08-17).** The 93 hits in `MoviesShape.cs` are
> gone with the file: structure and intent are derived from `Movie`'s properties, and neither names a
> vendor. `LevelIdentity` states **roles** (`RequiredRoles`/`AdmittedRoles`) and the host binds schemes from
> installed catalogers. `IdentifierOrder` derives empty — which catalog to trust first is provider
> reliability knowledge, so it is host configuration.
>
> **Not yet fully closed.** Two vendor-naming sites survive by design, both "somebody else's wire format":
> `MoviesCatalogDeclaration.cs` (44 hits), which leaves wholesale at the cataloger milestone; and two
> release-title token patterns, because `tt1234567` and `tmdb-335984` appear in release names because a
> stranger typed them there. `MoviesParsing.cs`'s own `TmdbId`/`ImdbId` tag constants are in that second
> group. Everything else is mechanically refused by
> `TypedMediaKindGovernanceTests.NamesNoCatalogVendorInItsStructureIntentOrEngineInputs`, which caught a
> real leak on its first run (the `{Movie Id}` token's example value read `tmdb-335984`).

> *"why does it know about tmdb and imdb? they should be entirely behind `ICataloger`."*

**Measured: 217 provider references across all 10 files**, not merely in the catalog section.

| File | Hits |
|---|---:|
| `MoviesShape.cs` | **93** |
| `Definition/MoviesCatalogDeclaration.cs` | 44 |
| `MoviesIntent.cs` | 35 |
| `Definition/MoviesNotifications.cs` | 11 |
| `Definition/MoviesParsing.cs` | 10 |
| `Definition/MoviesQuerying.cs` | 6 |
| `Definition/MoviesMatching.cs` | 5 |
| `Definition/MoviesNamingDeclaration.cs` | 4 |
| `plugin.json` | 8 |

Every section is contaminated, and the worst offender is the file that is supposed to say *what a movie
is*.

**The structural error underneath is a taxonomy violation.** Per the owner's four-category taxonomy a
cataloger is an **integration plugin** — same category as qBittorrent and Discord. So
`Definition/MoviesCatalogDeclaration.cs` should not exist in this plugin at all: `Arronix.Plugin.Movies`
currently *contains a TMDb integration* (`movie/{tmdbId}`, `themoviedb.org/movie/{id}`,
`$.movieRatings.tmdb.value`). It belongs in a cataloger plugin that declares how to fill a movie shape
from TMDb, and a second cataloger must be addable without editing Movies.

**Specific couplings, worst first.**

1. **Five rating providers hard-coded into the shape** — `TmdbRating`, `TmdbVotes`, `ImdbRating`,
   `ImdbVotes`, `RottenTomatoesRating`, `MetacriticRating`, `TraktRating`. A rating is
   `(source, value, votes)`; this is the repeated-composite problem (see D-4's neighbour) *and* provider
   leakage at once. Adding Letterboxd currently means editing the media kind.
2. **`RequiredFields = ["tmdbId"]`** on the identifier query tier (`MoviesQuerying.cs`) — without a TMDb
   cataloger installed, movies can **never** be searched by identifier. A media kind has made itself
   dependent on one vendor being present.
3. **`IdentifierOrder = [imdb, tmdb]`** (`MoviesMatching.cs`), justified in-comment by *"a release source
   that reports both and gets one wrong almost always gets the TMDb one wrong"* — that is **provider
   reliability knowledge**, cataloger domain, not movie domain.
4. **`{Movie TmdbId}`** in the folder template — the Plex/Jellyfin identity stamp is a real user need, but
   it should stamp *the primary identifier*, resolved by whichever cataloger owns it.
5. **`tt\d{7,8}` / `tmdb(id)?-\d+`** extraction rules in `MoviesParsing.cs`, and
   `https://www.themoviedb.org/movie/{tmdbId}` link templates in `MoviesNotifications.cs`.

**Direction (not yet a decision).** The media kind declares abstract identifier **roles** (a primary
external identity, an optional secondary, a collection identity), ratings as one repeated composite keyed
by source, and query tiers that ask for *the primary identifier* rather than `tmdbId`. Catalogers declare
which concrete schemes they supply and bind to those roles. Consequence worth noting: `IdentifierOrder`
then becomes **host configuration over installed catalogers**, not a plugin constant.

---

### P2-2 · We chose C# to avoid a stringly-typed DSL, and built six of them inside strings

> **CLOSED by approach (2026-08-17).** Field ids, token names, browse/sort/filter rows, state descriptors
> and workbench columns are all derived from typed properties. What remains stringly-typed is recorded
> honestly as C1–C14 in `docs/design/typed-media-model.md`, and P2-5 below.

The design chose an internal C# surface specifically so declarations would be type-checked. In practice
the compiler checks the *record shapes* and nothing inside them. At least six distinct grammars live in
string values:

| Grammar | Example |
|---|---|
| template + optional/escaped groups | `"<{{tmdb-{Movie TmdbId}}}>"`, `"Grabbed{ in {quality}?}"` |
| format pipelines | `"{genres:take(5):join(, )}"`, `"{runtime:hours-minutes}"` |
| boolean/temporal expressions | `"now > inCinemas + theatricalWindowDays"` |
| JSONPath (**37 sites**) | `"$.movieRatings.tmdb.value"` |
| ordering / mapping micro-syntax | `"deleted<tba<announced<inCinemas<released"`, `"poster->poster, fanart->fanart"` |
| constraints | `"length 9..10"` |

**This fails the design's own acceptance test.** The critique's rule was *"the moment a declaration needs
an `if`, the design has failed at that point."* `DerivationKind.Conditional` is exactly that:

```csharp
["when"]  = FieldValue.OfText("premier present && premier.year != year"),
["value"] = FieldValue.OfText("premier.year")
```

Arguably worse than an external DSL: an external DSL has one grammar, a schema and a parser spec. Six
ad-hoc grammars in string properties have none, and no tooling.

**Direction.** Pick **one** expression grammar, specify it once, and make everything either use it or be a
real typed record. Also note 7 distinct predicate `Subject` strings (`"tags.SourceGroup"`, `"guard:…"`,
`"options.…"`, `"fields.…"`, `"reading.…"`, `"entry.…"`) and 21 bare id assignments in the same class of
problem.

---

### P2-3 · This is not yet "only the small differences"

> **MEASURABLE, not closed (2026-08-17).** The authored Movies kind is 854 lines
> (`Movie.cs` + `Movies.cs` + `Workbenches.cs` + the module); the other 1,217 are carried residue
> (parsing 472, corpus 319, catalog 317, ladder 109). What a second kind costs is the 854. Hold TV to it.

The stated goal: every media kind "basically looks the same, encoding only the small differences — schema,
naming, release models."

**4,316 lines, of which `MoviesShape.cs` + `MoviesIntent.cs` are 2,410 (56%)** — and neither shrank in the
conversion; Shape grew (1,379 → 1,445). The 15.9k → 4.3k reduction is real but concentrated entirely in
parsing, naming and catalog.

Nothing reads as a diff against a template. There is no shared skeleton, no base/builder making the four
kinds structurally identical — the `Definition/` folder layout is a convention one agent chose, not
anything enforced or shared. **The measure to hold pass 2 to: what does the *second* kind cost?** Books
should be closer to 400 lines than 4,000.

---

### P2-4 · Hand-maintained coupling a compiler should be doing

> **CLOSED (2026-08-17).** `RequiredVocabulary`, `StrategyRequirement`, `StrategyBinding` and
> `HostVocabulary` are **deleted**. There is no table to maintain because there is nowhere to put one: a
> typed kind's vocabulary is whatever it compiled against, and the compiler already checked it.
> `EnumOrdinalMaxima` went with them. The two match strategies the engines actually have are now *derived* —
> entry resolution had one implementation, and unit assignment is needed exactly when a kind's unit rules
> expand a span.

```csharp
[nameof(PredicateOp)]   = (int)PredicateOp.GuardMatches,
[nameof(CaptureTarget)] = (int)CaptureTarget.Tag,
```

`MediaKindDefinition.RequiredVocabulary.EnumOrdinalMaxima` asks the **author** to track by hand the highest
enum member their declaration uses, and to update it whenever they add a row. It is mechanically derivable
and will go stale. Same class: host vocabulary ids as bare strings — `"clean-title"`,
`"roman-numeral-variants"`, `"dotted-title-respace"` — and converter names such as `"language"`,
`"keep-empty"`, `"tmdb-collection-id"`. All resolved at load, never at compile, with no IntelliSense.

**Direction.** A source generator or analyzer derives the maxima and validates vocabulary ids against the
host's inventory at build time. This is also the natural home for validating P2-2's expression grammar.

---

### P2-5 · The regexes never became declarative

> **OPEN, unchanged (2026-08-17).** Confirmed still true after the typed conversion, exactly as Part 6
> predicted. `MoviesParsing.cs` is byte-identical.

The *ordering* around matching became data; the matching itself did not. `EditionRegex` (a ~500-character
alternation, referenced by three patterns and one guard), the `br-disk` mega-regex with nested lookaheads,
and `NormalizationOptions.StopWordExpression` with its lookarounds all moved from code into **string
constants, unchanged**.

Why it matters beyond aesthetics: these are the highest-regression-risk part of the whole plugin, they
cannot be validated, composed or diffed meaningfully, and they are opaque to any consumer of the
declaration that is not a .NET regex engine — which undercuts the claim that a definition is reviewable by
reading.

Formally this is a special case of P2-2 (regex is an embedded grammar), but it is listed separately
because the remedy differs: release-title parsing probably **cannot** eliminate regex, so the goal is to
*bound and structure* it — named, individually testable fragments with declared intent, composition
instead of one mega-alternation, and corpus coverage per fragment — rather than to replace it.

---

### P2-6 · Three parallel event vocabularies, one of them named after a subscriber

> **CLOSED for the kind's half (2026-08-17).** `NotificationDeclaration.Occasions` is **deleted from the
> contract**: twelve rows of English per kind, of which not one was about the media. Zero consumers existed
> in the host. The host-side vocabulary unification is untouched and remains open work — this closes only
> the part where a media kind carried a copy of it.

`NotificationEvent` names a set of **domain lifecycle facts** — `Imported`, `Upgraded`, `FileDeleted`
happened whether or not anyone is notified. Notification is one *consumer*, alongside the activity feed,
history, SignalR, telemetry, webhooks and automation. Naming the vocabulary after one subscriber is the
same error as `Capability.ImportList` (D-5) and as Movies knowing about TMDb (P2-1): **naming a thing
after something downstream of it.**

The cost is not cosmetic. The contract assembly currently carries three overlapping event models:

| Type | Members | Audience |
|---|---|---|
| `Events/IDomainEvent` | interface — id, timestamp, correlation | structural |
| `Wire/EventKind` | 8: `ItemChanged`, `JobProgress`, `JobCompleted`, `HealthChanged`, `QueueChanged`, `ActivityRaised`, `PluginStateChanged`, `ProviderStatusChanged` | the client |
| `Providers/NotificationEvent` | 15: `Grabbed`, `Imported`, `Upgraded`, … `HealthDegraded`, `HealthRestored` | notifiers |

`HealthDegraded`/`HealthRestored` and `HealthChanged` are the same fact at two granularities.
`Imported`/`Upgraded`/`ItemAdded` all reach the client as `ItemChanged`, losing what actually happened —
which is exactly why `EventEnvelope` carries a loose `string? State` and `string? Message` to smuggle the
detail back. The client cannot render an activity feed saying "upgraded" from the typed vocabulary alone.

**Direction.** One vocabulary of domain facts; consumers project subsets from it. A notifier subscribes to
a subset, the wire layer projects a subset, neither owns the names. Rename away from the subscriber
(`MediaEvent` / `LifecycleEvent` / similar) and delete the loose `State`/`Message` smuggling once the
projection is faithful.

---

### P2-7 · Kind-agnostic boilerplate declared per kind — already drifting

> **CLOSED for the members that had drifted (2026-08-17).** `NotificationDeclaration.ArtworkRoleOrder` and
> `GroupSummaryRule.ArtworkRoleOrder` are **deleted** — a platform-wide vocabulary restated per kind, with
> zero consumers. The match-confidence table joined them for the same reason but by a different route: it
> is now host policy in `MatchConfidencePolicy`, so a kind that says nothing gets the host's answer instead
> of copying it.

The `Occasions` table maps each event to an English phrase. Read the twelve and ask which is
movie-specific: *Grabbed · Added to the library · Upgraded · Renamed · Tags rewritten · Added · Removed
from the library · File deleted · Could not be grabbed · Could not be imported · Needs a decision ·
Complete.* **None.** Every one is identical for TV, music and books — 12 rows per kind, 48 across four
plugins, whose only possible future is silent divergence. It also hardcodes **English into a plugin**: a
host-owned table can be localized once; four scattered copies cannot.

One row is arguably genuine — `AcquisitionComplete → "Complete"` reads oddly for a film where "Season
complete"/"Album complete" mean something. Even that is derivable from the name of the shape's
completeness unit.

**The drift is not hypothetical — it has already happened.** The only two kinds that got as far as
declaring normalization disagree on the same English article rule:

| Kind | `LeadingArticles` |
|---|---|
| Movies | `["the", "a", "an"]` |
| Tv | `["the ", "a ", "an "]` — **trailing spaces** |

Two kinds, one rule, already divergent, and one of them is almost certainly a bug. This is the predicted
failure mode observed in the smallest possible sample.

Same class, same file set:
- **`OriginLimit`** — Movies declares Interactive 100 / UserInvoked 100 / Automatic 200 / Rss 200 /
  ReleasePush 200. Those are *host search policy* numbers, not movie facts; the other three kinds declare
  none, so behavior differs by kind for no reason.
- **`Transliterations`** (German umlaut rows) and `QueryRewrites` (drop leading article, `&`→`and`, strip
  apostrophes) — language handling, not media handling. Every kind with titles needs them.
- **`ArtworkRoleOrder`** — `poster`/`fanart`/`banner` are general media artwork roles.

**Direction.** Host-owned defaults with per-kind override where a kind genuinely differs. The review
question to apply to every row in pass 2: **is this a difference between media kinds, or is it here
because the surface demanded a row?**

---

### P2-8 · The surface demands rows that say nothing

> **CLOSED for the two rows this pass met (2026-08-17).** The gate no longer demands a match-confidence
> table (host policy supplies it) and no longer demands a hand-written unit-resolution row (the derivation
> produces the one a single-coordinate-space structure implies). Both were rows every kind would have
> written identically.

Two shapes of noise, both from the declaration surface rather than the domain.

**Declared-and-empty** — five in Movies alone: `HostVerbs = []`, `MultiUnitStyles = []`,
`PreRewrites = []`, `EscapeIds = []`, `Substitutions = []`.

**Declared-as-the-default** — five more: `PagingPolicy.Default`, `AmbiguityPolicy.Reject`,
`RungFallback.RoundUp`, `CrossFamilyRule.NeverCompare`, `CoordinateGrammar.None`. `MoviesQuality.cs`
admits it in its own comment: *"One family, so the rule is moot — and declared anyway."*

Ten rows carrying no information. Defensible individually as explicitness; collectively they are the
surface making every kind restate the default so a reader cannot tell a decision from a formality. Under
typed classes (Part 6) most vanish into property defaults and the rest become genuinely optional.

---

### P2-9 · The plugin knows the host's route shape

> **CLOSED (2026-08-17).** `NotificationDeclaration.DeepLinkTemplate` is **deleted from the contract**, so
> no kind can carry a route. `LinkTemplates` went with it, for the same reason one layer out: an outbound
> catalog address belongs to whoever owns the identifier. Both had zero consumers anywhere in the host —
> the derivation set them to empty and nothing ever read them. Enforced going forward by
> `TypedMediaKindGovernanceTests.CarriesNoRouteOrAddressInItsStructureIntentOrEngineInputs`, which reads
> the derived artifact rather than the source.

`MoviesNotifications.cs` declares `DeepLinkTemplate = "/kinds/movies/items/{itemId}"`. That is the
**API/client routing scheme**, hardcoded into a media kind. Change the route, version the API, or let a
different front end mount elsewhere, and every plugin carrying a deep-link template is silently wrong.

Same family as P2-6: the kind knows something about a downstream consumer. A kind should name the *item*;
the host resolves an item reference to a link for whichever surface is asking — which is also the only
form that works for a CLI or a mobile client.

---

## Part 6 — Pass 2 direction (agreed 2026-08-17)

The review in Part 5 converged on a single architecture. Recorded here as the target; P2-1, P2-2 and P2-4
are **resolved by approach** and P2-3 is largely dissolved, leaving P2-5 as the only survivor.

### The shape of it

**1. The schema is class definitions, fully typed — not string-keyed data.**

```csharp
public sealed class Movie : IMediaItem
{
    public required string Title { get; init; }
    public int? Year { get; init; }
    public Certification? Certification { get; init; }
    public IReadOnlyList<Rating> Ratings { get; init; } = [];
    // …
}
```

This is still declarative — it uses **C#'s own declarative features** (type declarations, attributes,
generics) instead of a hand-rolled sub-DSL carried in strings. The precedent is EF Core, which the owner
has also chosen for persistence: typed entity classes are the source of truth, a runtime model is
*derived* from them, and generic infrastructure never names the entities. Media model and persistence are
then built the same way.

**2. `IMediaType` is the polymorphic handle. The client's reference discipline does not change.**

`Arronix.Client` references `Arronix.Abstractions` only, exactly as today. Definition assemblies are
loaded at runtime (`BlazorWebAssemblyLazyLoad`, keyed off the installed-kinds list) and resolved through
DI as `IMediaType`; the client never names `Movie`. `IMediaType` surfaces the item `Type` so
`System.Text.Json` has a target, plus the derived model the generic browser renders from — so metadata
comes from the type and there is exactly one source of truth.

**3. Catalogers and curators are generic over the media type, with a non-generic host handle.**

```csharp
public interface ICataloger { MediaKindId Kind { get; } }
public interface ICataloger<TItem> : ICataloger where TItem : IMediaItem { … }
```

Registry, DI and dispatch hold `ICataloger`; the generic closes **inside the family**, where both sides
already know the type. The host stays kind-blind at compile time — the property that separates Arronix
from Jellyfin, whose `IRemoteMetadataProvider<Movie, MovieInfo>` works only because `Movie` is a core type.

**4. Catalogers are grouped by vendor, not by kind, with a shared base.**

```csharp
public abstract class Tmdb { /* auth, base URL, gateway calls, rate limiter, error mapping */ }

public sealed class TmdbMovies : Tmdb, ICataloger<Movie> { }
public sealed class TmdbTv     : Tmdb, ICataloger<Tv>    { }
```

The base earns its place on more than DRY: TMDb rate-limits **per API key, not per media kind**, so split
across two plugins the bindings would each hold their own limiter and jointly exceed the vendor's quota —
a fault that only appears under load and reads as vendor flakiness. Same for the auth token and for the
`/configuration` image-base-URL fetch, which is one call shared by every kind.

**5. Per-kind bindings ship as separate assemblies inside one plugin package.**

```
Arronix.Plugin.Catalogers.Tmdb/
  plugin.json                         ← declares: movies → …Tmdb.Movies.dll, tv → …Tmdb.Tv.dll
  Arronix.Catalogers.Tmdb.dll         ← the base; Abstractions only
  Arronix.Catalogers.Tmdb.Movies.dll  ← references Arronix.Plugin.Movies
  Arronix.Catalogers.Tmdb.Tv.dll      ← references Arronix.Plugin.Tv
```

Partial installation then works **by construction**: with Movies present and TV absent, the loader reads
the manifest, loads only the Movies binding, and never touches a type whose reference cannot resolve.
A single-assembly form would trip `ReflectionTypeLoadException` during type enumeration and take the whole
cataloger down rather than one binding. The manifest schema must therefore carry the **per-kind binding
assembly**, and the reference-graph check runs per binding.

**6. The reference rule becomes role-based, not family-based.**

> An integration plugin may reference the media-type assemblies of the kinds it declares support for —
> and nothing else.

A family rule ("a `Movies.*` plugin may reference `Movies`") does not fit: `Catalogers.Tmdb` references
both `Movies` and `Tv` and belongs to neither family. The role-based form stays mechanically checkable
(the manifest declares `mediaKinds`; the reference graph must be a subset of them plus Abstractions) and
still prevents the DTO duplication that Prowlarr's inability to reference sibling types forced.

### Consequences to carry into the work

- **Trimming is now a build-time rule, not an annotation.** Nothing statically references `Movie`, so the
  WASM trimmer is *correct* to strip it. Every definition assembly must be published untrimmed or fully
  rooted by a trimmer descriptor. This is the likeliest thing to be discovered late and painfully.
- **The descriptor projection survives only as an API concern**, for non-.NET consumers — a Python CLI
  cannot load the assembly, and the owner's requirement that intent be consumable by "a CLI, a mobile app,
  a TUI" still stands. It is projected from the typed model, so it is not a second source of truth.
- **WASM has a single ALC and no unloading**, so installing or removing a kind requires a client reload.
  It should be a deliberate prompted reload, not a mystery.
- **Version skew is solved but should be asserted**: the client must load the same build of a definition
  assembly the server loaded. The server serves it, so this holds naturally — assert it rather than
  assume it.
- **`ItemView`/wire DTOs shrink or disappear** for .NET consumers, since the typed model crosses directly.

### What this resolves

| Item | Status |
|---|---|
| **P2-1** provider leakage | **Resolved by approach** — TMDb moves to `Catalogers.Tmdb`; `Movie` keeps `Certification`, `Runtime`, `Collection`, which it legitimately owns. Ratings become a typed `IReadOnlyList<Rating>` rather than five vendor-named fields. |
| **P2-2** six string grammars | **Resolved by approach** — typed properties and typed mappings replace field ids, converter names and JSONPath. The compiler does the checking. |
| **P2-4** hand-maintained coupling | **Resolved by approach** — `EnumOrdinalMaxima` and vocabulary ids derive from types. |
| **P2-3** "only the small differences" | **Largely dissolved** — the typed class *is* the difference. Hold it to the same measure: what does the second kind cost? |
| **P2-5** regexes | **Survives unchanged.** Release-title parsing is still regex; typing the model around it does not help. Bound and structure it. |

### Rejected along the way, with reasons

- **`FieldSemantics` as the cataloger binding surface** — lossy and ill-defined; cannot express
  `certification`, `runtime`, `youTubeTrailerId`.
- **A shared external field vocabulary "both reference and neither owns"** — that is baking the coupling
  somewhere worse. Movies genuinely owns its fields.
- **Eliminating the cataloger↔kind coupling** — mistaken. A movie cataloger knowing about movies is what
  it *is*; only the kind knowing about a *vendor* was wrong.
- **A family-scoped client reference relaxation** — unnecessary. The client operates polymorphically
  through `IMediaType` and needs no reference to any media type.

---

## Part 7 — Parked kinds (2026-08-17)

**Nothing is parked. No project was removed from `Arronix.sln`.** This part exists so that a later session
does not go looking for a parked-plugins register that was planned but never needed, and does not mistake
the absence of one for an oversight.

### Why the parking clause never fired

The pass-2 plan expected the typed surface to break `Arronix.Plugin.Tv`, `.Music` and `.Books`, and provided
for removing each — with its test project — from the solution against a re-add trigger. It did not break
them:

> Tv, Music and Books were never on the string-declaration surface. They are **imperative** extensions —
> `AddMediaShape` plus one instance per seam. `MediaKindDefinition` had exactly one producer in the whole
> repository, and that was Movies, which had already converted.

So deleting `MediaKindDefinition`, `AddMediaKind`, `StrategyBinding`, `StrategyRequirement`,
`RequiredVocabulary` and `HostVocabulary` removed code with **zero production producers**. The only things
that broke were test fixtures that constructed the deleted types, and those were re-pointed at
`MediaKindModel` rather than deleted.

### Current state of the three unconverted kinds

| Project | Solution | Path it uses | Tests | Converts when |
|---|---|---|---|---|
| `Arronix.Plugin.Tv` | in | imperative per-seam | `Arronix.Plugin.Tv.Tests` — **84 passing** | its own iteration; expected to force changes to Movies |
| `Arronix.Plugin.Music` | in | imperative per-seam | `Arronix.Plugin.Music.Tests` — **66 passing** | after TV |
| `Arronix.Plugin.Books` | in | imperative per-seam | no dedicated project; covered by `Arronix.Architecture.Tests` | after Music |

`IPluginRegistry` carries both `AddMediaType<TItem, TType>()` and the per-seam registrations, and
`PluginBootstrapper.TryAdmitMediaKinds` admits typed and imperative kinds through the same registry. A seam
is deleted only when its last imperative implementer converts — so the per-seam methods go when Books does,
not before.

### Coverage genuinely lost this pass

Small, and all of it coverage of deleted features rather than of behaviour:

- **6 cases** covering the strategy/vocabulary negotiation (`AnUnknownStrategyIsALoadFailure…`,
  `ARequiredStrategyVersionBeyondTheHost…`, `ARequiredVerbTheHostDoesNotSpeak…`,
  `ARequiredEnumOrdinalBeyondThisHost…`, `TheHostVocabularyCarriesTheDesignsInitialStrategyInventory`,
  `AnUnknownStrategyBindingRefusesTheEngineAtConstruction`). A typed kind names no strategy by string, so
  there is no identifier that can be unknown. Replaced by
  `MatchEngineAssignmentTests.AKindWhoseUnitsCannotSpanGetsNoAssignmentStrategy` and its counterpart, which
  pin the derivation those strings used to configure. A note in `DefinitionValidationTests` records the
  deletion in place.
- **2 cases** in `Arronix.Abstractions.Tests` over `RequiredVocabulary` and `StrategyBinding` defaults.

**Two cases were un-parked**: `CapabilityDeclarationTests.ConfiguringTheExtensionRegistersSomething` and
`NoCapabilityIsHeldOnlyBecauseAnotherImpliedIt` were excluded for Movies while it had no way to register.
`AddMediaType` landed, the exclusion list is deleted, and both now run against all four extensions.

---

## Part 7 — Quality axes: critique outcome (2026-08-17)

Design docs: `docs/design/quality-axes.md` (1,928 lines), `docs/design/clean-room-plan.md` (650 lines).
Adversarial critique: session scratchpad `quality-axes-critique.md` (1,044 lines) — verdicts verified by
the orchestrator against the critique's own hand-mapped evidence.

**Verdicts.** Clean-room plan: *proceed with additive amendments, none blocking* (its inventory numbers all
verify; it found a second copy of ported fixture data in `ParseEngineFixtures.cs`, and the critique added
`ReleaseTagScanner.cs` + four more fixtures and re-sized the docs scrub — 566 `.cs:NN` citations in
`docs/`, ~10× the plan's estimate). Quality axes: *proceed with amendments — three blocking, one headline
retraction*.

**R-A1, the blocking refutation (with proof).** The design celebrated that Remux→Bluray and WEBDL→WEBRip
are "the same relationship" — one step on the `Generation` axis. The critique proved that is exactly what
breaks it: with per-axis preferences only, **no policy can hold `WEBDL ≈ WEBRip` and `Bluray < Remux`
simultaneously** — tying Generation {0,1} ties both pairs; ordering Generation strictly orders both pairs.
The shipped default silently chose strict and lost the WEBDL≈WEBRip equivalence the design's own §2.1
promised. Do not start implementation until this is answered; the answer decides whether `AxisPreference`
needs a cross-axis mechanism, which changes contract + profile editor + defaults together.

**The reality test: 30 real titles hand-mapped — 11 clean, 9 changed answers/labels, 10 unmappable**
without unspecified material. The critique's summary is the honest frame: *"the axes model is better than
the ladder over the clean cross-product (1440p, AV1, 1080p-from-anywhere all work; the ladder cannot
express any of them) and worse over the messy residue — and the messy residue is precisely where the 101
rung rules came from. A model that handles the cross-product and drops the residue has replaced the part of
the ladder that was easy."* The 10 unmappable rows cluster: per-kind guards the family-level `Read` cannot
consult (german-remux, anime-web), orphan-remux inference, container-evidence rows, conflicting same-source
resolution claims, DV+HDR10+ dual dynamic range, REPACK2-style corrections, and labels the renderer refuses
(`Remux-480p`).

**Other SOUND findings, briefly:** S-A1 precedence has no *exchange rate* — the one thing scalar weights
could do and real TRaSH-style profiles use (trade-off scores, not just ordering); S-A2
`UnknownEvidence.Ignore` admits strict preference cycles (counterexample given) — the upgrade loop can
oscillate; S-A3 comparison is provenance-blind, so a title that over-claims resolution is re-grabbed
forever (needs `EvidenceSource` in the judgement); S-A5 `ReleaseEvidence` cannot supply 4 of 13 declared
video axes and no work package builds the missing scanners; R-A7 "structurally unrepresentable"
cross-family comparison is in fact a runtime `ArgumentException` — an overclaim to retract in the doc.

**Clean-room amendments (additive):** budget the black-box oracle (it is an unbounded reconstruction
channel that partly undoes source quarantine); the gatekeeper's similarity score is a hill-climbable
gradient — report pass/fail only; corpus reduction currently loses the negative cases, which carry most of
the corpus's value; spec sanitization is syntactic while the disputed material is semantic.

**D-7 — RESOLVED (owner, 2026-08-17): Remux becomes its own `Origin` member.** The untouched-disc case
is promoted out of `Generation`, so the disc pair separates strictly on `Origin` (Remux > HDDisc) while
`Generation` stays tieable {0,1} for the web pair. Zero new policy mechanism; the per-axis profile UI
survives; the ladder's separate Remux rung is revealed as having accidentally encoded this all along. The
design's "same relationship" observation is retained as an insight but retracted as a modeling decision.

**D-8 — RESOLVED (owner, 2026-08-17): precedence core + bounded facet scores.** The primary axes
(Resolution, Origin, Generation, Corrections) remain strictly lexicographic — readable and cycle-free by
construction. Beneath them an optional additive scoring tier over declared facets (dynamic range, audio,
release-group tiers) consulted ONLY when the core judgement is a tie — the legitimate successor to custom
formats, confined where it cannot destabilize or cycle the core ordering. S-A1 is thereby answered;
S-A2's cycle risk must additionally be closed for the core tier itself (restrict `UnknownEvidence.Ignore`
or prove acyclicity) as part of the amendment pass.

**Q6 — RESOLVED (owner, 2026-08-17): truth over familiarity.** Sub-1080 Remux labels render what the
evidence says (`Remux-480p`, `Remux-720p`); no collapse to `Bluray-*` for familiarity. Plausibility is the
computed size gate's job, not the label's. **QA-j** (the "1080p Remux over 2160p WEB" shape migrating as a
cutoff, not an ordering) was presented alongside and the owner proceeded — treated as accepted; reopen
only with evidence from real profile usage.

---

## Part 7 (continued) — Quality axes: what landed, what did not (gatekeeper closeout, 2026-08-17)

> **Verified against the working tree, not against any session's report.** Where a report and the tree
> disagree, the tree is recorded. Build 0 warnings / 0 errors; **2,738 passing, 333 skipped, 0 failing**
> across eight assemblies (Common 420, Host 937, Movies 587 +332 skipped, Plugins 296, Abstractions 177,
> Architecture 171 +1 skipped, Tv 84, Music 66).
>
> The gatekeeper artifacts are new and live under `docs/provenance/`: `differential-report.md` (the oracle
> ledger), `gate-results.md` (the textual-derivation gate) and `quarantine-manifest.md` (the residual
> inventory). The first two are **not readable by any implementer session** — one carries per-case oracle
> answers, the other carries similarity values.

### Work packages, verified

| WP | State | Evidence in the tree |
|---|---|---|
| QA-1 Framework core | **landed** | `Abstractions/Quality/` — 46 public types, all `[Experimental(ARX0021)]` |
| QA-2 Attributes + derivation | **landed** | `AxisAttribute.cs`; `Host/Engines/Quality/QualityAxisReader.cs` |
| QA-3 Type seams | **landed** | `IQualityFacts`, `IQualityType{,OfT}`, `IQualityTypeBuilder`, `ReleaseEvidence`, `MediaProbe` |
| QA-4 Policy core tier | **landed** | `QualityPolicy.For/Compare/Admits/IsGoodEnough/Decide` implemented, not merely declared |
| QA-5 Policy prose renderer | **partial** | `QualityPolicy.Describe()` exists; the separate `QualityPolicyDescription.cs` does not. **Carries a known defect** — see below |
| QA-6 Size model | **landed** | `SizeExpectation.cs`, `Families/VideoSizeModel.cs`, all five §4 cross-checks re-run as assertions |
| QA-7 Video family | **landed** | `Families/{VideoQuality,VideoQualityType,VideoLabels,VideoDefaults}.cs` — 12 axes, §7's 16-row table, §5.1's policy |
| QA-8 Audio / written / spoken families | **not started** | `Families/` holds video only |
| QA-9 Builder surgery | **landed** | `Ladder` → `Quality<>`/`RefinedBy<>`; `FormatFamily.Quality` added, "a ladder or a model, never both and never neither" enforced |
| QA-10 Host evaluator | **landed** | `Host/Engines/Quality/` — 11 files |
| QA-11 Parity harness | **NOT as specified** | `Host.Tests/Quality/LadderAxisParityTests.cs` and `docs/design/quality-divergences.md` **do not exist**. What exists is `Movies.Tests/Quality/QualityDivergenceRegister.cs` — 23 of 220 rows, capped at 30 and ceilinged at 15% |
| QA-12 Movies switch | **landed** | `MoviesLadder.cs` deleted; `MoviesParsing.cs` 472 → 261; `MoviesVideoRefinement.cs` added |
| QA-13 Token binding | **not started** | `ShapeTokenDeriver.cs` still reads `file.Quality` and `QualityRevision` |
| QA-14 User axes | **not started** | no `UserAxis.cs` |
| QA-15 Analyzer rules | **substituted** | no `Arronix.Analyzers` project. Replaced by tests — see below |
| QA-16 Old surface deletion | **not started, correctly** | Stage E; every ladder type still has consumers |
| QA-17 Host scanner additions | **landed, by rewrite rather than by edit** | new `Host/Engines/Parsing/Evidence/` (14 files, 252 tests). The three files QA-17 named for editing were **left in place** |
| QA-18 Live preview + profile editor | **not started** | no `QualityPreview.cs` |
| QA-19 Facet tier | **landed** | `FacetScoring.cs`, `FacetScore.cs`, bounded at 10 facets |
| QA-20 Per-kind refinement | **landed** | `IQualityRefinement.cs`, `MoviesVideoRefinement.cs` |
| QA-21 Language axis wiring | **partial** | `LanguageClaim.cs` and `EvidenceLanguageScanner.cs` land; the §2.1 `Languages` **axis** does not compile — see D-9 |

### Two sequencing violations, recorded because both were gates

**QA-11 was the gate on QA-12 and it was not built.** Its own note reads *"Stage C, and the gate on
deleting anything."* `MoviesLadder.cs` and the 101 rung rows were deleted without a parity harness ever
running. The substitute — a 23-row divergence register with a cap and a percentage ceiling — is a genuine
and well-argued instrument, but it measures the new model against the *corpus*, not against the ladder,
which is what parity meant. **Nothing is proposed to undo here**: the ladder is gone and rebuilding it to
measure it would be worse. Recorded so the register is not later read as evidence that parity was checked.

**QA-15 was scheduled to ship before QA-7 and shipped after it.** Its note reads *"Ships before QA-7, or
the guarantees are load-time again."* QA-7 landed first, so `ARXQ001`–`ARXQ004` were load-time-only for
the whole period. Closed now, partially — see the next section.

### D-9 · Two declared axes do not compile — needs an owner decision

`quality-axes.md` §2.1 declares a `Distributor` axis and a `Languages` axis. Neither can exist:

- **`Distributor`** would be `Evidence<Distributor>` over a wrapper struct. That satisfies the
  `TValue : struct` constraint but is absent from §1.3's derivation table (enum / `int` / `double` /
  `EvidenceSet<TEnum>`), so it has no `AxisForm` and no members. The wrapper solves the *constraint*
  problem and not the *derivation* problem.
- **`Languages`** would be `EvidenceSet<Language>`, and `EvidenceSet<T>` requires `struct, Enum` while
  `Arronix.Abstractions.DTOs.Language` is a **class**.

**Consequence.** "Prefer a German dub" and "refuse a dub" have no home in any profile. The German-remux
rule still works, because `ReleaseEvidence.Languages` is readable inside `Read` — which is where §6.3 puts
it — but that is a family rule, not a user-facing axis. **Options:** (a) add an open-vocabulary axis form
to §1.3; (b) make `Language` an enum-backed value for axis purposes; (c) drop both axes from §2.1 and say
so. Confirmed independently by three sessions (R16/R17 and the migration pass).

### Defects recorded against the tree

| # | Defect | Where |
|---|---|---|
| 1 | **`QualityPolicy.Describe()` renders raw magnitudes on a descending axis** — *"the richest generation, and beyond 1 re-encodes it stops mattering"* — which is exactly what §3.2's honest-prose note forbids | QA-5's, pinned only at the level it currently reaches |
| 2 | **§7 row 12 over-fires.** `CameraCapture` + `Audio ≥ LossyStereo` → `TELESYNC`; nothing emits `AudioPresentation.RoomCapture`, so *any* camera capture stating any audio codec renders `TELESYNC` | registered against corpus row q005 |
| 3 | **`ReleaseEvidence` cannot tell a codec named as a *format* from one named as an *encoder***, so a dubbed disc naming `x265` and one naming `HEVC` are indistinguishable before `Read` sees them | the only one of the 23 registered divergences that is a finding about the model rather than about scanner coverage (q110, q120) |
| 4 | **`ResolutionClaimForm` has no "not stated" member.** `LineCount` is 0, so a record with `StatedResolution == null` reads as claiming `LineCount` | `ReleaseEvidence.StatedResolutionForm` |
| 5 | **The rewritten evidence scanner reads fewer spellings than the tag scanner it replaces** — it was built against a 30-row sample and the Movies corpus is 220 | 21 of the 23 registered divergences; each closes by adding a phrase to `EvidenceVocabulary` |
| 6 | **`Host.Tests/Engines/QualityAxisFixtures.cs` is a second declaration of the video taxonomy.** Its own doc says its assertions are re-pointable at the real family without rewriting an expectation — that is now possible | 94 tests would move onto the shipped `VideoQualityType` and the fixture would delete |
| 7 | **`IQualityBuilder<TItem>` has no production consumer** — only a `Host.Tests` fixture. It *produces* `TierDefault`/`RungFallback`, which QA-16 owns | recommend it dies with them, not before |

### New this pass — the two negative-gate failures

The clean-room plan's step 6a sets a **100% hard gate** on negative cases. It **fails**, on four cases
across two spec rules. Full detail in `docs/provenance/differential-report.md` §4.

- **NEG-18 — `EvidencePackagingScanner` over-claims disc-image packaging.** `COMPLETE.BluRay.BDRip`,
  `…BRRip`, `…HDDVDRip` each claim `disc-image` even though the same title states a re-encode. Three of
  four cases fail. Over-claiming is the failure direction §3.3 singles out as the worse one: a false
  disc-image claim promotes a re-encode into the library as an untouched disc.
- **NEG-14 — `EvidenceSourceScanner` claims a source from a title word.** `Bluray Morning 1977 AVC`. One
  of six cases.

**Neither was fixed here, and the reason is a rule rather than a preference.** `clean-room-plan.md` §3.1
states that no session holds two roles, and the gatekeeper session has read the quarantined originals and
executed the oracle. Writing into `Engines/Parsing/Evidence/` from here would put dirty-side authorship
inside the rewritten artifact. What crosses back is what §3.1 control 5(b) and §4.3 prescribe — a boolean
and a spec-paragraph id.

### Governance added, and which mechanism was chosen

`ARXQ001`–`ARXQ006` were scheduled as Roslyn analyzer rules (QA-15). **Tests were chosen over an
analyzer**, and the reason is constraint rather than judgment: an analyzer needs an `Arronix.Analyzers`
project, and adding a project means editing `Arronix.sln`, which this pass may not do.

| New fixture | Rules | Scope |
|---|---|---|
| `Architecture.Tests/Contracts/QualityAxisDeclarationTests.cs` | `ARXQ001`–`ARXQ004` | reflects over every quality-facts type in the contract assembly. New coverage: the host's `QualityAxisReader` restates these rules **at load time**, so a family nobody registers was checked by nothing |
| `Host.Tests/Engines/QualityPolicyGovernanceTests.cs` | `ARXQ005`, `ARXQ006`, plus the facet bound | constructs every shipped family's **real** default policy. New coverage: `QualityPolicy.For` only fires when somebody calls it |

Both sweeps carry a non-vacuity guard, because a reflection sweep that finds nothing passes every rule
under it. 9 tests, all green. `ARXQ005` and `ARXQ006` were already pinned against a *fixture* in
`Abstractions.Tests/Quality/QualityPolicyDeclarationTests.cs`; what was missing was any check on the
family that ships.

**The gap an analyzer would close and these tests do not: a family compiled in somebody else's plugin.**
Recorded rather than papered over.

---

## Part 8 — Quality axes implementation: closeout (2026-08-18)

**Verified by the orchestrator:** build 0/0; **2,738 passing, 333 skipped, 0 failing** (skips DOWN from
336 — the migration retired D-1's two ignored tests and the corpus pin, superseded by the policy model).
The quality-axes model is SHIPPED: 46 contract types (ARX0021), policy engine with the provenance clamp,
evidence pipeline, Movies migrated, ladder and rung table deleted. D-1 is closed by supersession.

**Oracle discipline held: 1 of 12 budgeted runs used**, logged in `docs/provenance/differential-report.md`.
Candidate ahead of the superseded scanner on every dimension (codec 99.39% vs 50.76%). Textual-derivation
gate PASSES for the rewritten expressions (longest 34 chars; no fingerprint match) —
`docs/provenance/gate-results.md`.

**A moment worth recording: the gatekeeper refused to fix its own findings.** The negative gate fails on
four cases (one scanner over-claim class, one corpus-generator mis-attachment affecting 706 rows), and the
gatekeeper — having read the quarantined originals and executed the oracle — declined to write into the
rewritten scanners, because that would put dirty-side authorship inside the clean-room artifact. What
crosses back is a boolean and spec-paragraph ids. The discipline worked exactly as designed, at the cost
of a follow-up: **a fresh quarantined implementer fixes NEG-18/NEG-14 from spec ids alone.**

**License flip: BLOCKED — precise remainder recorded by the gatekeeper:**
- `src/`: 17 class-(c) files — `ParseEngineFixtures.cs` (a complete untouched second copy of the ladder,
  rung table and 16 expressions), `MoviesParsing.cs` still carrying 21 expressions incl. the ~442-char
  edition alternation and ~491-char disc probe, `MoviesCorpus.cs` 249 rows, six ported fixtures (three
  containing the literal `RADARR`), 53 citation sites.
- `docs/`: 532 citation sites across 17 documents.
- **Step 0 (source quarantine relocation) was never executed** — discipline was enforced by instruction +
  output testing, not removal; SHA-256 manifest now at `docs/provenance/quarantine-manifest.md`.
- **`NOTICE` missing: the Munkres MIT attribution obligation is unmet TODAY, independent of the GPL
  question.** This is the single most urgent licensing item.
- QA-11 (parity harness) never ran though it was styled "the gate on deleting anything" — what exists is a
  23-row divergence register; do not later read it as parity.

### P2-10 · Format-family vocabulary landed in the core (owner finding, 2026-08-18)

`Arronix.Abstractions/Quality/Families/` contains the entire video family (`VideoQuality`,
`VideoQualityType`, `VideoDefaults`, `VideoLabels`), and `Arronix.Host/Engines/Parsing/Evidence/` carries
video token vocabularies; `ReleaseEvidence` itself has video-only members (`ScanType`). Root cause is a
genuine bind: "the family type is shared by Movies and TV" + "plugins reference Abstractions only" forced
family types INTO Abstractions — compounded by an orchestrator brief mislabeling the scanners
"media-agnostic" (the survey's quality-parsing-is-identical observation was family-internal, wrongly
generalized). Governance never caught it: `MediaVocabulary.cs` polices media nouns, not family terms.

**Proposed fix (awaiting owner decision): family assemblies** — `Arronix.Family.Video` owning the quality
type, defaults, labels and scanner vocabularies; host keeps the agnostic lexer/merge; `ReleaseEvidence`
loses video members to family-extensible evidence. Requires the third role-based reference-rule extension:
*a media-kind plugin may reference the family assemblies of the families it declares; a family assembly
references Abstractions only.* Add family-vocabulary governance after relocation.

### Next iteration queue (ordered)

1. ~~`NOTICE` file (MIT/Munkres)~~ — **DONE 2026-08-18**: root `NOTICE` + full MIT block in `MunkresSolver.cs` header (the attribution obligation is now met); P2-7's article-drift also closed (TV aligned to the space-less convention with a word-boundary check)
2. Owner decision on P2-10 → family-assembly relocation pass
3. Fresh quarantined implementer: NEG-18/NEG-14 fixes + the remaining 21 `MoviesParsing.cs` expressions
4. Corpus-generator fix (the 706 mis-attachments) + replace `MoviesCorpus.cs`/fixture rows
5. Step 0 retro-execution + citation scrub (53 src / 532 docs sites)
6. Then, and only then: the license flip — owner's action

**P2-10 extended (owner, 2026-08-18) — vocabulary openness, naming, and scope.** The owner's review of
`EvidenceTokens.cs` established three additions to the relocation pass:
1. **"Evidence" is the wrong name** — it names the pipeline, not the thing (the P2-6 error class). These
   are scene release-naming vocabulary; rename for what they are.
2. **Classify every vocabulary by openness**: closed-by-nature sets (packaging, dynamic-range kinds) →
   typed family enums; standard-backed-but-growing sets (codecs — `h266` had to be added mid-pass,
   proving a new codec currently costs a contract release) → family-owned recognition data;
   **living vendor sets (distributors) → open recognition tables, never constants** — family ships
   defaults, runtime-extensible, so a new streaming service is a data row, not a release. The shipped
   `EvidenceDistributorTokens` (19 vendors in the contract assembly) is the exhibit; the governance
   vendor-name rule was scoped to kind surfaces and missed core itself — widen it.
3. **Dispositions**: file formats — extensions already family data; container recognition joins it.
   Languages — the ISO-backed `Language` DTO stays core (external standard, media-agnostic); scene
   spellings (`MULTi`, `VOSTFR`, `German.DL`) move to family recognition data.
The owner's framing ("shouldn't distributors just be registration plugin based?") is treated as the
decision on P2-10's direction: family assemblies + open registries. Relocation pass launched 2026-08-18.
