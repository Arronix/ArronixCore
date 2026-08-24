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

### 1.2 The closed pairing is read from the contract the implementation implements

`AddCataloger<TItem, TCataloger>(descriptor)` became `AddCataloger<TCataloger>(descriptor)`, and the same
for `AddCurator`. The item type and the closed contract come from the contract the implementation actually
implements:

```csharp
public interface IClosedCataloger : ICataloger;     // a family marker; no members
public interface IClosedCurator : IProvider;

public interface ICataloger<TItem> : ICataloger, IClosedCataloger
    where TItem : class, IMediaItem { ... }

IPluginRegistry AddCataloger<TCataloger>(ProviderDescriptor descriptor)
    where TCataloger : class, IClosedCataloger;
```

**A first attempt at this got it wrong, and the correction is the substance of this section.** The markers
originally carried `static abstract Type PairedItemType`/`PairedContractType`, answered by
`ICataloger<TItem>` from its own type argument, and registration trusted those answers. An interface can be
implemented directly, so a class implementing only the non-generic floor plus the marker — never
`ICataloger<Movie>` — satisfied the constraint, reported `Movie` and `ICataloger<Movie>`, passed the media
pairing check, and reached its constructor; only the post-construction check caught it, which is after the
thing the check exists to prevent. It was reproduced with a scratch compiler probe, and the same probe
confirms the refusal afterwards:

```
REFUSED AT REGISTRATION: 'SpoofCataloger' is registered as a Cataloger but implements no 'ICataloger`1'.
Implement the closed contract — the item type is read from it, and a marker interface alone states nothing
the platform can check.
```

The fix removes the members rather than adding an agreement check between them and the truth. What each
part now does, stated exactly:

- **The constraint is a family gate, not a proof.** `where TCataloger : class, IClosedCataloger` makes a
  provider of the wrong family — a curator handed to `AddCataloger`, or a type that is not a cataloger at
  all — a `CS0311` at the registration call site. It cannot prove a closed contract exists, because the
  marker can be implemented directly.
- **The pairing is read by one-time type inspection.** `ProviderTypeRegistration.ForCataloger<T>` /
  `ForCurator<T>` scan `typeof(T).GetInterfaces()` for closed constructions of `ICataloger<>` / `ICurator<>`
  and take the contract and its type argument from the one they find. This is erasure at the boundary the
  north star sanctions, and it is stated in the method's own documentation rather than presented as
  something the compiler established.
- **Zero and several are both refused**, at registration, inside the extension's own configure method.
  Several are named in the message, because choosing one would make the erased registration disagree with a
  contract the author actually wrote.
- **An author still names the item type once**, and one class may serve both families: each registration
  reads only its own family's contract. (The markers share no base — which also means there is no diamond,
  and the earlier justification appealing to `CS8705` no longer applies, because the members that caused it
  are gone. The reason to keep them separate is exactness of family, not ambiguity.)

`IClosedCataloger` and `IClosedCurator` are public only because an interface cannot inherit a less
accessible one. They are binding SPI: an author never implements or names them, which is asserted per
implementation, and `INTERFACE.md` lists them beside the other `System.Type` carriers that public
visibility does not promote to authoring vocabulary. Moving or hiding that class of surface is G06's work.

Files: `src/Arronix.Abstractions/Providers/MediaPairing.cs` *(new)*, `ICataloger.cs`, `ICurator.cs`,
`Registrations.cs`, `src/Arronix.Abstractions/Plugins/IPluginRegistry.cs`,
`src/Arronix.Plugins/Registration/PluginRegistry.cs`.

### 1.3 Admission proves the whole relationship, before provider code runs

`PluginBootstrapper.TryPrepareProviders` runs `TryCheckProviderContracts` over every provider registration
in a package **before** it constructs any of them, in two passes.

**Structural.** For each registration: the family must expect a closed contract (or, for a family with no
media pairing, the registration must carry no item type); the recorded `ContractType` must be an interface
that is one constructed — not open — form of that family's contract; its single type argument must be
exactly the recorded `MediaItemType`; and the `ImplementationType` must implement that contract and no
sibling construction of it. A failure refuses the package with
`CoreErrorCode.PluginProviderContractInvalid`.

This deliberately repeats what the registration already derived. Host is the boundary that constructs, so
Host is where being wrong costs an extension constructor call; `ContractType.IsInstanceOfType(provider)`
remains after construction as defense in depth.

**Supply.** A registration carrying a `MediaItemType` must match the item type of a typed kind either
admitted by the same attempt or already active in the installation. This is a different claim from G03's
dependency check: that one proves a package with a given identifier is installed and compatible; it cannot
prove that some kind is closed over the exact CLR type a cataloger's contract names. A failure carries
`CoreErrorCode.PluginMediaPairingUnsatisfied` and names the item types the installation does supply.

Both passes run before any activation, so one unsound or unpairable provider means **none** of the
package's providers is constructed.

`CoreErrorCode` gained two members, and the reason there are two is that the two refusals are two different
operator problems:

| Code | Means | The operator's move |
|---|---|---|
| `PluginProviderContractInvalid = 2015` | the registration does not describe one relationship | report it to whoever wrote the extension |
| `PluginMediaPairingUnsatisfied = 2014` | the relationship is sound; nothing installed supplies that item type | install the media package that declares it |

Neither reuses `PluginContractMismatch = 2001`. That code means a contract *version* or assembly *identity*
mismatch, and it is what `SharedContractStore`, `SharedContractIdentityException` and the loader's declared
contract-range check already emit; folding a structurally incoherent provider registration into it would
collapse two diagnoses an operator has to tell apart. `AnIncoherentRegistrationAndAnUnsuppliedItemTypeAreDifferentDiagnoses`
asserts the split, including that 2001 is not what either refusal carries.

Files: `src/Arronix.Host/Runtime/PluginBootstrapper.cs`,
`src/Arronix.Abstractions/Health/CoreErrorCode.cs`.

### 1.3a One item type, one media kind

The item-type map was built with `Dictionary.TryAdd`, so two admitted kinds closed over the same item type
silently resolved first-win — and downstream registration stores only a `Type`, so every lookup keyed on it
(paired providers, external-identifier recognition) would have answered with whichever kind was seen first.

That is now an explicit invariant, enforced at **kind** admission rather than at provider admission, because
the ambiguity is in the kinds and exists whether or not anything pairs with them. `TryCheckItemTypeOwnership`
runs immediately after `TryPrepareMediaKinds`, before any activation at all, and refuses with
`CoreErrorCode.MediaItemTypeConflict = 3003` naming both kinds. Already-active kinds are walked first and in
registry order, so the incumbent is always named as the owner and the diagnostic does not depend on the
order the attempt's own kinds arrive in. A kind colliding with itself is not a collision: an extension
re-preparing a kind it already supplies is the duplicate-kind check's business, and reporting it here would
rename an existing failure.

With that invariant in place the provider-side map cannot be order-dependent, which is why it is safe for it
to remain a dictionary.

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

### New cases: 33, all passing

| File | Cases | What it holds |
|---|---|---|
| `src/Arronix.Architecture.Tests/Topology/ProviderPairingAuthorityTests.cs` | 19 | the detector's own control; per-implementation rules that no provider publishes an identifier or a family, and that every marked provider implements exactly one closed contract of that family (5 implementations × 2); the registration methods take one type argument and name the family; the two markers are independent and carry nothing; a registration derives item type, contract and family from the contract alone; one class serving both families keeps a pairing per family; **a marker implemented without a closed contract is refused at registration**; **an implementation closing two contracts of one family is refused and both are named**; and neither consumer edge mints or parses a provider identity outside serialization (2 cases) |
| `src/Arronix.Host.Tests/Runtime/ProviderMediaPairingAdmissionTests.cs` | 10 | a provider is admitted against a kind prepared in the same attempt; one whose item type no installed kind supplies is refused with `PluginMediaPairingUnsatisfied` and never constructed; one unresolvable pairing stops every provider in the package being constructed; the refusal names the item types the installation does supply; **a contract no implementation backs is refused with `PluginProviderContractInvalid` and the whole package's constructors stay at zero**; **a contract and item type that disagree are refused before construction**; **a non-media family carrying an item type is refused**; **two kinds in one attempt cannot share an item type**; **a kind cannot take an item type an active kind already owns**; **an incoherent registration and an unsupplied item type carry different codes, and neither is the contract-version code** |
| `src/Arronix.Host.Tests/Providers/CatalogMaterializationBoundaryTests.cs` | 3 | a typed cataloger result projects through the kind-blind runtime as the exact item with its media-owned lifecycle intact; no Host seam consumes a shaped cataloger or curator result; a durable item key is required of the provider and minted by nobody |
| `src/Arronix.Plugins.Tests/Registration/RegistryAdmissionTests.cs` | 1 | a curator registration retains the same item type through its own contract, with the family from the registration |

The adversarial Host cases write the ledger directly, because a registration built through the registry can
no longer say what they need it to say. That is the point: they put in front of Host exactly the
registration the registry refuses to build, and assert Host refuses it too, before construction.

Per-project totals after the change (base → now): Architecture `313 → 332` passed (1 registered skip
unchanged), Host `528 → 541`, Plugins `492 → 493`.

### Focused runs

```
Arronix.Plugins.Tests          493 passed,   0 skipped, 0 failed
Arronix.Host.Tests             541 passed,   0 skipped, 0 failed
Arronix.Architecture.Tests     332 passed,   1 skipped, 0 failed
Arronix.Abstractions.Tests     119 passed,   0 skipped, 0 failed
Arronix.Plugin.Tv.Tests         87 passed,   0 skipped, 0 failed
Arronix.Plugin.Music.Tests      66 passed,   0 skipped, 0 failed
```

### Full rail — `DOTNET_COMMAND=/usr/local/share/dotnet/dotnet ./eng/ci/run-tests.sh`

```
projects=11 total=2852 enabled=2550 passed=2549 failed=1 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0
requiredTests=3 compileLogs=1 compileProjects=11 compileItems=260 boundSources=15
error execution.failed: The run contains 1 failed cases.
```

Release build: 0 warnings, 0 errors. Skips unchanged at the ratcheted 302 (301 Movies, 1 architecture).
Ledger unchanged at 302 cases, 0 replacements; three required sentinels pass.

**The one failure was pre-existing and is not owned by this branch.**
`Arronix.Plugin.Movies.Tests.Shape.ManifestOwnershipTests.CarriesNothingButWhatItOwns` failed at base commit
`bbed12a69`; it was reproduced there by stashing this branch's changes and rerunning. The manifest declares
`contractAssemblies` and `dependencies` — landed by G03's dependency pipeline — while the test's
`ManifestOwnedProperties` allow-list and `INTERFACE.md`'s manifest paragraph both still said those do not
exist. Fixing it was a G03 record catching up with G03's own landed schema; making it here would have been
this branch asserting a G03 ownership answer. The independent G05 branch reached the same conclusion about
the same case.

**As integrated, the rail is green.** This work was merged onto the G03 close, which fixed that record. The
integrated run on the merge commit reports `projects=11 total=2871 enabled=2569 passed=2569 failed=0
skipped=302 inconclusive=0`, with the ledger unchanged at 302 cases and 0 replacements and the three
required sentinels passing. The counts in the block above are this branch's own run at its tip and are kept
as the branch record; the integrated numbers are current truth.

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
| a cataloger or curator author names `Movie` once | **met** — one generic position per contract, none in registration; the pairing is read from the contract implemented, not from a claim |
| admission rejects a provider whose closed item contract has no active matching media package | **met** — the whole contract relationship and the supply are both proved before any construction |
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
- Nothing else changes: both types already implement `ICataloger<Movie>` / `ICurator<Movie>`, which is where
  the pairing is now read from.
- If TMDb ends up serving a second media kind from one class, that now compiles and each registration keeps
  its own pairing.

**For whoever closes the identity decision.** The three blocked provider members and
`TmdbMovieMapper.ToMovie` are unchanged by whatever is chosen; only the identity assignment moves. On the
Host side, `CatalogMaterializationBoundaryTests.NoHostSeamConsumesAShapedCatalogerOrCuratorResult` is the
tripwire: it fails the moment a materialization seam is added, which is the moment the decision must already
have been made.

**For G06.** `IClosedCataloger` and `IClosedCurator` join `CompiledShapes`, the capture visitors and the
erased registrations as public-but-not-authoring surface. They are the newest members of the set G06 exists
to move or hide. They are now empty markers, so hiding them costs nothing semantically; what cannot be
hidden with them is the one-time type inspection that reads the pairing, which stays wherever the
registration is built.

**For G03 — closed.** `ManifestOwnershipTests.CarriesNothingButWhatItOwns` and `INTERFACE.md`'s sentence
that "explicit package dependencies are future G03 ownership and do not exist in the current schema" were
both stale against the landed manifest. G03 made that one change; the case passes in the integrated tree.
