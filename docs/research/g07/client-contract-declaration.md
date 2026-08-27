# G07.2 spike — the declared client contract entry point

**Status:** a bounded spike on `claude/g072-browser-proof`, not an integration. It establishes one mechanism
and measures the framework behaviours the rest of G07.2 has to be built against. It claims no browser
evidence, no host manifest publication and no client loader or page work.

Every claim below is asserted by a named test or by a command whose exact output is quoted.

## 1. The question

G07.1 leaves a browser holding a verified `Assembly` and no compile-time knowledge of anything inside it.
G07.2 has to get a typed entity out of it without the client enumerating types or properties — an
architecture rule already rejects `GetTypes`, `GetProperties`, `GetFields`, `GetMethods`, `GetMembers` and
`Activator.CreateInstance` anywhere in the client's source, because a client that discovers a media kind by
reading its properties has a second media schema and cannot be trimmed.

So the contract has to say what it holds. This branch answers *how a declaration can be both readable
before the runtime is handed the payload and executable after*.

## 2. What was built

**`Arronix.Abstractions.Client.ClientContractEntryPointAttribute`** — an abstract assembly attribute whose
every decidable fact is a **constructor argument**: the entity's `System.Type`, the generated-metadata hash
and the projection-schema hash. Behavior is separate and abstract: `Deserialize`, `Serialize`, `Project`,
`Schema`, and the contract's own `JsonSerializerContext` and root `JsonTypeInfo`.

**`Arronix.Abstractions.Client.ClientContractDigest`** — the canonical rendering, and hash, of a live
`JsonTypeInfo` graph and a live `FieldDescriptor` schema.

**`Arronix.Generators.ClientContractGenerator`** — emits, per public non-generic type deriving
`MediaItem<TItem,TReleaseTimeline,TReleaseStage>`: the sealed **internal** implementation of that attribute
with its literal hashes, and an **internal** static contract holding the projection and the serializer
calls.

**`Arronix.Generators.MediaShapeModel`** — the one CLR shape reading, now shared with `MediaShapeGenerator`.

**`MovieClientJsonContext`** — the movies contract's serialization metadata, on
`JsonSerializerDefaults.Strict` plus camel case.

## 3. Why the facts are constructor arguments

An overridden property is executable code. Publishing it means running the package's code; checking it
means having already loaded the payload that was supposed to be checked first. Constructor arguments live
in the custom attribute blob, so a **browser preflight** decodes them from the exact bytes with
`PEReader`/`MetadataReader` before the runtime is handed anything, and a **host** reads them from an
assembly it holds with `GetCustomAttributesData()`, constructing nothing.

The entity travels as a `System.Type` rather than as a type plus a name: the blob stores the type
reference, the reader decodes it and the runtime resolves it, so the two sides read one fact twice instead
of holding two spellings that can disagree.

`Deserialize` and `Project` are separate calls, and that is the claim rather than a convenience. One
bytes-to-fields call proves only that fields came out, and is satisfied equally well by a shortcut that
never constructs anything. Asking the returned value what it is — and getting the exact type the blob
referenced, from the assembly that declared the entry point — is what proves a typed value existed.

`ClientContractDeclarationTests`:

| Claim | Case |
| --- | --- |
| The three facts decode from the raw bytes, nothing loaded | `TheDeclarationIsDecodableFromTheBytesWithoutLoadingThem` |
| The generated type derives **directly** from the shared declaration | `TheGeneratedTypeDerivesDirectlyFromTheSharedDeclaration` |
| That reference resolves through an `AssemblyReference` naming the **exact** universal contract identity | `TheSharedDeclarationIsResolvedThroughTheExactUniversalContractIdentity` |
| The decoded type reference is the type the runtime resolved after loading | `TheDecodedTypeReferenceIsTheTypeTheRuntimeResolvedAfterLoading` |
| The implementation is internal, still found by exact base type, and the contract assembly's public surface is unchanged | `TheGeneratedDeclarationIsInternalAndStillFoundByItsExactBaseType` |
| A host reads the same facts without constructing the declaration | `AHostReadsTheSameFactsWithoutConstructingTheDeclaration` |
| Deserializing produces a value of the declared type from the declaring assembly | `DeserializingProducesAValueOfTheDeclaredTypeFromTheDeclaringAssembly` |

### 3.1 A namespace and a name are not an identity

Anyone can declare a type called `Arronix.Abstractions.Client.ClientContractEntryPointAttribute`. A payload
whose declaration derived from a private copy of it would satisfy a namespace-and-name comparison exactly
while binding to a type the host never admitted.

So the base `TypeReference` is followed to its **resolution scope**, that scope is required to be an
`AssemblyReference` rather than a type in the payload itself, and the complete identity it names is
required to equal this client's own universal contract identity. Measured:

```text
entryType         = Arronix.Media.Movies.MovieClientContractEntryPointAttribute
entryTypeIsPublic = False
base              = Arronix.Abstractions.Client.ClientContractEntryPointAttribute
scopeKind         = AssemblyReference
scopeIdentity     = Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null
```

This is the byte-level form of G07.1's post-load rule that the loaded assembly's contract reference must
resolve, by object identity, to the client's own `Arronix.Abstractions`; the test asserts both halves. Its
strength is bounded by the same thing that bounds G07.1: `PublicKeyToken=null`, because package signing
remains unbuilt. What proves *which bytes* is still the content hash G07.1 checks first.

## 4. The two hashes, and why they are checked against the runtime

A hash computed from the model that produced the value it is checking proves nothing. So the generator
emits literals from a compile-time model of the framework's serializer, and `ClientContractDigest`
recomputes the same canonical renderings from the **live** metadata. Measured, equal:

```text
serialization      = 6D346BE2E3B80815766937331C5C97FD733E5314019C82EF4CC9F9E55F7C3E15
declaredMetadata   = 6D346BE2E3B80815766937331C5C97FD733E5314019C82EF4CC9F9E55F7C3E15
projection         = 372E2CB8C8092F5F378EAD7F681339157A5E55F636B612F1A8CF94AF69250861
declaredProjection = 372E2CB8C8092F5F378EAD7F681339157A5E55F636B612F1A8CF94AF69250861
```

The serialization rendering carries the strict options, then every type reachable from the root: its kind,
its element type, and each member's wire name, declared type, direction, requiredness and nullability. The
projection rendering carries each descriptor's identifier, name, description, shape, semantics, prominence,
cardinality, editability, unit, choices and components. Both are recursive and deterministic.

Every type is resolved through the contract's own `JsonSerializerContext`, never through
`JsonSerializerOptions`. A `JsonTypeInfo` the context does not hold is a failure, not a gap to fill in:
options can fall back to a reflecting resolver, which would describe a graph the compiler never generated —
the exact thing generated metadata exists to avoid. Measured, this context has no fallback at all:
`context.GetTypeInfo(typeof(Guid))` returns `null` and `context.Options.GetTypeInfo(typeof(Guid))` throws
`NotSupportedException`. A root from another context is refused.

Getting the two sides to agree took four measurements the model would otherwise have got wrong:

1. **Members are ordered most-derived first**, then each base in turn. Host's compiled shapes read base
   first, so the two orders are deliberately different and the serialization model has its own walk. The
   movie graph cannot exercise this — no movie type both inherits and declares members — so it is pinned
   directly by `FrameworkWireBehaviorTests.MembersAreOrderedMostDerivedFirst`.
2. **A byte array is a value, not a sequence**: `kind=None`, no element type.
3. **Nullability comes from the declaration, not the substitution.** A member declared as a type parameter
   is nullable unless a constraint rules null out: `MediaItem.Lifecycle` (`TReleaseTimeline` with an
   interface constraint) is nullable to the serializer, and `Localized<T>.Value` (`where T : notnull`) is
   not, whatever the closed types are.
4. **An ignored member's placeholder is an implementation detail.** The framework leaves a
   `JsonPropertyInfo` with no getter and no setter, whose `PropertyType` is `System.Object` when the real
   type is reachable nowhere else and the real type otherwise. The digest records that the member is
   ignored and nothing more.

Four framework features are **refused** rather than described wrongly, with `ARX1011`: `[JsonPropertyName]`,
`[JsonConverter]`, `[JsonPolymorphic]`/`[JsonDerivedType]`, and dictionaries. Each changes what a payload
means in a way the model does not reproduce, and a hash that disagrees with the wire while looking like
agreement is worse than no contract.

## 5. Strict, and the one hole it leaves

The contract reads on `JsonSerializerDefaults.Strict`. `StrictPayloadTests` proves each refusal against a
real payload: an unknown property, a duplicate property, `null` into a non-nullable member, a member
spelled with different case, a missing required member, and a missing required constructor argument.

One measured hole. A member the contract computes carries `[JsonIgnore]`, which makes it **mapped** rather
than unknown — so `UnmappedMemberHandling.Disallow` let a forged `"status"` or `"normalizedValue"` through
silently. A sender that wrote one would believe it had been read. The generated reader now refuses those
names outright, before deserializing, from a list the generator holds; if a computed member's name were
also a live member's name anywhere in the graph the contract is refused instead, because the guard would
then reject valid payloads.

## 6. A source generator cannot feed the framework's serialization generator

Emitting the `[JsonSerializable]` context **from** `ClientContractGenerator` does not work on
`11.0.100-preview.7.26381.103`: the framework's generator never sees it and the partial is left
unimplemented.

```text
error CS0534: 'MovieClientJsonContext' does not implement inherited abstract member
'JsonSerializerContext.GetTypeInfo(Type)'
```

The same declaration written as ordinary source is consumed immediately and produces the complete graph. So
the context is one declared line in the contract assembly, and a media kind that omits it gets **`ARX1010`**,
whose message names the exact declaration to add. Recorded as debt: the two ways to remove it are an SDK
build target contributing the file as ordinary `Compile` input, or moving the client contract into a second
compilation.

## 7. The generated metadata acquires no shared-assembly cadence

`Arronix.Media.Movies` is admitted once per installation and released only when every dependant has
withdrawn, so what it holds statically is held by every dependant. Probed with the same rules
`PackageFacetTopologyTests` applies:

```text
total static fields inspected: 61
referenced: Arronix.Abstractions, System.Collections, System.Runtime, System.Text.Encodings.Web, System.Text.Json
```

No writable static field, no static delegate, no editable static collection, no module initializer — so
`SharedContractAssemblyHoldsNoMutableOrExecutableStaticState` and
`SharedContractAssemblyRunsNothingWhenItIsLoaded` pass **unchanged**. Two shared-framework references are
new; an integration must confirm both are present in the published browser client's `_framework`.

Two architecture rules changed, and only about analyzers:
`SharedContractProjectReferencesOnlyContractsAndOtherSharedContracts` now applies its subset rule to
*runtime* references and holds analyzer references to exactly the Arronix generator, and
`SharedContractProjectReferencesNoHostLoaderOrExecutableProject` denies the generator as a runtime
reference rather than outright. **`INTERFACE.md` says "a shared contract assembly takes no analyzer at
all."** That sentence is what this branch changes, and an integration must change it in the same commit.

## 8. Domain corrections carried with it

Cherry-picked from `d90305446`: five `[JsonIgnore]` attributes, so no derived value is written —
`MediaItem.Status`, `MovieReleaseTimeline.AvailableOn` and `Stage`, `Rating.NormalizedValue`,
`RatingScale.IsValid` — and `[JsonConstructor]` on `RatingScale`, whose parameterless struct form is the
interval zero-to-zero, which every rating value then falls outside of.

Two value types in the universal contracts have the same shape as `RatingScale` and are **not** in this
graph, so they are latent rather than fixed: `PlatformPath` (two public parameterized constructors, so
which one carries the invariant is a real decision) and `CategoryId`.

`FieldValue` gained a typed artwork slot. `OfArtwork(ArtworkImage)` carries the whole image — role, address
and any measurements — and populates that slot alone; `OfArtwork(Uri)` remains for producers that hold only
an address, and `FieldValue.Address` reads whichever slot is populated. An address alone throws away the
role that says which of several images this one is and the measurements that let one be chosen without
fetching it.

## 9. The complete movie round trip

`MovieClientContractTests` builds one movie carrying every shape the common item admits and drives it
through the declaration — `Serialize`, `Deserialize`, `Project` — never through the internal contract:
common values; artwork with role, address, width and height, in nested entities too; ratings with source,
value, scale, voice and sample size, and `NormalizedValue` recomputed from the restored scale; lifecycle
milestones and `Status`, which has no setter and is therefore computed rather than read; collections; and
external identifiers in both integer and string schemes.

The projection is one-way presentation data in declared schema order. Where it differs from Host's compiled
shapes it differs deliberately: Host projects a nested entity as a `Reference` to the durable identity it
assigned at materialization, and a browser projecting a payload holds no such identity, so a nested entity
is projected as its own values kept together.

## 10. What this branch does not claim

1. **No browser evidence.** The client loader, the projection page and the real-Chromium matrix are the
   integration branch's; this branch touched `Arronix.Client` only to keep its renderers correct against the
   typed artwork slot.
2. **No host publication.** `ClientContractManifest` is unchanged. Publishing the entity type and the two
   hashes per client-safe assembly — read with `GetCustomAttributesData()`, constructing nothing — is the
   natural next commit, along with what a host should do when a declaration cannot be read.
3. **No media-kind association.** The declaration carries the CLR entity type; associating it with a
   `MediaKindId` is the host's job from the admitted runtime registration.
4. **No fixture transport.** `Serialize` on the declaration is the honest producer for whatever the
   integration chooses; inventing a test-only production endpoint is still the thing not to do.
5. **`CONTEXT.md` and `INTERFACE.md` are unchanged** beyond the two lines the cherry-pick carried, because
   this branch is not the integrated line. The sentence an integration must change is named in section 7.

## 11. Evidence

`DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/ci/run-tests.sh`:

```text
projects=14 total=3485 enabled=3183 passed=3183 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0 requiredTests=3
compileLogs=1 compileProjects=14 compileItems=340 boundSources=15
```

The registered skip count is unchanged at 302 and both ratchets pass. The baseline before this work was
3,141 passed.

Extracting `MediaShapeModel` out of `MediaShapeGenerator` changed no generated byte, proved by building
`Arronix.Plugin.Movies` with `EmitCompilerGeneratedFiles` before and after and comparing
`Arronix_Plugin_Movies_Movies.MediaShape.g.cs`:

```text
IDENTICAL: the shared model refactor changed no generated byte
```
