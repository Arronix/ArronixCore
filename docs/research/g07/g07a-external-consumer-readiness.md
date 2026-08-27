# G07A readiness audit — the package-only external consumer

**Status:** research only. Nothing in this document is landed; it is the preparation Codex needs to
implement G07A. No code, fixture, or proof script in this branch changes production behaviour.

**Scope:** the delta between what G06 already proved and what G07A's exit gate requires, the topology and
commands an implementer needs, the artifact lifecycle a retained proof script must manage, a first test and
mutation matrix, the exact fixture contract G07A may and may not depend on, and the blockers that gate real
execution today. Provider ingestion, durable item rendering, persistence, a test-only production endpoint,
and any G07B or G28 work are explicitly out of scope, per the roadmap's own text for this gate.

**Last verified:** 2026-08-27, against `docs/design/typed-media-roadmap.md`, `CONTEXT.md`,
`docs/research/g04/provider-pairing-contract.md`, and the local machine's installed SDKs. Any claim below
that depends on a moving target (a status field, an installed toolchain) names the date it was checked
rather than being stated as a permanent fact; a claim about this branch's or a sibling worktree's exact
commit is deliberately avoided, because that is exactly the kind of fact that goes stale the moment another
session commits.

## 0. Operational note: which `dotnet`

This machine's default `dotnet` on `PATH` resolves to `/opt/homebrew/bin/dotnet`, Homebrew's Cellar install
at SDK `10.0.400`. `global.json` pins `11.0.100-preview.7.26381.103` with `"rollForward": "disable"`, so that
default `dotnet` cannot even honour the pin — it reports `Install the [11.0.100-preview.7.26381.103] .NET SDK
or update [global.json]` and refuses to run, rather than silently building against a different target
framework. The correct binary is `/usr/local/share/dotnet/dotnet`, confirmed at
`11.0.100-preview.7.26381.103` on 2026-08-27, and it is the binary `eng/proofs/g07-client-contracts.sh`
already documents in its own invocation line (`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash
eng/proofs/g07-client-contracts.sh`).

Every `dotnet` invocation in §3, §4, and §5 below must use that exact path — directly, or through a
`DOTNET_COMMAND`/`$DOTNET` variable resolved to it — and never the bare `dotnet` name. This is not a
preference: since the default resolves to a different major version, a script that shells out to bare
`dotnet` does not fail loudly, it fails exactly the way `global.json`'s `rollForward: disable` is designed to
prevent, or — if a future `global.json` edit ever loosened that pin — would silently retarget the build.
G07A's own retained proof script must not downgrade `net11.0` targets by picking up whichever SDK happens to
be first on `PATH`.

## 1. What G06 already proved, and what it deliberately left open

G06 (`docs/research/g06/authoring-sdk-boundary.md`) proved the package-boundary half of the authoring
promise: `Arronix.Sdk 0.9.0`, built with `dotnet pack` and inspected, contains only `README.md`,
`analyzers/dotnet/cs/Arronix.Generators.dll`, and the `lib/net11.0/_._` metapackage marker; its only NuGet
dependency is `Arronix.Abstractions 0.9.0`. A project created outside the repository, referencing only that
packed package, restored and compiled a partial typed media declaration with zero warnings; removing
`partial` reproduced `ARX1003` at the declaration and restoring it returned the project to a clean build.

That proof is real but narrow, and the G06 record says so directly: "G07A retains the stronger permanent
proof of packaging, installation, admission, exact browser type identity, and rendering in an unmodified
Host and Client." Four gaps separate the two:

1. **Not retained.** No external-consumer project exists anywhere in this repository or its solution
   (`Arronix.sln` lists 34 projects, none named for an external fixture). The G06 proof was built, exercised,
   and discarded outside the repository tree. G07A's own exit gate requires the opposite: "this smoke proof
   is retained permanently."
2. **Compile-only.** G06 stopped at `dotnet build`. It never packaged the consumer's own extension, never
   installed it beside first-party packages in a running `Arronix.Api`, never exercised admission
   (`IPluginAdmissionCheck.Prepare`, publication, or refusal diagnostics), and never proved that the exact CLR
   type the consumer's typed media kind closes is the type Host's registry actually holds — the G03-style
   `typeof(...)` identity proof, but for a package built with no repository knowledge at all.
3. **No provider, no browser.** G06 exercised a media declaration only. G07A additionally requires "one typed
   provider" — a cataloger closing `ICataloger<TItem>` against the shipped domain, admitted and
   quarantine-checked per G04/G05, but never ingested (see §6) — and a browser-side load of the consumer's
   client-safe contract assembly through the unmodified `Arronix.Client`, which G06 never attempted. `ICurator`
   is not required for this proof (see §2); the roadmap's own phrase is "one typed provider," singular, and
   G04's pairing check is already proved per-family and per-contract (`docs/research/g04/provider-pairing-contract.md`
   §1.2–1.3), so one cataloger exercises it completely.
4. **No cross-package pairing.** A single combined package that both declares the media kind and implements
   its own cataloger never crosses a package boundary at all — Host would admit one package whose kind and
   provider arrive together, which is not what G04's admission proof or G03's shared-CLR-identity proof are
   about. Both existing precedents split this on purpose: G03's fixtures are "two independent fixture
   dependants" sharing one admitted `Video` identity, and G05's `Arronix.Provider.Tmdb` is packaged, versioned,
   and admitted separately from `Arronix.Plugin.Movies`, closing `ICataloger<Movie>` against a domain it
   never authored. G07A must reproduce that shape with packages built from outside the repository, or it
   proves nothing about independent provider pairing that a combined package doesn't already trivially satisfy.

G07A is therefore not a bigger G06 test; it is the first time any extension — first-party or not — is
proved end to end from `dotnet pack` through a real browser tab without ever touching this repository's
projects, sources, or internal visibility grants.

## 2. Topology

The default topology is **two independently built external packages**, each outside `Arronix.sln` and each
built with no knowledge of the other's source — only of the other's packed, shipped output. A combined
package that declares the kind and implements its own cataloger in one assembly is explicitly rejected as the
default: it never exercises admission across a package boundary, so it cannot stand in for the pairing proof
G04 establishes (`docs/research/g04/provider-pairing-contract.md` §1.2, "the pairing is read from the contract
the implementation implements" — a claim about *a* registration's own package, never about two packages
agreeing). The two packages mirror the real split between `Arronix.Media.Movies`/`Arronix.Plugin.Movies` and
the independently versioned `Arronix.Provider.Tmdb`, with one further split the first-party pair does not
need: the media package itself is two assemblies, because the provider package must compile against a
*shipped* domain type, not a project sibling.

**Package 1 — the media package** (two projects, one package; not necessarily committed under `src/`, since
it must prove it needs no repository project reference — a location such as
`eng/proofs/fixtures/g07a-media/` keeps it discoverable without adding it to the solution):

```
ExternalKind.Domain.csproj    -- PackageReference: Arronix.Abstractions only. No Sdk, no analyzer, no
│                                 generator: this mirrors Arronix.Media.Movies exactly, which "declares no
│                                 MediaType and takes no analyzer" for the same reason — a definition here
│                                 would put its generated reader delegates on this assembly's shared cadence.
├── ExternalItem.cs             -- the nominal item type, e.g. ExternalItem : MediaItem<ExternalItem,...>

ExternalKind.csproj            -- PackageReference: Arronix.Sdk, the packed ExternalKind.Domain package
│                                 (§3), arronix.format.video (or an equally trivial owned format — see §7).
│                                 No ProjectReference, no InternalsVisibleTo.
├── ExternalKind.cs              -- partial class : MediaType<ExternalItem,ReleaseTarget<ExternalItem>,
│                                    Release<Video>,ExternalKindParser>
├── ExternalKindParser.cs        -- static IReleaseParser<Release<Video>>
├── ExternalKindModule.cs        -- IPluginModule: AddMediaType<ExternalKind>() only — no provider
│                                    registration lives in this package
└── plugin.json                  -- id "externalkind" (or similar, distinct from movies/tmdb/
                                     arronix.format.video), entryAssembly ExternalKind.dll,
                                     contractAssemblies [ExternalKind.Domain.dll],
                                     clientContracts [ExternalKind.Domain.dll], capabilities ["media-kind"]
```

**Package 2 — the provider package** (one project, built and versioned independently of Package 1, in a
sibling location such as `eng/proofs/fixtures/g07a-provider/`):

```
ExternalKindCataloger.csproj  -- PackageReference: Arronix.Abstractions, the same packed ExternalKind.Domain
│                                 package Package 1 published (§3) — with its runtime asset excluded from
│                                 this project's own publish output (the NuGet equivalent of the `Private="false"`
│                                 ProjectReference Arronix.Provider.Tmdb uses against Arronix.Media.Movies),
│                                 so this package never carries a private copy of a type it must share by
│                                 exact identity. No Arronix.Sdk: a provider registers nothing the generator
│                                 projects, exactly as Arronix.Provider.Tmdb takes no analyzer reference.
├── ExternalKindCataloger.cs    -- : ICataloger<ExternalItem> only — no ICurator (see below)
├── ExternalKindCatalogerModule.cs -- IPluginModule: AddCataloger<ExternalKindCataloger>(descriptor) only
└── plugin.json                  -- id "externalkind.cataloger" (distinct from Package 1's id), entryAssembly
                                     ExternalKindCataloger.dll, no contractAssemblies (nothing to share),
                                     dependencies [{ "package": "externalkind", "range": "..." }],
                                     capabilities ["metadata"]
```

**No `ICurator` in the default proof.** The roadmap's own phrase is "one typed provider," and G04's owner
decision (`docs/owner-ledger.md` O-40; `docs/research/g04/media-item-identity.md`) already settled what a
curator would have to do if one were added later: it returns `CuratedReference` values naming a catalog
identity in the cataloger's own scheme, never an item, and it never owns identity — Host alone assigns
`MediaItemId` at materialization. A curator adds no new admission mechanism `ICataloger<TItem>` does not
already exercise (both close through the same `IClosedCataloger`/`IClosedCurator` family-marker path per
`docs/research/g04/provider-pairing-contract.md` §1.2); it would be additional package/proof surface for the
same proof G07A already gets from one cataloger. If a curator is added to this fixture in a later revision, it
must return catalog-qualified references exactly as above and must not be given, or appear to have, its own
notion of durable identity.

This mirrors the shape every first-party plugin already has (`src/Arronix.Media.Movies` +
`src/Arronix.Plugin.Movies`, `src/Arronix.Provider.Tmdb`, `src/Arronix.Format.Video`) — the point of G07A is
that the *only* difference from a first-party split is where each package was built and what it was allowed
to reference.

Runtime topology follows the G07.1 precedent in `eng/proofs/g07-client-contracts.sh` exactly, with two
packages — not one — added to the same installation:

```
artifacts/g07a/
├── packages/
│   ├── arronix.format.video/     -- dotnet publish of the in-repo project (unchanged G07.1 staging)
│   ├── movies/                   -- optional: only needed if the two external packages are not sufficient
│   │                                 alone to exercise a populated, non-empty installation
│   ├── externalkind/              -- dotnet publish of Package 1 (media), built from its own
│   │                                 restored/packed dependency graph, copied (not built) into place
│   └── externalkind.cataloger/    -- dotnet publish of Package 2 (provider), independently built and
│                                     staged, admitted after Package 1 per its declared dependency edge
├── client/                       -- dotnet publish of the unmodified Arronix.Client (no proof-only flags)
└── evidence/                     -- server.log, manifest.json, plugins.json, browser #contract-proof text
```

`Arronix__Plugins__RootFolder` points at `artifacts/g07a/packages`; `Arronix__Api__ClientRoot` points at the
unmodified client publish output. The server is the real `Arronix.Api` `Program`, started with `dotnet run`,
exactly as G07.1 does — G07A's exit gate forbids a "test-only production endpoint," and the existing script
already proves the real composition is sufficient.

## 3. Commands

A retained proof script (`eng/proofs/g07a-external-consumer.sh`, following the header/option conventions of
`eng/proofs/g07-client-contracts.sh`) needs four phases the existing script does not have, run before the
existing staging/serving sequence, and the fourth (packing and restoring the provider) strictly depends on the
domain package the second phase produces. Every command below resolves `dotnet` the same way
`eng/proofs/g07-client-contracts.sh` already does — `dotnet_command=${DOTNET_COMMAND:-dotnet}` — and every
invocation shown here is written against that variable, never bare `dotnet`, per §0:

1. **Pack this repository's own dependencies**, into a throwaway local feed:
   ```bash
   "$dotnet_command" pack src/Arronix.Sdk/Arronix.Sdk.csproj -c Release -o "$feed_root"
   "$dotnet_command" pack src/Arronix.Abstractions/Arronix.Abstractions.csproj -c Release -o "$feed_root"
   "$dotnet_command" pack src/Arronix.Format.Video/Arronix.Format.Video.csproj -c Release -o "$feed_root"   # if reused, see §7
   ```
   `Arronix.Sdk.0.9.0.nupkg`'s only dependency is `Arronix.Abstractions 0.9.0`, and `Arronix.Generators` is
   never packed independently (`IsPackable=false`) — it is embedded as the SDK package's analyzer asset, so
   packing the SDK is sufficient; the generator project itself is never a restore target.

2. **Pack Package 1's domain project into the same feed, then restore and build Package 1 against it**, with
   an isolated package cache so the proof cannot silently succeed by falling back to a locally installed copy
   or a warm global cache:
   ```bash
   "$dotnet_command" pack "$media_domain_project" -c Release -o "$feed_root"
   "$dotnet_command" restore "$media_entry_project" \
     --source "$feed_root" --source https://api.nuget.org/v3/index.json \
     --packages "$isolated_nuget_cache"
   "$dotnet_command" build "$media_entry_project" -c Release --no-restore -warnaserror
   ```
   `ExternalKind.Domain` must be packed and present in `$feed_root` *before* this restore, because
   `ExternalKind.csproj` takes it as an ordinary `PackageReference`, not a project sibling reference — that
   restore is the first point at which "the consumer needs no project reference" is actually exercised for the
   cross-package edge, not merely for the SDK edge G06 already proved.

3. **Restore and build Package 2 against the same feed**, which by now holds both the repository's packages
   and Package 1's domain package:
   ```bash
   "$dotnet_command" restore "$provider_project" \
     --source "$feed_root" --source https://api.nuget.org/v3/index.json \
     --packages "$isolated_nuget_cache"
   "$dotnet_command" build "$provider_project" -c Release --no-restore -warnaserror
   ```
   `ExternalKindCataloger.csproj`'s `PackageReference` to `ExternalKind.Domain` must exclude the runtime asset
   from this project's own build/publish output (`<ExcludeAssets>runtime</ExcludeAssets>` or equivalent) — the
   NuGet-restore analogue of the `Private="false"` `Arronix.Provider.Tmdb` already sets on its `ProjectReference`
   to `Arronix.Media.Movies`. Without it, Package 2's own publish output would carry a second copy of
   `ExternalKind.Domain.dll`, which G03's private-copy refusal exists precisely to catch at admission
   ("a package carrying a private copy of an admitted contract... is refused with both module identifiers and
   both content hashes") — a passing local build would then fail loudly only once the two packages are staged
   together in phase 4, which is a worse place to discover a manifest/project mistake than the restore that
   caused it.

   Both restores in phases 2 and 3 target `net11.0` throughout (Package 1's entry project through `Arronix.Sdk`,
   Package 2 directly); a `global.json` in each project's own directory, mirroring the repository root's,
   keeps `rollForward: disable` refusing an unpinned `dotnet` outright rather than letting either restore or
   build silently retarget. A `NuGet.Config` scoped to each fixture directory (this repository currently has
   none — restore relies on ambient machine configuration) is cleaner than repeated `--source` flags and makes
   "clean package cache" mean the same thing on every run.

4. **Pack and publish both extensions**, the same way `eng/proofs/g07-client-contracts.sh` publishes
   first-party packages:
   ```bash
   "$dotnet_command" publish "$media_entry_project" --configuration Release --no-build \
     --output artifacts/g07a/packages/externalkind
   "$dotnet_command" publish "$provider_project" --configuration Release --no-build \
     --output artifacts/g07a/packages/externalkind.cataloger
   ```
   so each staged payload is that package's real publish closure, not a hand-copied `bin/` directory — and
   Package 2's staged folder is the concrete evidence that phase 3's `ExcludeAssets` actually worked: it must
   contain `Arronix.Abstractions.dll` and its own entry assembly, and must not contain `ExternalKind.Domain.dll`.

From there, the script reuses G07.1's pattern verbatim, with the same `$dotnet_command` variable and the same
invocation, `DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07a-external-consumer.sh`:
build/publish the unmodified client, start `Arronix.Api` against the combined `packages/` folder — now holding
both external packages plus `arronix.format.video` (and, optionally, `movies`) — assert over HTTP that both
packages reached `Active` in dependency order and that Package 1 offers its client facet, then hand off to a
real browser (`--serve`, Playwright or manual) to load the client-safe contract and read `#contract-proof`.

## 4. Artifact lifecycle

Every artifact this proof produces is disposable and regenerated on each run, matching the existing G07.1
discipline (`rm -rf "$proof_root"` at the top of the script, nothing written outside `artifacts/`). Every
"produced by" cell below is `"$dotnet_command"` (§0/§3), not bare `dotnet`, on every run:

| Artifact | Produced by | Lives under | Regenerated |
| --- | --- | --- | --- |
| Local package feed (`.nupkg` files) | `"$dotnet_command" pack` of Sdk/Abstractions/(format) **and** of Package 1's domain project (`ExternalKind.Domain`) | `artifacts/g07a/feed/` | every run |
| Isolated NuGet cache | `"$dotnet_command" restore --packages`, shared by both external packages' restores | `artifacts/g07a/nuget-cache/` | every run |
| Package 1 build output | `"$dotnet_command" build`/`publish` of the out-of-repo media entry project | Package 1's own `bin`/`obj`, or redirected via `--output` | every run; `obj/` must be cleared first, per the G07.1 trimming lesson about stale Blazor/publish state |
| Package 2 build output | `"$dotnet_command" build`/`publish` of the out-of-repo provider project, only after Package 1's domain nupkg is in the feed | Package 2's own `bin`/`obj`, or redirected via `--output` | every run, `bin`/`obj` cleared first |
| Staged package payloads | `"$dotnet_command" publish --no-build --output` per package, two external packages plus first-party ones | `artifacts/g07a/packages/<id>/` | every run |
| Client publish | `"$dotnet_command" publish` of unmodified `Arronix.Client` | `artifacts/g07a/client/` | every run, `bin`/`obj` cleared first |
| Server process + logs | `"$dotnet_command" run` against `Arronix.Api` | `artifacts/g07a/evidence/server.log` | every run |
| HTTP evidence | `curl` against `/api/v1/client-contracts`, `/api/v1/plugins` | `artifacts/g07a/evidence/*.json` | every run |
| Browser report | operator/Playwright reading `#contract-proof` | `artifacts/g07a/evidence/browser-*.txt` (recommended, not yet scripted) | every run |

Both packages' *sources* are the artifacts that are not disposable — they are the retained fixture the exit
gate requires — but their build outputs, the domain nupkg itself, the local feed, and the isolated cache must
never be. A retained feed or cache is exactly the shortcut ("it still works because a warm cache from last
time has it") that would quietly reopen the escape hatches §2 and §5 rule out — most concretely for Package 2,
whose whole restore is supposed to prove it can only ever see Package 1's domain type through a freshly
packed, freshly published artifact.

## 5. Test and mutation matrix

G07A's own exit gate is a positive/negative pair at every layer, following the precedent both G06
(`ARX1003` negative build) and G07.1 (nine-mutation table in `client-contract-loading.md` §4.4) already set.
Every row that builds, restores, or runs anything does so under `$dotnet_command` resolved to
`/usr/local/share/dotnet/dotnet` (§0) — a matrix run under the machine's default `dotnet` would not exercise
`net11.0` at all and would misreport every row below as inapplicable rather than passing or failing. A first
matrix, to be executed by the retained script plus a small number of hermetic unit cases colocated with it:

| Layer | Positive case | Negative case | Expected refusal / diagnostic |
| --- | --- | --- | --- |
| Authoring | Package 1's media type is `partial` | Remove `partial` | `ARX1003` at the declaration, build fails |
| Package 1 restore | Restore Package 1 from the local feed only | Point `--source` at an empty feed | NU1101 / restore failure, no fallback resolution |
| Cross-package restore | Package 2 restores `ExternalKind.Domain` from the feed Package 1's `dotnet pack` populated | Run Package 2's restore before phase 2 packs the domain project | NU1101 for `ExternalKind.Domain`, distinct from Package 1's own restore failure above — this is the row that proves the *pairing* has no project-reference escape hatch, not just the SDK edge |
| No private copy | Package 2's publish output carries no `ExternalKind.Domain.dll` (`ExcludeAssets=runtime`, §3 phase 3) | Reference the domain package without excluding the runtime asset | admission refuses with both module identifiers and both content hashes named, per G03's private-copy check (`docs/research/g04/provider-pairing-contract.md` is silent on this specifically; the check is G03's, exercised here for the first time by an out-of-repo package) |
| Provider pairing | Package 2's cataloger closes `ICataloger<TItem>` for Package 1's exact item type | Implementation closes no member of the family, or closes it over the wrong item type | `PluginProviderContractInvalid` |
| Media/provider coherence | Package 1 is admitted before Package 2 | Package 2 alone, without an admitted kind supplying its item type | `PluginMediaPairingUnsatisfied` |
| Kind admission | One item type, one kind | A second package closes a kind over the same item type | `MediaItemTypeConflict` |
| Contract range | Both `plugin.json` files declare `>=0.9 <0.10` | Declare an incompatible range on either package (e.g. `>=0.1 <0.2`) | `PluginContractMismatch`, that package quarantined before construction |
| Escape hatch: project reference | none present, in either package | add a `ProjectReference` to any in-repo project, or between the two external packages themselves | build proof must fail closed (script asserts neither `.csproj` contains a `ProjectReference`) |
| Escape hatch: `InternalsVisibleTo` | none present, none needed, in either package | grant `InternalsVisibleTo` from any first-party assembly to either package | architecture-test-style assertion: grep the solution for a grant naming either fixture; today's five real grants (`Arronix.Host`, three `*.Tests` assemblies, `Arronix.Client.Tests`) are the complete list and none should ever name either fixture |
| Client load | Package 1's client-safe assembly hashes, verifies, and loads under G07.1's two-pass loader | Flip one byte of Package 1's staged assembly (same mutation G07.1 already proves generically) | `ContentHashMismatch`, `CanProject=false`, nothing resident |
| Browser fixture projection | **Open — see §6 and §8 item 1.** Not yet a single defined positive case until the G07.2 integration decision names what "reuse" means. | n/a until the positive case is defined | — |

The escape-hatch rows are the ones with no first-party precedent to imitate: they are assertions about the
*fixtures' own project files and the solution's `InternalsVisibleTo` grants*, not about Host behaviour, and
belong in the proof script itself (grep/assert) rather than in a unit test that only ever sees the repository
side of the boundary. The cross-package and no-private-copy rows have no first-party precedent either, in a
different sense: G03 and G05 prove those mechanisms exist, but always between packages built inside this
repository from a shared `Directory.Packages.props`; nothing today exercises them between two packages that
never shared a solution.

## 6. Fixture contract — what G07A's consumer may and must not depend on, and one integration decision that is not yet made

Per the roadmap text verbatim, the consumer:

- defines **a trivial typed media kind and one typed provider** — enough to exercise `AddMediaType<T>()`,
  `AddCataloger<T>`, and the G04 pairing check across the two-package boundary in §2, not a realistic domain;
- **packages and installs into an unmodified Host** — the real `Arronix.Api`, no test-only route, no
  fixture-specific composition root;
- **loads its client-safe typed contract in the unmodified browser Client using the serialized G07
  fixture**, with the exit gate stating the browser proof is explicitly "limited to contract loading and
  fixture projection," and the gate's own **Outcome** line naming "generated accessibility" as one of the
  four things this gate proves — package assets, analyzer packaging, generated accessibility, and admission;
- explicitly **does not** ingest provider results or render a durable item — "Provider-result ingestion and
  durable item rendering remain G19/G28 work" — so Package 2's cataloger only needs to admit and pass the
  G04/G05 contract checks; it is never invoked through `CatalogDispatcher` in this proof;
- **must not** add "a temporary external item source, test-only production endpoint, or premature generic
  catalog workflow merely to make this smoke test render provider results" — i.e. no shortcut that makes the
  provider *look* ingested is acceptable, even if it would make the browser page more convincing.

**What "reuse" means for the serialized fixture is not settled, and this document must not settle it by
assumption.** G07.2 is recorded `not started` (§8 item 1); its own Implement list is the only place the
phrase "the serialized fixture G07A later reuses" appears, describing "typed deserialization of a Movie
graph" as what produces that fixture. That sentence fixes what fixture exists once G07.2 lands — a serialized
Movie graph — but it does not fix what *reusing* it means for a package whose whole point is that it is not
Movie. Three readings are each consistent with the roadmap text as written, and they make different claims
about whether G07A actually proves "generated accessibility" for the external media type in the browser, not
just for Movies:

1. **Protocol reuse.** G07.2 defines a generated-metadata wire protocol and a typed-deserialization/rendering
   mechanism; Package 1's own generator output is run through that same mechanism against Package 1's own
   trivial item, producing a *second*, package-specific serialized fixture. "The serialized fixture G07A later
   reuses" would then describe the *shape and tooling* G07.2 leaves behind, not literal shared bytes. This
   reading is the only one of the three that exercises the external package's own generated client metadata in
   the browser, and is the only one that fully answers the Outcome line's "generated accessibility" for this
   package rather than for Movies.
2. **Schema reuse, external data.** Package 1's fixture is serialized using G07.2's schema and hashed the way
   G07.2's projection-schema hash requires, but the browser-side rendering component itself is not
   reimplemented per package — it is the one G07.2 built, pointed at Package 1's bytes instead of Movie's.
   This still exercises Package 1's own generated metadata, but reuses more of G07.2's browser-side machinery
   verbatim than reading 1 does.
3. **Literal reuse of the Movie fixture.** The browser leg of G07A never touches Package 1's own generated
   client metadata at all: it loads Package 1's contract assembly through the already-complete G07.1
   mechanism (proving *that* half honestly), and separately re-renders the *existing* Movie fixture through
   the unmodified `ContractsPage.razor` as a demonstration that the rendering path G07.2 built still works
   after a second, unrelated package is installed. This is the cheapest reading to implement and the only one
   of the three explicitly ruled out as sufficient by itself: it would leave "generated accessibility" proved
   for Movies, not for the external package, which is not what this gate is for.

This report does not choose between them — doing so from this branch, before G07.2 exists, would be exactly
the kind of "mechanism's needs become an owner decision by omission" this repository's own discipline warns
against (`CONTEXT.md`; owner-ledger O-33). Whoever implements G07.2 must record which reading it commits to,
because the choice changes what G07.2's own "serialized fixture" artifact needs to be parameterizable over
(one Movie-shaped fixture only, versus a fixture-generation mechanism reusable against any admitted package's
generated metadata) — recorded again in §8 item 1 and §9.

## 7. Open scoping decision: does Package 1 need its own format?

`MediaType`'s primary constructor requires non-empty format composition (`INTERFACE.md` §3,
`typed-media-north-star.md` rule 8). This applies only to Package 1 (the media package); Package 2 (the
cataloger) composes no format at all. Two honest options, neither settled by the roadmap text:

- **Reuse `arronix.format.video`.** It is already an independently installable, no-entry-assembly,
  zero-capability contract package (`src/Arronix.Format.Video/plugin.json`) — the cheapest way to satisfy the
  constructor without inventing a second format capability. Package 1's dependency edge
  (`{ "package": "arronix.format.video", "range": ">=0.1 <0.2" }`) then also incidentally re-proves the G03
  shared-CLR-identity claim ("two independent fixture dependants share the same admitted Video contract
  identity") for a package built with no repository access, which G03 itself never claimed.
- **Compose a still-simpler format Package 1 defines and ships itself.** More faithfully "external," but
  spends scope G07A does not ask for — a self-authored format capability is not in the roadmap's Implement
  list for this gate, and the exit gate's own phrase is "a trivial typed media kind," singular, not three new
  extensions (media, provider, and now format).

Reusing `arronix.format.video` is the lower-risk default; record the choice in the eventual G07A record
rather than deciding it silently in code, per this repository's own discipline against turning a mechanism's
convenience into an undocumented decision (`CONTEXT.md`, owner-ledger O-33).

## 8. Blockers

1. **G07.2 and G07.3 are recorded `not started`, as of 2026-08-27, and the "reuse" integration decision from
   §6 is unmade because of it.** `docs/design/typed-media-roadmap.md` carries an explicit `**Status:**` line
   per sub-gate — G07.1 `complete`, G07.2 `not started`, G07.3 `not started` — and that field, not a snapshot
   of any branch's commit history, is this repository's authoritative record of gate progress; re-check it
   directly rather than trusting a git-log comparison frozen at the moment this report was written, since
   other work can land on sibling branches at any time without this document being updated. This blocks two
   distinct things, not one: G07A's browser half cannot run at all until G07.2 exists, *and*, separately, it
   is not yet decided which of §6's three readings of "reuse" G07A's browser half is even supposed to prove —
   that decision belongs to whoever implements G07.2, because only that implementation fixes whether the
   serialized fixture is Movie-specific or parameterizable over any admitted package's generated metadata. The
   roadmap states the sequencing dependency directly: "G07 is closed only when all three are closed; G07A and
   G07B remain later gates and none of their work is folded into these." **This blocks only the
   browser-fixture-projection portion of G07A**, not the package/restore/pack/admission/CLR-identity portion
   across the two external packages in §2, which depends only on already-complete G01–G06.
2. **No retained external-consumer fixtures exist.** Confirmed by absence from `Arronix.sln` and by grep
   across the tree; this is expected (G06's proof was intentionally ephemeral, and it was also only ever one
   package) but means G07A starts from nothing rather than hardening an existing scaffold, for both the media
   package and the provider package.
3. **No local package feed, cross-package restore, or isolated-cache tooling exists.** There is no
   `NuGet.Config` in the repository root, and no script packs first-party projects — or an out-of-repo
   package's own domain project — for external consumption today. `eng/proofs/*` currently only stages
   first-party projects by `dotnet publish` of an in-tree `.csproj`; nothing exercises `dotnet pack` against a
   consumer that never had repository access, and nothing exercises one out-of-repo package restoring another
   out-of-repo package's packed output, which is the specific mechanism §2's two-package split and §3 phase 3
   depend on. This is buildable now — it depends on nothing still in flight — but does not exist.
4. **Trimming remains disabled** (`Arronix.Client.csproj`, `PublishTrimmed=false`), recorded G07.1 debt. G07A
   inherits this rather than resolving it; the browser proof, like G07.1's, runs against the same
   ordinary-but-untrimmed `dotnet publish` output. Not a G07A blocker, but worth restating so the eventual
   G07A record does not silently claim a smaller download than what ships.
5. **`docs/design/plugin-distribution.md`'s zip/`package.json` distribution format is not implemented.**
   The real admission path today reads a plain folder per package under `Arronix__Plugins__RootFolder`
   (`PluginRuntimeOptions.RootFolder`, default `"plugins"`), populated by `dotnet publish --output`, exactly
   as `eng/proofs/g07-client-contracts.sh` already does. That design document is itself flagged by the
   roadmap as a research input requiring revalidation, not an executable plan; §2's topology and §3's commands
   in this report describe the real mechanism, not the aspirational one. G07A must not be implemented against
   the unbuilt zip format.
6. **Five platform services were the last blocker to any entry-assembly package activating in a real host,
   and that blocker is now closed.** `CONTEXT.md`'s current technical-debt section records that an ordinary
   `Arronix.Api` server now composes `ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`,
   `IHostRuntimeInfo`, and `IOperatingSystemInfo`, and activates the independently published Video and Movies
   packages beside it. This directly unblocks both of §2's external packages — Package 1's media-kind module
   and Package 2's cataloger module both have entry assemblies — from reaching `Active` in the same
   real-server topology G07.1 already proved for Movies — recorded here because it is easy to mistake for
   still-open after reading only G07.1's own residual-gaps list, which predates the fix.
7. **The machine's default `dotnet` does not match the pinned SDK.** Confirmed 2026-08-27: `PATH` resolves
   `dotnet` to `/opt/homebrew/bin/dotnet`, Homebrew's `10.0.400`; the repository's `global.json` pins
   `11.0.100-preview.7.26381.103` at `/usr/local/share/dotnet/dotnet` with `rollForward: disable`. This is not
   a G07A-specific blocker — it applies to every restore, build, and test in this repository — but it is
   sharpest here because G07A's proof script is the first one that also restores and builds a project *outside*
   the repository, where there is no `global.json` inherited by proximity unless each fixture ships its own —
   now sharper still with two independent fixture directories instead of one. §0 states the operational rule;
   the retained script must set `DOTNET_COMMAND`/`$dotnet_command` explicitly and both fixtures' own
   directories should each carry a `global.json` pinning the same SDK, rather than relying on whichever
   `dotnet` a future operator's shell happens to find first.

## 9. Recommendation for sequencing

Split G07A into three slices when it is implemented — one more than previously recorded here, because §6's
open integration decision is now its own piece of work rather than an assumption folded into Slice B:

- **Slice A (buildable now):** both external packages from §2 — pack, cross-package restore (the domain nupkg
  chain in §3 phases 2–3), build, the private-copy check, provider-pairing admission, exact CLR identity, and
  the full diagnostic matrix in §5 through the client-load row, ending at "both packages reach `Active` in an
  unmodified `Arronix.Api`, in dependency order, and Package 1 publishes its client facet." Depends only on
  G01–G06, all complete, and proves the two-package pairing G06 alone never attempted.
- **Slice B0 (a decision, not code):** whoever implements G07.2 records which of §6's three readings of
  "reuse" applies, and states it in G07.2's own record before Slice B is scoped or estimated. This is
  deliberately sequenced before Slice B rather than folded into it, so Slice B's own cost estimate is not
  built on an assumption this document explicitly declined to make.
- **Slice B (blocked on G07.2 and on Slice B0):** the browser load of Package 1's client-safe assembly against
  the unmodified `Arronix.Client`, and the fixture-projection check — whose shape depends entirely on Slice
  B0's answer: reading 1 or 2 (§6) requires Package 1's own generator output to reach the browser; reading 3
  requires only that the pre-existing Movie fixture still renders after Package 1 is installed alongside it.

Slice A retires the "not retained, compile-only" and "no cross-package pairing" halves of §1's gap list
without waiting on work whose completion this document cannot itself certify — re-check the roadmap's
`**Status:**` fields at implementation time rather than trusting §8 item 1's 2026-08-27 reading. Slice B is a
small addition once G07.2 lands and Slice B0 is answered, because it reuses G07.1's loader and whichever of
G07.2's fixture mechanisms Slice B0 named, rather than inventing either.

Both slices, and every command either one runs, use `/usr/local/share/dotnet/dotnet` (§0) — never the
machine-default `dotnet` — and never relax `net11.0` or the pinned prerelease SDK to work around a restore or
build failure; a failure caused by the wrong SDK being picked up is a script defect to fix, not a target to
downgrade.
