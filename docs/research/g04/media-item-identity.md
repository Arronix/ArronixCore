# G04 — media item identity at the catalog boundary

**Status:** decided by the owner on 2026-08-25 and implemented. Supersedes the option paper that stood here.

## The invariant

- A cataloger owns an item's identity in the semantic catalog scheme it declares as
  `ICataloger.CatalogScheme`, independent of any implementation, definition or package. The scheme is
  captured once at registration, lower-case and free of white space; a cataloger declaring anything else
  is refused at activation.
- A cataloger returns the exact media-owned item. Every item states exactly one identifier in its own
  cataloger's scheme; that identifier is its catalog identity.
- A curator returns `CuratedReference` values — a catalog identity, optionally with the curator's own
  `CuratedEntryId`, which is a separate type. It returns no items.
- The host alone assigns `MediaItemId`, when a catalog answers for an item; a search resolves one without
  materializing a record. `IMediaEntity` declares no key,
  so a cataloger has nothing to invent. Identity is host state (`CatalogIdentity`), scoped by media kind and
  level, and outlives the reload of any extension. A local identity is unique within its media kind and no
  further, so every lookup — assignment, supersession, canonicalization — carries the whole scope rather
  than the number.
- Repeated fetches, aliases and redirects resolve to one reference. Identifiers assigned separately and
  later found to name one item merge onto the lower assignment, and the superseded reference resolves
  through `CatalogIdentity.Canonical`.
- A catalog reference carries a scheme and a value: never a `ProviderId`, a configured definition, or a CLR
  implementation type. Routing is by scheme alone.
- Materialization is `CatalogDispatcher`, returning `MaterializedItem<TItem>`. A reference in an unowned
  scheme is `CatalogSchemeUnowned` (an installation problem); an item stating other than one identifier in
  its own scheme is `CatalogIdentityInvalid` (an extension defect). A curated list's schemes are all checked
  before any of it is fetched, and a materialized result retains the curator's optional entry identifier.

## Current limitations

Two of the limitations recorded here were closed by G07B, and are kept with their resolutions so the
reasoning that produced them is still readable:

- ~~**A merge resolves identity and moves nothing else.**~~ **Closed by G07B.** The transaction that records
  a supersession now re-keys the catalog record and the library entry onto the surviving identity. Where
  both identities were added, the survivor's record stands, the earlier addition date is kept, and a
  monitoring axis the survivor does not answer is carried over — so nothing the user decided is lost to a
  merge. Callers still canonicalize before reading, and `HostItemSource` does.
- ~~**Nothing persists.**~~ **Closed by G07B.** `CatalogIdentity` is journaled: assignments, supersessions
  and each kind's allocation high-water mark are written in one transaction per assignment, so a restart
  continues the sequence rather than reissuing numbers the library is keyed by. The library facet and the
  catalog records are durable beside it.
- ~~**No production cataloger ships**, so `CatalogDispatcher` has no production caller.~~ **Closed.** G05
  shipped the TMDb cataloger, and G07B gave the dispatcher a production caller: the catalog search, add and
  refresh routes reach it through `CatalogLibrary`, kind-blind, routing by scheme.
- **`MediaItemId.FromInt64` is still public**: the wire layer rehydrates identifiers the host assigned, and
  the unconverted kinds' legacy `IMediaItemSource` seams still mint their own. The guarantee for providers is
  structural — no catalog contract reaches a `MediaItemId` — and is asserted against the compiled contracts
  and against a compiled provider package, not enforced by the CLR.
