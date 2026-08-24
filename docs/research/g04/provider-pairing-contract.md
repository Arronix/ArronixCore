# G04 — closing the typed provider-pairing contract

**Branch:** `claude/g04-provider-pairing`
**Base commit:** `bbed12a6993b6cfb59f452e6e9ac690efa66360f` (`Port the dependency pipeline proofs onto the reconciled seams`)
**SDK:** `/usr/local/share/dotnet/dotnet`, pinned .NET 11 Preview 7 `11.0.100-preview.7.26381.103`

**Outcome:** three of G04's five work items are closed and proved. The fourth — durable identity and catalog
materialization — is deliberately left at its honest boundary with a bounded owner decision record, because
answering it means choosing an identity policy the roadmap explicitly declines to pre-approve.

---

## 1. What changed, and the semantics behind each change

### 1.1 Provider family, item type and identity each have exactly one authority

Before this branch three facts each had several declarative homes and nothing compared them.

| Fact | Authorities before | Authority now |
|---|---|---|
| Provider family | the registration method, `ProviderDescriptor.Family`, `IProvider.Family` | the registration method alone |
| Media item type | the closed contract, the `TItem` argument on `AddCataloger`/`AddCurator`, `ProviderTypeRegistration.MediaItemType` | the closed contract alone; the registration reads it back |
| Provider identity | `ProviderRegistry.Register` minting it, `IProvider.Id` minting the same value again | the host mint alone |

The identity restatement was not merely redundant. `IndependentMovieCurator` published
`ProviderId.Create(Package, "independent-list")` while the declaration it was registered with said
`LocalId = "independent-curator"`; the two disagreed, and no code path could notice. That disagreement is
gone because the losing authority is gone.

Files:

- `src/Arronix.Abstractions/Providers/IProvider.cs` — `Id` and `Family` removed; the remarks now say where
  each comes from and that a provider needing its own identifier reads
  `ProviderInvocation.Definition.Provider`.
- `src/Arronix.Abstractions/Providers/ProviderDescriptor.cs` — `Family` removed.
- `src/Arronix.Abstractions/Providers/ProviderCatalogEntry.cs` *(new)* — the host-built wire projection
  pairing the minted `ProviderId`, the registration's family, and the extension's declaration.
- `src/Arronix.Host/Providers/ProviderRegistry.cs` — `RegisteredProvider.Catalog` builds that entry once.
- `src/Arronix.Plugins/Loading/PluginLoader.cs` — the duplicate-provider check reads the family from the
  registration rather than from the declaration; the `DescribedProviders` helper it needed is gone.
- `src/Arronix.Plugin.Books`, `src/Arronix.Plugin.Music` — the reference indexers stamp listings with
  `invocation.Definition.Provider` instead of a locally re-minted identifier.
- `src/Arronix.Plugin.Tv` — the same members removed; the indexer keeps a private field for its own
  synthetic release-key namespace, which is not a published answer to either question. Threading the
  invocation through its release-synthesis chain is TV-conversion work, not this branch's.

### 1.2 The closed pairing is stated once and checked at the call site

`AddCataloger<TItem, TCataloger>(descriptor)` became `AddCataloger<TCataloger>(descriptor)`, and the same
for `AddCurator`. The item type and the closed contract are read from the contract the implementation
already closed:

```csharp
public interface ICatalogerPairing
{
    static abstract Type PairedItemType { get; }
    static abstract Type PairedContractType { get; }
}

public interface ICataloger<TItem> : ICataloger, ICatalogerPairing
    where TItem : class, IMediaItem
{
    static Type ICatalogerPairing.PairedItemType => typeof(TItem);
    static Type ICatalogerPairing.PairedContractType => typeof(ICataloger<TItem>);
    ...
}
```

Three properties this buys, each asserted rather than assumed:

- **An author names the item type once.** `ProviderTypeRegistration.ForCataloger<T>` / `ForCurator<T>` read
  both types statically. There is no reflection over interface lists and no second argument to synchronize.
- **The mistake is a compiler error, not an admission failure.** A type that closed no media contract, or a
  curator passed to `AddCataloger`, fails at the registration call site with `CS0311` naming the missing
  conversion.
- **One class may serve both families.** The two pairing interfaces deliberately share no base. A common
  base declaring the members once is smaller and breaks exactly this case: a class implementing both
  `ICataloger<Movie>` and `ICurator<Movie>` inherits two implementations of one static abstract member and
  fails with `CS8705`, "does not have a most specific implementation", which the author cannot sensibly
  resolve. This was probed against the pinned SDK before the shape was chosen, and the working shape is
  asserted by `OneImplementationMayServeBothFamiliesAndEachRegistrationKeepsItsOwnPairing`.

`ICatalogerPairing` and `ICuratorPairing` are public only because an interface cannot inherit a less
accessible one. They are binding SPI: an author never implements or names them, which is asserted per
implementation, and `INTERFACE.md` now lists them beside the other `System.Type` carriers that public
visibility does not promote to authoring vocabulary. Moving or hiding that class of surface is G06's work.

Files: `src/Arronix.Abstractions/Providers/MediaPairing.cs` *(new)*, `ICataloger.cs`, `ICurator.cs`,
`Registrations.cs`, `src/Arronix.Abstractions/Plugins/IPluginRegistry.cs`,
`src/Arronix.Plugins/Registration/PluginRegistry.cs`.

### 1.3 Admission refuses a mismatched or inactive media contract before provider code runs

`PluginBootstrapper.TryPrepareProviders` now runs `TryCheckMediaPairings` over every provider registration
in a package **before** it constructs any of them. A registration carrying a `MediaItemType` must match the
item type of a typed kind either admitted by the same attempt or already active in the installation.

This is a different claim from G03's dependency check. That one proves a package with a given identifier is
installed and compatible; it cannot prove that some kind is closed over the exact CLR type a cataloger's
contract names. A refusal carries `CoreErrorCode.PluginMediaPairingUnsatisfied` and names the item type the
provider closed over plus the item types the installation does supply.

The pre-pass matters: one unpairable provider means **none** of the package's providers is constructed, so a
contribution the installation has already decided it cannot use never gets a constructor call.

`CoreErrorCode` gained one member, `PluginMediaPairingUnsatisfied = 2014`. The existing
`PluginContractMismatch` is documented as a *contract version* mismatch and the dependency codes are about
packages, so reusing either would have made an operator's diagnostic less precise than the check.

Files: `src/Arronix.Host/Runtime/PluginBootstrapper.cs`,
`src/Arronix.Abstractions/Health/CoreErrorCode.cs`.

### 1.4 The consumer edge, traced end to end

`ProviderCatalogEntry` was kept only because real consumers need it, and every one was followed:

| Consumer | Before | After |
|---|---|---|
| `Arronix.Api/Endpoints/ProviderEndpoints.ListProviders` | returned `ProviderDescriptor`; filtered on `Descriptor.Family` | returns `ProviderCatalogEntry`; filters on the registered family |
| `Arronix.Client/Services/ArronixApiClient.GetProvidersAsync` | `IReadOnlyList<ProviderDescriptor>` | `IReadOnlyList<ProviderCatalogEntry>` |
| `Arronix.Client/Providers/ProviderCatalog.razor` | grouped on `Descriptor.Family`; tried `ProviderId.TryParse(descriptor.LocalId)` and rendered *unavailable* whenever it failed, which was always | groups on the entry's family; hands the entry straight to the caller. The `CatalogChoice` pairing record and the blocked affordance are both gone |
| `Arronix.Client/Pages/SettingsPage.razor` | matched a stored definition to a declaration by comparing `LocalId` against two spellings of the provider identifier | matches on `entry.Provider == definition.Provider`; the `Editing` record lost a field |
| `Arronix.Client/Providers/ProviderForm.razor` | took `Descriptor` and `Provider` as two parameters that had to agree, and built a definition with `Family = Descriptor.Family` | takes one `Entry` |

The entry is not a second descriptor-shaped model: it adds no fact the declaration already carries, and it
carries the two the declaration must not. It also fixes a defect the client had written down against itself
in a comment — that it could not add any provider at all, because the catalog never told it which extension
supplied one.

---

## 2. What was deliberately not done

- **No materialization seam.** Adding the first Host member that consumes a shaped `ICataloger<TItem>` or
  `ICurator<TItem>` result requires the durable identity rule to exist. It does not, so none was added, and
  a test asserts none exists.
- **No candidate, draft or partially-materialized type**, no optional `MediaItemId`, no softening of
  `MediaItemId`'s "host-minted surrogate" documentation, no provider-minted identity, no field bag. Each of
  those would have decided the open question by omission.
- **`ProviderDefinition.Family` was left in place.** It looks like a fourth restatement and is not: a
  definition whose implementation has been uninstalled is retained and marked orphaned, and it must still be
  listable and routable by family with no registration to derive one from. The type documents that reason;
  this branch verified it rather than removing it.
- **The `MovieCatalogerFixture` key fabrication was left as it is.** It is labelled in its own source as
  compile-time package-shape evidence and explicitly not provider coverage. Rewriting it would hide the gap.
- **`Arronix.Plugin.Tv`'s private release-key identifier was left as it is.** It is not a contract member
  and threading the invocation through TV's release synthesis belongs to TV's conversion.

---

## 3. Tests and exact counts

### New cases: 25, all passing

| File | Cases | What it holds |
|---|---|---|
| `src/Arronix.Architecture.Tests/Topology/ProviderPairingAuthorityTests.cs` | 17 | the detector's own control; per-implementation rules that no provider publishes an identifier or a family and none writes its own pairing members (5 implementations × 2); the registration methods take one type argument and demand the pairing; the two pairing contracts are independent; a registration derives item type, contract and family from the contract alone; one class serving both families keeps a pairing per family; and neither consumer edge mints or parses a provider identity outside serialization (2 cases) |
| `src/Arronix.Host.Tests/Runtime/ProviderMediaPairingAdmissionTests.cs` | 4 | a provider is admitted against a kind prepared in the same attempt; one whose item type no installed kind supplies is refused with `PluginMediaPairingUnsatisfied` and never constructed; one unresolvable pairing stops every provider in the package being constructed; the refusal names the item types the installation does supply |
| `src/Arronix.Host.Tests/Providers/CatalogMaterializationBoundaryTests.cs` | 3 | a typed cataloger result projects through the kind-blind runtime as the exact item with its media-owned lifecycle intact; no Host seam consumes a shaped cataloger or curator result; a durable item key is required of the provider and minted by nobody |
| `src/Arronix.Plugins.Tests/Registration/RegistryAdmissionTests.cs` | 1 | a curator registration retains the same item type through its own contract, with the family from the registration |

Per-project totals after the change (base → now): Architecture `313 → 330` passed (1 registered skip
unchanged), Host `528 → 535`, Plugins `492 → 493`.

### Focused runs

```
Arronix.Plugins.Tests          493 passed,   0 skipped, 0 failed
Arronix.Host.Tests             535 passed,   0 skipped, 0 failed
Arronix.Architecture.Tests     330 passed,   1 skipped, 0 failed
Arronix.Abstractions.Tests     119 passed,   0 skipped, 0 failed
Arronix.Plugin.Tv.Tests         87 passed,   0 skipped, 0 failed
Arronix.Plugin.Music.Tests      66 passed,   0 skipped, 0 failed
```

### Full rail — `DOTNET_COMMAND=/usr/local/share/dotnet/dotnet ./eng/ci/run-tests.sh`

```
projects=11 total=2844 enabled=2542 passed=2541 failed=1 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0
requiredTests=3 compileLogs=1 compileProjects=11 compileItems=260 boundSources=15
error execution.failed: The run contains 1 failed cases.
```

Release build: 0 warnings, 0 errors. Skips unchanged at the ratcheted 302 (301 Movies, 1 architecture).
Ledger unchanged at 302 cases, 0 replacements; three required sentinels pass.

**The one failure is pre-existing and not owned by this branch.**
`Arronix.Plugin.Movies.Tests.Shape.ManifestOwnershipTests.CarriesNothingButWhatItOwns` fails at base commit
`bbed12a69`; it was reproduced there by stashing this branch's changes and rerunning. The manifest declares
`contractAssemblies` and `dependencies` — landed by G03's dependency pipeline — while the test's
`ManifestOwnedProperties` allow-list and `INTERFACE.md`'s manifest paragraph both still say those do not
exist. The fix is a G03 record catching up with G03's own landed schema, in the test's allow-list and in
that paragraph together; making it here would be this branch asserting a G03 ownership answer. The
independent G05 branch reached the same conclusion about the same case.

---

## 4. Unresolved owner decisions

**One, and it is the remainder of G04's exit gate:** who chooses a catalog item's durable `MediaItemId`, and
how collision, merge, redirect and retry resolve.

Full record, with the constraints, the alternatives, what each forecloses, and the one-sentence answer that
unblocks the rest: **`docs/research/g04/media-item-identity-decision.md`**.

The short form: `MediaItem.Key` is a `required MediaItemId`, so a cataloger cannot return a valid item
without choosing one; `MediaItemId` documents itself as a host-minted surrogate that nothing outside the
platform chooses. The decision separates into a *mechanism* (how an item travels before its key exists) and
a *policy* (whether the durable key is a host surrogate over an owned natural-key map, or the vendor's
natural key itself). Only the second determines whether the already-declared
`CatalogerCapabilities.IdentifierRedirects` can mean anything.

This is corroborated independently. The G05 TMDb pressure test (`claude/g05-tmdb-proof`, commit
`72de1fa5a`, `docs/research/g05/tmdb-provider-pressure-test.md`) built a real vendor package, mapped every
`Movie` fact including the lifecycle, and stopped at exactly three members — `SearchAsync`, `GetAsync`,
`FetchAsync` — rather than fabricate a key.

### G04 exit gate, item by item

| Criterion | State |
|---|---|
| a cataloger or curator author names `Movie` once | **met** — one generic position per contract, none in registration |
| admission rejects a provider whose closed item contract has no active matching media package | **met** — refused with `PluginMediaPairingUnsatisfied` before any construction |
| the closed registration is the single authority for family, and the Host-qualified registration for identity | **met** — the competing authorities are removed, not reconciled |
| durable identity has one documented authority, collision, merge and retry rule reflected in `INTERFACE.md` | **open** — recorded as an owner decision; `INTERFACE.md` states that it is open and where |
| Host can produce a fully valid `Movie` without a public field bag or temporarily invalid domain object | **open** — blocked on the same decision |
| any approved construction type is an exact typed lifecycle boundary | **not applicable yet** — nothing was approved, so nothing was built |

---

## 5. Integration notes

**For the G05 TMDb provider package.** Its module and declarations need three mechanical edits against this
branch, and no logic change:

- `AddCataloger<Movie, TmdbMovieCataloger>(descriptor)` → `AddCataloger<TmdbMovieCataloger>(descriptor)`;
  same for the curator.
- Remove `Family = ProviderFamily.Cataloger` / `Curator` from its `ProviderDescriptor` initializers.
- Remove `ProviderId Id` and `ProviderFamily Family` from `TmdbMovieCataloger` and `TmdbMovieCurator`. Note
  that removing them is not signalled by a compiler error — they stop implementing anything and become
  ordinary public members — but `ProviderPairingAuthorityTests` fails on them once the package is added to
  the assemblies that fixture scans.
- If TMDb ends up serving a second media kind from one class, that now compiles and each registration keeps
  its own pairing.

**For whoever closes the identity decision.** The three blocked provider members and
`TmdbMovieMapper.ToMovie` are unchanged by whatever is chosen; only the identity assignment moves. On the
Host side, `CatalogMaterializationBoundaryTests.NoHostSeamConsumesAShapedCatalogerOrCuratorResult` is the
tripwire: it fails the moment a materialization seam is added, which is the moment the decision must already
have been made.

**For G06.** `ICatalogerPairing` and `ICuratorPairing` join `CompiledShapes`, the capture visitors and the
erased registrations as public-but-not-authoring surface. They are the newest members of the set G06 exists
to move or hide, and `ProviderPairingAuthorityTests` already asserts that no author implements them.

**For G03.** `ManifestOwnershipTests.CarriesNothingButWhatItOwns` and `INTERFACE.md`'s sentence that
"explicit package dependencies are future G03 ownership and do not exist in the current schema" are both
stale against the landed manifest. They are one change and it belongs to G03.
