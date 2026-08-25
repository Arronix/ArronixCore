# Arronix Architecture

## 1. Boundary rule

The host owns algorithms whose inputs can be expressed generically. Media types own what their items, acquisition targets, and releases mean. Format capabilities own facts about representations. External provider plugins own vendor integration.

```text
Arronix.Abstractions
        ↑
        ├── Arronix.Common ──┐
        ├── Arronix.Plugins ─┼── Arronix.Host ── Arronix.Api
        ├── Arronix.Client   │
        ├── Format capability│
        └── Language capability
                  ↑          │
             Media extension┘
```

Host does not reference a format or media implementation. Client references Abstractions only. A media extension may reference the domain assembly of each format capability it composes, and its own media domain; it references neither another kind's media domain nor a format capability's executable half.

The architecture succeeds when a third party can express a complete media domain through a small set of
ordinary, strongly typed SDK concepts while Arronix derives the common operations, orchestration, runtime
dispatch, and generic presentation. Minimal authoring comes from complete shared primitives and derivation,
not field bags or hidden Host knowledge. Observable *arr and Scene/PTP behaviour remains an acceptance
target even though legacy class graphs and wire APIs are not. No feature is complete until its typed meaning
is used by the relevant production execution and consumer path.

## 2. Typed media model

`MediaType<TItem,TTarget,TRelease,TParser>` is the abstract authoring boundary:

- `TItem : IMediaItem` is durable shaped catalog and library data.
- `TTarget : IReleaseTarget` is what acquisition is trying to satisfy.
- `TRelease : IRelease` is what a raw listing was interpreted to publish.
- `TParser : IReleaseParser<TRelease>` is the media-owned parser for that exact release shape.

The definition is an ordinary instance with virtual or abstract members; there are no static-abstract media
type declarations. Parsing is the deliberate static generic seam: the parser type implements
`IReleaseParser<TRelease>.Parse`. The public surface has no non-generic shadow model. `IMediaTypeRegistration` uses double
dispatch to carry a captured closed generic definition through the kind-blind loader. Host binds it to an
internal `IMediaTypeRuntime` only after admission.

The four-arity base is the current typed media-definition surface. It exposes ordinary typed override
values for files, format use, identity, groups, selections, semantic searches, matching, release policy,
querying, naming, summaries, intent exceptions, workbenches, and derivations. Minimum availability is an
explicit typed value; other selection dimensions are additional values. Platform actions are derived by Host
from this shape rather than declared per media type. Optional and repeatable relationships use empty or
populated typed collections; there is no public whole-media replay builder or `IUses...` capability badge
vocabulary.

The C# type definitions are the schema. `IMediaEntity` supplies the minimum compiled floor;
`MediaItem<TReleaseTimeline,TReleaseStage>`, its exact-item three-arity form, and
`MediaCollection<TItem>` are concrete reusable schema classes, not opaque host models. A media extension
may publish an empty nominal closure such as `Movie` to give typed provider pairs and relationships a
stable domain name without repeating the common properties. Closed generic definition values describe
cross-type relationships and policies.

`Arronix.Generators` reads those definitions during compilation and emits closed getters for item, group,
and workbench-row fields. Host validates the generated projection and erases it into descriptors for
discovery and generic presentation. It does not inspect plugin properties or attributes through runtime
reflection. The generator is an analyzer-only dependency and never enters the plugin's runtime reference
graph.

That rule also applies to acquisition values: `ReleaseTarget<TItem>` directly models a request for one
item, and `Release<TRepresentation>` directly carries the common title, year, edition, and representation.
Television keeps specialized types because a release can cover a set of episode coordinates; a media kind
name alone is never a reason to introduce another wrapper.

Semantic names do not come from reusable CLR implementation names. A level and its naming-token word use
the media type's declared singular name. A grouping axis uses the declared singular group name, while its
membership field remains the ordinary item property. This allows plural `Collections` membership and the
stable `collection` axis without leaking `MediaCollection` into public identifiers.

## 3. Acquisition model

```text
IIndexer
   ↓
ReleaseListing (raw provider statement)
   + cataloger-owned ExternalIdReading values
   ↓ TParser : IReleaseParser<TRelease>
TRelease
   + TTarget
   ↓ typed matcher
TargetMatch<TTarget>
   ↓ ReleasePolicy<TRelease>
ReleaseOption<TTarget,TRelease>
   ↓ deterministic ReleaseSelector
selected acquisition
```

`TargetMatch` retains Covered and Missing target portions and distinguishes Satisfied, Partial, Superset, and Rejected. Set-shaped targets are necessary for episodic media; Television is the pressure test.

The selector applies hard requirements, lexicographic core preferences, bounded facets, acquisition preference, then stable release identity. Provider enumeration order is never a tie-break.

The typed parser is production-bound for converted media and Host maintains a temporary `ParsedRelease`
adapter for unconverted consumers. Format-owned recognizer composition and the end-to-end acquisition
wiring are still in migration.

## 4. Format capabilities and Video

Video is not a universal abstraction. The video package owns it, and ships as two assemblies with two
release cadences.

`Arronix.Format.Video` is the shared domain surface — video's owner semantics, and everything a media
declaration composes:

- `Video` and `VideoLineage`;
- resolution, revision, video stream, every audio track, and defects;
- open codec and dynamic-range identifiers;
- the Video format identity and its file-extension vocabulary;
- video-owned default release-policy contributions.

`Arronix.Format.Video.Contributions` is the isolated half — video's executable work, which grows as
recognition work lands and which no media extension references:

- common release-token interpretations, and the recognizers and probing that follow them.

Video lineage records only observable facts from a public channel artifact:

```text
ReleaseChannel → SourceCarrier → AcquisitionMethod → TranscodeHistory
```

A Blu-ray Remux is a direct copy of streams from an optical disc. A WEB-DL is a direct copy of the service-hosted stream artifact. A BDRip or WEBRip establishes at least one additional transformation. None of those statements invents an upstream studio-master lineage.

Unknown remains explicit. Multi-valued facts such as audio tracks and dynamic-range presentations remain collections until policy interprets them.

## 5. Language capabilities

A media type states the language of its titles and localized payloads. It does not own the linguistic
rules for those languages. `ILanguageDefinition` implementations are registered by type under the
`language` capability and activated through the same constrained plugin constructors as providers. They own comparison preparation, provider-query spelling,
file-name spelling, and alphabetical sort spelling. The reference language extension currently supplies English, German, and
French implementations.

Host composes those operations with invariant Unicode key mechanics. Each language's comparison key is
namespaced by its BCP 47 code, so loading one language cannot silently change another language's
equivalence relation. Text whose language is not stated receives invariant mechanics only; it is never
silently treated as English.

## 6. Policy and provenance

One `ReleasePolicy<TRelease>` is compiled from fragments contributed by the owner of each fact. A format capability may contribute defaults for its representation. A media type may contribute edition or coverage preferences. User configuration is the final authority.

Core preferences are lexicographic. Facets are deliberately secondary and bounded to five contributions of plus or minus ten points each. This keeps a large pile of preferences from silently overturning the core order.

Provenance is retained as an `InterpretationTrace<TSubject>` sidecar. It explains whether a property was claimed, measured, or inferred and by whom. It does not impose a universal trust order, and ordinary properties are not wrapped in `Evidence<T>` by default.

## 7. Provider boundary and activation

Provider families are platform-owned dispatch seams:

- `IIndexer`
- `IDownloader`
- `INotifier`
- `ICataloger<TItem>`
- `ICurator<TItem>`

Cataloger and Curator are generic because both are paired with one media-owned item type. A cataloger returns
authoritative `TItem` values. A curator proposes catalog references for that item type, and the cataloger
owning each reference supplies the item. Neither returns a universal field dictionary.

Registration records implementation types. After compatibility and capability admission, Host activates them
through an exact public `(IPluginContext)` constructor, or a public parameterless constructor when no context is
needed. Host DI is never consulted and modules do not construct provider objects.

## 8. Plugin lifecycle

The loader:

1. discovers and parses `plugin.json`;
2. validates the `0.9` contract range and static assembly references;
3. creates an isolated load context and capability-scoped context;
4. invokes the single module's registration method;
5. seals and verifies the registration ledger in both capability directions;
6. asks Host to prepare complete attempt-local kind, provider, language, job, and health candidates and takes
   back their authoritative admitted inventory without publishing them;
7. checks declaration agreement, naming-token ownership and installation-wide identity against that
   inventory rather than against the declaration file;
8. under one shared publication gate, commits the per-kind token plan, exact Host attempt, and Active runtime
   result as one change, or quarantines and completely releases the attempt.

The admitted inventory is keyed per `MediaKindId` and carries each admitted kind's own derived naming
tokens. A media extension therefore does not restate its kinds, tokens, policies, or actions in its manifest:
those are compiled from its types and settled against what Host admitted. Nor does it claim external
identifier vocabularies: the media type owns the roles, while installed catalogers own the schemes and
release markers which fill them. The current manifest keeps what cannot be learned from code the loader has
not yet allowed to run — package identity, contract compatibility, the entry assembly, and requested
capabilities. Per-extension network and filesystem access grants are operator configuration; package
dependencies are G03 work.

Shutdown is the inverse transaction. The bootstrapper explicitly stops and drains the scheduler before
teardown, including when hosted services stop concurrently; an exact runtime-to-Host receipt atomically
withdraws Host registrations, token claims, and Active state, leaving a
reference-free `Stopped` result. Owned instances are then disposed outside the publication gate in
async-preferred reverse order, with the module and collectible load context last. A genuinely overrun job
retains the plugin's Active roots rather than allowing executing code to be unloaded.

One installable package may ship zero or more shared contract assemblies and zero or one isolated
executable entry assembly, and one dependency names a package id with a compatible range. A shared contract
assembly carries typed owner semantics and pure deterministic behavior only: no `IPluginModule`, no parser
or registration execution, no provider implementation, no I/O, no mutable process state, no module
initializer. Its intended lifetime is one copy per installation in a Host-owned collectible contract
context, released once every dependant has withdrawn.

The movies and video packages are split that way today: `Arronix.Media.Movies` and `Arronix.Format.Video`
hold the shared types, and `Arronix.Plugin.Movies` and `Arronix.Format.Video.Contributions` hold the
executable halves. `Arronix.Format.Video` ships as the installed package `arronix.format.video`, Movies
publishes its media domain and requires the video package, and the installation admits each declared contract
once into one Host-owned collectible context — so a separately packaged cataloger observes one CLR identity
for `Movie`, and two independently installed dependants observe one for `Video`. A package binds only to
contracts published by itself or by a package in its exact transitive dependency closure.

The remaining half is the browser: loading those exact types into the Blazor client, with the package
identity, content hash, dependency-closure hash and cache protocol that requires, is G07's work and is not
claimed here.

## 9. Migration state

Movies is on the typed media path, loads as a real package through the complete loader, and registers the
empty nominal closure `Movie : MediaItem<Movie,MovieReleaseTimeline,MovieReleaseStage>`. The complete reusable item shape lives
in the base; the nominal type exists so typed movie catalogers, curators, targets, collections, and future
movie-specific additions all close over the same public domain identity.
Television has typed release/target models and coverage tests but still uses its legacy production shape
and engines. Books and Music remain legacy. The legacy `IQualityModel`, ladder DTOs, and untyped
parse-quality string remain until those three conversions complete.

This is deliberate compatibility scaffolding, not the target architecture. New work must use the typed media/release and format-owned representation model. Do not add features to the legacy quality axis.

## 10. Verification and governance

`Arronix.Architecture.Tests` enforces project directions, capability coverage, pre-release versioning,
topology, vocabulary, and media neutrality. The complete solution build and test run are the acceptance
gate. Skips must be reported because Movies compatibility scenarios account for most of them. Parser
regression cases live in test projects and are not carried by production media definitions.

`eng/ci/run-tests.sh` is that gate as one command, run locally and in repository CI: locked restore, one
Release warnings-as-errors solution build whose binlog is retained as compiler-input evidence, a non-empty
NUnit result from every discovered test project, exact NUnit-leaf/method/assembly/PDB/source binding, the
exact 302-skip ratchet, and current-plus-prior compatibility validation. Local green results now are
continuous enforcement; what they do not establish is cryptographic or hermetic build attestation.
