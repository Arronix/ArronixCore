# G04 — the Movies and Video package topology, as built

**Status:** superseded as current state by
[`integrated-package-runtime.md`](integrated-package-runtime.md). This remains an accurate record of the
`claude/g04-package-topology` branch at base commit `1b8ce1b3f`, including its section 7 list of what that
change deliberately did not do. Those items are now done; read the integrated report for what the repository
does today.

This document records the assembly and package topology that now exists in the repository for the movies
and video packages, the rules it is held to, and what it deliberately does not do. It is a record of a
change, not a proposal: every rule below is asserted by a test named in section 6.

A note on how to read section 7: this document has been wrong once. An earlier revision stated that the
movies types kept the `Arronix.Plugin.Movies` namespace. They did not — they were renamed with the assembly
— and the claim was corrected rather than the code. Section 7 is the list of what is genuinely missing, and
it is worth more than the rest of the document if the two ever disagree again.

The owner-adopted topology this implements is:

- one package, not one package per facet. A package may ship zero or more shared contract assemblies and
  zero or one isolated executable entry assembly;
- one dependency names a package id and a compatible range — not separate contract and plugin edges, and
  not an arbitrary facet name;
- shared contract assemblies ultimately load once, in a Host-owned collectible contract context, not in
  the default context;
- a shared contract assembly contains typed owner semantics and pure deterministic behavior only. It must
  not contain `IPluginModule`, parser or registration execution, provider implementations, I/O, mutable
  process state, or module initializers.

The dependency declaration, the loader's admitted-contract resolution, and the dependency-aware lifecycle
are **not** in this branch. Section 7 states exactly what is still missing and what currently stands in
for it.

---

## 1. Why an assembly split was the only available move

`Movie` shipped in `Arronix.Plugin.Movies.dll` beside `MoviesPluginModule`, the `Movies` definition and
`MovieReleaseParser`. A separately shipped cataloger implementing `ICataloger<Movie>` has to reference the
assembly that declares `Movie`, which left two choices and no third:

- **ship it privately.** `PluginLoadContext` unifies exactly one assembly with the default context —
  `Arronix.Abstractions` — and loads everything else as a private byte-array copy. The provider's `Movie`
  is then a different `Type` from the registered kind's item type, `IsInstanceOfType` returns false, the
  registration is skipped, and the operator is told the extension contributed nothing. It is not a load
  error, which is what makes it expensive to diagnose.
- **share the whole assembly.** That fixes type identity and takes the module, the parser and the
  generated reader delegates with it, onto a release cadence set by every dependant of the package rather
  than by the package itself.

The G03 CLR-identity spike measured both outcomes directly and recorded the operative rule: type identity
is a function of assembly identity and load context, and of nothing else. Matching names do not produce
it; matching versions do not; byte-identical files do not. Only being handed the same loaded `Assembly`
produces it — which means the packaging boundary decides what can be shared, and no loader rule can
rescue a single assembly that mixes the two lifetimes.

So the split is the substance of the change. Everything else — the manifest declaration, the resolution
step, the dependency-ordered withdrawal — is machinery that only becomes expressible once it exists.

---

## 2. What now exists

### 2.1 The movies package

| Assembly | Role | References |
|---|---|---|
| `Arronix.Media.Movies` | shared contract assembly | `Arronix.Abstractions` |
| `Arronix.Plugin.Movies` | isolated executable entry assembly | `Arronix.Abstractions`, `Arronix.Media.Movies`, `Arronix.Format.Video`, `Arronix.Generators` (analyzer only) |

`Arronix.Media.Movies` holds `Movie`, `MovieReleaseStage` and `MovieReleaseTimeline` — the item type, the
lifecycle it closes over, and the stage vocabulary that lifecycle produces. That is exactly the set an
`ICataloger<Movie>` or `ICurator<Movie>` must be able to name and construct.

`Arronix.Plugin.Movies` keeps the `Movies` definition, `MovieReleaseParser`, `MoviesPluginModule` and —
because the generator emits into whichever assembly declares the `MediaType` subclass — the generated
`CompiledShapes` catalog with its compiled reader delegates. No generator change was needed for the split,
and the contract project takes no analyzer precisely so that a definition cannot migrate there and bring
those delegates onto the shared cadence.

**On the name.** `Arronix.Media.Movies`, not `Arronix.Plugin.Movies.Contracts`. `Movie` is a media-domain
type; a plugin is how the movies extension reaches the host, and naming the domain assembly after the
delivery mechanism gets the ownership backwards. The pairing an independent vendor writes is
`class TmdbMovies : ICataloger<Movie>` against the movies media domain — the extension is not in that
sentence and should not be in the assembly name either.

### 2.2 The video package

| Assembly | Role | References |
|---|---|---|
| `Arronix.Format.Video` | shared contract assembly | `Arronix.Abstractions` |
| `Arronix.Format.Video.Contributions` | isolated executable assembly | `Arronix.Abstractions`, `Arronix.Format.Video` |

`Arronix.Format.Video` keeps its name and holds video's owner semantics — everything a media declaration
composes and everything a release carries:

- the representation and quality facts: `Video`, `VideoLineage`, `ReleaseChannel`, `SourceCarrier`,
  `AcquisitionMethod`, `TranscodeHistory`, `VideoResolution`, `ReleaseRevision`, `VideoStream`, `ScanMode`,
  `AudioTrack`, and the open identifier structs `VideoCodecId`, `AudioCodecId`, `DynamicRangeId`,
  `VideoDefectId`;
- `VideoFormat.Definition`, the format family a video media type names in its constructor;
- `VideoReleasePolicyDefaults`, the preferences video folds into a dependant's compiled release policy.

The last two are behavior, and they are here because of what kind of behavior they are: given a builder and
a selector, `Configure` adds three deterministic preferences over values and returns. It reads nothing,
writes nothing outside the object it was handed, and holds no state between calls. That is the same class
of thing as `VideoResolution.CompareTo` — owner semantics expressed as code — and it is what a media
declaration needs from video.

`Arronix.Format.Video.Contributions` holds `VideoReleaseVocabulary`, the token-to-lineage recognition
table, and is the home for the recognizers, probing and registration that follow it. Recognition reads
text and grows with every naming discovery, which is a different lifecycle from a value's meaning.

The consequence is the point: **no media extension references the Contributions assembly at all.** Movies
and Television reference only `Arronix.Format.Video`, so neither package's payload carries video's
executable half, and video can update its recognition work without touching either of them.

**On the names.** The stable domain surface keeps the plain name and the churning half is the one that
carries a qualifier, in both packages. A third-party media or provider author writes
`using Arronix.Format.Video;` and reaches the clean semantic domain; the assembly whose lifecycle is
different is visibly `Contributions`. Appending `.Contracts` to the domain name would have put the
qualifier on the stable surface and left the churn holding the plain one, which reads backwards.

Both video assemblies share the `Arronix.Format.Video` namespace. A format capability owns one
representation vocabulary, and which assembly a type ships in is a lifetime decision rather than a second
vocabulary; a package that references only `Arronix.Format.Video` never sees the contributions types at
all, because the assembly reference is the gate.

### 2.3 The reference graph

```text
                    Arronix.Abstractions
                     ▲     ▲          ▲
        ┌────────────┘     │          └───────────────┐
        │                  │                          │
 Arronix.Media.Movies       Arronix.Format.Video      │
        ▲    ▲                 ▲          ▲           │
        │    │                 │          │           │
        │    │   Arronix.Format.Video.Contributions    Arronix.Client
        │    │        (referenced by no                 (Abstractions only;
        │    └──────┐  media extension)                  references neither)
        │           │                     │
        │      Arronix.Plugin.Movies   Arronix.Plugin.Tv
        │
  Arronix.Architecture.Tests.MovieCatalogerFixture
  (a separately shipped provider: Abstractions + the movies media domain, nothing else)
```

Television is the second dependant of the video domain assembly and it is not a contrivance: it already
closes `TelevisionRelease` over `Video` and compiles `VideoReleasePolicyDefaults` into its own policy. Two
media packages compile against one video domain assembly today, which is the compile-time half of the
shared-identity requirement.

---

## 3. What may live in a shared contract assembly

The rule that decides placement is not behavior versus data. It is **deterministic value semantics versus
execution with a lifecycle**.

`MovieReleaseTimeline.StageOn(DateOnly)` encodes the theatrical-window rule and derives a release stage
from dates it is handed. `VideoResolution.CompareTo` orders rasters. Both are behavior, both are in the
shared half, and both belong there: they are the media or format owner's meaning about its own values.
Hoisting either into Host to keep the assembly "data only" would move domain semantics out of the domain
that owns them, which the north star forbids for good reasons that have nothing to do with packaging.

What is refused is execution that carries a lifecycle: a module invoked once per load, a provider Host
constructs, a parser compiled into a kind, a definition that drags generated delegates, I/O, recognition
tables, mutable process state, and anything that runs when the assembly is loaded.

Both shared assemblies were audited type by type against that line. `Arronix.Media.Movies` is one empty
nominal class, one enum, and one record whose methods are pure functions of their arguments. In
`Arronix.Format.Video` the representation family is records, record structs and enums whose only methods
are `CompareTo` and `ToString`; `VideoFormat` is one canonical value; and `VideoReleasePolicyDefaults` is a
single static method that adds deterministic preferences to a builder it is handed. Neither assembly
contains a regular expression, a lookup table, an author-declared static delegate, a writable static, a
module initializer, or an I/O call.

One canonical value needed more than a declaration to be safe. `VideoFormat.Definition` is created once and
handed to every video media type, so its contents are process-global; its extension list was a collection
expression behind an `IReadOnlyList<string>`, which is an array underneath and can be cast back and edited
by any caller. Read-only at the declaration was not read-only at the boundary. It is a
`ReadOnlyCollection<string>` now, and the format tests assert the wrapper rather than the interface — the
distinction only matters where one object is shared installation-wide, which is exactly here.

---

## 4. Lifetime, stated accurately

A shared contract assembly is admitted once per installation into a **Host-owned collectible context** and
is released only once every dependant has withdrawn. Its release cadence therefore belongs to the whole
dependency graph rather than to its own package.

That is not the same as permanence, and this document does not claim it is. The context is collectible;
the constraint is ordering, not immortality. The G03 spike also recorded the honest limit on the other
side: `AssemblyLoadContext.Unload()` is a request, not a check — the runtime accepts it while dependants
still hold types, releases nothing, and leaves a context that can never serve another dependant. So the
refusal has to be the platform's, and actual collection is never promised merely because Arronix dropped
its roots and called `Unload`. None of that mechanism is in this branch; what is in this branch is the
packaging that makes it expressible.

Today, with no admitted-contract resolution, both shared assemblies still load privately into each
dependant's own `PluginLoadContext`, exactly as they did before the split. The split changes what *can* be
shared. It does not yet share anything, and no test here says otherwise.

---

## 5. The independent provider proof

`src/Arronix.Architecture.Tests.MovieCatalogerFixture` stands in for a separately shipped vendor package.
It declares `IndependentMovieCataloger : ICataloger<Movie>` and `IndependentMovieCurator : ICurator<Movie>`
and references `Arronix.Abstractions` and `Arronix.Media.Movies` — nothing else. Its compiled reference
table is asserted to be exactly those two.

What that proves: a provider states its item relationship once, as an ordinary closed generic; the `Movie`
it closes over comes from the movies media domain assembly; it can construct a fully shaped `Movie`
including the media-owned lifecycle whose stage the availability selection reads; it owns its own
identifier marker spelling, which the movies package neither knows nor can be made to know; and it needs
no part of the Movies extension to do any of it. Before the split, none of those sentences could be true
at once.

What it does not prove: that a `typeof(Movie)` observed inside a separately loaded provider is reference
equal to the registered kind's item type. That is a loader property. It requires the admitted-contract
resolution step, and it is proved — or not — by the loader's own fixtures. Nothing in this branch should be
read as evidence for it.

The fixture is also not provider coverage. It has no transport, and a real vendor cataloger with an HTTP
boundary is G05's work.

---

## 6. What is asserted, and where

| Rule | Test |
|---|---|
| A shared contract project references only Abstractions and other shared contracts, and takes no package | `PackageFacetTopologyTests.SharedContractProjectReferencesOnlyContractsAndOtherSharedContracts` |
| A shared contract project references no Host, loader, generator, format-executable or media-extension project | `PackageFacetTopologyTests.SharedContractProjectReferencesNoHostLoaderOrExecutableProject` |
| The compiled shared assembly links nothing outside the shared set | `PackageFacetTopologyTests.SharedContractAssemblyLinksNoArronixAssemblyOutsideTheSharedSet` |
| No `IPluginModule`, `IProvider`, `IReleaseParser<>`, `IMediaTypeDefinition` or `MediaType<,,,>` subclass in a shared assembly | `PackageFacetTopologyTests.SharedContractAssemblyDeclaresNoExecutablePlatformType` |
| No writable static and no static delegate in a shared assembly | `PackageFacetTopologyTests.SharedContractAssemblyHoldsNoMutableOrExecutableStaticState` |
| Nothing runs when a shared assembly is loaded | `PackageFacetTopologyTests.SharedContractAssemblyRunsNothingWhenItIsLoaded` |
| No regex, I/O or ambient-state spelling in shared contract source | `PackageFacetTopologyTests.SharedContractSourceContainsNoRecognitionOrIoSpelling` |
| The module, definition, parser, vocabulary, family definition and policy defaults are still in the isolated assemblies | `PackageFacetTopologyTests.TheIsolatedAssembliesStillHoldTheExecutableCode` |
| An extension references no other kind's media domain | `PackageFacetTopologyTests.MediaExtensionReferencesNoOtherKindsMediaDomain` |
| The video domain assembly references Abstractions only, its contributions reference it, and never the reverse | `FormatCapabilityTopologyTests.VideoDependsOnlyOnTheUniversalContracts`, `.VideoContributionsDependOnlyOnTheUniversalContractsAndTheVideoDomain`, `.TheVideoDomainDoesNotReferenceItsContributions` |
| Two media extensions compile against one video domain assembly | `FormatCapabilityTopologyTests.TwoMediaExtensionsCompileAgainstTheOneVideoDomainAssembly` |
| No media extension declares, links, or ships a format's executable half | `PackageFacetTopologyTests.MediaExtensionDeclaresNoReferenceToAFormatExecutableAssembly`, `.MediaExtensionLinksNoFormatExecutableAssembly`, `PackagedMoviesLayoutTests.NoMediaExtensionPayloadCarriesAFormatsExecutableHalf` |
| A staged payload carries only what its own `deps.json` names, so a stale file cannot enter it | `PackagedMoviesLayoutTests.NoStagedPayloadCarriesAnAssemblyItsDependencyManifestDoesNotName`, proved to fail on a planted stale assembly by `StagedPayloadDetectorTests` |
| No project stages files by listing another project's build output | `PackageFacetTopologyTests.ProjectStagesNothingByReadingAnotherProjectsBuildOutput` |
| Each package domain type is declared exactly once across the solution | `PackageFacetTopologyTests.EveryPackageDomainTypeIsDeclaredExactlyOnceAcrossTheSolution` |
| The movies domain publishes exactly its three types under `Arronix.Media.Movies` | `PackageFacetTopologyTests.TheMoviesDomainDeclaresItsTypesOnceUnderTheMediaNamespace` |
| The video halves are spelled in two namespaces and share no identity | `PackageFacetTopologyTests.TheVideoPackageSpellsItsTwoHalvesInTwoNamespaces` |
| The shared extension vocabulary cannot be cast back and edited | `VideoFormatFamilyTests.TheSharedExtensionVocabularyCannotBeCastBackToItsArray`, `.TheSharedExtensionVocabularyRefusesMutation` |
| A lock file records exactly the evaluated transitive project closure, both directions | `PackageLockTopologyTests.LockFileRecordsExactlyTheEvaluatedProjectClosure`, guarded by `.TheEvaluatedClosureIsActuallyComputed` |
| Host references neither half of the video package | `FormatCapabilityTopologyTests.HostDoesNotReferenceAFormatCapability` |
| A provider package declares and links only Abstractions plus the media domain it pairs with | `IndependentProviderPairingTests.ProviderPackageDeclaresOnlyContractsAndTheMediaDomainItPairsWith`, `.ProviderAssemblyLinksNoPartOfTheMoviesExtension` |
| `ICataloger<Movie>` and `ICurator<Movie>` close over the media domain's `Movie` | `IndependentProviderPairingTests.ProviderClosesItsCatalogerOverTheMediaDomainsMovie`, `.ProviderClosesItsCuratorOverTheSameMovie` |
| A provider returns a fully shaped `Movie` with its media-owned lifecycle, and owns its marker spelling | `IndependentProviderPairingTests.ProviderReturnsAFullyShapedMovieWithItsMediaOwnedLifecycle`, `.ProviderOwnsItsIdentifierMarkerSpelling` |
| The staged package payload is exactly the declared set, and the reference runs one way inside it | `PackagedMoviesLayoutTests` |

The existing G02 acceptance path is unchanged and still passes: the real staged Movies package reaches
`Active`, publishes `movies`, owns exactly its derived tokens once each, and its admitted item type — now
declared in `Arronix.Media.Movies.dll` — still resolves inside the extension's own `PluginLoadContext`.

---

## 6a. How the package payloads are staged, and why it matters

The payload rules above are only worth as much as the folder they read, and the obvious way to produce that
folder is wrong.

Staging by copying a project's `bin` directory recursively looks like it produces the payload. It does not,
quite: MSBuild does not delete an assembly that a removed `ProjectReference` stopped producing, so the
folder can keep a file the project no longer depends on. A recursive copy carries it, clearing the
destination first does not help — the stale file is in the *source* — and a payload test then faithfully
reports a dependency that does not exist. Nothing fails, which is what makes it expensive.

The staging targets publish instead. Publish computes the runtime closure from the current reference set
rather than listing a directory, so a stale file in `bin` cannot reach the payload. That was verified
directly: with `Arronix.Format.Video.Contributions.dll` planted in both the Movies and Television output
directories, a recursive copy produced a five-assembly Movies payload and publish produced the correct four.

Three rules keep it that way. `ProjectStagesNothingByReadingAnotherProjectsBuildOutput` refuses the glob at
its source. `NoStagedPayloadCarriesAnAssemblyItsDependencyManifestDoesNotName` compares every staged payload
against its own `deps.json` — the same computation, written down — so anything that arrives by any other
route is caught whatever its name. And `StagedPayloadDetectorTests` plants a stale assembly in a copy of the
staged payload and asserts that rule fails, because a rule that has only ever seen a correct payload cannot
be told apart from a rule that does not look.

---

## 7. What this branch does not do

These are the G03 items the split makes expressible and does not itself deliver. Each needs the manifest
or loader work, which lives on other branches.

1. **No manifest dependency declaration.** `plugin.json` still carries `schemaVersion: 0` and the reader
   refuses unmapped members, so adding `dependencies` or a shared-assembly declaration here would break
   loading rather than extend it. `PackagedMoviesLayoutTests` asserts their absence, so the day the schema
   grows, this fixture is one of the places that has to be updated deliberately.
2. **No admitted-contract resolution.** `PluginLoadContext` still unifies only `Arronix.Abstractions`.
   Both shared assemblies load privately per dependant. One CLR identity across packages is not yet true
   and is not claimed.
3. **The movies and television packages still carry the video domain assembly privately.**
   `Arronix.Format.Video.dll` is copied into each package folder by MSBuild and loaded privately. Install
   Television beside Movies as separate packages today and each would get its own `Video` type. The staged
   payloads name that file explicitly for exactly this reason: the list is what changes when the loader
   work lands. What they no longer carry is video's executable half, and that is not waiting on the loader
   — it is a reference that should never have been taken.
4. **Video is still not an installable package.** It has no manifest, and it cannot get one under the
   current schema, which requires `entryAssembly` and `capabilities` — a contract-first package with no
   module has neither. That shape is a manifest-schema decision.
5. **No dependency-aware quarantine, ordering or withdrawal.** Nothing here refuses to release a shared
   assembly while a dependant is active.
6. **No duplicate-copy refusal**, no signing, no content-hash verification, no cycle detection, no version
   ranges.
7. **The Movies test project imports its domain through one `global using`.** The namespace, the assembly
   and the identity are correct — `Movie`, `MovieReleaseStage` and `MovieReleaseTimeline` are declared once,
   in `Arronix.Media.Movies`, with no forwarding type and no compatibility shape. What is stated once rather
   than per file is the import, in `src/Arronix.Plugin.Movies.Tests/GlobalUsings.cs`, because the regression
   sources that name `Movie` are byte-locked by the compatibility ledger. No locked source changed, so no
   ledger transition was performed; `history.case-changed` would require new case identities and replacement
   records rather than a digest edit, and there is no semantic change to record.
8. **`MovieReleaseParser.EditionRegex` is still `public const string`.** C# inlines a `const` into the
   referencing assembly at compile time, so a `const` that ever crossed a package boundary would silently
   pin dependants to the value they compiled against. It does not cross one today — it is read only by the
   Movies tests, built from the same solution — but it should become `static readonly` before anything
   outside the package reads it.

---

## 8. The naming rule both packages follow

**The stable domain surface keeps the plain name. The half whose lifecycle is different carries the
qualifier.**

| Package | Shared domain surface | Isolated executable half |
|---|---|---|
| movies | `Arronix.Media.Movies` | `Arronix.Plugin.Movies` |
| video | `Arronix.Format.Video` | `Arronix.Format.Video.Contributions` |

The rule exists because the names are the only part of this topology a third-party author reads before
making a decision. Someone writing a movie cataloger should land on `Arronix.Media.Movies` because that is
what a movie is; someone composing video into a media kind should land on `Arronix.Format.Video` because
that is what a video release carries. In both cases the assembly with the different update and unload
cadence is the one that had to say something extra about itself — `Plugin`, because it is the loadable
extension, and `Contributions`, because it is what video contributes to a dependant's compiled behavior.

Two earlier spellings were rejected on the same rule. `Arronix.Plugin.Movies.Contracts` named a media-domain
type after the mechanism that delivers it. `Arronix.Format.Video.Contracts` put the qualifier on the stable
surface and left the churning half holding the plain domain name, which points a reader at exactly the wrong
assembly.

The rule reaches the namespaces too. `Movie`, `MovieReleaseStage` and `MovieReleaseTimeline` are declared in
`Arronix.Media.Movies`; video's public domain types are in `Arronix.Format.Video` and its executable-only
types in `Arronix.Format.Video.Contributions`. No type is declared in two places, so a reader can tell which
half they are looking at from the using site, and there is one CLR identity per domain type to observe once
a loader is able to share it.

---

## 9. Sources

- `git show claude/g03-facet-audit:docs/research/g03/contract-executable-facet-audit.md` — the type-by-type
  facet inventory and the topology comparison whose T3 this implements, with one package rather than two.
- `git show claude/g03-clr-identity-spike:docs/research/g03/clr-identity-experiment.md` — the runtime
  measurements behind sections 1 and 4, including the six conflated identities and the negative result
  about `Unload()`.
- `git show claude/g03-manifest-contract:docs/research/g03/manifest-dependency-contract.md` — the worked
  failure analysis in its section 2, and the pre-execution test for what a manifest owns.
- `docs/design/typed-media-roadmap.md` G03, `docs/design/typed-media-north-star.md`, `CONTEXT.md`,
  `INTERFACE.md`, `docs/owner-ledger.md` open question 1.
