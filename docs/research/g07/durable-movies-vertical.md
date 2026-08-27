# G07B — the narrowest durable Movies catalog vertical

**Status:** architecture preparation. No production code changed on this branch; the integration is Codex's.
This decides the shape of the slice, the rules its lifecycle obeys, and the places where the obvious
implementation would quietly freeze a decision that belongs to G13–G19.

**Branch:** `claude/g07b-durable-audit`
**Base commit:** `ce643da45` (`State the filter rule as it is implemented`)
**Reads:** `CONTEXT.md`, `INTERFACE.md`, `docs/design/typed-media-north-star.md`,
`docs/design/typed-media-roadmap.md` (G04, G05, G07B, G18, G19), `docs/owner-ledger.md` (O-22, O-23, O-40),
`docs/research/g04/media-item-identity.md`, `docs/research/g05/tmdb-provider-pressure-test.md`.

G07B exists to find out what is wrong with the current model *before* Television, Music and Books copy it.
Section 2 is therefore the most valuable part of this report: seven behaviours that are correct while
`CatalogIdentity` and `IMediaStore` are dictionaries and become defects the day they are rows.

---

## 1. The slice

Durable in G07B, and nothing else:

| Concern | Durable state | Owner |
|---|---|---|
| Configured provider definitions | `ProviderDefinition` rows, including orphan retention | operator |
| Catalog identity | catalog identifier → `MediaItemRef`, plus supersession | host |
| Catalog records | one media-owned item graph per held reference, plus presence and refresh stamps | cataloger |
| Library presence and monitoring | `LibraryFacet` at the library-entry level | user |

Explicitly **not** durable here, and not designed here: unit/file links, media file records, group
memberships, profiles and cutoffs, exclusions, operation and acquisition snapshots, import state,
per-kind coordinates, and any cross-media relational shape. G13–G17A must constrain those before G18
freezes them. Section 3.5 says how the store carries the existing in-memory file/link/group operations
without either persisting them or pretending they persist.

Provider *status* (the back-off ladder) is not in the slice either, but section 2.6 records that nothing
currently writes it on the catalog path, which the refresh leg has to fix whether or not it is stored.

---

## 2. What durability changes about code that already exists

### 2.1 Search assigns durable identity

`CatalogDispatcher.SearchAsync` calls `Materialize`, and `Materialize` calls `CatalogIdentity.Identify`.
Every row a catalog search returns is therefore given a `MediaItemId` — including the forty results the
user scrolls past. In a dictionary this is free. As a table it is an unbounded, monotonically growing
write path driven by search text, and the numbers it burns can never be reused (see 4.6).

The ledger's wording is `O-40`: Host assigns `MediaItemId` "if and when a catalog item is materialized
**into local library state**". `docs/research/g04/media-item-identity.md` records it as "when a catalog
item is materialized", and the implementation materializes search results. The three readings agree until
identity is durable and then they do not. This is owner question **Q1**.

Recommended resolution, which is also the narrowest one: searching and fetching *read*; only taking an item
into the library *assigns*. That needs one typed distinction, not a field bag — see 3.3.

### 2.2 Projection assigns durable identity, on the read path

`ItemProjector.Reference` resolves a reference-valued field — for Movies, each `MediaCollection<Movie>` in
`Movie.Collections` — by calling `identity.Identify(...)`. Rendering a browse page is currently a write
against identity state. Durably, every page render would insert group-level identity rows, from a read.

The rule the store needs is flat: **the read path never allocates**. Identity assignment for an item and for
every catalog reference it carries happens once, in the write transaction that stores the payload. A
reference the store cannot resolve projects with its title and its catalog identifier and no local handle,
rather than minting one. This needs `CatalogIdentity` split into a reader and a writer (3.1) so that the
projector cannot allocate even by accident.

Assigning identity to a group reference is identity, not a groups schema: G04 already scopes identity by
kind *and level*, and a grouping axis already occupies the level slot. No group table, no membership row,
and no axis semantics are decided by doing this.

### 2.3 A merge moves no library rows, and nothing canonicalizes

G04 states the limitation plainly: `CatalogIdentity.Canonical` resolves a superseded reference and moves
nothing, and "a caller must canonicalize before reading". There is no such caller. `Canonical` and
`TryIdentify` have zero production call sites; the only two callers of the type at all are the two
`Identify` calls in 2.1 and 2.2.

So today a merge is invisible: the superseded identity has no rows to strand because nothing persists.
Durably, a merge strands the user's monitoring, path and tags under a reference nothing will read again.
The slice therefore has to make merge a store operation and put the resolve step inside the store's read
path rather than in every caller — a caller that must remember to canonicalize is a caller that will
forget.

### 2.4 `ProviderDefinitionStore` and `CatalogIdentity` are separate singletons with no shared transaction

Add is one intent that touches three things: assign identity, write the catalog record, create the library
row. Refresh touches one. Merge touches identity *and* library rows and must be atomic across both, or a
crash between them leaves an item whose identity says "merged" and whose rows say otherwise. Two
independent in-memory singletons cannot express that; two tables in one unit of work can. Section 5 is the
transaction map this forces.

### 2.5 `HostItemSource` is the seam, and it is empty for typed kinds

`EngineRegistration` gives *every* validated definition a `HostItemSource` through
`DefinitionEngineCatalog.ItemStore`, and `MediaTypeBinder` binds it as the kind's `Items`. Movies is a typed
kind, so its catalog projection is that empty placeholder. Television, Music and Books register their own
`IMediaItemSource` and are untouched by this slice.

That is a clean narrow boundary and it is worth naming: filling `HostItemSource` from durable rows lights
up the existing generic browse path for typed kinds only, needs no per-kind item source, and does not
touch the three legacy kinds. The G07B exit gate's "`HostItemSource` no longer returns an empty placeholder
for Movies" and its "no temporary per-kind item source" are the same sentence read twice.

### 2.6 Nothing records a catalog provider's failure

`IndexerDispatcher` and `NotificationDispatcher` both call `ProviderStatusStore.RecordFailure` and
`RecordSuccess`. `CatalogDispatcher` calls neither. It reads `IsAvailable` in `AuthoritiesFor` and never
feeds it, so the cataloger back-off ladder is consulted and never climbed.

This matters for refresh specifically. A refresh sweep over a library is the first thing in this platform
that will call a cataloger in a loop, and without back-off a provider outage becomes a rate-limit ban.

### 2.7 `CatalogSchemeUnowned` currently means two different things

`AuthoritiesFor` filters by `_status.IsAvailable`, and `RequireAuthorities` turns an empty list into
`CatalogSchemeUnowned` — "No installed cataloger is the authority for '{scheme}'". When the only authority
is installed but backed off, that message is false and the error code names an installation problem the
operator does not have. The same conflation reaches the refresh leg through `FetchAsync` returning `null`
when a lease cannot be taken or every authority declined.

A refresh must distinguish **the catalog says this record is gone** from **no authority answered**, because
the first is a durable fact about the item and the second is a fact about this minute. See 4.4.

---

## 3. The durable shape, in typed C#

Names are indicative; the relationships are not.

### 3.1 Identity: assignment is a write, resolution is a read

```csharp
/// Resolves catalog identifiers and superseded references. Allocates nothing, so the projection and
/// browse paths can hold one without being able to grow the identity space.
public interface ICatalogIdentityReader
{
    ValueTask<MediaItemRef?> FindAsync(
        MediaKindId kind, MediaLevelId level, ExternalId catalogId, CancellationToken cancellationToken = default);

    /// The surviving reference, or the argument when it was never superseded. Chains are collapsed on
    /// write, so this is one lookup however many merges preceded it.
    ValueTask<MediaItemRef> CanonicalAsync(MediaItemRef reference, CancellationToken cancellationToken = default);
}

/// Assigns durable identity. Host-internal, and reachable only from a write transaction.
internal interface ICatalogIdentityWriter
{
    ValueTask<CatalogAssignment> IdentifyAsync(
        MediaKindId kind, MediaLevelId level, IReadOnlyCollection<ExternalId> catalogIds,
        CancellationToken cancellationToken = default);
}

/// The reference an entity is held under, and the references this call merged onto it. The caller needs
/// the second half: a merge that resolves identity and leaves library rows behind is G04's recorded
/// limitation, and it is this value that closes it.
public readonly record struct CatalogAssignment(
    MediaItemRef Reference,
    IReadOnlyList<MediaItemRef> Superseded);
```

`Identify`'s existing semantics are preserved exactly: converging identifiers merge onto the lower
assignment, every identifier the entity carries is bound, and the scope is always the whole
`(kind, level)` pair. What changes is that the result now says what it merged, and that only a writer can
call it.

### 3.2 Catalog records: typed in, typed out, opaque in between

Host cannot name `Movie`; it lives in a collectible plugin assembly Host does not reference. The store
therefore never sees an item type. The kind's own runtime — already the sanctioned kind-blind bridge, and
already the thing that projects an item — is the only code that turns bytes into the exact typed graph:

```csharp
public interface IMediaTypeRuntime
{
    // ... existing members ...

    /// Writes one of this kind's exact items to the representation the store holds.
    CatalogPayload Capture(object item);

    /// Reads a payload back as this kind's exact item type.
    /// Refuses a payload this contract cannot read; it never returns a partial graph.
    object Restore(CatalogPayload payload);
}

/// One media-owned item graph as the store holds it: opaque to the host, readable only by the runtime of
/// the contract that wrote it.
public readonly record struct CatalogPayload(ContractStamp Contract, ReadOnlyMemory<byte> Body);
```

This is the roadmap's own instruction for G18 applied one gate early — "typed ingress and queries;
serialized storage representations remain internal implementation details" — and it is not a field bag: the
payload is never keyed by field identifier, never queried by field identifier, and never partially written.
It is written whole and read whole.

`ContractStamp` must not be a new invention. G07.1 already publishes, per client-safe assembly, its
assembly identity and module version identifier, and G07.2 will agree a generated-metadata hash and a
projection-schema hash between server and browser. The stamp is those same values. One notion of "which
contract wrote this", used by the browser and by the store.

The serializer is likewise G07.2's generated, trimming/AOT-safe metadata rather than a second one. The
difference between the two uses is stated, not blurred: a wire payload lives for milliseconds and a stored
payload must survive a package upgrade, so a stored payload carries its stamp and a stamp the current
contract cannot read is a refusal with the item marked for re-fetch — never a silent partial read. **This
makes G07.2 a hard prerequisite for G07B, not merely an earlier gate.**

```csharp
/// What the platform holds about one catalog record, beside the media-owned graph.
public sealed record CatalogEntry
{
    public required MediaItemRef Ref { get; init; }

    /// The identifier in the owning cataloger's own scheme. Aliases and redirects resolve to this.
    public required ExternalId CatalogId { get; init; }

    /// Presence, not release stage. An upstream-deleted item is not a stage below Tba.
    public required CatalogRecordState State { get; init; }

    public required ContractStamp Contract { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public DateTimeOffset? RefreshedAt { get; init; }
}

/// Where the platform keeps the catalog records it has taken in — the counterpart of IMediaStore, which
/// keeps the half the user owns. It moves entries and payloads and names no media concept at all.
public interface ICatalogStore
{
    ValueTask<StoredRecord?> FindAsync(MediaItemRef reference, CancellationToken cancellationToken = default);

    ValueTask<StoredPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        CatalogEntry entry, CatalogPayload payload, CancellationToken cancellationToken = default);
}

public sealed record StoredRecord(CatalogEntry Entry, CatalogPayload Payload);
public sealed record StoredPage(IReadOnlyList<StoredRecord> Records, int Page, int PageSize, int TotalCount);
```

The store never holds a media object, so it needs no item type and no runtime: it executes the query
against the common floor of 3.6 and hands back payloads. Whoever holds the kind's runtime calls `Restore`
and then `Project`, and the erasure stays inside the bridge that already owns it.

One real integration detail follows from that, worth naming now rather than discovering late.
`HostItemSource` is constructed from a `ValidatedDefinition` alone — `DefinitionEngineCatalog.ItemStore`
is a `ValidatedDefinition → IMediaItemSource` factory — so it cannot reach the runtime that would restore
a payload. Either that factory gains the runtime handle, or restoring happens above the seam. It is a
small change; it is not a free one.

### 3.3 A catalog result is not a library item

The typed distinction 2.1 needs, and the only new vocabulary in this report:

```csharp
/// One catalog result the platform has not taken in. It carries the catalog's identity and, when the
/// platform already holds the item, the reference it holds it under. Producing one assigns nothing.
public sealed record CatalogCandidate<TItem>(
    ExternalId CatalogId,
    TItem Item,
    MediaItemRef? Held,
    CuratedEntryId? CuratedEntryId = null)
    where TItem : class, IMediaItem;
```

`SearchAsync` and `FetchAsync` return candidates. `MaterializedItem<TItem>` keeps its current meaning —
an item that has a durable reference because it was taken in — and is produced only by the coordinator in
3.4. This is the "exact typed lifecycle boundary, not a second field vocabulary" G04's exit gate allows.

### 3.4 The coordinator owns the transactions

```csharp
/// The platform's own copy of the catalog, for the items it holds. The only writer of durable identity.
public sealed class LibraryCatalog
{
    public Task<MaterializedItem<TItem>> AddAsync<TItem>(
        IMediaTypeRuntime kind, ExternalId catalogId, MonitoringScope monitoring,
        CancellationToken cancellationToken = default) where TItem : class, IMediaItem;

    public Task<CatalogRefresh> RefreshAsync(
        IMediaTypeRuntime kind, MediaItemRef reference, CancellationToken cancellationToken = default);
}

public enum RefreshOutcome
{
    /// The catalog answered and the record changed.
    Updated = 0,
    /// The catalog answered and the record is unchanged.
    Unchanged = 1,
    /// The catalog answered that it no longer holds the record.
    Withdrawn = 2,
    /// The identifier redirected onto a record the platform already held; the two are now one.
    Merged = 3,
    /// No authority answered. Nothing about the item changed, including its presence.
    NotAnswered = 4
}

public sealed record CatalogRefresh(
    MediaItemRef Reference,
    RefreshOutcome Outcome,
    MediaItemRef? MergedInto,
    string? Reason);
```

Five outcomes rather than success and failure, because 2.7 is a real conflation and the whole point of
`Withdrawn` is that it must never be reachable from a timeout.

`MonitoringScope` already exists and is used as it stands. Its `ItemAndGroup` member needs durable group
membership, which this slice does not have, so `AddAsync` accepts `None` and `Item` and refuses
`ItemAndGroup` with the reason. That is the narrow answer: the enum is not trimmed and no partial group
schema is invented to satisfy one member of it.

### 3.5 Library state, and the operations that stay where they are

`LibraryFacet` already carries presence, monitoring, path, root folder, tags and added-at, and
`InMemoryMediaStore` already enforces the monitoring-dimension and variant invariants against the declared
shape. The relational store reproduces those invariants; it does not restate them.

One operation is added, because 2.3 requires it:

```csharp
public interface IMediaStore
{
    // ... existing members ...

    /// Moves every row keyed by a superseded reference onto the surviving one. Called inside the same
    /// transaction as the identity merge that superseded it.
    ValueTask<LibraryMergeOutcome> MergeAsync(
        MediaItemRef superseded, MediaItemRef surviving, CancellationToken cancellationToken = default);
}

public sealed record LibraryMergeOutcome(
    MediaItemRef Surviving,
    LibraryFacet? Merged,
    IReadOnlyList<LibraryMergeConflict> Conflicts);

/// A user-owned answer that existed on both sides and could not be carried forward whole.
public enum LibraryMergeConflict { Path, RootFolder, SelectedVariant, Monitoring, AutoSwitchVariant, Tags }
```

The file, link and group-membership operations on `IMediaStore` keep exactly the behaviour they have
today. That is honest rather than lazy: they have **no production writer**. `MediaItemProjection` and
`CompletenessCalculator` read links and file records; nothing in the tree writes one. Persisting them would
mean guessing the schema G18 owns and G13–G17A have not yet constrained; declaring them durable when they
are not would be the silent fallback the exit gate forbids. So they stay in-process, the store says so on
its face, and an architecture test fails the day a production writer appears without the schema decision
having been made.

### 3.6 What a browse query may order and narrow by

Browse needs sort, filter and text search over a page. Two answers are wrong for this gate. A key/value
index over field identifiers is a field bag with a table around it. Loading every row and narrowing in
memory silently changes what a page means as the library grows.

The narrow answer: **index the facts the common item class guarantees for every kind** — title, year,
added-at, catalog presence, wanted state — because those are `MediaItem` properties, not Movies
properties, and indexing them freezes nothing per-kind. A query that orders or narrows outside that floor
is refused, visibly, naming the field, rather than returning a page that quietly ignored half the request.

Two related facts about the typed path, both currently invisible for want of rows. `ItemProjector` never
sets `ItemView.SortTitle` — only the legacy TV source does — so a typed kind has no sort title to order
by. And it sets `SortIndex = reference.Id.Value`, so a typed kind's natural order is its durable identity,
which is the order items were taken in. That is defensible, and it is another reason an identity freed by
a merge must stay retired (4.6): reissuing one would move an unrelated item in the list.

Which further fields become indexable, and whether that index is derived from each kind's compiled shape
at activation, is G18's decision. G07B's job is to hand G18 the requirement — the list of fields a real
Movies browse actually asked for and was refused — instead of guessing it. This is the single place where
the obvious implementation would freeze cross-media schema, so it is the place to be most deliberate.

---

## 4. Lifecycle

### 4.1 Search

Route by scheme, call every available authority, return `CatalogCandidate` values with `Held` populated
from `ICatalogIdentityReader.FindAsync`. No identity is assigned, no row is written, and a result the
platform already holds is visibly already held so the client does not offer to add it twice.

### 4.2 Add

One transaction: `IdentifyAsync` for the item and for every catalog reference it carries, `WriteAsync` for
the entry and payload, `UpsertLibraryAsync` for the user's monitoring scope and added-at, and — if the
assignment reported supersessions — `MergeAsync` for each. Adding an item the platform already holds is not
an error: it converges on the held reference and applies the monitoring scope, which is the same
idempotence G04 already requires of repeated fetches.

### 4.3 Refresh

Read the entry, fetch by its `CatalogId`, and write **only** the payload, the presence and the stamps.
`LibraryFacet` is not touched by a refresh, on any path. That is the structural form of "factual refresh
cannot overwrite user-owned monitoring/profile state": the two live in different rows with different
writers, so it is not a discipline that can be forgotten.

Record success and failure against `ProviderStatusStore`, as the indexer and notifier dispatchers already
do (2.6).

### 4.4 Deletion

`Withdrawn` is written only when an available authority answered and said it no longer holds the record.
Every other negative — no authority available, lease refused, transport failure, cancellation — is
`NotAnswered`, and leaves presence exactly as it was. The row and the user's state are retained in both
cases; withdrawal is a fact to show an operator, not a licence to delete what the user owns. This mirrors
the decision already made for orphaned provider definitions, and for the same reason.

`CatalogSchemeUnowned` must stop covering the backed-off case (2.7): an installed-but-unavailable authority
is a different sentence and belongs in `Reason`.

### 4.5 Redirect, collision and merge

A redirect is a fetch whose answer carries a different canonical identifier. `Materialize` already binds
the requested identifier alongside the canonical one, so the two converge in `Identify`. Durably that
convergence is a merge, and the merge is only complete when the library rows have moved.

The order inside the one transaction is fixed: assign (which collapses supersession chains and repoints
every alias to the survivor), then move library rows, then write the entry under the surviving reference,
then delete the superseded entry. Supersession rows are kept forever — a caller may hold an old
`MediaItemRef` indefinitely, including in a bookmarked URL — and the read path resolves through them, so
no caller has to remember to canonicalize.

Merging two rows the user owns is the one place in this slice where the platform must discard something a
person entered, so it is owner question **Q2**. The proposed default, applied by `MergeAsync` and reported
in `Conflicts`: the earlier `AddedAt` survives; monitoring takes the wanted answer on each dimension where
the two differ, because losing "I want this" is worse than gaining it; path, root folder and selected
variant keep the survivor's value and the discarded one is reported rather than acted on, because moving
files is G23's; tags are the union.

### 4.6 Restart

Identity survives, and an identity is never reissued. A merge frees a number and that number stays retired,
because library rows and API URLs are keyed by it. `MAX(item_id) + 1` is therefore wrong; the allocator is
an explicit per-kind next-value row updated inside the assigning transaction.

Provider definitions survive with their identifiers, their orphan state and their secret settings. The
existing sentinel round-trip in `SecretRedaction` already prevents an edit of a definition's name from
blanking its credentials; persistence must not create a second path that bypasses it.

---

## 5. Transactions

| Intent | One transaction over |
|---|---|
| Add | identity assignment · catalog entry + payload · library row · any merge the assignment reported |
| Refresh | catalog entry + payload + stamps |
| Merge | alias repointing · supersession row · library row move · entry move |
| Set monitoring | library row |
| Provider definition add/update/remove | definition row |

Two rules. A merge is never a second transaction after the assignment that caused it, or a crash between
them leaves identity and rows disagreeing with no way to tell which is right. A refresh never opens a
transaction that includes a library row, so it cannot write one by mistake.

Domain events (`ProviderDefinitionChanged` and whatever the catalog path raises) are published **after**
the transaction commits, never inside it. A subscriber that reads the store must not be able to observe a
state that is about to roll back.

---

## 6. Concurrency

- Correctness lives in the database, not in a monitor. `CatalogIdentity`'s `lock (_gate)` is process-local;
  the durable authority is a unique index on `(kind, level, scheme, value)`. Concurrent assignment of the
  same new identifier races, one insert wins, the loser re-reads and returns the winner's reference. That
  is the same idempotence the in-memory version gives, obtained from the constraint instead of the lock.
- `IdentifyAsync` becomes asynchronous. That is a signature change on a Host-public type with two call
  sites, one of which (`ItemProjector`) stops calling it entirely under 2.2.
- `LibraryFacet` writes carry a row version. Two concurrent user edits are last-writer-wins today and may
  stay so, but adding the column later is a migration and adding it now is free.
- A refresh and a monitoring change cannot conflict, because 4.3 puts them in different rows.
- `InMemoryMediaStore` serializes writes through one `SemaphoreSlim` because a span check needs an await.
  The relational store uses the database's concurrency control, as that file's own remark anticipates; the
  gate does not come with it.

---

## 7. Migrations and the package graph

**EF Core with LINQ and no SQL is owner signal** (O-22, O-23), and both are currently recorded as "not
built" / "recorded, not exercised". G07B is where they are first exercised, and the G07B exit gate repeats
the constraint as "no raw SQL, provider-shaped persistence API, silent in-memory fallback, or temporary
per-kind item source".

`docs/design/storage-layer.md` must not be followed as written. It predates the typed model and states
"**No EF Core** (hard contributor rule). Dapper + FluentMigrator + SQLite/Postgres", and its decision #20
chooses hand-written SQL. That is a direct contradiction of O-22 and O-23 and of the roadmap's own G18
wording ("the selected EF Core/LINQ-only persistence boundary"). Its *reasoning* remains valuable — the
catalog/library split, the retained-orphan rule, one `provider_status` table rather than five, no
cross-owner foreign keys, forward-only with a ledger — and G18's instruction to revalidate it should be
read as revalidating those conclusions against EF Core, not as adopting its mechanism. Codex should not be
left to discover the contradiction mid-implementation.

Concrete consequences for the package graph, which is locked and consumed-only:

- `Directory.Packages.props` gains EF Core and one provider, pinned to the same `.NET 11 Preview 7` train
  (`11.0.100-preview.7.26381.103`, `rollForward: disable`). The file's own rule — "a package enters this
  graph only with a consuming project and executable proof" — is satisfied by this gate and not before it.
- `Arronix.Host/packages.lock.json` is regenerated; `eng/ci/run-tests.sh` restores locked, so a stale lock
  fails the rail rather than resolving quietly.
- Host keeps its dependency direction: Abstractions, Common, Plugins. Nothing about EF Core reaches
  Abstractions, a media package, a provider package, or the Client.

Migration rules:

- Host owns the schema and its migrations. Forward-only; `Down` is not written and not required.
- Migrations are applied at startup, and a database whose schema is **newer** than the binary refuses to
  start rather than running against it.
- A media package's payload shape is not a database migration and cannot be, because Host cannot name the
  type. It is versioned by the contract stamp (3.2): a payload the current contract cannot read marks its
  entry for re-fetch, and the user's row is untouched because it never lived in the payload.
- Plugin-owned tables are out of scope. G07B must not ship the declarative plugin-schema mechanism
  `storage-layer.md` designs, because nothing needs it yet and shipping it would freeze it.
- No silent in-memory fallback. If the database cannot be opened or migrated, the host fails to start with
  the reason. `InMemoryMediaStore` stays a test double; the production composition registers the relational
  store unconditionally. The current `TryAddSingleton<IMediaStore, InMemoryMediaStore>()` in
  `StorageRegistration` is exactly the shape a silent fallback would take, and is the line to change.

---

## 8. API and Client

Most of this leg is already built, which is the point of doing it now.

**Browse needs no new endpoint.** `GET /api/v1/kinds/{kind}/levels/{level}/items` and its siblings already
run through `MediaItemBroker` → `IMediaItemSource` → `MediaItemProjection`, and already compose the
library half onto the catalog half. Filling `HostItemSource` from durable rows lights them up unchanged.

**Add and refresh need no new endpoint.** `StandardMediaAction` already names both, the standard
parameter vocabulary already carries `Identifier` for the item being added, and
`POST /api/v1/kinds/{kind}/actions/{actionId}` already dispatches through `IStandardActionDispatcher` —
which today answers `501` for everything except `SetMonitoring`. G07B gives that dispatcher two more
capabilities. Nothing new crosses the wire.

**Catalog search does need one endpoint**, because searching a catalog for something to add is not an item
query: it returns candidates that are not library items and may never become any. It assigns nothing
(2.1), and that has one honest cost worth stating rather than discovering: `ItemView.Ref` is `required`,
and `ItemProjector.Project` takes the reference as an argument and validates it, so a candidate with no
local identity cannot be projected as an `ItemView` at all.

The narrow answer is not a second item schema. `Project` already builds two things — the derived field map
and the reference-bearing envelope around it. Splitting it so the field map can be produced on its own
gives the candidate wire shape the same derived field vocabulary a browse row has, carrying the catalog
identifier and `Held` where a browse row carries `Ref`. One projector, one field vocabulary, two envelopes.
Group references inside a candidate resolve through the reader and may be absent, which is 2.2's rule
applied unchanged.

**Typed Client browse rides G07.2.** The Client already loads the exact contract assembly (G07.1) and
already discovers nothing by enumerating it. Typed deserialization and typed rendering of a Movie are
G07.2's deliverable, and G07B consumes them; it must not grow a second projection path to get a Movie onto
a page sooner. Combined with 3.2's use of the same generated metadata for the stored payload, **G07.2 is a
prerequisite, and the roadmap's current next task is already pointed at it.**

---

## 9. Tests and gates

The vertical proof, in one test: a real TMDb result becomes a valid `Movie`, is added, survives a genuine
process restart against a real database file, refreshes its provider-owned facts, and still carries the
user's monitoring answer. `PackagedTmdbProviderTests` already stands up publish-installed Video, Movies and
TMDb packages over a fake gateway with every other layer in production; this is that fixture plus a
database and a second host.

Behavioural cases, each named for the rule it defends:

| Case | Defends |
|---|---|
| Search returns candidates and the identity table is unchanged | 2.1 / Q1 |
| Rendering a page of movies with collections writes no identity row | 2.2 |
| Fetching the same identifier twice yields one reference and one row | G04 idempotence |
| An alias fetched after its canonical merges, and the library row moves with it | 2.3 / 4.5 |
| A merge with monitoring on both sides keeps wanted and reports the conflict | 4.5 / Q2 |
| An authority answering "gone" marks `Withdrawn` and keeps the row | 4.4 |
| An authority that is backed off, unleasable or throwing yields `NotAnswered` and changes no presence | 2.7 / 4.4 |
| A cataloger failure climbs the back-off ladder | 2.6 |
| Refresh writes no library row, proved by a store double that fails the assertion if it does | 4.3 |
| A payload written under a contract stamp the current package cannot read is refused, not partially read | 3.2 |
| An identity freed by a merge is never reissued after restart | 4.6 |
| A sort or filter outside the common floor is refused visibly, naming the field | 3.6 |
| Opening a database that cannot be migrated fails startup; no in-memory store is registered | 7 |

Architecture gates to add beside the existing `CatalogIdentityAuthorityTests`:

- No EF Core type is reachable from `Arronix.Abstractions`, any media or format package, any provider
  package, or `Arronix.Client`.
- No raw SQL: no `FromSql*`, `ExecuteSql*` or `DbCommand` construction in the Host source.
- No public persistence contract names a provider, a vendor, or a media concept.
- The file, link and group-membership operations have no production writer (3.5), so the day one appears
  the gate fails and the schema decision is taken deliberately.

`eng/ci/run-tests.sh` remains the rail: locked restore, one warnings-as-errors build, non-empty results
from every discovered project, and the exact skip ratchet. New skips are not how a durable slice ships.

---

## 10. Owner questions

Only two. Everything else in this report follows from signal that already exists.

**Q1 — Does a catalog *search* assign durable identity?**
`O-40` says Host assigns `MediaItemId` "if and when a catalog item is materialized into local library
state". `docs/research/g04/media-item-identity.md` says "when a catalog item is materialized". The
implementation assigns on search as well as on fetch. The three agree until identity is durable. Assigning
on search makes the identity table grow with search traffic and burns numbers that can never be reused;
not assigning needs the typed candidate/materialized distinction in 3.3 and the split projector in
section 8, because an unheld candidate cannot fill `ItemView.Ref`. Recommendation: searching and fetching
read, taking in assigns — the cost is one refactor of an existing projector, and the alternative is a
table that grows with search traffic for as long as the deployment lives.

**Q2 — When a merge finds user-owned state on both sides, what survives?**
This is the one place in the slice where the platform must discard something a person entered, and no
existing signal decides it. Proposed default in 4.5: earlier `AddedAt` wins; monitoring takes the wanted
answer where the two differ; path, root folder and selected variant keep the survivor's and report the
discarded one rather than moving files; tags are the union. Every discard is reported in `Conflicts`, so
whatever rule is chosen is visible rather than silent.

Two further things are worth the owner *knowing* without being questions. First, G07B is the first
milestone that writes an operator's credentials to disk, and the threat model has no at-rest item —
`SettingSensitivity.Secret` currently governs redaction on the wire and in logs only. Nothing here decides
encryption at rest; it is named so it is not decided by omission. Second,
`docs/design/storage-layer.md` contradicts O-22 and O-23 on its face (section 7) and needs a status header
saying so; this branch is architecture preparation and edits no document but this one, so that correction
travels with the integration.

---

## 11. Exit-gate mapping

| G07B exit gate | Where |
|---|---|
| A real G05 provider result becomes a valid Movie, survives restart, refreshes provider-owned facts, preserves user-owned state | 4.2, 4.3, 4.6, 9 |
| Identity collision, redirect, deleted-record and retry match the G04 contract | 4.4, 4.5; retry is read in both its senses — idempotent re-materialization (4.2) and provider back-off (2.6, 4.3) |
| `HostItemSource` no longer returns an empty placeholder for Movies | 2.5, 3.2, 8 |
| No raw SQL, provider-shaped persistence API, silent in-memory fallback, or temporary per-kind item source | 7, 9; the persistence contracts in 3 name a reference and a payload, never a provider, a vendor or an item type |
| Do not design cross-media groups, unit/file links, profiles, operation records or import state | 1, 3.5, 3.6 |
