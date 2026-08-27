# G07A record — the package-only external consumer

**Status:** the package, admission, identity, contract-loading and browser-rendering legs are all built,
retained and executed. G07A is **not self-declared closed here**: it is a candidate for independent
acceptance. It follows accepted G07.3; its 37-check external-consumer proof remains separate from G07.3's
30 + 43 + 64 = 137 checks. §6 records what closing the browser leg found and fixed, because it changed a
claim G07.2 had already made.

**Retained proof:** `eng/proofs/g07a-external-consumer.sh`, with `eng/proofs/g07a-read-installation.py`,
the two consumers under `eng/proofs/fixtures/g07a-media` and `eng/proofs/fixtures/g07a-provider`, the
fixture writer under `eng/proofs/g07a-fixture`, and the committed fixture
`eng/proofs/fixtures/g07a/shortfilm.json`. Run the server half as

```
DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07a-external-consumer.sh --serve
```

and the browser half, needing Playwright's Chromium beside this repository, as

```
DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07a-external-consumer.sh --browser
```

which drives `eng/proofs/g07-browser-proof.mjs --mode g07a` against the server it starts. The declaration
rules the server half depends on are also held on the ordinary test rail by
`Arronix.Architecture.Tests.Topology.ExternalConsumerFixtureTests`.

**Prepared by** `g07a-external-consumer-readiness.md`, whose Slice A the server half implements; §7 of the
original record (reproduced below) lists its corrections. The initial server half landed on an earlier base;
this record ports it after the accepted G07.2 and G07.3 work, preserving G07.3's harness and composition
while closing the payload-rendering seam the original record left open.

## 1. Topology

Two packages, built from two directories that stop MSBuild's upward search at their own root, so no
property, package version or build prop of this repository configures either.

| | Package 1 — `northmark.shorts` | Package 2 — `northmark.shorts.catalog` |
| --- | --- | --- |
| Source | `eng/proofs/fixtures/g07a-media` | `eng/proofs/fixtures/g07a-provider` |
| Assemblies | `Northmark.Shorts.Domain.dll` (shared, client-safe) + `Northmark.Shorts.dll` (entry) | `Northmark.Shorts.Catalog.dll` (entry) |
| Owns | `ShortFilm`, its timeline and stage, its `JsonSerializerContext`; the `Shorts` media type and its parser | `ICataloger<ShortFilm>` and the `northmark` identifier scheme |
| Packages taken | `Arronix.Sdk`, `Arronix.Format.Video` (compile only), `Northmark.Shorts.Domain` | `Arronix.Abstractions`, `Northmark.Shorts.Domain` (compile only) |
| Declares | `media-kind`, `parsing`, `matching`, `indexing` | `metadata` |

Package 2 has never seen package 1's source, project or extension assembly. The only thing they share is a
`.nupkg` that package 1's domain project produced, restored into package 2's own empty package cache.

The format is `arronix.format.video`, reused rather than re-invented: `MediaType`'s constructor requires
non-empty format composition, composing the installed video package satisfies it without spending a
self-authored format capability the gate never asked for, and it makes the run a second, independently
built witness to G03's one-shared-`Video` claim.

## 2. What the server half proves

Every `dotnet` invocation resolves through `DOTNET_COMMAND`; both consumers carry a `global.json` equal to
this repository's, so a wrong SDK fails rather than retargeting.

1. **Declarations.** Neither consumer declares a project reference, includes a file from outside itself,
   names a project in this repository, or grants internals visibility; no assembly here grants either of
   them any.
2. **No warm-cache fallback.** The provider's restore is run *first*, into an empty cache, against a feed
   holding every Arronix package but not the domain package. It must fail, naming `Northmark.Shorts.Domain`.
   Each consumer then restores into its own empty cache.
3. **Payloads.** The media package publishes its own domain assembly and no copy of the video format; the
   provider publishes neither. The domain assembly the provider *compiled against*, taken from its own
   package cache, hashes equal to the one the media package installs.
4. **Admission.** All three packages reach `Active` in an unmodified `Arronix.Api`, in dependency order,
   through the ordinary folder-per-package path. No test-only route or composition root exists.
5. **The generated compiled shape.** `GET /api/v1/kinds` publishes the `shorts` descriptor, including
   `premiere` — a field only the external item declares — so Host read the generated shape of a type it has
   no compile-time knowledge of.
6. **The pairing.** `GET /api/v1/providers` reports `northmark.shorts.catalog:northmark-shorts` as a
   cataloger. Admission proved its closed item type against the admitted kind before constructing it.
7. **The generated client contract.** `GET /api/v1/client-contracts` publishes the external domain
   assembly's own declared entry point — `Northmark.Shorts.ShortFilmClientContractEntryPointAttribute`,
   entity `Northmark.Shorts.ShortFilm`, a generated-metadata hash and a projection-schema hash — under the
   shared identity both packages bound to, served content-addressed, with the entry assembly refused.
8. **The ShortFilm fixture is served as the bytes its own contract wrote**, byte for byte, exactly as
   `eng/proofs/fixtures/g07/movie.json` is for Movies.
9. **One mutation.** A copy of `Northmark.Shorts.Domain.dll` placed in the provider's payload quarantines
   the provider, naming the private copy, the duplicated assembly and the package that publishes it, while
   the media package stays `Active`. Without this row, every check above would also pass for a provider that
   closed its cataloger over a second, unrelated CLR type.

## 3. Two authoring findings

Both surfaced from running a genuinely trivial kind through real admission, and neither is fixed here.

1. **A trivial media kind is not expressible.** `MediaType` supplies default `Matching` and `Querying`
   values, admission validates that entry resolution carries at least one key layer and a query declaration
   at least one tier, and the defaults carry neither. The consumer therefore declares one match layer and one
   query tier it would otherwise not need. CONTEXT already records that Movies' likely-standard declarations
   need classification; this is the same debt seen from the author's side, and G17A is where it belongs.
2. **Capabilities are demanded for defaults the author never stated.** Because those default values exist,
   the kind must declare `matching` and `indexing`, and because a parser type is a constructor argument it
   must declare `parsing`. A package declaring `media-kind` alone is refused with `PluginCapabilityMissing`
   naming `MediaKindModel.Matching`. Least privilege is stated before code runs, which is right; deriving the
   demand from a default the author did not write is not obviously right, and the two interact.

## 4. The author diagnostic this gate also closed

`PlatformSymbols` resolves every platform type from the one referenced contract and returns nothing unless
all of them resolve. Nothing distinguished *no Arronix contract* from *an incomplete or duplicated one*, so
the second silently turned every generator off: a media declaration lost its compiled shape and surfaced
later as a missing abstract member, and a client-safe assembly lost its client contract and surfaced only in
a browser, as an installed package nothing could project.

`ARX1004` now reports it, at the declaration, naming what is wrong:

- more than one referenced assembly declares `IMediaEntity`, with every identity named;
- a contract type the referenced contract does not declare, said differently when another referenced
  assembly does declare it — that is the version-skew case and has a different remedy;
- a framework shape absent from the reference set.

It reports only where a declaration in the compilation derives from a contract's own `MediaType<,,,>` or
`MediaItem<,,>`, so a compilation that merely references Arronix — Host, Common, every test project — hears
nothing, and one with no Arronix contract hears nothing whatever its reference set looks like. The known
limit is that a contract too degraded to declare either base makes no compilation identifiable as an
authoring one, and nothing is reported.

`Arronix.Generators.Tests.PlatformResolutionDiagnosticTests` compiles its own contract assembly per case —
complete, missing one declaration, or duplicated — because the defect is a property of a reference set. The
control asserts a positive, `ARX1003` on a non-partial declaration, which is reachable only through a
resolved reading. Three mutations were run against the landed tests: never reporting fails five cases,
reporting regardless of applicability fails the no-noise case, and reporting while the reading succeeded
fails the control.

## 5. Closing the seam: a ShortFilm payload through the unchanged Movie panel

The original record's §6 left one seam open: G07.2 had not yet defined a serialized-payload path, so there
was nothing for the external consumer's browser half to read through. G07.2 has since landed, and settled
the question its own roadmap left open — the Client "spells no media kind and no installation's own path,
including no default payload address, which is what lets the same unchanged panel take an external
consumer's contract at G07A." That is the reading adopted here: not a `Movie` payload substituted for a
short film, and not a parallel payload route, but the same `ContractPayloadLoader` and
`ContractPayloadPanel` reading a payload this consumer owns.

**The fixture.** `eng/proofs/g07a-fixture/Northmark.Shorts.Fixture` is a small console tool, taken as an
ordinary consumer of the packed `Northmark.Shorts.Domain` package — no project reference into the media
package, exactly like package 2. It builds a canonical `ShortFilm` — title, overview, external identifiers,
genres, artwork, and the field the gate names: a `Premiere` (festival and edition) alongside a `Lifecycle`
whose `Premiered` is a real date — and writes it through the domain assembly's own
`ClientContractEntryPointAttribute.Serialize`, the same reflection lookup a browser's loaded assembly
answers. `eng/proofs/g07a-external-consumer.sh` runs it after packing the domain package and holds the
committed `eng/proofs/fixtures/g07a/shortfilm.json` to what it writes, byte for byte — the same drift
discipline `MovieFixtureTests` holds `movie.json` to, `ARRONIX_REGENERATE_G07A_FIXTURE=1` included. The
fixture is proof-only input, served as an ordinary static file beside the client's own; neither Host nor
Client gained a dependency on it, and no test endpoint or alternate payload route was added.

**The browser proof.** `eng/proofs/g07-browser-proof.mjs` gained a `--mode g07a`, sharing its installation
checks, its fetch-and-project mechanics and its "unavailable" / "off-origin" refusal checks with the
existing `g07` mode verbatim, and replacing only the Movie-specific field assertions with ShortFilm's own.
Run against the real server `eng/proofs/g07a-external-consumer.sh --browser` starts:

- the installation is compatible and every required assembly is resident, exactly as for Movies;
- two client contracts are fetched over the network: `northmark.shorts` itself and `arronix.format.video`,
  the format contract its domain assembly depends on; no other contract is fetched;
- the payload projects, read into `Northmark.Shorts.ShortFilm` — the exact runtime type, proved by object
  identity, the same rule G07.2 proves for `Arronix.Media.Movies.Movie`;
- the document carries one `artwork`, one `premiere`, one `lifecycle` and one `status` field, each present;
- the `lifecycle` composite carries its own `premiered` part, rendered rather than absent — **the
  premiere/date value the exit gate names**;
- `status` renders the stage that date puts the film in;
- the `premiere` composite's own parts are drawn under their own component ids — `festival` and `edition`,
  names this client has never heard of — and carry the fixture's own festival and year;
- the poster decodes as a real image, inline, so this proof fetches nothing for it;
- a payload this host does not serve is refused visibly, and an address off this origin is refused before
  anything is fetched — the same two refusal paths G07.2 proved, exercised a second time against a second
  contract.

37 of 37 browser checks passed. Evidence: `artifacts/g07a/evidence/browser-half.json`,
`artifacts/g07a/evidence/server-half.txt`.

## 6. The payload-panel defect, already fixed on this base

The original G07A work exposed that `ContractPayloadLoader` had an `internal` constructor but was registered
through reflection activation, so the panel could not resolve. The accepted G07.3 base already carries the
correct same-assembly factory registration and its behavioural coverage. This port deliberately leaves that
composition unchanged; its 37-check ShortFilm run exercises the shared loader and panel, while the unchanged
G07.3 re-run independently retains the 43 G07.2 browser checks and 64 lifecycle checks. G07A therefore does
not claim to fix or re-close a G07.3 concern.

## 7. What closing this leg found: a proof script can outlive itself

Driving the browser half also surfaced a defect in the proof rail rather than the product: an earlier run of
`eng/proofs/g07a-external-consumer.sh` left its server process running after the script exited, because it
started the server with `dotnet run --project`, whose tracked process is the build/run wrapper rather than
the server it launches — killing the wrapper does not reliably reap the child. A later run could then have
silently observed that stale server instead of its own. Three corrections, scoped to this script:

- **A preflight.** Before anything else runs, both the primary port and the mutation port are checked with a
  portable Python socket probe; either one already listening is a loud, immediate failure naming the port,
  not a run that proceeds against a server it did not start.
- **The server is the built assembly, run directly.** `start_server` now launches `Arronix.Api.dll` itself
  rather than `dotnet run --project`, so `server_pid` is the one real server process and stopping it stops
  it. This uncovered a second, smaller fact: `dotnet run` changes into the project directory before it
  launches its child, which is where ASP.NET Core's default content root comes from — started as the built
  assembly with the repository root as its working directory, `appsettings.json` goes missing and the server
  fails the same options validation `HostIdentityOptions.ApplicationName` once already needed fixing for.
  `start_server` now sets `ASPNETCORE_CONTENTROOT` explicitly, to the assembly's own directory.
- **Stopping is verified, not assumed.** `stop_server` now polls the port it used until it stops answering,
  or fails the run loudly rather than reporting a clean stop that left a listener behind.

`eng/proofs/g07-client-contracts.sh` was not changed: it still starts the server through `dotnet run
--project`, and the fix above was not applied there. The two scripts now differ in this one respect; folding
the same discipline into the Movies proof is unstarted work, not a claim made here.

## 8. Corrections to the readiness audit

- **The domain project does take the analyzer.** The audit's §2 called that provisional on G07.2, and it is
  now settled the other way: `ClientContractGenerator` fires on a public non-abstract non-generic class
  deriving `MediaItem<,,>`, which is the domain assembly's item, and `ARX1010` requires a declared
  `JsonSerializerContext` beside it. The audit's §5 row asserting the domain project takes no analyzer is
  therefore obsolete, and no such assertion was written.
- **The Host-binding half of that row still holds.** `MediaShapeGenerator` matches only a `MediaType<,,,>`
  base, so `CompiledShapes` is generated on the entry assembly and nowhere else.
- **`ICurator` remains out**, as the audit recommended: the roadmap says one typed provider, and one
  cataloger exercises the pairing completely.
- **The audit's blocker 3 is retired.** A local feed, a cross-package restore and per-consumer isolated
  caches now exist.
- **The seam §6 of the original record named is retired too.** A serialized `ShortFilm`, owned by this
  consumer's own fixture, now flows through the unchanged generic payload panel; §5 and §6 above are the
  record of it.
