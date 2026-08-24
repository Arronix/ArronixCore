# Arronix History

## 2026-08-24 — Stage package payloads from the computed closure rather than from a build directory

- Replaced the payload staging source. The Movies, Television and G02 fixture packages were staged by
  copying their projects' `bin` directories recursively, which is not the same thing as producing a payload:
  MSBuild does not delete an assembly that a removed `ProjectReference` stopped producing, so the source
  directory can hold a file the project no longer depends on, a recursive copy carries it, and clearing the
  destination first does not help because the stale file is on the other side. The three targets publish
  into their cleared directories now, so a payload is the runtime closure computed from the current
  reference set rather than a listing of whatever a directory still contains.
- Verified the difference directly rather than reasoning about it. With
  `Arronix.Format.Video.Contributions.dll` planted in both the Movies and Television output directories, the
  recursive copy produced a five-assembly Movies payload and publish produced the correct four.
- Added the regression the payload rules were missing. Every staged payload is compared against its own
  `deps.json` — the same computation the publish performs, written down — so an assembly that arrived by any
  other route is caught whatever its name, including after a future change back to a recursive copy. A
  detector fixture plants a stale assembly in a copy of the staged payload and asserts that rule fails,
  because a rule that has only seen correct payloads cannot be told apart from a rule that does not look.
- Added a rule refusing the cause as well as the symptom: no project may stage files by listing another
  project's build output directory.
- Replaced the two one-directional lock-file rules with one that compares each lock file against the
  evaluated transitive project-reference closure in both directions, guarded by a case proving that closure
  is actually computed. A name the graph no longer reaches and a project the lock file does not record are
  different mistakes, and `--locked-mode` reports neither.
- Each new rule was checked against the defect it describes: reintroducing the `bin` glob, adding an
  unreachable project to a lock file, and removing a recorded one each fail exactly one rule.
- The exact .NET 11 proof rail finished with 2,289 passed, 302 registered skips, zero failed, and zero
  inconclusive from 2,591 cases across all 11 test projects; the ledger is unchanged at 302 cases and zero
  replacements, and compatibility and required-sentinel ratchets pass.

## 2026-08-24 — Close the media/format dependency boundary and correct the namespace record

- Moved video's owner semantics into the shared domain assembly. `VideoFormat.Definition` and
  `VideoReleasePolicyDefaults` are what a media declaration composes — the family it names in its
  constructor and the preferences it folds into its own compiled policy — and both are deterministic
  in-memory logic over values, so both now live in `Arronix.Format.Video`.
  `Arronix.Format.Video.Contributions` keeps video's executable work: the release-term recognition
  vocabulary today, and the recognizers, probing and registration that follow.
- Removed the reference from `Arronix.Plugin.Movies` and `Arronix.Plugin.Tv` to
  `Arronix.Format.Video.Contributions`. Neither needs it now, and taking it copied an independently
  updatable assembly into each package's payload for nothing. Neither payload carries it any more.
- Spelled the video halves in two namespaces. Public domain types are `Arronix.Format.Video`;
  executable-only types are `Arronix.Format.Video.Contributions`. No type is declared in both, so the
  boundary is legible at the using site and not only in the reference list.
- Made the shared extension vocabulary read-only at the boundary. `VideoFormat.Definition` is one canonical
  object handed to every video media type, and its `FileExtensions` was a collection expression behind an
  `IReadOnlyList<string>` — an array any caller could cast back and edit, mutating a vocabulary every
  dependant reads. It is a `ReadOnlyCollection<string>` now, with cases asserting the wrapper rather than
  the interface.
- Added the rules that make the boundary hard to regress: a media extension declares no reference to a
  format's executable half, links none in its compiled reference table, and — asserted against the staged
  Movies and Television payloads, which the build clears and re-copies — ships none in its package output.
- Added rules for one canonical identity: every package domain type is declared exactly once across the
  solution, read from built metadata rather than the loaded set, and the movies domain publishes exactly
  `Movie`, `MovieReleaseStage`, and `MovieReleaseTimeline` under `Arronix.Media.Movies`.
- Repaired the checked-in lock files. Splitting the video package left `arronix.format.video.contracts` in
  `src/Arronix.Format.Video/packages.lock.json` after the project was gone, and a `--locked-mode` restore
  reported success anyway: that validation covers packages, not the project entries a lock file records.
  The lock files were regenerated by re-evaluating the graph, and architecture rules now check that a lock
  file names only projects that exist and records every project reference its project file declares.
- Corrected the record on the movies namespace. The previous commit did rename it to `Arronix.Media.Movies`;
  the entry above it said otherwise, and that was wrong. There is one declaration, no forwarding type, and
  no compatibility shape. No compatibility-ledger transition was performed, because no locked public source
  changed.
- The exact .NET 11 proof rail finished with 2,282 passed, 302 registered skips, zero failed, and zero
  inconclusive from 2,584 cases across all 11 test projects; the ledger is unchanged at 302 cases and zero
  replacements, and compatibility and required-sentinel ratchets pass.

## 2026-08-24 — Split the movies and video packages into shared contract and isolated executable assemblies

- Adopted one package with two kinds of assembly. A package may ship zero or more shared contract assemblies
  and zero or one isolated executable entry assembly, and one dependency names a package id with a
  compatible range rather than separate contract and plugin edges or an arbitrary facet name. A shared
  contract assembly carries typed owner semantics and pure deterministic behavior only; its intended
  lifetime is one copy per installation in a Host-owned collectible contract context, released once every
  dependant has withdrawn.
- Split the movies package. `Arronix.Media.Movies` now declares `Movie`, `MovieReleaseStage`, and
  `MovieReleaseTimeline`; `Arronix.Plugin.Movies` keeps the `Movies` definition, `MovieReleaseParser`, the
  plugin module, and — because the generator emits into the assembly declaring the `MediaType` subclass —
  the generated `CompiledShapes` catalog and its reader delegates. The generator needed no change.
- Named the shared half for the domain that owns it. `Movie` is a media-domain type, not a plugin
  implementation contract; a cataloger pairs with it without caring that an extension exists, so the
  assembly is `Arronix.Media.Movies` rather than a `.Contracts` suffix on the extension's name. The file
  holding the lifecycle was renamed `MovieReleaseLifecycle.cs`, since it declares the typed timeline and its
  stage vocabulary rather than parser vocabulary.
- Split the video package the same way round. `Arronix.Format.Video` keeps its name and becomes the shared
  domain surface — the representation and quality facts a `Release<Video>` carries — because that is the
  assembly a third-party media or provider author should reach for. The new
  `Arronix.Format.Video.Contributions` takes `VideoReleaseVocabulary`, `VideoReleasePolicyDefaults`, and
  `VideoFormat.Definition`: recognition vocabulary, policy contribution, and the file-extension vocabulary,
  all of which grow as recognition work lands and none of which anything outside a media definition
  consumes. Both assemblies keep the `Arronix.Format.Video` namespace, because the format capability owns
  one vocabulary and which assembly a type ships in is a lifetime decision rather than a second vocabulary.
  Video remains one package.
- Chose the placement rule deliberately: deterministic value semantics belong with their owner, execution
  with a lifecycle does not. `MovieReleaseTimeline.StageOn` and `VideoResolution.CompareTo` are media- and
  format-owned meaning and stay in the shared half; hoisting them into Host to keep those assemblies
  data-only would move domain semantics out of the domain that owns them.
- Added `Arronix.Architecture.Tests.MovieCatalogerFixture`, which stands in for a separately shipped vendor
  package. It implements `ICataloger<Movie>` and `ICurator<Movie>`, declares and links exactly
  `Arronix.Abstractions` and `Arronix.Media.Movies`, returns a fully shaped `Movie` with its media-owned
  lifecycle, and owns its identifier marker spelling. Before the split no such assembly could exist:
  referencing `Movie` meant referencing the module and parser too.
- Added structural and metadata rules rather than leaving the boundary to review. A shared contract assembly
  may not declare `IPluginModule`, `IProvider`, `IReleaseParser<>`, `IMediaTypeDefinition`, or a
  `MediaType<,,,>` subclass; may not hold a writable static or a static delegate; may not run anything when
  loaded; may not spell a regular expression or an I/O call; and may not reference Host, the loader, the
  generator, a format executable assembly, or a media extension. The isolated assemblies are checked for
  still holding the module, definition, parser, vocabulary, family definition, and policy defaults, and the
  staged movies package payload is asserted against a written-down file list.
- Renamed the type namespace with the assembly: `Movie`, `MovieReleaseStage`, and `MovieReleaseTimeline`
  are declared in `Arronix.Media.Movies`, with no forwarding type and no compatibility shape keeping the old
  spelling alive. The Movies regression sources that name `Movie` are byte-locked by the compatibility
  ledger, so the test project states the import once in its `GlobalUsings.cs` rather than per file; no
  locked source changed and no ledger transition was needed. (An earlier draft of this entry said the
  namespace had been left behind. It had not; the correction is recorded in the 2026-08-24 entry above.)
- This is compile-time and package shape only, and claims nothing about runtime identity. The manifest still
  declares no package dependency, `PluginLoadContext` still unifies only `Arronix.Abstractions`, both shared
  assemblies still load privately per dependant, and the movies package still carries a private copy of the
  video package. G03 remains open: admitted-contract resolution, duplicate-copy refusal, version ranges,
  ordering, and dependency-aware withdrawal are not here.
- The exact .NET 11 proof rail finished with 2,206 passed, 302 registered skips, zero failed, and zero
  inconclusive from 2,508 cases across all 11 test projects; compatibility and required-sentinel ratchets
  pass.

## 2026-08-24 — Make admission transactional, remove Movies' second schema, and close G02

- Replaced provisional Host publication with attempt-scoped preparation. Host derives and activates complete
  candidates but exposes none of them; a successful `PluginAdmissionResult` carries the exact
  `IPluginAdmissionAttempt` which owns its inventory, commit, rollback, and Host-created instances.
- Made Host admission mandatory for every loader entry point. Provider and language implementation types are
  constructed only through an exact public `(IPluginContext)` or parameterless constructor; plugin activation
  never resolves from Host DI.
- Added one shared Host-owned publication gate across token, Host, and runtime registries. After declaration agreement,
  token planning, and identity checks all pass, the loader commits the token plan, exact Host attempt, and
  `Active` result as one change. Every exceptional or refused path gives back only that attempt, and a failed
  reload cannot overwrite the existing Active authority.
- Made stop the inverse exact transaction. The bootstrapper owns scheduler cancellation and drain even when
  hosted services stop concurrently;
  Host and token registrations are withdrawn with the matching runtime receipt; a stopped result retains no
  ledger, runtime lease, or load-context root; and extension-created values dispose once by reference in
  reverse registration order, with async disposal preferred, the module last, and the collectible context
  unloaded last. A genuine job overrun deliberately retains the Active roots.
- Added a separately packaged G02 fixture with a real media kind, notifier, language, scheduled job, health
  contributor, naming tokens, self-registered module, and disposal telemetry. It proves complete publication,
  runtime-envelope propagation, late-failure rollback, exact async cleanup order, ordinary scheduler drain,
  overrun deferral, stopped-root removal, and containment of a throwing load-context unloading handler before
  another package is loaded.
- Moved the naming-token equality operation to `Arronix.Common.Naming` rather than widening the plugin SDK.
  Its Rune-based canonical form is now shared by parsing, declaration agreement, reserved-name checks,
  ownership, and rendering, including supplementary-plane letters.
- The exact .NET 11 proof rail finished with 2,168 passed, 302 registered skips, zero failed, and zero
  inconclusive from 2,470 cases across all 11 test projects; compatibility and required-sentinel ratchets pass.

- Removed `mediaKinds`, `identifiers`, `tokens`, and `policies` from `src/Arronix.Plugin.Movies/plugin.json`.
  Kinds, tokens, and policy are compiled from the media definition; external identifier schemes and their
  release markers belong to installed catalogers, while Movies owns only the roles those identifiers can
  fill. None is manifest authority. The manifest now carries only what the loader cannot learn from admitted
  code: manifest schema version, package identity, operator-facing name, version, description, the Arronix
  contract range, the entry assembly, and explicit capability grants.
- Kept requested capabilities in the manifest deliberately. Least privilege is a statement made before the
  extension's code runs, so it cannot be derived from that code without granting the privilege in order to
  discover whether it was wanted. Per-extension host and filesystem grants remain operator configuration;
  they are not manifest fields.
- Replaced the manifest mirror fixture. It existed to keep a hand-maintained copy honest, and its own
  documentation said so; with the copy gone the fixture asserts that no media-derived kind, token, policy,
  or action list and no cataloger-owned identifier vocabulary appears in the manifest, and that the manifest
  carries nothing outside what it owns. `actions` has never been a manifest key and is now pinned so it
  cannot quietly become one. Derived-vocabulary assertions remain against the media type itself.
- Left the Books, Music, and Television manifests alone. Their declarations belong to the legacy media paths
  those kinds still use and are removed with their conversions.
- Corrected the stale statement that no CI workflow exists. G01 added the proof rail.

This closes G02. The real packaged Movies extension survives discovery, typed preparation, late agreement,
atomic publication, and exact withdrawal, and its declaration is no longer a second media definition.
G03 is now active: package dependency and version rules, and one exact CLR type identity across host and
dynamically loaded client code.

## 2026-08-24 — Make the admitted projection, not the manifest, the authority after admission

- Changed `IPluginAdmissionCheck` from a verdict to `PluginAdmissionResult`, carrying an `AdmittedInventory`
  of one entry per `MediaKindId` Host actually registered together with that kind's own derived
  `NamingToken` collection. The entries are read back off each admitted `RegisteredMediaKind`, so the
  inventory is the projection the platform will serve rather than a second derivation of it.
- Made the loader's post-admission steps consume that inventory. Declaration agreement, cross-installation
  duplicate-kind checking, and naming-token ownership no longer read a manifest for facts derived from an
  extension's own types, and scheduled-job kind association uses the admitted kind when there is one. This
  repairs the defect where a typed Movies registration was bound and published, and then quarantined by a
  token check that could only see the legacy `IMediaShapeProvider` seam.
- Kept the legacy shape-provider path as the transitional answer for a loader running without a host
  admission check. After Host admission the inventory is the authority.
- Made naming-token ownership per kind through `TokenClaimRequest`. The previous signature took a set of
  kinds and a set of tokens, which could only be turned into claims by taking their cross product; an
  extension supplying two kinds does not own each kind's vocabulary for the other.
- Made a manifest able to omit derivable media facts. Holding the `media-kind` capability without restating
  the kinds is now valid, because which kinds an extension supplies is settled against what was admitted. A
  manifest that does state kinds or tokens is still held to them exactly, in both directions. Capabilities
  remain explicit manifest-owned least-privilege requests, because a privilege cannot be derived from code
  that has not been allowed to run.
- Made post-admission withdrawal complete. The loader releases token claims on any quarantine after the
  claim rather than at one hand-picked step, and Host withdrawal — used for both a late loader failure and
  `StopAsync` — now gives back token ownership alongside kinds, providers, languages, jobs, and health
  contributors.
- Changed the real packaged Movies proof from recording the quarantine to proving `Active`: the kind is
  published, its derived tokens are owned once each by that kind, the admitted item type resolves inside the
  extension's own `PluginLoadContext` rather than from an in-process binder call, a planted conflicting token
  claim quarantines the package and leaves nothing behind, a restaged manifest declaring a kind the
  projection does not supply is refused, and stopping releases both the kind and its tokens.
- Narrowed the reference-inspector assertion to the default load context. Counting every context made it
  measure whether the runtime had collected other fixtures' collectible plugin contexts — one of which loads
  the contract assembly as an extension's own entry assembly — rather than whether inspection loads anything.

G02 remains open. The runtime no longer reads Movies' hand-maintained `mediaKinds`, `identifiers`, `tokens`,
and `policies`, but the manifest still carries them, so the second schema this gate exists to remove is still
in the repository.

## 2026-08-21 — Establish the G01 proof rails and adopt .NET 11 Preview 7

- Advanced every runtime, SDK, plugin, application, and test project to `net11.0`, pinned exact Preview 7 SDK
  `11.0.100-preview.7.26381.103`, checked in package lock files, and reduced the central package graph to dependencies with real consumers.
  The generator remains `netstandard2.0` because it is an analyzer, not a runtime compatibility surface.
- Added one local and hosted-CI rail for locked restore, one Release warnings-as-errors solution build, discovery
  of every test project, a required non-empty result from each, result artifacts, append-only critical sentinels,
  and explicit compatibility accounting against the PR base or pre-push ledger. The same-build solution binlog
  is retained as the practical authoritative record of actual `Csc` sources, embedded inputs, warning policy,
  and configuration.
- Made the compatibility ledger canonical: 302 known skips map to 129 semantic requirements and 12 provenance
  sources. Explicit replacement edges and executable proof are required before an omission can disappear.
- Added mutation-tested ratchet and generator suites. Published ledger semantics and test sources are immutable
  across revisions; renamed, removed, newly skipped, weakly rebound, malformed, coordinated source-and-ledger
  weakening, or same-change replacement evidence now fails rather than silently improving a count. Removed the
  forgeable design in which a test could certify itself by printing a semantic digest supplied by the ledger.
- Made the live skip count an exact committed ratchet: every observed reduction must be recorded immediately,
  after which comparison with the prior ledger rejects any regression.
- Bound every registered execution and critical sentinel from an exact NUnit leaf to the declared CLR method in
  NUnit's exact executed assembly and that assembly's matching Portable PDB. Method sequence points, async and
  iterator state machines, source paths, checksums, and exact embedded source bytes for primary and separately
  declared support documents now fail closed. Together with the same-build binlog this is practical layered
  provenance, not a claim of cryptographic identity or hermetic compiler causality.
- Published an eight-column, append-only required-test registry containing only three durable invariants: the
  Abstractions dependency boundary, executable generated shape, and rejection of coordinated ratchet weakening.
  Transient outcomes such as the currently quarantined Movies loader path remain ordinary tests so their repair
  does not contradict a permanent sentinel.
- Isolated the four published Movie-title compatibility inputs (`t012`, `t013`, `t023`, and `t027`) from the
  ordinary representative title corpus. Their dedicated support document can remain immutable while normal
  parser coverage evolves.
- Separated replacement topology (`one-to-one` or `partition`) from semantic outcome. Equivalent, ownership
  correction, recovered evidence, approved divergence, and scope correction now have disposition-, requirement-,
  semantic-, provenance-, and history-specific rules. Owner decisions are pinned sources, and replacement-witness
  is an introduction role so a permanent witness can itself be replaced later. Verified acyclic one-to-one
  chains close to a fixed point; recovered witnesses can keep their evidence-gap ownership, while a pinned owner
  decision attached to the requirement may retain rather than alter a scope-correction candidate's locked
  semantics. Partition records remain non-closing until aggregate composition can be represented rather than
  inferred from unrelated opaque digests.
- Added a real packaged Movies integration proof. It deliberately records the current late token-agreement
  quarantine and atomic typed-kind withdrawal, leaving repair of that production path as G02 rather than
  granting direct binder tests false completion status.
- Closed G01 at 2,112 passed, 302 registered skips, zero failed, and zero inconclusive across 11 test projects.

This establishes proof discipline rather than feature parity. A registered omission remains missing
capability, and the packaged Movies extension remains quarantined until G02 repairs its typed admission path.

## 2026-08-21 — Adopt a gated typed-media execution roadmap

- Recorded the `018e2b0d1` typed Movies checkpoint as R00 and replaced the flat outstanding-action list with a
  dependency-ordered roadmap whose gates are taken one at a time and closed only by production-path evidence.
- Put continuous clean-clone and compatibility proof before the next mutation, then real package admission and
  CLR type identity before provider, parser, or Client claims. A separately shipped provider is not viable while
  it can load a second `Movie` type or the Movies package is quarantined by its own loader.
- Allowed one narrow durable Movies catalog slice to expose materialization and identity faults early, but
  required typed interpretation, matching, and policy to pass through Television, Music, and Books before the
  cross-media relational shape and semantic defaults freeze.
- Ordered durable catalog state, acquisition, transfer, probing/import, actions, workbenches, API, and Client as
  complete packaged Movies, Television, Music, and Books verticals before legacy removal or
  replacement-readiness claims.
- Made CI skip accounting, the compatibility matrix, independent SDK consumption, production provider packages,
  and operational recovery explicit gates rather than untracked follow-up work.

This decision changes execution and acceptance discipline, not the current public interface. The typed-media
north star remains the destination; subsystem design documents remain research inputs which must be revalidated
instead of being executed when they conflict with current context, owner intent, or the interface contract.

## 2026-08-21 — Make plugin authoring and vertical coherence governing acceptance criteria

- Adopted the third-party authoring experience as a product contract: an extension author should be able to own a complete Sonarr-, Radarr-, Lidarr-, Readarr-, or new-media-style domain through a few intuitive, strongly typed abstractions while Arronix derives common application machinery.
- Defined minimal surface area as the result of complete common types, generic relationships, constructor-owned invariants, typed defaults, and generated projections. Rejected field bags, reflection conventions, builder transcripts, repeated type juggling, and Host callbacks as false reductions in complexity which merely transfer it to extension authors.
- Required every claimed abstraction to remain coherent through authoring, registration, Host-owned activation, runtime execution, persistence where relevant, wire and Client projection, tests, and documentation. Types, descriptors, tests, or registration seams alone are not completion evidence.
- Retained full *arr feature coverage and observable ecosystem compatibility as acceptance constraints. Consolidation may remove accidental coupling, but it may not simplify away hard-earned product behaviour without an explicit, tested divergence.
- Made durable owner signal the input to bounded feature work. Fresh tasks consume the current context, interface, north star, history, and owner ledger; questions, hypotheses, and illustrative models do not silently become approved architecture.

This rejects feature-local completion as the governing unit of design. A short-term adapter may be useful during an explicit migration, but it cannot weaken a shared invariant, become a second authoring model, or justify calling a partial vertical slice complete.

## 2026-08-21 — Move invariant media definition values into the base constructor

- Made `MediaType<TItem,TTarget,TRelease,TParser>` take the stable kind identifier, singular name, plural name, non-empty format composition, typed minimum-availability selection, and file binding through its primary constructor.
- Made those required values non-overridable base properties. File binding now defaults to the common one-file-per-item relationship, while a media type with different cardinality supplies it explicitly. Empty format composition now fails at construction instead of surviving until Host admission rejects it.
- Reduced Movies and the typed test definitions to constructor arguments plus genuinely kind-specific declarations. Kind identifiers remain explicit durable values rather than being derived from lower-cased display text.

## 2026-08-21 — Remove the per-type experimental contract mechanism

- Removed every use of `System.Diagnostics.CodeAnalysis.ExperimentalAttribute` and deleted the `ExperimentalContracts` diagnostic-id registry.
- Removed the reflection-based contract-marking tests, the typed-media marking assertion, all file-scoped `ARX` suppressions, and the generated-code suppressions.
- Removed the loader's experimental minor-range-width gate. Plugin compatibility is now the ordinary question of whether the installed contract version satisfies the plugin's declared range.
- Retained the single honest stability rule: the complete public surface is pre-1.0 and may change in a minor release. No per-type pseudo-stability tier remains or is planned.

## 2026-08-21 — Make the common item complete and generate its host projection

- Hoisted typed release stage, catalog presence, and plural `MediaCollection<TItem>` membership into the common item contract and implementation. `IReleaseTimeline<TReleaseStage>` now makes the lifecycle-to-stage relationship explicit and compiled.
- Split the reusable item into a directly usable two-arity form and an exact-item three-arity form. Restored `Movie` as an intentionally empty nominal closure so catalogers, curators, targets, collections, and future movie extensions retain one stable public domain type without duplicating fields.
- Added the analyzer-only `Arronix.Generators` project. It emits closed getters and field projections for each typed media definition's item, related groups, and workbench rows while the plugin compiles.
- Removed Host's property/attribute reflection schema factory. Host now validates and consumes the generated projection, and typed declaration expressions are reduced to compiler-checked paths or direct member names rather than retained `PropertyInfo` values.
- Preserved the established naming-token order explicitly after moving Movie's former local fields into the common base.

This supersedes the intermediate direct-use rule which treated an empty nominal media item as inherently redundant. Nominal identity and duplicated shape are separate concerns: an empty `Movie` is useful; repeated movie properties are not.

## 2026-08-20 — Derive platform actions instead of making media types restate them

- Removed `MediaType.Actions`, `ActionDefinition<TItem>`, and the action-parameter authoring mini-DSL. Search, refresh, rescan, monitoring, availability, rename, add, remove, exclusion, and group operations are platform behavior, not optional Movies configuration.
- Made minimum availability an explicit `MediaType.Availability` value and retained only `AdditionalSelections` for media-specific profile dimensions. Host uses the typed availability declaration when deriving standard action parameters.
- Added `StandardMediaAction` as the semantic identity of a platform operation and retained string action/parameter identifiers only as stable wire spellings. Client affordances now bind through that enum instead of private string conventions.
- Replaced the mismatched private Client and API action bodies with one shared typed `ActionRequest` carrying `MediaItemRef` subjects.
- Added the host standard-action dispatcher and connected `SetMonitoring` to `IMediaStore`. Other standard actions fail explicitly until their scheduler, catalog, filesystem, removal, or exclusion capability exists.

This removes another declaration transcript from Movies while preserving a derived, media-neutral intent surface for API and client consumers.

## 2026-08-20 — Bind typed parsers and return identifier syntax to catalogers

- Extended the media definition to `MediaType<TItem,TTarget,TRelease,TParser>` and introduced `IReleaseParser<TRelease>` with a static `Parse` contract. The parser's C# type is now the executable declaration and returns the exact media-owned release type; typed media no longer manufacture a `ParseDeclaration` builder graph.
- Replaced Movies' parse-declaration factory with `MovieReleaseParser`, an ordinary typed parser using direct C# control flow. Host invokes it through the typed runtime bridge and projects to legacy `ParsedRelease` only for unconverted consumers.
- Added the non-generic `ICataloger.ReadExternalIds` composition floor. Cataloger implementations now own the spelling and recognition of identifiers from their external namespace; Host validates and supplies those readings to the media parser.
- Removed TMDB/IMDb marker knowledge from Movies. A future TMDB or IMDb cataloger package must contribute those recognizers with its typed `ICataloger<Movie>` implementation.

This narrowly supersedes the earlier decision that every part of media authoring would be an instance override: the media definition remains an ordinary object, while parsing is deliberately bound by a static generic contract so the compiler can generate and invoke the exact shaped parser without a C#-implemented sub-DSL.

## 2026-08-20 — Move parser regression cases out of the production contract

- Removed `CorpusCase` and the `Corpus` members from `MediaType`, `MediaKindModel`, and Host's typed compilation path. The data had no runtime consumer and admission performed no parser parity execution.
- Moved the representative Movie title cases into `Arronix.Plugin.Movies.Tests`, where NUnit now feeds them directly to the real parser.
- Connected the media definition's `Respace` function to Host title projection after the real parser cases exposed that the function was carried but never executed.
- Rewrote the Movies extension XML documentation as concise consumer guidance and removed historical implementation narrative from the production objects.

## 2026-08-20 — Complete the typed override-value media definition

- Removed the temporary six-arity acquisition base and all media `IUses...` capability badges. The single `MediaType<TItem,TTarget,TRelease>` base now exposes the complete definition through ordinary typed virtual members.
- Removed `IMediaTypeBuilder` and every public media sub-builder. Host compiles one captured definition directly into its internal draft; media extensions no longer execute `Configure(builder)` or advertise shape through implemented interfaces.
- Reintroduced public `Movie` as a meaningful typed item contract for separately shipped `ICataloger<Movie>` and `ICurator<Movie>` pairs. It adds `MovieReleaseTimeline`, computed release stage, separate catalog presence, and plural collection membership.
- Replaced the common enum-keyed date/status item parameters with `MediaItem<TLifecycle>`, keeping milestones and time-dependent availability behaviour in one media-owned object.
- Made collection membership plural and many-to-many while retaining the stable semantic `collection` axis name.
- Removed Newznab-style category numbers from media search definitions. Provider/protocol plugins own mappings from semantic searches to their wire vocabulary.
- Added architecture tests preventing the public builder/capability surface from returning. The temporary typed `Corpus` override introduced here was removed later the same day when it proved to be test-only data with no runtime consumer.

This supersedes the same-day intermediate entries which introduced the six-arity base, capability interfaces, `MediaItem<TReleaseDateKind,TStatus>`, and a source-only Movie alias. Those were migration steps, not the final contract.

## 2026-08-20 — Replace the static media type class with an overridable definition

- Replaced the static-abstract `IMediaType<TItem,TTarget,TRelease>` type-class seam with the ordinary abstract `MediaType<TItem,TTarget,TRelease>` definition base.
- Changed kind identity, names, transitional configuration, and typed capability values to instance members. Concrete media definitions now use virtual/abstract overrides instead of static dispatch.
- Registration captures one closed definition and passes that same instance through the kind-blind double-dispatch boundary; Host derives its runtime projection from the captured object.
- Converted selection, search, matching, format, identity, grouping, release-policy, and release-grammar definition contracts away from static-abstract members.
- Added the full acquisition `MediaType<TItem,TTarget,TRelease,TRepresentation,TPrimarySearch,TMatching>` base so format, identity, policy, grammar, a primary search, and matching no longer appear as optional badges on every ordinary media definition.
- Reclassified `Edition` as a cross-media release fact rather than a forbidden media-kind noun; books, music, film, games, and documents can all have editions.

## 2026-08-20 — Remove nominal one-item target and common-release wrappers

- Added concrete `ReleaseTarget<TItem>` for the common one-item acquisition intent and concrete `Release<TRepresentation>` for the common title, year, edition, and format-owned representation.
- Removed Movies-only `MovieReleaseTarget` and `MovieRelease`. Movies now closes the common target and release types directly over its item and Video representation.
- Kept richer media-owned targets only where they carry real semantics. Television's set of episode coordinates is not equivalent to one item and remains a legitimate specialized target.
- Clarified the capability boundary: every media type participates in common acquisition concerns, while separate closed capability interfaces express real optionality, repetition, or alternative cardinalities rather than nominal media-kind wrappers.

## 2026-08-20 — Make the common item and collection the schema

- Hoisted the complete reusable collection shape into the concrete, extensible `MediaCollection<TItem>` class.
- Introduced concrete `MediaItem<TReleaseDateKind,TStatus>` as the directly usable common catalog-item shape rather than an abstract shell or field bag.
- Replaced four Movies-only release-date properties with one enum-keyed date map. Host projects its media-owned enum members as named date components.
- Hoisted the repeated item fields, including localized text, ratings, certification, organization, website, preview, artwork, and collection, into the common class.
- Removed the nominal `Movie` entity from the public compiled contract. Movies registers the closed `MediaItem<MovieReleaseDateKind,MovieStatus>` type directly; its local `Movie` name is only a source alias.
- Changed derived item and group identifiers to use declared semantic names and relationship properties rather than CLR implementation type names. Reusing `MediaItem<,>` or `MediaCollection<T>` therefore does not leak those implementation names into tokens or wire identifiers.

## 2026-08-20 — Restore typed media ownership and format-owned quality

- Replaced the split generic/non-generic media-type model with `IMediaType<TItem,TTarget,TRelease>` and a Host-only kind-blind runtime bridge.
- Distinguished raw `ReleaseListing`, typed acquisition targets, interpreted releases, target coverage, and selectable release options.
- Made Cataloger and Curator item-typed and removed their field-dictionary result models.
- Changed provider registrations from preconstructed instances to implementation types activated by Host DI after admission.
- Removed the universal video-shaped evidence contract, Host-owned video scanner vocabulary, public video quality family, and executable quality-axis objects from descriptors.
- Introduced `Arronix.Format.Video` as the owner of video representation, lineage, codecs, dynamic range, audio tracks, file extensions, vocabulary, and default policy fragments.
- Replaced source/fidelity labels with channel-relative observable lineage. Remux and WEB-DL are direct copies of different channel artifacts; neither claims a pristine master.
- Introduced deterministic typed release policy and selection with bounded facet scoring and a stable final tie-break.
- Removed the Movies-owned TMDB/IMDb catalog declaration. Media types own their shaped entities and identity roles; provider plugins own vendor integrations.
- Advanced the contract assembly and first-party manifest range to the `0.8` line so breaking experimental contracts are not shipped under the old `0.3` identity.

This change supersedes the video quality-axis/evidence architecture introduced in commit `ca2cd4eef`. Its generic policy ideas were retained where useful; its placement of video vocabulary in Abstractions and Host, globally ordered evidence, closed codec enums, and descriptor-carried executable policy were rejected.

## 2026-08-20 — Hoist common values and separate language behaviour

- Moved ratings, structural rating scales, rating voice, content certification, monitoring scope, and typed standard workbench rows out of Movies into media-neutral contracts.
- Introduced `Localized<T>` to preserve the relationship between a language and a typed payload.
- Added `TitleLanguageAttribute` so typed entities can state the language of their configured title and Host can carry it through naming without guessing English.
- Made catalog candidate rows generic over the complete media item and allowed them to carry artwork as semantic data without imposing a grid or thumbnail layout.
- Added typed workbench proposal, row, and commit values; retained the untyped field projection only as the current wire bridge.
- Added the `language` plugin capability and `ILanguageDefinition` activation seam. English, German, and French comparison/query/file-name/sort rules now live in the independently loadable `Arronix.Language.Reference` implementation; Host no longer assumes that an unstated language is English.
- Removed English, French, and German stop-word, article, query, and transliteration data from the Movies parse declaration.
- Changed composite derivation and projection to preserve nested typed values instead of flattening them through `ToString()`.

## 2026-08-20 — Compile the common entity floor and media relationships

- Replaced the empty `IMediaItem` marker with an `IMediaEntity` contract shared by items and groups: host key, external identifiers, title, title language, overview, and artwork.
- Made common entity semantics derive from the interface itself, removing the need for media extensions to repeat identity, title, title-language, and artwork attributes.
- Removed `TitleLanguageAttribute`; the compiled `IMediaEntity.TitleLanguage` member is the single source of that fact.
- Hoisted the localized title/overview pair from Movies as the media-neutral `ItemInfo`; Movies now carries `Localized<ItemInfo>` rather than a movie-named duplicate.
- Added closed generic capability contracts for file cardinality, format composition, external identity roles, grouping, release policy, release grammar, ordered and numeric selections, searches, and matching.
- Changed Host derivation to compile those capabilities at its deliberate type-erasure boundary before producing existing descriptors and runtime engines.
- Moved the corresponding Movies declarations out of the monolithic builder transcript into typed capability definitions. Querying, naming, summaries, intent, actions, workbenches, and derivations remain transitional and are recorded as active migration work.
