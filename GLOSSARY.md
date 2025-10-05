# Arronix Glossary

Core terminology used across the Arronix platform. All definitions are intentionally framed as **functional concepts**, not specific class or interface names. (Example: "Media Store" instead of an interface like `IMediaStore`).

| Term | Definition |
|------|------------|
| Capability | A functional area a plugin declares (for example: indexing, parsing, import, renaming). Determines which behaviors it can contribute. |
| Policy Chain | An ordered pipeline of policy steps applied to a category of processing (parsing, matching, quality, import, naming). |
| Policy Step | A single transformation or validation stage within a policy chain. It may enrich data, emit warnings, or halt processing. |
| Token | A placeholder used in naming or formatting templates that is resolved to a concrete value at decision/import time. |
| Naming Token Set | The collection of tokens a plugin exposes for use in user-defined naming patterns. |
| Manifest | A plugin's declarative descriptor (identity, version ranges, capabilities, policies, tokens, identifiers). Serves as a contract of intent. |
| Media Store | The abstraction layer providing persistence and retrieval of polymorphic media entities regardless of type (series, albums, etc.). |
| Job | A schedulable unit of background work (e.g., refresh metadata, run indexer, import scan) managed by the central scheduler. |
| Scheduler | Core orchestrator that queues, prioritizes, throttles, and retries jobs from all plugins under shared policies. |
| Queue Entry | A persisted record representing pending or in-progress work referencing logical media identifiers. |
| Health Check Contributor | A plugin-provided reporter that supplies health status segments aggregated into a platform health view. |
| Telemetry Event | A structured occurrence (job started, import decision, policy failure) emitted for observability and metrics. |
| Policy Graph | The manifest-declared ordered list of policy step identifiers forming a chain for a given category. |
| Media Kind | A top-level classification (e.g., tv, movies, music, books) corresponding to a plugin domain. |
| External Identifier | A key from an outside metadata ecosystem (e.g., tvdb, tmdb) used for lookup and cross-referencing. |
| Contract | A versioned abstraction (interface/DTO definition set) establishing stable boundaries between core and plugins. |
| Contract Version Range | The semantic version interval of core contracts a plugin asserts compatibility with. |
| Token Collision | Situation where two plugins expose the same token name with conflicting semantics; must be resolved or rejected. |
| Compatibility Layer | Transitional API surface emulating legacy endpoints to ease migration for clients or tooling. |
| Policy Category | A functional grouping of related policy chains (parsing, matching, quality, import, naming). |
| Execution Context | The aggregate mutable state passed through a policy chain, carrying inputs, intermediate annotations, and results. |
| Distribution Package | A packaged form of a plugin ready for deployment (may include manifest, assemblies, metadata). |
| Capability Declaration | Portion of a manifest enumerating the functional areas a plugin will participate in, gating available contracts. |
| Token Sanitization | The process of cleaning resolved token values (illegal characters, length limits) before final path/materialization. |
| Retry Strategy | Rules governing how failed jobs or external calls are re-attempted (delays, backoff, jitter, max attempts). |
| Version Negotiation | The process of verifying overlap between a plugin's declared contract version range and the host's current contract version. |
| Policy Failure | A structured outcome emitted by a policy step indicating a hard stop with user-relevant messaging and diagnostic code. |
| Import Decision | The result state determining whether a discovered file is accepted, upgraded, rejected, or deferred. |
| Observability Sink | Destination for telemetry (logs, metrics, traces) configured centrally and abstracted from plugin code. |
| Naming Template | User-configurable pattern composed of tokens and literals that produces final folder/file names. |
| Activation | The lifecycle phase in which a plugin’s manifest is validated and its services, jobs, and policies are registered. |
| Deactivation | The lifecycle phase (future capability) where a plugin is cleanly unloaded, releasing resources and registrations. |
| Cross-Media Relation | A linkage stored by core that associates entities of different media kinds (e.g., soundtrack album to a TV series). |
| Policy Short-Circuit | A deliberate early termination of a policy chain when a decisive outcome is reached (success or failure). |
| Backfill Job | A job type focused on retroactively populating or repairing historical metadata or artifacts. |
| Release Matching | The process of correlating an incoming or indexed release artifact to a canonical media entity. |
| Quality Evaluation | Assessment stage assigning rank or tier based on source, resolution, codec, bit depth, and configured cutoffs. |
| Upgrade Rule | A rule governing whether an existing imported item can be replaced by a higher-quality candidate. |
| Library Layout Policy | A set of rules governing directory/structure decisions when placing media on disk. |

