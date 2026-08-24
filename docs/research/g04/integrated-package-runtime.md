# G03 — the integrated package runtime, as built

**Status:** implemented on `claude/g04-integration-r3`, from the reviewed G02 close at `1b8ce1b3f`.

This records what the repository now does, the exact tests that hold it, and what is still open. It is a
record of a change, not a proposal: every rule below is asserted by a named fixture.

The branch names say G04; the gate this closes is **G03** — package dependency and version rules with one
exact CLR type identity. The separate G04 gate (typed provider pairing, materialization, registration
ergonomics) is not started and nothing here claims it. In particular the fixture provider still writes
`AddCataloger<Movie, IndependentMovieCataloger>`, repeating an item relationship `ICataloger<Movie>` already
closes; removing that repetition is G04's work.

## Candidate branches consumed

Each was clean at the tip recorded here and carried a terminal report.

| Branch | Tip | Terminal commit |
| --- | --- | --- |
| `claude/g04-manifest-graph` | `cbe3eb6222407` | Make configuration-disabled packages terminally unavailable before resolution |
| `claude/g04-contract-loader` | `6dd3731ab0709` | Contain every non-fatal shared-contract load failure per package |
| `claude/g04-dependency-lifecycle` | `d4e92b80ad7e5` | Make shutdown completion account for retained package receipts |
| `claude/g04-package-topology` | `b7eb08a1f41b4` | Stage package payloads from the computed closure, not from a build directory |

They were reconciled rather than merged. Where two branches modelled the same fact, the overlapping model was
deleted rather than kept beside its replacement — see [What was deleted](#what-was-deleted).

## 1. One canonical installation model

`Arronix.Plugins.Dependencies.InstalledPackage` is the only description of an installed package. It carries
the exact package identity, version, source, folder, optional entry assembly, published shared contract
assemblies, typed direct requirements and a closed availability state, and it is the exact object every later
phase consumes: resolution, shared-contract admission, executable loading, runtime publication and teardown.

- every collection is copied into a `ReadOnlyCollection<T>`, so a caller cannot cast a property back to the
  list behind it;
- every declared file name is proved bare at the boundary through the one `PackageFileName` rule the manifest
  validator also uses, so the two cannot drift;
- an entry assembly may not also be a declared shared contract;
- an undefined availability value is refused rather than treated as another kind of unavailability;
- the type has **reference identity only**. Two installation attempts of one identifier are exactly what
  exact-receipt withdrawal exists to tell apart, so a structurally equal clone must not compare equal to the
  package the loader admitted.

`ValidatedManifest` no longer retains the deserialized declaration. It projects the canonical snapshot for
identity, version, entry assembly, contracts and requirements, and snapshots only the orthogonal validated
metadata it owns — name, description, contract range, capabilities, media kinds, tokens and an immutable
`ValidatedPolicies`.

## 2. One resolver, one graph

`PackageDependencyResolver` is the one production `IPackageGraphSource`. It runs `PackageDependencyEngine`
over the installed packages and maps its diagnostics into the failure classes and member paths an operator
acts on. `ResolvedPackageGraph` is the one durable result: admission order, refusals with their defects and
copies, dependency closures and dependant closures. The engine's three pass-local answers are `out`
parameters, so no second durable resolved-graph model exists.

- **there is no edgeless fallback.** Once a package can declare a dependency, a host with no resolution
  authority does not compose rather than assuming every package is independent;
- one edge is an exact package identifier plus a compatible version range. No named facets, no separate
  contract and plugin edges, no transitive restatement;
- the engine never prefers: two copies of one identifier, one requirement stated twice, and a cycle are
  refusals rather than resolutions;
- eligibility propagates and is contained: a directly faulty package and every transitive dependant become
  ineligible, and unrelated packages keep their place;
- activation order is the ordinally smallest valid topological order, so it is a property of the graph rather
  than of the walk that discovered the folders.

**The null-versus-empty-origin tie.** The candidate's repair normalized a blank origin to none and made the
sort key equal to the rendered text. The canonical model closes it one step earlier: a package with no folder
is refused outright, because an installed copy was found somewhere. The state is unrepresentable rather than
normalized. `ACopyThatCannotSayWhereItCameFromIsRefused` covers all three spellings; the rendering-order half
survives as `CopiesSharingAVersionAreListedByTheTextTheyPrintBy`.

## 3. Typed availability

`PackageAvailability` is a closed state with `Available` and `DisabledByConfiguration` and nothing else. The
loader translates operator configuration into it once, where the canonical snapshot is built. There is no
caller-supplied prose channel: the graph branches on the state, and `PackageAvailabilityReason.Describe`
renders it into words at the diagnostic boundary. `PackageAvailabilityReason.Required` refuses an undefined
value, and `InstalledPackage` calls it at construction.

A disabled package keeps `PluginDisabled` with its own defect list — it is still installed, so it is never
reported as missing, and its genuine declaration defects stay inspectable. Its dependants are refused with
`PluginDependencyUnavailable` naming the real root cause.

## 4. Transactional shared-contract admission

`SharedContractStore` is a required DI authority. `NoSharedContracts`, `ISharedContractPlanSource` and the
nullable `IPackageContractLoader` seam are gone: an installation that shares nothing has an empty admitted
set, not an absent authority.

Admission is installation-context transactional:

1. discovery reads and validation proves one immutable snapshot per package;
2. configuration becomes typed availability;
3. the resolver produces the total graph;
4. eligible packages' contracts are staged and metadata-validated **in graph order**;
5. the accepted set loads into a provisional Host-owned collectible context;
6. a publisher failing at the real load boundary discards that whole provisional context — including siblings
   that had already loaded — and the surviving set is rebuilt;
7. only then are packages published, in graph order.

Step 6 discards the context rather than a dictionary entry because a load context's binding cache answers
**before** any resolver runs: an assembly left out of a map is still there and still answers its own name.
The surviving context is a different object, which is what makes the withdrawal real.

Withdrawal follows the **declared package edge**, not metadata. A dependant whose own contracts never mention
the failed package's assemblies is refused anyway, as is a dependant that publishes no contract at all, and it
is refused with its own headline rather than the publisher's. Lost assembly names appear as secondary detail.

Identity is exact: simple names compare case-insensitively, because that is how the runtime binds them, and
version, culture and public key token are compared in full, because the runtime checks none of them.
`AssemblyIdentity` is an immutable value — `AssemblyName` is not used to carry a proved identity, because its
setters can rewrite one after the bytes were staged — and a reference's `PublicKeyOrToken` blob is decoded
according to its own `AssemblyFlags.PublicKey`, so a full key is reduced to the token the runtime binds on.

Conflict diagnostics carry source, full identity, MVID and content hash on both sides, for private duplicates,
identity mismatches and undeclared references alike. One snapshot of the admitted state governs a package's
whole file walk, so a concurrent release cannot erase half a verdict.

## 5. Global admission is not global visibility

A package binds only to contracts published by itself or by a package in its **exact transitive dependency
closure**. The rule is enforced twice:

- **before load**, a contract whose metadata references a contract outside its publisher's closure is refused,
  naming the publisher it actually came from;
- **at runtime**, each executable package receives a `PackageContractScope` rather than the store. The scope
  resolves what its closure published, returns `null` for a name the installation never admitted — which is
  the package's own private resolver's business — and **throws** for an admitted name outside its closure,
  rather than falling through to a private copy or the default context.

The scope is also the package's releasable hold on the contract context, released by reference. A private
gate orders `Resolve` against `Release`, so a release cannot complete while a resolve is in flight and no
resolve can begin after one has. `PluginLoadContext` now refuses anything its four rules do not resolve
rather than returning `null`, which would hand the request to the default context where Host, the API and
everything the host loaded already live.

## 6. Package and assembly topology

| Package | Shared contract assembly | Isolated executable assembly |
| --- | --- | --- |
| `arronix.format.video` | `Arronix.Format.Video` | *(none; it runs no code)* |
| `movies` | `Arronix.Media.Movies` | `Arronix.Plugin.Movies` |

`Arronix.Format.Video.Contributions` holds video's executable half — currently the release-term recognition
vocabulary — and no media extension references it. Both shared contract assemblies declare an explicit
`AssemblyVersion` of `1.0.0.0`, stable while the package version moves independently, because a shared
contract's binding identity must not change on every release.

Movies and Television reference `Arronix.Format.Video` with `Private="false"`: compilation only, no copy in
the payload. The video package is staged as an installed package beside them.

**Television.** It declares its video dependency and its payload is proved to carry no private copy of either
video half, but it is staged into a directory the loader never discovers, so its runtime proof is the
compile-and-package half only. The runtime proof belongs to G13's conversion and is deferred rather than
fabricated here.

## 7. Package lifetime

`PackageAdmissionLease` owns the exact `PackageAdmissionReceipt` and the `PackageContractScope`, and wraps an
optional `PluginRuntimeLease`. A contract-only package is a first-class active package — rooted, diagnosable,
dependency-bearing, withdrawable — with no load context, registration ledger or Host admission attempt
invented for it. `PluginLoadResult.ActivePackage` is its published form.

- a package pins the exact receipts of its dependencies **before it reads a byte**, and the edges it carries
  must be a one-for-one ordered binding of its own declared requirements;
- an identifier held by a retained attempt refuses a replacement before that replacement's module is
  instantiated;
- publication is one transaction under one gate: Host authorization, token claims, the Host attempt, the
  package edges and the runtime result commit together or not at all. The transition to shutdown takes the
  same write lease, so a publish either commits wholly before shutdown claims the gate or observes it and is
  refused;
- teardown reverses the order packages were **actually published in**, read from the registry rather than
  recomputed from declarations at shutdown;
- under the gate a package is hidden and marked withdrawing while keeping every pin; outside it, its
  instances and context are released and then its contract hold — which it gives up while its dependencies
  are still pinned, because its own contract assembly may reference theirs; only a release that reported no
  failure re-enters the gate to remove edges and unpin;
- any disposer, unload-request, contract-release, authority-mismatch, in-flight-work or dependent-pin failure
  retains the exact identifier, receipt, edges and pins and yields `StopIncomplete`, including on repeated
  stops. An empty active set is not proof that nothing remains held;
- a clean stop is not reported until `SharedContractStore.TryRequestUnload` succeeds.

One global contract context cannot physically unload one assembly, so a package's release is modelled as
giving up a claim on that context; unloading is requested only after every holder is gone.
`AssemblyLoadContext.Unload()` is a request, and collection is never claimed anywhere.

## 8. Containment policy

Two boundaries with different failure surfaces, so two rules in `LoadFailurePolicy`:

- **the staged-file boundary** — reading and loading a file is bounded, so it uses a **closed allowlist** of
  the failure types it can produce. A type outside that set stops admission rather than being absorbed;
- **package code and package-registered callbacks** — a module, a constructor, a property getter or an
  `Unloading` handler may throw any type, so this boundary contains everything except conditions in which the
  process is unsound. An allowlist here would let a novel extension bug stop the whole installation.

Neither absorbs cancellation, exhausted memory, exhausted stack, corrupted memory or a structured native
failure, and both inspect the whole exception chain, so a type initializer that ran out of memory is not
absorbed as an ordinary refusal.

The policy governs every place package code runs before registration, including the two reflection
boundaries: the entry module's constructor, whose failure reflection wraps in a
`TargetInvocationException`, and the identifier getter, which the loader calls directly.
`ModuleActivationContainmentTests` proves both against a real emitted package that declares its own
exception type — so "unfamiliar" is literal rather than a stand-in — and throws process-fatal conditions
both wrapped and direct. A module constructed before a failing identifier getter is still disposed before its
context is unloaded, whether that getter's failure was contained or propagated.

## 9. The required exit proofs

| # | Claim | Fixture |
| --- | --- | --- |
| 1 | A separately packaged provider's `ICataloger<Movie>` closes over the registered kind's item type, by reference | `PackagedIdentityAndLifecycleTests.ASeparatelyPackagedProviderClosesItsCatalogerOverTheRegisteredMoviesItemType` |
| 2 | Two independently packaged dependants see one `Video` assembly and type | `.TwoIndependentlyPackagedDependantsSeeOneVideoAssemblyAndType` |
| 3 | Video loads once in the collectible contract context; no dependant context holds a copy | `.VideoLoadsOnceInTheContractContextAndNoDependantHoldsAPrivateCopy` |
| 4 | The contract-only video package is rooted and cannot be released while dependants live | `.TheContractOnlyVideoPackageIsRootedAndCannotBeReleasedWhileDependantsLive` |
| 5 | Missing, incompatible, duplicate, cyclic, disabled, contract-load and executable dependency failures refuse the affected packages before activation; unrelated packages survive | `PackagedRefusalMatrixTests` (7 cases) |
| 6 | Exact receipts and observed dependant-before-dependency order govern teardown; terminal failure paths retain everything | `.TeardownIsObservedToRunDependantsBeforeTheirDependencies`, `PackageWithdrawalFailureTests` (3 cases) |
| 7 | Private duplicates and different builds of one identity are refused with both MVIDs and hashes | `.APrivateCopyOfAnAdmittedContractIsRefusedAndUnrelatedPackagesSurvive`, `.APackageCarryingADifferentBuildOfAnAdmittedContractIsRefusedNamingBothBuilds` |
| 8 | Every discovery order yields the same eligibility, order and diagnostics | `PackagedRefusalMatrixTests.EveryDiscoveryOrderProducesTheSameEligibilityOrderAndDiagnostics`, `PackageDependencyPermutationTests`, `PackageResolutionPlanTests` |
| 9 | Staging comes only from evaluated publish inputs and excludes stale source-bin artifacts | `StagingExcludesStaleBuildOutputTests.APlantedStaleAssemblyInTheSourceBuildOutputCannotEnterAStagedPayload` |

Proof 9 plants a controlled stale assembly in the movies project's **real build-output directory** and runs
the **real publish** the staging targets run. The pre-existing `StagedPayloadDetectorTests` plants a file into
an already-staged copy, which proves the detector rather than the staging mechanism; both are kept, and the
distinction is stated in both fixtures.

## 10. Mutation evidence

Each guard was locally inverted, the suite run, and the guard restored. The working tree carries none of them.

| Mutation | Result |
| --- | --- |
| `PackageContractScope` makes every admitted contract visible to every package | `AnAdmittedContractOutsideAPackagesClosureIsRefusedRatherThanResolved` fails |
| `TryCheckPackage` inspects no package file | 4 fail: private-copy and different-build refusal in both the store suite and the packaged suite |
| `BeginWithdrawal` no longer refuses a package with a live dependant | 5 fail, including both preparation-window fixtures |
| A failed contract publisher no longer withdraws its package dependants | `AContractThatCannotLoadWithdrawsItsPackageDependantsWhateverTheirMetadataSays` fails |
| `PluginLoadContext` never resolves an admitted contract | 8 fail, including all three identity proofs |
| Staging copies the build directory instead of publishing | `APlantedStaleAssemblyInTheSourceBuildOutputCannotEnterAStagedPayload` fails, naming the planted file |
| Module construction and identity getters catch without the containment policy | 4 fail: all three process-fatal propagation cases and the disposal proof for the propagating one |

## 11. What was deleted

Overlapping models whose facts could drift, removed rather than kept beside their replacements:
`PackageCandidate`, `ResolvedPackage`, `ResolvedRequirement`, `PackageDependencyResolution` (as a durable
type), `PluginDependencyPlan`, `PluginDependencyPlanEntry`, `PluginDependencyRefusal`,
`PluginDependencyPlanner`, `PluginPackage`, `SharedContractPlan`, `SharedContractDeclaration`,
`ISharedContractPlanSource`, `NoSharedContracts`, `SharedContractLease`, `IPackageContractLoader`,
`IPackageContractAdmission`, `PackageContractAdmissionResult`, `EdgelessPackageGraphSource`,
`ResolvedPackageGraph.Edgeless`, `InstalledPackage.WithAvailability`, and
`PluginCandidate.ResolveEntryAssemblyPath` (which read the unproved manifest).

## 12. Test results

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/ci/run-tests.sh` — exit 0.

```text
projects=11 total=2836 enabled=2534 passed=2534 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 requiredTests=3 compileLogs=1 compileProjects=11 compileItems=261 boundSources=15
```

Against the G02 close (2,168 passed of 2,470, 302 skipped) this is +366 enabled cases. The registered skip
count is unchanged at 302 — 301 Movies cases and one architecture case — and both ratchets pass. Per project:

| Project | Passed | Skipped |
| --- | --- | --- |
| `Arronix.Plugins.Tests` | 499 | 0 |
| `Arronix.Host.Tests` | 532 | 0 |
| `Arronix.Architecture.Tests` | 317 | 1 |
| `Arronix.Plugin.Movies.Tests` | 370 | 301 |
| remaining seven projects | 816 | 0 |

## 13. Residual risks

1. **Reload involving shared contracts is refused, not solved.** An admitted store takes a second resolved
   graph only when neither shares anything: matching identifiers, versions and file names do not prove
   matching bytes, identities or dependency closures, and adopting another graph's loaded assemblies on that
   evidence would be a hole. A real reload needs staged-byte, identity and closure reconciliation, which is
   not designed.
2. **Prerelease range semantics are unresolved.** `>=0.1 <0.2` admits `0.2.0-preview.1` by ordinary
   precedence; there is no npm-style prerelease exclusion. This is inherited from `VersionRange`, is the same
   rule the contract range has always used, and belongs there if it is wrong. Recorded, not decided.
3. **Side-by-side contract versions are unsupported.** One admitted version per simple name per installation,
   and contract replacement is restart-scoped, because an assembly with live dependants cannot be unloaded.
4. **No signing or package integrity.** Content hashes are recorded and compared between copies; nothing
   verifies provenance.
5. **The terminal withdrawing state is unbounded.** A hostile or merely faulty disposer pins a whole closure
   for the life of the process and leaves the host in `StopIncomplete`. That is the safe direction and a real
   operational cost; nothing surfaces it to an operator beyond the log.
6. **Collection is never proved.** `AssemblyLoadContext.Unload()` is a request. Nothing here asserts that any
   context was collected, and a discarded provisional context's bytes are unreachable but not freed.
7. **Type initializers are not refused.** A module initializer is detected from metadata and refused; a
   `static` constructor on a contract type runs on first use, inside the shared context, on whichever
   dependant's thread touched it first. That remains a packaging and review responsibility.
8. **A shared contract cannot carry satellite resources.** Satellite lookups do not appear in a reference
   table, so admission cannot validate them and the fail-closed context refuses them at runtime.
9. **Package file scanning is top-level only.** RID-specific subfolders, satellite assemblies and native
   libraries are not walked for duplicate detection.
10. **The shared-assembly source-text screen is a heuristic**, and is documented as one. The structural rules
    are what prove the boundary; the spelling screen catches literal usage only.
11. **`deps.json` payload comparison proves what it says and no more.** It proves a staged payload contains
    nothing its own dependency manifest does not name. It is not a completeness claim about the payload.
12. **Television's video dependency is proved at compile and package level only.** Its runtime proof is G13's.
13. **Concurrent admission is not supported.** Admission is single-threaded; the store refuses re-entrant
    admission and a second same-identifier preparation, but the ordering proofs assume one loader pass.
