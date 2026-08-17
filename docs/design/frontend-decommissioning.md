# Decommissioning the legacy React frontend — Plan (v0.1.0)

> **Status:** Plan. Nothing is deleted, moved or edited by this document. `frontend/`, `package.json`,
> `yarn.lock`, `tsconfig.json` and every build script are read-only inputs here.
>
> **Subject:** `frontend/` — the inherited Sonarr v5 React/Redux SPA (1,728 files, 8.9 MB,
> ~84,600 lines of TS/TSX/JS and 9,462 lines of CSS) plus the Yarn/webpack/TypeScript toolchain rooted at the
> repository root (`package.json`, `yarn.lock` 342 KB / 7,190 lines, `tsconfig.json`).
>
> **Replacement:** `Arronix.Client`, the Blazor WebAssembly PWA specified in
> `docs/design/unified-host-runtime.md` §6 and `docs/design/pwa-and-push.md`.
>
> **Governing documents:** `docs/design/unified-host-runtime.md` (§6 intent vocabulary, §6.5 workbench,
> §6.6 REST surface), `docs/design/pwa-and-push.md` (client shell, service worker, disconnected surface),
> `ARCHITECTURE.md` §7/§15, `docs/arronix-common-extraction-plan.md` (additive rule, three-tier promotion),
> `docs/contracts/stability.md`.
>
> **The real deliverable is §3 and §4.** §3 is the capability checklist that stops the new UI silently losing
> function; §4 is the list of things the intent vocabulary **cannot express today** — those are new
> requirements on `Arronix.Abstractions.Intent`, not opinions about the removal.

---

## How the contested calls were resolved

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | One atomic removal or incremental? | Atomic: one PR deletes `frontend/` + toolchain + Selenium + CI job. Incremental: staged over the milestone. | **Incremental, in five gates, with a single *deletion* commit at the end — and the window bounded at Gate B sign-off, no later than the first tagged Arronix release (§8.1)** | The legacy `Sonarr.sln` must keep building while the §3 inventory is being closed, because that inventory can only be *closed* by comparison against a running legacy UI; and once closed, a piecemeal deletion leaves a half-wired webpack config nobody owns. Both are reasons about *sequence*. Neither is a reason for an open-ended window, and an unbounded strangler-fig period is how a rewrite ends up shipping its predecessor. |
| 2 | Do the two UIs coexist behind one host? | Reverse-proxy both under one origin. Two processes, two ports. | **Two processes, two ports, no proxy** | The legacy SPA is served by `Sonarr.Http`'s `StaticResourceController` off `ConfigFileProvider.UiFolder`; `Arronix.Api` serves `Arronix:Api:ClientRoot` with its own SPA fallback (spec §6.1). Both claim `/{**path}`; merging them means one of them stops owning its own fallback, which is a debugging tax for zero benefit given no shared session and no shared database. |
| 3 | Is the legacy UI ported feature-by-feature, or is the inventory a checklist only? | Port screens. Use the inventory as an acceptance checklist for a generic client. | **Checklist only** | The whole falsifiable claim of spec §6.7 is that a fifth media kind costs *zero client changes*; porting screens re-introduces per-kind client code and destroys the claim on day one. |
| 4 | Does `Sonarr.Api.V3` leave with the SPA? | Yes, it exists to serve the SPA. No, integration tests use it. | **Not with the SPA, but scheduled — V3 and the test suite that drives it are removed together, in this milestone (§10)** | `InitializeJsonController.cs:48` points the SPA at `/api/v3` and `NzbDrone.Integration.Test/IntegrationTestBase.cs:102` also drives `api/v3/`. That is a reason to sequence the removal after D5, not a reason to keep a legacy API surface alive indefinitely: in a from-clean rewrite the legacy integration-test project leaves with the legacy API it tests, rather than acting as the reason to keep it. Owned by whoever retires `Sonarr.Http`; scheduled here rather than left on an unowned trigger. |
| 5 | Does `Sonarr.Api.V5` leave with the SPA? | It is the "new" API. | **No, and it was never coupled** | Verified: the SPA reads `window.Sonarr.apiRoot`, set to `/api/v3` only. `Sonarr.Api.V5` has zero SPA consumers, so it neither blocks nor is blocked by this work. |
| 6 | What happens to `NzbDrone.Automation.Test` (Selenium)? | Retarget at `Arronix.Client`. Delete. | **Delete, do not retarget** | Every assertion binds to React CSS-module class names (`div[class*='SeriesIndex']`, `input[class*='AddNewSeries-searchInput']`) and to the React spinner element `#followingBalls`; a Blazor client shares none of them, so "retargeting" is writing a new test project that happens to reuse a folder name. |
| 7 | Is a replacement browser-automation suite in scope? | Yes, at parity. No. | **No — deferred, with a named trigger** | Six smoke tests that click six nav links are not the reason to hold up a decommission; and the right target is `Arronix.Client` with stable `data-testid` hooks, which do not exist yet. Recorded in §10 rather than implied. |
| 8 | Localization — 45 locales exist and the spec says localization is out of scope. | Carry the 45 locale files forward. Accept English-only. | **Accept the regression, but record it as a shipping blocker for 1.0, not for the decommission** | `unified-host-runtime.md:2456` is explicit ("Localization is **out of scope**") and §8 defers `ILocalizedStrings` on the trigger "a second locale". 45 populated locale files **are** that second locale — see §4.1, gap G6. This is the single largest user-visible regression in the whole plan and it must be stated, not buried. |
| 9 | Should the legacy UI's client-persisted state (view modes, column sets, sort keys) be migrated? | Migrate `localStorage` shape. Drop. | **Drop** | Greenfield platform, no upgrade path from any existing *arr install (settled decision); there is no legacy install whose `localStorage` we would be reading. |
| 10 | Where does the go/no-go decision sit? | At the deletion commit. Before it. | **Before Gate D — at the moment the CI `frontend` job is removed from `deploy`'s `needs:`** | That is the last step that is a one-line revert; everything after it changes the release artifact's contents (§8). |
| 11 | Do we keep `frontend/src/Content/` (fonts, icons, manifest, robots.txt)? | Delete with the rest. Extract first. | **Extract first, delete with the rest** | `pwa-and-push.md` §3.2 already schedules new icon/screenshot asset production; but `Content/Fonts` and `robots.txt` are the only assets in `frontend/` with no successor named anywhere, and re-sourcing a licensed font after deletion is a real cost. One extraction step, done once. |

---

## 1. What actually exists

### 1.1 The tree

```text
frontend/                          1,728 files · 8.9 MB
├── build/webpack.config.js        the only build entry point
├── build/webpack/                 loaders/plugins support
├── babel.config.js · postcss.config.js · tsconfig.json
├── .eslintrc.js · .eslintignore · .prettierrc.json · .prettierignore · .stylelintrc
├── .tern-project · .vscode/{extensions,settings}.json
├── typings/{Globals.d.ts, element-class.ts, jdu.d.ts, worker-loader.d.ts}
└── src/                           31 top-level feature folders + 8 entry files
```

`src/` file counts, largest first — this is the shape of the port surface:

| Folder | Files | Folder | Files | Folder | Files |
|---|---:|---|---:|---|---:|
| `Components/` | 436 | `Activity/` | 55 | `Wanted/` | 8 |
| `Settings/` | 344 | `Calendar/` | 53 | `Organize/` | 7 |
| `Series/` | 151 | `Episode/` | 50 | `EpisodeFile/` | 5 |
| `Store/` | 117 | `typings/` | 49 | `RootFolder/` | 4 |
| `Utilities/` | 81 | `AddSeries/` | 43 | `FirstRun/` | 4 |
| `System/` | 62 | `App/` | 41 | `Quality/`, `DownloadClient/`, `Commands/` | 2 each |
| `InteractiveImport/` | 58 | `Helpers/` | 36 | `Tags/`, `Shared/`, `Season/`, `Language/`, `Diag/` | 1 each |
| | | `Content/` | 33 | `InteractiveSearch/` | 25 |
| | | `Styles/` | 13 | `Parse/` | 15 |

343 of those files are generated `*.css.d.ts` shims (`css-modules-typescript-loader`) — they are build
artifacts checked into the tree, and they inflate every naive file count of this directory by ~20 %.

Routes are declared in one place, `frontend/src/App/AppRoutes.tsx` — 27 routes across six groups (Series,
Calendar, Activity, Wanted, Settings ×14, System ×6). That file is the authoritative index of the legacy UI
and is the spine of the inventory in §3.

### 1.2 The root-level toolchain

| File | Size | What it is |
|---|---:|---|
| `package.json` | 159 lines | Name `"sonarr"`, version `4.0.0`. **66 runtime dependencies, 60 devDependencies.** Six scripts, all frontend-only. |
| `yarn.lock` | 342 KB / 7,190 lines | Yarn 1 classic lockfile. |
| `tsconfig.json` | 3 lines | `{ "extends": "./frontend/tsconfig.json" }` — exists solely so editors and `tsc` resolve from the repo root. Deleting `frontend/tsconfig.json` without deleting this leaves a file that cannot parse. |

`package.json` scripts, verbatim (`package.json:5–14`):

```json
"build":     "webpack --config ./frontend/build/webpack.config.js",
"prebuild":  "yarn clean",
"clean":     "rimraf ./_output/UI && rimraf --glob \"**/*.js.map\"",
"start":     "webpack --watch --config ./frontend/build/webpack.config.js",
"watch":     "webpack --watch --config ./frontend/build/webpack.config.js",
"lint":      "eslint --config frontend/.eslintrc.js --ignore-path frontend/.eslintignore frontend/",
"stylelint": "stylelint \"frontend/**/*.css\" --config frontend/.stylelintrc"
```

Every one of the six references `frontend/`. There is no script in `package.json` that survives the removal
of `frontend/`, which is why the file goes rather than being trimmed.

Node/Yarn pinning is via Volta (`package.json` `"volta": { "node": "20.11.1", "yarn": "1.22.19" }`) plus
`"packageManager": "yarn@1.22.22+sha512…"`. Both are node-toolchain declarations with no .NET consumer.

### 1.3 The build output contract

`frontend/build/webpack.config.js:12,18`:

```js
const uiFolder = 'UI';
const distFolder = path.resolve(frontendFolder, '..', '_output', uiFolder);
```

So webpack's entire contract with the rest of the repository is: **produce `_output/UI/`.** Nothing else in
the repo imports a TypeScript module, and — verified by grep across `src/**/*.csproj` — **no `.csproj`
references `frontend/`, `_output/UI`, or any frontend build output.** There is no MSBuild coupling to sever.
The coupling is in exactly two other places: CI packaging (§1.5) and runtime file serving (§1.4).

### 1.4 How the .NET stack serves it

Runtime resolution is one config property:

- `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:53` — `string UiFolder { get; }`
- `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:282` —
  `public string UiFolder => BuildInfo.IsDebug ? Path.Combine("..", "UI") : "UI";`

Seven mappers in `src/Sonarr.Http/Frontend/Mappers/` combine `IAppFolderInfo.StartUpFolder` with that value:

| Mapper | Line | Serves |
|---|---:|---|
| `IndexHtmlMapper.cs` | 23 | `UI/index.html` — SPA shell, with a cache-breaker regex rewrite (`HtmlMapperBase.cs:13`) |
| `LoginHtmlMapper.cs` | 22 | `UI/login.html` |
| `StaticResourceMapper.cs` | 26 | everything else under `UI/` |
| `FaviconMapper.cs` | 32 | `UI/Content/Images/Icons/*` |
| `ManifestMapper.cs` | 19 | `UI/Content/manifest.json`, url-base rewritten |
| `BrowserConfig.cs` | 14 | `UI/Content/browserconfig.xml` |
| `RobotsTxtMapper.cs` | 25 | `UI/Content/robots.txt` |

`src/Sonarr.Http/Frontend/StaticResourceController.cs` binds them to routes `""`,
`/{**path:regex(^(?!(api|feed)/).*)}`, `/content/{**path}` and `/login`, under `[Authorize(Policy="UI")]`.
That regex is the legacy stack's SPA fallback and it claims every path that is not `/api/` or `/feed/`.

**The API version the SPA actually uses.** `src/Sonarr.Http/Frontend/InitializeJsonController.cs:48` emits
`"apiRoot": "{urlBase}/api/v3"`, and `frontend/src/Utilities/createAjaxRequest.js:4` reads
`window.Sonarr.apiRoot`. **The legacy SPA talks to `Sonarr.Api.V3` and nothing else.** `Sonarr.Api.V5` has
no SPA consumer — a useful fact, because it means the decommission touches neither V5 nor any V5 work in
flight.

### 1.5 CI and packaging

`.github/workflows/build_v5.yml:96–121` — a standalone `frontend` job: `actions/checkout` → `volta-cli/action@v4`
→ `yarn install` → `yarn lint` → `yarn stylelint -f github` → `yarn build --env production` → upload artifact
`build_ui` from `_output/UI/**/*`. It is listed in `needs:` for `deploy` (`:217`) and `notify` (`:236`).

`.github/actions/package/action.yml:33–37` downloads `build_ui` into `_output/UI`;
`.github/actions/package/package.sh:5,19,26` sets `uiFolder="$outputFolder/UI"`, skips `UI` when iterating
runtime folders, and does `cp -r $uiFolder $sonarrFolder` into every platform package.

So the UI reaches the shipped artifact through CI only. A local `dotnet build` of `Sonarr.sln` has never
produced a UI and never needed one.

### 1.6 The Selenium suite

`src/NzbDrone.Automation.Test/` — 5 source files, in `src/Sonarr.sln`, driven by `scripts/test.sh`
(`:6` includes `Sonarr.Automation.Test.dll`; `:56` selects `Category=AutomationTest`).

`Sonarr.Automation.Test.csproj` carries the only Selenium references in the repository:
`Selenium.Support`, `Selenium.WebDriver.ChromeDriver`.

It is welded to React specifics:

- `PageModel/PageBase.cs:31–46` — `WaitForNoSpinner()` polls `By.Id("followingBalls")`, the React loading
  spinner element.
- `MainPagesTest.cs` — six tests asserting CSS-module-hashed class names:
  `div[class*='SeriesIndex']`, `div[class*='CalendarPage']`, `div[class*='Health']`,
  `input[class*='AddNewSeries-searchInput']`, plus `By.LinkText("Series"/"Calendar"/"Queue"/…)`.
- `AutomationTest.cs:52` — `driver.ExecuteScript("window.Sonarr.NameViews = true;")`, a React-only debug hook.

Every selector is invalidated by a Blazor client. This is a delete, not a port (resolution #6).

---

## 2. The two-host coexistence topology

They do not conflict, because they are different processes serving different origins:

```text
Legacy (unchanged, builds until D5 — §8.1)         New (additive)
┌─────────────────────────────────────────┐        ┌────────────────────────────────────┐
│ Sonarr.Console / NzbDrone.Host  :8989   │        │ Arronix.Api  :8990 (or configured) │
│  Sonarr.Http.StaticResourceController   │        │  UseStaticFiles over               │
│   → ConfigFileProvider.UiFolder ("UI")  │        │   Arronix:Api:ClientRoot            │
│   → _output/UI/ (webpack output)        │        │   → Arronix.Client publish output   │
│  API: /api/v3 (+ /api/v5, unused by UI) │        │  API: /api/v1  ·  HUB /hub/events   │
│  DB: legacy Sonarr SQLite               │        │  DB: Arronix schema v1              │
└─────────────────────────────────────────┘        └────────────────────────────────────┘
```

Both stacks claim a catch-all route (`^(?!(api|feed)/).*` on the legacy side, SPA fallback to `index.html`
on the new side). **Neither is modified.** Coexistence costs exactly one thing: two ports in a dev setup.
There is no shared session, no shared config file, no shared database, and — per the settled greenfield
decision — nothing to migrate between them.

This is why the removal can be sequenced by *readiness* rather than by *conflict*: nothing **technical**
forces the decommission at any particular moment. That is a gift, and §8 argues for spending it
deliberately rather than drifting — which means spending it against a deadline (§8.1), because "nothing
forces it" is precisely the condition under which a transitional period becomes permanent.

---

## 3. Feature inventory of the legacy UI

Legend for **Verdict**:

- **EXPRESSIBLE** — the intent vocabulary in `unified-host-runtime.md` §6.3–§6.5 can already describe it;
  the work is in `Arronix.Client`, not in Abstractions.
- **HOST** — not a plugin-declared concept at all; a host feature that needs an endpoint and a client
  screen, but no intent vocabulary.
- **GAP-n** — **not expressible today**; needs new vocabulary. Detailed in §4.
- **DROP** — TV-specific or otherwise not carried forward, with the reason.

### 3.1 Library browse and detail (`Series/`, 151 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Index with three presentations (poster / overview / table) | `Series/Index/{Posters,Overview,Table}/` | **EXPRESSIBLE** — presentation mode is a client choice; spec §6.7 selects a presenter per `BrowseAxisKind`. A *fourth* mode is a client change, not a contract change. |
| Configurable poster size, "show title/quality/monitored" toggles | `Series/Index/Posters/Options/` | **EXPRESSIBLE** — client-local view options over `FieldDescriptor.Prominence`. |
| Sort by 15+ keys, ascending/descending | `Series/Index/` sort selectors | **EXPRESSIBLE** — `SortOption(FieldId, Name, SortDirection)`. |
| Filter bar with saved **custom filters** | `Components/Filter/CustomFilters/`, `Store/Actions/customFilterActions.js` | **GAP-1** — `FilterOption` describes *what can be filtered*; nothing describes a **named, persisted, user-authored filter**. |
| Typed filter-value builders (14) — quality profile, tag, series status, indexer, language, protocol, queue status, history event type… | `Components/Filter/Builder/*FilterBuilderRowValue.tsx` | **EXPRESSIBLE** — `FilterOption.Operators` + `FieldDescriptor.Choices` / `OptionSourceId`. |
| A–Z jump bar for large libraries | `Components/Page/PageJumpBar.tsx` | **EXPRESSIBLE** — derived client-side from the `SortKey` field (`FieldSemantics.SortKey`). |
| Virtualized tables and poster grids (react-window) | `Components/Table/VirtualTable*.tsx` | **EXPRESSIBLE** — pure client concern; but see §4.6, it is a *performance* requirement the new client must meet. |
| Per-item progress bar (have/total episodes, size on disk) | `Series/Index/ProgressBar/` | **EXPRESSIBLE** — `ProgressSummary(Have, Want, Total, SizeOnDisk)`. |
| Detail page: season accordion, per-episode rows, file info, history tab, search tab | `Series/Details/`, `Season/`, `Episode/` | **EXPRESSIBLE** — `BrowseAxisKind.Sequence` over a `SequenceAxis` (spec §2.4). The tabs are client layout. |
| Global fuzzy search over the library, computed in a Web Worker | `Components/Page/Header/{SeriesSearchInput.tsx,fuse.worker.ts}`, `fuse.js` dep | **HOST** — the spec's `?q=` on `/items` is server-side; a client-side fuzzy index is a *different* feature with different latency characteristics. See §4.6. |
| Multi-select mode with shift-range and a select-footer | `App/SelectContext.tsx`, `Series/Index/Select/` | **EXPRESSIBLE** — `ActionScope.Selection` exists. |
| Move series (root folder change with path preview) | `Series/MoveSeries/` | **GAP-4** (path preview) — the *action* is `ActionScope.Item` + a `RootFolderRef` parameter; the **preview of what will move where** has no representation. |
| Delete with "also delete files" and a size warning | `Series/Delete/` | **EXPRESSIBLE** — `Consequence.Destructive` + `ConsequenceStatement` + a boolean `ActionParameter`. |
| Series-type / monitoring-option pickers | `Series/MonitoringOptions/`, `Settings` `seriesTypeSelect` | **DROP as TV-specific vocabulary; EXPRESSIBLE as mechanism** — `MonitorDimension` (spec §2.6) generalizes it; the *words* "anime/standard/daily" leave with Sonarr. |

### 3.2 Adding to the library (`AddSeries/`, 43 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Search a metadata source, pick a result, configure and add | `AddSeries/AddNewSeries/` | **EXPRESSIBLE** — `IMetadataSource` (spec §5.6) + an `ActionScope.Kind` action with parameters. |
| **Import existing library** — scan a root folder, match each folder to a catalog item, bulk-add | `AddSeries/ImportSeries/{SelectFolder,Import,Import/SelectSeries}/` | **EXPRESSIBLE via `WorkbenchDescriptor`** — this is `WorkbenchSubject.LooseFiles` with folder rows. A genuinely good fit that neither the spec nor either input design called out; worth recording as a fourth workbench (§4.7). |
| Root-folder picker with free space | `RootFolder/`, `Components/FileBrowser/` | **HOST** — root folders are a host feature (spec §6.3 affordance derivation names them so). |
| Server-side path browser modal | `Components/FileBrowser/` | **HOST** — needs an endpoint (`GET /api/v1/system/paths?path=`); `SettingRole.Path` declares the *intent*, but no browse endpoint is specified. Minor, listed in §9. |

### 3.3 Calendar (`Calendar/`, 53 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Month / week / forecast / agenda views | `Calendar/calendarViews.ts`, `Calendar/{Day,Agenda,Events,Header}/` | **EXPRESSIBLE** — `BrowseAxisKind.Sequence` over a `Date` field is explicitly "how a CALENDAR is expressed" (`unified-host-runtime.md:2379`). |
| Color legend by state (unaired / downloading / downloaded / missing / unmonitored) | `Calendar/Legend/`, `Calendar/getStatusStyle.ts` | **EXPRESSIBLE** — `StateDescriptor` + `StateTone`; `ToneMap` is one of the client's five lookup tables. |
| Options: first day of week, week column header format, time format, show episode info / finale icons | `Calendar/Options/CalendarOptionsModalContent.tsx` | **EXPRESSIBLE** — client-local preferences. |
| "Search all missing in view" button | `Calendar/CalendarMissingEpisodeSearchButton.tsx` | **EXPRESSIBLE** — `ActionScope.Selection`. |
| **iCal feed** — a shareable, tokenized `.ics` subscription URL with per-kind filters | `Calendar/iCal/CalendarLinkModal*.tsx` | **GAP-2** — zero occurrences of `iCal`/`ics` in the spec outside a regex. A calendar feed is not a screen; it is an *export surface with its own auth*, and nothing in Intent or Wire expresses one. |

### 3.4 Activity (`Activity/`, 55 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| **Download queue** — live rows from every download client, with progress, ETA, size, protocol, indexer, client, status message | `Activity/Queue/`, `Activity/Queue/Status/` | **GAP-3** — `unified-host-runtime.md:2544`'s `QueueEntryView(JobId, Owner, CorrelationId, Attempt, …)` is the **scheduler job queue**, not the download queue. The data exists provider-side (`DownloadClientItem`, spec:2253) but has no wire projection, no `/api/v1/downloads` endpoint and no actions. |
| Queue row actions: remove, remove+blocklist, remove+blocklist+search, manual import, grab pending release | `Activity/Queue/Details/`, `Store/Actions/queue*` | **GAP-3** — same. Also the only place in the product where "blocklist" appears as a verb. |
| Queue "pending" / delay-profile rows with a countdown and a force-grab | `Activity/Queue/Status/` | **GAP-3**. |
| **History** — paged event log with types Grabbed/Imported/Upgraded/Renamed/Failed/Deleted/Ignored, per-event detail (release title, indexer, quality, custom formats, download client, age, size) | `Activity/History/`, `Activity/History/Details/`, `typings/History.ts` | **GAP-3** — the spec has `HistoryEventKind` values inline at `:2104` (`Grabbed, Imported, Upgraded, Renamed, Retagged…`) and `DownloadAttribution` at `:2134`, but **no `HistoryEntryView` wire type and no `/api/v1/history` endpoint**. `EventKind.ActivityRaised` is a live push, not a queryable log. |
| **Blocklist** — persisted rejected releases with un-blocklist | `Activity/Blocklist/` | **GAP-3** — zero occurrences of "blocklist" in the spec. |

### 3.5 Wanted (`Wanted/`, 8 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Missing / Cutoff-Unmet lists with monitored filter, paging, per-row and bulk search | `Wanted/{Missing,CutoffUnmet}/` | **EXPRESSIBLE** — a `BrowseAxisKind.Facet` partition over a `Status` field, with `StateDescriptor` ids `missing`/`upgradable` and `ActionScope.Selection` search. Genuinely zero new vocabulary. |

### 3.6 Interactive release search (`InteractiveSearch/`, 25 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Release table: age, title, indexer, size, peers, language, quality, custom-format score, protocol | `InteractiveSearch/InteractiveSearch.tsx`, `Peers.tsx` | **EXPRESSIBLE** — `WorkbenchSubject.ReleaseCandidates` columns. |
| **Rejection reasons** per row, as a hoverable list | `InteractiveSearchRow.tsx:241–248`, column `rejections` at `:99` | **EXPRESSIBLE** — `WorkbenchRow.Issues : IReadOnlyList<ValidationFailure>` is a direct, faithful fit, and `ValidationSeverity {Warning, Error}` even captures the "rejected vs. warned" distinction the legacy UI blurs. **This is the single best match between the legacy UI and the new vocabulary and should be called out as such.** |
| Scene-mapping indicator | `ReleaseSceneIndicator.tsx` | **EXPRESSIBLE** — a `FieldDescriptor` with `StateTone`. |
| Filter the release list (quality, protocol, indexer, seeders…) | `InteractiveSearchFilterModal.tsx` | **GAP-5** — `WorkbenchDescriptor` has `Columns` and `Inputs` but **no `Filters`/`Sorts`**. A 300-row release list without filtering is unusable, and manual import has the same problem. |
| **Override match** — grab a release but tell the system it is a *different* series/season/episode, and optionally a different download client | `InteractiveSearch/OverrideMatch/`, `OverrideMatch/DownloadClient/` | **EXPRESSIBLE, awkwardly** — it is per-row editable cells over a `ReleaseCandidates` workbench, i.e. exactly what a workbench is. But it needs `OptionSourceId` row-scoping, which §6.5 has. Record as a workbench, not as a bespoke modal. |

### 3.7 Manual import (`InteractiveImport/`, 58 files)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Scan a folder, propose file→unit assignments, let the user correct series / season / episode / quality / language / release group / indexer flags / release type per row, then commit | `InteractiveImport/{Series,Season,Episode,Quality,Language,ReleaseGroup,IndexerFlags,ReleaseType,Folder,Interactive}/` | **EXPRESSIBLE** — `WorkbenchSubject.LooseFiles` with eight editable columns and row-scoped `OptionSourceId`. Spec §6.5 designed this deliberately after both input designs under-delivered, and it lands. |
| Per-row rejection/warning before commit | `InteractiveImport/Interactive/` | **EXPRESSIBLE** — `WorkbenchRow.Issues` + `WorkbenchProposal.Issues`. |
| "Apply this value to every selected row" | `InteractiveImport` bulk selects | **GAP-5** — a workbench has no notion of a bulk cell-set. On a 40-file season pack, setting the quality 40 times individually is the difference between a feature and a chore. |

### 3.8 Bulk editing (`Series/Index/Select/`)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Bulk edit: monitored, quality profile, series type, season folder, root folder, tags (add/remove/replace) applied to N items | `Series/Index/Select/Edit/EditSeriesModalContent.tsx` | **GAP-5** — spec §6.5 claims `WorkbenchSubject.LibraryItems` covers bulk edit. It does not, cleanly: the legacy interaction is **one form applied to N rows**, not N rows each individually edited. Both are legitimate; only the second is expressible. |
| Bulk delete with file-deletion toggle and a total-size warning | `Series/Index/Select/Delete/` | **EXPRESSIBLE** — `ActionScope.Selection` + `Consequence.Destructive`. |
| Bulk tag add/remove/replace | `Series/Index/Select/Tags/` | **EXPRESSIBLE** — `SettingRole.TagSet` as an `ActionParameter`, once GAP-5 is closed. |
| Bulk rename/organize | `Series/Index/Select/Organize/` | **GAP-4** — see §3.10. |
| **Season Pass** — a matrix of series × seasons with bulk monitor/unmonitor by row, column and pattern | `Series/Index/Select/SeasonPass/` | **DROP the screen, GAP-5 the mechanism** — "seasons" is TV vocabulary and does not carry, but *"bulk-set a monitor dimension across a sequence axis for N items"* is exactly the generalization, and it needs the same bulk-apply primitive. |

### 3.9 Settings (`Settings/`, 344 files — the largest subsystem)

| Section | Evidence | Verdict |
|---|---|---|
| Media Management (naming templates + preview, folder options, file management, permissions) | `Settings/MediaManagement/Naming/` | **EXPRESSIBLE partly** — `SettingRole.NamingTemplate` + `NamingToken` (resolution #44). The **live preview** of a template against a sample item is **GAP-4**. |
| Root folders | `Settings/MediaManagement/RootFolder/` | **HOST**. |
| Profiles: quality profile (drag-ordered ladder, grouping, upgrade-until, min/max score) | `Settings/Profiles/Quality/` | **EXPRESSIBLE** — `SettingRole.Ordering` + `QualityProfileRef`; drag-ordering is a client rendering of `Ordering`. |
| Profiles: delay, release (required/ignored terms) | `Settings/Profiles/{Delay,Release}/` | **HOST** (policy engine, deferred by the spec §8). |
| Quality definitions (size limits per quality, slider) | `Settings/Quality/Definition/` | **HOST**. |
| Custom formats (spec builders, scoring) | `Settings/CustomFormats/` | **HOST** — a policy-engine surface; deferred with it. Worth noting it is 40+ files of *form builder*, and the generic `SettingsField` renderer will not reproduce a nested boolean spec editor. **GAP-5-adjacent**, recorded in §4.5. |
| Indexers, Download Clients, Import Lists, Notifications ("Connect"), Metadata, Metadata Source | `Settings/{Indexers,DownloadClients,ImportLists,Notifications,Metadata,MetadataSource}/` | **EXPRESSIBLE** — this is precisely what `ProviderForm.razor` over `SettingsField[]` is for (spec §6.7). One form for five families is a real simplification over the legacy tree. |
| Provider field input kinds actually used: `text, textArea, number, float, password, path, file, date, check, select, tag, tagSelect, textTag, seriesTag, autoComplete, autoSuggest, dynamicSelect, deviceSelect, indexerSelect, indexerFlagsSelect, downloadClientSelect, languageSelect, qualityProfileSelect, rootFolderSelect, seriesTypeSelect, monitorEpisodesSelect, monitorNewItemsSelect, umask, keyValueList, oauth, captcha` (30 in `Components/Form/FormInputGroup.tsx:60–71`) | | **Mostly EXPRESSIBLE**; three are **GAP-7**: `oauth`, `captcha`, `keyValueList`. |
| Remote path mappings | `Settings/DownloadClients/RemotePathMappings/` | **HOST**. |
| Import list exclusions | `Settings/ImportLists/ImportListExclusions/` | **HOST**. |
| Tags + tag detail ("what uses this tag?") + **auto-tagging rules** | `Settings/Tags/{Details,AutoTagging}/` | **HOST**; auto-tagging is policy-engine. |
| General: host, security, proxy, logging, analytics, updates, backup | `Settings/General/*.tsx` | **HOST**. |
| UI: theme (auto/light/dark), color-impaired mode, first day of week, date/time formats, "show relative dates" | `Settings/UI/UISettings.tsx`, `App/{ApplyTheme,ColorImpairedContext}.tsx` | **EXPRESSIBLE as client-local**. Color-impaired mode in particular is an accessibility feature that must survive — see §4.4. |
| Unsaved-changes guard on navigation | `Settings/PendingChangesModal.tsx` | **EXPRESSIBLE** — client concern. |
| Advanced-settings toggle (hides ~40 % of every settings form) | `Settings/AdvancedSettingsButton.tsx` | **EXPRESSIBLE** — `SettingsField.Advanced` exists. Good. |

### 3.10 System, tools and diagnostics

| Legacy feature | Evidence | Verdict |
|---|---|---|
| Status: health checks with wiki links, about, disk space, "more info" | `System/Status/{Health,About,DiskSpace,MoreInfo}/` | **EXPRESSIBLE / HOST** — `HealthSnapshotView` + `HealthCheck` exist; disk space needs an endpoint. |
| Tasks: scheduled list with last/next run + manual trigger; queued list | `System/Tasks/{Scheduled,Queued}/` | **EXPRESSIBLE** — `JobView` + `POST /jobs/{id}/trigger` + `QueueEntryView`. A clean match. |
| Backups: list, create, restore, download, upload-and-restore | `System/Backup/` | **HOST** — zero spec occurrences. Endpoint + screen. |
| Updates: available versions, changelog, install now | `System/Updates/` | **HOST** — zero spec occurrences. |
| Log events table (filter by level, paged) and log **file** browse/download | `System/{Events,Logs}/` | **HOST**. |
| **Rename / organize preview** — show every file's current and proposed path before committing | `Organize/`, `Store/Actions/organizePreviewActions.js` | **GAP-4** — `Affordance.Renamable` says the level *can* be renamed; nothing describes a **dry-run diff surface**. This is the same missing primitive as the naming-template preview and the move-series path preview. |
| **Release-name parse tester** — paste a release title, see how it parsed | `Parse/`, `Store/Actions/parseActions.ts` | **HOST diagnostic** — no spec surface. Cheap and disproportionately useful for support; recommend keeping (§4.8). |
| Console/diagnostic API hook | `Diag/ConsoleApi.js` | **DROP**. |
| Sentry error reporting with source-mapped stacks | `Store/Middleware/createSentryMiddleware.js`, `@sentry/browser`, `stacktrace-js` | **DROP / defer** — a client telemetry decision that belongs with `pwa-and-push.md`, not here. |

### 3.11 Cross-cutting shell (`App/`, `Components/`, `Store/`)

| Legacy feature | Evidence | Verdict |
|---|---|---|
| SignalR live updates driving optimistic store patches | `Components/SignalRListener.tsx`, `@microsoft/signalr` | **EXPRESSIBLE** — `HUB /hub/events` + `EventEnvelope`, with per-kind groups (spec §6.6). |
| **Connection-lost modal** with auto-reconnect | `App/ConnectionLostModal.tsx` | **EXPRESSIBLE — already designed better.** `pwa-and-push.md` §1.5 calls the disconnected surface "the most important UI in this delivery". Carry the *intent*, not the modal. |
| App-updated modal ("a new version is running, reload") | `App/AppUpdatedModal.tsx` | **EXPRESSIBLE** — `pwa-and-push.md` §2.5 covers the stale-shell case explicitly. |
| Authentication-required first-run modal | `FirstRun/AuthenticationRequiredModal*.tsx` | **HOST**. |
| **Keyboard shortcuts** (`?` help modal, `Esc` close, `Enter` confirm, `s` focus search, `mod+s` save) | `Components/keyboardShortcuts.tsx:23–57`, `Components/Page/Header/KeyboardShortcutsModal.tsx` | **EXPRESSIBLE as client-local**, but see §4.4 — the legacy set is small and the new client should not merely match it. |
| Client-persisted UI state with **versioned migrations** | `Store/Middleware/createPersistState.js`, `Store/Migrators/migrate.js` | **EXPRESSIBLE**; the *migration* discipline is worth copying (§4.4). |
| **Metadata attribution** — "Metadata provided by TheTVDB", linked | `Components/MetadataAttribution.tsx` | **GAP-6-adjacent → GAP-8** — this is a **contractual obligation** of the metadata source, not decoration. TMDb, TheTVDB and MusicBrainz all require attribution. Nothing in `IMetadataSource` or the intent vocabulary carries an attribution string, and a plugin author cannot make the host display one. |
| Localization via `/localization`, 45 locale files | `Utilities/String/translate.ts`, `src/NzbDrone.Core/Localization/Core/*.json` (45 files) | **GAP-6** — the spec declares localization out of scope (`unified-host-runtime.md:2456`). |
| Modal stack, portal, focus-lock, drag-and-drop (multi-backend incl. touch), scroll-position restore, markdown rendering, tooltips, measure/resize observers | `Components/{Modal,Portal.tsx,Scroller,Markdown,Tooltip}`, `withScrollPosition.tsx`, `react-dnd-multi-backend` | **EXPRESSIBLE** — all client-internal. Note `react-dnd-multi-backend` + `react-dnd-touch-backend`: the legacy UI supports **touch drag-reorder on mobile**; Blazor gets nothing equivalent for free. Recorded in §4.6. |
| `mobile-detect` responsive behaviors | `Utilities/browser.ts` | **EXPRESSIBLE**. |

### 3.12 What should simply not be carried forward

| Legacy asset | Reason |
|---|---|
| All TV nouns in routes and components — `Series`, `Season`, `Episode`, `EpisodeFile`, `AddSeries`, `SeasonPass`, `seriesTypeSelect`, `monitorEpisodesSelect` | Invariant 1: no TV concept in Abstractions/Common/Plugins/Host/Api/Client. These become plugin-declared levels, coordinates and monitor dimensions. |
| `Store/` in its entirety (117 files: Redux, redux-actions, redux-thunk, reselect, connected-react-router, redux-localstorage, redux-batched-actions, zustand) | Replaced by `DescriptorCache` + component parameters (spec §6.7). No behavior is lost; ~117 files of state plumbing are. |
| `typings/` (49 files hand-mirroring the v3 API) | The whole point of `Arronix.Client` referencing `Arronix.Abstractions` is that the wire types **are** the client's types. A mirrored type is a guaranteed-divergence bug (resolution #32). |
| `jquery`, `jdu`, `react-addons-shallow-compare`, `prop-types`, `element-class`, `history@4`, `react-router@5` | Legacy-era dependencies with no successor concept. |
| `Diag/ConsoleApi.js`, `window.Sonarr.NameViews` | Debug hooks bound to the React build. |
| `login.html`, `oauth.html` as *separate webpack entry points* | The auth shape belongs to `Arronix.Api`; the OAuth landing page is GAP-7, but not as a second SPA bundle. |

---

## 4. Genuinely good ideas, and what the intent model cannot express

### 4.1 The capability gaps — ordered by how expensive they are to discover late

These are the findings that matter. Each is a **new requirement on `Arronix.Abstractions.Intent` /
`.Wire`**, or on the host's endpoint surface, and each is currently silent in
`docs/design/unified-host-runtime.md`.

| ID | Gap | Why the current vocabulary cannot express it | Recommended shape |
|---|---|---|---|
| **GAP-3** | **The download queue, history and blocklist.** Three of the five top-level nav items in every *arr. | `QueueEntryView` (spec:2544) is the **scheduler** queue — `JobId`, `Owner`, `CorrelationId`, `Attempt`. The download queue's rows are `DownloadClientItem`s joined to media units, with their own status vocabulary (`DownloadItemStatus`, spec:2257) and their own actions. `history` appears twice in 2,981 lines and `blocklist` zero times. | Three new `Wire` records — `DownloadQueueItemView`, `HistoryEntryView`, `BlocklistEntryView` — plus `GET /api/v1/downloads`, `/history`, `/blocklist` and a small closed action set (`downloads.remove`, `downloads.remove-and-blocklist`, `downloads.import`, `blocklist.remove`). All host-owned; **no new plugin-facing contract is needed**, because `IDownloadClient` already returns the data. This is the cheapest gap on the list and the most damaging to miss. |
| **GAP-4** | **Dry-run / preview surfaces.** Rename preview, naming-template preview, move-series path preview, "what will this profile change" — four screens, one missing primitive. | `ActionDescriptor` has `Consequence` and `ConsequenceStatement` (a *sentence*), but no way to return a **computed, per-item, before/after projection** for the user to inspect and then approve. `WorkbenchDescriptor` is close but is an *editing* grid over a proposal, not a *read-only diff* the user accepts wholesale. | Either (a) a `WorkbenchDescriptor` variant with all columns `Editable = false` and a `Preview` subject — cheapest, reuses one client component, and honestly covers all four; or (b) an explicit `PreviewDescriptor`. **Recommend (a)**: add `WorkbenchSubject.Preview` and let `AllowsRowExclusion` carry "rename these 12 of 40". One enum member. |
| **GAP-5** | **Bulk-apply, filter and sort inside a workbench.** Bulk edit (one form → N items), "apply quality to all selected rows", filtering a 300-row release list, sorting a manual-import proposal. | `WorkbenchDescriptor` carries `Columns`, `Inputs`, `CommitLabel` — and nothing else. §6.5's claim that it "covers bulk edit" is true only for the row-by-row reading; the legacy interaction is one form applied to a selection, which is a different shape. | Add to `WorkbenchDescriptor`: `IReadOnlyList<SortOption> Sorts`, `IReadOnlyList<FilterOption> Filters`, and `bool SupportsBulkAssign` per `WorkbenchColumn`. All three reuse types that already exist in `Intent`; none names a widget; all four pass the CLI falsification test (`--sort age`, `--filter protocol=usenet`, `--set-all quality=WEBDL-1080p`). |
| **GAP-6** | **Localization.** 45 populated locale files in `src/NzbDrone.Core/Localization/Core/`; `translate()` is called in essentially every legacy component. | `unified-host-runtime.md:2456` — *"Localization is **out of scope**: names are strings supplied by the plugin in the host's configured locale"* — and §8 defers `ILocalizedStrings` on the trigger *"a second locale"*. **That trigger is already met, 44 times over, in this repository.** | Do **not** build `ILocalizedStrings` for this milestone, but change its trigger from "a second locale" to "before 1.0", and record the regression explicitly in the release notes. The plugin-supplied-string design is right; the *deferral rationale* is factually wrong and should be corrected rather than quietly inherited. |
| **GAP-7** | **Interactive credential acquisition: OAuth and CAPTCHA.** | `SettingsField` (spec §5.2) is a static schema: `FieldId`, `ValueKind`, `SettingRole` (17 members), `Sensitivity`, `Choices`, `OptionSourceId`. Neither an OAuth authorization round-trip (`frontend/src/oauth.html` — `window.opener.onCompleteOauth(...)`; `Store/Actions/oAuthActions.js`) nor a CAPTCHA challenge/response (`Components/Form/CaptchaInput.tsx`, `Store/Actions/captchaActions.js`) is a value the user *types*. `POST /providers/definitions/{id}/test → ValidationOutcome` cannot carry a challenge. | Two additions, both semantic: `SettingRole.AuthorizationGrant` (the host mints a start URL and completes the exchange; the client only opens and closes a window — a CLI prints the URL and waits) and `SettingRole.Challenge` with a `ValidationFailure` extended to carry an optional `ChallengeRef {ChallengeId, Kind, PayloadUri}`. Without these, Trakt/Plex/Dropbox notifications and captcha-gated indexers are unconfigurable — a whole class of provider, not an edge case. Also add `FieldValueKind.KeyValuePairs` for `keyValueList` (custom HTTP headers, remote path maps). |
| **GAP-1** | **Saved, named, user-authored filters.** | `FilterOption(FieldId, Name, Operators)` declares *what is filterable*. A custom filter is a *persisted user artifact* — a name plus a predicate tree — that appears in a picker, is shared across views and survives a reload. Nothing in `Intent` or `Wire` represents one. | Host feature, not plugin vocabulary: `SavedFilterView(Id, Name, KindId, LevelId, PredicateJson)` + CRUD endpoints. The *predicate grammar* must be the same one `/items?filter=` already accepts, so it costs one serialization format, not a new concept. |
| **GAP-2** | **Calendar / iCal feed.** A tokenized `.ics` subscription URL, filterable, consumed by an external calendar app. | Not a screen and not an action: an authenticated **export endpoint** with its own token, plus a client screen that composes the URL. Zero spec coverage. | Host feature: `GET /feed/v1/calendar.ics?token=…&kinds=…&tags=…`. Note it needs a *separate* auth path (external calendar clients cannot carry a session cookie) — which is a security decision that belongs in `threat-model.md`, referenced not duplicated here. |
| **GAP-8** | **Metadata-source attribution.** `Components/MetadataAttribution.tsx` renders "Metadata provided by TheTVDB". | TheTVDB, TMDb and MusicBrainz all impose attribution terms. A plugin declaring `IMetadataSource` has no way to declare *"you must display this text and this link wherever my data appears"*, and the host has no way to honor it. This is a **license-compliance** gap dressed as a UI omission. | Add `AttributionNotice(string Text, Uri? Link, Uri? LogoUri)` to `ProviderDescriptor` for the `MetadataSource` family, surfaced by the host on the kind descriptor. Small, and it is the kind of thing that is discovered by a takedown request rather than by a design review. |

### 4.2 Ideas worth preserving that the vocabulary **does** express well

Called out specifically, because a rewrite is not automatically an improvement and these are the parts that
were right.

1. **Rejection reasons on release rows** (`InteractiveSearchRow.tsx:241–248`). The single most-praised piece
   of *arr UX: the app tells you *why* it will not grab something, in a list, per release. It maps onto
   `WorkbenchRow.Issues : IReadOnlyList<ValidationFailure>` with `ValidationSeverity {Warning, Error}` — and
   the new model is **strictly better**, because the legacy UI flattens warnings and hard rejections into
   one gray list. Preserve the feature; keep the severity distinction the legacy UI lost.
2. **Manual import as a first-class grid** (`InteractiveImport/`, 58 files). Spec §6.5 already treats this
   as the design's load-bearing case rather than an exotic one, and that judgment is correct: it is a daily
   workflow. The eight editable columns are the acceptance test for `WorkbenchColumn` + row-scoped
   `OptionSourceId`.
3. **Import-existing-library as a workbench** (`AddSeries/ImportSeries/`). Not identified as a workbench
   anywhere in the spec, but it is one — folder rows, a proposed catalog match per row, per-row
   correction, bulk commit. Adding it as a fourth declared workbench costs nothing and removes a whole
   bespoke screen. **Recommend recording it in §6.5's workbench list.**
4. **Advanced-settings toggle** (`Settings/AdvancedSettingsButton.tsx` ↔ `SettingsField.Advanced`). A
   settings form that hides 40 % of itself by default is why *arr settings are approachable. The declaration
   already exists; the client must actually honor it rather than rendering everything.
5. **The disconnected experience.** The legacy `ConnectionLostModal` is reactive and modal.
   `pwa-and-push.md` §1.5 already specifies something better. Named here only so the checklist records that
   the capability is not lost.
6. **Wanted/Missing as a facet, not a screen.** The legacy app has two hand-written pages;
   `BrowseAxisKind.Facet` over a status field produces them from declaration. A genuine simplification —
   worth stating, because most of this document is about what gets harder.
7. **`ProviderForm` unification.** Five settings sections (indexers, download clients, import lists,
   notifications, metadata) collapse to one form driven by `SettingsField[]`. In the legacy tree those five
   sections are ~200 of the 344 `Settings/` files.

### 4.3 Ideas that are good but are *client* obligations, not contract gaps

Listed so they do not fall between the two stools of "not in the spec" and "not my job".

| Capability | Legacy evidence | Obligation on `Arronix.Client` |
|---|---|---|
| Virtualized lists at 5,000–20,000 rows | `Components/Table/VirtualTable*.tsx` (`react-window`) | Blazor WASM has no equivalent for free. `Virtualize<T>` exists but is not a drop-in for a variable-height poster grid. **Budget it explicitly**; discovering it at 10k items is a rewrite. |
| Client-side fuzzy search in a Web Worker | `Components/Page/Header/fuse.worker.ts` | Sub-50 ms global search without a round trip. Either accept server `?q=` latency or budget a WASM-side index. State which. |
| Touch drag-and-drop (reordering quality profiles on a phone) | `react-dnd-multi-backend`, `rdndmb-html5-to-touch` | `SettingRole.Ordering` renders as a drag list; on touch it needs pointer-event handling the framework does not supply. |
| Color-impaired mode | `App/ColorImpairedContext.ts`, `Settings/UI` | `ToneMap` (one of the client's five lookup tables) must have a second palette, not just a color swap. Accessibility, and cheap if designed in at table-definition time. |
| Keyboard shortcuts | `Components/keyboardShortcuts.tsx:23–57` — five bindings | Five is a low bar. With `ActionDescriptor` in hand the client can offer a **generic command palette** over every declared action, which the legacy UI could never do because its actions were hard-coded. Recommend that rather than parity. |
| Versioned client-state migrations | `Store/Migrators/migrate.js` | The legacy app versions its persisted UI state and migrates it. Copy the discipline into `DescriptorCache`/preferences from day one. |
| Scroll-position restore across navigation | `withScrollPosition.tsx`, `Store/scrollPositions.ts` | Small, invisible when present, infuriating when absent. |

---

## 5. The removal sequence

Five gates. Each gate names **what must exist in `Arronix.Client` before it opens**, and each is
independently revertible up to Gate D.

### Gate A — Coexistence (no removal at all)

**Precondition:** `Arronix.Api` + `Arronix.Client` build and serve a shell (`pwa-and-push.md` WP-P03).

**Steps**

1. Add `Arronix.Api` / `Arronix.Client` to `Arronix.sln` only. `src/Sonarr.sln` is untouched.
2. Document the two-port dev setup (§2) in `CONTRIBUTING.md`.
3. **Do not** touch `package.json`, `frontend/`, CI, or `Sonarr.Http`.

**Reversibility:** total. Nothing has been removed.

### Gate B — Inventory closure (still no removal)

**Precondition:** `Arronix.Client` can render `MediaKindDescriptor` → browse → detail → action for at least
one plugin, and the workbench grid renders a proposal.

**Steps**

1. Walk §3 against a running legacy Sonarr and a running Arronix side by side. Every row gets a verdict of
   *covered*, *deliberately dropped*, or *gap*.
2. Every remaining gap becomes an issue against `Arronix.Abstractions.Intent` — **contract changes must land
   before the deletion**, because the legacy UI is the only artifact that can tell you a vocabulary gap
   exists, and once it is gone the gap is found by a user instead. (Below 1.0.0 the contract itself is free
   to change at any point — `docs/contracts/stability.md` — so this is a discovery deadline, not a
   versioning one.)
3. Close GAP-3, GAP-4, GAP-5, GAP-7 (§4.1). GAP-1, GAP-2, GAP-6, GAP-8 may ship after the deletion — they
   are host features or explicitly-accepted regressions, not contract shapes.

**Reversibility:** total.

**This is the gate that is skipped under schedule pressure, and skipping it is how a rewrite loses
capability.** It costs days; the alternative costs the download queue.

### Gate C — Extraction (first irreversible-ish step, but cheap)

**Precondition:** Gate B closed.

**Steps**

1. Copy out of `frontend/src/Content/`, into a new `distribution/branding/` (or wherever
   `pwa-and-push.md` WP-P01 puts assets): `Fonts/`, `robots.txt`, and any icon source not superseded by
   WP-P01. Record licenses.
2. Copy `src/NzbDrone.Core/Localization/Core/*.json` (45 files) to a preserved location **even though
   localization is deferred** — they are the corpus that makes GAP-6 solvable later, and they are cheap to
   keep and expensive to re-source.
3. Nothing is deleted yet.

**Reversibility:** total (additive copies only).

### Gate D — Decoupling (the point of no return; see §7)

**Precondition:** `Arronix.Client` covers the §3 checklist minus the explicitly-accepted drops, and a
release artifact containing `Arronix.Client` publish output has been produced at least once.

**Ordered steps — each is a separate commit, and the order matters:**

| # | Step | Why this order |
|---|---|---|
| D1 | Remove `frontend` from `deploy`'s and `notify`'s `needs:` in `.github/workflows/build_v5.yml:217,236` | One-line revert. **This is where the go/no-go sits (§7).** |
| D2 | Remove the "Download UI Artifact" step from `.github/actions/package/action.yml:33–37` and the `cp -r $uiFolder` from `package.sh:26` (and the `UI` skip at `:19`) | The package now ships without a UI. Verify the artifact still builds and the app still starts (it will — `IndexHtmlMapper` returns 404, it does not throw). |
| D3 | Delete the `frontend` job, `.github/workflows/build_v5.yml:96–121` | Nothing consumes `build_ui` any more. |
| D4 | Delete `src/NzbDrone.Automation.Test/` and its entry in `src/Sonarr.sln`; remove `Sonarr.Automation.Test.dll` from `scripts/test.sh:6`; drop the `Automation` branch at `scripts/test.sh:56` and the `Category!=AutomationTest` clauses in the CI test filters | Must precede D5: with no `_output/UI`, these tests fail rather than skip. |
| D5 | Delete `frontend/`, root `package.json`, root `yarn.lock`, root `tsconfig.json` | Single commit, ~1,730 files. |
| D6 | Remove `Selenium.Support` and `Selenium.WebDriver.ChromeDriver` from `Directory.Packages.props` | Only `Sonarr.Automation.Test.csproj` referenced them; a stale central-package entry is a warning waiting to happen. |
| D7 | Leave `Sonarr.Http/Frontend/` and `ConfigFileProvider.UiFolder` **in place** | See below — this is the deliberate call. |

**Why D7 leaves the serving code.** `StaticResourceController` and its seven mappers are ~350 lines that
now serve a directory that no longer exists. Removing them means editing `Sonarr.Http`, `NzbDrone.Core`
(`ConfigFileProvider:53,282`) and their tests — i.e. touching the legacy tree, which the additive rule
forbids and which risks the `Sonarr.sln` build the whole sequence is designed to protect. **A dead mapper
that returns 404 is cheaper than a live edit to the legacy stack.** It leaves with `Sonarr.Http` itself
(§10), not with the frontend.

**Verification after D5, in this order:**

```bash
dotnet build src/Sonarr.sln -c Release     # must succeed — no csproj referenced frontend/ (verified §1.3)
dotnet build Arronix.sln    -c Release     # unaffected
bash scripts/test.sh Linux Unit            # must succeed with Automation removed
git grep -n "frontend/\|yarn\|_output/UI\|package.json" -- ':!docs/'   # must return only this document's siblings
```

### Gate E — Cleanup

1. `.gitignore`: remove `node_modules`, `*.js.map` and any webpack-era entries.
2. `.github/dependabot.yml`: remove the npm ecosystem entry if present.
3. `README.md` / `CONTRIBUTING.md`: strip Node/Yarn prerequisites and `yarn start` instructions.
   *(Both files are owned by concurrent agents — raise as a request, do not edit.)*
4. `.github/copilot-instructions.md:8` currently reads *"Migrate UI/front end from the current
   TypeScript/React implementation…"* — after D5 it is describing a migration that has completed.
   *(Also concurrently owned.)*
5. **WP-D16 — the legacy API surface leaves too.** `Sonarr.Api.V3`, `NzbDrone.Integration.Test`,
   `NzbDrone.Api.Test` and `Sonarr.Http/Frontend/` are deleted together; see §10. Listed here so the gate
   is not read as ending at "the SPA is gone" while the API written to serve it survives.

---

## 6. What leaves with it — the complete dependency inventory

| Item | Location | What depends on it | Removed at |
|---|---|---|---|
| React/Redux SPA source, 1,728 files | `frontend/src/` | webpack config only | D5 |
| webpack 5 + `webpack-cli`, 12 loaders/plugins (`babel-loader`, `ts-loader`, `css-loader`, `mini-css-extract-plugin`, `terser-webpack-plugin`, `html-webpack-plugin`, `fork-ts-checker-webpack-plugin`, `filemanager-webpack-plugin`, `worker-loader`, `file-loader`, `url-loader`, `webpack-livereload-plugin`) | `package.json` devDeps; `frontend/build/` | `yarn build`, CI `frontend` job | D3/D5 |
| Babel 7 (`@babel/core`, `preset-env`, `preset-react`, `preset-typescript`, 2 plugins) + `babel.config.js` + `core-js` | root + `frontend/` | webpack | D5 |
| TypeScript 5.7.2 + `frontend/tsconfig.json` + root `tsconfig.json` + `typescript-plugin-css-modules` + `css-modules-typescript-loader` (and the 343 generated `*.css.d.ts`) | root + `frontend/` | webpack, `yarn lint`, editors | D5 |
| PostCSS 8 + `postcss.config.js` + `autoprefixer`, `postcss-{mixins,nested,simple-vars,url,color-function}` | root + `frontend/` | webpack | D5 |
| ESLint 8 + 8 plugins + `frontend/.eslintrc.js` / `.eslintignore`; Prettier 2.8 + `.prettierrc.json` / `.prettierignore`; Stylelint 15 + `.stylelintrc` + `stylelint-order` | root + `frontend/` | `yarn lint`, `yarn stylelint`, CI | D3/D5 |
| Yarn 1.22 classic + `yarn.lock` (342 KB) + Volta pin (node 20.11.1) + `packageManager` field | root `package.json` | CI `frontend` job | D3/D5 |
| 66 runtime npm dependencies — React 18, Redux 4 + `react-redux` 7, `react-router` 5, `connected-react-router`, `@tanstack/react-query` 5, `zustand` 5, `@microsoft/signalr` 8, `@sentry/browser` 7, `moment`, `lodash`, `jquery`, `fuse.js`, `mousetrap`, `react-window`, `react-dnd` ×4, `@fortawesome` ×5, `react-google-recaptcha`, `qs`, `filesize`, `mobile-detect`, … | `package.json:22–89` | the SPA | D5 |
| CI job `frontend` (volta / yarn install / lint / stylelint / build / upload `build_ui`) | `.github/workflows/build_v5.yml:96–121` | `deploy` `needs:` `:217`, `notify` `needs:` `:236` | D1→D3 |
| CI packaging: download `build_ui` → `_output/UI` | `.github/actions/package/action.yml:33–37` | `package.sh` | D2 |
| Packaging copy: `uiFolder="$outputFolder/UI"`, `cp -r $uiFolder $sonarrFolder`, the `UI` skip | `.github/actions/package/package.sh:5,19,26` | every platform artifact | D2 |
| Selenium suite — `AutomationTest.cs`, `AutomationTestAttribute.cs`, `MainPagesTest.cs`, `PageModel/PageBase.cs`, `app.config` | `src/NzbDrone.Automation.Test/` | `src/Sonarr.sln`; `scripts/test.sh:6,56`; CI test filters `TestCategory!=AutomationTest` | D4 |
| `Selenium.Support`, `Selenium.WebDriver.ChromeDriver` | `Directory.Packages.props` | only `Sonarr.Automation.Test.csproj` | D6 |
| `frontend/.vscode/{extensions,settings}.json`, `.tern-project` | `frontend/` | editors only | D5 |
| **`.csproj` coupling** | — | **NONE. Verified: no `.csproj` under `src/` references `frontend/` or `_output/UI`.** | n/a |
| **`Sonarr.Http.Frontend` mappers + `ConfigFileProvider.UiFolder`** | `src/Sonarr.Http/Frontend/` (7 mappers + controller), `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:53,282` | `Sonarr.Http` DI | Not at D5 — **scheduled with the legacy-stack removal (§10), in this milestone** |
| **`Sonarr.Api.V3`** | `src/Sonarr.Api.V3/` | the SPA **and** `NzbDrone.Integration.Test/IntegrationTestBase.cs:102` | Not at D5 — **scheduled with `NzbDrone.Integration.Test` (§10, WP-D16), in this milestone** (resolution #4) |
| **`Sonarr.Api.V5`** | `src/Sonarr.Api.V5/` | nothing in the SPA | **NOT affected** (resolution #5) |

---

## 7. Risks and the point of no return

### 7.1 The point of no return

**It is D2, not D5.**

The intuition says the deletion commit (D5) is the irreversible step. It is not: `frontend/` is 1,730 files
in git history and `git revert` restores it exactly, along with `package.json` and `yarn.lock`. Reverting a
deletion is a mechanical operation with a known cost.

D2 — removing the UI copy from `package.sh` and the artifact download from `package/action.yml` — is the
first step that changes **what users receive**. From D2 onward every published artifact is a Sonarr build
with no web interface. If that lands on a release branch, the recovery is not a revert; it is a revert plus
a re-release plus an explanation.

D1 (removing `frontend` from `deploy`'s `needs:`) is therefore the correct **go/no-go placement**: it is the
last step that is a one-line, zero-consequence revert, and it is the step that *proves* the pipeline no
longer needs the frontend without yet changing any output. Approve there.

Cost of reversal by gate:

| Gate | Reverting costs |
|---|---|
| A, B, C | Nothing — all additive |
| **D1** | **One line. ← place the go/no-go here** |
| D2 | One revert + one re-release if a build shipped |
| D3 | A revert, plus re-verifying the yarn toolchain still resolves (lockfile drift, transitive deprecations) |
| D4 | A revert; Selenium tests will still pass if `_output/UI` is restored |
| D5 | A revert of ~1,730 files, plus `yarn install` against a lockfile whose registry state has moved on. Weeks stale is fine; months is a dependency-resolution project. |
| D6, D7 | Trivial |

### 7.2 Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | **Gate B is skipped**; a capability in §3 is discovered missing after D5 | High — it is the boring gate | High — GAP-3 alone is three nav items | Make Gate B a named, signed-off deliverable with the §3 table as its artifact. This document exists to make skipping it visible. |
| R2 | A **contract-shaped** gap (GAP-4/5/7) is discovered after `Arronix.Abstractions` reaches 1.0 | Medium | High — `stability.md` makes additions to a stable contract a MAJOR conversation | Close all four before the deletion (Gate B step 3). They are enum members and record properties, cheap at 0.3.0. |
| R3 | `Sonarr.sln` breaks during D1–D6 | **Low** — verified: no `.csproj` touches `frontend/` | High | The verification block in Gate D runs after every D-step, not just at the end. |
| R4 | Localization regression (GAP-6) ships unannounced | High if unmanaged | High for non-English users — 45 locales imply a real non-English population | Explicit release-note statement + retarget the `ILocalizedStrings` trigger (§4.1). Do not present English-only as a design choice. |
| R5 | Blazor WASM cannot meet the legacy UI's list performance at 10k+ items | Medium | Medium — degrades the primary screen | Budget virtualization work explicitly (§4.3); prototype against a 20,000-item descriptor *during Gate B*, while the legacy UI is still there to compare against. |
| R6 | Assets (fonts, icons, robots.txt) are lost in D5 | Low, but silent | Low–Medium; re-sourcing a licensed font is a legal exercise | Gate C, done before any deletion. |
| R7 | Loss of the Selenium suite leaves the legacy stack with no browser-level coverage | Certain | Low — the legacy stack is being retired; the tests assert only that six nav links render | Accept. Record the replacement as deferred (§10) with `Arronix.Client` + `data-testid` as the target. |
| R8 | The two-host dev setup confuses contributors during Gates A–D | Medium | Low | One paragraph in `CONTRIBUTING.md`, two ports, no proxy (resolution #2). |
| R9 | GAP-7 (OAuth/CAPTCHA) is treated as cosmetic and deferred past 1.0 | Medium | High — a whole class of provider becomes unconfigurable | It is listed among the four **must close before deletion** gaps for exactly this reason. |
| R10 | `package.json`'s 126 total dependencies mask a supply-chain surface that is currently unmonitored | Certain today | Removing it is the mitigation | A pleasant side effect worth stating: D5 removes 126 npm dependencies and a 7,190-line lockfile from the security surface. |

---

## 8. One atomic change or incremental? — and why

**Recommendation: incremental gating (A→E), with a single atomic *deletion* commit at D5.**

This is not a compromise; the two halves answer different questions.

**Why the *sequence* must be incremental.** The additive rule this project has followed — `Arronix.Common`
is added to `Arronix.sln` alone, `src/NzbDrone.*` is never edited (`arronix-common-extraction-plan.md` §1)
— exists because `src/Sonarr.sln` is the only thing in the repository that is known to work end to end. A
single PR that deletes `frontend/`, the toolchain, the CI job, the packaging steps and the Selenium project
puts every one of those verifications into one revert unit: if the packaging change is wrong, the fix is
entangled with 1,730 deletions. Worse, the feature inventory in §3 can only be *closed* by running the two
UIs side by side, which is impossible once the legacy one is gone. **The comparison is the deliverable, and
the comparison requires coexistence.**

**Why the *deletion* must nonetheless be atomic.** A piecemeal deletion — remove `Series/` this week,
`Settings/` next — leaves a webpack config pointing at missing entry points, an ESLint config over a
half-tree, a lockfile for dependencies nothing imports, and a `package.json` whose `build` script fails.
Nobody owns that state and everybody trips over it. `frontend/` has exactly one consumer
(`frontend/build/webpack.config.js`) and one output contract (`_output/UI`); there is no seam along which a
partial deletion is coherent.

**The shape that follows:** *decouple incrementally (D1→D4, four small revertible commits), delete
atomically (D5), clean up after (D6→E).* The legacy `Sonarr.sln` builds at every one of those points —
which is verifiable, because no `.csproj` references the frontend at all (§1.3).

### 8.1 The coexistence window is bounded

Incremental sequencing is a method, not a license to drift, and two things follow from the clean-sheet
ruling that an earlier draft of this section got wrong.

**No Arronix artifact ever carries the SPA — and none does today.** Worth stating precisely, because it is
easy to misread §6 as saying otherwise: `.github/actions/package/package.sh` builds the **legacy Sonarr**
artifact (`archiveName="Sonarr.$BRANCH.$SONARR_VERSION.$name"`, `cp -r $uiFolder $sonarrFolder`). There is
no Arronix packaging pipeline yet, so no Arronix package contains the React SPA and D2 is not a step that
cleans one up — D2 is the moment the *legacy* release stops having a web interface, which is exactly why
§7.1 places the point of no return there and why D1 is the go/no-go. That sequencing is correct and does
not move.

What must hold instead is forward-looking and absolute: **when an Arronix packaging pipeline is written, it
copies no `_output/UI` and depends on no `build_ui` artifact.** The rule costs nothing to keep because the
pipeline does not exist yet, and it removes the only path by which the SPA could reach an Arronix user —
somebody deriving Arronix packaging from `package.sh`, which is the obvious way to write it.

**The coexistence window ends at a named milestone, not at "readiness".** The window closes when
**Gate B is signed off — the §3 inventory closed, GAP-3/4/5/7 landed — and no later than the first tagged
Arronix release.** Whichever comes first is the deadline; D5 lands before that tag, not after it. A window
with no end date is how a strangler fig becomes a permanent second system, and this repository would carry
two UIs, two API surfaces and two test suites into its first release while calling it a transition.

An earlier draft advised the opposite — if `Arronix.Client` reached parity early, *"resist"* deleting the
SPA, because "nobody is maintaining it; it is frozen, it builds, and it costs one CI job and 8.9 MB". Three
corrections. The cost is not one CI job and 8.9 MB: it is 126 npm dependencies and a 7,190-line lockfile on
the security surface (R10), a Node/Yarn toolchain every contributor must install, and a standing invitation
to port a screen rather than close a gap. "Frozen and it builds" is a property of *legacy* code that a
clean-sheet rewrite is not obliged to keep paying for. And the insurance it buys against R1 is real but
expires: once Gate B is signed off, the comparison it insures has already been made, and continuing to hold
the policy is just holding the SPA.

What does *not* change: the deletion is still atomic at D5, the sequence is still D1→D4 first, and the
go/no-go is still at D1. The destination was always right; only the schedule was open, and it is now closed.

---

## 9. Work packages, in order

| WP | Gate | Deliverable | Owner surface | Depends on |
|---|---|---|---|---|
| **WP-D1** | A | Two-host dev-setup documentation; `Arronix.Api`/`Arronix.Client` in `Arronix.sln` only | `CONTRIBUTING.md` (request — concurrently owned), `Arronix.sln` | `unified-host-runtime.md` WP-11, WP-12 |
| **WP-D2** | B | **Close the §3 inventory** against a running legacy UI and a running Arronix client; every row marked covered / dropped / gap | this document, updated in place | WP-D1; a browsable `Arronix.Client` |
| **WP-D3** | B | **Contract additions — GAP-4, GAP-5**: `WorkbenchSubject.Preview`; `WorkbenchDescriptor.{Sorts,Filters}`; `WorkbenchColumn.SupportsBulkAssign` | `src/Arronix.Abstractions/Intent/` | `unified-host-runtime.md` WP-5 |
| **WP-D4** | B | **Contract additions — GAP-7**: `SettingRole.{AuthorizationGrant,Challenge}`, `FieldValueKind.KeyValuePairs`, `ValidationFailure.ChallengeRef`; host-side OAuth exchange + challenge endpoints | `src/Arronix.Abstractions/Providers/`, `src/Arronix.Api/Endpoints/ProviderEndpoints.cs` | `unified-host-runtime.md` WP-7, WP-11 |
| **WP-D5** | B | **Wire + endpoints — GAP-3**: `DownloadQueueItemView`, `HistoryEntryView`, `BlocklistEntryView`; `GET /api/v1/{downloads,history,blocklist}`; the queue/blocklist action set; client screens | `src/Arronix.Abstractions/Wire/`, `src/Arronix.Api/Endpoints/`, `src/Arronix.Client/` | WP-D3 |
| **WP-D6** | B | **GAP-8**: `AttributionNotice` on `ProviderDescriptor` for the `MetadataSource` family; host surfaces it on `MediaKindDescriptor` | `src/Arronix.Abstractions/Providers/` | — |
| **WP-D7** | B | Client obligations budgeted and prototyped: virtualization at 20k rows, color-impaired `ToneMap` palette, command palette over `ActionDescriptor`, persisted-preference migrations | `src/Arronix.Client/` | WP-D2 |
| **WP-D8** | C | Extract `frontend/src/Content/{Fonts,robots.txt}` + icon sources to `distribution/branding/`, with licenses; preserve the 45 locale JSON files | new paths only | WP-D2 |
| **WP-D9** | **D1 — go/no-go** | Remove `frontend` from `needs:` in `build_v5.yml:217,236` | `.github/workflows/build_v5.yml` | WP-D2…D8 signed off |
| **WP-D10** | D2 | Remove `build_ui` download (`package/action.yml:33–37`) and UI copy (`package.sh:5,19,26`); verify artifacts build and the app starts | `.github/actions/package/` | WP-D9 |
| **WP-D11** | D3 | Delete the `frontend` CI job (`build_v5.yml:96–121`) | `.github/workflows/build_v5.yml` | WP-D10 |
| **WP-D12** | D4 | Delete `src/NzbDrone.Automation.Test/`; remove from `src/Sonarr.sln`, `scripts/test.sh:6,56`, CI test filters | `src/Sonarr.sln`, `scripts/test.sh`, `.github/` | WP-D11 |
| **WP-D13** | **D5** | **Atomic deletion**: `frontend/`, `package.json`, `yarn.lock`, `tsconfig.json`. Run the §5 verification block. | repo root | WP-D12 |
| **WP-D14** | D6 | Remove `Selenium.Support`, `Selenium.WebDriver.ChromeDriver` from `Directory.Packages.props` | `Directory.Packages.props` | WP-D13 |
| **WP-D15** | E | `.gitignore` npm entries; `dependabot.yml` npm ecosystem; README/CONTRIBUTING Node prerequisites; `.github/copilot-instructions.md:8` | (several concurrently owned — raise as requests) | WP-D13 |
| **WP-D16** | E | **Delete `src/Sonarr.Api.V3/` and `src/NzbDrone.Integration.Test/` together**, plus `src/Sonarr.Http/Frontend/` (7 mappers + `StaticResourceController`) and `ConfigFileProvider.UiFolder:53,282`; remove all four from `src/Sonarr.sln`, `scripts/test.sh` and the CI test filters | `src/Sonarr.sln`, legacy tree | WP-D13. **Scheduled in this milestone** — see §10. Sequenced after D5 because until then the SPA still calls `/api/v3`. **Verify the reference set first:** `NzbDrone.Api.Test` also references `Sonarr.Api.V3` (`v3/Qualities/`, `v3/ReleaseProfiles/`), so it is part of the same removal unit; grep the tree for `Sonarr.Api.V3` before starting rather than trusting this list |

---

## 10. Deferred, with the trigger that un-defers it

| Deferred | Trigger |
|---|---|
| **Removing `Sonarr.Http/Frontend/` (7 mappers + `StaticResourceController`) and `ConfigFileProvider.UiFolder:53,282`** | Deferred only past D5, and only because deleting 350 lines that already 404 is not worth a separate legacy-stack edit before then. It leaves with `Sonarr.Http`, in the same milestone as the row below — **not** on an open trigger. |
| **Removing `Sonarr.Api.V3` + `NzbDrone.Integration.Test`** | **Scheduled, not triggered — WP-D16, after D5, in this milestone.** The two leave *together*: V3 exists to serve a SPA that no longer exists, and its only remaining driver is a legacy integration-test project that tests the legacy stack. Earlier drafts deferred V3 on the trigger "the legacy test suite retires", which made a legacy API surface's lifetime depend on a legacy test suite nobody had scheduled to retire — a rewrite keeping an old API alive because old tests call it. The clean-sheet ruling authorizes deleting both. Worth noting alongside: `Sonarr.Api.V3` is exactly the wire surface `servarr-prior-art.md` §8.3 once proposed re-creating inside Arronix, and `ARCHITECTURE.md` §8 has already dropped that plan — so this API is being neither kept nor rebuilt. |
| **A replacement browser-automation suite** | `Arronix.Client` reaching route stability plus `data-testid` hooks. Target Playwright over Selenium (no ChromeDriver version pinning); scope it at the six legacy smoke tests' level, not at parity with the whole §3 inventory. |
| **`ILocalizedStrings` and the 45-locale corpus (GAP-6)** | **Before 1.0** — not "a second locale", which is already met. Retargeting that trigger is a recommended amendment to `unified-host-runtime.md` §8. |
| **Saved custom filters (GAP-1)** | A user with more than ~200 library items; i.e. immediately after first real use. Host feature, ships after the deletion. |
| **iCal feed (GAP-2)** | The calendar view shipping. Needs a token-auth decision that belongs to `threat-model.md`. |
| **Client-side fuzzy search index** | Measured `?q=` latency above ~200 ms on a 5,000-item library. |
| **Sentry-equivalent client error reporting** | A decision in `pwa-and-push.md`'s telemetry section, not here. |
| **Custom-format spec builder UI (§3.9)** | The Policy Engine milestone. A nested boolean spec editor is not a `SettingsField[]` form and will need either a workbench variant or an honest admission that it is a bespoke client screen. |
| **Release-name parse tester** | Ships with `parsing-and-test-corpus.md`'s work if that document wants a diagnostic surface; otherwise a cheap standalone host endpoint. |

---

## Summary of recommended amendments to other documents

Raised here, **not edited** — every target is concurrently owned.

1. `docs/design/unified-host-runtime.md` §6.5 — add `WorkbenchSubject.Preview`; add `Sorts`, `Filters` to
   `WorkbenchDescriptor` and `SupportsBulkAssign` to `WorkbenchColumn` (GAP-4, GAP-5).
2. `docs/design/unified-host-runtime.md` §5.2 — add `SettingRole.AuthorizationGrant`,
   `SettingRole.Challenge`, `FieldValueKind.KeyValuePairs` (GAP-7).
3. `docs/design/unified-host-runtime.md` §6.4/§6.6 — add `DownloadQueueItemView`, `HistoryEntryView`,
   `BlocklistEntryView` and their endpoints; **rename `QueueEntryView` to `JobQueueEntryView`** so the two
   queues are never confused again (GAP-3).
4. `docs/design/unified-host-runtime.md` §8 — retarget the `ILocalizedStrings` trigger from "a second
   locale" to "before 1.0"; the stated trigger is already met 44 times in this repository (GAP-6).
5. `docs/design/unified-host-runtime.md` §5.6 — add `AttributionNotice` to `ProviderDescriptor` for the
   metadata-source family; this is license compliance, not decoration (GAP-8).
6. `docs/design/unified-host-runtime.md` §6.5 — record **import-existing-library** as a fourth declared
   workbench alongside manual import, interactive search and bulk edit.
7. `.github/copilot-instructions.md:8` — after D5, the React→Blazor migration is history, not a plan.
