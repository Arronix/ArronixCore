# G07.1 — publishing, caching, and loading the exact client-safe contract package

**Status:** implemented. G07 was split into three numbered sub-gates before implementation; this records
G07.1 and nothing else. G07.2 (generated metadata, typed deserialization and rendering) and G07.3 (update,
removal, stale cache and stale tab) are open, and none of their work is claimed here.

This is a record of a change, not a proposal. Every rule below is asserted by a named fixture or by a
command whose exact output is quoted.

## 1. What was built

A browser client acquires an installed media kind's typed CLR contract from whichever host served it,
proves it against what that host published, and loads it — with no compiled dependency on any media or
format package anywhere in the client build.

The whole protocol is four things:

1. **A declared client facet.** `plugin.json` gains `clientContracts`: zero or more of the package's own
   published shared contract assemblies, which a browser may download. It is validated as a subset of
   `contractAssemblies`, so an entry assembly can never appear in it — that is refused one rule earlier —
   and neither can a file the package does not publish. Declared, never inferred, for the same reason the
   shared list is: what is named here leaves the host's process.
2. **A projection of the running installation.** `IClientContractCatalog` reads the Active plugin registry
   and, for each package that declared a facet, publishes each offered assembly's SHA-256 content hash, its
   complete CLR identity, its module version identifier and its length; and per package, its transitive
   client dependency closure in load order plus one hash over that closure. One further hash covers the
   whole installation.
3. **Content-addressed serving.** `GET /api/v1/client-contracts` returns that manifest, uncacheable.
   `GET /api/v1/client-contracts/{package}/{contentHash}/{fileName}` returns the exact admitted bytes,
   `immutable`. An address names bytes, so it can be held forever; an installation that changes mints new
   addresses instead of changing what an old one means.
4. **A verifying browser loader.** `MediaContractLoader` compares the host's universal contract identity
   with its own before anything else, keeps bytes in a content-hash-keyed Cache Storage store, hashes
   whatever it receives from wherever it received it, loads through
   `AssemblyLoadContext.Default.LoadFromStream`, and then proves the loaded assembly's identity, its module
   version identifier, and that its reference to the universal contract resolves — by object identity — to
   the client's own compiled contract assembly.

## 2. Decisions worth stating

**The bytes served are the bytes admitted.** `AdmittedContract` retains the staged bytes rather than the
catalog re-reading `SourcePath`. The loader's own rule is that a file a package owns is read once, because
a second read is a race in which the file inspected and the file handed on need not be the same file; here
that race ends with a browser holding an assembly the host never proved. The retained set is bounded by an
installation's shared contract assemblies, which are domain contracts.

**A superseded address is `410 Gone`, not `404`.** The file is still published, at a different address, so
re-reading the manifest is the recovery. A client told "not found" would give up instead.

**Object identity, not name equality.** `bindsToClientContract` is
`ReferenceEquals(AssemblyLoadContext.Default.LoadFromAssemblyName(reference), typeof(IMediaEntity).Assembly)`.
Two assemblies with one name is exactly the failure a shared contract exists to prevent, and only the loader
can say whether a particular load avoided it.

**An incompatible installation is refused whole.** If the host's `Arronix.Abstractions` identity is not the
client's, nothing is loaded and the page says why. Loading the part of an installation that happens to
resolve would render values whose meaning nothing has agreed on.

**The client enumerates nothing.** The loader reads an assembly's identity, its manifest module and its
reference table, and no more. `ClientTopologyTests.ClientDiscoversNothingByEnumeratingALoadedAssembly`
rejects `GetTypes`, `GetExportedTypes`, `GetProperties`, `GetFields`, `GetMethods`, `GetMembers` and
`Activator.CreateInstance` anywhere in the client's source. Discovery of what a contract *contains* is
G07.2's generated metadata; enumerating it here would make the client untrimmable and would make property
reflection a second media schema beside the typed contracts.

**`Arronix.Abstractions` is a trimmer root in the client.** A dynamically loaded contract binds to members
the trimmer never saw, so a trimmed contract assembly is one whose surviving surface is whatever the client
happened to call. Nothing else is rooted.

## 3. Evidence

### 3.1 Server half — the real hosted path

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07-client-contracts.sh` builds the solution,
stages both packages by the real publish, publishes the browser client, starts `Arronix.Api` with no test
composition anywhere in it, and asserts over HTTP:

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
ok    the published client carries no media or format assembly

address=http://127.0.0.1:5223
contractIdentity=Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null
installationHash=B82F8262F663FF53CA92934D6F84B80749570A6A8CDAB1D1A4BC1B27B9B288DA
publishedPackages=1
video=Arronix.Format.Video.dll C453292985EA78735B81C899A011769BAD2C6A86FA406A2F135577B00E6701FC Arronix.Format.Video, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
quarantined=movies
```

`publishedPackages=1` and `quarantined=movies` are the honest state of a production Arronix host today; see
[residual gaps](#5-residual-gaps).

### 3.2 Browser half — a real Chromium against that server

Driven with Playwright against the running proof. The client is served on its own origin by the WebAssembly
development host and the API admits that origin, because the *published* client cannot start on this SDK at
all; see [residual gaps](#5-residual-gaps). The routes under proof are served by the real API either way.

**Clean store.** After `caches.delete('arronix-client-contracts-v1')` and a reload, the page's
`#contract-proof` element held:

```json
{
  "compatibility": 0,
  "publishedContractIdentity": "Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null",
  "clientContractIdentity": "Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null",
  "installationHash": "B82F8262F663FF53CA92934D6F84B80749570A6A8CDAB1D1A4BC1B27B9B288DA",
  "packages": [{
    "id": "arronix.format.video", "version": "0.1.0", "name": "Video Format",
    "closureHash": "DC7E3DFA31AB02FB3DF5DBBF41DD0C76423FCD29BE7D6FBB1F795008756F8C38",
    "closure": ["arronix.format.video"],
    "assemblies": [{
      "published": {
        "assemblyName": "Arronix.Format.Video",
        "fileName": "Arronix.Format.Video.dll",
        "identity": "Arronix.Format.Video, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "contentHash": "C453292985EA78735B81C899A011769BAD2C6A86FA406A2F135577B00E6701FC",
        "moduleVersionId": "5f55ed2c-00c0-4d53-b2a6-c1b154c41da7",
        "length": 28160
      },
      "outcome": 0,
      "source": 0,
      "observedContentHash": "C453292985EA78735B81C899A011769BAD2C6A86FA406A2F135577B00E6701FC",
      "observedIdentity": "Arronix.Format.Video, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
      "observedModuleVersionId": "5f55ed2c-00c0-4d53-b2a6-c1b154c41da7",
      "bindsToClientContract": true
    }]
  }],
  "storeAvailable": true
}
```

`outcome: 0` is Loaded and `source: 0` is Network. The browser's own resource timeline recorded exactly two
requests to the host for this: the manifest, and the one content address.

**Warm store.** Reloading the same page:

```text
outcome=0 (Loaded)   source=1 (Store)   bindsToClientContract=true   storedKeys=1
requests: /api/v1/client-contracts
```

The bytes were not refetched; only the manifest was read.

**Incompatible client.** With `window.fetch` patched to answer the manifest with
`Version=0.10.0.0` and "Reload contracts" clicked:

```text
compatibility = 1 (ContractIdentityMismatch)
packages      = 0
chip          = "ContractIdentityMismatch"
visible text  = "This host publishes media contracts against 'Arronix.Abstractions, Version=0.10.0.0, ...',
                 and this client was compiled against 'Arronix.Abstractions, Version=0.9.0.0, ...'.
                 Nothing was loaded: ..."
```

Nothing was projected, and the failure is on the page rather than in a log.

**Corrupted bytes.** With the store cleared and one byte of the byte route flipped by a Playwright route
handler, on a page that had loaded nothing:

```text
outcome  = 2 (ContentHashMismatch)
published = C453292985EA78735B81C899A011769BAD2C6A86FA406A2F135577B00E6701FC
observed  = B035F7C503F16A4401A54E4DE4AC3BC4DE78AC3AB5C59FE3B8F71B2005744A20
observedIdentity = null
failure  = "'Arronix.Format.Video.dll' hashed to B035F7...; the host published C45329.... Nothing was loaded."
```

The runtime never saw the bytes.

### 3.3 The complete Movies-and-video installation

The production server cannot activate a package with an entry assembly yet, so the two-package proof lives
in `Arronix.Host.Tests`, which installs the same real staged package folders and supplies the five platform
services no production host implements. `PackagedClientContractTests` (5 cases, all passing):

| Claim | Fixture |
| --- | --- |
| Both packages publish a facet; Movies' client closure is `arronix.format.video` then `movies` | `.EachPackageStatesItsClientClosureDependencyFirst` |
| The published hash, identity, module and length match the staged file, and `Open` returns those exact bytes | `.AClientFacetCarriesTheExactAdmittedBytesIdentityAndModule` |
| The entry assembly, another package's contract, an uninstalled package and a stale hash are each refused, the last as superseded | `.NothingOutsideTheDeclaredFacetIsOffered` |
| An installation offering less hashes differently, while an unchanged package's closure hash does not move | `.AnInstallationThatOffersLessHashesDifferently` |
| A stopped installation offers a browser nothing | `.AStoppedInstallationOffersNothing` |

`ManifestValidatorTests` adds cases for the facet declaration: the subset rule, the ordinary offers-nothing
package, a file the package does not publish, a name that could leave the package folder, and a duplicate
entry. `ManifestOwnershipTests` pins Movies' own facet to its media domain and admits `clientContracts` to
the manifest-owned list for the reason `capabilities` is on it — a decision about what leaves the host's
process cannot be derived from the code it governs.

### 3.4 Mutation evidence

Each guard was locally inverted, the suite run, and the guard restored. The working tree carries none of
them.

| Mutation | Result |
| --- | --- |
| `ClientContractCatalog.Open` serves whatever the package offers, ignoring the content address | `NothingOutsideTheDeclaredFacetIsOffered` fails |
| The client facet is validated against its own declaration rather than the published contracts | Both `AClientFacetNamingAFileThePackageDoesNotPublishIsRefused` cases fail |
| A `GetTypes()` call is added to the client's source | `ClientDiscoversNothingByEnumeratingALoadedAssembly` fails, naming the file and line |

### 3.5 Full rail

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/ci/run-tests.sh` — exit 0.

```text
projects=12 total=3089 enabled=2787 passed=2787 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0 requiredTests=3
compileLogs=1 compileProjects=12 compileItems=289 boundSources=15
```

Against the G06 close (2,771 enabled) this is +16 cases: five in `Arronix.Host.Tests`, ten in
`Arronix.Plugins.Tests`, and one in `Arronix.Architecture.Tests`. The registered skip count is unchanged at
302 — 301 Movies cases and one architecture case — and both ratchets pass. Per project:

| Project | Passed | Skipped |
| --- | --- | --- |
| `Arronix.Host.Tests` | 576 | 0 |
| `Arronix.Plugins.Tests` | 519 | 0 |
| `Arronix.Common.Tests` | 427 | 0 |
| `Arronix.Architecture.Tests` | 370 | 1 |
| `Arronix.Plugin.Movies.Tests` | 370 | 301 |
| `Arronix.Provider.Tmdb.Tests` | 132 | 0 |
| `Arronix.Abstractions.Tests` | 119 | 0 |
| `Arronix.Compatibility.Ratchet.Tests` | 103 | 0 |
| `Arronix.Plugin.Tv.Tests` | 87 | 0 |
| `Arronix.Plugin.Music.Tests` | 66 | 0 |
| `Arronix.Format.Video.Tests` | 10 | 0 |
| `Arronix.Generators.Tests` | 8 | 0 |

## 4. What this does not claim

- No typed `Movie` graph was deserialized or rendered. That is G07.2, and it needs generated metadata that
  does not exist yet; the loader deliberately performs no type discovery.
- No update, removal, stale-cache or stale-tab behavior was exercised. That is G07.3. The store is keyed by
  content hash, so it cannot hold the wrong bytes under the right name, but nothing yet evicts an entry the
  installation stopped naming.
- Nothing here verifies provenance. Content hashes say which bytes; no signature says who vouched for them.
  Package signing remains unbuilt, as recorded for G03.
- Trimming was not exercised end to end. The `wasm-tools` workload is not installed in this environment, so
  the published client is built without ahead-of-time compilation, and the trimmer root was added on the
  reasoning above rather than on an observed failure it prevents.

## 5. Residual gaps

1. **No production host can activate a package with an entry assembly.** `PluginPlatformServices` requires
   `ICacheProvider`, `ITelemetryEmitter`, `IEventPublisher`, `IHostRuntimeInfo` and `IOperatingSystemInfo`,
   and no implementation of any of them exists outside the test projects. A real server therefore
   quarantines Movies with `PluginActivationRefused` before it can publish a client facet, and only the
   contract-only video package reaches a browser. This predates G07 and blocks G07.2, whose whole subject is
   the Movies item graph. It is host work, not client work, and inventing five platform services here would
   have been exactly the scope creep the roadmap forbids.

2. **The published browser client cannot start on .NET 11 preview 7.** Serving a `dotnet publish` output of
   `Arronix.Client` — trimmed or untrimmed, self-contained or not, from the API or from a plain static
   server — fails during startup with:

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

   The same build served by the WebAssembly development host starts normally, and a default Blazor
   WebAssembly template application publishes and starts normally with the same SDK. It reproduces with
   every Arronix service registration removed, with the client's JSON converters removed, with
   `PublishTrimmed=false`, with `SelfContained=true`, and with the RID properties matching the template, so
   it is neither a service registration nor a trimming casualty. It was not diagnosed further: it is a
   startup defect in the client's own shipping path with no bearing on the contract protocol, and it belongs
   to whoever next owns the client's packaging. **Until it is fixed the client cannot ship at all**, which is
   a larger problem than G07.

3. **The browser half is not on the hermetic test rail.** `eng/ci/run-tests.sh` carries no browser
   toolchain, and adding one would put a browser download in the clean-repository proof. The harness stands
   the real thing up and proves the server half; the browser half is operator-run and its exact output is
   quoted above. `#contract-proof` exists so any browser driver can read the client's complete report.

4. **One store, one page, no unload.** A browser cannot unload an assembly, so a second load pass reuses
   what a first pass loaded and reports `AlreadyLoaded`. An installation that changes underneath a live tab
   is a condition to report rather than to reconcile, and reporting it is G07.3.

5. **`Cache Storage` is not universally available.** Outside a secure context the store is absent and the
   loader refetches. That is slower and exactly as correct, and the page says which mode it is in.

6. **The API had never been started.** `src/Arronix.Api/appsettings.json` declared no
   `Arronix:Identity:ApplicationName`, which `HostIdentityOptions` requires, so `dotnet run` on the server
   failed validation at startup before any of this work could be proved. One line was added. Nothing else in
   the API's configuration has been exercised against a running process.
