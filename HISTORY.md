# Arronix History

## 2026-08-28 — Start G08 with a generated classification inventory

The compatibility ratchet now emits an optional typed, versioned classification report only after ledger,
execution, required-sentinel and compiler-input validation all succeed. It removes a requested stale artifact
before reading new inputs and atomically publishes the complete replacement. The generated report is not a
second hand-authored matrix and cannot promote a baseline-only or ineligible source, a candidate divergence,
or a provisional or unresolved owner into proof.

Independent review accepted the implementation at `d02a7f9719e9a4ea98fcc57406e1ab056ad4125a`.
The full .NET 11 rail passed 3,645 enabled cases across 14 projects with exactly 302 registered skips and no
failures or inconclusive cases. Its report kept the 301 Movies omissions separate from the one architecture
governance case, projected all five zero-case evidence controls, and recorded the Movies disposition split as
158 restorations, 114 ownership-correct replacements, 20 evidence gaps and 9 candidate divergences. Those are
declared classifications, not compatibility closure; Movies ownership remains 187 cases provisional and 114
unresolved.

## 2026-08-28 — Close the durable Movies catalog vertical

Independent review accepted G07B at `e61fec49766e108d2410b37a1338cfe8cbb3f695`. The full .NET 11 rail
passed 3,638 enabled cases across 14 projects with exactly 302 registered skips and no failures or
inconclusive cases. A fresh detached worktree then reproduced the separate 46-check G07B browser proof:
search resolved but did not materialize the primary Movie and its referenced collection; visible Add created
one durable item; an API retry returned the same reference without changing durable state; visible monitoring
survived a provider refresh; and the refreshed provider-owned facts plus user-owned state survived restart.
Provider discovery, six read-only store snapshots and proof-owned process teardown also passed.

The same accepted candidate separately reproduced G07A at 37 checks and G07.3 at 30 server checks, 43 G07.2
browser checks and 64 lifecycle checks. G07B's 46, G07A's 37 and G07.3's 137 are distinct proof identities;
no combined count is claimed. Before the accepted rerun, the retained G07/G07A browser harness was corrected
to read the Client's canonical `loaded` and `alreadyLoaded` enum names rather than obsolete CLR ordinals.

## 2026-08-28 — Project admitted provider pairing into generic catalog discovery

Provider discovery now publishes a singular nullable `PairedMediaKind` and an admitted cataloger's nullable
`CatalogScheme` on `ProviderCatalogEntry`. The existing admission-validated item-type-to-kind map is consumed
once to produce those semantic values; Host routing, marker reading and provider filtering no longer retain
or compare contributing CLR types. Kind-filtered discovery therefore includes a separately packaged cataloger
when its exact closed pairing serves the requested kind, rather than guessing from plugin ownership.

The generic Client adds a descriptor-driven `/kinds/{kind}/catalog` workspace. It offers only active,
enabled definitions whose scope admits the selected kind, treats 201 versus 200 add responses as created
versus already-present state, and navigates/refreshes through complete item references. This advances the
G07B candidate but does not close it: retained browser acceptance evidence remains outstanding.

## 2026-08-28 — Record standalone G07B durable-core acceptance

Independent review accepted the standalone durable-core candidate at
`9b202eda8a472e1a14604e926f36094682718ca9`: the full rail reported 3,895 total cases, 3,593 enabled and
passed, zero failed, and 302 skipped; all three required tests and compiler-input provenance checks passed.
This does not close G07B after integration: its generic typed Client/browser proof must run through the
accepted G07.3/G07A harness, and final gate acceptance awaits that combined evidence. G07.3's 137 checks
and G07A's 37 checks remain separate proof identities.

## 2026-08-28 — Accept the package-only external consumer

Independent review accepted G07A at `8dd44db15ab1dba1b8f1e1e35c696a65b8f7e732`. The full .NET 11 rail
passed 3,526 enabled cases across 14 projects with exactly 302 registered skips and no failures or
inconclusive cases. A fresh disposable worktree then reproduced the 37-check external-consumer browser
proof and its private-domain-copy quarantine mutation. The unchanged G07.3 proof passed separately as 30
server checks, 43 G07.2 browser checks and 64 lifecycle checks. All proof ports were released and the
disposable worktree was removed.

G07A is therefore closed without combining its 37 checks with G07.3's 137. The retained external media,
provider and fixture-writer projects remain package-only consumers of the published SDK and domain packages.

## 2026-08-28 — Hold G07A teardown to the accepted owned-process rule

G07A's external-consumer proof now uses the accepted G07.3 lifecycle discipline for both its primary and
mutation server: it tracks only processes it starts, bounds TERM and KILL escalation, distinguishes a
zombie from a running child, retains a failed signal's handle for bounded retry, and rejects a proof port
that remains occupied. The fixture writer is also permanently checked as a package-only external consumer,
with its own configuration and pinned SDK. This strengthens only G07A's proof lifecycle; it does not combine
its 37 checks with G07.3's 137 checks or declare G07A accepted.

## 2026-08-28 — Port G07A after accepted G07.3

The package-only external-consumer candidate was ported onto accepted G07.3 without changing the lifecycle
browser harness or Client composition. `northmark.shorts` supplies the client-safe `ShortFilm` contract and
isolated media entry; `northmark.shorts.catalog` is separately restored from the published domain package,
then admitted as its typed cataloger. The retained proof holds the fixture bytes to the external domain's
generated serializer, validates package-only boundaries and the private-domain-copy quarantine mutation, and
uses the shared generic payload panel to render the external premiere, lifecycle/status and inline artwork.

Observed at this port: G07A browser proof 37/37. The unchanged G07.3 proof remains 30 server + 43 G07.2
browser + 64 lifecycle = 137, separately; it is never reported as 174. G07A remains pending independent
acceptance.

## 2026-08-28 — Accept the browser contract lifecycle at its exact candidate

Independent review accepted G07.3 at `4cfc0b92d97036a1524788f39db97efaa00eadc5`. The full .NET 11 rail
passed 3,509 enabled cases across 14 projects, retained exactly 302 registered skips, and reported zero
failures or inconclusive cases. The reviewer then changed reunion to compare CLR identity alone in a
disposable worktree and reproduced the recorded partial lifecycle result exactly: 26 of 64 checks executed,
4 failed — 3 direct terminal-behaviour refusals and 1 cascading navigation check — the run aborted on the
expected locator timeout, and 38 checks did not run. The disposable worktree and proof process were removed.

That acceptance closes G07.3. Its browser proof remains its own 137-check result — 30 server checks, 43
G07.2 browser checks and 64 lifecycle checks — and is not combined with the separate G07A external-consumer
proof.

## 2026-08-27 — Drive update, removal and a held tab through a real browser

G07.3's hermetic core was built and proved in the rail; its exit gate names update, removal, stale cache and
stale tab as exercises in a real browser, and none had run there. They have now, and this is an
implementation and proof candidate under independent acceptance review rather than a closed gate.

- **One context, one origin, three restarts.** The API is restarted over three installations composed on
  disk — video plus movies, video plus a rebuilt movies, video alone — while one `BrowserContext` and its
  tabs stay open. The tabs opened before a restart are never navigated again; they change only through the
  contracts page's own reload, which is the same serialized `ContractView` transaction the extension-state
  watcher calls. Reloading the document is the escape hatch this gate exists to say is the only cure, so
  using it would prove nothing, and the matrix asserts each tab navigated exactly once.
- **The update is a rebuild, not an edit.** The shared contract assembly is rebuilt alone with determinism
  off: identical CLR identity, identical projection schema, identical length, different module version
  identifier and different bytes. The deterministic rebuild afterwards must reproduce the first build
  exactly, and the two payloads are compared over the union of their file names and then over content, so a
  file added or removed cannot pass as "only the shared contract changed".
- **A held tab refuses a build it cannot use, and does not fetch it.** The refusal is `NameAlreadyResident`
  in preflight, so the page never asks for the new content address — asserted as an absence of requests, not
  inferred. It goes terminal, withdraws the projection it was already showing, loses the control to project
  at all, and still sheds the stale bytes while keeping the live ones. A fresh tab in the same browser then
  loads the update from the network *because* those bytes were evicted, and reuses the unchanged video bytes
  from the store.
- **A withdrawn package is reported, not silently kept.** The held tab stays compatible over what remains
  installed, reports Movies under "held here, no longer installed" attributed to a package this host no
  longer offers, serves nothing through it, and ends holding the live video hash alone. A fresh tab holds no
  orphan of its own.
- **Errors are attributed or they are defects.** Both channels of every page are captured. Nothing may be
  written outside a restart or the teardown, and inside one only a browser-issued `net::ERR_` transport
  failure is tolerated — narrow enough that an application failure cannot match. One mutation run
  demonstrated exactly that by failing on a WebSocket connection error the list had not yet named; the list
  names that form now, and still requires a `net::ERR_` cause.

**A defect this found.** The accepted base could not render the contracts page at all:
`ContractPayloadLoader` has an internal constructor and was registered by type, so the container found no
constructor and the page threw `NoConstructorMatch` at its first render — taking the whole G07.2 browser
proof with it, invisibly, because the browser half is not on the rail. The constructor stays internal and
the composition root, in the same assembly, constructs it by factory. A behavioural case resolves it and
asserts one singleton, which build-time container validation cannot do because it never calls a factory.

The proof scripts own their lifetimes rather than trusting one. The port must be free before anything is
built and is never cleared by force — a listener this proof did not start is refused, not killed. The
server is the assembly launched directly rather than `dotnet run`, whose child is what holds the port.
Shutdown is bounded at every step, distinguishes an exited-but-unreaped child from a running one, escalates
only against the exact owned pid, and treats a surviving process as a failure whatever the socket says. A
spawn failure arrives asynchronously and is observed rather than left to become an uncaught exception that
walks past cleanup. The handle survives a failed signal, because dropping it is what leaves a detached
server with nothing to escalate against, and both owners retry a bounded number of times. Every teardown
step is independently guarded and bounded, and any failure in setup, body, evidence or cleanup makes the run
non-zero. What that buys is bounded effort and a visible failure naming the pid, not a promise that nothing
can survive: a process the run may not signal at all keeps running, and the next run refuses to start while
it holds the port.

Mutation: reunion accepting any build of the same identity executes 26 of the 64 checks before the run
aborts on a locator timeout, and 4 of those fail — the 3 direct refusals of the terminal behaviour, plus one
cross-phase check cascading off the abort because only two tabs were ever opened; 38 never run, so the row
says the guard is load-bearing and nothing about the matrix beyond it. A sweep that evicts nothing fails 5
of the 64 with the matrix running to the end. Registering the payload loader by type fails the composition
case. Recorded
rather than dressed up: the loader's orphan gate is not distinguishable by this matrix, because `Admitted()`
is already gated by the report's package list — it is covered hermetically, and what the browser proves
about removal is the rendered consequence.

Observed: 30 server-half checks, 43 G07.2 browser checks and 64 G07.3 lifecycle checks, all green, with
machine-readable evidence under `artifacts/g07/evidence`. Full rail: 3,509 passed, 302 registered skips,
zero failed and zero inconclusive from 3,811 cases across 14 projects.
`docs/research/g07/client-contract-lifecycle.md` records what was proved, and the five things it does not
claim — including that no shipped server path raises `EventKind.PluginStateChanged` yet, so the event-driven
re-read stays proved in the rail rather than in a browser.

## 2026-08-28 — One narrow Movies catalog vertical becomes durable

**Decision.** The host keeps a catalog record for an item somebody explicitly added, in a local SQLite file
reached through EF Core, and reads it back through the media kind's own projector. A search resolves the
identity a hit would be held under and materializes nothing; an add is the decision that writes.

**Why the payload is the item's own serialization.** The alternative — a column per common fact — cannot
hold a media type's own values. `MovieReleaseTimeline`'s milestones are Movies-owned, and the common
contract deliberately refuses to flatten them into a universal date table, so a flattened schema would have
stored a Movie that came back missing its lifecycle. It would also have hardened a cross-media catalog
schema before G13–G17A constrained what such a schema must carry. The record therefore holds the item as
its own generated metadata writes it, with the writing build's hash beside it, and title and catalog state
as indexes over that payload so a page can be ordered and a withdrawn record found without reading every
row.

**Why the codec is carried rather than discovered.** Reading the contract entry point off an assembly's
attributes would have been discovery, and reusing that attribute as the platform's persistence object would
have made one declaration answer for two boundaries. Instead the client contract generator publishes a
second, hidden, inert holder per item type; the assembly declaring the media type names it, and the media
shape generator carries it onto `CompiledShapes`. The holder is public because which assembly declares the
media type is the author's choice, not this generator's — an `InternalsVisibleTo` from a shared domain
assembly to one named implementation assembly would have made the contract name its own consumer and broken
any other package layout. Two topology rules that pinned the movies domain's exported surface to exactly
three types were narrowly widened to admit that one generated registration SPI, and now assert its shape.

**What the two halves cost.** Identity convergence and record materialization are serialized behind one
admission gate: a merge that commits between an assignment and the write that depends on it moves the rows
existing at that instant, and a record written afterwards lands under an identity nothing resolves to. The
gate costs concurrency between catalog operations, which this vertical can afford; a silently unreachable
record is not. The transaction that records a merge now also re-keys the catalog record and library entry
onto the survivor, closing the G04 limitation that a merge moved nothing.

**Ownership.** A cataloger remains the sole identity authority; no curator identity was introduced. Two
catalogers that converge on one work produce one record, owned by whichever materialized it — being asked
second does not transfer ownership, and a refresh that discovers the merge continues through the surviving
record's own catalog rather than overwriting it, bounded to one handover.

**Credentials were not quietly moved to disk.** Making provider definitions durable would have written
every settings value into the file, TMDb's read access token among them. `SettingSensitivity.Credential`
and `Secret` already declare that such a value is never read back, and writing one to a local database reads
it back — so persisting them would have contradicted the field's own contract and invented an at-rest
protection story with no owner decision and no key management behind it. They are therefore not written.
A definition missing a value its provider declares required reads back as `Incomplete` rather than Active,
so nothing routes work to a provider whose settings are already known to be short, and a health check and a
message name what to enter again. Whether the platform should offer protected storage instead is an owner
decision this gate did not take.

**A limit worth stating.** The storage bridge is resolved from a referenced assembly, because one source
generator cannot read another's output in the same compilation. A media type declared beside its own item
therefore has no bridge and its catalog add and browse refuse loudly. That is the shipped movies topology,
so what is proved is durability for a package with a separate domain assembly — not for every otherwise
valid media declaration.

**What is not here.** Files, unit-file links, group memberships, profiles, operation records and import
state have no tables: later gates constrain those relationships, and a composed host refuses a write to one
rather than accepting it into memory. The browser half of typed Client browse is merge-dependent on the
G07.3/G07A harness and is not duplicated here. G07B is a candidate; independent acceptance is not claimed.

## 2026-08-27 — Read one serialized entity through the contract a browser admitted

G07.1 left a page holding contract assemblies whose bytes, CLR identity, build and universal-contract
reference had all been proved before the runtime saw them — and no way to read anything through them. This
is that read, and it is an implementation and proof candidate under independent acceptance review rather
than a closed gate.

- **The payload path is generic in both directions.** It takes one admitted contract — named by declaring
  assembly and entry point together — plus a path on the host that served the client. Only a verified
  contract deserializes. The Client's source spells no media kind, no media assembly and no installation's
  own path, including no default payload address, and an architecture rule holds it there; that is what
  lets the same unchanged panel take an external consumer's contract later.
- **An offer is a capability, and it is re-proved by contract object identity** before the fetch, after it,
  and again before any value is handed back, because a withdrawal can land at any of those points. Name
  equality is not equivalent: a contract with the same name from another installation is another contract.
  A projection already on screen is invalidated by the same rule.
- **A schema is read once and whole, when its contract is admitted.** Reading `declaration.Schema` once
  captures the root list and nothing else: every field's components and every field's choices are lists the
  contract still owns. Without freezing them, the graph hashed at admission, the graph a payload is proved
  against and the graph a page renders are three separate reads of objects that may answer differently each
  time, and the published schema hash does not necessarily cover what is rendered. The contract's own root
  objects are kept for one purpose — requiring a projected field to carry the descriptor it was admitted
  with — and everything else is a deep copy this client owns. One total covers a rendering, which is a
  schema plus one projection's values, so a projection continues from what freezing its schema spent rather
  than starting from a second full budget.
- **Reading a contract's list once is necessary and not sufficient.** The report, the renderer and the proof
  would each read the same objects again, so a list that answers safely while it is checked and returns a
  `javascript:` URL afterwards would reach the document. The projection proof therefore **captures what it
  proves as it proves it**, and what a consumer renders is that copy. Descriptor identity is still proved
  against what the contract returned: a dropped, reordered, duplicated or merely equal-cloned descriptor is
  refused.
- **One walk, bounded in every direction a contract-produced graph can be unbounded**: depth, a path-scoped
  cycle check, one node budget over every descriptor, value, item and choice charged before any list is
  iterated, and one character budget over the whole rendering — because 4,096 values times 65,536 characters
  is hundreds of megabytes out of a payload capped at four. A list that throws is a contained refusal.
- **`FieldValue` is held to being a real tagged value.** The kind matches its descriptor, exactly the
  permitted slot is populated, cardinality and component positions match the schema, an unknown enumerated
  value fails rather than displaying as arbitrary text, and absent is distinct from a present empty list —
  in the document as well as in the model, which the renderer had been throwing away.
- **A label is not a format.** Links are absolute http or https with no user information; artwork is that or
  a strict inline raster whose decoded bytes carry the container signature the address claims. Artwork stays
  a whole image throughout: role, address, width and height survive projection, the captured graph, the
  serialized proof and the rendered element.
- **A payload failure never rewrites the installation report.** The installation was admitted correctly
  whatever a document turned out to be, and downgrading it would lose the one fact still true. Failures are
  distinct outcomes rather than one "failed", and cancellation belongs to the caller.
- **A composite's parts are described by its components.** A list and a composite both arrive as items and
  are not the same thing; the renderer had passed the parent descriptor down, so an enumerated part showed
  its stored value instead of its declared choice and a counted part lost its unit.

The browser half is now driven rather than read by hand, and running it found two defects nothing else did:
a Blazor component with no parameters is never handed them again, so the payload panel read what could be
projected once — before the installation had loaded — and kept that answer; and the panel shipped a default
payload address. Both are fixed and re-proved. `docs/research/g07/client-payload-projection.md` records what
was built, what was observed, and what is not claimed.

## 2026-08-27 — Publish by deciding, and name an operation for what it acts on

Three corrections to the transaction that landed in `6d701b05f`.

- **A guard that had already advanced did not stop the caller it had advanced for.** `ContractView` asked
  `NewestWins` for the right to commit and assigned the snapshot on the next line — two atomics with an
  ordinary preemption window between them, not an await. An older transaction could take the right at
  sequence two, be preempted, be overtaken by a newer one at three that published and announced, and then
  resume and assign two over it: a page showing an installation it had already stopped showing, with the
  sequence guard reading three and permitting nothing to correct it. Deciding and publishing are now one
  compare-and-swap inside the holder, which returns the commit it published. The view has no snapshot field
  to assign — it reads what stands — so the split is not merely untested but unrepresentable. An
  announcement's refusals attach to the commit that raised them, matched by identity, or to nothing.
- **A rule that reads text was holding names that text cannot tell apart, and only where it looked.** The
  lifecycle rule scanned for `.ReadAsync(`, `.WriteAsync(`, `.ClearAsync(` and their kind, which an HTTP
  stream, a cache or a component loading its own data all carry — G07.2's payload loader reads an HTTP body,
  and the rule would have refused it the day that branch merged. It also required an open parenthesis, so
  handing `Store.ClearContractsAsync` on as a method group walked past it. The store's operations are named
  for what they act on now, the scan matches the member rather than the call, and both boundaries of a name
  are checked. A second rule holds the vocabulary itself against the generic names.
- **Two alternate doors were still open.** Nothing stopped a component injecting the loader and rendering
  its live `Report` beside a committed snapshot — one moment's installation next to another's stored keys —
  or an ordinary consumer reaching the transaction directly and changing what this page holds with nothing
  committing the result. Markup and its code-behind may not name the loader; the transaction is reached only
  from the view that shows what it produced, its own definition and the composition root. Ordinary code may
  still hold the loader to read back what an admitted contract declares, which is what typed projection
  needs and is not presentation state.

Ten new or renamed test methods contribute twenty-one executed cases beyond the preceding candidate: what
stands cannot regress when an older transaction resumes last, and everything left to that transaction is refused; every overtaken transaction is refused, not only
the one before last; refusals attach to the commit that raised them and leave its installation exactly where
it was; a transaction overtaken while announcing publishes nothing over the one that overtook it; the
lifecycle rule's matcher permits an ordinary `ReadAsync` and a declaration while refusing a contract read, a
method group and a `nameof`; its type matcher refuses `ContractReloader` and permits `NotAContractReloader`,
`ContractReloaderFactory` and `ContractReloadResult`; and a component's markup and its code-behind are both
what the markup rule reads. Two standing witnesses gained a barrier rather than a start: a second
transaction is now observed to have entered the reloader and read nothing, which is the lease holding,
because an async method runs synchronously until its first incomplete await.

Mutation: thirty-two in the standing set, each failing a named test. Two probes ran out of band: a
code-behind file reaching the loader is caught, while the compiling split that restores acceptance and
publication as separate atomics survives the test suite because expressing it requires reintroducing the
separate assignable snapshot field the design removed. That defect is prevented structurally, not by a
regression witness. Removing either lease now fails its witness on five consecutive runs rather than passing
on scheduling luck.

G07.3 is still open, and `docs/design/typed-media-roadmap.md` now says so accurately rather than "not
started": the hermetic core is implemented and proved in the rail, and update, removal, stale cache and
stale tab have not been exercised in a real browser.

## 2026-08-27 — One transaction, one record, one moment

The lifecycle core had one ordering authority for load and sweep, and everything around it was still free to
disagree with it. Each defect below is a boundary answering one question with another question's answer, or
with nothing at all.

- **A failure was the latest message, not a fact.** `ContractReloader.LastFailure` was one string: a read
  that failed was overwritten by a sweep that failed, and both by whichever subscriber happened to refuse
  last. `Announcement` returned only its final refusal, so three broken consumers were indistinguishable
  from one, and `ContractView` did the same to a refresh failure. Each contained failure is now a
  `ContractFailure` value naming the step it happened in, kept whole and in occurrence order. A
  process-unsound failure still propagates rather than being filed beside them.
- **A record was still being written while its readers read it.** The reload published its failures,
  announced, then appended the announcement's refusals — so a consumer refreshing synchronously inside that
  signal held a value the reloader then replaced, permanently. Records are now sealed before anyone is told.
  The boundary is stated rather than fudged: what a transaction did is inside the value it hands over; a
  subscriber refusing the notification that delivered it is reported beside it, because that refusal happens
  strictly after the subscriber was handed the value. No announcement can carry the record of its own
  refusals, and pretending otherwise is what produced the race.
- **What a page showed came from three moments.** `ContractView` published `Report` and `StoredKeys` from
  one refresh but answered `LastReloadFailure` by reading the reloader live, and the page read each of them
  off the view separately, several times per render. What a view shows is now one immutable value, assigned
  once and never re-published, and the page captures that one reference at the top of its render.
- **"Every caller goes through the reloader" was a convention.** `MediaContractLoader.LoadAsync` was public
  and the page held the store directly, so a load could fetch and write bytes that a sweep already running
  against an older installation would then evict, and clearing the store ran beside a reload rather than
  under it. Reading, sweeping and discarding are now one lease with no second door, and an architecture rule
  holds every operation that changes what this page holds — and every store read a page could pair with an
  installation — to the layer that owns it. The rule names operations rather than types, because typed
  projection will read what a loaded contract declares through the same loader and reading what this page
  holds is not changing it.
- **The lifecycle had two signals and two refreshes for one change.** The loader announced its report
  before the sweep, the reloader announced again after it, and a view refreshing on either could read store
  keys mid-transaction and pair them with an older record. There is one signal now, and it is the view's
  own. A transaction is numbered under the lease that runs it, seals its report, its post-sweep keys and its
  failures as one value, and hands that back to its caller; the view commits by sequence, so a caller the
  scheduler resumed late cannot land an older installation on newer state. The loader raises nothing and
  the view subscribes to nothing.
- **Two notifications were still discarded tasks.** `ContractsPage.OnChanged` dropped
  `InvokeAsync(StateHasChanged)`, and `ContractStateWatcher.OnReceived` dropped its reload while its own
  documentation claimed it contained everything but an unsound process — it contained nothing; it dropped
  the task carrying one. Both are explicit observing `async void` boundaries now: every boundary here
  records the failures it contains, so the only thing a dropped task could carry away was the class
  contained nowhere.

Nine cases beyond the ones already standing: two transactions that cannot overlap, ending with the store
holding what the newest installation names and each sealed over its own keys; a discard that waits for the
transaction in front and seals the same installation without reading one; every failure of one transaction
kept in order; an unsound process propagating; an overtaken transaction committing neither its keys, its
installation nor its failures; a subscriber reading the snapshot this signal announces while three of them
report two refusals in order; a refusal that cannot land on the transaction that overtook it; a reload and a
discard each committing what they produced; and a fatal failure from an event-driven reload arriving at the
boundary's own synchronization context rather than on a task nobody holds. Four source rules stand beside
them, because this solution has no component-test harness: every lifecycle operation is called only from the
layer that owns it, no boundary in the contract path discards the task it starts, a page injecting the view
reads its snapshot exactly once and nothing else off it, and the client still discovers nothing by
enumerating a loaded assembly.

Mutation: twenty-two, each failing a named test — keeping only the last refusal, reordering them, raising
the delegate whole, dropping a contained failure, sealing keys before the sweep, numbering every transaction
alike, running a discard beside a reload, committing an overtaken transaction, recording refusals without
asking whether one was overtaken, announcing before committing, weakening the ordering guard, dropping the
loader's orphan computation, restoring either discarded task, reading the view twice in one render, and a
page reaching each of the seven store and loader doors directly.

Recorded rather than dressed up: the reloader's containment around the read is defence in depth. The loader
turns every ordinary failure into an outcome in its own report, so nothing in this suite produces a
`Load`-stage failure, and that branch is stated rather than witnessed. The two page rules are source-shape
rules for the same reason — there is no component-test harness to render through.

G07.3 is still open: its exit gate names update, removal, stale cache and stale tab as exercises in a real
browser, and none has run.

## 2026-08-27 — Stop serving a contract this host has withdrawn

A page that loaded Movies and then had Movies uninstalled reported itself fully compatible on its next
read, showed no trace that Movies had ever been there, and answered `Find("Arronix.Media.Movies")` with a
live assembly. `RequiredAssemblies` is built from the packages the *current* manifest lists, so a withdrawn
name was never preflighted, never re-committed, never flagged and never rendered — and `Find`'s two
conditions, "the last pass could project" and "the dictionary holds it", were both still true. That is the
failure G07.3's exit gate names, except worse than a rendering bug: nothing downstream could detect it.

- **Residency and currency are now separate facts.** A resident entry whose simple name drops out of the
  installation just read is marked orphaned. It stays — a browser cannot unload — and stops being served:
  `Find` and `ContractsOf` both refuse it, through one gate rather than two copies of the same condition.
  It is reported under its own heading instead of vanishing with its package's panel, carrying the owning
  `PluginId`, name and version captured while it was still admitted, plus what that identifier means to the
  host *now*: withheld with the host's own live reason, still offered without this file, or not mentioned
  at all. The host keeps no history, so those three are everything a client may honestly say, and the
  sentence an operator reads is derived at the rendering edge rather than stored twice.
- **An orphan is not a fault.** `CanProject` still means "everything currently required is resident", and a
  removal elsewhere in an installation must not make the rest of it incompatible. The fix had to avoid
  trading a silent-serving bug for a false alarm, so it lives entirely in what `Find` answers and what the
  report shows.
- **Reunion and replacement stay different.** A package that returns at the exact content hash, identity,
  module, length and declarations this page holds is reused without a byte fetched. One that returns under
  any different description is terminal `NameAlreadyResident` — the same outcome as replacing an assembly
  in place, because a stale resident assembly is stale whichever path produced it.
- **`410`, `404` and a transport failure are three answers.** The byte route already computed the
  distinction and `GetByteArrayAsync` threw it away. `410 Gone` is now `Superseded` and says the address
  moved and a manifest re-read recovers; `404` is `NotOffered`; anything else stays `Unavailable`. None is
  terminal: an address this page never held cannot make it unable to satisfy the installation.
- **Eviction is store-only.** A janitor discards content hashes the verified installation does not name,
  using the primitives the store already had. It cannot touch residency, and it refuses to run against a
  report whose manifest was never proved whole — an empty package list from an unreachable host is an
  absence of knowledge, and sweeping on it would make every failed fetch a cold start.
- **A connected tab re-reads for itself, and sheds bytes while it is there.** One subscriber, on
  `EventKind.PluginStateChanged` only, calling the loader's own already-serialized `LoadAsync` and then the
  janitor. A trigger, not a second manifest reader: this client keeps one description of an installation and
  one place that proves it. Ahead of it, an unchanged `InstallationHash` decides whether to look and never
  decides what is true — every required assembly must still match the description the fresh manifest states,
  over values already in memory, because the published closure hash covers identity and content and not what
  a payload declares.
- **One load is one transition.** The orphan and owner bookkeeping a pass computes is applied only with the
  report it publishes. Applying it first — as the first cut did — paired the previous report with the next
  installation's residency whenever a caller abandoned a load mid-fetch: a page still claiming to be
  compatible while `Find` had already changed its mind about which names it serves. A completed load now
  raises a change notification too, because the consumer showing a report is rarely the one that asked for
  it, and a page that snapshots it once would render an installation the loader has already stopped serving.
  The watcher raises its own signal after it has swept, on success and on a contained failure alike: the
  loader's notification arrives before the sweep, so a consumer refreshing on that alone would read an
  evicted address as still held with nothing afterwards to correct it. Its three steps are contained
  separately, and it tells each subscriber in turn: an observer refusing one step is that observer's defect,
  not a reason to keep bytes this host no longer names, to withhold the signal, or to deny it to the next
  subscriber. And because two notifications arrive per reload and their store reads do not complete in the
  order they started, only the newest refresh commits — an older read landing last would put the evicted
  address back on the page.
- **One ordering authority, not one per caller.** The loader serializes its own reads and nothing else, so a
  sweep computed from an older read could finish after a newer one and evict exactly the addresses the newer
  installation had just fetched. Two entry paths reach it — an operator's button and a tab reacting to an
  extension changing state — and a gate private to either would have left the race between them. One
  `ContractReloader` now owns load, sweep and announcement as a single serialized transaction, and both
  callers go through it. The announcement is inside the lease: releasing first would let the next reload
  read, sweep and record over the one still telling its subscribers, so consumers would learn of two
  installations out of order. The watcher is a filter again and owns nothing.
- **What a view shows is one type, and one observation.** The page had been holding the coordination: two
  subscriptions, a generation guard, a refresh sequence and a task nothing awaited, none of it reachable by
  a test because the solution has no component-test harness. `ContractView` owns it instead — the page
  injects it, renders it and subscribes once. A refresh reads store and report as one observation and
  commits it only if nothing overtook it, so an older read cannot put back addresses a newer one saw
  evicted, cannot pair its keys with another moment's report, and cannot answer a failure the newest
  refresh already stated. Its own refresh is observed rather than dropped, so an unsound process is
  rethrown instead of vanishing, and one rule for telling subscribers is now shared with the reloader.
- **Cancellation is the caller's token and nothing else.** A request timeout arrives as the same
  `OperationCanceledException` and had been propagating as an abandoned load, leaving the previous report
  standing as this page's description of an installation it had just failed to read. It is now an ordinary
  `Unreachable` or `Unavailable` outcome. Beside it, no boundary in the client's contract path contains an
  exhausted heap or stack, corrupted memory or a structured native failure any more, and the watcher — which
  has no caller to return anything to — states every failure it does contain rather than swallowing it.

Twenty-two cases: orphaning with its refusal and its attribution, reunion with the same declaration instances
and no fetch, the withdraw-then-replace terminal, the three withdrawn-address answers, selective eviction
with live-hash preservation and the unreadable-manifest refusal, the state-change filter with its eviction,
its disposal, the serialized transaction across both entry paths, its in-lease announcement, its refusal by
an observer and by a subscriber, the view's overtaken-refresh discard, its refusing subscriber and its
reload, the newest-wins guard itself, plus two witnesses for abandonment —
a timeout that becomes an outcome, and a canceled load that leaves residency and the report describing one
installation. Mutation: dropping
the orphan flag from `Find`, trusting the installation hash without checking what was published, skipping
reconciliation, applying the bookkeeping before the cancellable work, dropping the watcher's sweep, and
announcing before it, letting a refused notification abort it, accepting an overtaken refresh, announcing
outside the lease, dropping serialization altogether, committing an overtaken refresh's failure or its keys,
and swallowing a subscriber's refusal each fail. Removing the early-out
*entirely* fails nothing, which is recorded as debt rather than dressed up — the reuse path it precedes
already returns before any fetch or hash.

G07.3 is not closed. Its exit gate names update, removal, stale cache and stale tab as exercises in a real
browser; this is the hermetic core they will be driven through.

## 2026-08-26 — Stop charging for the whole telemetry stream to shape your own events

`ITelemetryEnricher` and `ITelemetryEventFilter` were gated by `Capability.TelemetrySink`, which is the
privilege of reading every telemetry event in the process — every extension's and the host's — and which
implies the network privilege and needs an operator's approval. An enricher shapes the events its own
extension raised; a filter suppresses them, and cannot touch anything at error severity or above. Charging
the first price for the second privilege is the opposite of least privilege, and the public
`IPluginRegistry` documentation still described both as ungated, so the contract disagreed with itself as
well.

- **`Capability.TelemetryProcessing`, wire name `telemetry-processing`.** It gates the enricher and the
  filter, implies nothing, and needs no operator grant. `TelemetrySink` keeps its meaning exactly: the whole
  post-redaction stream, the `network` implication, and the operator naming the package in
  `Arronix:Plugins:TrustedSinks`.
- **`CapabilitySet` widened before it ran out.** Sixteen bit positions with fifteen capabilities in them is
  not the state to add a capability in, so the mask is 32 bits, its bounds are read from the vocabulary
  rather than written beside it, and both `Of` and `Has` are held to exactly the declared values — a shift
  count C# masks would otherwise let ordinal 32 answer for `Indexing`.
- The proofs are the three that matter: an extension holding only the narrower privilege registers its
  enricher and filter and is refused a sink; one holding only the sink privilege is refused an enricher;
  and a package declaring `telemetry-sink` is still quarantined before it runs a line unless the operator
  named it.

`docs/design/unified-host-runtime.md`, `docs/open-decisions.md` B1 and `docs/design/threat-model.md` are
brought into line. The threat model keeps its original T-02 recommendation — gate both under
`telemetry-sink` — and records beside it why the implementation split the grant instead.

## 2026-08-26 — Give the platform the five services a running host actually needs

A production Arronix could not host an installed extension at all. `PluginPlatformServices` required
`ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`, `IHostRuntimeInfo` and `IOperatingSystemInfo`, and
none had an implementation outside the test projects, so a real server quarantined Movies before it
contributed anything. All five are now real, and `Arronix.Api.Tests` proves it through the ordinary
`Program` over independently published Movies and Video packages staged beside the server: the active set
is exactly `{arronix.format.video, movies}`, `/api/v1/kinds` answers `movies`, the
client manifest names both packages by identifier with each package's own closure, every offered file
hashes to its published content address, and a sink registered after `AddArronixHost` receives telemetry.

- **Caching.** One provider, namespaces per package, and a release that genuinely lets go: a released
  namespace drops its values, factory delegates and constructed generic types, and a collectible context
  that filled one unloads while the provider is still alive. Disposal refuses new namespaces and
  resolutions, closes every gate, waits for every admitted factory and refresh, and only then clears — it is
  joinable and idempotent rather than a truncation documented as a compromise. Expired entries stay counted
  until a sweep removes them, and a self-refreshing cache with no lifetime still fetches once, because
  contents never loaded are stale under any reading.
- **Host and operating-system facts.** What the host can establish, and nothing else. `StartTime` is
  nullable rather than composition time wearing a process's name, `IsUserInteractive` is
  `Environment.UserInteractive`, and `IsWindowsService` means the service control manager specifically —
  the Host registers a detector built on `WindowsServiceHelpers` ahead of Common's fallback. Container
  evidence is read for what it says: `/libpod` and `/docker` name their runtime, `/containerd` and
  `kubepods` say a container and no more.
- **Events.** Fan-out with host handlers first, most-specific contract first, then each extension's
  handlers by exact runtime type. An extension subscribes to types its own package owns — proved against
  the entry assembly plus its published contract assemblies — or to a closed platform allow-list; absent
  ownership refuses a non-platform subscription rather than permitting it. A handler's failure is reported
  as host-owned text, cancellation is checked between handlers rather than only before the first, and a
  type reaching collectible code is never cached.
- **Telemetry.** The order is structural: snapshot, cap, render and redact on the caller's thread, then a
  pump that enriches, filters, logs and delivers. Redaction is additive, owner-qualified, non-backtracking
  and time-bounded, and an extension's rules are admitted transactionally with its package. An extension's
  enrichers and filters see only the events it raised; contributing a sink needs the capability and an
  operator naming it in `TrustedSinks`. No live exception ever leaves the host boundary.
- **Lifetime.** One ownership path: from the instant a Host activation succeeds the object is on the
  package's reverse-activation list, so no successfully constructed object exists unowned, and a retained
  failed attempt roots the exact lifetime rather than a receipt that names it. Teardown is a containment
  boundary in both directions — a hostile disposer, and a failure that will not describe itself, are
  recorded and stepped over rather than allowed to abandon the packages behind them.

Every central guard is mutation-checked: removing it fails a named test. WP-T2's telemetry findings (T-02,
T-09, T-11, T-12) are closed; T-18 quotas and T-19 host-fact disclosure remain open and are recorded in
`CONTEXT.md`.

Full rail: 3,141 passed, 302 registered skips, zero failed and zero inconclusive from 3,443 cases across 14
projects. Operator proof `eng/proofs/g07-client-contracts.sh`: 23 checks, all green.

## 2026-08-25 — Assert non-residency, and hold a refusal's file names to a declaration's shape

Three narrow corrections to the G07.1 close.

- The runtime-disagreement case asserted that the loader would not hand either assembly out, and the commit
  message claimed neither was resident. `Find` returning null says only the first. It now asserts residency
  directly for both, and the test's runtime seam answers wrongly on the first load and ordinarily
  afterwards, so a commit that continued past the disagreement would genuinely load the next assembly and
  fail the assertion.
- `ClientContractRefusal.UnadmittedFiles` documents bare file names and arrives on an untrusted manifest,
  and the validator only checked for blanks and duplicates. A refusal is rendered to an operator, so a path
  there is a path this client would print as though a package had declared it. Non-bare entries are now
  refused, by the same rule the declaration side uses.
- Renumbered the residual gaps in the research note, which skipped from 2 to 4 after an earlier gap was
  resolved.

Full rail: 2,875 passed, 302 registered skips, zero failed and zero inconclusive from 3,177 cases across 13
projects.

## 2026-08-25 — Close G07.1 and stop a refusal from mixing two kinds of name

- **`ClientContractCatalog` takes only the registry** and derives the publication gate from it. A catalog
  could previously be handed a gate guarding a different registry, which is a read that looks synchronised
  and is not; no assertion about one read can show the gate was the right one, so the second parameter is
  gone and the composition asserts the single-parameter shape.
- **The post-load disagreement branch has a real regression.** The load goes through an internal seam whose
  production value is `AssemblyLoadContext.Default.LoadFromStream`. A test supplies a runtime that honestly
  returns a different already-loaded assembly, and the existing single commit narrative now proves the whole
  shape: terminal, the first entry `RuntimeRefused`, a later verified entry still `Verified`, and nothing
  resident or projectable. It is no longer a branch defended only by mutation.
- **`ClientContractRefusal` stopped mixing two kinds of string.** `MissingAssemblies` carries admitted
  assembly simple names a facet binds and cannot reach; `UnadmittedFiles` carries declared file names the
  installation admitted nothing for. `CausedBy` became a list, because a package can lose several required
  facets at once and naming one sends an operator to fix a fraction of the problem.
- **Removed the unused `MediaContractLoader.Loaded` event**, which invoked external code while the loader's
  semaphore was held.

**G07.1 is complete.** Its exit conditions were about publishing, serving, verifying and loading a
client-safe contract package in a real browser, and the browser matrix proves all of them against the client
an ordinary `dotnet publish` produces. That the *trimmed* client cannot start on .NET 11 preview 7 is
technical debt, recorded in `CONTEXT.md` and in the research note; it predates this work and is not grounds
to reopen the gate. The roadmap's next task is the five platform services no production host implements —
`ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`, `IHostRuntimeInfo`, `IOperatingSystemInfo` —
without which a running Arronix cannot host the extension it ships with, and G07.2 follows.

Full rail: 2,874 passed, 302 registered skips, zero failed and zero inconclusive from 3,176 cases across 13
projects.

## 2026-08-25 — Prove a contract before the browser runtime can ever see it

The first pass of this loader verified after loading. It called `LoadFromStream`, then compared the loaded
assembly's identity and module identifier against what the host published — which in a browser is one
irreversible step too late, because the default load context cannot unload. A payload whose content hash was
right and whose declared identity was a lie would have been resident for the life of the page, with a warning
printed beside it.

- **Verification moved in front of the runtime.** `ContractMetadataReader` reads the declared CLR identity,
  the module version identifier and the universal-contract reference straight out of the PE metadata of the
  exact bytes, alongside their length and SHA-256. Nothing resolves a type or runs a class constructor.
- **The whole closure is verified before the first load.** A closure is not a list of independent downloads:
  admitting a dependency and then finding its dependant wrong leaves a page holding half an installation it
  can never complete. Only if every required member passes does anything load.
- **Verified is not loaded.** An entry that passed and was deliberately not loaded reports `Verified`.
  Reporting it as loaded because it could have been is the one lie a report like this must not tell, and it
  matters most in the two cases where it would be told: an all-or-nothing refusal, and a commit that stopped
  part-way.
- **There is still a post-load proof, and it is a different question.** Preflight establishes what the bytes
  declare; only the runtime can say what it did with them. After the load, the assembly's `FullName` and
  module identifier are compared with the published ones and its contract reference is resolved through the
  default context and required to be `ReferenceEquals` to this client's own. Only then does the entry become
  `Loaded`.
- **Compatibility became all or nothing.** `CanProject` is true only when every required assembly is
  resident, and `Find` answers nothing otherwise. Diagnostics stay visible; "compatible" now means safe to
  project and nothing else.
- **The manifest is treated as untrusted input.** It is validated whole before any field is acted on —
  including before the contract-identity refusal, which renders the packages. Duplicate package identifiers,
  duplicate assembly simple names, a closure that omits or misorders its own package, a member the host did
  not publish, unsafe file names, non-SHA-256 hashes, an identity whose simple name disagrees with the
  assembly name, and self-blaming or dangling refusal causes are each one whole-installation refusal.
  `PluginId` now travels through the wire and report models, and the client's converter refuses unreadable
  identifier text rather than defaulting it to a value that compares equal to every other unreadable one.
- **Withholding is a fixed point, not a pass.** A single pass let the package examined first survive with a
  closure a later withdrawal had already emptied. The rule now runs until nothing changes, orders
  requirements canonically so a closure hash describes the graph rather than a declaration order, and
  withholds a dependant of a withheld facet even when its own assemblies bind nothing from it — "it does not
  bind that today" is a property of the build, not of the dependency.
- **Withheld facets stopped being a silent side channel.** They are published on the manifest, rendered on
  the contracts page, and reported by a health contributor, each naming the package whose withdrawal caused
  it. A withheld facet is invisible from a browser by construction, so the host has to say it.
- **The catalog reads one Active snapshot under the publication gate** and copies bytes before releasing it,
  so a lease cannot be disposed between deciding what to serve and serving it.

The published client's own failure was attributed properly rather than assumed. Exporting the pristine base
commit and publishing it under the same SDK shows it fails trimmed and starts untrimmed, on a fresh origin
with no service worker; a default template application publishes and starts trimmed. So trimming is the
variable and the defect predates this work. `Arronix.Client.csproj` now sets `PublishTrimmed=false`, because
a property that lives only in a proof script leaves the artifact somebody deploys different from the one
anybody tested — and G07.1 is recorded as in progress, not complete, while that debt stands.

Proved in a real Chromium against the ordinary published client the API serves from one origin: a clean
store loads over the network with length, hash, identity and build all agreeing; a warm store reuses the
bytes with no byte request at all; and a foreign contract line, one flipped byte, a falsified identity, a
falsified build and a malformed manifest are each refused with nothing loaded and nothing projected.

Full rail: 2,868 passed, 302 registered skips, zero failed and zero inconclusive from 3,170 cases across 13
projects.

## 2026-08-25 — Give a browser the exact contract assembly the host admitted

G07 asked for typed media loading in Blazor as one gate: identity and cache protocol, real serving, real
browser loading, typed deserialization and rendering, and update/removal/stale behaviour. That is not one
coherent outcome, so it was split into three numbered sub-gates before any code was written — G07.1 publish
and load, G07.2 generated metadata and typed rendering, G07.3 cache lifecycle — and only G07.1 is claimed.

- **A package declares a client facet.** `plugin.json` gains `clientContracts`, validated as a subset of the
  package's own published shared contract assemblies. An entry assembly cannot appear in it, because it is
  already refused as a shared contract one rule earlier, and neither can a file the package does not publish:
  a browser receives the identity this installation admitted once, or nothing. Declared and never inferred,
  for the same reason the shared list is — what is named here leaves the host's process.
- **The catalog is a projection, not a second registry.** `IClientContractCatalog` reads the Active plugin
  registry. A package appears because it is Active and declared a facet, and disappears the moment either
  stops being true, so a stopped installation offers a browser nothing without anything having to remember to
  withdraw it. It also withholds, with a reason, a facet whose assemblies reference an admitted contract no
  package in its client closure offers: a browser given half a closure fails at whichever type it touches
  first, which is a far harder failure to read than an absent package.
- **The bytes served are the bytes admitted.** `AdmittedContract` retains its staged bytes rather than the
  catalog re-reading the file. The loader's rule is that a file a package owns is read once, because a second
  read is a race in which the file inspected and the file served need not be the same file — and here that
  race ends with a browser holding an assembly the host never proved.
- **The byte route is content addressed.** An address names bytes, so it is served `immutable` and a client
  may hold it forever; an installation that changes mints new addresses instead of changing what an old one
  means, which is why there is no invalidation message anywhere in the protocol. A superseded address is
  `410 Gone` rather than `404`, because the file is still published and re-reading the manifest is the
  recovery.
- **The browser verifies three independent things and refuses on any one.** The content hash it computed over
  whatever it received, from network or from its own store; the CLR identity the runtime bound those bytes as;
  and the module version identifier of the build that actually loaded. It also proves the loaded assembly's
  reference to `Arronix.Abstractions` resolves, by object identity, to the client's own compiled contract
  assembly — two assemblies with one name being exactly the failure a shared contract exists to prevent.
- **An incompatible installation is refused whole.** A host publishing a different universal contract identity
  gets nothing loaded and a sentence on the page. Loading the part of an installation that happens to resolve
  would render values whose meaning nothing has agreed on.
- **The client discovers nothing by enumeration.** The loader reads an assembly's identity, manifest module
  and reference table, and an architecture rule rejects `GetTypes`, `GetExportedTypes`, `GetProperties`,
  `GetFields`, `GetMethods`, `GetMembers` and `Activator.CreateInstance` anywhere in the client's source.
  Enumerating a loaded contract would make the client untrimmable and would make property reflection a second
  media schema beside the typed contracts; saying what a contract holds is generated metadata's job, in G07.2.
  `Arronix.Abstractions` became a trimmer root for the matching reason: a dynamically loaded contract binds to
  members the trimmer never saw.

Proved in a real Chromium against the real server: a clean store loads over the network with all three facts
agreeing and the reference resolving to the client's own contract; a warm store reuses the bytes and fetches
only the manifest; a manifest declaring another contract line loads nothing and says so; and one flipped byte
is refused before the runtime sees it. The complete two-package Movies-and-video closure is proved in
`Arronix.Host.Tests` over the same real staged packages, because no production Arronix host can activate a
package with an entry assembly yet — `ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`,
`IHostRuntimeInfo` and `IOperatingSystemInfo` have no implementation outside the test projects. That gap, and
a .NET 11 preview defect that stops the *published* client starting at all, are recorded in
`docs/research/g07/client-contract-loading.md` rather than worked around.

## 2026-08-25 — Apply the containment policy to the two boundaries that still bypassed it

`LoadFailurePolicy` already split containment by boundary: staging and loading a file uses a closed allowlist,
package code uses a deny-list of conditions in which the process is unsound. Two boundaries were still
catching outside it, and each produced a different failure.

- **Listing a package's own folder ended the load pass.** The shared-contract isolation walk runs before the
  loader's per-package try, so an unreadable folder escaped as an `UnauthorizedAccessException` and no package
  after it in admission order was attempted — not quarantined, not reported, absent. The walk now contains
  file-boundary failures and refuses that package with `PluginLoadFailure`, and the loader's headline says the
  package could not be checked rather than that it conflicts, which is a different claim. A folder that is
  traversable by name but not listable is the reachable case: discovery still reads the manifest, so the
  package is real, declared and answered for, and only its contents cannot be inspected.
- **Teardown filed an exhausted process as a cleanup note.** A disposer and a load-context unloading handler
  are package code and were caught without the policy, so an ordinary disposer bug was contained correctly
  and an `OutOfMemoryException` or a cancellation was recorded as a string and discarded. Both now use the
  package-code rule. A process-fatal or canceled teardown propagates into the bootstrapper's accumulated
  failures, which it already rethrows, and because the contract hold is retained on any reported failure the
  stricter rule can only keep a hold the old one would have kept.

Neither change widens what is contained. Both narrow what is silently absorbed, and both are proved by
mutation: reverting either makes exactly the new fixtures fail, and reverting the teardown rule leaves the
unfamiliar-disposer case passing, which is what the rule is for.

The combined .NET 11 proof rail built with zero warnings and errors, then passed all 2,771 enabled tests out
of 3,073 cases across 12 projects. The 302 registered skips were unchanged.

## 2026-08-25 — Separate the extension SDK from its generated Host-binding bridge

- Added `Arronix.Sdk` as the packaging-only reference for extension authors. It depends on
  `Arronix.Abstractions`, embeds `Arronix.Generators` as an analyzer asset, and contributes no runtime
  assembly. The generator remains a non-packable implementation project rather than a second package an
  author must coordinate.
- Removed generated capture mechanics from concrete author-facing surfaces. `CompiledShapes` became a
  generator-supplied override hidden from ordinary completion lists; its public CLR visibility remains only
  because the published G01 executable-generator sentinel is immutable. Capture became an explicit
  hidden-interface operation, visitor dispatch and erased `Type`/expression carriers became explicit
  implementations on concrete semantic values, and the cross-assembly bridge types which must remain public
  are hidden from ordinary completion lists.
- Added `ARX1003` at the media declaration when `partial` is missing, plus architecture gates which reject
  Host/Client/runtime dependencies and binding vocabulary in typed media extensions. This keeps one typed
  definition and one one-way erasure path rather than creating a simplified parallel SDK model.
- Advanced the contract and first-party manifest line from `0.8` to `0.9`. Packing and inspecting the real
  NuGet artifact proved that the SDK contains the analyzer but no runtime DLL, and an external project with
  only package references compiled a typed media definition; removing `partial` produced `ARX1003` at the
  author site.

## 2026-08-25 — Ship the first independent production provider package

- Added `Arronix.Provider.Tmdb` as a separately versioned package depending on the Movies package while
  compiling only against Abstractions and `Arronix.Media.Movies`. TMDb settings, DTOs, transport, identity
  grammar, embedded markers, mapping, cataloger, and curator remain in that vendor package.
- Applied the G04 ownership rule to a real provider. `TmdbMovieCataloger` returns exact `Movie` values with
  TMDb catalog identity and no durable key; `TmdbMovieCurator` returns catalog references; Host resolves those
  references through the cataloger and assigns `MediaItemId`.
- Exercised the published provider through the real package loader and capability-scoped HTTP gateway. The
  proof observes one shared installed `Movie` type, repeated-fetch identity, curator-to-catalog materialization,
  provider-owned marker readings reaching the Movie parser, and isolated quarantine for missing or incompatible
  Movies dependencies.
- Added structural gates for the provider's source and binary dependencies and for vendor-vocabulary
  containment. Recorded deferred persistence, credentialed live verification, pagination, certification
  authority, and language resolution as later coverage rather than weakening the typed boundary.

## 2026-08-25 — Give catalog identity to the cataloger and durable identity to the host

- Answered G04's open identity question as the owner decided it, and removed `Key` from `IMediaEntity` to
  make it true. A required durable key on the entity was why a cataloger had to invent one; no wrapper or
  optional key removes that obligation while the member is there. No catalog contract reaches a
  `MediaItemId` now, asserted against the compiled contracts and against a compiled provider package.
- `ICataloger.CatalogScheme` declares the one scheme a cataloger is the authority for, and every item it
  returns states exactly one identifier in it. That identifier is read off the item rather than restated in
  a wrapper, because the item already carries it. `CuratedListFetch<TItem>.Items` became `CuratedReference`
  — a catalog identity plus an optional curator-owned `CuratedEntryId`, a separate type — so a list proposes
  what to look at and the owning catalog remains the authority on what it is.
- Made durable identity host state rather than a media-runtime member. `CatalogIdentity` has the host's
  lifetime, is scoped by media kind and level so an item's and a group's identifiers are never compared, and
  survives the reload that rebuilds a kind's runtime. Projection takes the reference as an argument rather
  than deriving the root identity, so an entity no catalog has named still projects; reference-valued fields
  resolve through the explicit host identity state.
- Bounded what a merge means. Repeats, aliases and redirects resolve to one reference; identifiers assigned
  separately and later found to name one item merge onto the lower assignment, and the superseded reference
  resolves through `Canonical`. Library rows already written under it are not moved, which is recorded as a
  limitation rather than implied to be handled.
- Routing is by scheme alone — never provider identifier or implementation type — and the scheme is captured
  once at registration, then its canonical form is enforced at activation. `CatalogSchemeUnowned` is an
  installation problem, `CatalogIdentityInvalid` an extension defect; a curated list's schemes are all
  checked before any of it is fetched.
- Record: `docs/research/g04/media-item-identity.md`. Nothing persists and no production cataloger exercises
  the seam. The rail finished with 2,603 passed, 302 registered skips, zero inconclusive and zero failures,
  from 2,905 cases across 11 test projects.

## 2026-08-24 — Read a provider's pairing from the contract it implements, not from a value it claims

- Closed a hole in the pairing SPI that the same day's earlier entry claimed was closed. The two pairing
  interfaces carried `static abstract` members which `ICataloger<TItem>` and `ICurator<TItem>` answered from
  their own type argument, and registration trusted those answers. An interface can be implemented directly,
  so a class implementing only the non-generic cataloger floor plus the marker — never
  `ICataloger<Movie>` — satisfied the constraint, reported `Movie` and `ICataloger<Movie>`, passed the media
  pairing check, and reached its constructor. Only the post-construction contract check caught it, which is
  after the thing the check exists to prevent. It was reproduced with a scratch compiler probe before being
  fixed and again afterwards.
- Removed the static members rather than adding an agreement check between them and the truth. The markers
  are now empty: `IClosedCataloger` and `IClosedCurator` name a family so that the constraint makes the
  ordinary mistake a compiler error, and nothing more. The pairing is read from the closed contracts the
  implementation actually implements, once, when the registration is built — one-time type inspection, said
  plainly in the code and in the boundary documents rather than presented as something the compiler proved.
  Zero such contracts and several are both refused, the latter naming both, because choosing one would make
  the erased registration disagree with a contract the author wrote.
- Made Host prove the whole relationship before it constructs anything, rather than one link of it. The
  recorded contract must be exactly one closed construction of its family's contract; its type argument must
  be exactly the recorded item type; the implementation must implement that one contract and no sibling of
  it; and a family with no media pairing must not carry one. Such a failure refuses the package with
  `PluginProviderContractInvalid` before activation; the post-construction check stays as defense in depth.
  That code is new, and separate from `PluginMediaPairingUnsatisfied`: a registration that does not describe
  one relationship is a defect in the extension's code, while a coherent one whose item type nothing
  supplies is a defect in the installation, and an operator fixes them differently. Neither is
  `PluginContractMismatch`, which means a contract version or assembly identity mismatch everywhere else in
  the loader. This is
  Host repeating a check the registry already makes, deliberately: Host is the boundary that constructs.
- Made two media kinds closed over one item type an explicit refusal instead of a silent first-win. The map
  from item type to kind was built with `TryAdd`, so a second kind over the same type was dropped and every
  lookup keyed on that type — paired providers, identifier recognition — silently answered with whichever
  kind was seen first. It is refused at kind admission now, before any activation and whether or not the
  extension supplies a provider, with `MediaItemTypeConflict` naming both kinds. Active kinds are walked
  first and in registry order, so the incumbent is always named as the owner.
- Repaired the identity decision record's option analysis rather than its conclusion, which stays open. Its
  wrapper option claimed to keep `Key` required while leaving the key to Host, and those cannot both hold of
  a constructed item; it is now split into a factory-carrying form, whose cost is the author-driven callback
  shape this project has rejected before, and a fact-carrying form, which is the parallel candidate schema
  the roadmap refuses. A fifth option was added: move durable identity off the item entirely into a thin
  Host-owned envelope around the exact media-owned item, with its public-contract and migration cost stated
  neutrally. The smallest-owner-answer section now offers every mechanism the document discusses.
- The integrated .NET 11 proof rail finished with 2,569 passed, 302 registered skips, zero
  inconclusive and zero failures, from 2,871 cases across 11 test projects. Skips are unchanged at
  the ratcheted 302, the ledger at 302 cases and zero replacements, and the three required sentinels
  pass. The manifest-ownership allow-list failure this work saw at its base commit is fixed by the G03
  close it is integrated onto and does not reproduce here.

## 2026-08-24 — Give provider family, item type and identity one authority each, and refuse an unpairable provider before it runs

- Removed `IProvider.Id` and `IProvider.Family`. Every provider implemented both and nothing ever read
  either: Host mints the qualified identifier in `ProviderRegistry.Register` from the contributing extension
  and the declaration's local name, and the registration a provider is admitted through already fixes its
  family. The restatement was not merely redundant, it was wrong in the tree — the movie curator fixture
  called itself `independent-list` while its declaration said `independent-curator`, and nothing compared
  them. A provider that needs its own identifier during a call now reads it from
  `ProviderInvocation.Definition.Provider`, which is the one authority.
- Removed `ProviderDescriptor.Family` for the same reason, and served the two host-owned facts to consumers
  instead. `GET /providers` returns `ProviderCatalogEntry` — the minted `ProviderId`, the family from the
  registration, and the extension's own declaration — which also closed a defect the client had documented
  against itself: it was reconstructing a qualified identifier by parsing a local name, could never succeed,
  and rendered every provider in settings as unavailable. The client's catalog, form and settings page now
  carry one entry instead of a descriptor plus a separately threaded identifier.
- Made the closed cataloger and curator contract the single authority for the item type.
  `AddCataloger<TItem,TCataloger>` and `AddCurator<TItem,TCurator>` became `AddCataloger<TCataloger>` and
  `AddCurator<TCurator>`, and an author names the item type once, in the contract. The first attempt read the
  pairing from static abstract members on the contracts; that was corrected the same day, because a value on
  a directly implementable interface is a claim rather than a fact. See the entry above.
- Kept the two family markers deliberately without a common base, so that a curator cannot satisfy a
  cataloger registration and one class can be both while each registration keeps its own pairing.
- Made admission refuse a provider whose closed item type no active media kind supplies, before any
  implementation in the package is constructed. A package dependency proves a package is installed; it
  cannot prove some kind is closed over the exact CLR type a cataloger's contract names, and that second
  claim is what a typed cataloger is built on. The check runs across every registration first, so one
  unpairable provider means none of the package's providers gets a constructor call, and the refusal carries
  the new `CoreErrorCode.PluginMediaPairingUnsatisfied` with the item types the installation does supply.
  It is a new member rather than a reuse of `PluginContractMismatch`, which is documented as a contract
  *version* mismatch, or of the dependency codes, which are about packages rather than about types.
- Recorded, rather than answered, the durable identity question. A cataloger must return an item whose `Key`
  is a required `MediaItemId`, and that type documents itself as host-minted and not chosen from outside the
  platform; both cannot hold. No candidate type, optional key, provider-minted identifier or parallel schema
  was introduced. The alternatives were recorded in a decision paper and the contradiction was pinned by a
  fixture; the owner answered it the next day, and both were replaced by
  `docs/research/g04/media-item-identity.md`. The independent G05 TMDb pressure test reached the same three
  blocked members from the provider side.

## 2026-08-24 — Close G03: one installation model, one resolved graph, one shared contract identity

The four G04-branch candidates were reconciled into one runtime rather than merged. Where they overlapped,
the overlapping model was deleted rather than kept beside its replacement.

**One canonical installation model.** `InstalledPackage` is the only description of an installed package.
It carries the exact package identity, version, source, folder, optional entry assembly, published shared
contract assemblies, typed direct requirements and a closed availability state, and it is the exact object
every later phase consumes — resolution, contract admission, executable loading, publication and teardown.
Every collection is copied into a read-only collection, declared file names are proved bare at the boundary,
the mutable declaration is not retained, and the type has reference identity only, because two installation
attempts of one identifier are exactly what exact-receipt withdrawal exists to tell apart. The candidates'
`PackageCandidate`, `ResolvedPackage`, `ResolvedRequirement`, `PluginPackage`, `SharedContractPlan` and
`PluginDependencyPlan` are gone. `ValidatedManifest` no longer holds the deserialized declaration; it
projects the canonical snapshot and snapshots only the orthogonal validated metadata.

**One resolver and one graph.** `PackageDependencyResolver` runs the resolution engine and maps its
diagnostics into failure classes and member paths; `ResolvedPackageGraph` is the one durable result. The
edgeless graph source is deleted: once packages can declare dependencies, a host with no resolution
authority does not compose rather than assuming an installation shape. Operator-disabled is a typed closed
state entering resolution before any assembly is opened, and an undefined state value is refused rather than
silently treated as another kind of unavailability. The candidate's null-versus-empty-origin tie becomes a
stronger claim: a package that cannot say where it came from is refused outright, so the two spellings that
let discovery order decide an operator-facing message are unrepresentable.

**One shared contract authority, required.** `SharedContractStore` is registered in DI and takes the
resolved graph as its plan; `NoSharedContracts`, `ISharedContractPlanSource` and the nullable contract-loader
seam are gone. An installation that shares nothing has an empty admitted set, not an absent authority.
Admission stages and metadata-validates in graph order, loads the accepted set into a provisional collectible
context, and installs that context only on a completely successful load — because a load context's binding
cache answers before any resolver runs, so an assembly left out of a dictionary is still there. A publisher
that fails at the real load boundary takes its whole set with it, and every package that required it is
refused over the **declared package edge**, whether or not its own metadata named the failed assemblies.

**Global admission is not global visibility.** A package binds only to contracts published by itself or by a
package in its exact transitive dependency closure. The rule is enforced twice: a contract whose metadata
reaches outside its publisher's closure is refused before it loads, and each executable package receives a
package-scoped resolver rather than the store, which refuses an out-of-closure request instead of falling
through to a private copy or the default context. `PluginLoadContext` now refuses anything its four rules do
not resolve, rather than handing it to the default context where Host already lives.

**Video is a real package.** `Arronix.Format.Video` ships `plugin.json` declaring package id
`arronix.format.video`, one shared contract assembly, no entry assembly and no capability. Movies publishes
`Arronix.Media.Movies.dll` and requires the video package; neither Movies nor Television carries a private
copy of it. Both shared contract assemblies declare a stable `AssemblyVersion` independent of package
version, because a shared contract's binding identity must not change on every release.

**Package lifetime is separate from executable lifetime.** A `PackageAdmissionLease` owns the exact package
receipt and the contract-context hold and wraps an optional `PluginRuntimeLease`. A contract-only package is
a first-class active package — rooted, diagnosable, dependency-bearing and withdrawable — with no load
context, ledger or Host admission attempt invented for it. One Host-owned authorization admits every package
including that one, and the transition to shutdown takes the same publication write lease, so a package
cannot root itself after Stop has begun. A retained attempt occupies its identifier before a replacement
executes a line. A clean stop is not reported until the contract context is released, and a failure anywhere
in a package's release retains its identifier, receipt, edges and pins and yields `StopIncomplete`, including
on repeated stops.

**Containment is split by boundary.** Staging and loading a file has an enumerable failure surface, so that
boundary uses a closed allowlist and anything unexpected stops admission. Running a package's own code, or a
callback it registered, has none, so that boundary contains everything except conditions in which the process
is unsound. Both inspect the whole exception chain, so a type initializer that ran out of memory is not
absorbed as an ordinary refusal. That includes both places package code runs before
registration — the module constructor, whose failure reflection wraps, and the identifier getter, where the
condition arrives direct — and a module constructed before a failing getter is still disposed before its
context is unloaded.

Recorded as unresolved rather than decided: prerelease range semantics (`>=0.1 <0.2` admits
`0.2.0-preview.1` by ordinary precedence, inherited from `VersionRange` and belonging there if it is wrong),
side-by-side contract versions, signing and package integrity, reload involving shared contracts, and the
browser half of one CLR identity, which G07 owns. Television declares its video dependency and its payload is
proved free of private copies; its runtime proof is deferred to G13 rather than fabricated.

Evidence, exact tests and residual risks: `docs/research/g04/integrated-package-runtime.md`.

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
