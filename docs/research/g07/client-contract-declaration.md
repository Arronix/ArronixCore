# G07.2 spike — the declared client contract entry point

**Status:** a bounded spike on `claude/g072-browser-proof`, not an integration. It establishes one
mechanism and measures three things that decide how the rest of G07.2 must be built. It claims no browser
evidence, no host manifest publication, and no client loader or page work; those are the integration
branch's, and the shapes this branch fixes are what they should be built against.

Every claim below is asserted by a named test or by a command whose exact output is quoted.

## 1. The question

G07.1 leaves a browser holding a verified `Assembly` and no compile-time knowledge of anything inside it.
G07.2 has to get a typed entity out of it without the client enumerating types or properties — an
architecture rule already rejects `GetTypes`, `GetProperties`, `GetFields`, `GetMethods`, `GetMembers` and
`Activator.CreateInstance` anywhere in the client's source, because a client that discovers a media kind by
reading its properties has a second media schema and cannot be trimmed.

So the contract has to say what it holds. The question this branch answers is *how a declaration can be
both readable before the runtime is handed the payload and executable after*.

## 2. What was built

**`Arronix.Abstractions.Client.ClientContractEntryPointAttribute`** — an abstract assembly attribute whose
every fact is a **constructor argument**: the entity's `System.Type`, the generated-metadata hash, and the
projection-schema hash. Behavior is separate and abstract: `Deserialize`, `Serialize`, `Project`, `Schema`.

**`Arronix.Generators.ClientContractGenerator`** — emits, per public non-generic type deriving
`MediaItem<TItem,TReleaseTimeline,TReleaseStage>`: the sealed **internal** implementation of that
attribute, its application with `typeof(TEntity)` and the two hashes, and an **internal** static contract
holding the projection and the two serializer calls.

**`Arronix.Generators.MediaShapeModel`** — the one CLR shape reading. `MediaShapeGenerator` now reads
through it instead of its own copy.

**`[JsonConstructor]` on `RatingScale`** — see section 4.3.

## 3. Why the facts are constructor arguments

An overridden property is executable code. Publishing it means running the package's code; checking it
means having already loaded the payload that was supposed to be checked first. Constructor arguments live
in the custom attribute blob, so:

- a **browser preflight** decodes them from the exact bytes with `PEReader`/`MetadataReader`, before the
  runtime is handed anything — the same discipline G07.1 already uses for length, hash, identity and module
  version;
- a **host** reads them from an assembly it holds with `GetCustomAttributesData()`, constructing nothing.

The entity travels as a `System.Type` rather than as a type plus a name. The blob stores the type
reference; the reader decodes it and the runtime resolves it, so the two sides read one fact twice instead
of holding two spellings that can disagree. Any display name is taken from `EntityType` after load.

`Deserialize` and `Project` are separate calls, and that is the claim rather than a convenience. One
bytes-to-fields call proves only that fields came out, and is satisfied equally well by a shortcut that
never constructs anything. Asking the returned value what it is — and getting the exact type the blob
referenced, from the assembly that declared the entry point — is what proves a typed value existed.

`ClientContractDeclarationTests`:

| Claim | Case |
| --- | --- |
| The three facts decode from the raw bytes, nothing loaded | `TheDeclarationIsDecodableFromTheBytesWithoutLoadingThem` |
| The generated type derives **directly** from the shared declaration, by a `TypeReference` into the universal contracts | `TheGeneratedTypeDerivesDirectlyFromTheSharedDeclaration` |
| The decoded type reference is the type the runtime resolved after loading | `TheDecodedTypeReferenceIsTheTypeTheRuntimeResolvedAfterLoading` |
| The implementation is internal and still found by exact base type; the contract assembly's public surface is still `Movie`, `MovieReleaseStage`, `MovieReleaseTimeline` | `TheGeneratedDeclarationIsInternalAndStillFoundByItsExactBaseType` |
| A host reads the same facts without constructing the declaration | `AHostReadsTheSameFactsWithoutConstructingTheDeclaration` |
| Deserializing produces a value of the declared type from the declaring assembly, and projecting is a second call | `DeserializingProducesAValueOfTheDeclaredTypeFromTheDeclaringAssembly` |
| The schema is readable before any payload is | `TheDeclaredSchemaIsReadableBeforeAnyPayloadIs` |

The reader identifies the declaration in **one hop**: it walks the attribute's constructor to the type that
declares it and compares that type's own base to the universal contract's declaration. A deeper hierarchy
would mean resolving a type the reader has not been given, which is exactly what a preflight must not do —
so the generator emits a direct derivation and the test asserts it.

## 4. Three measurements

### 4.1 A source generator cannot feed the framework's serialization generator

Emitting `[JsonSerializable(typeof(Movie))] internal sealed partial class MovieClientJsonContext :
JsonSerializerContext` **from** `ClientContractGenerator` does not work on
`11.0.100-preview.7.26381.103`. The framework's generator never sees it and the partial is left
unimplemented:

```text
Arronix_Media_Movies_Movie.ClientContract.g.cs(14,31): error CS0534: 'MovieClientJsonContext' does not
implement inherited abstract member 'JsonSerializerContext.GetTypeInfo(Type)'
Arronix_Media_Movies_Movie.ClientContract.g.cs(14,31): error CS0534: 'MovieClientJsonContext' does not
implement inherited abstract member 'JsonSerializerContext.GeneratedSerializerOptions.get'
```

The same declaration written as **ordinary source** in the same project is consumed immediately, and
produces the complete graph — 30+ files including `Movie`, `MovieReleaseTimeline`, `MovieReleaseStage`,
`MediaCollectionMovie`, `Rating`, `ContentCertification`, `LocalizedItemInfo`, `ArtworkSet`,
`ExternalIdSet`, and every list and nullable in between.

So the context is one declared line in the contract assembly, and a media kind that omits it gets
**`ARX1010`**, whose message names the exact declaration to add. This is recorded as debt, not as a
design: the two ways to remove it are an SDK build target that contributes the file as ordinary `Compile`
input, or moving the client contract into a second compilation. Both are integration decisions.

### 4.2 The generated metadata acquires no shared-assembly cadence

`Arronix.Media.Movies` is admitted once per installation and released only when every dependant has
withdrawn, so what it holds statically is held by every dependant. Probed over the built assembly with the
same rules `PackageFacetTopologyTests` applies:

```text
total static fields inspected: 61
referenced: Arronix.Abstractions, System.Collections, System.Runtime, System.Text.Encodings.Web, System.Text.Json
```

No writable static field, no static delegate, no editable static collection, no module initializer — so
`SharedContractAssemblyHoldsNoMutableOrExecutableStaticState` and
`SharedContractAssemblyRunsNothingWhenItIsLoaded` pass **unchanged**. Two shared-framework references are
new; an integration must confirm both are present in the published browser client's `_framework`.

Two architecture rules did change, and only about analyzers:
`SharedContractProjectReferencesOnlyContractsAndOtherSharedContracts` now applies its subset rule to
*runtime* references and holds analyzer references to exactly the Arronix generator, and
`SharedContractProjectReferencesNoHostLoaderOrExecutableProject` denies the generator as a runtime
reference rather than outright. The thing the old rule protected against — a media definition moving into
the shared half and bringing its compiled reader delegates onto the shared cadence — is asserted directly
by the two structural rules above, and the probe shows the output acquires no cadence at all.

**`INTERFACE.md` says "a shared contract assembly takes no analyzer at all."** That sentence is what this
branch changes, and an integration must change it in the same commit.

### 4.3 A validating value type is silently defaulted by the serializer

`RatingScale` is a `readonly record struct` with get-only `Minimum`/`Maximum` and one validating
constructor. A struct always has a parameterless form, and the generated deserializer reached for it,
producing the interval zero-to-zero; `Rating`'s constructor then refused a value that was always valid:

```text
System.ArgumentOutOfRangeException: Rating 'tmdb' must be between 0 and 0, inclusive.  ActualValue: 8.6
  at Arronix.Abstractions.Media.Rating..ctor(String, Decimal, RatingScale, RatingVoice, Nullable`1)
  at Arronix.Media.Movies.MovieClientJsonContext.<>c.<Create_Rating>b__88_0(Object[] args)
  ...
  at Arronix.Media.Movies.MovieClientContract.Read(ReadOnlySpan`1 utf8Json)
```

The failure is a throw from domain code during deserialization, in a stack that names neither the scale nor
the value. Fixed by naming the constructor that establishes the invariant — `[JsonConstructor]` on
`RatingScale(decimal minimum, decimal maximum)` — and proved by the round trip rather than by the presence
of metadata.

Two further value types in the universal contracts have the same shape and are **not** in this graph, so
they are latent rather than fixed: `Arronix.Abstractions.FileSystem.PlatformPath` (two public parameterized
constructors, so which one carries the invariant is a real decision) and
`Arronix.Abstractions.Shape.CategoryId`. Whoever puts either into a client contract graph must settle them
first.

## 5. The complete movie round trip

`MovieClientContractTests` builds one movie carrying every shape the common item admits and drives it
through the declaration — `Serialize`, `Deserialize`, `Project` — never through the internal contract:

- common values: title and language, original title and language, alternate titles, localized
  translations, years, overview, runtime, organization, certification with its authority and minimum age,
  genres, keywords, website, preview, popularity, catalog state;
- **artwork** with role, address, width and height, surviving as a multivalued artwork field;
- **ratings** with source, value, scale, voice and sample size, and `NormalizedValue` recomputed from the
  restored scale;
- **lifecycle and status**: the milestones survive, and `Status` — which has no setter and is therefore not
  read from the payload — is `Released`, computed by `MovieReleaseTimeline.StageOn` over the restored
  lifecycle;
- **collections** with their own artwork, external identifiers and member count;
- **external identifiers** in both integer and string schemes.

The projection is one-way presentation data: `FieldDescriptor` plus `FieldValue`, in declared schema order,
which the existing client renderer already draws. Where it differs from Host's compiled shapes it differs
deliberately: Host projects a nested entity as a `Reference` to the durable identity it assigned at
materialization, and a browser projecting a payload holds no such identity, so a nested entity is projected
as its own values kept together.

## 6. The module-initializer registry is not available here

A generated module initializer registering a `WeakReference` in a default-context registry is a real
mechanism and does not root a collectible load context. It is not available to a **client-safe contract
assembly**, and the refusal is not a style rule:

- `SharedContractStore` refuses a shared contract carrying one **at admission, before it loads**
  (`src/Arronix.Plugins/Loading/SharedContractStore.cs:1101`, `PluginIsolationViolation`);
- `PackageFacetTopologyTests.SharedContractAssemblyRunsNothingWhenItIsLoaded` asserts the same over the
  built assembly, and the source screen rejects the spelling;
- the threat model bans it as **T-13**: code that runs before and despite quarantine.

Reaching an internal implementation by exact base type gets the same result with nothing running on load,
which is why this branch took it.

## 7. What this branch does not claim

1. **No browser evidence.** Nothing here has been in a browser. The client loader, the projection page and
   the real-Chromium matrix are the integration branch's, and this branch deliberately did not touch
   `Arronix.Client`.
2. **No host publication.** `ClientContractManifest` is unchanged. Publishing `EntityType`,
   `GeneratedMetadataHash` and `ProjectionSchemaHash` per client-safe assembly — read with
   `GetCustomAttributesData()`, constructing nothing — is the natural next commit, along with what a host
   should do when a declaration cannot be read.
3. **No media-kind association.** The declaration carries the CLR entity type. Associating it with a
   `MediaKindId` is the host's job from the admitted runtime registration; `Arronix.Plugins`, where the
   client contract catalog lives, does not know media kinds by design.
4. **No fixture transport.** G07.2's exit gate wants a typed movie rendered "over serialized network
   payloads", and no production endpoint yields a typed `Movie` before G07B. `Serialize` on the declaration
   is the honest producer for whatever the integration chooses; inventing a test-only production endpoint
   is still the thing not to do.
5. **`CONTEXT.md` and `INTERFACE.md` are unchanged**, because this branch is not the integrated line. The
   sentences an integration must change are named in section 4.2.

## 8. Evidence

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/ci/run-tests.sh`:

```text
projects=14 total=3459 enabled=3157 passed=3157 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0 requiredTests=3
compileLogs=1 compileProjects=14 compileItems=336 boundSources=15
```

The registered skip count is unchanged at 302 and both ratchets pass. The 16 new cases are the client
contract fixtures; the baseline was 3,141 passed.

Extracting `MediaShapeModel` out of `MediaShapeGenerator` changed no generated byte, proved by building
`Arronix.Plugin.Movies` with `EmitCompilerGeneratedFiles` before and after and comparing
`Arronix_Plugin_Movies_Movies.MediaShape.g.cs`:

```text
IDENTICAL: the shared model refactor changed no generated byte
```
