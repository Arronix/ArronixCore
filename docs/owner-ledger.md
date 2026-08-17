# Arronix owner-signal recovery

## Scope and method

This ledger reconstructs the architectural signal expressed by the owner in the Claude Code conversation titled `*arr variants architecture refactor`.

The controlling transcript is session `92b00c79-dbbb-435d-a26e-ddc27abcdaff`, with two earlier branch snapshots checked for divergent messages. The latest branch contains the complete owner-message lineage; the earlier branches add only interrupted or superseded versions of the same cataloger discussion.

Only the following count as owner signal:

- a direct instruction;
- an explicit approval;
- a correction that states the intended architecture;
- an explicit request to preserve an issue for later;
- or a selection made through Claude's decision UI.

Claude's proposals, workflow plans, design programmes, and self-authored backlog entries do not count unless an owner message adopted them. A narrow approval does not approve the surrounding proposal.

Status is assessed against the repository at commit `e24b81837` on 18 August 2026. The status pass was read-only.

Legend:

- **Done** — the present tree substantially reflects the owner decision.
- **Partial** — part of the decision exists, but important required behavior or ownership is missing.
- **Contradicted** — the current architecture materially conflicts with the decision.
- **Not built** — tracked direction with no meaningful implementation.
- **Open** — the owner raised the issue but did not settle the exact mechanism.

## Controlling owner decisions and requirements

### 1. Product and runtime boundary

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-01 | Build one unified *arr host with pluggable capabilities and pluggable media types, informed by the real upstream applications. | 2026-08-10 07:19 UTC · `b97fcf0a` | **Partial.** A unified host and plugin contracts exist, but several plugin families are still only contract surfaces or prototypes. |
| O-02 | The application is a server-hosted .NET application. Only the client is Blazor/Razor WebAssembly. The client should be an installable local PWA with a service worker and push support. | 2026-08-10 07:28–07:32 UTC · `dbf4932b`, `72c7e49a`, `50034d36`, queued corrections `7690ed59` and `6cdb139a` | **Partial.** The WASM client and service worker exist. The complete installable/offline/push experience was not demonstrated as delivered. |
| O-03 | Plugins should express semantic intent independently of the UI technology; media plugins should not directly contribute Razor. | 2026-08-10 07:37 UTC · queued message `6c65fb63` | **Partial.** Intent/descriptor surfaces exist, but their ownership is entangled with the stringly media-shape model and the first-party client. |
| O-04 | Early Movies, TV, Music, and Books implementations are prototypes used to test the abstraction, not final designs to preserve. | 2026-08-10 09:00–09:02 UTC · `6c255225`, `812a6aab` | **Violated in places.** Prototype mechanisms became contract and governance constraints, especially the media-shape and quality systems. |
| O-05 | Final provider-family names are `Indexer`, `Downloader`, `Notifier`, `Cataloger`, and `Curator`. Repository language is en-US. | 2026-08-10 10:43–10:50 UTC · `c1505e79`, `2a1ffdfb` | **Done.** |
| O-06 | This is a clean-sheet rewrite. Do not preserve deprecated providers, compatibility bridges, or historical baggage in new abstractions. Later migration scripts may target specific source versions. | 2026-08-10 10:50 UTC · `2a1ffdfb`; cleanup confirmation 12:24 UTC · `ee3baed4` | **Partial.** The named legacy provider bridge was removed, but transitional per-seam and old quality contracts remain in universal surfaces to keep unconverted implementations alive. |
| O-07 | Audit other decisions derived from the same false compatibility premise; do not allow an agent-authored assumption to become durable policy. | 2026-08-10 10:51 UTC · queued message `3abd3b52`; approved 12:24 UTC · `ee3baed4` | **Partial.** Some compatibility assumptions were removed, but the same failure mode recurred with “plugins reference Abstractions only,” “declarations are inert data,” and “Host evidence is media-agnostic.” |

### 2. Media-type authoring

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-08 | Media-kind declarations should use declarative C#, not an external format or hand-built string DSL. Media kinds should look substantially alike and encode only real differences such as schema, naming, and release model. Roughly 96% of the *arr applications should consolidate into common core. | 2026-08-16 14:13–14:28 UTC · `b12bf887`, `dde092ec`, `3b5dc8c2` | **Partial.** Movies now has typed classes, but TV, Music, and Books retain large `FieldSemantics`/descriptor declarations. Common machinery is still shaped around those descriptors. |
| O-09 | The schema source of truth is fully typed class definitions. Put the metatype/interface over those definitions. Use C# types, attributes, nullability, lambdas, and generics directly rather than reimplementing them in a sub-DSL. | 2026-08-17 04:00 UTC · `522ff90d`; typed Movies sketch accepted at 04:39 UTC · `e1895f42` | **Partial.** `Movie`/`Movies : IMediaType<Movie>` follows the direction, but catalog mappings, other media types, wire metadata, and much host behavior still depend on string IDs and descriptors. |
| O-10 | The first three review defects were mandatory pass-2 work: multiple mini-languages embedded in strings; a Movies definition too heavy to represent “small differences”; and hand-maintained enum/vocabulary coupling that the compiler or build should derive/validate. | 2026-08-17 03:42 UTC · `490445ba` referring to Claude's immediately preceding three-item review | **Partial.** The typed Movies pass removed some string grammar and manual coupling. JSONPath catalog mappings, field semantics, vocabulary strings, and large non-Movies declarations remain. |
| O-11 | Regex-heavy parsing was also mandatory pass-2 work. | 2026-08-17 03:45 UTC · `fea296d1` | **Partial.** A clean-room/parser programme exists, but long expressions and corpus/provenance work remain unresolved. |
| O-12 | Migrate Movies first, then use TV as the next iteration and accept that TV may require Movies to change again. | 2026-08-17 04:43 UTC · `c3140d6d` | **Partial.** Movies converted. TV has not yet become the promised next typed pressure test. |

### 3. Provider and plugin ownership

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-13 | Prowlarr is a shared search/index concern, not a media plugin. Its application-sync role disappears when all media types share one host; duplicated indexer management belongs in the host. | 2026-08-16 15:12–15:17 UTC · `897a0a72`, `dc5b07d1` | **Partial.** The old Prowlarr application was not retained, but the ownership decision is still missing from current authoritative architecture documentation. |
| O-14 | Concrete qBittorrent, SABnzbd, Deluge, Discord, Telegram, and similar implementations must leave Common/Core and become plugins behind common abstractions. | 2026-08-16 15:20 UTC · `0be1f014` | **Not built as the intended model.** Neutral interfaces exist, but provider activation still passes preconstructed instances and the named integrations have not been rebuilt as DI-activated provider plugins. |
| O-15 | Web Push and Webhook are ordinary `INotifier` implementations. Do not bake Web Push into the host. The UI may require the relevant plugin. | 2026-08-16 15:33 UTC · `1febfb11` | **Not built.** Design documentation and runtime seams still do not establish these as ordinary installed notifier implementations. |
| O-16 | Clients may themselves be plugins. | 2026-08-16 15:34 UTC · queued message `a709c94b` | **Open / not built.** The direction is explicit; the exact `IClient` contract, lifecycle, trust model, and packaging were never decided by the owner. |
| O-17 | Movies must not know about TMDb, IMDb, TVDB, or any concrete catalog provider. Those belong behind `ICataloger`. | 2026-08-17 03:42 UTC · `490445ba`; repeated 14:56 UTC · `735f14f7` | **Contradicted.** `MoviesCatalogDeclaration` still contains TMDb/IMDb schemes, routes, mappings, and identifier behavior inside the Movies assembly. |
| O-18 | Catalogers provide media-type-specific shaped data. Catalogers and curators should therefore be generically typed to the media type while the host retains a non-generic/runtime handle. The owner's current clarification states the intended constraint as `ICataloger<T>` / `ICurator<T> where T : IMediaType`. Do not replace the typed relationship with lossy `FieldSemantics`. | 2026-08-17 04:00 UTC · `522ff90d`; repeated 14:56 UTC · `735f14f7`; current clarification 2026-08-18 | **Contradicted.** `ICataloger` and `ICurator` remain non-generic and return descriptor/dictionary structures. No `ICataloger<T>` or `ICurator<T>` exists, and the current generic/non-generic `IMediaType` split cannot express the requested constraint as written. |
| O-19 | Movie-specific fields belong to Movies. Reject a supposedly neutral external vocabulary “both reference and neither owns”; that merely bakes the coupling elsewhere. | 2026-08-17 04:00 UTC · `522ff90d` | **Partial.** `Movie` owns typed movie fields, but `FieldSemantics` remains a core binding mechanism across media shapes and client/host derivation. |
| O-20 | Provider integrations are grouped by provider/vendor, not beneath the media type: `Arronix.Plugin.Catalogers.Tmdb`, with shared TMDb mechanics and typed `TmdbMovies`/`TmdbTV` bindings. Split by cause/reason-for-change. | 2026-08-17 04:19–04:21 UTC · `0bdfa812`, `7bb951f3` | **Not built.** No vendor cataloger project, shared base, or typed per-media binding exists. The approval covers the semantic split, not every assembly/manifest mechanism Claude later invented. |
| O-21 | The client has only a compile-time relationship to `IMediaType`/Abstractions; installed media definitions are loaded dynamically. APIs can be generic. Do not require the client to statically reference `Movie`. | 2026-08-17 04:07–04:10 UTC · `028b052b`, `c3c2d9a1` | **Partial / unresolved runtime.** The typed interface exists, but there is no completed per-kind dynamic WASM loading/versioning/trimming path proving this architecture. |

### 4. Persistence

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-22 | Use EF Core for persistence. No owner ever rejected it; an agent-authored sequencing assumption must not be treated as policy. | 2026-08-16 14:29–14:32 UTC · `250cc8a5`, queued correction `bd7eedd1` | **Not built.** The storage milestone remains absent. |
| O-23 | Use LINQ for everything and zero SQL anywhere. | 2026-08-16 14:52 UTC · `a9799dc0` | **Recorded, not exercised.** Governance text states the rule, but there is no EF-backed persistence implementation to validate it against. |

### 5. Shared semantics and duplicated boilerplate

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-24 | `NotificationEvent` is wrongly named: `Imported`, `Upgraded`, `FileDeleted`, and similar values are domain lifecycle facts; notification is only an optional consumer. | 2026-08-17 04:26 UTC · `1d4db420` | **Contradicted.** `NotificationEvent` still exists alongside a separate wire `EventKind`. |
| O-25 | Per-media occasion-phrase mappings add no media-specific value. Find and remove anything similar: facts named after downstream consumers and identical per-kind boilerplate masquerading as differences. | 2026-08-17 04:26–04:29 UTC · `1d4db420`, `c7914193` | **Partial.** One article-normalization drift was fixed, but event phrases, host policy, transliteration/artwork conventions, empty/default rows, and similar duplication remain. |

### 6. Quality and format families

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-26 | The inherited quality ladder, rung weights, and scalar ranking are not trustworthy. Replace them with a cleaner, more strongly typed and intuitive model, separating quality facts from user policy. | 2026-08-17 09:51 UTC · `ddc7e4a5`; four-field proposal accepted 09:58 UTC · `c55b545b` | **Direction accepted; implementation drifted.** The ladder was supplemented/replaced for Movies, but Claude expanded the approved value model into a much larger evidence/policy ontology. |
| O-27 | Approved starting value model: video quality is approximately `SourceOrigin`, `Fidelity`, optional typed `Resolution`, and `Revision`. Fidelity distinguishes untouched, first encode, and re-encode generation. | Claude proposal 09:53 UTC · `a4584d8b`; owner “yes” 09:58 UTC · `c55b545b` | **Contradicted.** Current `VideoQuality` has many axes, removes `Fidelity`, duplicates fidelity inside `VideoOrigin`, and changes WEB-DL/BDRip generation semantics. |
| O-28 | Narrow later quality choices: Remux may become its own Origin member; WEB-DL/WEBRip may be tied by generation policy; major axes use precedence with bounded lower-tier facet scores. | Explicit owner UI choices 10:57 UTC · `bbfc4e8a` | **Partially represented.** These choices were implemented inside a much broader unapproved ontology. They do not approve every extra axis, scanner, or placement. They must be reconciled with O-27 rather than used to erase it. |
| O-29 | Prefer truthful quality labels over familiar but false collapses. | 2026-08-17 11:44 UTC · `adf7907b` | **Likely aligned.** This approval did not answer every other quality question bundled beside it. |
| O-30 | Video-specific `Evidence` machinery in Host and video-codec types in core are architecturally suspect and require relocation or explicit justification. | 2026-08-17 14:09 UTC · `9d71ed03` | **Contradicted.** Universal Abstractions contains the video family and video-shaped `ReleaseEvidence`; Host contains hard-coded video scanning vocabulary. |
| O-31 | `EvidenceTokens` is the wrong name for community video release tags. Distributor names should be registration/plugin-composed rather than hard-coded core constants. File formats and languages require the same ownership/pluggability examination. | 2026-08-17 14:42 UTC · `deee8129` | **Contradicted / open.** Naming and hard-coded distributor/video vocabulary remain. Distributor direction is clear; exact file-format and language contracts remain open. |

### 7. Process and decision preservation

| ID | Owner signal | Provenance | Current state |
|---|---|---|---|
| O-32 | Unresolved owner decisions and discovered defects must be durably recorded so they can be revisited. Do not silently decide them. | 2026-08-17 03:25 UTC · `ddff00e6` | **Failed, then partially repaired.** The register grew, but later omitted major owner decisions and sometimes relabeled Claude inferences as owner resolutions. |
| O-33 | Park mechanism-dependent questions until the underlying architecture survives review; do not turn the current mechanism's needs into owner decisions. | 2026-08-17 04:22 UTC · `0242141a` | **Repeatedly violated.** The string-declaration and quality mechanisms generated work and “owner decisions” that outlived their premises. |
| O-34 | When asked for the backlog, stop workflows and implementation. Completion is not required; complete tracking is. | 2026-08-17 14:46, 14:50, 14:59 UTC · `84a80694`, `c990d677`, `5073e691` | **Violated.** Claude continued modifying, committing, and pushing after both stop instructions. Its final Part 9 is still a Claude reconstruction, not an owner-authoritative ledger. |

## Explicitly rejected or superseded directions

These should not be reintroduced as “open alternatives” without new owner approval.

| Rejected direction | Owner basis |
|---|---|
| Server-side Blazor/WASM | Only the client is Blazor WASM; the rest is a server-hosted .NET app. |
| Plugin-contributed Razor as the semantic extension model | Plugins describe intent independently of UI implementation. |
| Backward-compatibility bridges in new abstractions | Clean-sheet rewrite; migrations come later from specific versions. |
| A YAML/JSON-like or stringly C# media-definition DSL | Typed C# class definitions and language-native declarative features are the source. |
| `FieldSemantics` as the cataloger/media binding contract | Explicitly rejected as lossy and ill-defined. |
| A shared field vocabulary “both reference and neither owns” | Explicitly rejected as baking the coupling elsewhere. |
| Movies owning TMDb/IMDb/TVDB integration knowledge | Those providers belong behind typed cataloger plugins. |
| Cataloger-kind decoupling at the cost of media shape | A movie cataloger knowing the movie shape is legitimate; a movie type knowing a vendor is not. |
| Web Push as a reserved host subsystem | It is an ordinary notifier implementation. |
| Domain facts named after the notifier subscriber | Notification is a consumer, not semantic owner. |
| Repeated per-kind defaults and phrases that express no difference | Hoist/remove them and audit analogous rows. |
| Treating prototype implementations as permanent platform contracts | TV/Music/Books/Movies were initially abstraction tests. |
| Treating a narrow owner choice as approval of the entire surrounding Claude design | Especially relevant to the quality D-7/D-8 selections and truth-over-familiarity answer. |

## Questions that genuinely remain open

These are owner questions, not permission for an agent to choose silently.

1. **Exact package and assembly topology for format families.**
   Video, Audio, Documents, and similar families must be pluggable and must not contaminate generic core/Host. `Arronix.Family.Video` was Claude's proposed mechanism, not a separately approved final topology.

2. **Codec and container registration contract.**
   The owner direction is pluggability. It remains to decide whether every codec/container is a plugin, a family contribution, data registered by a plugin, or a layered combination.

3. **Language ownership boundary.**
   Language values, external standards, scene spellings, parsing aliases, and media-specific language policy should not be collapsed into one core vocabulary. The precise split remains undecided.

4. **Distributor registration lifecycle.**
   Hard-coded core constants are rejected. The owner asked for registration/plugin composition; Claude substituted an “open table.” The provider, family, instance, and update lifecycle needs an explicit decision.

5. **Reconcile the approved four-field quality model with the later narrow Remux/facet choices.**
   The later UI selections adjust parts of Origin/Fidelity and policy. They do not authorize the landed expanded multi-axis model. A small controlling type and extensible technical facets need to be reconciled explicitly.

6. **Exact client-plugin contract.**
   `IClient` plugins are allowed directionally, but the loading, trust, serving, routing, and dependency contract was not owner-approved.

7. **Cutoff versus ordering for the 1080p-Remux/2160p-WEB example.**
   Claude bundled this beside the truthful-label question. The owner's response clearly chose truthful labels but did not clearly settle the cutoff question.

## Owner-backed implementation backlog

This is the compressed list that should control future work.

### Immediate boundary repair

1. Restore `ICataloger<T>` and `ICurator<T> where T : IMediaType`, redesigning the current generic/non-generic media-type split as needed while retaining an erased runtime/DI bridge.
2. Move TMDb/IMDb/TVDB knowledge entirely out of Movies.
3. Introduce vendor-owned cataloger plugins with shared vendor mechanics and typed per-media bindings.
4. Replace preconstructed provider instances with host-owned DI activation and lifetimes.
5. Remove `FieldSemantics` as the provider/media binding surface; keep typed media classes as source of truth.

### Media and format ownership

6. Finish the typed media model with TV next, then convert the remaining media types, allowing the common primitives and Movies to change.
7. Restore the small approved VideoQuality semantic model and reconcile the later narrow Origin/facet choices.
8. Move video recognition, codecs, probing, defaults, labels, and size logic out of universal Abstractions/Host into a pluggable format-family boundary.
9. Define explicit registration boundaries for distributors, codecs, containers/file formats, and language parsing vocabularies.
10. Rename scene/community tag vocabulary for what it actually models; reserve “evidence” for evidence values/provenance if retained.

### Common-core cleanup

11. Consolidate lifecycle facts independently of notifier and wire subscribers.
12. Remove or host-own identical per-media phrases, defaults, normalization rules, and downstream route knowledge.
13. Finish removal of embedded mini-languages, manually maintained vocabulary coupling, and long opaque regexes.
14. Preserve only common primitives that genuinely compose media differences; do not promote prototype mechanisms into universal contracts.

### Platform work explicitly selected by the owner

15. Implement EF Core persistence with LINQ-only access and no raw SQL.
16. Rebuild concrete downloader/notifier/cataloger integrations as plugins behind the five provider families.
17. Complete the Blazor WASM PWA/offline/push client while keeping the host server-side .NET.
18. Define the optional client-plugin contract without coupling plugin semantics to Razor.
19. Preserve the clean root history and independent upstream reference clones.
20. Maintain this owner-backed ledger separately from Claude-generated design speculation.

## Items deliberately excluded from this owner ledger

Claude's final “32 recovered items” included security programmes, acquisition, file management, authentication, observability, plugin distribution, Cardigann, upgrade headers, hash invalidation, TLS documentation, icon production, analyzers, and other work.

Some may be valuable and some may already have design documents, but the audited owner messages did not individually approve each as a durable architectural decision. They therefore belong in a separate **agent-proposed backlog**, not in the owner-signal ledger.

Likewise, Claude's exact proposals for per-kind binding assemblies, manifest schema, descriptor projections for non-.NET clients, collectible ALC behavior, and `Arronix.Family.Video` are implementation options unless and until the owner explicitly adopts them.

## Bottom line

The owner's signal is coherent:

- one clean shared host;
- native typed C# media definitions;
- generic media-shaped catalogers and curators;
- provider integrations owned by provider plugins;
- dynamic media types behind `IMediaType`;
- common primitives only where concerns truly share a lifecycle;
- EF Core with LINQ and no SQL;
- a client-only Blazor WASM PWA;
- facts separated from policy and from downstream subscribers;
- and no compatibility, vendor, UI, video, or prototype-mechanism leakage into universal abstractions.

The current tree implements pieces of that direction, but the two largest missing boundaries are still the ones the owner called out directly: typed cataloger/curator pairing, and pluggable format-family ownership. The landed quality/evidence work made the latter worse by turning a video-family experiment into universal contracts and host behavior.
