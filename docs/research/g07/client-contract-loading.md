# G07.1 — publishing, caching, and loading the exact client-safe contract package

**Status:** complete. G07 was split into three numbered sub-gates before implementation; this records G07.1
only. Every numbered exit condition is met against the ordinary published client. The shipping publish
disables trimming, which is recorded technical debt in section 3 rather than an open exit condition: this
gate never required an optimized publish. G07.2 (generated metadata, typed deserialization and rendering)
and G07.3 (update, removal, stale cache and stale tab) are open, and none of their work is claimed here.

This is a record of a change, not a proposal. Every rule below is asserted by a named fixture or by a
command whose exact output is quoted.

## 1. What was built

A browser client acquires an installed media kind's typed CLR contract from whichever host served it,
proves it against what that host published **before the runtime sees a byte**, and loads it — with no
compiled dependency on any media or format package anywhere in the client build.

Five parts:

1. **A declared client facet.** `plugin.json` gains `clientContracts`: zero or more of the package's own
   published shared contract assemblies, which a browser may download. Validated as a subset of
   `contractAssemblies`, so an entry assembly can never appear in it — that is refused one rule earlier —
   and neither can a file the package does not publish, nor a duplicate. Declared, never inferred, for the
   same reason the shared list is: what is named here leaves the host's process.
2. **A projection of the running installation.** `IClientContractCatalog` reads the Active plugin registry
   once, under the registry's own publication read gate — taken from the registry rather than supplied, so a
   catalog guarding a different one cannot be constructed — and publishes, per client-safe assembly, its SHA-256 content hash, its
   assembly/version/culture/public-key identity, its module version identifier and its length; and per
   package, its transitive client dependency closure in load order plus one hash over it. One further hash
   covers the whole installation. Facets it will not serve appear in the same document, with reasons.
3. **Content-addressed serving.** `GET /api/v1/client-contracts` returns that manifest, uncacheable.
   `GET /api/v1/client-contracts/{package}/{contentHash}/{fileName}` returns the exact admitted bytes,
   `immutable`. An address names bytes, so it can be held forever; an installation that changes mints new
   addresses instead of changing what an old one means.
4. **A verifying browser loader.** Two passes. The first fetches nothing into the runtime: it validates the
   manifest structurally, then for every assembly in the required closure checks length, SHA-256, and — by
   reading the PE metadata of the exact bytes — the declared CLR identity, the declared module version
   identifier, and the declared reference to this client's universal contract. The second pass loads, and
   only entries the runtime actually accepted are reported as loaded.
5. **All or nothing.** `ContractLoadReport.CanProject` is true only when every required assembly is verified
   and resident. Diagnostics stay visible either way; "compatible" means safe to project and nothing else.

## 2. Decisions worth stating

**Nothing is loaded to find out whether it should have been.** A browser's default load context cannot
unload, so a payload whose content hash was right and whose declared identity was a lie would be resident
for the life of the page. Every question is therefore answered while the payload is still a byte array, with
`PEReader`/`MetadataReader` over those exact bytes — a structured reader, no type resolution, no static
constructors, nothing handed to the runtime.

**The whole closure is verified before the first irreversible load.** A closure is not a list of
independent downloads: admitting a dependency and then discovering its dependant is wrong leaves the page
holding half an installation it can never complete.

**Verified is not loaded.** An entry that passed every check and was not loaded — because another member of
the required set failed, or the commit stopped earlier — reports `Verified`. Saying `Loaded` because it
could have been is the one lie this report must not tell.

**There is still a post-load proof.** Preflight establishes what the bytes declare; it cannot establish what
the runtime did with them. The load goes through an internal seam whose production value is
`AssemblyLoadContext.Default.LoadFromStream`, so a test can supply a runtime that returns a different
already-loaded assembly and drive the refusal branch as an ordinary regression. After `LoadFromStream` returns, the loaded assembly's `FullName` and module
version identifier are compared with the published ones, and its reference to `Arronix.Abstractions` is
resolved through the default context and required to be `ReferenceEquals` to `typeof(IMediaEntity).Assembly`.
Object identity, not name equality: two assemblies with one name is exactly the failure a shared contract
exists to prevent. Only after that does the entry become `Loaded`.

**The manifest is untrusted input.** It is validated whole before any field is acted on — before even the
contract-identity refusal, which renders the packages. Null collections, duplicate package identifiers,
duplicate assembly simple names, a closure that omits or misorders its own package, a closure member the
host did not publish, unsafe file names, non-SHA-256 hashes, empty module identifiers, an identity whose
simple name disagrees with the assembly name, and self-blaming or dangling refusal causes are each one
visible whole-installation refusal. No missing closure member is silently skipped and no duplicate assembly
can overwrite another's verification result.

**Identifiers are identifiers.** `PluginId` travels through the wire and report models rather than text, and
the client's converter throws `JsonException` on unreadable identifier text rather than defaulting — a
default identifier compares equal to every other unreadable one, which silently merges packages the host
never merged.

**A superseded address is `410 Gone`, not `404`.** The file is still published, at a different address, so
re-reading the manifest is the recovery.

**A refusal separates what it means.** `MissingAssemblies` carries admitted assembly simple names a facet
binds and cannot reach; `UnadmittedFiles` carries declared file names the installation admitted nothing for.
They are different kinds of string and one list made neither readable. `CausedBy` is a list, because a
package can lose several required facets at once and naming one sends an operator to fix a fraction.

**Withholding cascades to a fixed point.** A facet is servable only if a browser can bind everything its
assemblies name and every required facet it declares is itself being served. Withholding one package removes
its assemblies from every closure that contained it, which can make a dependant unservable in turn, so the
rule runs until nothing changes. A single pass lets the package examined first survive with a closure a
later withdrawal already emptied. Direct requirements are canonically ordered inside the rule, so a closure
and its hash describe the graph rather than the order an author wrote the declaration in.

**A dependant of a withheld facet is withheld even if it binds nothing from it.** "It does not bind that
assembly today" is a property of the current build, not of the dependency; the closure a browser is told to
load contains it either way. The conservative answer costs one media kind in a browser; the permissive
answer costs the meaning of the closure.

**The client enumerates nothing.** `ClientTopologyTests.ClientDiscoversNothingByEnumeratingALoadedAssembly`
rejects `GetTypes`, `GetExportedTypes`, `GetProperties`, `GetFields`, `GetMethods`, `GetMembers` and
`Activator.CreateInstance` anywhere in the client's source. Discovery of what a contract *contains* is
G07.2's generated metadata.

## 3. The trimming debt, and how it was attributed

Technical debt, recorded here and carried in `CONTEXT.md`. It is not an exit condition of this gate: G07.1
asked for a client-safe contract package to be published, served, verified and loaded in a real browser, and
the browser matrix below proves that against the client an ordinary `dotnet publish` produces.

**The trimmed client cannot start.** On .NET 11 preview 7 a trimmed publish fails during
`WebAssemblyHost.RunAsyncCore` with:

```text
System.InvalidCastException: Arg_InvalidCastException
   at Microsoft.AspNetCore.Components.Reflection.MemberAssignment.GetPropertiesIncludingInherited(Type, BindingFlags)+MoveNext()
   at Microsoft.AspNetCore.Components.Infrastructure.PersistentServicesRegistry.PropertiesAccessor..ctor(Type targetType, Type keyType)
   at Microsoft.AspNetCore.Components.Infrastructure.PersistentServicesRegistry.RestoreInstanceState(Object, Type, PersistentComponentState)
   at Microsoft.AspNetCore.Components.Infrastructure.PersistentServicesRegistry.RegisterForPersistence(PersistentComponentState)
   at Microsoft.AspNetCore.Components.Infrastructure.ComponentStatePersistenceManager.RestoreStateAsync(IPersistentComponentStateStore, RestoreContext)
   at Microsoft.AspNetCore.Components.WebAssembly.Hosting.WebAssemblyHost.RunAsyncCore(CancellationToken, WebAssemblyCultureProvider)
   at Arronix.Client.Program.Main(String[])
```

The first record of this claimed it was pre-existing on the strength of a default template application and
of removing selected G07 registrations. That was not proof, and the review was right to reject it. It was
then attributed properly, by A/B against the pristine base commit:

| Tree | Publish | Result |
| --- | --- | --- |
| `f943b8a0f`, exported with `git archive`, untouched | `-c Release` (trimming on) | **fails** |
| `f943b8a0f`, untouched | `-c Debug -p:PublishTrimmed=false` | starts, renders |
| this branch | `-c Debug -p:PublishTrimmed=false` | starts, renders |
| this branch | `-c Release` (trimming on) | **fails** |
| default `blazorwasm` template, same SDK | `-c Release` (trimming on) | starts, renders |

Each was served by a plain static server on a port never visited before, in a fresh browser page, so no
service worker and no cache from an earlier run could be involved. Trimming is the variable and the base
commit already had it; rooting `Microsoft.AspNetCore.Components` does not help.

One earlier observation in this work — "untrimmed also fails" — was an artifact of a working copy whose
`obj/` had absorbed a dozen publishes with conflicting properties. Clean-building it made the untrimmed
publish start. The proof script now removes `obj/` and `bin/` before publishing for exactly that reason.

**The response is a property, not a flag on one command.** `Arronix.Client.csproj` sets
`<PublishTrimmed>false</PublishTrimmed>`, so an ordinary `dotnet publish` produces an application that
starts, and that is the artifact the browser matrix runs against. A proof-only `-p:PublishTrimmed=false` would leave the artifact somebody deploys different from the
artifact anybody tested. The cost is a much larger download and it is visible in the project file where the
decision is. `TrimmerRootAssembly Include="Arronix.Abstractions"` stays and is inert: a dynamically loaded
contract binds to members the trimmer never saw, so it is what must already be true the day trimming returns.

Restoring trimming means diagnosing the framework failure. Until then the deployed application is larger
than it should be.

## 4. Evidence

### 4.1 Server half — the real hosted path, ordinary publish

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07-client-contracts.sh` — exit 0. It builds
the solution, stages both packages by the real publish, publishes the browser client with no property of its
own, starts `Arronix.Api` with no test composition anywhere in it, and asserts over HTTP:

```text
ok    the video client contract is served at its content address
ok    a file the package does not offer is not served
ok    an entry assembly is not offered to a client
ok    an installed package that is not Active offers nothing
ok    a package this host never installed offers nothing
ok    a stale content address is refused as superseded
ok    the served bytes hash to the published content hash
ok    the served bytes are the staged package's bytes
ok    the manifest is served uncacheable
ok    a content address is served immutable
ok    a content address states the identity it will bind as
ok    the client project declares its own publish posture
ok    the published client carries no media or format assembly

address=http://127.0.0.1:5223
contractIdentity=Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null
publishedPackages=1
quarantined=movies
```

The client is served by the API from that same origin. There is no development host and no cross-origin
allowance, and the script writes nothing outside `artifacts/g07`.

`publishedPackages=1` and `quarantined=movies` are the honest state of a production Arronix host today; see
[residual gaps](#5-residual-gaps).

### 4.2 Browser half — a real Chromium against that server

Driven with Playwright against the running proof, on the published client the API serves. Service workers
are blocked in the cases that rewrite a response, because the published client registers a caching worker
whose fetches a page-level route never sees; the first two cases run with it in place.

| # | Case | Result |
| --- | --- | --- |
| 1 | Clean store | `Compatible`, projection permitted, outcome `Loaded`, source `Network`, one byte request; length, content hash, declared identity and declared module all agree, declared contract reference is this client's |
| 2 | Warm store | `Compatible`, outcome `Loaded`, source `Store`, **zero** byte requests |
| 3 | Manifest declares `Arronix.Abstractions 0.10.0.0` | `ContractIdentityMismatch`, projection withheld, every assembly `NotAttempted` |
| 4 | One byte of the payload flipped | `Refused`, `ContentHashMismatch`, failure names both hashes, nothing loaded |
| 5 | Manifest declares identity `Version=9.9.9.9` | `Refused`, `IdentityMismatch`, report states the identity the bytes actually declare |
| 6 | Manifest declares a different module identifier | `Refused`, `ModuleVersionMismatch`; the bytes hash correctly and are a different build |
| 7 | Manifest with an empty closure | `ManifestInvalid`, nothing fetched, refusal names the defect |
| 8 | Manifest carrying a withheld facet | `Compatible` and the withheld facet rendered, with its unreachable assemblies and its unadmitted files under separate headings |

Cases 3–7 all report **projection withheld**, and in every one the runtime was never handed the bytes. The
outcomes are read as names from the rendered page as well as from `#contract-proof`, because the report
serializes enumerations numerically and a number in a document is a number that goes stale.

### 4.3 Hermetic tests

`Arronix.Client.Tests` is new. It runs on the machine rather than in a browser because what it tests is what
the client decides *before* a runtime is involved, and because the question "was this payload loaded?" can
only be asked about an assembly the test host does not already hold. Its fixture assemblies are staged as
files, never referenced.

| Claim | Fixture |
| --- | --- |
| The metadata reader describes a real contract assembly, agreeing with `AssemblyName.GetAssemblyName`, without loading it | `MediaContractLoaderTests.TheReaderDescribesARealContractAssemblyWithoutLoadingIt` |
| An unreadable manifest, and a contract line this client does not carry, load nothing | `.AManifestThisClientCannotReadLoadsNothing`, `.AContractLineThisClientDoesNotCarryLoadsNothing` |
| Length, content hash, declared identity and declared module are each falsified alone and each refused before the runtime sees the bytes | `.AFalsifiedFactIsRefusedBeforeTheRuntimeSeesTheBytes` (4 cases) |
| A payload that is exactly what it says it is and is not built against this contract line is refused | `.APayloadThatDoesNotReferenceThisContractLineIsRefused` |
| A failed prerequisite stops a verified dependant from loading, and that dependant reports `Verified` | `.AFailedPrerequisiteStopsItsDependantFromLoading` |
| Nothing verified may be reached while the installation is refused | `.NothingMayBeProjectedFromARefusedInstallation` |
| A runtime returning an assembly other than the verified bytes is terminal, the entry is `RuntimeRefused`, a later verified entry stays `Verified`, and nothing is resident or projectable; then a verified installation loads, is reused as `Resident`, and becomes terminal when the host publishes a different build | `ContractCommitTests.AVerifiedInstallationIsLoadedReusedAndThenTerminalWhenTheHostMovesOn` |
| Nineteen malformed manifests are each named, and unreadable identifier text makes the whole document unreadable | `ContractManifestValidatorTests` (24 cases) |

`ClientFacetResolverTests` proves the withholding rule directly, because a fixed point is the kind of thing
that is either tested or believed: a direct binding outside the closure, a chain that only a fixed point
catches, a dependant withheld with its required facet, a requirement that offers a client nothing being no
cause at all, a refusal carrying every reason that applies, and the same graph in all six presentation
orders.

`PackagedClientContractTests` proves the same catalog over the real staged Movies-and-video installation:
both facets published, Movies' closure `arronix.format.video` then `movies`, published facts equal to the
staged file, the entry assembly and a stale hash refused, an installation that offers less hashing
differently, and a stopped installation offering nothing.

`ManifestSchemaTests` keeps `plugin.schema.json` — which sets `additionalProperties: false` — in step with
the reader and with every first-party manifest. That file had silently gone stale, which is how a valid
manifest ends up underlined in an editor.

### 4.4 Mutation evidence

Each guard was inverted with a mutation that compiles, the suite rebuilt, and the guard restored. A mutation
that fails to build proves nothing, because `--no-build` then runs the previous binary; two earlier runs in
this work were invalid for exactly that reason and were redone.

| Mutation | Result |
| --- | --- |
| The loader loads whatever verified instead of all-or-nothing | 9 of 10 client cases fail |
| The length check is skipped | 4 fail |
| The identity check is skipped | 2 fail |
| The module version check is skipped | 1 fails |
| The contract reference check is skipped | 1 fails |
| The post-load disagreement branch is never taken | `ContractCommitTests` fails |
| The resolver stops cascading to dependants of withheld facets | 3 resolver cases fail |
| `ClientContractCatalog.Open` ignores the content address | `NothingOutsideTheDeclaredFacetIsOffered` fails |
| The client facet is validated against its own declaration rather than the published contracts | both manifest cases fail |
| A `GetTypes()` call is added to the client's source | `ClientDiscoversNothingByEnumeratingALoadedAssembly` fails, naming file and line |

### 4.5 Full rail

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/ci/run-tests.sh` — exit 0.

```text
projects=13 total=3176 enabled=2874 passed=2874 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0 requiredTests=3
compileLogs=1 compileProjects=13 compileItems=299 boundSources=15
```

The registered skip count is unchanged at 302 — 301 Movies cases and one architecture case — and both
ratchets pass. Per project:

| Project | Passed | Skipped |
| --- | --- | --- |
| `Arronix.Host.Tests` | 576 | 0 |
| `Arronix.Plugins.Tests` | 547 | 0 |
| `Arronix.Common.Tests` | 427 | 0 |
| `Arronix.Architecture.Tests` | 374 | 1 |
| `Arronix.Plugin.Movies.Tests` | 370 | 301 |
| `Arronix.Provider.Tmdb.Tests` | 132 | 0 |
| `Arronix.Abstractions.Tests` | 119 | 0 |
| `Arronix.Compatibility.Ratchet.Tests` | 103 | 0 |
| `Arronix.Plugin.Tv.Tests` | 87 | 0 |
| `Arronix.Plugin.Music.Tests` | 66 | 0 |
| `Arronix.Client.Tests` | 55 | 0 |
| `Arronix.Format.Video.Tests` | 10 | 0 |
| `Arronix.Generators.Tests` | 8 | 0 |

## 5. Residual gaps

1. **The shipping client publishes untrimmed.** Section 3. Technical debt, not an open exit condition.

2. **No production host can activate a package with an entry assembly.** `PluginPlatformServices` requires
   `ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`, `IHostRuntimeInfo` and `IOperatingSystemInfo`,
   and none of the five has an implementation outside the test projects. A real server therefore quarantines
   Movies before it can publish a client facet, and only the contract-only video package reaches a browser.
   This predates G07 and blocks G07.2, whose whole subject is the Movies item graph. Building the five is
   the roadmap's next task.

4. **The browser half is not on the hermetic test rail.** `eng/ci/run-tests.sh` carries no browser
   toolchain, and adding one would put a browser download in the clean-repository proof. The harness stands
   the real thing up and proves the server half; the browser half is operator-run and its exact output is
   quoted above. `#contract-proof` exists so any browser driver can read the client's complete report.

5. **No provenance.** Content hashes say which bytes; no signature says who vouched for them. Package
   signing remains unbuilt, as recorded for G03.

6. **One store, one page, no unload.** A browser cannot unload an assembly, so a second pass reuses what a
   first pass loaded and a page holding a contract the host has stopped publishing is terminal until
   reloaded. Update, removal and stale-cache handling are G07.3.

7. **`Cache Storage` is not universally available.** Outside a secure context the store is absent and the
   loader refetches — slower and exactly as correct — and the page says which mode it is in.

8. **The API had never been started.** `src/Arronix.Api/appsettings.json` declared no
   `Arronix:Identity:ApplicationName`, which `HostIdentityOptions` requires, so `dotnet run` on the server
   failed options validation at startup. One line was added. Nothing else in the API's shipped configuration
   has been exercised against a running process.
