# Arronix Glossary

Core terminology used across the Arronix platform. All definitions are intentionally framed as **functional concepts**, not specific class or interface names. (Example: "Media Store" instead of an interface like `IMediaStore`).

Terms are grouped by the subsystem they belong to. Where a concept is declared but not yet executed by the runtime, the definition says so.

> **Spelling.** Every term here, and every identifier it names, is **American English** — `Catalog`, `Normalize`,
> `Behavior`, `Canceled`. See [`ARCHITECTURE.md` §3.4](ARCHITECTURE.md).
>
> **Where a term says "formerly".** Arronix is a clean-sheet rewrite with no back-compatibility. A former name is
> recorded so that older design notes remain readable — it is **not** a surviving alias. Nothing in the codebase
> answers to it.

---

## Platform & Plugins

| Term | Definition |
|------|------------|
| Media Kind | A top-level classification (e.g. tv, movies, music, books) contributed by a plugin. Exactly one plugin owns a given kind across an installation. |
| Manifest | A plugin's declarative descriptor (identity, version, contract range, entry assembly, capabilities, media kinds, identifiers, tokens, policies). Serves as a contract of intent, and is the first thing validated at load. |
| Plugin Module | A plugin's single composition entrypoint. One per plugin assembly; its declared identity must equal the manifest identity, and it is the only thing the host constructs from plugin code. |
| Plugin Context | The whole of what a plugin can reach: platform services, its own identity and paths, and gated contracts obtained through explicit request. Nothing reaches a plugin that does not arrive through it. |
| Plugin Registry (registration surface) | The closed set of registrations a plugin may make during composition. There is no container, no assembly scan and no reflection over plugin types — a plugin registers what it registers, explicitly. |
| Registration Ledger | The record of everything one plugin registered during composition. It is what the capability checks, the token cross-check and the host's admission checks are evaluated against. |
| Capability | A privilege a plugin declares in its manifest, from a **closed vocabulary of fourteen**: indexing, metadata, parsing, matching, quality, renaming, import, download, notification, media-kind, import-list, network, storage, telemetry-sink. |
| Capability Declaration | The manifest section enumerating a plugin's capabilities. It is a grant request, not a description — an undeclared privilege is unavailable at runtime. |
| Bidirectional Capability Check | The rule that a declared capability must have a matching registration **and** a gated registration must have a declared capability. Without the second direction a plugin could register a gated implementation it never declared. |
| Implied Capability | A capability granted as a consequence of another rather than declared directly. Network access is implied by indexing, metadata, download or notification, and declaring it explicitly is a load failure for want of a matching registration. |
| Contract Range | The semantic version interval of core contracts a plugin asserts compatibility with. Expressed in a documented subset of npm/yarn syntax: comparison operators, whitespace conjunction, alternation and partial versions. Caret, tilde, wildcard and hyphen forms are rejected. |
| Version Negotiation | Verifying overlap between a plugin's declared contract range and the host's current contract version. No overlap means the plugin is refused. |
| Experimental Contract | A contract published for review rather than for stability. Consuming one requires a file-scoped, reasoned opt-in, so every dependency on an unsettled contract is greppable. |
| Experimental Gate | The additional load rule that a plugin using experimental contracts must bound its contract range at or below the host's next minor version, because an experimental contract may change in any minor release. |
| Plugin Isolation | Loading each plugin into its own assembly load context so that plugin dependencies cannot collide. The contract assembly is deliberately shared with the host so that values cast cleanly across the boundary. |
| Reference Graph Check | Reading a plugin assembly's references as metadata — before any load context exists and before any type initializer runs — to prove it references the contract assembly and no other platform assembly. |
| Quarantine | The outcome when a plugin fails any admission step: that plugin is disabled and its failure recorded with a diagnostic code and specific defects. The host and every other plugin continue unaffected. |
| Activation | The lifecycle phase in which a plugin's manifest is validated, its assembly isolated and loaded, its registrations admitted, and its contributions committed to the host registries. |
| Deactivation | The lifecycle phase (not implemented) where a plugin is cleanly unloaded, releasing resources and registrations. |
| Scoped Platform Service | A platform facility handed to a plugin in a narrowed form — a file system rooted at granted paths, an HTTP gateway with host allow/deny lists, a partitioned cache, a partitioned rate limiter, an event stream filtered to what the plugin may see. |
| Distribution Package | A packaged form of a plugin ready for deployment (manifest, assemblies, metadata). Not yet implemented. |

## The Media Shape Model

| Term | Definition |
|------|------------|
| Media Shape | The complete declaration of how one media kind is structured: its levels, file binding, addressing schemes, groupings, format families, selection facets, search kinds and tokens. It is **pure data** — no code, no types — which is why one declaration can serve the plugin, the host and a front end without any of them duplicating it. |
| Level | One tier of a media kind's containment hierarchy. A shape has exactly one root level, each level names its parent, the graph is acyclic, and a level has at most one child. |
| Level Role | What a level is *for*. Roles are a flag set, not a position, because they are not mutually exclusive — the simplest kind carries every role on one level and the richest spreads them over four. |
| Library Entry | The role marking what a user actually adds to their library. It owns the path, the profiles, the tags and the root folder. Exactly one level per shape carries it. |
| Acquisition Unit | The role marking what a search or a grab targets. |
| Variant Axis | The role marking competing manifestations of the parent item, with at-most-one-selected semantics (for example, editions of a book or pressings of an album). |
| Completeness Unit | The role marking what "have" and "missing" are counted in. |
| File-Bearing Unit | The role marking a level that participates in the item-to-file join. |
| Level Identity | A level's declaration of whether it is a catalog record, a library record or both, whether it may be redirected, and which external identifier schemes it admits. This split is part of the shape, not a storage normalization. |
| Addressing Scheme (Coordinate Space) | One declared way the items at a level can be addressed — an ordinal tuple, a date, a label, or a singleton. Addressing is **not** a property of a level: a level admits a set of schemes and an item carries a bag of readings, zero or more populated. |
| Coordinate | One item's position within one addressing scheme. A tagged value with exactly one payload populated, so ordering is preserved and no polymorphic serialization is needed to cross an assembly boundary. |
| Coordinate Reading | A coordinate together with the confidence attached to it. Confidence is a property of the reading, not of the scheme. |
| Coordinate Confidence | How a reading was arrived at: unverified, derived, asserted or verified. Only a scheme that declares it may be unverified is allowed to produce an unverified reading. |
| Canonical Scheme | The addressing scheme in which identity and completeness are measured. Exactly one per level that declares any. |
| Provenance-Sensitive Scheme | An addressing scheme whose readings are only meaningful when the source text came from a release name rather than from a catalog or a file. This is how alternative community numbering is expressed without inventing a new kind of addressing. |
| Dense Sequence | A scheme declared to have no intentional holes, so that a gap means something is *missing* rather than something never existed. |
| Sequence Axis | A named run over one component of an ordinal addressing scheme — for example a season within a series. It is not a level. It may carry a policy record (a stored entity for the run of its own). |
| Sequence Exception | A reserved value outside a dense run, optionally excluded from completeness counting (for example, a specials collection). |
| File Binding | The declaration of how files relate to the items they satisfy. Always stored as the join *(item, file, ordinal?)*; the declaration supplies uniqueness constraints over that join, never a choice between schemas. |
| Anchor Level | The level a file record hangs from. |
| Unit Level | The level whose items a file satisfies — a descendant of, or the same as, the anchor level. Where the two differ, file ownership survives a change of selected variant. |
| Span Constraint | A declaration of whether one file may straddle two values of a named coordinate component. Declared rather than thrown, so the host enforces it for any kind without knowing what the run means. |
| Variant Selection | The declared at-most-one-selected semantics of a variant axis: whether the host may auto-switch, on which occasions (manual, on import, on refresh), and whether completeness is counted against the selected variant or the union of all of them. |
| Grouping Axis | A collection that cuts *across* the containment hierarchy rather than nesting inside it — a collection, a box set, a book series. Declares arity, whether membership carries a position, whether a primary member exists, whether the group outlives its members, and whether it has metadata of its own. |
| Group Lifetime | Whether a group is reference-counted (it ceases to exist when its last member leaves, and orphan links are swept) or independent (it exists with state of its own). |
| Primary Member | The one member of a group designated as its representative, needed wherever a single-valued name is required. Only meaningful when an item may belong to several groups. |
| Format Family | A group of file formats that share a quality ladder — for example written versus spoken books. Each family carries its own extension set, its own ladder and its own unknown tier, which is how one media kind holds two incomparable quality ladders without ranking across them. |
| Quality Ladder | The ordered tiers within one format family, used for ranking and cutoff decisions. |
| Selection Facet | The third profile axis, after "how good must a file be" and "which releases are preferred": which sub-items should exist at all. Expressed as an enumeration, a numeric threshold or a flag. |
| Facet Application | Whether a facet decision prevents an item from being created at all (materialization) or merely hides one that exists (visibility). Declaring this is the answer to a refresh unexpectedly destroying data. |
| Monitor Dimension | One orthogonal axis of "does the user want this", declared as a toggle or an enumeration. A kind may declare several. |
| Acquisition Scope | How broad a single grab is: one item, a span of a sequence axis, or everything beneath an ancestor level. |
| Search Term | A member of the closed nine-value vocabulary an indexer query can be built from — free text, work title, creator, issuer, genre, year, external identifier, ordinal, date. Deliberately contains no media noun. |
| Search Kind | A declared way a media kind can be searched for: the level it targets, its acquisition scope, its required and optional search terms, and its categories. |
| Indexer Eligibility | Deciding whether an indexer can serve a search, by pure set intersection of declared search terms and categories. No indexer-protocol search-mode string ever enters a shape. |
| Field Descriptor | A declaration of one displayable or filterable field: its identity, value shape, semantics, importance and permitted choices. |
| Field Value | A tagged value carrying one of a closed set of value shapes. Absence is representable distinctly from emptiness. |
| Prominence | A field's declared *importance rank* — primary, secondary, detail or diagnostic. It is deliberately not a layout instruction; a front end decides what an importance rank looks like. |
| Behavior Seam | One of the four things a plugin must actually *do* rather than declare: supply its shape, match release text to items, plan release queries, and project its own catalog. Everything else in the model is data. |
| Catalog Projection | A plugin serving its own catalog to the host on demand, rather than the host storing catalog data. The host stores library state only and merges the two. |
| Library Facet | The host's half of an item: monitoring, selection and other state the host owns, as distinct from the catalog data the plugin projects. |
| Validated Shape | A declared shape that has passed the host's sixteen structural rules. The gate exists so that after it, no host lookup can fail and the rest of the host is written without defensive checks. |
| Shape Defect | One specific structural fault in a declared shape, carrying the offending path and a diagnostic code. Validation returns every defect in one pass rather than throwing on the first. |
| Affordance | An ability the host **derives** from a validated shape — monitorable, searchable, refreshable, renamable, removable, browsable, selectable, taggable, relocatable, downloadable. It is never declared, because a declaration that can be derived is a declaration that can disagree. |
| Completeness | How much of what should exist actually exists, computed by the host from the file binding, the variant selection and any sequence exceptions. Not a plugin responsibility. |

## Intent & User Interface

| Term | Definition |
|------|------------|
| Intent Surface | A plugin's declaration of what a user can *do* with its media kind: browse axes, sorts, filters, actions, states, external surfaces and working surfaces. It contains no reference to any UI technology or control. |
| UI Contribution (Descriptor) | The only way a plugin influences the interface: by declaring intent as data. A plugin cannot name, ship or inject a component. A front end maps declared intent to its own components, in one place, and that mapping is the whole coupling. |
| Browse Axis | A declared way of traversing a media kind — by hierarchy, by sequence, by grouping, by facet, or flat. |
| Action Descriptor | A declared operation a user can invoke, with its scope, its parameters, its consequence and how certain the user must be before it runs. |
| Consequence | The declared cost and reversibility of an action, used to decide how (or whether) the user is asked to confirm it. |
| State Descriptor | A declared item state with a valence, backed by a field whose value the state names. Lets a front end show status without knowing what the status means. |
| External Surface | A declared link out to somewhere the platform does not own, expressed as a template with placeholders that name declared fields. |
| Workbench | A declared row/column editable grid over a plugin-supplied proposal — the designed escape hatch for workflows a declarative vocabulary cannot express, manual import being the daily example. It lets a plugin *point at* a generic surface; it never lets a plugin inject markup. |
| Workbench Proposal | The plugin's suggested contents of a working surface, which the user may adjust before committing. |
| Wire Contract | A data type that crosses the HTTP boundary. Wire contracts live in the shared contract assembly rather than being mirrored on each side, because a mirrored type across two independently versioned boundaries is a guaranteed-divergence bug. |
| Media Kind Descriptor | Everything a client needs to render a media kind: its shape, its per-level presentation, its intent surface, its capabilities and its owning plugin. |
| Rendered Summary | Structured text about an item, produced by the plugin that owns it and consumed identically by notifications and activity feeds. |
| Event Envelope | The single closed-vocabulary shape every real-time event is delivered in, so a client dispatches on a known enumeration rather than parsing a topic string. |

## Providers

| Term | Definition |
|------|------------|
| Provider | A stateless integration point with something outside the platform, belonging to exactly one of the five families below. Statelessness is deliberate — configuration arrives with every call rather than being mutated onto a shared instance. |
| Provider Family | Which of the five kinds of integration a provider is: **Indexer**, **Downloader**, **Notifier**, **Cataloger** or **Curator**. A closed enum, because the host dispatches on it — each family has its own registration method, status policy and configuration surface. |
| Indexer | A provider that answers *where a release can be obtained*. It supplies release candidates for a query; it does not transfer anything. Contract `IIndexer`. |
| Downloader | A provider that *transfers* a chosen release and reports on the transfer. Contract `IDownloader`. Formerly "download client"; the user-facing label still reads **Download Clients**. |
| Notifier | A provider that tells something outside the platform that an event happened. Contract `INotifier`. |
| Cataloger | A provider that answers **what a thing *is***: an external authority supplying canonical identity and facts, which populate the catalog record (`LevelIdentity.HasCatalogRecord`). Contract `ICataloger`. Formerly "metadata source". TheMovieDB is a Cataloger. |
| Curator | A provider that answers **which things you *want***: an external list selecting what belongs in the library at all. It says nothing about what any of its entries *are*. Contract `ICurator`, gated by `Capability.Curation`. Formerly "import list". A Trakt watchlist is a Curator. |
| Cataloger vs Curator | The distinction the two names exist to make. A **Cataloger** is an external *authority* — it describes things that exist in the world. A **Curator** is an external *opinion* — it picks things the user wants. Neither is derivable from the other, and a plugin routinely uses both: a Curator says "add this show", a Cataloger says "and here is what that show is". |
| Provider Descriptor | What a plugin publishes about a provider it offers: its identity, family, declared settings and presets. |
| Provider Definition | One configured instance of a provider, as an operator has set it up. |
| Provider Invocation | The per-call bundle of configuration and context handed to a stateless provider, replacing any notion of a configured instance. |
| Declarative Settings | A provider's settings described as data — identity, role, sensitivity, validation — rather than discovered by reflecting over plugin types. The host never reflects over plugin code. |
| Setting Sensitivity | A setting's declaration that its value is secret. One declaration drives three behaviors: how it is stored, how it is redacted on the way out, and how it is edited. |
| Secret Sentinel | The masked placeholder a secret is replaced with on the way out, and which is treated as "unchanged" on the way back in, so a round-tripped form cannot silently erase a stored credential. |
| Quarantined Definition | A provider definition retained but disabled after repeated failure, rather than deleted. |

## Orchestration

| Term | Definition |
|------|------------|
| Job | A schedulable unit of background work (refresh metadata, run indexer, import scan) managed by the central scheduler. |
| Scheduler | The core orchestrator that queues, prioritizes, throttles and retries jobs from all plugins under shared policies. |
| Schedule Grammar | The closed four-form vocabulary a job's schedule is expressed in: manual, at startup, every interval, or daily at a time. Cron is deliberately excluded. |
| Queue Entry | A record representing pending or in-progress work, referencing logical media identifiers. Currently in-memory only, and lost on restart. |
| Concurrency Governor | The component enforcing global and category-scoped limits on simultaneous work. It walks the ready set rather than taking from the head, so a saturated throttle never blocks unrelated work behind it. |
| Backoff Ladder | The exponential, jittered schedule of retry delays applied to a failing job or external call. |
| Failure Class | The classification of a failure that determines whether and how it is retried. |
| Correlation Id | An identifier threaded through a unit of work so that a job, the events it emits and the health it affects can be tied together. |
| Retry Strategy | The rules governing how failed jobs or external calls are re-attempted: delays, backoff, jitter, maximum attempts. |

## Storage, Naming, Health

| Term | Definition |
|------|------------|
| Media Store | The abstraction providing persistence and retrieval of library state, item-to-file links and group membership, independent of media kind. **Currently in-memory only — nothing survives a restart.** |
| Cross-Media Relation | A linkage stored by the core that associates entities of different media kinds. |
| Token | A placeholder used in naming templates that is resolved to a concrete value at decision or import time. |
| Naming Token Set | The tokens a plugin exposes for use in user-defined naming patterns. Declared in two places — the media shape and the manifest — which the host requires to agree exactly. |
| Token Cross-Check | The load step comparing a plugin's manifest tokens and its shape tokens as a **symmetric set**. A token in one and not the other is a load failure, so no token can ship without operator-facing help text. |
| Token Ownership | The installation-wide claim on a token name, so two plugins cannot define the same token with conflicting meaning. |
| Token Collision | Two plugins exposing the same token name with conflicting semantics. Rejected at load. |
| Token Sanitization | Cleaning resolved token values (illegal characters, length limits) before a path is materialized. Currently plugin-local; no shared pipeline exists. |
| Naming Template | A user-configurable pattern of tokens and literals producing final folder and file names. |
| Library Layout Policy | The rules governing directory and structure decisions when placing media on disk. |
| Health Check Contributor | A component supplying health status segments that are aggregated into a platform health view. Both the host's own subsystems and plugins contribute through the same contract. |
| Telemetry Event | A structured occurrence (job started, import decision, load failure) emitted for observability, carrying a correlation id. |
| Observability Sink | A destination for telemetry, configured centrally and abstracted from plugin code. **No sink is implemented; the platform currently emits no telemetry.** |

## Declared but not executed

These terms describe manifest content the platform validates and records but does not yet act on. They are retained because the declarations are real.

| Term | Definition |
|------|------------|
| Policy Chain | An ordered list of policy step identifiers for a category of processing. **Declared and syntactically validated only — no engine executes one.** |
| Policy Step | A single named stage within a policy chain. No execution contract exists. |
| Policy Graph | The manifest-declared map from policy category to its ordered step identifiers. |
| Policy Category | One of the five closed groupings a policy chain belongs to: parsing, matching, quality, import, naming. A sixth category is a load failure. |
| Policy Failure | A structured outcome indicating a hard stop with user-relevant messaging and a diagnostic code. The diagnostic-code mechanism exists; the policy pipeline that would raise it does not. |
| Import Decision | The result state determining whether a discovered file is accepted, upgraded, rejected or deferred. The contract exists; no import pipeline is implemented. |
| Quality Evaluation | Assessment assigning rank or tier based on source, resolution, codec, bit depth and configured cutoffs. Implemented per-plugin against the stable quality contract; there is no cross-family cutoff. |
| Upgrade Rule | A rule governing whether an existing imported item may be replaced by a higher-quality candidate. |
| Release Matching | Correlating an incoming or indexed release to a canonical media entity. Implemented as a behavior seam, per plugin. |
| Backfill Job | A job type focused on retroactively populating or repairing historical metadata or artifacts. |

## Refused

These were considered and **rejected**. They are recorded so that a rejected idea is not mistaken for a backlog
item, and so nobody proposes one again without knowing it was already decided.

| Term | Why it is refused |
|------|-------------------|
| Compatibility Layer | A transitional API surface emulating legacy *-arr* endpoints. **Not built, and not planned.** Arronix is a clean-sheet rewrite: it shares no database with any *-arr* application and offers no in-place upgrade path, so a compatibility layer would wrap nothing. Migration is import-based — separate, later, per-*-arr* scripts. |
| Deprecation Window | A notice period during which a superseded contract keeps working. Below 1.0 nothing has ever shipped against an Arronix contract, so there is nobody to notify: a contract that is wrong is **deleted and replaced**. There are no `[Obsolete]` attributes in `Arronix.*`, no sibling "v2" interfaces, no shims and no host-side bridges. |
| Plugin UI Component Registry | A registry through which a plugin supplies its own interface components. Permanently rejected: it requires shipping plugin code to the browser. A plugin declares intent as data; the client maps intent to its own components. |
