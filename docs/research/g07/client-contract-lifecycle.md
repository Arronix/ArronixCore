# G07.3 — contract cache lifecycle in a real browser

**Status:** implementation and proof candidate under independent acceptance review. Every exit condition of
G07.3 has been exercised in a real browser; the gate is not self-declared closed here. The hermetic
lifecycle core this drives was recorded when it landed; what is new is that update, removal, stale cache and
stale tab have now been driven through a real Chromium against real restarts of the real API.

This is a record of a change, not a proposal. Every claim below is asserted by a named check in a script
whose observed output is quoted.

## 1. What was proved

One `BrowserContext`, one origin, three installations, and the API restarted between them. The tabs opened
in phases one and two are never navigated again: everything that happens to them afterwards happens through
the contracts page's own reload, which is the same serialized `ContractView` transaction the host's
extension-state event drives. Reloading the document is not used, because reloading the document is the
escape hatch this gate exists to say is the only cure.

| Phase | Installation | What the browser did |
| --- | --- | --- |
| 1 | `arronix.format.video` + `movies` v1 | Clean store: fetched, stored, verified, loaded, deserialized and rendered a typed Movie |
| 2 | same two packages, `movies` rebuilt | The held tab went terminal; a fresh tab loaded the new build |
| 3 | `arronix.format.video` alone | The held tab orphaned Movies and stayed compatible; a fresh tab saw only Video |

**The update is a rebuild, not an edit.** `Arronix.Media.Movies.dll` is rebuilt alone with determinism off,
so the CLR identity, the generated projection schema, the file's length and every other file in the payload
are unchanged while the module version identifier and the bytes are not. The deterministic rebuild
afterwards is asserted to reproduce the first build exactly, and the two payloads are compared over the
union of their file names and then over content, so an added or removed file cannot pass as "only the
shared contract changed".

From the run quoted in section 3. The update's own hash and module identifier differ on every build, by
construction — what is asserted is that they differ from v1's while the identity, schema and length do not:

| Phase | `Arronix.Media.Movies` content hash | Module version id | Bytes |
| --- | --- | --- | --- |
| v1 | `61A97CEB…D39111B7` | `ba23d050-0279-42e6-a701-2e957bb3e136` | 101,888 |
| v2 | `47D3C4DC…1E36E96F` | `23139520-732c-4224-b243-92b728646f14` | 101,888 |

`Arronix.Format.Video` is byte-identical across all three phases (`8D12FD2A…02142ACD`), which is
what makes the eviction claims mean something: one address is expected to survive every sweep and the other
is not.

## 2. What each phase asserts

**Phase 1 — install.** The host publishes both packages. A clean page reads `Compatible`, permits a
projection, and reports its first observation as number one — so nothing is quietly reloading behind the
proof. Both contracts load with source `Network`. The browser's own Cache Storage holds exactly the two
published content hashes, and the count the page states agrees with it, so the independent measurement and
the application's own answer are checked against each other. The fixture then projects into
`Arronix.Media.Movies.Movie` and renders artwork, ratings, lifecycle, status and collections with its
declared status choice.

**Phase 2 — update.** After the restart the manifest is checked directly: same assembly identity, different
content hash, different module version identifier, same projection schema hash, moved installation hash,
untouched video. Then the held tab is reloaded **in place**:

- it becomes `Terminal` and says `projection withheld`;
- the movies row's outcome is `NameAlreadyResident`, and the refusal names what it holds;
- **no byte is fetched at the new content address** — the refusal happens in preflight, so the page never
  asks for a build it could not use;
- the projection it was already showing is withdrawn to `NoAdmittedContract` with no values, the panel
  offers nothing, and the control to project is gone from the document;
- the terminal tab still sheds the stale bytes: the v1 content hash leaves the store and the live video
  hash stays.

A fresh tab in the same context then reads the update, loads the new movies build from `Network` — because
the stale bytes were evicted — and reuses video from `Store`. It projects the update. The held tab is still
`Terminal`.

**Phase 3 — removal.** The host publishes video alone and withholds nothing. The held v2 tab is reloaded in
place: what remains installed is still `Compatible` and still projectable, Movies is no longer part of an
installation, and it is reported under "held here, no longer installed" attributed to a package this host no
longer offers. Nothing is served through it, the projection is withdrawn, and the store holds the live video
hash and nothing else. A fresh tab holds no orphan of its own, carries no withdrawn assembly, loads video
from `Store` without a fetch, and its store is exactly the live installation. The tab that went terminal in
phase 2 never recovers.

**Across every phase.** No page wrote an error outside a restart or the teardown; every error inside one
matches a browser-issued `net::ERR_` transport failure and nothing else; and each of the three tabs was
opened exactly once and never navigated again.

## 3. Evidence

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07-client-contracts.sh --browser --lifecycle --port 5231`
— exit 0.

```text
== server half ==            30 checks, all green
== browser half (G07.2) ==   43 of 43 browser checks passed.
== lifecycle half (G07.3) == 64 of 64 browser checks passed.
```

137 checks in total, zero failures. Machine-readable evidence is written under `artifacts/g07/evidence`:
`server-half.txt`, `browser-half.json`, and `lifecycle-half.json` — the last carrying every phase's
published identity, build and content address, the restart windows, every navigation, and every error.

The default port is 5223; `--port` was used because an unrelated development server owned that port on the
machine this was run on, and the harness refuses to touch a listener it did not start.

Full rail, `DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/ci/run-tests.sh` — exit 0:

```text
projects=14 total=3811 enabled=3509 passed=3509 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0 requiredTests=3
compileLogs=1 compileProjects=14 compileItems=369 boundSources=15
```

The registered skip count is unchanged at 302 — 301 Movies cases and one architecture case — and both
ratchets pass.

## 4. Mutation evidence

Each mutation compiles, the client is republished from the mutated tree, and the lifecycle matrix is re-run.
A mutation that fails to build proves nothing, so the runner refuses one.

| Mutation | Result |
| --- | --- |
| Reunion accepts any build with the same identity (`Matches` compares identity alone) | 3 fail: the held tab reuses the new build instead of going terminal |
| The sweep treats everything held as live, so it evicts nothing | 5 fail: the stale hash survives phase 2, and phase 3's store is not the live installation |
| `ContractPayloadLoader` registered by type rather than by the composition root's factory | the composition case fails; before it existed, the whole contracts page failed to render |

The orphan gate on the loader's `Current` — refusing to serve a resident assembly the installation no longer
names — is **not** distinguishable by this matrix, and that is recorded rather than dressed up. `Admitted()`
is already gated by the report's package list, so an orphaned assembly is never reached through the rendered
page; the gate protects callers that ask for one by name. Removing it fails two hermetic cases in
`Arronix.Client.Tests` and no browser check. What the browser proves about removal is the rendered
consequence: Movies is reported as orphaned rather than as part of an installation, nothing is offered
through it, its projection is withdrawn, and its bytes are evicted.

## 5. A defect this found

The accepted base could not render the contracts page at all. `ContractPayloadLoader`'s constructor is
internal and it was registered by type, so the container found no constructor and the page threw
`NoConstructorMatch` at its first render — taking the G07.2 browser proof with it. The constructor stays
internal; the composition root, which is in the same assembly, constructs it by factory. A behavioural case
in `Arronix.Client.Tests` resolves it and asserts it is one singleton, which a container's build-time
validation cannot do because it never calls a factory.

## 6. What the harness promises about its own server

The proof owns one origin for the length of a run and is deliberately not entitled to clear it: a port
already in use is refused before anything is built, and a listener this harness did not start is never
signalled. The server is the built assembly launched directly, so the tracked pid is the process that holds
the port rather than a launcher whose child does.

Shutdown is bounded at every step. A signal is sent to the exact owned pid, an exited-but-unreaped child is
distinguished from a running one, escalation to `SIGKILL` happens only against a process still running, and
a surviving owned process is a failure whatever the socket says. The handle is retained across every failed
attempt — dropping it is what leaves a detached server with nothing to escalate against — and both owners
retry a bounded number of times: the shell's exit trap re-attempts the whole bounded stop, and the Node
driver's exit handler re-attempts `SIGKILL` synchronously.

**This is bounded effort and visible failure, not a guarantee.** A process the run may not signal at all
stays running. What is promised is that the attempts are bounded, that the failure is reported with the pid,
and that the run exits non-zero — and that the next run refuses to start while that process holds the port,
rather than quietly measuring somebody else's server.

The driver deletes and rebuilds its own package root, and it takes that root as an argument. It locates the
repository from its own file, resolves every path against that rather than against the caller's working
directory, and refuses any path that does not resolve to its exact place under `artifacts/g07` before it
removes anything.

Seven failure paths were exercised against the exact code:

| Case | Result |
| --- | --- |
| `dotnet` that does not exist, so `spawn` fails asynchronously | exit 1, reported as an error rather than an uncaught exception, port free, no owned process |
| A failure induced after the API started, through the full harness | exit 1, port free, no owned process |
| The shell's first signal refused, with only the exit trap able to recover | bounded trap retry reaps it; **exit 1**, because a transient cleanup failure is still a failure; port free, no owned process |
| The Node driver's first signal throwing `EPERM`, with only the synchronous exit reap able to recover | exit 1, reaped, port free, no owned process |
| `SIGTERM` to the driver while its API is running | exit 130, the signal handler's bounded reap runs, port free, no owned process |
| `--packages` pointing outside `artifacts/g07` | exit 2 before anything is removed; the named directory is untouched and no API is started |
| `reap`'s first three signals refused, then `SIGTERM` to the driver | each refusal reported with the pid, the handle retained, and the fourth attempt from the exit handler reaps it; exit 130, port free, no owned process |

The cross-phase assertions — no unattributed error, only transport failures inside a window, one navigation
per tab — are evaluated after teardown rather than before it, so they cover the seconds in which the API is
stopped under pages that are still open.

## 7. Limitations

1. **The event-driven path is not driven here.** Nothing in the shipped server raises
   `EventKind.PluginStateChanged` yet, so a connected tab has no host event to react to. The matrix uses the
   contracts page's own reload, which is the same `ContractView` transaction the watcher calls; the watcher's
   filter itself is covered hermetically by `ContractStateWatcherTests`. Until a host publishes that event,
   "a connected tab re-reads for itself" is proved in the rail and not in a browser.

2. **The store is Cache Storage, not IndexedDB.** `contract-store.js` uses the Cache API deliberately, and
   the matrix reads that store directly from the browser to check eviction. Any claim about "IndexedDB"
   should be read as this store.

3. **The browser half is not on the hermetic rail.** It needs Playwright's Chromium, which a
   clean-repository proof does not carry, so it runs beside the rail. The rail still holds the hermetic
   lifecycle core and the composition case.

4. **One machine, one browser.** Chromium only. Nothing here claims Firefox or Safari behaviour, and nothing
   here claims anything about G07A's external consumer or G07B's durable catalog.

5. **A restart is a process restart, not an operator installing a package.** The packages are composed on
   disk and the API is restarted over them. Live installation without a restart is not part of this gate.
