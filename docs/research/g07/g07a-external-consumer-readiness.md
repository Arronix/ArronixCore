# G07A readiness audit — the package-only external consumer

**Status:** research only. Nothing in this document is landed; it is the preparation Codex needs to
implement G07A. No code, fixture, or proof script in this branch changes production behaviour.

**Scope:** the delta between what G06 already proved and what G07A's exit gate requires, the topology and
commands an implementer needs, the artifact lifecycle a retained proof script must manage, a first test and
mutation matrix, the exact fixture contract G07A may and may not depend on, and the blockers that gate real
execution today. Provider ingestion, durable item rendering, persistence, a test-only production endpoint,
and any G07B or G28 work are explicitly out of scope, per the roadmap's own text for this gate.

## 1. What G06 already proved, and what it deliberately left open

G06 (`docs/research/g06/authoring-sdk-boundary.md`) proved the package-boundary half of the authoring
promise: `Arronix.Sdk 0.9.0`, built with `dotnet pack` and inspected, contains only `README.md`,
`analyzers/dotnet/cs/Arronix.Generators.dll`, and the `lib/net11.0/_._` metapackage marker; its only NuGet
dependency is `Arronix.Abstractions 0.9.0`. A project created outside the repository, referencing only that
packed package, restored and compiled a partial typed media declaration with zero warnings; removing
`partial` reproduced `ARX1003` at the declaration and restoring it returned the project to a clean build.

That proof is real but narrow, and the G06 record says so directly: "G07A retains the stronger permanent
proof of packaging, installation, admission, exact browser type identity, and rendering in an unmodified
Host and Client." Three gaps separate the two:

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
   provider" (a cataloger/curator pairing, admitted and quarantine-checked per G04/G05, but never ingested —
   see §6) and a browser-side load of the consumer's client-safe contract assembly through the unmodified
   `Arronix.Client`, which G06 never attempted.

G07A is therefore not a bigger G06 test; it is the first time any extension — first-party or not — is
proved end to end from `dotnet pack` through a real browser tab without ever touching this repository's
projects, sources, or internal visibility grants.

## 2. Topology

The consumer lives entirely outside `Arronix.sln`. It is a standalone directory (not necessarily committed
under `src/`, since it must prove it needs no repository project reference — a location such as
`eng/proofs/fixtures/g07a-external-consumer/` keeps it discoverable without adding it to the solution) that
references only packed artifacts:

```
external consumer (outside Arronix.sln)
├── ExternalKind.csproj          -- PackageReference: Arronix.Sdk, arronix.format.video (or an equally
│                                    trivial owned format — see §7), no ProjectReference, no InternalsVisibleTo
├── ExternalKind.cs               -- partial class : MediaType<TItem,TTarget,TRelease,TParser>
├── ExternalKindProvider.cs       -- ICataloger<TItem> / ICurator<TItem>, or a combined class implementing both
├── ExternalKindModule.cs         -- IPluginModule: AddMediaType<...>(), AddCataloger<...>(descriptor), AddCurator<...>(descriptor)
└── plugin.json                   -- schemaVersion 1, id distinct from movies/tmdb/arronix.format.video,
                                      entryAssembly + contractAssemblies + clientContracts + capabilities
```

This mirrors the shape every first-party plugin already has (`src/Arronix.Plugin.Movies`,
`src/Arronix.Provider.Tmdb`, `src/Arronix.Format.Video`) — the point of G07A is that the *only* difference
from a first-party extension is where it was built and what it was allowed to reference.

Runtime topology follows the G07.1 precedent in `eng/proofs/g07-client-contracts.sh` exactly, with one
package added to the same installation:

```
artifacts/g07a/
├── packages/
│   ├── arronix.format.video/     -- dotnet publish of the in-repo project (unchanged G07.1 staging)
│   ├── movies/                   -- optional: only needed if the consumer's own kind is not sufficient
│   │                                 alone to exercise a populated, non-empty installation
│   └── <external-id>/            -- dotnet publish of the OUT-OF-REPO consumer, built from its own
│                                     restored/packed dependency graph, copied (not built) into place
├── client/                       -- dotnet publish of the unmodified Arronix.Client (no proof-only flags)
└── evidence/                     -- server.log, manifest.json, plugins.json, browser #contract-proof text
```

`Arronix__Plugins__RootFolder` points at `artifacts/g07a/packages`; `Arronix__Api__ClientRoot` points at the
unmodified client publish output. The server is the real `Arronix.Api` `Program`, started with `dotnet run`,
exactly as G07.1 does — G07A's exit gate forbids a "test-only production endpoint," and the existing script
already proves the real composition is sufficient.

## 3. Commands

A retained proof script (`eng/proofs/g07a-external-consumer.sh`, following the header/option conventions of
`eng/proofs/g07-client-contracts.sh`) needs three phases the existing script does not have, run before the
existing staging/serving sequence:

1. **Pack the consumer's dependencies from this repository**, into a throwaway local feed:
   ```bash
   dotnet pack src/Arronix.Sdk/Arronix.Sdk.csproj -c Release -o "$feed_root"
   dotnet pack src/Arronix.Abstractions/Arronix.Abstractions.csproj -c Release -o "$feed_root"
   dotnet pack src/Arronix.Format.Video/Arronix.Format.Video.csproj -c Release -o "$feed_root"   # if reused, see §7
   ```
   `Arronix.Sdk.0.9.0.nupkg`'s only dependency is `Arronix.Abstractions 0.9.0`, and `Arronix.Generators` is
   never packed independently (`IsPackable=false`) — it is embedded as the SDK package's analyzer asset, so
   packing the SDK is sufficient; the generator project itself is never a restore target.

2. **Restore and build the consumer against only that feed, with an isolated package cache**, so the proof
   cannot silently succeed by falling back to a locally installed copy or a warm global cache:
   ```bash
   dotnet restore "$consumer_project" \
     --source "$feed_root" --source https://api.nuget.org/v3/index.json \
     --packages "$isolated_nuget_cache"
   dotnet build "$consumer_project" -c Release --no-restore -warnaserror
   ```
   nuget.org stays reachable only for the .NET SDK's own implicit packages; every `Arronix.*` reference must
   resolve from `$feed_root`. A `NuGet.Config` scoped to the fixture directory (this repository currently
   has none — restore relies on ambient machine configuration) is cleaner than repeated `--source` flags and
   makes "clean package cache" mean the same thing on every run.

3. **Pack and publish the consumer's own extension**, the same way `eng/proofs/g07-client-contracts.sh`
   publishes first-party packages — `dotnet publish --configuration Release --no-build --output
   artifacts/g07a/packages/<external-id>` — so the staged payload is the consumer's real publish closure, not
   a hand-copied `bin/` directory.

From there, the script reuses G07.1's pattern verbatim: build/publish the unmodified client, start
`Arronix.Api` against the combined `packages/` folder, assert over HTTP that the consumer's package reached
`Active` and offers its client facet, then hand off to a real browser (`--serve`, Playwright or manual) to
load the client-safe contract and read `#contract-proof`.

## 4. Artifact lifecycle

Every artifact this proof produces is disposable and regenerated on each run, matching the existing G07.1
discipline (`rm -rf "$proof_root"` at the top of the script, nothing written outside `artifacts/`):

| Artifact | Produced by | Lives under | Regenerated |
| --- | --- | --- | --- |
| Local package feed (`.nupkg` files) | `dotnet pack` of Sdk/Abstractions/(format) | `artifacts/g07a/feed/` | every run |
| Isolated NuGet cache | `dotnet restore --packages` | `artifacts/g07a/nuget-cache/` | every run |
| Consumer build output | `dotnet build`/`publish` of the out-of-repo project | consumer's own `bin`/`obj`, or redirected via `--output` | every run; `obj/` must be cleared first, per the G07.1 trimming lesson about stale Blazor/publish state |
| Staged package payloads | `dotnet publish --no-build --output` per package | `artifacts/g07a/packages/<id>/` | every run |
| Client publish | `dotnet publish` of unmodified `Arronix.Client` | `artifacts/g07a/client/` | every run, `bin`/`obj` cleared first |
| Server process + logs | `dotnet run` against `Arronix.Api` | `artifacts/g07a/evidence/server.log` | every run |
| HTTP evidence | `curl` against `/api/v1/client-contracts`, `/api/v1/plugins` | `artifacts/g07a/evidence/*.json` | every run |
| Browser report | operator/Playwright reading `#contract-proof` | `artifacts/g07a/evidence/browser-*.txt` (recommended, not yet scripted) | every run |

The consumer's *source* is the one artifact that is not disposable — it is the retained fixture the exit
gate requires — but its build outputs, the local feed, and the isolated cache must never be. A retained feed
or cache is exactly the shortcut ("it still works because a warm cache from last time has it") that would
quietly reopen the escape hatches §6 rules out.

## 5. Test and mutation matrix

G07A's own exit gate is a positive/negative pair at every layer, following the precedent both G06
(`ARX1003` negative build) and G07.1 (nine-mutation table in `client-contract-loading.md` §4.4) already set.
A first matrix, to be executed by the retained script plus a small number of hermetic unit cases colocated
with it:

| Layer | Positive case | Negative case | Expected refusal / diagnostic |
| --- | --- | --- | --- |
| Authoring | Consumer's media type is `partial` | Remove `partial` | `ARX1003` at the declaration, build fails |
| Package restore | Restore from the local feed only | Point `--source` at an empty feed | NU1101 / restore failure, no fallback resolution |
| Provider pairing | Cataloger/curator close `ICataloger<TItem>`/`ICurator<TItem>` for the consumer's own item type | Implementation closes no member of the family, or closes it over the wrong item type | `PluginProviderContractInvalid` |
| Media/provider coherence | Consumer's kind is admitted before its provider | Package the provider alone, without an admitted kind supplying its item type | `PluginMediaPairingUnsatisfied` |
| Kind admission | One item type, one kind | A second package closes a kind over the same item type | `MediaItemTypeConflict` |
| Contract range | `plugin.json` declares `>=0.9 <0.10` | Declare an incompatible range (e.g. `>=0.1 <0.2`) | `PluginContractMismatch`, quarantined before construction |
| Escape hatch: project reference | none present | add a `ProjectReference` to any in-repo project | build proof must fail closed (script asserts the consumer's `.csproj` contains no `ProjectReference`) |
| Escape hatch: `InternalsVisibleTo` | none present, none needed | grant `InternalsVisibleTo` from any first-party assembly to the consumer | architecture-test-style assertion: grep the solution for a grant naming the fixture; today's five real grants (`Arronix.Host`, three `*.Tests` assemblies, `Arronix.Client.Tests`) are the complete list and none should ever name this fixture |
| Client load | Consumer's own client-safe assembly hashes, verifies, and loads under G07.1's two-pass loader | Flip one byte of the staged consumer assembly (same mutation G07.1 already proves generically) | `ContentHashMismatch`, `CanProject=false`, nothing resident |
| Browser fixture projection | The already-published G07.2 serialized fixture renders through the unmodified `ContractsPage.razor` | n/a — G07A does not invent a second rendering path | — |

The escape-hatch rows are the ones with no first-party precedent to imitate: they are assertions about the
*fixture's own project file and the solution's `InternalsVisibleTo` grants*, not about Host behaviour, and
belong in the proof script itself (grep/assert) rather than in a unit test that only ever sees the repository
side of the boundary.

## 6. Fixture contract — what G07A's consumer may and must not depend on

Per the roadmap text verbatim, the consumer:

- defines **a trivial typed media kind and one typed provider** — enough to exercise `AddMediaType<T>()`,
  `AddCataloger<T>`/`AddCurator<T>`, and the G04 pairing check, not a realistic domain;
- **packages and installs into an unmodified Host** — the real `Arronix.Api`, no test-only route, no
  fixture-specific composition root;
- **loads its client-safe typed contract in the unmodified browser Client using the serialized G07
  fixture** — this is the one clause that reaches outside G07A's own package. Read against G07.2's own
  Implement list ("typed deserialization of a Movie graph... the serialized fixture G07A later reuses"), the
  fixture G07A loads in the browser is the **existing G07.2 Movie fixture**, not a fixture the consumer's own
  trivial kind generates. G07A's contribution to the browser half is therefore narrower than it first reads:
  it proves the consumer's *contract assembly* loads and verifies through the unmodified client (the G07.1
  mechanism, already complete and generic over any package), while typed deserialization/rendering is
  exercised against the fixture G07.2 already built and published, not reimplemented per-kind.
- explicitly **does not** ingest provider results or render a durable item — "Provider-result ingestion and
  durable item rendering remain G19/G28 work" — so the provider only needs to admit and pass the G04/G05
  contract checks; it is never invoked through `CatalogDispatcher` in this proof;
- **must not** add "a temporary external item source, test-only production endpoint, or premature generic
  catalog workflow merely to make this smoke test render provider results" — i.e. no shortcut that makes the
  provider *look* ingested is acceptable, even if it would make the browser page more convincing.

## 7. Open scoping decision: does the consumer need its own format?

`MediaType`'s primary constructor requires non-empty format composition (`INTERFACE.md` §3,
`typed-media-north-star.md` rule 8). Two honest options, neither settled by the roadmap text:

- **Reuse `arronix.format.video`.** It is already an independently installable, no-entry-assembly,
  zero-capability contract package (`src/Arronix.Format.Video/plugin.json`) — the cheapest way to satisfy the
  constructor without inventing a second format capability. The consumer's dependency edge
  (`{ "package": "arronix.format.video", "range": ">=0.1 <0.2" }`) then also incidentally re-proves the G03
  shared-CLR-identity claim ("two independent fixture dependants share the same admitted Video contract
  identity") for a package built with no repository access, which G03 itself never claimed.
- **Compose a still-simpler format the consumer defines and ships itself.** More faithfully "external," but
  spends scope G07A does not ask for — a self-authored format capability is not in the roadmap's Implement
  list for this gate, and the exit gate's own phrase is "a trivial typed media kind," singular, not two new
  extensions.

Reusing `arronix.format.video` is the lower-risk default; record the choice in the eventual G07A record
rather than deciding it silently in code, per this repository's own discipline against turning a mechanism's
convenience into an undocumented decision (`CONTEXT.md`, owner-ledger O-33).

## 8. Blockers

1. **G07.2 has not started.** Every sibling worktree cut for this phase of work
   (`claude/g072-metadata-spike`, `claude/g072-integration`, `claude/g072-browser-proof`,
   `claude/g073-lifecycle-audit`, `claude/g07b-durable-audit`) is at the same commit (`ce643da45`) as this
   branch — none carries G07.2 or G07.3 work yet. G07A's browser half depends on G07.2's generated client
   metadata and its serialized Movie fixture (§6). The roadmap states the dependency directly: "G07 is closed
   only when all three are closed; G07A and G07B remain later gates and none of their work is folded into
   these." **This blocks only the browser-fixture-projection portion of G07A**, not the package/restore/pack/
   admission/CLR-identity portion, which depends only on already-complete G01–G06.
2. **No retained external-consumer fixture exists.** Confirmed by absence from `Arronix.sln` and by grep
   across the tree; this is expected (G06's proof was intentionally ephemeral) but means G07A starts from
   nothing rather than hardening an existing scaffold.
3. **No local package feed or isolated-cache tooling exists.** There is no `NuGet.Config` in the repository
   root, and no script packs first-party projects for external consumption today. `eng/proofs/*` currently
   only stages first-party projects by `dotnet publish` of an in-tree `.csproj`; nothing exercises `dotnet
   pack` against a consumer that never had repository access. This is buildable now — it depends on nothing
   still in flight — but does not exist.
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
   packages beside it. This directly unblocks the consumer's own entry-assembly package (its media
   kind/provider module) from reaching `Active` in the same real-server topology G07.1 already proved for
   Movies — recorded here because it is easy to mistake for still-open after reading only G07.1's own
   residual-gaps list, which predates the fix.

## 9. Recommendation for sequencing

Split G07A into two slices when it is implemented:

- **Slice A (buildable now):** pack, isolated restore, build, provider-pairing admission, exact CLR identity,
  and the full diagnostic matrix in §5, ending at "the package reaches `Active` in an unmodified `Arronix.Api`
  and publishes its client facet." Depends only on G01–G06, all complete.
- **Slice B (blocked on G07.2):** the browser load of the consumer's client-safe assembly against the
  unmodified `Arronix.Client`, and the fixture-projection check against G07.2's serialized Movie fixture.

Slice A retires the "not retained, compile-only" half of §1's gap list without waiting on work this branch
cannot see. Slice B is a small addition once G07.2 lands, because it reuses G07.1's loader and G07.2's
fixture rather than inventing either.
