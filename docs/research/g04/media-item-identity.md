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
- The host alone assigns `MediaItemId`, and only when a catalog item is taken into local library state.
  `IMediaEntity` declares no key, so a cataloger has nothing to invent. Identity is host state
  (`CatalogIdentity`), scoped by media kind and level, and outlives the reload of any extension. A local
  identity is unique within its media kind and no further, so every lookup — assignment, supersession,
  canonicalization — carries the whole scope rather than the number.
- Resolving identity and assigning it are separate contracts on that one state. `ICatalogIdentityReader` is
  public; assignment is a host-internal interface `CatalogIdentity` implements explicitly. A read path that
  holds identity state therefore has no member through which the identity space could grow.
- Repeated fetches, aliases and redirects resolve to one reference. Identifiers assigned separately and
  later found to name one item merge onto the lower assignment, and the superseded reference resolves
  through `ICatalogIdentityReader.Canonical`.
- A catalog reference carries a scheme and a value: never a `ProviderId`, a configured definition, or a CLR
  implementation type. Routing is by scheme alone.
- A catalog read answers with `CatalogCandidate<TItem>`: the catalog's identity, the exact typed item, the
  identifier asked for when a redirect made those differ, and the reference the platform already holds it
  under. That held reference is the lowest surviving assignment among every identifier the candidate would
  bind, so it does not depend on the order a cataloger lists them in. Assignment is host-internal. A reference in an unowned scheme is
  `CatalogSchemeUnowned` (an installation problem); an item stating other than one identifier in its own
  scheme is `CatalogIdentityInvalid` (an extension defect). A curated list's schemes are all checked before
  any of it is fetched, and a candidate retains the curator's optional entry identifier.

## Current limitations

- **A merge resolves identity and moves nothing else.** Library rows already written under a superseded
  reference stay under it; migrating them is a store operation this milestone does not have. `Held` and
  `Canonical` resolve on the read path, so a caller does not have to remember to; nothing moves rows.
- **Nothing persists.** `CatalogIdentity` is in memory, as `IMediaStore` is.
- **No production cataloger ships**, so `CatalogDispatcher` has no production caller. That is G05's work.
- **Assignment is host-internal and offered to no caller.** Naming a record and recording that the library
  holds it are one transaction, and only the first half exists; the second is the durable store's. It stays
  internal until it runs inside that transaction.
- **`MediaItemId.FromInt64` is still public**: the wire layer rehydrates identifiers the host assigned, and
  the unconverted kinds' legacy `IMediaItemSource` seams still mint their own. The guarantee for providers is
  structural — no catalog contract reaches a `MediaItemId` — and is asserted against the compiled contracts
  and against a compiled provider package, not enforced by the CLR.
