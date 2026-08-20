# Arronix Glossary

| Term | Meaning |
|---|---|
| Media Type | The owner of one media domain, expressed as `MediaType<TItem,TTarget,TRelease,TParser>`. It defines typed entities, binds their parser, and declares relationships the types alone cannot express. |
| Media Item | Durable media-owned catalog/library data. This is the shape Catalogers and Curators return. |
| Release Target | Ephemeral typed acquisition intent: the exact work or set of units a search is trying to satisfy. |
| Release Listing | Raw data returned by an Indexer. It is a provider statement, not yet an interpreted media release or selection candidate. |
| Release | A publication interpreted into the media type's own `TRelease` shape. |
| Representation | Format-owned facts about the artifact a release carries, such as `Video`. |
| Format Capability | An independently owned package of representation types, file extensions, recognition vocabulary, and default policy fragments, such as Video, Audio, or Document. |
| Target Match | The typed relation between a Release and a Release Target, retaining Covered and Missing target portions. |
| Satisfied | The release covers exactly the requested target. |
| Partial | The release covers only part of the requested target. |
| Superset | The release covers the requested target and additional units. |
| Rejected | The release does not identify or cannot satisfy the target. |
| Release Option | A Release Listing, interpreted Release, and Target Match considered together for deterministic selection. |
| Release Policy | Typed hard requirements, lexicographic core preferences, and bounded secondary facets over one release shape. |
| Policy Fragment | Defaults contributed by the owner of a fact into one compiled Release Policy. It is not a separately executing policy. |
| Facet | A secondary score consulted only after the lexicographic core ties. Each facet is bounded to plus or minus ten and a policy may declare at most five. |
| Interpretation Trace | Provenance sidecar recording which typed property was claimed, measured, or inferred, by which contributor, without wrapping every property or globally ranking evidence sources. |
| Video Lineage | Observable facts from a public release channel artifact to the publication: channel, carrier, acquisition method, and subsequent transcode history. It excludes speculative studio-master ancestry. |
| Direct Copy | A byte/stream-preserving copy relative to the acquired channel artifact. Both Remux and WEB-DL are direct copies of different carriers. |
| Unknown | No evidence established the value. Unknown is explicit and must not be treated as a favorable zero/default. |
| Media Descriptor | Host-derived data for discovery and generic presentation. It is not the media schema and contains no executable policy, `System.Type`, or delegate. |
| Cataloger | `ICataloger<TItem>`: an external authority that returns shaped items, answers what a thing is, and recognizes identifiers from its own namespace in release text. |
| Curator | `ICurator<TItem>`: an external list that returns shaped items proposed for inclusion and answers which things are wanted. |
| Indexer | A provider that returns raw Release Listings for a query. |
| Downloader | A provider that transfers a selected listing and reports on the transfer. |
| Notifier | A provider that sends an event outside Arronix. |
| Provider Descriptor | Static provider identity and settings declaration. It is registered with an implementation type, not an instance. |
| Provider Invocation | Per-call configuration and scoped context supplied to a stateless provider implementation. |
| Capability | A manifest-declared privilege enforced in both directions: a contribution requires its capability and a declared contributing capability requires a matching contribution. |
| Registration Ledger | The ordered record of everything a plugin contributed during its sealed configuration phase. |
| Plugin Isolation | A collectible assembly load context containing a plugin and its local dependency closure. Abstractions and framework assemblies unify with Host. |
| Quarantine | Refusal of one invalid plugin while Host and other plugins continue. |
| Media Store | Host-owned library state. The current implementation is in memory and is not restart-durable. |
| Legacy Quality Model | Temporary `IQualityModel`/ladder compatibility used only by unconverted Television, Music, and Books paths. It is migration scaffolding, not the architecture for new work. |
