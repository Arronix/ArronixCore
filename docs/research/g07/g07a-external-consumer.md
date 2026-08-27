# G07A record — the package-only external consumer

**Status:** the package, admission, identity and contract-loading legs are built, retained and executed.
G07A is **not closed**: its exit gate names browser rendering of the consumer's own serialized item, and
that fixture does not exist until G07.2 defines it. §6 states the seam exactly.

**Retained proof:** `eng/proofs/g07a-external-consumer.sh`, with
`eng/proofs/g07a-read-installation.py` and the two consumers under `eng/proofs/fixtures`. Run it as
`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07a-external-consumer.sh --serve`. The
declaration rules its result depends on are also held on the ordinary test rail by
`Arronix.Architecture.Tests.Topology.ExternalConsumerFixtureTests`.

**Prepared by** `g07a-external-consumer-readiness.md`, whose Slice A this implements. Where this record and
that audit disagree, this one is the landed fact; §7 lists the corrections.

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

**The format is `arronix.format.video`, reused rather than re-invented** — the readiness audit's §7 left
this open and recommended exactly this. `MediaType`'s constructor requires non-empty format composition;
composing the installed video package satisfies it without spending a self-authored format capability the
gate never asked for, and it makes the run a second, independently built witness to G03's one-shared-`Video`
claim.

## 2. What the script proves

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
8. **One mutation.** A copy of `Northmark.Shorts.Domain.dll` placed in the provider's payload quarantines
   the provider, naming the private copy, the duplicated assembly and the package that publishes it, while
   the media package stays `Active`. Without this row, every check above would also pass for a provider that
   closed its cataloger over a second, unrelated CLR type.

## 3. Browser half, as executed

Run on 2026-08-27 against the ordinary published `Arronix.Client` and the server the script starts, with a
real browser at `/contracts`, reading `#contract-proof`:

- clean store: `Northmark.Shorts.Domain.dll` — outcome `Loaded`, source `Network`; observed length, content
  hash, assembly identity and module version identifier each equal to the published value; observed contract
  reference `Arronix.Abstractions, Version=0.9.0.0`; `refused` empty; `canProject` true;
- the declarations read back out of the loaded runtime carry the same entry point type, entity type name,
  generated-metadata hash and projection-schema hash the host published;
- second navigation: same assembly, outcome `Loaded`, source `Store` — the warm start refetches nothing.

That is G07.1's loader and G07.2's landed declaration reading, exercised for the first time against a
package built with no access to this repository.

## 4. Two authoring findings

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

## 5. The author diagnostic this gate also closed

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

## 6. The seam this stops at

The exit gate asks for the consumer's client-safe contract to be loaded "using the serialized G07 fixture",
and the readiness audit's §6 recorded that what *reuse* means was undecided. It is still undecided, and the
work it depends on is not landed: there is no endpoint serving a serialized item payload and no projector
turning one into rendered values, for any package.

What exists on the external package's side of that seam is complete: the domain assembly declares its
serialization context, carries generated serialization metadata for the whole `ShortFilm` graph, publishes a
generated-metadata hash and a projection-schema hash, and a browser has loaded it and read those hashes back
out of the runtime. What remains is entirely G07.2's:

- a payload of a `ShortFilm`, written through that context;
- the client-side path that reads a payload through a loaded entry point and projects it;
- the decision, recorded when G07.2 lands, of which of the audit's three readings of "reuse" applies.

Nothing here substitutes a `Movie` payload for a short film, and no external item source, test-only endpoint
or premature catalog workflow was added to make the page look further along than it is.

## 7. Corrections to the readiness audit

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
