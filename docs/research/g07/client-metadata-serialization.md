# G07.2 spike — can a browser read a typed movie out of generated metadata?

**Status:** spike. This is evidence, not a design and not a gate closure. It answers the two questions
G07.2's serialization half rests on and records one domain-contract defect it found. The declared client
entry point, its discovery, and the projection schema are deliberately **not** built here; the integration
worker owns them.

Every claim below is either a quoted command output, a named test, or a mutation whose result is stated.

## 1. What was asked, and what is answered

G07.2 needs the browser to deserialize a complete `Movie` graph through **trimming and ahead-of-time-safe**
metadata — `JsonTypeInfo` produced while the assembly compiled, not a reflecting serializer discovering the
shape at run time. Two things had to be established before any of that can be designed:

1. whether a platform source generator can drive `System.Text.Json`'s source generator, so that an author
   states nothing about serialization;
2. whether the framework's generator actually covers this graph on the pinned SDK, and whether a movie
   survives the round trip.

The answer to (1) is no, for a reason that is a property of the compiler. The answer to (2) is yes, after
one fix to a universal contract type.

Everything here ran under `/usr/local/share/dotnet/dotnet`, SDK `11.0.100-preview.7.26381.103`, which is the
SDK `global.json` pins.

## 2. Cross-generator visibility is phase-sensitive, and the visible phase cannot derive anything

**Measured. The result is phase-sensitive, which is why a reasonable experiment can conclude the
opposite — so the false positive that produces it is recorded here too.**

Roslyn runs every generator against one compilation snapshot. Which phase a generator emits in decides
whether another generator sees the emission:

- **Post-initialization** output (`RegisterPostInitializationOutput`) is added *before* the snapshot, so
  other generators — the framework's included — do see it. It takes no inputs at all: no syntax tree, no
  symbol, no analyzer option. It can only carry a name written into the generator itself.
- **Ordinary source** output (`RegisterSourceOutput`) is added *after* the snapshot, so no other generator
  sees it. This is the only phase that can read the compilation, and therefore the only phase in which a
  type could be discovered.

So visibility and derivation are in different phases, and no emission has both.

Three emissions of the same text, in one probe:

| Emission channel | Item type known from | Result |
| --- | --- | --- |
| `RegisterPostInitializationOutput` | a literal in the generator | **compiles**; framework metadata generated; round-trips |
| `RegisterSourceOutput` | a literal in the generator | `error CS0534` — the framework's generator never saw it |
| `RegisterSourceOutput` via `ForAttributeWithMetadataName` | discovered from the compilation | `error CS0534` |

```text
error CS0534: 'DerivedJsonContext' does not implement inherited abstract member
              'JsonSerializerContext.GetTypeInfo(Type)'
error CS0534: 'DerivedJsonContext' does not implement inherited abstract member
              'JsonSerializerContext.GeneratedSerializerOptions.get'
```

The tempting workaround fails the same way: a hand-written `partial class X : JsonSerializerContext` with
`[JsonSerializable]` applied by a generated *second partial declaration* also produces `CS0534`, because the
generated declaration is in a tree the framework's generator cannot see.

The one row that compiles is the one whose item type was a literal in the generator: it proves the phase is
visible, not that anything was derived. Pointing it at `Movie` would mean hard-coding
`Arronix.Media.Movies.Movie` into the platform's generator, which is a magic name for one media kind in a
kind-blind generator — the thing the platform exists not to do.

**So the item type has to be named where both generators can see it: in ordinary source.** That is why the
declaration below is hand-written, and why the cost of it — one line an author writes — is a real finding
rather than an implementation shortcut.

### 2.1 The false positive, exactly

This is worth stating because it reads as a success and is reproducible:

```text
$ rm -rf obj bin generated
$ dotnet build                # clean
  error CS0534 (×2)
$ dotnet build                # no source change
  Build succeeded.
$ dotnet run
  json={"Name":"x","Year":1}
```

The project set `EmitCompilerGeneratedFiles=true` with a `CompilerGeneratedFilesOutputPath` **inside the
project directory**, so the first build's generated files land where the SDK's default `**/*.cs` glob picks
them up. The second build compiles the previous build's output as ordinary source, the framework's generator
sees it, and everything works — including a real round trip. Any experiment that builds twice, or that runs
in a tree a previous experiment wrote into, will report that cross-generator visibility works.

The check that distinguishes them is one clean build.

## 3. The framework generates the complete `Movie` graph

`MovieContractSerialization` — a `[JsonSerializable(typeof(Movie))]` context — produces metadata for
34 types, covering every type a movie reaches through the wire:

```text
Movie, MovieReleaseTimeline, MediaCollection<Movie>,
ExternalIdSet, ExternalId, ArtworkSet, ArtworkImage, IReadOnlyList<ArtworkImage>,
Rating, RatingScale, RatingVoice, ContentCertification, Localized<ItemInfo>, ItemInfo,
Language, CatalogRecordState, Uri, DateOnly?, TimeSpan?, Int64?, Double?, Int32?, …
```

`MovieContractSerializationTests.TheGeneratorProducesMetadataForTheWholeMovieGraph` asserts the fifteen that
carry domain meaning. Deserialization is asserted through
`JsonSerializer.Deserialize(string, JsonTypeInfo)` — the overload that carries no trimming annotation. A case
using the reflecting overload would pass and prove nothing about the client this metadata exists for.

`EveryMovieValueSurvivesTheRoundTrip` covers artwork with dimensions, external identifiers, certification,
translations, collections with their own identifiers and artwork, genres, keywords, links, runtime,
popularity and both languages.

The declaration lives in `Arronix.Plugin.Movies.Tests`, not in the shipped domain assembly. Where it has to
live for a browser to reach it — and what an author should have to write, which on the evidence in §2 is not
nothing — is the integration worker's decision, and putting a provisional answer in the shipped surface
would prejudge it.

## 4. The defect this found: `RatingScale` could not be read back

**Fixed, in `Arronix.Abstractions/Media/Rating.cs`.** Serialization always succeeded; deserialization threw.

`RatingScale` is a `readonly record struct` with get-only `Minimum`/`Maximum`, an explicit validating
constructor, and — like every struct — an implicit parameterless one. The framework prefers the parameterless
constructor unless told otherwise, so it generated:

```csharp
ObjectCreator = () => new global::Arronix.Abstractions.Media.RatingScale(),
ObjectWithParameterizedConstructorCreator = null,
```

which rebuilds every scale as zero-to-zero. `Rating`'s constructor then rejects its own value:

```text
System.ArgumentOutOfRangeException : Rating 'tmdb' must be between 0 and 0, inclusive. (Parameter 'value')
Actual value was 8.6.
   at Arronix.Abstractions.Media.Rating..ctor(String, Decimal, RatingScale, RatingVoice, Nullable`1)
   at …MovieContractSerialization.<>c.<Create_Rating>b__88_0(Object[] args)
   at …LargeObjectWithParameterizedConstructorConverter`1.CreateObject(ReadStackFrame&)
```

The failure names neither the scale nor the payload, and it only appears on a graph that carries a rating —
which is why it survived to be found by a complete fixture rather than by a shape test.

`[JsonConstructor]` on `RatingScale(decimal minimum, decimal maximum)` fixes it:

```csharp
ObjectCreator = null,
ObjectWithParameterizedConstructorCreator = static args =>
    new global::Arronix.Abstractions.Media.RatingScale((decimal)args[0], (decimal)args[1]),
```

This is a statement about the type, not about a serializer: the scale is an invariant with no settable
member, so the constructor that establishes it is the only way back into one. Recorded in `INTERFACE.md`.

**Mutation evidence.** Removing the attribute — and the now-unused `using`, so that the mutation actually
compiles — fails 4 of the 8 cases with the stack above (`EveryMovieValueSurvivesTheRoundTrip`,
`ARatingAndItsScaleSurviveTheRoundTrip`, `ALifecycleSurvivesTheRoundTripAndItsStageIsRecomputed` and
`NoDerivedValueIsWrittenAndAForgedOneCannotChangeWhatIsRead`). Restoring it returns 8 of 8.

Two runs during this work were invalid and were redone, both for reasons the repository already warns about:
a mutation that failed to build left `--no-build` running the previous binary, and a restore by `mv` carried
the backup's older timestamp, so MSBuild judged the project up to date and the "restored" run was still the
mutant. A mutation run is only evidence if the build that produced it is quoted.

**Other invariant-bearing values in the graph were checked and are sound.** `ContentCertification`, `Rating`,
`Localized<T>` and `ItemInfo` each expose exactly one public constructor, so it is selected without an
attribute; `ExternalId`, `ArtworkImage` and `Language` are positional records whose members have `init`
setters; `Movie`, `MovieReleaseTimeline`, `MediaCollection<T>`, `ArtworkSet` and `ExternalIdSet` deserialize
through a parameterless constructor into `init` and `required` members. `RatingScale` was the only type that
combined a validating constructor with no settable member.

## 5. Derived values are not written, and a forged one changes nothing

Default generated metadata writes every get-only computed property beside the values it is computed from:
`Movie.Status`, `MovieReleaseTimeline.AvailableOn`, `MovieReleaseTimeline.Stage`, `RatingScale.IsValid`,
`Rating.NormalizedValue`. Each is a redundant wire fact that an untrusted payload can make disagree with its
own inputs. That the framework happens to ignore them on the way back is a property of its current default,
not a rule this platform has stated.

**The rule is now stated.** All five carry `[JsonIgnore]`. A derived value is neither written nor read; a
consumer reads it off the item it deserialized, which recomputes it.

The distinction that makes the rule safe to apply is not "has no setter". `Rating.Source`, `.Value`,
`.Scale`, `.Voice` and `.SampleSize` have no setter and are authoritative, because the constructor writes
them; `Rating.NormalizedValue` has no setter and is computed. A rule keyed on the setter alone gets one of
them wrong, and getting it wrong in the excluding direction drops authoritative facts off the wire. The
decidable form is **no setter and no matching constructor parameter**, and both directions are asserted:
`NoDerivedValueIsWrittenAndAForgedOneCannotChangeWhatIsRead` and `AnAuthoritativeGetOnlyValueIsStillWritten`.

The forged-payload half matters independently of the absence half. A sender that writes `Status`, `Stage`,
`AvailableOn`, `NormalizedValue` and `IsValid` anyway gets none of them read: unmapped members are skipped,
and every one of the five is recomputed from what was actually sent.

**A consequence worth stating.** `MovieReleaseStage` reached the wire only through `Movie.Status` and
`MovieReleaseTimeline.Stage`, and `bool` only through `RatingScale.IsValid`. With those excluded neither is
a wire type: the generated set drops from 36 to 34. Nothing is lost — a stage is a CLR value a consumer
recomputes from the milestones it did receive — but the wire surface is genuinely smaller, and a later
change that put either type back would be putting a derived value back with it.
`ATypeReachedOnlyThroughADerivedValueIsNotAWireType` holds that.

**Mutation evidence.** Removing all five attributes — and the two now-unused `using` directives, so the
mutation compiles — fails 2 of the 8 cases. Restoring them returns 8 of 8.

## 6. Measured, recorded, and not built here

Two facts were established by probe while scoping this spike, before it was narrowed. They are handed over
as measurements for the integration worker to weigh, and are explicitly **not** proposals — nothing here
argues for a mechanism, and no part of either was kept on this branch:

- **The framework's generated metadata contains reflection, of a bounded kind.** Each property carries
  `AttributeProviderFactory = static () => typeof(T).GetProperty("Name", …, typeof(string), …)` and each
  parameterized type carries an equivalent `GetConstructor`. These are exact single-member lookups by name
  and signature, not member enumeration, and they are lazy factories invoked only if something reads
  `JsonPropertyInfo.AttributeProvider`. The Client's architecture rule forbids `GetProperties`, `GetFields`,
  `GetMethods`, `GetMembers`, `GetTypes` and `Activator.CreateInstance` **in the Client's own source**, and
  none of those appears here. But this code ships inside the assembly a browser downloads, so whether it is
  acceptable is a decision to take deliberately rather than one this spike should make by landing it.

- **Reading an assembly-level attribute typed as a universal base class does not root a collectible context.**
  A probe loaded a contract assembly into a collectible `AssemblyLoadContext`, resolved a generated attribute
  through `GetCustomAttribute<TBase>()`, deserialized through the `JsonTypeInfo` it handed back, dropped the
  references and unloaded: `usedEntryPoint=True`, `collectibleContextUnloaded=True`. Two constant strings
  passed to that attribute's constructor were also readable from the raw bytes with `MetadataReader`, before
  any load.

  That is a measurement, not an endorsement. Resolving an attribute *constructs* it, which runs the package's
  code — so "pull" describes when the code runs, not whether it runs, and any entry-point design has to say
  what is verified before that point. A byte-level reader that identifies the attribute by its base-type
  reference also cannot see an indirectly derived one, so bytes and runtime can disagree about whether an
  assembly declares a contract at all; that asymmetry has to be closed by comparing the resolved attribute's
  exact type and declaring assembly against what the bytes said, and it is the kind of thing that belongs in
  a design rather than in a spike's leftovers.

## 7. What this does not claim

- No entry point, no discovery mechanism, no projection schema, and no generated value reader. Removed from
  this branch deliberately.
- No generated-metadata hash and no projection-schema hash, so nothing here is published, preflight-checked
  or agreed after load. G07.2's exit conditions are untouched.
- Nothing about a browser. The declaration proved here is compiled into a desktop test assembly; the client
  half is G07.1's loader plus work not yet done.
- Nothing about trimming end to end. The metadata is the trimming-safe kind and the trim-safe overload is the
  one asserted, but the client still publishes untrimmed for the framework defect recorded in
  `client-contract-loading.md` §3.
- The wire form here uses declared property names, because the framework's generator applies no naming policy
  by default and none was set. Whether the client wire form carries one is undecided; `ApiJsonOptions` uses
  camel case for every other payload, and a per-kind policy would be the wrong way to answer it.

## 8. Suite

`dotnet build Arronix.sln -c Release` then `dotnet test Arronix.sln -c Release --no-build`, under the pinned
SDK: **3,149 passed, 302 skipped, 0 failed** across 14 test projects. The registered skip count is unchanged
— 301 Movies cases and one architecture case — so the ratchet is untouched. `Arronix.Plugin.Movies.Tests`
moves from 370 to 378 passing.

## 9. Evidence index

| Claim | Where |
| --- | --- |
| Framework metadata exists for every type a movie reaches | `MovieContractSerializationTests.TheGeneratorProducesMetadataForTheWholeMovieGraph` |
| A complete movie round-trips through the trim-safe overload | `.EveryMovieValueSurvivesTheRoundTrip` |
| A rating and its scale round-trip | `.ARatingAndItsScaleSurviveTheRoundTrip` |
| A lifecycle round-trips and its stage is recomputed | `.ALifecycleSurvivesTheRoundTripAndItsStageIsRecomputed` |
| No derived value is written, and a forged one changes nothing | `.NoDerivedValueIsWrittenAndAForgedOneCannotChangeWhatIsRead` |
| An authoritative get-only value is still written | `.AnAuthoritativeGetOnlyValueIsStillWritten` |
| A type reached only through a derived value leaves the wire | `.ATypeReachedOnlyThroughADerivedValueIsNotAWireType` |
| The wire form uses declared property names | `.TheWireFormUsesTheDeclaredPropertyNames` |
| Removing `[JsonConstructor]` fails 4 of 8 | mutation, §4 |
| Removing the five `[JsonIgnore]` attributes fails 2 of 8 | mutation, §5 |
