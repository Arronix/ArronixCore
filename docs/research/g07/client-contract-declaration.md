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
serialization      = CCDA9D828C1E686F71E13074DD1EAEA60FB63306C8A8752531A4A1A604415C7B
declaredMetadata   = CCDA9D828C1E686F71E13074DD1EAEA60FB63306C8A8752531A4A1A604415C7B
projection         = 46E52C947A3337B2A770C4FCFB513482318540967F8C4189A86D9C46E1944FDB
declaredProjection = 46E52C947A3337B2A770C4FCFB513482318540967F8C4189A86D9C46E1944FDB
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
4. **An enumeration reaches the wire as a number**, so its underlying type is part of the shape: widening
   one changes what a payload carries while nothing about the member that carries it moves.
5. **An ignored member's placeholder is an implementation detail.** The framework leaves a
   `JsonPropertyInfo` with no getter and no setter, whose `PropertyType` is `System.Object` when the real
   type is reachable nowhere else and the real type otherwise. The digest records that the member is
   ignored and nothing more.

### 4.1 Every value states its own length

Author-supplied text — a field's identifier, name, description, unit, a choice's stored value and label,
and every type and member name — is **length-prefixed** on both sides: `5:title`, and `~` for absent, which
is a different fact from empty. Concatenated raw with a separator, an author who used that separator moves
the boundary between two values, so two different schemas render identically and hash alike: the hash would
say two contracts agree when they do not, which is the one thing it exists to rule out.
`ClientContractDigestEncodingTests` proves it for a separator inside an identifier, a line break
impersonating another field, choice text, and absent-versus-empty.

### 4.2 Framework types are matched by identity, not by name

`Compilation.GetTypeByMetadataName` searches the compilation's own assembly first, so a package declaring
`System.Text.Json.Serialization.JsonIgnoreAttribute` would be handed back instead of the framework's — and
this generator would then read that package's own attribute as the instruction keeping a member off the
wire, while the real serializer wrote it anyway. Every framework type is therefore enumerated with
`GetTypesByMetadataName`, the compilation's own assembly excluded, and the assembly declaring
`JsonSerializerContext` required to declare all the rest. Regressions put an impostor in the framework's
exact namespace, in its own syntax tree; reinstating the metadata-name lookup fails both.

The root `JsonTypeInfo` is asked for with `GetTypeInfo(typeof(T))` rather than reached through the property
the framework's generator happens to name after the type. That name is a convention.

### 4.3 What is refused rather than described

`ARX1011`, and the list is an **allow list** rather than a deny list. A framework attribute in
`System.Text.Json.Serialization` the model has never heard of changes what a payload means in some way, and
the safe reading of "never heard of" is "not described" — only `[JsonIgnore]` on a member, and
`[JsonSerializable]`/`[JsonSourceGenerationOptions]` on a type, are modeled. Also refused: dictionaries; any
type implementing `IEnumerable` that is not a recognized sequence; an untyped value; an interface or
abstract type; a generic nested inside another type, whose arguments a compiler and a runtime spell
differently; and any array that is not single-dimensional and zero-based.

The rules run for **every type the graph reaches**, at the point it is described, not only where a member
declares one. A root-level attribute and the element of a recognized sequence are both reached without any
member naming them, so `List<HashSet<string>>` and an attribute on the entity itself are caught.

Two ways a type can put something on the wire without appearing in the digest are refused outright, both
measured on the pinned SDK: a public **field** carrying `[JsonInclude]` is serialized even with
`IncludeFields` off, and an `internal` property carrying it is serialized too. A `[JsonConstructor]` on a
non-public constructor **is** honoured by the framework, so the model reads it rather than looking only at
the public ones; more than one named constructor is refused.

`[JsonSerializable]`'s own arguments are read as well. `GenerationMode` selects which halves the framework
generates, and it is a flags value: zero inherits the options-level default, anything carrying the metadata
flag gives a reader what it needs, and serialization-only does not. It is read as flags rather than as a
member name, because a combined value has no named field and a name comparison lets it through unexamined.
`TypeInfoPropertyName` renames the generated property, which this contract never reads — the root is asked
for by type — so it is admitted deliberately, with a case proving a renamed property leaves both hashes
unchanged. Any other target argument is refused by name.

The declared options are held to one exact set — strict defaults plus the camel-case naming policy — and
any other declared option is refused by name. Reading two options and ignoring the rest was the failure
that mattered: a context that also set a number-handling mode would still have been published, under a hash
describing a wire it did not have.

Every refusal has a case in `ClientContractGeneratorTests`, each driven through a real compilation which is
required to produce **no** compiler errors — the framework's half of each serialization context is written
by hand for that reason. A case that reasons over source which did not compile proves nothing, and one did:
the first impostor case appended a namespace after a file-scoped one, so the impostor never existed and the
case passed without testing anything. The supported shape is also shown actually producing a contract, so
an empty refusal list cannot pass for agreement.

### 4.4 The live serializer surface, measured then closed

A hash over a partly described graph says two contracts agree when what differs is the part nobody looked
at, so every setting that changes reading or typed writing is either rendered on both sides or refused.
The baseline was measured against the real generated context first, and two of its facts would have made a
blind refusal wrong:

```text
types walked=34  converter assemblies: framework
ShouldSerialize Movie.status, MovieReleaseTimeline.availableOn, MovieReleaseTimeline.stage,
                Rating.normalizedValue, RatingScale.isValid
CreateObject factory: ArtworkSet, ExternalId, ExternalIdSet   (and no other of the 34)
```

Every type carries a converter and resolves through its own context, and every ignored member carries a
`ShouldSerialize`. So what is refused is a converter declared **outside the framework assembly**, metadata
whose `OriginatingResolver` is not this contract's own context — the check that separates a generated graph
from a hand-built one — and a `ShouldSerialize` on a member that is actually read or written.

`CreateObject` turned out to be present for an object with a parameterless constructor **and no required
member**, and absent otherwise, which is not what "has a parameterless constructor" predicts: `Movie`,
`MediaCollection<Movie>` and `MovieReleaseTimeline` all have one and carry no factory. It is rendered on
both sides rather than refused, and the compile-time model reproduces all 34 answers.

Rendered in the options line, and previously absent from it: `maxDepth`, `preferredObjectCreation`,
`unknownType`, `outOfOrderMetadata`, `ignoreReadOnlyProperties`, `ignoreReadOnlyFields`, `namingPolicy`.
Refused outright, because nothing in either rendering describes what they do: a `ReferenceHandler` (it puts
`$id` and `$ref` in the payload), options-level `Converters`, a `DictionaryKeyPolicy`, a naming policy other
than camel case, and a `TypeInfoResolver` or resolver chain that is not exactly this contract's context.

**Consciously formatting-only**, and excluded by decision rather than oversight: `WriteIndented`,
`IndentCharacter`, `IndentSize`, `NewLine`, `DefaultBufferSize` and `Encoder`. They change the bytes a
payload is written as; every conforming reader recovers the same values.
`FormattingSettingsDoNotChangeTheDigest` asserts the boundary directly.

`ClientContractDigestRefusalTests` carries a witness for each: 25 cases over a stand-in context, including
a non-default `ReferenceHandler`, a type-level `[JsonConverter]` built through the reflecting resolver
(created by hand the attribute is not read, and the case would prove nothing), a member `CustomConverter`,
and a `ShouldSerialize` on a live member beside one on an ignored member that is admitted. Removing the
options refusals and the added option fields fails seven of them and nothing else — they are the cases that
kept the same digest before this work.

### 4.5 The rest of the public surface, and what is deliberately not proved

The earlier pass worked from a checklist. Enumerating the actual .NET 11 Preview 7 surface turned up
members no checklist named — `JsonTypeInfo.TypeClassifier`, `UnionCases`, `UnionConstructor`,
`UnionDeconstructor`, `IsReadOnly`; `JsonSerializerOptions.InferClosedTypePolymorphism`, `TypeClassifiers`,
`IsReadOnly`, and the obsolete `IgnoreNullValues` — alongside the four `On*` callbacks,
`ConstructorAttributeProvider`, `AttributeProvider`, `JsonPropertyInfo.AssociatedParameter` and
`JsonTypeInfo<T>.SerializeHandler`.

Baselined over all 34 movie wire types first:

```text
types=34 optionsSameInstance=34 props=63 propOptionsSame=63
callbacks, TypeClassifier, UnionCases/Constructor/Deconstructor: none; every type IsReadOnly
IgnoreNullValues=False (and setting it leaves DefaultIgnoreCondition at Never — independent)
SerializeHandler: present under Default and Metadata|Serialization, absent under Metadata
```

**Refused**, none of which the baseline carries: any of the four callbacks; a `TypeClassifier`; a union
constructor, deconstructor or case; a `JsonTypeInfo` or `JsonSerializerOptions` still open to change; a
`JsonTypeInfo` or `JsonPropertyInfo` built for other options; `IgnoreNullValues`;
`InferClosedTypePolymorphism`; `TypeClassifiers`. The generator refuses the four `IJsonOn…` contracts
before load, resolved through `FrameworkSymbols` and compared by symbol identity, so a callback is a
compile error rather than a run-time surprise.

**Represented**, because it decides what a payload means: `JsonPropertyInfo.AssociatedParameter` and the
`JsonParameterInfo` behind it — position, name, parameter type, member-initializer flag, nullability and
default value. A default decides what a member becomes when a payload omits it, and two contracts differing
only in one hashed alike before. A parameter's nullability is read from the **parameter**, not from its
member: `AParametersNullabilityIsItsOwn` pins a member that is not nullable filled by one that is, and
reading the member's answer instead fails `AParameterNullabilityDifferentFromItsMembersChangesTheHash`.
An enumerated default is refused on both sides, because a compiler hands back the underlying number and the
runtime hands back the enumeration value.

**Consciously classified, with reasons:**

- `ConstructorAttributeProvider` and `JsonPropertyInfo.AttributeProvider` are attribute lookup surfaces for
  other resolvers. They carry no reading or writing behavior, and the constructor they describe is already
  rendered through `AssociatedParameter` and requiredness.
- Whitespace, line endings, buffer size and the encoder change the bytes, not the values recovered from
  them.

**The write fast path is refused, not classified.** `JsonTypeInfo<T>.SerializeHandler` is a generated
delegate, and one witness type writing the same bytes is not a proof about an arbitrary one. So the contract
requires `GenerationMode = JsonSourceGenerationMode.Metadata` and nothing else: measured, that removes the
handler from **every reachable type**, not only the one named.

```text
Metadata only:  -       Branch(Object)   -       IReadOnlyList(Enumerable)   -       Leaf(Object)
Default:        handler Branch(Object)   handler IReadOnlyList(Enumerable)   handler Leaf(Object)
```

`Default` and `Metadata | Serialization` are now refused along with `Serialization`, and omitting the mode
is refused too, because the options-level default also generates one. Removing the declaration from the
movies context fails the build with `ARX1011` naming it, and
`NoReachableTypeCarriesAWriteFastPath` asserts the live graph over all 34 types.

### 4.6 What the metadata cannot be asked, and is measured instead

Two holes survived the surface audit, and both are about believing what metadata says rather than checking
it.

**A context has to answer for the type it was asked about.** `GetTypeInfo` was taken at its word: a context
returning another type's metadata, or a fresh object on each call, was rendered as if it were the requested
type's. Both are now refused — the answer must be for that exact `Type` and must be the same object twice.
`OriginatingResolver` is kept as a consistency check and nothing more: the property has a setter until the
metadata is sealed, so it cannot prove where metadata came from. What is observable is the stable answer.

**Converter identity says nothing about converter configuration.** The earlier admission rule compared the
converter's assembly to the framework's, which reads as a guarantee and is not one. Measured:

```text
numeric: Converters=0 [] converter=EnumConverter`1 payload={"stage":1}
stringy: Converters=0 [] converter=EnumConverter`1 payload={"stage":"Done"}
```

`UseStringEnumConverter` registers nothing on the options, keeps the same converter type, and changes what
a payload carries. No inspection of `JsonSerializerOptions` or `JsonTypeInfo` separates the two — not the
assembly, not the type name, not a comparison against a pristine baseline's converter, which returns
`EnumConverter\`1` for both.

So an enumeration is rendered by **what its own metadata writes**: a declared constant, chosen by ordinal
name so the choice is stable, plus zero. A names mode renders the declared constant as a string and a
numeric mode as a number. Zero alone would not do it — a names mode writes an undefined zero as a number,
so an enumeration whose first member is one would still collide; the witness enumeration has no zero member
for exactly that reason. This detects the framework's supported converter modes. What an arbitrary delegate
would do is not detected here and is not claimed to be.

The compile-time model predicts the numeric form, because the two declarations that would write names —
`UseStringEnumConverter` and a `[JsonConverter]` on the type — are both refused before load.

**The structural limit, stated rather than implied.** For `CreateObject` the digest renders *presence* and
never behavior: nothing here proves what that factory does. What pins the code behind them is one layer up: G07.1 content-hashes the assembly's exact
bytes and proves its CLR identity and module before it loads, and this rendering additionally requires that
every type comes back from the contract's own context as the same object, for that exact type, on those
exact options. A different delegate means different bytes, and different bytes mean a different content
address.

### 4.6 One guard was inert when it was written

The check that a computed member's name does not collide
with a live member's name subtracted the live names from the computed set *before* looking for the
intersection, which empties the set the collision would have been found in, so it never fired — and the
colliding names were dropped from the guard list as well, leaving them forgeable. Restoring the subtraction
fails `AComputedMemberSharingALiveMembersNameIsRefused` and nothing else.

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
fetching it. `Address` is itself derived, so it carries `[JsonIgnore]` under the same rule as every other
computed value on this contract; the image's own address is a fact of the image and is still written.

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
projects=14 total=3572 enabled=3270 passed=3270 failed=0 skipped=302 inconclusive=0
cases=302 replacements=0 passingWitnesses=0 closureEligibleWitnesses=0 requiredTests=3
compileLogs=1 compileProjects=14 compileItems=344 boundSources=15
```

The registered skip count is unchanged at 302 and both ratchets pass. The baseline before this work was
3,141 passed.

Extracting `MediaShapeModel` out of `MediaShapeGenerator` changed no generated byte, proved by building
`Arronix.Plugin.Movies` with `EmitCompilerGeneratedFiles` before and after and comparing
`Arronix_Plugin_Movies_Movies.MediaShape.g.cs`:

```text
IDENTICAL: the shared model refactor changed no generated byte
```
