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

## 5. The decision decomposes into two independent choices

Nothing can be built until both are answered, and they are genuinely separate.

### 5.1 Mechanism — how does a provider return an item whose durable key is not yet known?

**M1 · Host resolves the key before the call.** Host turns the natural key into a `MediaItemId` first and
supplies it to the provider, which stamps it.
*Consequence:* keeps the key host-minted and the item always fully valid. Works only for `GetAsync`, whose
argument is a single `ExternalId`. It cannot work for `SearchAsync` or `FetchAsync`, where the natural keys
of the results are unknown until the response arrives — which is the majority of real catalog traffic.
Rescuing those two would mean changing what they return, which is the one thing the typed pairing exists to
prevent.

**M2 · The key is absent until Host stamps it.** `Key` stops being `required`; `default(MediaItemId)` (zero)
means "not durable yet", and one Host-owned materialization step assigns the real value.
*Consequence:* one item type end to end, no wrapper, no second schema, and the sentinel already has a
precedent inside the platform (`IMediaStore.UpsertFileAsync`). The cost is that an item with key zero is
representable, so "a `Movie` that is structurally complete but not yet durable" becomes a legal value. The
owner has to say whether that is the "temporarily invalid domain object" the G04 exit gate forbids or the
ordinary before-state of an identity lifecycle. **This is the crux of the whole decision.**

**M3 · An exact typed lifecycle boundary.** The provider returns `CatalogResult<TItem>` (or similar) — the
item's facts plus its natural key, with the surrogate assigned on the way in.
*Consequence:* keeps `Key` required and every constructed `TItem` durable. It is only acceptable if the
wrapper carries the media type's own item and no restated facts; the moment it lists titles, years or dates
of its own it has become the candidate schema the roadmap refuses. It also changes three public signatures
and is the largest surface change of the three.

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
(G07B/G18), and it still needs M2 or M3 as its mechanism: the map explains *what* the key is, not *how* the
item travels without one.

The coherent combinations are **M2+A4**, **M3+A4**, and **M2+A3** or **M3+A3**. **M1** is coherent only with
a catalog surface that has no multi-result member, which this one does.

## 6. The four sub-rules, under each policy

| Sub-rule | Under A3 (natural key is the key) | Under A4 (surrogate over a map) |
|---|---|---|
| **Collision** — two natural keys, one item | Unrepresentable: two keys are two items, and a genuine duplicate stays duplicated | Two natural keys may point at one surrogate; one natural key pointing at two surrogates is a defect Host refuses |
| **Merge** — several catalogers describing one item | Not possible without choosing one vendor as identity owner | Authority order is read-time policy over one durable row, which is where the catalog/library split already merges |
| **Redirect** — `tmdb:123` becomes `tmdb:456` | Rewrites every stored `MediaItemRef`; library state moves or is lost | Repoints one map edge; stored rows never move |
| **Retry** — the same fetch runs twice | Idempotent by construction | Idempotent because minting is keyed by the natural key |

## 7. The smallest answer that unblocks G04

One sentence from the owner is enough to start:

> When a cataloger returns an item the installation has not seen, its `MediaItemId` is **(1)** chosen by the
> provider from its own namespace, **(2)** absent until Host stamps it, or **(3)** supplied by Host before
> the call.

(1) selects A3 and makes `Key` an ordinary provider-supplied value. (2) selects M2 and leaves the policy
choice between A3 and A4 open but answerable later. (3) selects M1 and requires the catalog surface to
change shape, so it should be chosen only deliberately.

## 8. What was deliberately not done

- No candidate, draft or partially-materialized type was added.
- `MediaItem.Key` was not made optional and `MediaItemId`'s documentation was not softened; either edit
  would have decided this by omission.
- No Host seam was added that consumes a shaped cataloger result, because such a seam cannot exist without
  an answer.
- The `Arronix.Architecture.Tests.MovieCatalogerFixture` key fabrication was left exactly as it is. It is
  labelled in its own source as compile-time package-shape evidence only, and rewriting it would hide the
  gap this document exists to keep visible.
