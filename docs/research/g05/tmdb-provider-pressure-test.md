# G05 pressure test — an independent TMDb provider package

**Status:** pressure test complete; **G05 is not closed**. A real, separately installed, vendor-owned TMDb
provider package compiles `ICataloger<Movie>` and `ICurator<Movie>` against the exact public typed SDK,
with a genuine HTTP boundary and fake-transport tests. It stops, honestly and by design, at the exact seam
G04 has not yet resolved: minting the durable `MediaItemId` a cataloger or curator needs to return a
`Movie`. No invalid `Movie` and no fabricated identity are ever produced — the boundary throws instead.
This is the intended outcome of the pressure test, not a partial implementation of it.

Base commit: `bbed12a6993b6cfb59f452e6e9ac690efa66360f` (branch `claude/g04-provider-pairing`'s tip, ported
onto `claude/g05-tmdb-proof`). Built and tested with the pinned .NET 11 Preview 7 SDK
`11.0.100-preview.7.26381.103` via `/usr/local/share/dotnet/dotnet`.

## 1. What was built

Two new projects, added to `Arronix.sln`, own everything in this pressure test:

- **`src/Arronix.Provider.Tmdb`** — the vendor package. References only `Arronix.Abstractions` and
  `Arronix.Media.Movies` (the latter `Private="false"`, mirroring `Arronix.Architecture.Tests.MovieCatalogerFixture`'s
  established compile-only pattern). No reference to `Arronix.Plugin.Movies`, `Arronix.Host`,
  `Arronix.Client`, `Arronix.Plugins`, or Host DI anywhere in the package.
- **`src/Arronix.Provider.Tmdb.Tests`** — 37 NUnit cases, all passing, all exercising the real production
  types (no test double stands in for the provider itself).

Code is grouped by vendor and reason for change, inside one package (TMDb serves only Movies in this proof,
so the shared-vendor-mechanics-plus-separate-typed-bindings split G05 asks for "where one vendor serves
several media kinds" does not yet apply — there is only one binding to separate from):

```
src/Arronix.Provider.Tmdb/
  Settings/TmdbProviderSettings.cs      - provider-owned settings (API key, base URLs, region)
  Transport/TmdbDtos.cs                 - TMDb's own snake_case wire shape, owned here only
  Transport/TmdbMovieClient.cs          - the real HTTP boundary (IHttpGateway -> System.Text.Json)
  Transport/TmdbTransportExceptions.cs  - TmdbApiException / TmdbResponseFormatException
  Identity/TmdbIdentity.cs              - the "tmdb" external-id namespace + {tmdb-12345} marker recognition
  Movies/TmdbMovieMapper.cs             - DTO -> full Movie-shaped facts (everything except Key)
  Movies/TmdbMovieCataloger.cs          - ICataloger<Movie>
  Movies/TmdbMovieCurator.cs            - ICurator<Movie>
  Movies/MovieMaterializationBoundary.cs - the documented G04 stop point
  TmdbPluginModule.cs                   - IPluginModule; AddCataloger<Movie,_>/AddCurator<Movie,_>
  plugin.json
```

## 2. Exact dependencies

- `Arronix.Provider.Tmdb.csproj`: `ProjectReference` to `Arronix.Abstractions` and `Arronix.Media.Movies`
  (`Private="false"`). No `PackageReference`s — TMDb's JSON is parsed with `System.Text.Json`
  (`JsonSerializerDefaults.Web` plus explicit `[JsonPropertyName]` attributes), which ships in the shared
  framework and needs no central-package-version entry.
- `Arronix.Provider.Tmdb.Tests.csproj`: `ProjectReference` to `Arronix.Provider.Tmdb` only, plus
  `FluentAssertions` and `Microsoft.Extensions.TimeProvider.Testing` (both already centrally versioned).
- `plugin.json`: `id: "tmdb"`, `contracts.arronix: ">=0.8 <0.9"`, `dependencies: [{ "package": "movies",
  "range": ">=0.1 <0.2" }]`, `capabilities: ["metadata", "curation"]`. `Network` is not listed because
  `CapabilitySet.WithImplied()` derives it from `Metadata`/`Curation` automatically, exactly as the existing
  fixture manifest relies on.
- Both `packages.lock.json` files are checked in; `dotnet restore Arronix.sln --locked-mode` passes.

## 3. Naming discovery: `Arronix.Plugin.*` is reserved, not just conventional

The package was first built as `Arronix.Plugin.Catalogers.Tmdb`, then renamed mid-task to
`Arronix.Plugin.Tmdb` on supervisor direction (vendor-first naming; cataloger/curator are capabilities, not
package identity). Both names collided with the same pre-existing, **enforced** rule:
`Arronix.Architecture.Tests.Repository.RepositoryLayout.ExtensionPrefix = "Arronix.Plugin."` globs
`src/Arronix.Plugin.*` and feeds every match into `MediaExtensionTopologyTests`,
`PackageFacetTopologyTests`, `CapabilityDeclarationTests`, and `TypedMediaKindGovernanceTests` as if it were
a first-party **media extension** — asserting it may reference only `Arronix.Abstractions` and a format
domain assembly, and specifically **never** a media domain assembly such as `Arronix.Media.Movies`. Twelve
of these cases failed the instant the package existed under either `Arronix.Plugin.*` name, not because of
anything wrong with the provider's code, but because the prefix itself carries governance meaning in this
repository that pre-dates this task.

This is exactly the class of shared-contract collision this task's instructions forbid working around by
touching shared test infrastructure. The fix was to rename the package again, this time off the reserved
prefix entirely: **`Arronix.Provider.Tmdb`** (package id remains `tmdb`), parallel in spelling to
`Arronix.Media.*` and `Arronix.Format.*` — neither of which `RepositoryLayout` claims for anything else.
After the rename, all 321 (320 executed + 1 registered skip) architecture-test cases pass, including the
American-English spelling rule the rename itself triggered a fix for (`cancelled` → `canceled`,
`catalogued` → `cataloged`).

**Recommendation for whoever names the next vendor/provider package:** use `Arronix.Provider.<vendor>`, not
`Arronix.Plugin.<anything>`. The reserved prefix is real and load-bearing, not merely a style preference.

## 4. The exact G04 blocker, captured precisely

`Movie.Key` is `required MediaItemId` (`Arronix.Abstractions.Media.MediaItem<TItem,TReleaseTimeline,TReleaseStage>`).
`MediaItemId`'s own documentation states it is "a host-minted surrogate; nothing outside the platform
chooses its value or its range." `ICataloger<TItem>.SearchAsync`/`GetAsync` and `ICurator<TItem>.FetchAsync`
all return `TItem`/`IReadOnlyList<TItem>` directly — there is no candidate, draft, or partially-materialized
wrapper type in the current public SDK. G04's own exit gate says exactly this is open: *"Host can produce a
fully valid `Movie` without a public field bag or temporarily invalid domain object"* is a listed exit
criterion, not yet met, and the roadmap's G04 work list opens with *"who creates `MediaItemId` ... how an
`ICataloger<Movie>` supplies the exact Movie-shaped facts when a durable key is not yet known."*

Concretely, this leaves exactly three call sites unable to honestly return a value the interface promises,
and no others:

| Member | Blocked when | Not blocked when |
|---|---|---|
| `TmdbMovieCataloger.SearchAsync` | TMDb matches ≥1 movie | TMDb matches 0 movies → `[]`, no `Key` needed |
| `TmdbMovieCataloger.GetAsync` | TMDb has the requested movie | TMDb answers 404 → `null`, no `Key` needed |
| `TmdbMovieCurator.FetchAsync` | TMDb's popular list has ≥1 entry | An empty list → `CuratedListFetch<Movie>([], ...)`, no `Key` needed |

`TmdbMovieCataloger.ChangedSinceAsync` (returns `IReadOnlyList<ExternalId>`, never `Movie`) and
`ReadExternalIds` (local marker recognition) have no such dependency and are fully implemented, including
network-round-trip and pagination-aggregation tests.

At each of the three blocked call sites, this provider does the real work first — the actual TMDb HTTP
request executes over the fake transport, the response is deserialized, validated, and would otherwise be
handed to `TmdbMovieMapper.ToMovie(MediaItemId, TmdbMovieDetailsDto, TmdbSettingsValues, DateOnly)` — and
only then throws `MovieMaterializationNotSupportedException` (a `NotSupportedException` subtype), whose
message and XML docs point straight at the G04 exit gate and this document. `TmdbMovieMapper.ToMovie` is
itself fully implemented and fully tested (`TmdbMovieMapperTests`, 3 cases covering a complete payload, a
minimal payload, and out-of-region release dates) — given *any* `MediaItemId`, it produces a completely
valid `Movie`: title, original title, year, overview, runtime, genres, organization, website, artwork
(poster/fanart via the configured image base URL), certification, ratings, external ids (`tmdb` + `imdb`),
and a fully populated `MovieReleaseTimeline` (in-cinemas/physical/digital dates and a stage that evaluates
correctly). **The mapping is proven; only the identity assignment is withheld.** This is deliberate: fabricating a
`MediaItemId` (the way the pre-existing `Arronix.Architecture.Tests.MovieCatalogerFixture` does, by its own
admission only to prove compile-time package shape, explicitly not calling itself provider coverage) would
be exactly the "temporarily invalid domain object" this task was told not to invent.

No adapter, field bag, reflection convention, Host dependency, or vendor knowledge was added to `Movies`,
`Arronix.Format.Video`, `Arronix.Host`, or `Arronix.Abstractions` to get around this. The blocker is
reported, not routed around.

## 5. Tests and counts

37 NUnit cases in `Arronix.Provider.Tmdb.Tests`, all passing, 0 skipped:

| File | Cases | Covers |
|---|---|---|
| `Identity/TmdbIdentityTests.cs` | 9 | marker recognition: single/multiple/case-insensitive/malformed/no-match/null-arg, no network |
| `Transport/TmdbMovieClientTests.cs` | 10 | real HTTP boundary via fake `HttpMessageHandler`: success (search + details), empty result page, 404-as-null, malformed JSON → `TmdbResponseFormatException`, failed status (401) → `TmdbApiException` with TMDb's own message, cancellation → `OperationCanceledException`, paging fields pass through, `TestAuthenticationAsync` success/failure |
| `Movies/TmdbMovieMapperTests.cs` | 3 | DTO → full `Movie` shape (complete payload, minimal payload with placeholder title, release dates outside configured region ignored) |
| `Movies/TmdbMovieCatalogerTests.cs` | 9 | `ICataloger<Movie>` end to end: empty search, boundary on a non-empty search, 404 → null, non-numeric id short-circuits without a network call, boundary on a found movie, paginated `ChangedSinceAsync` aggregation (2 pages), empty changes, local `ReadExternalIds` with no network call, `TestAsync` failure |
| `Movies/TmdbMovieCuratorTests.cs` | 4 | `ICurator<Movie>` end to end: empty fetch, boundary on a non-empty fetch, failed response surfaces `TmdbApiException`, `TestAsync` success |
| `TmdbPluginModuleTests.cs` | 2 | `Configure` registers exactly one cataloger and one curator, both naming `Movie` once, and never touches the network; null-context guard |

The fake transport is a real `HttpClient` over a fake `HttpMessageHandler` (`Support/FakeHttpMessageHandler.cs`),
wrapped in `Support/TestHttpGateway.cs` — a small, faithful `IHttpGateway` implementation (the real one lives
in `Arronix.Plugins`, which this package must not reference). `TestHttpGateway` performs the same
request/response translation the real gateway promises; it is not a test double of the provider under test,
only of the Host-owned transport plumbing the provider is not permitted to reference directly. No test
requires or embeds a credential, and no test performs a live API call.

Solution-wide (`eng/ci/run-tests.sh`, locked restore, warnings-as-errors build, 12 test projects, 3-column
required-sentinel registry): 2,864 total cases, 2,561 passed, 302 registered skips, 0 inconclusive — and
**1 failed case, pre-existing at base commit `bbed12a69` and unrelated to this pressure test**:
`Arronix.Plugin.Movies.Tests.Shape.ManifestOwnershipTests.CarriesNothingButWhatItOwns` fails because
`src/Arronix.Plugin.Movies/plugin.json` already declares `contractAssemblies` and `dependencies` (from the
G03/G04 dependency-pipeline work this base commit ports in) while the test's `ManifestOwnedProperties`
allow-list was not updated to match. Both files are byte-identical to the base commit (`git diff
bbed12a69 -- src/Arronix.Plugin.Movies/plugin.json src/Arronix.Plugin.Movies.Tests/Shape/ManifestOwnershipTests.cs`
produces no output) — this branch touches neither, per its instructions to own only the new TMDb provider
projects. `Arronix.Provider.Tmdb.Tests` itself: 37/37 passed. `Arronix.Architecture.Tests`: 321/322 (1
registered skip, 0 failed) — the full architecture-topology, capability-declaration, and naming suite
passes clean against the new package.

## 6. Integration notes for whoever closes G04

- Once G04 defines an approved construction/materialization contract (a candidate/draft type, a factory
  seam, or a changed identity lifecycle — the roadmap deliberately does not pre-approve one), the three
  blocked call sites in `TmdbMovieCataloger`/`TmdbMovieCurator` are a small, mechanical change: replace the
  `throw MovieMaterializationBoundary.For(...)` with a call into whatever seam G04 lands, using
  `TmdbMovieMapper.ToMovie` exactly as it exists today. No mapping logic needs to change.
- `TmdbIdentity.Scheme = "tmdb"` and its `{tmdb-12345}` marker are this provider's owned vocabulary, read
  today only by `TmdbMovieCataloger.ReadExternalIds` and consumed the same way the existing fixture's
  `"fixture"` scheme is: through the non-generic `ICataloger` floor, with no vendor spelling anywhere in
  `Arronix.Media.Movies` or `Arronix.Abstractions`.
- `TmdbProviderSettings` follows the existing `SettingsField`/`ProviderDefinition.Settings` seam exactly —
  no new settings mechanism was introduced. The API key is declared `SettingSensitivity.Secret` and
  `Required = true`; base URL, image base URL, and region are optional/advanced with TMDb's real defaults.
- Activation is constructor-based (`public TmdbMovieCataloger(IPluginContext context)` /
  `public TmdbMovieCurator(IPluginContext context)`), matching "the admitted plugin context or a public
  parameterless constructor" exactly. Neither type takes anything from Host DI.
- This is a pressure test and a G04 blocker report, not a claim of G05 closure. G05's own exit gate requires
  "Host can produce a fully valid `Movie`" and "absence or incompatibility of Movies quarantines only the
  affected typed binding" — the first is exactly the open G04 item above, and the second (admission-time
  dependency resolution) is G03/G04 loader work this package does not and cannot exercise on its own.
  Nothing here should be read as G05 being closed by this branch.
