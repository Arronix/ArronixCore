# G04 owner decision — durable media item identity at the catalog boundary

**Status:** unresolved. This document states the decision, the facts that constrain it, the alternatives and
what each forecloses. It does not choose one, and no code on `claude/g04-provider-pairing` implements a
choice.

**Raised by:** G04, closing the typed provider-pairing contract.
**Blocks:** the last two G04 exit criteria, all of G05's cataloger/curator closure, and any Host seam that
consumes a shaped catalog result.
**Base commit:** `bbed12a6993b6cfb59f452e6e9ac690efa66360f`.

## 1. The decision, in one sentence

When an `ICataloger<TItem>` or `ICurator<TItem>` returns an item the installation has never seen, who
chooses its `MediaItemId`, and what happens when two natural keys meet, when a catalog redirects one
identifier to another, and when the same fetch runs twice?

## 2. Why it cannot be answered by inspection

Two statements in the current contract are both load-bearing and cannot both hold.

| Statement | Where it is written | Consequence |
|---|---|---|
| A durable key is required on every item | `MediaItem<TItem,TReleaseTimeline,TReleaseStage>.Key` is `required MediaItemId` | A cataloger cannot return a valid `TItem` without choosing one |
| Nothing outside the platform chooses a durable key | `MediaItemId`'s own XML documentation: "a host-minted surrogate, so nothing outside the platform chooses its value or its range" | A cataloger choosing one is outside the platform doing exactly that |

Both are asserted mechanically by
`Arronix.Host.Tests.Providers.CatalogMaterializationBoundaryTests.ADurableItemKeyIsRequiredOfTheProviderAndMintedByNobody`,
so the contradiction fails a test the day either side is quietly changed.

Two independent implementations have now hit it from opposite directions and reached the same place:

- `Arronix.Architecture.Tests.MovieCatalogerFixture` fabricates `MediaItemId.FromInt64(title.Length)` and
  says in its own comment that this proves package shape and is not provider coverage;
- the G05 TMDb pressure test (`claude/g05-tmdb-proof`, commit `72de1fa5a`,
  `docs/research/g05/tmdb-provider-pressure-test.md`) performs the real TMDb round trip, maps every `Movie`
  fact including the media-owned lifecycle, and then throws rather than fabricate a key. It names exactly
  three blocked members and no others: `ICataloger<TItem>.SearchAsync`, `ICataloger<TItem>.GetAsync`, and
  `ICurator<TItem>.FetchAsync`. `ChangedSinceAsync` returns `ExternalId` values and `ReadExternalIds` is
  local recognition, so neither is affected.

## 3. What the platform already provides

Facts, not proposals. Each is what an alternative below has to be consistent with.

- **The reconciliation key already exists and is already owned correctly.** `IMediaEntity.ExternalIds` is an
  `ExternalIdSet`; `ICataloger.ReadExternalIds` makes the marker spelling the cataloger's property and
  nobody else's. A cataloger always knows its own natural key for an item it just fetched.
- **The platform already mints one kind of durable identifier this way.** `IMediaStore.UpsertFileAsync`
  documents "an identifier of zero asks the store to mint one" and `InMemoryMediaStore` mints a
  `MediaFileId` from a counter. The store mints no `MediaItemId`.
- **Library state is keyed by the surrogate, not by the natural key.** `MediaItemRef(MediaKindId,
  MediaLevelId, MediaItemId)` is what `IMediaStore` stores monitoring, file links and group membership
  against. Whatever the durable key becomes, it is what every stored row already points at.
- **The catalog half is projected, not stored.** `IMediaItemSource` projects the extension's own catalog and
  Host merges library state on read. In the only working example, `MoviesSeedProjection` uses the vendor's
  own identifier as the surrogate (`Key = MediaItemId.FromInt64(record.TmdbId)`), so alternative A3 below is
  already the de facto behaviour of the reference data.
- **The redirect and delta hooks are declared and unimplemented.** `CatalogerCapabilities` has
  `IdentifierRedirects` and `DeltaSync`; `ChangedSinceAsync` returns `IReadOnlyList<ExternalId>`. Any answer
  to redirect has to make those two members mean something.
- **There is no materialization call site to be wrong about.** No member anywhere in `Arronix.Host` takes or
  returns `ICataloger<>`, `ICurator<>` or `CuratedListFetch<>`, asserted by
  `CatalogMaterializationBoundaryTests.NoHostSeamConsumesAShapedCatalogerOrCuratorResult`. Adding the first
  one is the moment this decision has to have been made.

## 4. What is already settled and is not reopened here

These are owner-held invariants. No alternative below may be read as reopening one.

- No field bag, `FieldSemantics`, descriptor replay, or reflection convention at the provider boundary.
- No duplicate candidate schema: anything a provider returns is the media type's own shape or a thin,
  exact lifecycle wrapper over it — never a parallel vocabulary of the same facts.
- Type erasure stays one-way and internal.
- Vendor vocabulary stays out of `Arronix.Media.Movies`, `Arronix.Format.Video`, `Arronix.Host` and
  `Arronix.Abstractions`.

## 5. The decision decomposes into two choices

Nothing can be built until both are answered. They are mostly separate — one mechanism below answers both at
once, and that is a property worth seeing rather than hiding.

### 5.1 Mechanism — how does a provider return an item whose durable key is not yet known?

**M1 · Host resolves the key before the call.** Host turns the natural key into a `MediaItemId` first and
supplies it to the provider, which stamps it.
*Consequence:* keeps the key host-minted and every item fully valid. Works only for `GetAsync`, whose
argument is a single `ExternalId`. It cannot work for `SearchAsync` or `FetchAsync`, where the natural keys
of the results are unknown until the response arrives — which is the majority of real catalog traffic.
Rescuing those two would mean changing what they return, which is the one thing the typed pairing exists to
prevent.

**M2 · The key is absent until Host stamps it.** `Key` stops being `required`; `default(MediaItemId)` (zero)
means "not durable yet", and one Host-owned materialization step assigns the real value.
*Consequence:* one item type end to end, no wrapper, no second schema, and the sentinel already has a
precedent inside the platform (`IMediaStore.UpsertFileAsync` documents "an identifier of zero asks the store
to mint one"). The cost is that an item with key zero is representable, so "a `Movie` that is structurally
complete but not yet durable" becomes a legal value, and every reader of `Key` has to know that. The owner
has to say whether that is the "temporarily invalid domain object" the G04 exit gate forbids or the ordinary
before-state of an identity lifecycle.

**M3 · The provider returns a wrapper.** A `CatalogResult<TItem>` returned instead of `TItem`.

This is where an earlier draft of this document was wrong, and the error is worth stating because it looks
coherent. A wrapper cannot both *carry a constructed `TItem`* and *leave the key to Host*, because a
constructed `TItem` already has its `Key` — that is what `required` means. So M3 is not one option; it is
two, and each has a cost the other does not.

- **M3a · the wrapper carries a factory.** `CatalogResult<TItem>(ExternalId Natural, Func<MediaItemId, TItem> Build)`,
  or a typed equivalent. Host mints, then calls back to get the item.
  *Consequence:* `Key` stays required and every constructed item is durable, with no second schema — the
  provider's fetched facts are captured in a closure and no fact is restated anywhere. The cost is the
  shape: a provider now hands Host a callback and Host drives construction. Author-driven callbacks are a
  shape this project has rejected before, in the north star's rule that format and media policy fragments
  must compose "without requiring a media author to drive a public policy-builder callback", and in the
  owner ledger's rejection of builder-shaped authoring. Choosing M3a means accepting a callback at the
  provider boundary specifically, and saying why that boundary is different from the policy one.
- **M3b · the wrapper carries the facts.** `CatalogResult<TItem>` holds titles, years, dates and the rest
  without a key, and Host constructs `TItem` from them.
  *Consequence:* this is the parallel candidate schema the roadmap refuses by name and §4 above restates.
  It is listed only so that it is refused explicitly rather than arrived at by accident, and because it is
  what "a wrapper" usually turns into once someone tries to write one.

**M4 · The item has no durable key at all; Host wraps it.** `MediaItemId` leaves `IMediaEntity` and
`MediaItem`, and Host-owned durable identity lives in a thin envelope around the exact media-owned item —
`Catalogued<TItem>(MediaItemId Id, TItem Item)` or similar — which repeats no fact the item already carries.
A provider returns `TItem` exactly as the three blocked members already promise, keyless, and the question
in this section's title stops being a question.
*Consequence:* it is the only mechanism that changes no provider signature and introduces no sentinel, no
callback and no second schema. It is also the largest public-contract change on the list, and the cost is
concrete rather than theoretical: `Key` leaves the common item and the entity floor; `MediaItemRef` is built
from the envelope rather than read off the item; the generated closed projections that read `Key` change;
every Host, API and Client site that reads `item.Key` changes; `IMediaItemSource` implementations in Movies,
Television, Music and Books change; and the reference Movies data, which currently uses a vendor identifier
as the key, has to say instead which envelope it is in. Whether an item that cannot state its own identity
is still the right domain object is the question this option asks, and it is a real one in both directions.

### 5.2 Policy — what is the durable key *of*?

**A3 · The natural key is the durable key.** `MediaItemId` stops being a surrogate; identity is the owning
cataloger's `(scheme, value)`.
*Consequence:* providers may legitimately mint, and the reference Movies data already behaves this way. But
durable library state is then welded to one vendor's namespace: an identifier redirect rewrites every stored
`MediaItemRef`, a second cataloger for the same kind cannot be authoritative, and swapping vendors is a
migration. It also contradicts `MediaItemId`'s stated meaning and the 64-bit-column rationale behind it.

**A4 · Host surrogate over an owned natural-key map.** Host keeps minting, and holds
`(MediaKindId, MediaLevelId, ExternalId) -> MediaItemId`, resolving or minting on first sight. A provider
never sees the surrogate.
*Consequence:* the only alternative under which the declared `IdentifierRedirects` capability can mean
anything — a redirect repoints one edge of the map and leaves every stored row untouched. It answers all
four sub-questions below cleanly. Its cost is a new Host-owned store with a persistence dependency
(G07B/G18).

### 5.3 Which combinations are coherent

| | A3 · natural key is the key | A4 · surrogate over a map |
|---|---|---|
| **M1** pre-resolved key | incoherent: `SearchAsync`/`FetchAsync` have no key to pre-resolve | same |
| **M2** absent until stamped | coherent; the sentinel is redundant under A3, since the provider knows the key | coherent |
| **M3a** factory wrapper | coherent, at the cost of a callback boundary | coherent, at the same cost |
| **M3b** fact wrapper | refused: parallel candidate schema | refused: same |
| **M4** durable envelope | coherent, though A3 makes the envelope's identity the vendor's | coherent, and the two fit best: the item owns no identity and Host owns all of it |

A3 also weakens the reason for a mechanism at all: if the provider legitimately owns the key, `Key` can stay
required and be filled from the natural key, and M2/M3a/M4 all become optional refinements rather than
requirements. That is the largest single consequence of the policy choice.

## 6. The four sub-rules, under each policy

| Sub-rule | Under A3 (natural key is the key) | Under A4 (surrogate over a map) |
|---|---|---|
| **Collision** — two natural keys, one item | Unrepresentable: two keys are two items, and a genuine duplicate stays duplicated | Two natural keys may point at one surrogate; one natural key pointing at two surrogates is a defect Host refuses |
| **Merge** — several catalogers describing one item | Not possible without choosing one vendor as identity owner | Authority order is read-time policy over one durable row, which is where the catalog/library split already merges |
| **Redirect** — `tmdb:123` becomes `tmdb:456` | Rewrites every stored `MediaItemRef`; library state moves or is lost | Repoints one map edge; stored rows never move |
| **Retry** — the same fetch runs twice | Idempotent by construction | Idempotent because minting is keyed by the natural key |

## 7. The smallest answer that unblocks G04

One sentence from the owner is enough to start. The options are the mechanisms above, in full — an answer
that names none of them leaves the same question open.

> When a cataloger returns an item the installation has not seen, its `MediaItemId` is:
>
> 1. **chosen by the provider** from its own namespace (selects A3; `Key` stays required and nothing else
>    has to change);
> 2. **absent until Host stamps it** — `Key` optional, zero meaning not-yet-durable (M2; leaves A3 or A4
>    open);
> 3. **supplied by Host before the call** (M1; requires the multi-result members to change shape, so it
>    should be chosen only deliberately);
> 4. **produced by a factory the provider returns and Host calls after minting** (M3a; accepts a callback at
>    the provider boundary); or
> 5. **not on the item at all** — the item is keyless and Host wraps it in a durable envelope (M4; the
>    largest public-contract change, and the only one that leaves the three blocked signatures untouched).

Any of 2, 4 and 5 still needs the policy answer (A3 or A4) before redirects can be implemented; option 1
settles both at once, and option 3 is a mechanism whose shape cost is paid in the catalog surface itself.

## 8. What was deliberately not done

- No candidate, draft or partially-materialized type was added.
- `MediaItem.Key` was not made optional and `MediaItemId`'s documentation was not softened; either edit
  would have decided this by omission.
- No Host seam was added that consumes a shaped cataloger result, because such a seam cannot exist without
  an answer.
- The `Arronix.Architecture.Tests.MovieCatalogerFixture` key fabrication was left exactly as it is. It is
  labelled in its own source as compile-time package-shape evidence only, and rewriting it would hide the
  gap this document exists to keep visible.
