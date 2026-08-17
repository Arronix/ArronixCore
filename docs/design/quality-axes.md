# The quality-axes model — contract specification

> **Status:** Design, **pass 4 — the amendment pass**. Supersedes the rung ladder for every media kind.
>
> **What changed in pass 4, and why.** Pass 3 was read adversarially, title by title, against the tree.
> The critique is recorded in `docs/open-decisions.md` **Part 7**; thirty real release shapes were mapped
> by hand and **ten of them did not map at all**. Its central refutation stood: pass 3's proudest result —
> that Remux/Bluray and WEB-DL/WEBRip are "the same relationship" — was *exactly* what broke the model,
> because no configuration of a per-axis policy could hold `WEBDL ≈ WEBRip` and `Bluray < Remux` at the
> same time, and §5.1's own shipped default silently chose the second and lost the first. Everything in
> this pass follows from answering that and the four findings behind it. Where pass 3 claimed more than it
> delivered — label totality (§7), "structurally unrepresentable" cross-family comparison (§1.5), "TV's
> quality costs zero lines" (§1.5), a 130-row rung table (§0) — **the claim is retracted here in plain
> words rather than reworded into survival.**
>
> **Two owner resolutions are binding on this pass** (`open-decisions.md` Part 7, 2026-08-17):
> **D-7** — the untouched-master case is promoted out of `Generation` and becomes its own `Origin`
> member, so the disc pair separates strictly on `Origin` while `Generation` stays tieable for the web
> pair (§2.1, §5.1). **D-8** — the primary axes stay strictly lexicographic, and beneath them an optional
> **bounded additive facet score** is consulted *only* when the core judgement is a tie (§3.7). Neither is
> re-argued below; both are implemented.
>
> **Binding direction:** the owner's brief of 2026-08-17 — *"the weights and rungs are not even well
> defined; build a cleaner, more strongly typed model, and a more innovative/intuitive way to do it."*
> Reads onto `docs/open-decisions.md` **D-1** (revision ordering), **D-3** (corpus), **D-7**, **D-8**,
> **P2-5** (regexes) and **Part 6** (the typed direction), and slots into
> `docs/design/typed-media-model.md` as the quality half of the same idea.
>
> **Scope:** the quality model for all four kinds, the user-owned policy over it, the computed size model,
> the display renderer, and the migration. Parsing stays regex (P2-5 survives); this document says exactly
> where parsing stops.
>
> **Secondary consequence, stated once because it is a project goal and not a design argument.** Radarr's
> 29-rung ladder, its weights, its tie groups and its megabytes-per-minute rows are the largest block of
> verbatim GPL-derived *data* left in the repository. This model does not paraphrase them; it replaces the
> thing they are data for. What is left after §4 and §6 is our own table computed from published
> standards. That matters for the licence question, but nothing below is designed *for* it — every choice
> is argued on the domain, and the licence consequence follows or it does not.

> **The one-sentence claim.** Quality is not a rung, it is a point in a small space of typed, orderable
> **axes**; the format family declares the axes and reads evidence onto them (with the kind refining what
> only its own releases say), the **user** owns a **policy** that says which axes matter and in what order
> — plus, beneath that order and only on a tie, what a few facets are worth — and every ranking, cutoff,
> upgrade, equivalence and label is a function of those two, so nothing about preference is baked into
> data anybody but the user wrote.

---

## 0. What is wrong with the ladder, sharpened

The owner's list, each item confirmed against the tree, plus two of my own and one correction.

| # | Defect | Where it is visible today |
|---|---|---|
| L-1 | **The scalar weight bakes one global preference into data.** A single `int` must simultaneously express "2160 beats 1080", "disc beats stream", "untouched beats re-encoded" and "corrected beats original". One number cannot carry four opinions, so upstream grew Custom Formats to carry the rest. | `QualityTier.Weight`, `EffectiveWeight` |
| L-2 | **An equivalence relation grafted onto a total order.** `WEBDL-1080p` and `WEBRip-1080p` share `Weight = 18` *and* a `GroupName`, so the ladder is not a total order and the `Rank`/`Weight` split exists only to hide that. | `MoviesLadder.cs:72-73`; `QualityTier` remarks |
| L-3 | **D-1's revision ordering is a second orthogonal axis crushed into the scalar.** Weight is compared first everywhere, so the revision only ever breaks a tie — and the register has an open dispute about how to collapse `(version, real, repack)` into one order that is only a dispute *because* it must be collapsed. | `QualityRevision.CompareTo`; D-1 |
| L-4 | **`RoundUp` exists because ~29 rungs are a partial function over a ~360-cell cross-product.** 7 sources × 6 resolutions × remux/not × disc/not is far more cells than rungs, so most evidence combinations have no rung and the engine invents one. | `RungFallback.RoundUp`; `Movies.cs:303` |
| L-5 | **29 hand-written MB/min rows are a bitrate expectation stored as magic numbers.** **All thirty** rows carry one of two identical triples — 21 carry `(0, 100, 95)` and 9 carry `(0, null, null)` — which is the tell: the data was never per-rung, it was per-*class*, and the class is (resolution × codec). *(Pass 3 said "twenty-nine of the thirty"; it is thirty of thirty, which strengthens the argument.)* | `MoviesLadder.cs` — every `Tier(...)` call |
| L-6 | **The ladder conflates what the KIND detects with what the USER prefers.** The file says so itself about its own top two rungs: *"both are, for most users, unwanted — which is a profile decision and not a ranking one"* — and then ranks them anyway, at 25 and 26 of 26. | `MoviesLadder.cs:84-87` |
| L-7 | **A ladder forces unrelated quality spaces into one ordering.** Readarr interleaves ebook and audiobook rungs, making any audiobook an upgrade over any ebook. Our `FormatFamily` already fixes *that* case; it does not fix the general one, because within one family the ladder is still a single order over several dimensions. | `FormatFamily` remarks; `BooksQualityModel` |
| **L-8** | **New: the rung table is a 101-row ranking function living in the parser.** `MoviesParsing.RungTable()` is **101** `R(...)` rows (15 bluray + 5 webdl + 4 webrip + 6 pre-release + 6 hdtv + 5 bdrip + 2 dvd + 6 pdtv + 4 orphan-remux + 12 anime-bd + 7 anime-web + 11 container-evidence + 5 res-alone + 13 weak) plus a **7**-row container fallback, whose entire job is to collapse evidence into one of 29 names — a lossy projection performed *before* anything can reason about it. Every row is a small hand-made ranking decision in a file that is supposed to be about reading text. *(Pass 3 said 130 in three places; the count is 101 + 7 = 108, which is what `clean-room-plan.md` says and what the file contains. Every "130" and every "~155 lines" in pass 3 is corrected here and in §6.1/§6.3.)* | `MoviesParsing.cs:243-374` |
| **L-9** | **New: nothing can express a preference the ladder's author did not anticipate.** HDR flavour, audio format, streaming distributor and release group are not on the ladder, so upstream's answer is a bolt-on scoring system with `-10000` as a magic rejection value. The bolt-on is not an accident of that codebase; it is what a single ordered axis forces. | `{Custom Formats}` reserved in `TokenRegistry.cs:54` and nowhere implemented |

**One correction to the brief's own sketch, made here so it is not carried forward.** The sketch proposed
*"Fidelity with an encode-generation count"* as one axis. Generation count alone cannot separate a WEB-DL
from a Remux — both are zero re-encodes — and cannot separate a cam from a telecine, which differ in what
was captured rather than in how many times it was re-encoded. §2.1 splits the sketch's `Fidelity` into
**`Origin`** (which master signal the file carries) and **`Generation`** (how many lossy re-encodes since),
and checks the result row by row against real release taxonomy. Everything else in the sketch survives.

**One correction to pass 3's own split, which is D-7.** Pass 3 put the master/rip step entirely on
`Generation`, and that is what broke. `Origin` now names the master class **of the signal the file
actually carries** — which includes whether it carries that master's own bitstream or an encode targeted
below it — and `Generation` counts re-encodes *within* a class. The dividing rule is stated once, in §2.1,
and it is not a new opinion: §4's `MasterFactor` table already contained it, because that table's factor
varies by master at generation 0 and collapses to a single rip factor at generation ≥ 1.

---

## 1. The axis framework

Placement: `src/Arronix.Abstractions/Quality/`, under a new experimental area
`ExperimentalContracts.Quality = "ARX0021"`. It crosses the extension boundary (a kind declares axes) and
the client boundary (the client renders a policy editor and a quality label), so the contract assembly is
where the placement rule in `Wire/MediaKindDescriptor.cs` puts it.

### 1.1 The one concept

> **There is exactly one concept: the axis. There is no second kind of quality fact.**

Revision counts are axes. Flaws are an axis. Packaging is an axis. HDR is an axis. This is a design
invariant, not a coincidence, and it is what keeps the policy vocabulary small: `Prefer`, `Refuse`,
`Require` and `GoodEnoughAt` are written once and work on everything. The moment a second concept appears
— "and also a flags field", "and also a score" — the policy vocabulary doubles and the profile UI stops
being readable. Hold the design to it.

An axis has three properties and nothing else:

* an **identity** (`QualityAxisId`), derived from the property name;
* a **form** — `Ordinal` (a closed set with a declared order), `Scalar` (an ordered quantity with a unit),
  or `Nominal` (a closed set with no order at all), derived from the CLR type and one attribute argument;
* a **polarity** for the ordered forms — whether a greater CLR value means *more of what the axis
  measures*.

Polarity is a fact, not a preference: on `Generation`, more re-encodes is strictly less retained
information. Whether the user *wants* more of what an axis measures is policy, and policy may invert it
(§3.2) for the person who prefers small files.

### 1.2 Evidence, and "no evidence" as a typed state

The sentinel this replaces is `MoviesLadder.Unknown` — a fake 30th rung at weight 1, plus
`RungFallback.RoundUp`, plus `ContainerFallbacks`, three separate mechanisms for "we do not know". All
three are gone; the framework has one representation and the policy decides what it means.

```csharp
namespace Arronix.Abstractions.Quality;

/// <summary>
/// One axis reading, or the typed absence of one.
/// </summary>
/// <typeparam name="TValue">The axis's value type: an enum for a closed axis, a number for a quantity.</typeparam>
/// <remarks>
/// <para>
/// Not <c>TValue?</c>, for two reasons. A nullable carries no provenance, and provenance decides trust —
/// a resolution a release <i>title</i> claims and a resolution a container <i>probe</i> measured are not
/// the same evidence, and the surveyed application needed an entire per-kind
/// <c>IgnoreStatedResolutionFor</c> list because it could not tell them apart. And a nullable makes
/// "absent" comparable by accident: <c>null</c> sorts somewhere, silently, which is exactly how the
/// weight-1 unknown rung came to exist.
/// </para>
/// <para>
/// There is deliberately no <c>Value</c> property that throws. An absent reading is a state a caller must
/// handle, and the type makes handling it the only thing you can do.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct Evidence<TValue>
    where TValue : struct
{
    /// <summary>Gets the reading meaning "nothing in the evidence spoke to this axis".</summary>
    public static Evidence<TValue> None { get; }

    /// <summary>Creates a reading with its provenance.</summary>
    /// <param name="value">The value read.</param>
    /// <param name="source">Where it was read from.</param>
    /// <returns>The reading.</returns>
    public static Evidence<TValue> From(TValue value, EvidenceSource source);

    /// <summary>Gets whether anything was read.</summary>
    public bool IsKnown { get; }

    /// <summary>Gets where the reading came from. Meaningless when nothing was read.</summary>
    public EvidenceSource Source { get; }

    /// <summary>Reads the value when there is one.</summary>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when a value was read.</returns>
    public bool TryGet(out TValue value);

    /// <summary>Reads the value, or a stated fallback when there is none.</summary>
    /// <param name="fallback">The value to use when nothing was read.</param>
    /// <returns>The value or the fallback.</returns>
    public TValue Or(TValue fallback);
}

/// <summary>A set-valued reading, for an axis a release can carry several members of at once.</summary>
/// <typeparam name="TValue">The member type.</typeparam>
/// <remarks>
/// An empty set and an absent reading are different: "the evidence named no flaws" and "we did not look"
/// are different claims, and a policy that refuses a flaw must not refuse a release it never inspected.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct EvidenceSet<TValue>
    where TValue : struct, Enum
{
    /// <summary>Gets the reading meaning "we did not look".</summary>
    public static EvidenceSet<TValue> None { get; }

    /// <summary>Gets the reading meaning "we looked and found nothing".</summary>
    public static EvidenceSet<TValue> Empty(EvidenceSource source);

    /// <summary>Creates a reading holding the stated members.</summary>
    /// <param name="source">Where the members were read from.</param>
    /// <param name="members">The members found.</param>
    /// <returns>The reading.</returns>
    public static EvidenceSet<TValue> Of(EvidenceSource source, params TValue[] members);

    /// <summary>Gets whether anything was looked for.</summary>
    public bool IsKnown { get; }

    /// <summary>Gets where the reading came from.</summary>
    public EvidenceSource Source { get; }

    /// <summary>Gets the members found. Empty when nothing was found or nothing was looked for.</summary>
    public IReadOnlyList<TValue> Members { get; }

    /// <summary>Gets whether a member was found.</summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when the reading holds it.</returns>
    public bool Has(TValue member);
}

/// <summary>Where an axis reading came from, in ascending order of trust.</summary>
/// <remarks>
/// Ordered, and the order is load-bearing: when two sources disagree the later one wins, which is what
/// replaces the surveyed application's per-kind list of sources whose stated resolution must be ignored.
/// A camera capture's title claiming 1080p is <see cref="ReleaseTitle"/>; a probe measuring 480 lines is
/// <see cref="ContainerProbe"/>; the probe wins with no per-kind rule required.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum EvidenceSource
{
    /// <summary>Inferred by a stated default rather than read from anything. The weakest claim there is.</summary>
    Assumed = 0,

    /// <summary>Read from the release title.</summary>
    ReleaseTitle = 1,

    /// <summary>Read from the file's own name, which at least survived a download.</summary>
    FileName = 2,

    /// <summary>Read from the container's declared streams.</summary>
    ContainerProbe = 3,

    /// <summary>Measured by decoding the stream.</summary>
    StreamProbe = 4,

    /// <summary>Stated by the user, who is allowed to be wrong and is allowed to overrule us.</summary>
    UserOverride = 5,
}
```

**Between sources, the later one wins. Within one source, this rule** — new in pass 4, because the
common case is not two sources disagreeing but one source saying two things at once, and `Evidence<T>`
holds exactly one value and has nowhere to put a tie-break:

> **The most specific reading wins. Among equally specific readings, the lowest claim wins.**

Specificity is an ordering over *forms of statement*, not over values, and each axis states its own:

| Axis | Most specific → least |
|---|---|
| `Resolution` | an explicit line count (`1080p`, `720p`, `540p`) → an explicit raster (`1920x1080`) → a marketing name (`UHD`, `[4K]`, `FHD`) → a container or scene-convention inference |
| `Origin` | an unambiguous source token (`WEB-DL`, `BDRip`) → a compound token (`UHDBDRip`) → a bracketed convention (`[BD]`) → a container inference |
| `Codec` | a codec token (`x265`, `h266`) → a profile or depth token → a container-declared codec |

Two worked cases, both from the shipped corpus. `Movie Name 2005 1080p UHD BluRay …-LoRD` (q109) states
`1080p` *and* `UHD` at one source: the explicit line count is more specific, so `Resolution = 1080` — which
is what the current scanner does by a hard-coded precedence that pass 3 never restated. `4kto1080p` states
a transformation, which is the most specific form of all: it reads as `Resolution = 1080` **and**
`VideoFlaw.Upscaled`, where today it silently launders into a clean 1080.

The lowest-claim tie-break exists because the two failure directions are not symmetric: a missed claim
leaves a release ranked low, and a false claim promotes junk past everything the user asked for.
`IQualityType.Read` owns this rule; nothing downstream of `Read` ever sees the conflict.

**And a rule that is *not* here, deliberately.** Source precedence fixes the *reading*. It does not fix
the *grab*, because only the held file is ever probed — a title that over-claims 1080p against a file we
measured at 720 would be re-grabbed on every RSS pass, forever. That is a decision rule, not a reading
rule, and it lives in §3.2 and §3.4 where the irreversible call is made.

### 1.3 The attribute vocabulary

Two attributes. Both obey the typed-media-model's dividing rule verbatim: *an attribute states a fact
about one property in isolation; anything relating two things goes in the builder; an attribute never
takes an identifier string.*

```csharp
/// <summary>Declares that a property of a quality-facts type is a quality axis.</summary>
/// <remarks>
/// The axis's <i>form</i> derives from the CLR type and this attribute's <see cref="Ordering"/>; its
/// identity derives from the property name; its prose comes from <c>[Display]</c>, which is already the
/// vocabulary's single source of prose. Nothing here says where the axis sits in anyone's preference —
/// that relates the axis to other axes and is therefore policy, not an attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class AxisAttribute : Attribute
{
    /// <summary>Gets how the axis's values relate to one another.</summary>
    public AxisOrdering Ordering { get; init; } = AxisOrdering.Ascending;

    /// <summary>Gets the unit a quantity is expressed in, for presentation only.</summary>
    public string? Unit { get; init; }
}

/// <summary>How an axis's values relate to one another.</summary>
public enum AxisOrdering
{
    /// <summary>A greater value is more of what the axis measures. Resolution, dynamic range.</summary>
    Ascending = 0,

    /// <summary>A greater value is less of what the axis measures. Re-encode generations.</summary>
    Descending = 1,

    /// <summary>
    /// The values do not order. A container format, a distributor, a defect: membership is a fact,
    /// "more" is not.
    /// </summary>
    Unordered = 2,
}
```

Form derivation, mirroring the field-descriptor rules in `typed-media-model.md` §4.4:

| Property type | `Ordering` | Axis form |
|---|---|---|
| `Evidence<TEnum>` | `Ascending` / `Descending` | `Ordinal`; declared order is the enum's member order |
| `Evidence<TEnum>` | `Unordered` | `Nominal` |
| `Evidence<int>`, `Evidence<double>` | `Ascending` / `Descending` | `Scalar` |
| `EvidenceSet<TEnum>` | any (forced `Unordered`) | `Nominal`, multivalued |
| anything else — **including `Evidence<bool>` and `Evidence<string>`** | — | analyzer error `ARXQ001` |

**A `Scalar` axis may not be declared `Unordered`, and an `EvidenceSet` may not be declared ordered** —
both are analyzer errors, because both would produce an axis whose comparison is undefined. These join
the `WP-14` analyzer's rules (`typed-media-model.md` §4.9, C8), which must ship before the first quality
type for the same reason: otherwise the guarantee moves from compile time back to load time.

**Two axes in pass 3 violated this table, in the same document that states it.** `Evidence<string>
Distributor` was caught and wrapped; `Evidence<bool> Repacked` was not, and would have failed `ARXQ001`
before the video family compiled. Both are fixed in §2.1: `Distributor` over a
`readonly record struct Distributor(string Token)`, and `Repacked` over
`enum Repackaging { Original = 0, Repacked = 1 }`. `bool` is not an enum and the table does not make an
exception for it; there is no reading of pass 3 under which that axis compiled.

**The analyzer rule set, restated in full**, because pass 4 adds to it:

| Rule | Says |
|---|---|
| `ARXQ001` | A property carrying `[Axis]` is `Evidence<TEnum>`, `Evidence<int>`, `Evidence<double>` or `EvidenceSet<TEnum>`. Nothing else. |
| `ARXQ002` | A `Scalar` axis is not declared `Unordered`. |
| `ARXQ003` | An `EvidenceSet` axis is not declared ordered. |
| `ARXQ004` | A quality-facts type declares at least one axis, and no two axes derive the same `QualityAxisId`. |
| **`ARXQ005`** | **New (§3.2).** A `Nominal` axis does not appear in `QualityPolicy.Precedence`. It may carry a facet score (§3.7). |
| **`ARXQ006`** | **New (§3.7).** An axis appears in `Precedence` **or** carries a facet score, never both. This is the disjointness the cycle-safety argument rests on. |

`UnknownEvidence.Ignore` and `UnknownEvidence.Refuse` in a precedence entry needed no analyzer rule: §3.2
makes them **structurally unrepresentable** there by giving `AxisPreference.WhenUnknown` its own restricted
type. That is the pattern this document now follows and labels: *structural where it is cheap, runtime-
validated where it is not, and the document says which one each guarantee is* (contrast §1.5, where the
structural version is not cheap and pass 3 claimed it anyway).

### 1.4 The quality type, and the non-generic handle

Exactly the split `IMediaType<TItem>` / `IMediaType` already makes: a static-abstract authoring seam a
plugin writes, and a runtime handle the host and client hold. The two are unrelated by inheritance for
the same reason as before — one is how a thing is written, the other is what is held afterwards.

```csharp
/// <summary>The typed facts one file's quality consists of. Marker only.</summary>
/// <remarks>
/// Deliberately empty, exactly as <see cref="IMediaItem"/> is: everything about a quality type is read
/// from its properties.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityFacts;

/// <summary>The authoring seam: one type per format family, declaring how evidence becomes facts.</summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityType<TFacts>
    where TFacts : IQualityFacts
{
    /// <summary>Gets the format family these facts describe.</summary>
    static abstract FormatFamilyId Family { get; }

    /// <summary>Declares what the axis attributes cannot: labels, the size model, the stated default.</summary>
    /// <param name="builder">The builder.</param>
    static abstract void Configure(IQualityTypeBuilder<TFacts> builder);

    /// <summary>Reads release and file evidence onto the axes.</summary>
    /// <param name="evidence">What the parser and any probe produced.</param>
    /// <returns>The facts.</returns>
    /// <remarks>
    /// Ordinary C#. This is the method that replaces the 101-row rung-resolution table and its seven-row
    /// container fallback: there is nothing to collapse evidence <i>to</i>, so the whole <i>ranking</i>
    /// cascade disappears. The <i>inference</i> cascade does not — it moves in here, smaller and local
    /// to one function per axis. §6.3 says exactly which half goes and which half moves, and §1.7 gives
    /// the honest line count.
    /// </remarks>
    static abstract TFacts Read(ReleaseEvidence evidence);
}

/// <summary>Declares what the axis attributes cannot: the family's identity, its labels, its size model
/// and its stated default policy.</summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// The same dividing rule as the media-type builder: an attribute states a fact about one axis in
/// isolation; everything here relates two or more axes, or is about the family as a whole. A label rule
/// reads two axes at once; the size model reads four; the default policy orders all of them.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityTypeBuilder<TFacts>
    where TFacts : IQualityFacts
{
    /// <summary>Names the family.</summary>
    /// <param name="name">The display name.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityTypeBuilder<TFacts> Named(string name);

    /// <summary>Declares one rendering rule. Declared order is the rule order; the first match wins.</summary>
    /// <param name="when">What must hold.</param>
    /// <param name="label">The community's word for it.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// An <see cref="Expression{TDelegate}"/> rather than a <c>Func</c>, matching <see cref="Suffix"/>:
    /// the rule is authored against the typed facts and evaluated against an erased
    /// <see cref="QualityPoint"/>, and a compiled delegate cannot be rewritten onto the point. Pass 3
    /// declared a <c>Func</c> here and a <c>Func&lt;QualityPoint,bool&gt;</c> in §7 and left the two
    /// disagreeing; the expression form is what makes the rewrite possible and is the one that ships.
    /// </remarks>
    IQualityTypeBuilder<TFacts> Label(Expression<Func<TFacts, bool>> when, string label);

    /// <summary>Declares which axis the standard label suffixes with, and how.</summary>
    /// <param name="axis">The axis, as a property reference.</param>
    /// <param name="format">How the value is spelled, e.g. <c>"-{0}p"</c>.</param>
    /// <param name="appliesWhen">Which labels take the suffix.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityTypeBuilder<TFacts> Suffix<TValue>(
        Expression<Func<TFacts, Evidence<TValue>>> axis,
        string format,
        Func<string, bool> appliesWhen)
        where TValue : struct;

    /// <summary>Declares the family's expected-size model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityTypeBuilder<TFacts> Sizes(Func<TFacts, TimeSpan, SizeExpectation> model);

    /// <summary>Declares the policy shipped as this family's stated opinion.</summary>
    /// <param name="configure">The declaration.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The one place in the whole model where a preference is written by anybody but the user, and it is
    /// a <i>default</i>: a profile that replaces it owes nothing to it. §5 argues every line of ours.
    /// </remarks>
    IQualityTypeBuilder<TFacts> DefaultPolicy(Action<IQualityPolicyBuilder> configure);
}

/// <summary>One format family's runtime quality model, held by the host and served to the client.</summary>
/// <remarks>
/// Built by the host from an <see cref="IQualityType{TFacts}"/>; never implemented by a plugin. Every
/// member is derived from the facts type or carried verbatim from the builder, so there is no second
/// source of truth and nothing here can disagree with the facts type.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityType
{
    /// <summary>Gets the format family.</summary>
    FormatFamilyId Family { get; }

    /// <summary>Gets the family's display name.</summary>
    string Name { get; }

    /// <summary>Gets the facts type, so serialization and storage have a target.</summary>
    Type FactsType { get; }

    /// <summary>Gets the declared axes, in declaration order. Declaration order is not preference order.</summary>
    IReadOnlyList<QualityAxis> Axes { get; }

    /// <summary>Gets the policy shipped as this family's stated opinion. A user may replace it entirely.</summary>
    QualityPolicy DefaultPolicy { get; }

    /// <summary>Reads evidence onto a kind-blind point.</summary>
    /// <param name="evidence">What the parser and any probe produced.</param>
    /// <returns>The point.</returns>
    QualityPoint Read(ReleaseEvidence evidence);

    /// <summary>Projects typed facts onto a kind-blind point.</summary>
    /// <param name="facts">An instance of <see cref="FactsType"/>.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentException"><paramref name="facts"/> is not of <see cref="FactsType"/>.</exception>
    QualityPoint Project(object facts);

    /// <summary>Renders a point in the community's vocabulary.</summary>
    /// <param name="point">The point.</param>
    /// <param name="detail">How much of the point to spell.</param>
    /// <returns>The label, e.g. <c>WEBDL-1080p</c>.</returns>
    string Label(QualityPoint point, QualityLabelDetail detail);

    /// <summary>Reads a community label back into a point, for a pasted profile or a stored string.</summary>
    /// <param name="label">The label.</param>
    /// <param name="point">Receives the point.</param>
    /// <returns><see langword="true"/> when the label was understood.</returns>
    bool TryParseLabel(string label, out QualityPoint point);

    /// <summary>Computes the size a file at this point is expected to be.</summary>
    /// <param name="point">The point.</param>
    /// <param name="duration">The item's duration. <see cref="TimeSpan.Zero"/> when unknown.</param>
    /// <returns>The expectation, or an unassessable one when the family has no size model.</returns>
    SizeExpectation ExpectedSize(QualityPoint point, TimeSpan duration);
}
```

**Not on it, deliberately: any comparison.** `IQualityType` cannot tell you which of two points is
better, because that question has no answer without a policy. This is the single most important line in
the document: the type that *knows about* quality is structurally incapable of *ranking* it. That is what
"the ladder conflates what the kind detects with what the user prefers" (L-6) looks like when it is
fixed.

#### 1.4a The third seam: per-kind evidence refinement

Pass 3 had two seams — the family reads evidence, the user states a policy — and that was one too few.
`IQualityType<TFacts>.Read` lives in the **contract assembly**, and its inputs include
`ReleaseEvidence.Guards`, which is a set of **kind-owned identifier strings**. Reproducing today's answers
needs 20 of Movies' 29 guards, so a literal reading of pass 3 puts
`evidence.Guards.Contains("german-remux")` inside `Arronix.Abstractions.Quality.Families.VideoQualityType`
— a Movies identifier hard-coded in the contract assembly. That is a layering inversion and a straight
regression against **P2-2**, which the register records as closed precisely because *"field ids, token
names … are all derived from typed properties"*, and against `typed-media-model.md`'s dividing rule that
*"an attribute never takes an identifier string"*.

So there is a third seam, declared by the **kind**, applied by the **host**, after the family's `Read`:

```csharp
/// <summary>A kind's chance to contribute its own evidence to its family's axes.</summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// <para>
/// The family reads what every kind's releases say; a kind refines that with what only <i>its</i>
/// releases say. Fansub bracket conventions, a scene's disc-image spelling, a kind's own guard set —
/// all of it stays inside the plugin that owns the strings, and the contract assembly never learns an
/// identifier it did not derive.
/// </para>
/// <para>
/// <b>Refinements may only strengthen absence into presence, or raise a reading's source.</b> A
/// refinement that returns a reading at a weaker <see cref="EvidenceSource"/> than the one already
/// present is discarded by the host, which is what stops a per-kind heuristic from overwriting a probe.
/// A refinement therefore cannot make the family's reading <i>worse</i>, only more complete.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityRefinement<TFacts>
    where TFacts : IQualityFacts
{
    /// <summary>Gets the family the refinement contributes to.</summary>
    static abstract FormatFamilyId Family { get; }

    /// <summary>Contributes this kind's own evidence.</summary>
    /// <param name="read">What the family read.</param>
    /// <param name="evidence">The same evidence the family saw, including this kind's guards and tags.</param>
    /// <returns>The refined facts.</returns>
    static abstract TFacts Refine(TFacts read, ReleaseEvidence evidence);
}
```

Registered beside the kind's other declarations:

```csharp
b.Format(StandardFormatFamily.Video)
 .RefinedBy<VideoQuality, MoviesVideoRefinement>();
```

What Movies actually puts in it, after §6.3 moves everything that can be moved to the family:

| Guard | Contributes | Why it cannot be family-level |
|---|---|---|
| `anime-bd`, `anime-web` | `Origin`, `Generation` at `Assumed` | bracketed fansub conventions (`[FFF]`, `[HorribleSubs]`) are a Movies/TV release-naming dialect, not a video fact |
| `br-disk` | `Packaging = DiscImage` at `Assumed` | a heuristic over scene disc-naming habits, and a demonstrably over-firing one (§10 rows 15–16) |
| `hr-ws`, `text-*` | `Resolution` at `Assumed` | legacy scene spellings with no typed form |

And what does **not** go in it, because pass 4 moved it up rather than sideways: `german-remux` becomes a
family rule keyed on the typed `Languages` axis (§2.1), `mpeg2` becomes a family rule keyed on the typed
`Codec` axis, `container-web`/`container-disc`/`container-dvd` become family rules keyed on the typed
`Container` member, and `px-*` becomes a host scanner (QA-17). **Four of the seven guard classes leave the
per-kind residue entirely**; the seam exists for the three that genuinely are dialect.

This is the honest replacement for pass 3's "TV's quality costs zero lines" (retracted in §1.5): TV costs
one refinement class, one label override and one policy override.

### 1.5 Where a quality type lives — one sharpening of the brief

The brief says *"each kind declares its own quality TYPE with orderable axes"*. I am sharpening that to:

> **A quality type belongs to a format family, not to a media kind.**

Three reasons, in descending order of force.

1. **Quality is a property of a file, and a file belongs to a family.** `FormatFamily` already exists,
   already carries the ladder, already declares the extension set that decides which family a file is in,
   and is already validated pairwise disjoint. Hanging quality off the kind rather than the family is one
   indirection too many and produces the exact defect `FormatFamily`'s own remarks were written to
   prevent.
2. **It reduces Books' cross-family guards from three hand-written rules to one runtime check.**
   Requirement 2 asks that the ebook/audiobook split be unrepresentable as one ordering.
   **Pass 3 claimed this was delivered structurally. It is not, and the claim is withdrawn.**
   `QualityPoint` is a single non-generic sealed record carrying `FormatFamilyId Family` as *data*, and
   the signature pass 3 itself gives is
   `QualityJudgement Compare(QualityPoint held, QualityPoint candidate)` with an
   `<exception cref="ArgumentException">Either point belongs to another family.</exception>`.
   `Compare(ebookPoint, audiobookPoint)` **compiles**, and then throws. That is a hand-written cross-family
   guard with a different spelling — moved out of `BooksQualityModel` and into `QualityPolicy`, not
   deleted. What is genuinely won is real and smaller than advertised: **one guard in one place, exercised
   by one test, instead of three guards in each of three quality models** (`IsUpgrade` returning false,
   `MeetsCutoff` returning true, `FamilyOf` scanning two ladders).

   **Why the structural version is not taken.** `QualityPoint<TFacts>`, or a phantom family type
   parameter, would make it a compile error. The host stores points, serializes them to the wire and holds
   them in heterogeneous collections keyed by item, so a non-generic erasure is required anyway — the
   generic version buys a compile-time guarantee at the authoring seam and then re-introduces the runtime
   check at every storage boundary, which is more guard sites, not fewer. **Runtime-validated, stated as
   such.** Contrast §1.3, where the analogous restriction *was* cheap to make structural and therefore
   was.

3. **Movies and TV then share one video quality type with no duplication and no drift.** A WEB-DL 1080p
   is a WEB-DL 1080p; the same scene groups, the same tokens, the same taxonomy. Radarr and Sonarr keep
   two near-identical ladders that *have* drifted — different pre-release rungs, different Remux handling
   — which is P2-7's "silent divergence" failure at kind scale. The precedent for host-owning it is
   already set twice this project: `MatchConfidencePolicy` moved host-side because *"how far to trust an
   identifier is the same question for every media kind"*, and `LeadingArticles` moved host-side after
   Movies and Tv had already disagreed about it.

**Resolution table — do Movies and TV share one quality type?**

| Option | For | Against | Verdict |
|---|---|---|---|
| Each kind declares its own | Literal reading of the brief; a kind could specialize | Two copies of one taxonomy, guaranteed to drift; TV's quality then costs ~150 lines it should not | **Rejected** |
| The host owns one video model, kinds cannot vary it | No drift possible | A comics kind wanting scan-resolution axes has nowhere to go | **Rejected** |
| **The family owns it; the host ships standard families; a kind may declare a bespoke one** | No drift for the 95% case; TV's quality costs **one small class plus two overrides** (see below); a bespoke family is one class | The video model lives in the contract assembly, which is fatter than it was | **Chosen** |

**"TV's quality costs zero lines" is withdrawn.** `TvShape.cs:261-280` is 20 rungs and
`MoviesLadder.cs:42-88` is 29, and the differences are decisions rather than drift to be cured:

| | Movies | Tv |
|---|---|---|
| Pre-release rungs (WORKPRINT/CAM/TELESYNC/TELECINE/REGIONAL/DVDSCR) | 6 rungs | **absent** |
| `DVD-R`, `Bluray-576p`, `BR-DISK` | present | **absent** |
| `Raw-HD` | rank **30**, top of ladder | rank **11**, below `WEBRip-1080p` |
| Remux label at 1080p | `Remux-1080p` | **`Bluray-1080p Remux`** |
| Remux label at 2160p | `Remux-2160p` | `Bluray-2160p Remux` |

One shared family with one shared label table cannot render both spellings, and one shared default policy
either admits `FilmPrint`/TELECINE for TV — which has no rung for it — or refuses it for Movies. So the
honest number is:

> **TV's quality costs one `IQualityRefinement` class (§1.4a), one label override, one policy override,
> and one owner decision about where `Raw-HD` sits.** Small, and not zero.

The label override is a first-class part of the family seam, not a workaround: `IQualityTypeBuilder`
already declares labels, and a kind may supply a replacement rule list for its own rendering. The
`Raw-HD` disagreement moves to §9 beside the `Broadcast`/`Stream` question, because it is the same
question and the tree currently answers it two different ways.

The contract assembly is the right home anyway: the client references `Arronix.Abstractions` only and must
render quality labels and a policy editor, so the label rules and the axis descriptors have to be
reachable from there.

So `src/Arronix.Abstractions/Quality/Families/` ships `VideoQuality`, `AudioQuality`, `WrittenQuality`
and `SpokenQuality` with their types, labels and default policies, and a kind picks one:

```csharp
b.Format(StandardFormatFamily.Video)
 .RefinedBy<VideoQuality, MoviesVideoRefinement>()
 .Facet("edition", "Edition", TechnicalFacetCase.TitleCaseWithExceptions, …);
```

That is Movies' whole quality declaration — two calls, not one, because of §1.4a.
`MoviesLadder.cs` (109 lines) and `b.Quality.IgnoreStatedResolutionFor(...).FallbackRoundUp()` both go to
zero; `MoviesVideoRefinement` arrives at roughly 40 lines covering the three dialect guard classes. The
builder addition:

```csharp
public interface IFormatFamilyBuilder<TItem>
    where TItem : IMediaItem
{
    // …existing members unchanged, except Ladder(), which is deleted…

    /// <summary>Declares the family's quality model.</summary>
    /// <typeparam name="TFacts">The quality-facts type.</typeparam>
    /// <typeparam name="TType">The type declaring it.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    IFormatFamilyBuilder<TItem> Quality<TFacts, TType>()
        where TFacts : IQualityFacts
        where TType : IQualityType<TFacts>;

    /// <summary>Declares this kind's own contribution to the family's axes (§1.4a).</summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TRefinement">The kind's refinement.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Optional. A kind whose releases carry no dialect of its own declares nothing and gets the
    /// family's reading unchanged.
    /// </remarks>
    IFormatFamilyBuilder<TItem> RefinedBy<TFacts, TRefinement>()
        where TFacts : IQualityFacts
        where TRefinement : IQualityRefinement<TFacts>;
}

public interface IMediaTypeBuilder<TItem>
    where TItem : IMediaItem
{
    // …existing members unchanged…

    /// <summary>Declares one of the platform's standard format families, with its quality model.</summary>
    /// <param name="family">The standard family.</param>
    /// <returns>The family builder, for chaining.</returns>
    /// <remarks>
    /// Sugar over <c>Format(id, name).Extensions(…).Quality&lt;…&gt;()</c>. It exists because four kinds
    /// restating the same extension list and the same quality binding is P2-7's failure mode, and a kind
    /// that genuinely differs still has the long form.
    /// </remarks>
    IFormatFamilyBuilder<TItem> Format(StandardFormatFamily family);
}
```

### 1.6 The kind-blind value the host holds

```csharp
/// <summary>One axis, as the host and client see it.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityAxis
{
    /// <summary>Gets the axis's identifier, derived from the property name.</summary>
    public required QualityAxisId Id { get; init; }

    /// <summary>Gets the display name, from <c>[Display]</c> or the property name split on case.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the description, from <c>[Display]</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the axis's form.</summary>
    public required AxisForm Form { get; init; }

    /// <summary>Gets whether a greater value means more of what the axis measures.</summary>
    public required bool GreaterIsRicher { get; init; }

    /// <summary>Gets whether a reading may hold several members at once.</summary>
    public bool Multivalued { get; init; }

    /// <summary>Gets the unit a quantity is expressed in. Null for a closed axis.</summary>
    public string? Unit { get; init; }

    /// <summary>Gets the members of a closed axis, in declared order. Empty for a quantity.</summary>
    /// <remarks>
    /// Declared order is the family's <i>claim</i> about fidelity, not the user's preference. A policy
    /// may re-rank it (§3.2), which is what makes a contested pair a setting instead of an argument.
    /// </remarks>
    public IReadOnlyList<AxisValue> Members { get; init; } = [];
}

/// <summary>What an axis's values are.</summary>
public enum AxisForm
{
    /// <summary>A closed set with a declared order.</summary>
    Ordinal = 0,

    /// <summary>An ordered quantity with a unit.</summary>
    Scalar = 1,

    /// <summary>
    /// A closed set with no order. Usable for requirements, for grouping, and for a facet score
    /// (§3.7) — never for precedence, which is <c>ARXQ005</c>.
    /// </summary>
    Nominal = 2,
}

/// <summary>One value on one axis, or the typed absence of one.</summary>
/// <remarks>
/// Carries both a comparable magnitude and the community's spelling, because the same value must serve a
/// comparison and a rendered file name, and deriving one from the other in either direction is how a
/// display string ends up load-bearing.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct AxisValue
{
    /// <summary>Gets the absent value.</summary>
    public static AxisValue None { get; }

    /// <summary>Creates a member of a closed axis.</summary>
    /// <param name="declaredRank">The member's position in the family's declared order.</param>
    /// <param name="token">The member's community spelling.</param>
    /// <returns>The value.</returns>
    public static AxisValue Member(int declaredRank, string token);

    /// <summary>Creates a quantity.</summary>
    /// <param name="magnitude">The quantity, in the axis's unit.</param>
    /// <returns>The value.</returns>
    public static AxisValue Quantity(double magnitude);

    /// <summary>Gets whether there is a value at all.</summary>
    public bool IsKnown { get; }

    /// <summary>Gets the member's position in the declared order. Zero for a quantity.</summary>
    public int DeclaredRank { get; }

    /// <summary>Gets the quantity. Zero for a member.</summary>
    public double Magnitude { get; }

    /// <summary>Gets the community spelling, or the formatted quantity.</summary>
    public string Token { get; }
}

/// <summary>What one axis says about one file.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct AxisReading
{
    /// <summary>Gets the axis.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets the values read. Empty when nothing was read or nothing was found.</summary>
    public IReadOnlyList<AxisValue> Values { get; init; }

    /// <summary>Gets whether anything was looked for.</summary>
    public bool IsKnown { get; init; }

    /// <summary>Gets where the reading came from.</summary>
    public EvidenceSource Source { get; init; }

    /// <summary>Gets the single value of a single-valued axis, or <see cref="AxisValue.None"/>.</summary>
    public AxisValue Value { get; }
}

/// <summary>One file's quality: a point in its family's axis space.</summary>
/// <remarks>
/// Replaces <c>QualityTier</c> as the thing a file record stores, a notification renders and a naming
/// token spells. It carries no rank, no weight and no name — a name is a rendering (§7) and a rank is a
/// policy's opinion (§3).
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityPoint
{
    /// <summary>Gets the family this point belongs to. The only thing that makes two points comparable.</summary>
    public required FormatFamilyId Family { get; init; }

    /// <summary>Gets the readings, one per declared axis, in the family's declaration order.</summary>
    public required IReadOnlyList<AxisReading> Readings { get; init; }

    /// <summary>Reads one axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The reading; an unknown reading when the axis is not declared.</returns>
    public AxisReading this[QualityAxisId axis] { get; }
}
```

### 1.7 How parse evidence populates it

The parse engines already run a host-owned, kind-agnostic scanning layer before any per-kind rule —
`MoviesParsing.cs` says so in its own remarks: *"the shared source, resolution, codec, audio, revision,
remux, group, hash and language scanners"*. Today that layer's output is a string dictionary that a
101-row table then collapses. It becomes a typed record instead — with seven members pass 3 did not
declare, and one it declared without noticing that no scanner fills it (§5.1, QA-17):

```csharp
/// <summary>What the host's scanners and any probe found, before anything ranked it.</summary>
/// <remarks>
/// The typed members are the host-global scanning vocabulary, which is identical for every kind and
/// therefore host-owned. <see cref="Tags"/> and <see cref="Guards"/> are the per-kind residue, and they
/// stay string-keyed because release-title parsing stays regex (P2-5, and <c>typed-media-model.md</c>
/// C6). <b>That is the boundary:</b> strings enter <c>IQualityType.Read</c> and typed axes come out. What
/// changes is that they no longer run all the way into a ranking table.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ReleaseEvidence
{
    /// <summary>Gets the release title as it arrived.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the normalized source token the shared scanner settled on, e.g. <c>bluray</c>.</summary>
    public string? SourceToken { get; init; }

    /// <summary>Gets the resolution the release states, in lines, exactly as stated.</summary>
    /// <remarks>
    /// <b>Un-bucketed, which the current scanner is not.</b> `ScanResolution` today folds <c>1080i</c>,
    /// <c>1080p</c>, <c>1440p</c>, <c>FHD</c> and <c>4kto1080p</c> onto the single integer 1080 and
    /// <c>UHD</c>/<c>[4K]</c> onto 2160. That destroys the interlace marker before <see cref="Read"/>
    /// can see it, destroys 1440p entirely — which is the showcase §7 uses for label totality — and
    /// launders the one upscale token the vocabulary knows into a clean reading. QA-17 un-buckets it;
    /// the scan type and the upscale move to <see cref="ScanType"/> and <see cref="FlawTokens"/>.
    /// </remarks>
    public int? StatedResolution { get; init; }

    /// <summary>Gets how the stated resolution was stated, for the within-source rule in §1.2.</summary>
    public ResolutionClaimForm StatedResolutionForm { get; init; }

    /// <summary>Gets the scan type the release states, when it states one.</summary>
    public ScanType? ScanType { get; init; }

    /// <summary>Gets the video codec token, e.g. <c>h265</c>, <c>h266</c>.</summary>
    public string? VideoCodecToken { get; init; }

    /// <summary>Gets the audio format token, e.g. <c>truehd-atmos</c>.</summary>
    public string? AudioToken { get; init; }

    /// <summary>Gets the dynamic-range token, e.g. <c>dv</c>.</summary>
    public string? DynamicRangeToken { get; init; }

    /// <summary>Gets whether the release states that it is a bitstream copy.</summary>
    public bool IsRemux { get; init; }

    /// <summary>Gets the stated re-issue number. One when the release states none.</summary>
    public int Version { get; init; }

    /// <summary>Gets how many times the release states it corrects a mislabeled issue.</summary>
    public int RealCount { get; init; }

    /// <summary>Gets whether the release states that it is a repack of the same encode.</summary>
    public bool IsRepack { get; init; }

    /// <summary>Gets the release group.</summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>Gets the distributor token a stream capture names, e.g. <c>amzn</c>.</summary>
    /// <remarks>
    /// <b>No scanner emits this today.</b> AMZN/NF/DSNP appear only as an uncaptured lookahead inside
    /// <c>SourceTags</c>. QA-17 captures it.
    /// </remarks>
    public string? DistributorToken { get; init; }

    /// <summary>Gets the languages the release states, and how it stated them.</summary>
    /// <remarks>
    /// <c>ReleaseLanguageScanner</c> already produces this and already carries the DL/ML rule its own
    /// comment calls <i>"the one genuinely non-obvious rule"</i>. It is surfaced here because a dual-
    /// language disc encode is a remux, and pass 3 left that fact reachable only through the per-kind
    /// <c>german-remux</c> guard string. See §2.1's <c>Languages</c> axis.
    /// </remarks>
    public IReadOnlyList<LanguageClaim> Languages { get; init; } = [];

    /// <summary>Gets how the release is packaged, when a token or an extension says.</summary>
    /// <remarks><b>No scanner emits this today</b> — it is reachable only through a per-kind guard. QA-17.</remarks>
    public string? PackagingToken { get; init; }

    /// <summary>Gets the defect markers the release states: upscale, sample, hardsub, watermark, ad-break.</summary>
    /// <remarks>
    /// <b>No scanner emits these today</b>, and the shipped default in §5.1 refuses two of them, so QA-17
    /// is on the critical path in front of QA-7 rather than beside it.
    /// </remarks>
    public IReadOnlySet<string> FlawTokens { get; init; } = new HashSet<string>();

    /// <summary>Gets the frame rate the release states, when it states one.</summary>
    public double? StatedFrameRate { get; init; }

    /// <summary>Gets the file extension, leading dot included, when there is a file.</summary>
    /// <remarks>
    /// Load-bearing, and pass 3's migration table did not say so. The 11 container-evidence rung rules
    /// (<c>MoviesParsing.cs:339-349</c>) are gated on this, not on a fallback: the file's own comment is
    /// <i>"a bare '540p' inside a Matroska file is a stream download, not a broadcast capture"</i>. §6.3
    /// keeps them as a family rule over this typed member and deletes the three per-kind
    /// <c>container-*</c> guards.
    /// </remarks>
    public string? Container { get; init; }

    /// <summary>Gets the per-kind guards that matched.</summary>
    public IReadOnlySet<string> Guards { get; init; } = new HashSet<string>();

    /// <summary>Gets the per-kind tags the kind's own patterns captured.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Gets what a container or stream probe measured, when the file is on disk.</summary>
    public MediaProbe? Probe { get; init; }
}

/// <summary>What a container or stream probe measured. Measurements, never claims.</summary>
/// <remarks>
/// Every member here arrives as <see cref="EvidenceSource.ContainerProbe"/> or
/// <see cref="EvidenceSource.StreamProbe"/> and therefore overrules anything a release title said, with
/// no per-kind rule. This is the whole of what <c>IgnoreStatedResolutionFor</c> was working around.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MediaProbe
{
    /// <summary>Gets the measured frame width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the measured frame height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Gets the measured frame rate.</summary>
    public double? FrameRate { get; init; }

    /// <summary>Gets whether the video stream is interlaced.</summary>
    public bool? Interlaced { get; init; }

    /// <summary>Gets the codec the container declares.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Gets the audio codec the container declares.</summary>
    public string? AudioCodec { get; init; }

    /// <summary>Gets the audio channel count.</summary>
    public int? AudioChannels { get; init; }

    /// <summary>Gets the measured audio bitrate, in kilobits per second.</summary>
    public int? AudioBitrate { get; init; }

    /// <summary>Gets the audio sample depth, in bits.</summary>
    public int? SampleDepth { get; init; }

    /// <summary>Gets the audio sample rate, in kilohertz.</summary>
    public double? SampleRate { get; init; }

    /// <summary>Gets the transfer characteristics the container declares, e.g. <c>smpte2084</c>.</summary>
    public string? TransferCharacteristics { get; init; }

    /// <summary>Gets the duration the container declares.</summary>
    public TimeSpan? Duration { get; init; }
}
```

The supporting small types:

```csharp
/// <summary>How a release stated its resolution, most specific first (§1.2).</summary>
public enum ResolutionClaimForm
{
    /// <summary>An explicit line count: <c>1080p</c>, <c>540p</c>.</summary>
    LineCount = 0,

    /// <summary>An explicit raster: <c>1920x1080</c>.</summary>
    Raster = 1,

    /// <summary>A marketing name: <c>UHD</c>, <c>[4K]</c>, <c>FHD</c>.</summary>
    MarketingName = 2,

    /// <summary>Inferred from a container or a scene convention.</summary>
    Inferred = 3,
}

/// <summary>How a video stream is scanned.</summary>
public enum ScanType { Progressive = 0, Interlaced = 1, Telecined = 2 }

/// <summary>One language the release states, and whether it states it as the only one.</summary>
/// <param name="Language">The language.</param>
/// <param name="IsDualLanguageMarker">
/// Whether the claim came from a dual/multi-language marker (<c>DL</c>, <c>ML</c>, <c>MULTi</c>) rather
/// than from a language name. The distinction is what <c>ReleaseLanguageScanner.cs:119-132</c> exists for.
/// </param>
public readonly record struct LanguageClaim(Language Language, bool IsDualLanguageMarker);
```

**The honest size of `Read`.** Pass 3 said *"roughly forty lines for video … against 130 declaration
rows"*. Both halves were wrong. The declaration rows are 101 + 7 (§0, L-8), and the replacement is not
forty lines. The ranking cascade genuinely disappears — there is nothing to collapse evidence *to* — but
the **inference** cascade does not; it redistributes:

| Function | Reads | Honest estimate |
|---|---|---|
| `OriginOf` | ~20 source tokens, the `Container` member, the `Languages` axis, `IsRemux`, `Codec` | ~55 lines |
| `GenerationOf` | remux / rip / brrip / mpeg2 / xvid, plus the origin it was just given | ~25 lines |
| `ResolutionOf` | the stated value, its claim form, the scan type, the camera-origin drop | ~25 lines |
| everything else — codec, audio, dynamic range, flaws, packaging, corrections, languages | one to four lines each | ~35 lines |
| **Total** | | **~140 lines**, plus Movies' ~40-line refinement |

That is still a reduction — 140 lines of ordinary C# against 108 declaration rows plus the 132-line
`RungResolver.cs` that interprets them, and the 140 lines are readable in one screen per function rather
than as a cascade whose order is its semantics. But *"forty lines"* was a claim about a file nobody had
written, and it is corrected rather than defended.

---

## 2. The per-family quality types

### 2.1 Video — checking the sketch against real release taxonomy

The brief's sketch: `SourceOrigin`, `Fidelity` with an encode-generation count, `Resolution`, `Revision`.
Here is the taxonomy it has to account for, and what each row actually *is*.

| Community name | What the signal is | Master it descends from | Lossy re-encodes since | Typical bitrate |
|---|---|---|---:|---|
| Remux | UHD/BD video bitstream copied, container changed | HD disc | 0 | 25–100 Mbit/s |
| Bluray / BDRip | disc re-encoded to a target bitrate | HD disc | 1 | 2–12 Mbit/s |
| BRRip (strict sense) | re-encode of an existing rip | HD disc | 2 | 1–5 Mbit/s |
| WEB-DL | a service's stream, remuxed, not re-encoded | streaming service | 0 | 4–8 Mbit/s |
| WEBRip | a service's stream, re-encoded | streaming service | 1 | 3–8 Mbit/s |
| Raw-HD | broadcast transport stream, untouched | broadcast | 0 | ~19 Mbit/s |
| HDTV | broadcast capture, re-encoded | broadcast | 1 | 2–8 Mbit/s |
| DVD | DVD VOB, untouched | SD disc | 0 | ~5 Mbit/s |
| DVDRip | DVD re-encoded | SD disc | 1 | 1–2 Mbit/s |
| DVDSCR / BDSCR | a screener disc, watermarked, re-encoded | SD or HD disc | 1 (+ a flaw) | as its disc |
| TELECINE | a physical film print scanned | film print | 1 | — |
| TELESYNC | a projection re-photographed, with a line audio feed | cinema projection | 1 | — |
| CAM | a projection re-photographed, with room audio | cinema projection | 1 | — |
| WORKPRINT | an unfinished edit | preview | varies | — |

Four findings. The first is the retraction; the rest survive pass 4 unchanged.

* **Retained as an observation. Retracted as a modelling decision — this is D-7.**

  The observation is true and remains worth stating: Remux→Bluray and WEB-DL→WEBRip are both *one lossy
  re-encode*, and the ladder needed two unrelated mechanisms to say it — a separate `Remux-1080p` rung
  inserted above `Bluray-1080p`, and a same-weight tie-group with a `GroupName` for WEB.

  What pass 3 then did with the observation was wrong, and provably so. It is true that the *step* is the
  same. It is false that the *preference relationship* is the same, and preference is the only thing a
  quality model is for. Read the bitrate column: the disc step is a ~5× cliff and the web step is a few
  percent. `WEBDL ≈ WEBRip` and `Bluray < Remux` are therefore both correct, and **no per-axis policy can
  hold both**:

  > For each pair, `Resolution` and `Origin` are equal within the pair, so only `Generation` can decide
  > either. Every control an ordered axis has — presence in `Precedence`, `PreferRicher`, `Ceiling`,
  > `Floor`, `Ranking`, `WhenUnknown` — is a function of the generation value **alone**; none can read
  > `Origin`. So the induced order on `Generation` is identical for HD-disc points and stream points, and
  > `{gen 0 ≻ gen 1 | Origin = HDDisc}` ∧ `{gen 0 ~ gen 1 | Origin = Stream}` is unrepresentable. ∎

  Pass 3's §5.1 chose the strict reading and lost the equivalence in silence, turning
  `UpgradeDecisionTests.cs:63` — currently green — red, and re-downloading a WEB-DL over a held WEBRip of
  the same resolution: exactly the churn `MoviesLadder.cs:15-17` says the shared weight exists to prevent.

  **D-7 is the fix, and the observation survives in a sharper form: the step is the same; the cliff is
  not.** The cliff belongs to `Origin`, which is the axis that already means "which master". So:

  > **An `Origin` member exists for the untouched master wherever holding the master is a different
  > bitrate class from holding an encode of it — and not otherwise.**

  The threshold is not invented for this document. §4's `MasterFactor` table already carries it, and
  already collapses to a single rip factor of 0.9 at generation ≥ 1, i.e. **§4 already knew that "which
  master" only matters when you hold the master**:

  | Master | Factor at gen 0 | Factor at gen ≥ 1 | Ratio | Own `Origin` member? |
  |---|---:|---:|---:|---|
  | High-definition disc | 5.0 | 0.9 | **5.6×** | **yes** — `HighDefinitionDiscBitstream` (Remux) |
  | Standard-definition disc | 2.5 | 0.9 | **2.8×** | **yes** — `StandardDefinitionDiscBitstream` (DVD) |
  | Broadcast | 1.5 | 0.9 | **1.7×** | **yes** — `BroadcastBitstream` (Raw-HD) |
  | Stream | 1.0 | 0.9 | **1.11×** | **no** — one member; `Generation` carries the step |

  The rule is a single threshold at 1.5×, and the streaming row is the only one below it. That the model's
  own independently-derived numbers separate exactly the pair the community treats as equivalent from the
  three it treats as distinct is the strongest confirmation in this document, and it is a confirmation
  pass 3 could have had and did not look for.

  **What this costs.** `Origin` and `Generation` are no longer independent: a bitstream member forces
  `Generation = 0`, a rip member forces `Generation ≥ 1`, and `Stream` alone carries both. That is
  redundancy, and it is stated rather than hidden. It buys the two behaviours the ladder ships and pass 3
  could not, with **zero new policy mechanism** — no cross-axis weight, no per-step significance, no
  second comparison rule. The per-axis profile editor survives intact. And the ladder's separate `Remux`
  rung is revealed as having encoded this all along; it was never an inserted special case, it was an
  origin member with nowhere to live.

* **Corrected (unchanged from pass 3).** `Generation` alone cannot order the table: WEB-DL and Remux are
  both zero and are not equal, because the masters they descend from differ in bitrate by roughly an
  order of magnitude. So the sketch's `Fidelity` splits into `Origin` and `Generation`.
* **Corrected (unchanged from pass 3).** CAM and TELESYNC are the same *video* — a re-photographed
  projection — and differ only in the audio feed. The ladder needs two rungs (3 and 4) to say that; the
  axis model needs none, because the difference already lives on the audio axis. Confirmation that the
  decomposition is right: it makes a distinction the ladder had to hard-code fall out of a dimension that
  had to exist anyway.
* **New in pass 4: language is quality-bearing, and the tree already says so.** `MoviesParsing.cs:172-173`'s
  `german-remux` guard exists specifically because a German dual-language disc encode *is* a remux, and
  `ReleaseLanguageScanner.cs:119-132`'s DL/ML rule is described in its own comment as the one genuinely
  non-obvious rule. Pass 3 declared no language axis and evicted only indexer flags, which left "prefer a
  German dub", "refuse a dub" and "require original audio" reachable only through
  `EvidenceMatch.TitleContains("German")` — the residue category the document itself defines as the set of
  things nobody modelled. `Languages` is declared below as a nominal set-valued axis. It is not a
  convenience: it is what lets `german-remux` stop being a per-kind guard string (§1.4a).

**`Generation`, restated.** Its meaning changes with D-7 and the new sentence is:

> **`Generation` counts lossy re-encodes since the signal `Origin` names.** It no longer carries the
> master/rip *cliff* — `Origin` does. What is left to it is the steps that are not cliffs: WEB-DL (0) →
> WEBRip (1), and BDRip (1) → BRRip (2).

Its polarity is unchanged (`Descending`; more re-encodes is strictly less retained information), and its
role in music and written copies is unchanged. In the shipped video default it is deliberately near-
dormant — §5.1 ties {0, 1} with a ceiling, so it fires only for a second-generation rip — and that is the
point: D-7 moved its work to `Origin`, and a user who wants WEB-DL ranked over WEBRip removes the ceiling,
which is one chip.

**The declared axes.**

```csharp
namespace Arronix.Abstractions.Quality.Families;

/// <summary>The quality of one video file.</summary>
public sealed class VideoQuality : IQualityFacts
{
    /// <summary>The master signal the file carries.</summary>
    [Axis]
    [Display(Name = "Origin", Description = "What the release was made from, and whether it carries it whole.")]
    public Evidence<VideoOrigin> Origin { get; init; }

    /// <summary>How many lossy re-encodes since the signal <see cref="Origin"/> names.</summary>
    [Axis(Ordering = AxisOrdering.Descending, Unit = "re-encodes")]
    [Display(Name = "Generation", Description = "How many times it has been re-encoded since.")]
    public Evidence<int> Generation { get; init; }

    /// <summary>Vertical resolution.</summary>
    [Axis(Unit = "lines")]
    [Display(Name = "Resolution", Description = "Lines of vertical resolution.")]
    public Evidence<int> Resolution { get; init; }

    /// <summary>The dynamic-range formats the release carries.</summary>
    /// <remarks>
    /// <b>Set-valued, changed in pass 4.</b> A release may genuinely carry two at once — a Dolby Vision
    /// layer over an HDR10+ base is real and increasingly common (corpus q088), and a single-valued
    /// <c>Evidence&lt;DynamicRange&gt;</c> had no member for it and no way to grow one that was not a
    /// combinatorial enum. Set-valued makes it <c>Nominal</c>, so it leaves the precedence list; §3.7's
    /// facet tier is where it lands, which is D-8's answer and is where the community's own preference
    /// for it actually lives.
    /// </remarks>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Dynamic range")]
    public EvidenceSet<DynamicRange> DynamicRange { get; init; }

    /// <summary>The languages the release carries.</summary>
    /// <remarks>
    /// Nominal and set-valued: "German plus original" is two members, not a richer single value, and no
    /// language is better than another. Usable in requirements ("require original audio", "refuse a
    /// dub"), in a facet score ("prefer MULTi"), and — as evidence, not as preference — by
    /// <c>Read</c>, where a dual-language marker beside a disc source is what makes a German ML/DL
    /// encode a remux.
    /// </remarks>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Languages")]
    public EvidenceSet<Language> Languages { get; init; }

    /// <summary>The audio presentation.</summary>
    [Axis]
    [Display(Name = "Audio")]
    public Evidence<AudioPresentation> Audio { get; init; }

    /// <summary>The video codec, ordered by compression efficiency.</summary>
    [Axis]
    [Display(Name = "Codec", Description = "Ordered by efficiency, which is not the same as quality.")]
    public Evidence<VideoCodec> Codec { get; init; }

    /// <summary>Frames per second.</summary>
    [Axis(Unit = "fps")]
    [Display(Name = "Frame rate")]
    public Evidence<double> FrameRate { get; init; }

    /// <summary>How the release is packaged.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Packaging", Description = "A single file, a disc image, or a disc folder.")]
    public Evidence<Packaging> Packaging { get; init; }

    /// <summary>Which service a stream capture came from.</summary>
    /// <remarks>Wrapped, not <c>Evidence&lt;string&gt;</c> — see the note below the type.</remarks>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Distributor")]
    public Evidence<Distributor> Distributor { get; init; }

    /// <summary>Defects the release carries.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Flaws")]
    public EvidenceSet<VideoFlaw> Flaws { get; init; }

    /// <summary>How many corrections the release has been re-issued for. A first issue is zero.</summary>
    /// <remarks>
    /// <b>Base-0, fixed in pass 4, and <c>Read</c> subtracts.</b> <c>ReleaseTagScanner.ScanRevision</c>
    /// returns <c>version = 1</c> for a release that states nothing, 2 for PROPER and 3 for REPACK2, and
    /// <c>ReleaseEvidence.Version</c> documents that. Pass 3 declared this axis as a base-0 <i>count</i>,
    /// assumed 0 for an absent reading, and then rendered <c>Proper</c> at <c>&gt; 1</c> — three parts of
    /// one document disagreeing about the same number, with the visible consequence that an unread
    /// release ranked below a release explicitly stating no correction. One reading is chosen:
    /// <b><c>Corrections = Version - 1</c></b>, so a first issue is 0, a PROPER is 1, a REPACK2 is 2, the
    /// §5.1 assumption of 0 is correct, and §7 renders <c>Proper</c> at <c>&gt; 0</c>.
    /// </remarks>
    [Axis(Unit = "corrections")]
    [Display(Name = "Corrections", Description = "PROPER: the previous issue was a worse encode.")]
    public Evidence<int> Corrections { get; init; }

    /// <summary>How many times the release has been re-issued because the previous issue was the wrong content.</summary>
    [Axis(Unit = "corrections")]
    [Display(Name = "Mislabel fixes", Description = "REAL: the previous issue was the wrong content.")]
    public Evidence<int> Mislabels { get; init; }

    /// <summary>Whether the issue is a repack of the same encode.</summary>
    /// <remarks>
    /// <c>Evidence&lt;Repackaging&gt;</c>, not <c>Evidence&lt;bool&gt;</c>. <c>bool</c> is not an enum
    /// and §1.3's derivation table makes it <c>ARXQ001</c>; pass 3 caught the identical problem for
    /// <c>Distributor</c> and missed it here.
    /// </remarks>
    [Axis(Ordering = AxisOrdering.Unordered)]
    [Display(Name = "Repack", Description = "The same encode, packaged again.")]
    public Evidence<Repackaging> Repacked { get; init; }
}
```

> **Two wrappers, both forced by §1.3's derivation table rather than by taste.** `Evidence<string>` for
> `Distributor` breaks the `TValue : struct` constraint, so it is `Evidence<Distributor>` over a small
> `readonly record struct Distributor(string Token)`, which keeps the open vocabulary and the constraint.
> `Evidence<bool>` for `Repacked` is an outright `ARXQ001`, so it is `Evidence<Repackaging>` over
> `enum Repackaging { Original = 0, Repacked = 1 }`. Both are noted rather than silently applied, because
> they are the two places the framework's uniform shape needed help.

The closed axes:

```csharp
/// <summary>The master signal a video file carries, in ascending order of what that master retains.</summary>
/// <remarks>
/// <para>
/// A member names <i>which</i> master and <i>whether the file carries that master's own bitstream</i>.
/// The second half is D-7: a member exists for the untouched master exactly where holding the master is
/// a different bitrate class from holding an encode of it, which §2.1's table measures at 5.6×, 2.8× and
/// 1.7× for the three disc-and-broadcast masters and 1.11× for a streaming service. Streaming is
/// therefore the one master with a single member, and the WEB-DL/WEBRip step lives on
/// <c>Generation</c> — which is precisely the equivalence the community asserts and the ladder encodes
/// with a shared weight.
/// </para>
/// <para>
/// The consequence for <c>Generation</c> is stated in §2.1 and is not hidden: a <c>…Bitstream</c> member
/// forces generation 0 and a rip member forces generation ≥ 1. The two axes overlap on purpose, and the
/// alternative — a per-step significance weight readable across axes — is the scalar weight this whole
/// model exists to delete.
/// </para>
/// </remarks>
public enum VideoOrigin
{
    /// <summary>A projection re-photographed with a camera.</summary>
    CameraCapture = 0,

    /// <summary>An unfinished edit.</summary>
    Workprint = 1,

    /// <summary>A physical film print, scanned.</summary>
    FilmPrint = 2,

    /// <summary>An over-the-air, cable or satellite transmission, re-encoded. HDTV, SDTV.</summary>
    Broadcast = 3,

    /// <summary>The broadcast transport stream itself, untouched. Raw-HD.</summary>
    BroadcastBitstream = 4,

    /// <summary>A commercial streaming service's transmission. WEB-DL and WEBRip alike.</summary>
    Stream = 5,

    /// <summary>A DVD, re-encoded. DVDRip.</summary>
    StandardDefinitionDisc = 6,

    /// <summary>The DVD's own program stream, untouched. DVD.</summary>
    StandardDefinitionDiscBitstream = 7,

    /// <summary>A Blu-ray or UHD Blu-ray, re-encoded. Bluray, BDRip, BRRip.</summary>
    HighDefinitionDisc = 8,

    /// <summary>The disc's own video bitstream, copied. Remux.</summary>
    HighDefinitionDiscBitstream = 9,
}

/// <summary>The dynamic-range format, in ascending order of range carried.</summary>
/// <remarks>
/// Read as a set (<c>EvidenceSet&lt;DynamicRange&gt;</c>), so a Dolby Vision layer over an HDR10+ base is
/// two members rather than a missing enum value. The declared order is retained because the facet tier
/// scores members and a person reading the list expects them ordered; it is not a precedence order, and
/// <c>ARXQ005</c> makes sure it cannot become one.
/// </remarks>
public enum DynamicRange
{
    StandardDynamicRange = 0,
    HybridLogGamma = 1,
    HighDynamicRange10 = 2,
    HighDynamicRange10Plus = 3,
    DolbyVisionWithFallback = 4,
    DolbyVision = 5,
}

/// <summary>The audio presentation, in ascending order of what survives to the speakers.</summary>
public enum AudioPresentation
{
    RoomCapture = 0,
    LossyStereo = 1,
    LossySurround = 2,
    LossyObject = 3,
    Lossless = 4,
    LosslessObject = 5,
}

/// <summary>The video codec, in ascending order of compression efficiency.</summary>
/// <remarks>
/// Efficiency, not fidelity: at an equal <i>quality target</i> a more efficient codec produces a smaller
/// file of the same quality, not a better one. The default policy therefore does not order on this axis;
/// it exists because it feeds the size model (§4) and because compatibility is a real user preference.
/// </remarks>
public enum VideoCodec { Mpeg2 = 0, Mpeg4Part2 = 1, Vc1 = 2, H264 = 3, Vp9 = 4, H265 = 5, Av1 = 6, H266 = 7 }

/// <summary>Whether an issue is a repack of the same encode.</summary>
/// <remarks>An enum rather than a <c>bool</c>, because §1.3's table admits no <c>Evidence&lt;bool&gt;</c>.</remarks>
public enum Repackaging { Original = 0, Repacked = 1 }

/// <summary>How a release is packaged. Nominal: none of these is better than another.</summary>
public enum Packaging { SingleFile = 0, DiscImage = 1, DiscFolder = 2 }

/// <summary>Defects a video release may carry.</summary>
public enum VideoFlaw
{
    Upscaled = 0,
    Interlaced = 1,
    Watermarked = 2,
    HardcodedSubtitles = 3,
    AdBreaks = 4,
    NetworkLogo = 5,
    Cropped = 6,
    Sample = 7,
}
```

**Resolution table — contested calls on the video axes.**

| Call | Options | Resolution |
|---|---|---|
| Is `Broadcast` below `Stream`? | A good 1080i ATSC broadcast carries ~19 Mbps; a 1080p stream carries ~5–8 Mbps, so by bitrate broadcast wins. But broadcast is interlaced, logo-burned and ad-cut. | **Both broadcast members below `Stream` in the declared order**, with the bitrate advantage recovered by the `Interlaced`/`NetworkLogo`/`AdBreaks` flaws being on the *broadcast* side. This is the one place in `VideoOrigin` that is a judgement rather than a physical fact, and it is exactly why axis member order is policy-overridable (§3.2). Note this also settles `Raw-HD` at 4, i.e. **below** `Stream` — which is Tv's answer, not Movies' (`TvShape.cs` ranks `Raw-HD` at 11, below `WEBRip-1080p`; `MoviesLadder.cs` ranks it 30, at the top). §9 carries it to the owner. |
| Does every untouched master get its own `Origin` member? | Symmetry says yes; minimality says only the disc pair breaks a shipped test. | **Yes, by the 1.5× rule in §2.1, which yields three.** Adding only `HighDefinitionDiscBitstream` would satisfy D-7's letter and leave `Raw-HD` tied with `HDTV-1080p` and `DVD` tied with `DVDRip` under §5.1's generation ceiling — a shipped regression against `MoviesLadder.cs`, traded for one enum member. The rule is applied where it holds rather than where it is convenient, and §7's label table then *joins* the SD-disc pair back to the single word `DVD`, because the community has one word and the ladder has one rung. **The axis model splits where physics splits; the label table joins where the community joins.** |
| Is Dolby Vision above HDR10+? | Both carry dynamic metadata; DV is the richer container, but a DV profile-5 file on a non-DV display looks washed out, and a meaningful minority actively refuse DV. | **Neither, after pass 4.** `DynamicRange` is set-valued and therefore nominal, so it cannot appear in precedence at all (`ARXQ005`). The shipped default scores `HighDynamicRange10Plus`, `DolbyVisionWithFallback` and `DolbyVision` **equally** in the facet tier (§3.7, §5.1), which delivers what pass 3's tied `Ranking` delivered — the platform does not chase a re-download for a difference the user's display may not render — and additionally stops the axis from outranking a correction. |
| Where does `Languages` sit? | It is quality-bearing (the German remux rule) but no language is better than another. | **Nominal, set-valued, absent from every shipped precedence.** Usable in requirements and facet scores, and read by `Read` as evidence for `Origin`. Declaring it is what removes `german-remux` from the per-kind guard set (§1.4a). |
| Is `Codec` a quality axis at all? | It is not a fidelity axis. But it is the second-most-written Custom Format upstream (x265-avoiders and AV1-avoiders both exist). | **Declared as an axis, absent from the default precedence.** Ordering on it makes a library chase re-encodes, which is a downgrade dressed as an upgrade. Available for anyone who wants it; used by the size model regardless. |
| Is `1080i` a resolution or a flaw? | 1080i is 1920×1080 with half the temporal resolution. | **`Resolution = 1080` plus `VideoFlaw.Interlaced`.** The raster is genuinely 1080; what the viewer sees is combing or a halved frame rate, which is the class of thing the flaw axis names. **Deferred:** if this proves wrong, a `Scan` axis is added and nothing else changes — which is itself the argument for the framework. |
| Where does BR-DISK go? | The ladder puts it at rung 29 of 30, near the top, while its own comment says most users do not want it. | **`Packaging = DiscImage`/`DiscFolder`, a nominal axis, refused by the default policy.** L-6, fixed exactly where the ladder file diagnosed it and could not act. |
| Where does a screener go? | The ladder gives DVDSCR its own rung below SDTV. | **Its actual origin (`StandardDefinitionDisc` or `HighDefinitionDisc`) plus `VideoFlaw.Watermarked`.** A BD screener really is a high-fidelity signal with a burn-in, and saying so is more useful than pretending it is worse than a broadcast capture. |

### 2.2 Audio — music

```csharp
/// <summary>The quality of one music file.</summary>
public sealed class AudioQuality : IQualityFacts
{
    /// <summary>What the release was made from.</summary>
    [Axis] public Evidence<AudioOrigin> Origin { get; init; }

    /// <summary>How many lossy encodes since the origin master. Zero for a lossless copy.</summary>
    [Axis(Ordering = AxisOrdering.Descending, Unit = "encodes")]
    public Evidence<int> Generation { get; init; }

    /// <summary>The codec, lossy members below lossless ones.</summary>
    [Axis] public Evidence<AudioCodec> Codec { get; init; }

    /// <summary>The lossy encoder's target bitrate. Absent for a lossless copy.</summary>
    [Axis(Unit = "kbps")] public Evidence<int> Bitrate { get; init; }

    /// <summary>Sample depth.</summary>
    [Axis(Unit = "bits")] public Evidence<int> SampleDepth { get; init; }

    /// <summary>Sample rate.</summary>
    [Axis(Unit = "kHz")] public Evidence<double> SampleRate { get; init; }

    /// <summary>Defects the release carries.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)] public EvidenceSet<AudioFlaw> Flaws { get; init; }
}

public enum AudioOrigin { Broadcast = 0, Vinyl = 1, WebStore = 2, CompactDisc = 3, SuperAudioCd = 4 }
public enum AudioCodec { Wma = 0, Mp3 = 1, Vorbis = 2, Aac = 3, Opus = 4, Alac = 5, Flac = 6, Pcm = 7 }
public enum AudioFlaw { Transcoded = 0, Clipped = 1, GaplessBroken = 2, WrongTrackLengths = 3 }
```

Two modelling choices worth stating.

* **`Bitrate` is defined as "the lossy encoder's target", so a lossless file has no reading at all.** A
  FLAC does have a bitrate (~1000 kbps) and reporting it would make a floor of "at least 256 kbps"
  accidentally true for the right reason and accidentally false for a 6 kHz-limited transcode. Defining
  the axis by what it measures rather than by what a decoder reports keeps the floor meaningful, and the
  policy's `UnknownEvidence.Ignore` (§3.2) is what lets one floor cover both cases.
* **`Generation` is the same axis as video's.** A lossless copy is 0, a lossy encode of a lossless master
  is 1, a lossy encode of a lossy file — the cardinal sin in music — is 2. It also means the label
  renderer and the size model can share code.

  **Pass 3 offered the recurrence as *"evidence the vocabulary is at the right altitude"*; that inference
  is withdrawn.** The axis recurs as a **word**, not as **evidence**. For video it is read from
  `Remux`/`Rip` tokens that releases actually carry; for music and written copies nothing in a release
  title states it, so at grab time it is absent for every candidate and the reading only becomes available
  after a probe or a conversion audit. A vocabulary that transfers while its evidence does not is at best
  neutral about altitude, and §5.3's consequence — a precedence list every one of whose axes is unreadable
  before acquisition — is what that costs. Recorded honestly rather than counted as a win.

### 2.3 Books — the split, enforced in one place

Books declares **two families**, so it gets **two quality types**. A signature that holds both *does*
exist — `QualityPolicy.Compare(QualityPoint, QualityPoint)` compiles for any two points and throws for a
foreign one — so the split is enforced by one runtime guard rather than by the type system. §1.5 argument
2 carries the full retraction of pass 3's "structurally unrepresentable" claim and the reason the generic
alternative is not taken.

```csharp
/// <summary>The quality of one written copy.</summary>
public sealed class WrittenQuality : IQualityFacts
{
    /// <summary>How the text is carried: the only genuine fidelity axis a written copy has.</summary>
    /// <remarks>
    /// <b>Not readable before acquisition.</b> A <c>.pdf</c> may be page images or fixed-layout text and
    /// an <c>.epub</c> may contain scans; determining which needs the file open. §5.3 therefore states
    /// what pass 3 did not: written precedence is <i>post-import only</i>, and at grab time the readable
    /// axis is <see cref="Format"/>.
    /// </remarks>
    [Axis] public Evidence<TextForm> Form { get; init; }

    /// <summary>The container format, in a declared order that is explicitly a default to be re-ranked.</summary>
    /// <remarks>
    /// <b>Ordinal in pass 4, not nominal.</b> Pass 3 made it nominal to kill the inherited
    /// <c>PDF &lt; MOBI &lt; AZW3 &lt; EPUB</c> ladder, and then the ladder came back through the side
    /// door: a nominal axis cannot appear in precedence (§1.6), so every PDF's unknown <see cref="Form"/>
    /// fell to <c>UnknownEvidence.Lowest</c> and ranked below every EPUB whose form <i>was</i> readable —
    /// <c>PDF &lt; EPUB</c> restored by inference rather than by data, and now unarguable, because a user
    /// cannot re-rank a nominal axis. Ordinal with a re-rankable declared order is the honest answer to
    /// §2.3's own critique: the family states a default, <c>AxisPreference.Ranking</c> exists precisely to
    /// overrule it, and the technical-book reader moves one chip.
    /// </remarks>
    [Axis] public Evidence<BookFormat> Format { get; init; }

    /// <summary>How many conversions since the publisher's file.</summary>
    [Axis(Ordering = AxisOrdering.Descending, Unit = "conversions")]
    public Evidence<int> Generation { get; init; }

    /// <summary>Defects the copy carries.</summary>
    [Axis(Ordering = AxisOrdering.Unordered)] public EvidenceSet<WrittenFlaw> Flaws { get; init; }
}

/// <summary>How a written copy carries its text, in ascending order of what a reader can do with it.</summary>
public enum TextForm
{
    /// <summary>Scanned pages. The text is not text.</summary>
    PageImages = 0,

    /// <summary>Text at a fixed page geometry.</summary>
    FixedLayout = 1,

    /// <summary>Text that reflows to the reader's device and font size.</summary>
    ReflowableText = 2,
}

/// <summary>Container formats, in a declared order that is a stated default and nothing more.</summary>
/// <remarks>
/// Ascending by how much of the text survives a device change, which is the only defensible ordering
/// principle available before the file is open. It is the platform's opinion, it is one drag from being
/// something else, and §5.3 says so in the profile prose.
/// </remarks>
public enum BookFormat { Djvu = 0, Cbz = 1, Pdf = 2, Mobi = 3, Azw3 = 4, Epub = 5 }
public enum WrittenFlaw { Drm = 0, Ocr = 1, NoTableOfContents = 2, MissingImages = 3, Watermarked = 4 }

/// <summary>The quality of one spoken copy.</summary>
public sealed class SpokenQuality : IQualityFacts
{
    [Axis(Ordering = AxisOrdering.Descending, Unit = "encodes")] public Evidence<int> Generation { get; init; }
    [Axis] public Evidence<AudioCodec> Codec { get; init; }

    /// <summary>The container, which for audiobooks is what listeners actually select on.</summary>
    /// <remarks>
    /// New in pass 4. <c>BooksShape.cs:608-611</c>'s audiobook ladder is <c>MP3 &lt; AAC &lt; M4B &lt;
    /// FLAC</c>, and <c>M4B</c> is not a codec — it is AAC in an MP4 container with chapter marks, and
    /// the chapter marks are the whole reason anyone prefers it. Pass 3 declared no container axis for
    /// spoken copies and then had §7 render <c>M4B 128kbps</c>, which nothing in the model could produce.
    /// Ordinal, because chaptered beats unchaptered for every listener, unlike a written container.
    /// </remarks>
    [Axis] public Evidence<SpokenContainer> Container { get; init; }

    [Axis(Unit = "kbps")] public Evidence<int> Bitrate { get; init; }
    [Axis] public Evidence<SpokenOrigin> Origin { get; init; }
    [Axis(Ordering = AxisOrdering.Unordered)] public EvidenceSet<SpokenFlaw> Flaws { get; init; }
}

/// <summary>An audiobook container, ascending by what it carries beyond the audio.</summary>
public enum SpokenContainer { Mp3 = 0, Ogg = 1, M4a = 2, M4b = 3 }

public enum SpokenOrigin { Homemade = 0, Broadcast = 1, Retail = 2 }
public enum SpokenFlaw { Abridged = 0, Transcoded = 1, Chapterless = 2, SingleFile = 3, Dramatized = 4 }
```

**Resolution table — the ebook format ladder.**

| Call | Options | Resolution |
|---|---|---|
| Is `PDF < MOBI < AZW3 < EPUB` a fidelity order? | It is `BooksShape.cs:585-588` today, carried from Readarr. | **No — it is one user's device preference stated as a fact**, and that diagnosis stands. What pass 3 did with it does not: making `Format` nominal removed the *only* axis a written copy states before it is downloaded, so both precedence axes were absent at grab time, `Compare` returned `Same` for every pair, and the unknown-`Form` fallback re-created `PDF < EPUB` by inference where nobody could re-rank it. **Pass 4 keeps the diagnosis and reverses the remedy:** `Format` is ordinal, its order is declared as a *stated default*, `Ranking` re-orders it, and `Describe()` says out loud that it is the platform's opinion. That is what §3.2's re-ranking mechanism was built for, and using it here is more honest than removing the order and letting an inference supply one. |
| Is `Abridged` a flaw or a different work? | An abridged recording is different content under the same title. | **A flaw, refused by the shipped default.** Modelling it as a different work would need a catalog change; modelling it as a refused flaw gets the right behaviour today and is honest about what it is. Recorded in the deferred list. |

---

## 3. Policy — the user-owned half

### 3.1 The model

```csharp
/// <summary>One user's stated preference over one format family's axes.</summary>
/// <remarks>
/// <para>
/// Compiled against exactly one <see cref="IQualityType"/>. A point of another family is rejected at
/// runtime by <see cref="Compare"/> and <see cref="Admits"/>, <b>not</b> at compile time — see §1.5,
/// argument 2, where pass 3's "structurally unrepresentable" claim is withdrawn. One guard, in one place,
/// with one test on it.
/// </para>
/// <para>
/// The three sections are three different questions and are deliberately not merged into one score. What
/// to <i>prefer</i> orders candidates that are all acceptable; what to <i>refuse</i> decides
/// acceptability; what is <i>good enough</i> decides when to stop. The surveyed application merges all
/// three into one number and needs a magic <c>-10000</c> to make refusal work, which is what a merged
/// score costs.
/// </para>
/// <para>
/// <b>Four sections after D-8, not three.</b> <see cref="Facets"/> is a bounded additive score consulted
/// <i>only</i> when <see cref="Precedence"/> has returned <c>Same</c>. It is a fourth question — "among
/// candidates I have no ordered reason to separate, which do I like?" — and confining it beneath the
/// core is what stops it from becoming the merged score again. §3.7.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class QualityPolicy
{
    /// <summary>Builds a policy against a family's quality type.</summary>
    /// <param name="type">The family's quality type.</param>
    /// <param name="configure">The declaration.</param>
    /// <returns>The compiled policy.</returns>
    /// <exception cref="ArgumentException">
    /// The declaration names an axis the family does not declare; or places a <c>Nominal</c> axis in
    /// <see cref="Precedence"/> (<c>ARXQ005</c>); or gives one axis both a precedence entry and a facet
    /// score (<c>ARXQ006</c>); or declares more than <see cref="FacetScoring.MaximumFacets"/> facets or a
    /// facet point outside ±<see cref="FacetScoring.PointsPerFacet"/>.
    /// </exception>
    /// <remarks>
    /// The analyzer covers policies written in source; this covers policies a <b>user</b> composed in the
    /// editor, which the analyzer never sees. Both are needed, and the second is the one the cycle-safety
    /// argument depends on — a family's shipped default is not where a bad policy comes from.
    /// </remarks>
    public static QualityPolicy For(IQualityType type, Action<IQualityPolicyBuilder> configure);

    /// <summary>Gets the family this policy is over.</summary>
    public IQualityType Type { get; }

    /// <summary>Gets the axes that order candidates, most significant first. The core tier.</summary>
    /// <remarks>
    /// <para>
    /// An axis absent from this list never orders anything, though it may still refuse (via
    /// <see cref="Requirements"/>) or score (via <see cref="Facets"/>).
    /// </para>
    /// <para>
    /// Every entry is a <see cref="AxisPreference"/>, which by construction induces a <b>total
    /// preorder</b> on points: unknown maps to a fixed element under either surviving
    /// <c>WhenUnknown</c> mode, and <c>Ceiling</c>, <c>Floor</c>, <c>Ranking</c> and
    /// <c>PreferRicher</c> are monotone or order-reversing maps that preserve totality. The
    /// lexicographic composition of finitely many total preorders is a total preorder, so the core tier
    /// is transitive and cannot cycle. §3.1's closing proof states this in full; it is the guarantee the
    /// ladder had for free and pass 3 lost without noticing.
    /// </para>
    /// <para>
    /// <b>Absence is no longer the equivalence mechanism it was advertised as.</b> Pass 3 said dropping
    /// <c>Generation</c> from this list is how <c>WEBDL ≈ WEBRip</c> is expressed. It is one way, and it
    /// is unusable, because the same drop also ties <c>Bluray</c> with <c>Remux</c>. After D-7 the
    /// equivalence is a <c>Ceiling</c> on <c>Generation</c> and the disc distinction is an
    /// <c>Origin</c> member — two independent controls for two independent facts. §2.1.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AxisPreference> Precedence { get; }

    /// <summary>Gets what makes a candidate ineligible regardless of how it ranks.</summary>
    public IReadOnlyList<AxisRequirement> Requirements { get; }

    /// <summary>Gets the bounded scores consulted only when <see cref="Precedence"/> returns <c>Same</c>.</summary>
    /// <remarks>D-8. §3.7. <see cref="FacetScoring.None"/> when the policy declares none.</remarks>
    public FacetScoring Facets { get; }

    /// <summary>Gets when a held file is good enough to stop looking.</summary>
    public CutoffPredicate Cutoff { get; }

    /// <summary>Ranks two points of this family.</summary>
    /// <param name="held">The point already held.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The judgement, naming the axis that decided.</returns>
    /// <exception cref="ArgumentException">Either point belongs to another family.</exception>
    /// <remarks>
    /// A pure ordering and nothing else. It knows nothing about downloads, and in particular it does
    /// <b>not</b> carry the provenance rule (§3.2) — that is a rule about whether to <i>act</i>, it is
    /// pairwise rather than pointwise, and putting it here would destroy the transitivity the whole tier
    /// rests on. It lives in <see cref="Decide"/>.
    /// </remarks>
    public QualityJudgement Compare(QualityPoint held, QualityPoint candidate);

    /// <summary>Decides whether a candidate is eligible at all.</summary>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The verdict, carrying the requirement that refused it.</returns>
    public Eligibility Admits(QualityPoint candidate);

    /// <summary>Decides whether a held file is good enough to stop looking for upgrades.</summary>
    /// <param name="held">The point already held.</param>
    /// <returns><see langword="true"/> when every cutoff floor is met.</returns>
    public bool IsGoodEnough(QualityPoint held);

    /// <summary>Decides whether to take a candidate, and says why in one sentence.</summary>
    /// <param name="held">The point already held, or null when nothing is.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The decision and its reason.</returns>
    public GrabDecision Decide(QualityPoint? held, QualityPoint candidate);

    /// <summary>Renders the policy as English a person can read.</summary>
    /// <returns>The sentence. See §3.5.</returns>
    public string Describe();
}

/// <summary>How two points of one family relate under one policy.</summary>
public enum QualityJudgement
{
    /// <summary>The candidate is below the held file on the first axis that decided.</summary>
    Worse,

    /// <summary>No axis in the precedence list decided, and no facet score separated them either.</summary>
    Same,

    /// <summary>The candidate is above the held file on the first axis that decided.</summary>
    Better,

    /// <summary>
    /// Reserved. No <see cref="AxisPreference"/> can produce this after pass 4 — <c>Refuse</c> is not a
    /// preference mode any more (§3.2) — and it survives only for a bespoke family that declares an axis
    /// whose comparison genuinely partial. <see cref="QualityPolicy.Decide"/> maps it to
    /// <c>EvidenceInsufficient</c>, never to a grab.
    /// </summary>
    Incomparable,
}

/// <summary>Why a candidate is or is not eligible.</summary>
public readonly record struct Eligibility(bool IsAdmitted, AxisRequirement? RefusedBy);

/// <summary>What to do with a candidate, and why.</summary>
public readonly record struct GrabDecision(GrabVerdict Verdict, string Reason);

/// <summary>What to do with a candidate.</summary>
public enum GrabVerdict
{
    Grab,
    Refused,
    NotAnUpgrade,
    AlreadyGoodEnough,
    EvidenceInsufficient,
}
```

**The guarantee this tier owes, and pass 3 did not give: `Compare` cannot cycle.**

A comparison that admits a strict preference cycle is not a corner case, it is an unbounded download loop:
`Decide` grabs whenever `Compare` is `Better`, so `A ≻ C ≻ B ≻ A` is held A → grab C → grab B → grab A,
forever, each iteration a real download and a real import. Pass 3 shipped exactly that, because
`UnknownEvidence.Ignore` in a precedence entry makes comparison *lexicographic with skipping*, which is
not transitive. The counterexample is three points and three axes, all `Ignore`:

| | X | Y | Z |
|---|---|---|---|
| A | ∅ | 0 | 2 |
| B | ∅ | 1 | 0 |
| C | 0 | ∅ | 1 |

- **A vs B** — X skipped (both ∅) → Y decides, 0 < 1 → **B ≻ A**
- **B vs C** — X skipped (B ∅) → Y skipped (C ∅) → Z decides, 0 < 1 → **C ≻ B**
- **C vs A** — X skipped (A ∅) → Y skipped (C ∅) → Z decides, 1 < 2 → **A ≻ C**

`A ≻ C ≻ B ≻ A`. Nothing is contrived: `Origin`, `DynamicRange` and `Audio` are absent from real release
titles in different combinations for different releases, and a "small files, don't guess" user setting all
three to `Ignore` is the persona the mode exists for.

**The fix and the proof.** §3.2 removes `Ignore` and `Refuse` from `AxisPreference` structurally, leaving
`Lowest` and `Assume`. Then:

> **Claim.** `QualityPolicy.Compare` induces a total preorder on the points of its family, hence is
> transitive and acyclic.
>
> **Proof.** Take any entry *p* ∈ `Precedence`, over axis *a*. Both surviving `WhenUnknown` modes map an
> absent reading to a **fixed** element of *a*'s value order — the bottom for `Lowest`, a stated value for
> `Assume` — so *p* is a function from points to a totally ordered set, and therefore induces a total
> preorder ⪯ₚ. `PreferRicher = false` composes with order reversal; `Ceiling` and `Floor` are the clamp
> maps *x* ↦ min(*x*, *c*) and *x* ↦ max(*x*, *f*); `Ranking` is a monotone quotient onto a totally
> ordered set of groups. Each is monotone or order-reversing into a total order, so ⪯ₚ stays total.
> `Compare` is the lexicographic composition of finitely many ⪯ₚ, and the lexicographic composition of
> finitely many total preorders is a total preorder. Every total preorder is transitive, and a transitive
> relation has no strict cycles. ∎
>
> **`Ignore` is the only member of `UnknownEvidenceMode` that breaks this**, which is why it is the only
> one removed. It keeps its place on `AxisRequirement` and `AxisFloor`, where it is not ordering anything
> and is safe — indeed necessary, since §2.2's lossless-bitrate floor depends on it.

§3.7 extends the claim to the facet tier and to the two tiers composed.

### 3.2 Precedence, ranking, ties, ceilings and floors

```csharp
/// <summary>One axis's place in a preference.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record AxisPreference
{
    /// <summary>Gets the axis.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets whether richer is preferred. Inverting is legitimate for a small-files profile.</summary>
    public bool PreferRicher { get; init; } = true;

    /// <summary>
    /// Gets the value above which the axis stops mattering. A candidate above it compares equal to it.
    /// </summary>
    /// <remarks>
    /// Capping, not refusing. "I do not need more than 2160 lines" and "my player cannot open more than
    /// 2160 lines" are different intents; the second is an <see cref="AxisRequirement"/>. The surveyed
    /// application's profile checkbox does both at once, which is why nobody can predict what unchecking
    /// a quality does.
    /// </remarks>
    public AxisValue? Ceiling { get; init; }

    /// <summary>Gets the value below which the axis stops mattering.</summary>
    public AxisValue? Floor { get; init; }

    /// <summary>
    /// Gets a replacement ranking for a closed axis: groups worst first, members inside one group tied.
    /// </summary>
    /// <remarks>
    /// One mechanism for two jobs. Re-ordering a contested pair is moving a chip; declaring two members
    /// equivalent is dropping two chips into one row. Empty means the family's declared order stands.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<AxisValue>> Ranking { get; init; } = [];

    /// <summary>Gets what an absent reading means on this axis.</summary>
    /// <remarks>
    /// <see cref="PreferenceUnknown"/>, not <see cref="UnknownEvidence"/>. A preference may only place an
    /// absent reading somewhere in the order; it may not skip the axis (which breaks transitivity —
    /// §3.1) and it may not refuse (which is a requirement wearing a preference's clothes — see below).
    /// The restriction is enforced by the type rather than by an analyzer, because here it is cheap to
    /// make structural.
    /// </remarks>
    public PreferenceUnknown WhenUnknown { get; init; } = PreferenceUnknown.Lowest;
}

/// <summary>What an absent reading means <i>in an ordering</i>. Two modes, both order-preserving.</summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="UnknownEvidence"/>. Pass 3 used one type in all three places, which put
/// <c>Ignore</c> and <c>Refuse</c> on <see cref="AxisPreference"/> — the first admitting strict
/// preference cycles (§3.1) and the second violating the document's own central separation, since a
/// preference that can refuse <b>is</b> a requirement, and the argument for not merging preference with
/// refusal was the reason for not merging everything into one score.
/// </para>
/// <para>
/// Both modes are retained where they are safe: <see cref="AxisRequirement.WhenUnknown"/> and
/// <see cref="AxisFloor.WhenUnknown"/> keep the full <see cref="UnknownEvidence"/>, because neither
/// orders anything.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct PreferenceUnknown
{
    /// <summary>An absent reading sorts below every present one.</summary>
    public static PreferenceUnknown Lowest { get; }

    /// <summary>An absent reading is read as a stated value.</summary>
    /// <param name="value">The assumption.</param>
    /// <returns>The mode.</returns>
    /// <remarks>
    /// This is what <c>RungFallback.RoundUp</c> becomes, per axis and legible: instead of a global mode
    /// that silently promotes unrecognized evidence, the policy says "a release that does not say it is
    /// a remux is not one" in one place a person can read and change.
    /// </remarks>
    public static PreferenceUnknown Assume(AxisValue value);

    /// <summary>Gets the mode.</summary>
    public PreferenceUnknownMode Mode { get; }

    /// <summary>Gets the assumption. Absent unless <see cref="Mode"/> is <c>Assume</c>.</summary>
    public AxisValue Assumption { get; }
}

public enum PreferenceUnknownMode { Lowest = 0, Assume = 1 }

/// <summary>
/// What an absent reading means <i>to a requirement or a floor</i>. The typed replacement for a sentinel
/// rung and for RoundUp.
/// </summary>
/// <remarks>
/// Used by <see cref="AxisRequirement"/> and <see cref="AxisFloor"/> only. Ordering uses
/// <see cref="PreferenceUnknown"/>, which is a strict subset — see the remark there.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct UnknownEvidence
{
    /// <summary>An absent reading sorts below every present one.</summary>
    public static UnknownEvidence Lowest { get; }

    /// <summary>The requirement or floor does not apply when the reading is absent.</summary>
    public static UnknownEvidence Ignore { get; }

    /// <summary>A candidate with an absent reading is refused.</summary>
    public static UnknownEvidence Refuse { get; }

    /// <summary>An absent reading is read as a stated value.</summary>
    /// <param name="value">The assumption.</param>
    /// <returns>The mode.</returns>
    public static UnknownEvidence Assume(AxisValue value);

    /// <summary>Gets the mode.</summary>
    public UnknownEvidenceMode Mode { get; }

    /// <summary>Gets the assumption. Absent unless <see cref="Mode"/> is <c>Assume</c>.</summary>
    public AxisValue Assumption { get; }
}

public enum UnknownEvidenceMode { Lowest = 0, Ignore = 1, Refuse = 2, Assume = 3 }

/// <summary>What makes a candidate ineligible.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record AxisRequirement
{
    /// <summary>Gets the axis.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets whether the listed values are required or refused.</summary>
    public required RequirementMode Mode { get; init; }

    /// <summary>Gets the members the requirement names. Empty when it is a bound.</summary>
    public IReadOnlyList<AxisValue> Values { get; init; } = [];

    /// <summary>Gets the least acceptable richness, when the requirement is a bound.</summary>
    public AxisValue? AtLeast { get; init; }

    /// <summary>Gets the greatest acceptable richness, when the requirement is a bound.</summary>
    public AxisValue? AtMost { get; init; }

    /// <summary>Gets what an absent reading means. Defaults to admitting, so we never refuse what we did not inspect.</summary>
    public UnknownEvidence WhenUnknown { get; init; } = UnknownEvidence.Ignore;

    /// <summary>Gets the weakest reading this requirement will act on.</summary>
    /// <remarks>
    /// <para>
    /// New in pass 4, and it turns a silent loss into a stated one. A refusal is irreversible from the
    /// user's side — the release is simply never offered — so refusing on a <i>guess</i> is the worst
    /// thing this model can do. The shipped default refuses <c>Packaging.DiscImage</c>, and Movies'
    /// <c>br-disk</c> guard fires on titles carrying no disc token at all: <c>Movie Title 2005 1080p USA
    /// Blu-ray AVC DTS-HD MA 5.1-PTP</c> (q152) and, worse, <c>The German 2021 Bluray AVC</c> (q160),
    /// which is a <i>title</i> triggering a disc heuristic. Under pass 3 both were silently refused.
    /// </para>
    /// <para>
    /// Defaulting to <see cref="EvidenceSource.ReleaseTitle"/> means a heuristic contributing at
    /// <see cref="EvidenceSource.Assumed"/> — which is what §1.4a requires of every per-kind refinement —
    /// can inform ranking and labelling but cannot refuse. An explicit <c>BDISO</c> token still can.
    /// </para>
    /// </remarks>
    public EvidenceSource MinimumSource { get; init; } = EvidenceSource.ReleaseTitle;

    /// <summary>Gets the user's own words for why. Generated from the requirement when absent.</summary>
    public string? Reason { get; init; }
}

public enum RequirementMode { Require = 0, Refuse = 1 }
```

**`AtLeast` and `AtMost` always speak in richness, never in raw magnitude.** On `Generation`, whose
polarity is `Descending`, `AtLeast(Quantity(1))` means *at most one re-encode*. That is the polarity
attribute earning its keep: every bound in the whole policy reads the same direction, so a UI renders one
widget and a person reads one sentence, and nobody has to remember which axes count downwards. The same
is true of `Ceiling` and `Floor` on `AxisPreference`: a ceiling is the point above which extra *richness*
stops mattering, so `Ceiling = Quantity(1)` on `Generation` means *zero and one re-encode compare equal*.
That is the control D-7 uses for `WEBDL ≈ WEBRip`.

> **Honest note on the sentence itself.** *"`AtLeast(Quantity(1))` means at most one re-encode"* is a
> double negative that will be misread by most people who meet it. Pass 3 asserted a UI mitigation without
> designing one. The mitigation is now specified: the editor never renders a raw magnitude for a
> `Descending` axis — it renders the axis's own words (`at most one re-encode`, `untouched only`) from
> `[Display]` prose plus the polarity, and `Describe()` does the same. See §3.5 and QA-18.

#### Provenance: a claim never outranks a measurement

`EvidenceSource` is ordered and load-bearing at *read* time (§1.2), and pass 3 stopped there. But
`AxisReading.Source` is carried on the point and was never read by `Compare`, `Admits`, `IsGoodEnough` or
`Decide`, which produces a loop that does not terminate:

1. A release claims `1080p` in its title → candidate has `Resolution = 1080, Source = ReleaseTitle`.
2. Nothing held → **Grab**. Download. Import.
3. A probe measures 720 lines → the held point becomes `Resolution = 720, Source = ContainerProbe`.
   Correct, and the whole point of the source ordering.
4. The same release reappears on the next RSS pass. Candidate 1080 against held 720 → **Better** →
   **Grab**. Go to 2.

§6.3 makes it *worse* by design, because it deletes
`IgnoreStatedResolutionFor("cam","ts","tc","wp","dvd")` on the grounds that a probe overrules it by
ordinary source precedence. Source precedence fixes the reading; it *creates* the grab loop, because the
held file is the only side that ever gets probed.

> **The rule.** When `Compare` reports `Better`, and the axis it names carries a candidate reading whose
> `EvidenceSource` is **strictly weaker** than the held point's reading on that same axis, the decision is
> `NotAnUpgrade`. *A claim never outranks a measurement.*

Three things about where this lives, because they are the design:

* **It is on `Decide`, not on `Compare`.** The rule is pairwise and asymmetric, so folding it into the
  ordering would destroy the transitivity §3.1 just proved. `Compare` stays a total preorder; `Decide` is
  already the function that makes the irreversible call, and it already names the deciding axis for its
  sentence, so the rule costs one comparison.
* **It does not fall through to the next axis.** Falling through is `Ignore` by another name, with the
  same cycle. The grab is simply refused.
* **It terminates.** After import the held reading is at `ContainerProbe` or `StreamProbe`, and no
  `ReleaseTitle` or `Assumed` reading can ever exceed it again. Step 4 becomes `NotAnUpgrade` for ever.

QA-11 carries the regression: *import once, re-offer the same title, assert `NotAnUpgrade`.* And
`Describe()` gains the clause — *"…and never re-download on a claim we have already measured."*

### 3.3 Cutoff as a typed predicate

```csharp
/// <summary>When a held file is good enough to stop looking.</summary>
/// <remarks>
/// Conjunctive over axes, which is what a person means by "good enough": <i>at least 1080 lines and at
/// most one re-encode</i>. The surveyed application makes you name one of 29 rungs, which forces you to
/// accept a whole cross-product cell — choosing <c>Bluray-1080p</c> as a cutoff silently also says
/// something about WEB releases, and there is no way to say "1080p from anywhere is fine".
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CutoffPredicate
{
    /// <summary>Gets the predicate that is never satisfied, so upgrades never stop.</summary>
    public static CutoffPredicate Never { get; }

    /// <summary>Gets the per-axis floors, all of which must hold.</summary>
    public IReadOnlyList<AxisFloor> Floors { get; init; } = [];

    /// <summary>Gets whether upgrades are searched for at all below the cutoff.</summary>
    public bool UpgradesEnabled { get; init; } = true;

    /// <summary>Tests a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when every floor holds.</returns>
    public bool IsSatisfiedBy(QualityPoint point);
}

/// <summary>One axis's contribution to "good enough".</summary>
/// <param name="Axis">The axis.</param>
/// <param name="AtLeast">The least acceptable richness.</param>
/// <param name="WhenUnknown">What an absent reading means. <c>Ignore</c> makes the floor vacuous.</param>
public readonly record struct AxisFloor(
    QualityAxisId Axis,
    AxisValue AtLeast,
    UnknownEvidence WhenUnknown);
```

`CutoffPolicy`, `CutoffPolicy.MeetsCutoff`, `ShouldSearchForUpgrade` and `ProperHandling` all delete.
`ProperHandling.PreferProper` becomes *the revision axes appear in the precedence list*;
`ProperHandling.IgnoreProper` becomes *they do not*. An enum dissolved into the list that was already
there — the same move `RungFallback` and `CrossFamilyRule` make.

### 3.4 The decision, and its sentence

`Decide` walks a fixed order, and every branch produces a sentence the UI shows verbatim:

| # | Step | Verdict | Sentence template |
|---:|---|---|---|
| 1 | `Admits(candidate)` fails | `Refused` | *"Refused: {axis} is {value}, which you never take."* |
| 2 | a `Refuse`-mode requirement `WhenUnknown` fires | `EvidenceInsufficient` | *"Refused: nothing in the release says what its {axis} is."* |
| 3 | `held is null` | `Grab` | *"Nothing held."* |
| 4 | `IsGoodEnough(held)` and `Compare` is not `Better` | `AlreadyGoodEnough` | *"Already good enough: {held label}."* |
| 5 | `Compare` is `Worse` or `Same` | `NotAnUpgrade` | *"Not an upgrade: {axis} is {a} against {b}."* |
| **6** | **`Compare` is `Incomparable`** | **`EvidenceInsufficient`** | *"Cannot tell: {axis} has no reading on one side."* |
| **7** | **`Compare` is `Better`, and the deciding axis's candidate `Source` is weaker than the held reading's (§3.2)** | **`NotAnUpgrade`** | *"Not an upgrade: the release claims {axis} {a}; we measured {b} on the file we hold."* |
| 8 | otherwise | `Grab` | *"Upgrade: {axis} {a} → {b}."* |

Rows 6 and 7 are new in pass 4 and both fix inversions. Pass 3's table had six rows and **no
`Incomparable` row**, so `Incomparable` fell into "otherwise" and was **grabbed**, with a sentence
obliged to name "the axis that decided" when no axis had — the exact opposite of the intent. Row 7 is the
provenance rule; it is the only row that reads `EvidenceSource`, and putting it after `Compare` rather
than inside it is what preserves §3.1's proof.

When rows 5 and 8 both fail to separate the candidates on the core tier, the facet score decides — §3.7
states exactly where that happens and what the sentence is.

That the reason names *the axis that decided* is the whole UX argument for the model. Under a ladder the
only honest sentence is "rung 18 is not above rung 19", which tells a user nothing about what to change.

### 3.5 The profile UI

`Describe()` renders the policy as one paragraph, and **the paragraph is the UI's own subject**:

> Prefer **resolution**, up to **2160 lines** — then the **richest origin** — then the **fewest
> re-encodes**, counting untouched and once-encoded alike — then a **REAL** over a **PROPER**. Among
> releases none of that separates, count **HDR10+ and Dolby Vision alike** as a bonus. Good enough at
> **1080 lines with at most one re-encode**. Never take a **camera capture**, a **workprint** or a
> **disc image** — and never re-download on a claim we have already measured.

Every clause in that paragraph is generated, including the last: `Describe()` renders the provenance rule
because it is a behaviour a user would otherwise have to discover. A clause for a withheld refusal is not
rendered at all, which is the visible half of §5.1's second note — the prose says what the policy does,
never what it was going to do.

The editor is four stacked lists:

1. **Prefer, in this order** — a vertical list of axis chips, drag to reorder, drag out to stop ordering
   on it. Each chip expands to that axis's own controls: for a closed axis, its members as draggable rows
   with drop-into-one-row to tie them; for a quantity, a ceiling and a floor; for every axis, the
   "when the release does not say" choice.
2. **Then, as a bonus** — the facet list (§3.7): nominal axes and user axes with a points slider each.
3. **Good enough when** — one row per axis floor, added from a picker of the declared axes.
4. **Never take** — requirement chips.

**Pass 3 claimed "three affordances against 29 draggable rows, and the count does not grow with the size
of the taxonomy". The second half is true and the first half is not, and the arithmetic is worth doing
because the whole intuitiveness argument rests on it.** What a user actually faces for the shipped video
default:

* 13 declared axes, each in or out of precedence, and orderable within it;
* per precedence entry: a polarity, a `Ceiling`, a `Floor`, and a **two-mode** `WhenUnknown` (after §3.2
  removed the two dangerous ones — it was four in pass 3, one of which could produce an infinite upgrade
  loop and one of which secretly refused);
* per closed axis in precedence: a `Ranking` of its members into ordered groups with drop-to-tie;
* a facet list with a bounded points value per member;
* a requirements list with `Require`/`Refuse`, its own `WhenUnknown` and a `MinimumSource`;
* a cutoff list of `AxisFloor`s, each with a third `WhenUnknown`.

That is a **larger** configuration space than 29 checkboxes, by a wide margin. What did not grow is the
number of *lists*; the number of decisions grew. The defensible claim, and the one this document now
makes, is narrower and still worth something:

> The ladder's 29 rows are 29 *entangled* decisions — unchecking one silently decides things about four
> other cells of the cross-product, which is why nobody can predict what it does. The axes model has more
> knobs and each one means exactly one thing. **Fewer decisions is not the claim; separable decisions
> is.**

Three usability liabilities remain, and pass 4 answers two of them:

1. **The vocabulary is ours, not theirs.** Users read `WEB-DL`, `BluRay`, `REMUX`, `1080p` on indexers all
   day. They do not read `Origin` or `Generation` anywhere. §7's label layer bridges the *output*; the
   editor must bridge the *input*, so every axis chip renders its members in the community's spelling —
   `Origin` shows `Remux / BluRay / WEB / HDTV`, not `HighDefinitionDiscBitstream` — and the diagnostic
   name is a tooltip. This is a requirement on QA-18, not an aspiration.
2. **The polarity double negative** — answered in §3.2: the editor never renders a raw magnitude for a
   `Descending` axis.
3. **Lexicographic thinking.** People do not hold preferences as strict priority chains; they hold them
   as "1080p BluRay, or 1080p WEB if that is all there is" — a set with a fallback. §3.7's facet tier
   covers part of this and §3.3's conjunctive cutoff covers another part; **the remainder is a real,
   stated loss**, recorded in §8 (QA-j) with the mechanism that would close it.

**The live preview is the feature that only this model can have, and pass 4 promotes it from a paragraph
to a work package.** Because the policy is a total function over declared axes, the editor can rank a real
list of release names beside the controls and re-rank it on every keystroke. The corpus (§6.4) is exactly
the right source for that list. A rung ladder cannot do this because half the cross-product has no rung
and the answer is `RoundUp`. It is the single strongest answer to everything above — it converts an
abstract vocabulary into a concrete one by showing the user their own library re-sorting as they drag —
and it now ships **in the same release as the editor** (QA-18), because an editor without it is the
version of this design that deserves the criticism.

### 3.6 Custom Formats — the verdict

**Verdict, restated for pass 4: absorbed for everything a family can declare; what remains survives as
*user axes*; and the arithmetic — which pass 3 deleted without admitting it was deleting anything —
returns in the bounded, tie-only form D-8 specifies (§3.7).**

Pass 3's verdict was *"absorbed … no scoring arithmetic"*, and its evidence table addressed
`-10000` and "minimum custom format score" in detail while never once addressing **trade-off** scores.
That is not a small omission: it is the half of Custom Formats used by people who are not gaming a
refusal, and the shape it takes — *"I prefer 2160p, but I would rather have a 1080p Remux than a 2160p
WEBRip"* — is the most-copied community profile pattern there is. Under a pure precedence list it is
unrepresentable in either direction: `Resolution` first means resolution always wins, `Generation` or
`Origin` first means a 480p Remux beats a 2160p WEB-DL globally, and `Ceiling` does not help because a
capped axis makes 2160p *equal* to 1080p rather than a tiebreak. There was no partial-credit construct
anywhere in pass 3's `AxisPreference`.

**What D-8 gives back, and what it deliberately does not.** The owner's resolution is a bounded additive
score over **declared facets**, consulted only when the core is a tie. That is the legitimate successor to
Custom Format scoring, and it covers HDR flavour, audio format, distributor and release-group tiers —
which is the large majority of real custom-format usage. It **does not** cover trading a core axis against
another core axis, because the core stays strictly lexicographic by decision, for cycle-safety and
readability. So, plainly:

> **The "prefer a 1080p Remux over a 2160p WEBRip" profile shape does not migrate as an ordering.** Users
> who hold that preference express it as a **cutoff** instead — `GoodEnoughAt(Resolution, 1080)` plus
> `GoodEnoughAt(Origin, HighDefinitionDiscBitstream)` — which delivers the intent for a *held* file (once
> you have the 1080p Remux, the 2160p WEBRip is never taken) and does **not** deliver it at selection time
> when both are offered at once, where resolution still wins. That is a genuine loss against a scalar
> weight, it is the price of the guarantee in §3.1, and it is recorded in §8 (QA-j) with the one
> construct that would close it.

The honest breakdown of what upstream Custom Formats are actually used for:

| Use | Under this model |
|---|---|
| HDR flavour (DV, HDR10+, HDR10, HLG) | **Absorbed** — `DynamicRange` axis |
| Audio format (Atmos, DTS-HD MA, TrueHD, DD+) | **Absorbed** — `Audio` axis |
| Codec (x265 wanted, x265 refused, AV1 refused) | **Absorbed** — `Codec` axis, plus a `Refuse` requirement |
| Streaming service (AMZN, NF, DSNP) | **Absorbed** — `Distributor` nominal axis |
| Repack/Proper preference | **Absorbed** — `Corrections`/`Mislabels`/`Repacked` axes |
| Resolution and source preferences | **Absorbed** — they were always axes; the ladder just crushed them |
| Upscale rejection, hardcoded-subs rejection, whole-disc rejection | **Absorbed** — `Flaws` and `Packaging` requirements |
| **Trade-off scoring among facets** ("Atmos is worth something; DV is worth more") | **Absorbed** — §3.7's facet tier, bounded and consulted only on a core tie |
| **Trade-off scoring across core axes** ("1080p Remux over 2160p WEBRip") | **Not absorbed, by decision.** See the note above and §8 QA-j. |
| **Release-group preference** ("prefer these groups") | **Absorbed as an ordered user axis plus a facet score.** An open vocabulary no family can enumerate, but a user can, and tiering is what they do with it. |
| **Arbitrary title matching** ("HDLight", "Hybrid", a specific bad group's watermark) | **Not absorbed.** By construction: it is the set of things nobody modelled. |
| **Indexer flags** (freeleech, internal, scene) | **Not absorbed, and leaves quality entirely.** These are facts about the *acquisition*, not about the file. They belong to release selection, beside seeders and age. Putting them in the quality model is a category error upstream makes and we should not copy. |

So a **user axis** stays in the design:

```csharp
/// <summary>An axis a user declares over evidence no family models.</summary>
/// <remarks>
/// <para>
/// What is left of the surveyed application's custom formats. A user axis is an <b>ordinal</b> axis whose
/// members are named predicates over release evidence, declared in the user's own order; once declared it
/// is an axis like any other, so it appears in the same precedence list, takes the same requirements,
/// carries the same facet scores, and is rendered by the same editor. There is no <c>-10000</c> and no
/// separate "minimum custom format score" concept, because refusal was never a very negative preference.
/// </para>
/// <para>
/// <b>Ordinal, corrected in pass 4.</b> Pass 3 called it nominal in one paragraph, documented
/// <see cref="Members"/> as <i>"in the user's own order"</i> in the next, and put it in the precedence
/// list in the one after — while §1.6 forbids a nominal axis from appearing in precedence at all. Members
/// in the user's own order <i>is</i> the definition of ordinal, and it matters more than a doc slip
/// because tiered release-group preference (Tier 1 groups, then Tier 2, then everything else) is the
/// single most common real profile pattern and it needs precisely an ordered user axis.
/// <c>Ranking</c> is available to re-group it, exactly as for a family's own closed axis.
/// </para>
/// <para>
/// A user axis is total by construction: a point matches the first member whose predicate holds, or none,
/// and "none" is the bottom element. So it satisfies §3.1's proof obligation like any other ordinal axis.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record UserAxis
{
    /// <summary>Gets the axis's identifier, minted by the host from the user's name.</summary>
    public required QualityAxisId Id { get; init; }

    /// <summary>Gets the user's name for it, e.g. "Preferred groups".</summary>
    public required string Name { get; init; }

    /// <summary>Gets the family it applies to.</summary>
    public required FormatFamilyId Family { get; init; }

    /// <summary>Gets the members, in the user's own order. A member matches or it does not.</summary>
    public required IReadOnlyList<UserAxisMember> Members { get; init; }
}

/// <summary>One member of a user axis.</summary>
/// <param name="Token">The member's name, which is also what the label renderer spells.</param>
/// <param name="Match">What the member matches on.</param>
public sealed record UserAxisMember(string Token, EvidenceMatch Match);

/// <summary>What a user-axis member matches on.</summary>
/// <remarks>
/// Deliberately three cases and not a general expression language. A user axis is where P2-2's
/// stringly-typed grammars would grow back if the surface allowed one, so it does not.
/// </remarks>
public abstract record EvidenceMatch
{
    /// <summary>Matches when the release group is one of the listed names.</summary>
    public sealed record ReleaseGroupIn(IReadOnlyList<string> Groups) : EvidenceMatch;

    /// <summary>Matches when the release title contains one of the listed words.</summary>
    public sealed record TitleContains(IReadOnlyList<string> Words) : EvidenceMatch;

    /// <summary>Matches when the release title matches a pattern the user wrote.</summary>
    /// <remarks>
    /// The escape hatch, and the only place a user-authored regex exists in the platform. It is timed and
    /// length-bounded by the host for the same reason every other user-supplied pattern is.
    /// </remarks>
    public sealed record TitleMatches(string Pattern) : EvidenceMatch;
}
```

**What actually changes for a user.** Upstream, a usable Radarr profile requires importing somebody
else's several-dozen custom formats; the shipped defaults are not usable on their own, which is the real
indictment. Here the shipped default policy (§5) is complete: HDR, audio, codec, source, packaging and
upscales are all already expressible. A user axis is for the long tail, which is what an escape hatch is
supposed to be for.

### 3.7 The facet tier — D-8

> **One bounded additive score over declared facets, consulted only when the core precedence list has
> returned `Same`. It cannot promote a candidate the core ranked lower, it cannot refuse anything, and it
> cannot cycle.**

This is the owner's resolution of S-A1 and it is deliberately confined. §3.6 states what it does not do.

```csharp
/// <summary>A bounded additive score over axes the precedence list does not order.</summary>
/// <remarks>
/// <para>
/// The legitimate successor to custom-format scoring. Two properties make it safe where a global weight
/// was not: it is consulted <b>only</b> on a core tie, so it can never overturn an ordered judgement; and
/// it is <b>bounded</b>, so it cannot be inflated into a shadow precedence list.
/// </para>
/// <para>
/// Scoring is not refusing. A facet may carry negative points — "I mildly dislike Dolby Vision" is a real
/// preference — but no number of negative points makes a candidate ineligible. That is
/// <see cref="AxisRequirement"/>, and keeping them apart is what deletes the <c>-10000</c>.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FacetScoring
{
    /// <summary>Gets the scoring that never separates anything.</summary>
    public static FacetScoring None { get; }

    /// <summary>Gets the greatest magnitude one facet may contribute. Fixed at 100.</summary>
    public static int PointsPerFacet => 100;

    /// <summary>Gets the greatest number of facets a policy may declare. Fixed at 10.</summary>
    public static int MaximumFacets => 10;

    /// <summary>Gets the declared facets.</summary>
    public IReadOnlyList<FacetScore> Scores { get; init; } = [];

    /// <summary>Scores one point. Always within ±<see cref="PointsPerFacet"/> × <see cref="MaximumFacets"/>.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The score.</returns>
    public int Of(QualityPoint point);
}

/// <summary>What one facet is worth.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FacetScore
{
    /// <summary>Gets the axis. Must not appear in <c>QualityPolicy.Precedence</c> — <c>ARXQ006</c>.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets what each member is worth, in [-100, 100]. An unlisted member is worth zero.</summary>
    public required IReadOnlyDictionary<AxisValue, int> Points { get; init; }

    /// <summary>Gets what an absent reading is worth. Zero, and not configurable.</summary>
    /// <remarks>
    /// Deliberately fixed. A configurable unknown-score is the one thing that would let a facet punish
    /// releases for silence, which is how a bonus becomes a refusal.
    /// </remarks>
    public static int WhenUnknown => 0;
}
```

**Which axes may be facets.** Exactly those the precedence list does not order (`ARXQ006`), plus user
axes. The disjointness is not a style rule — it is the hypothesis the cycle-safety argument uses, and it
is also what makes the editor legible: an axis is in the "in this order" list or in the "as a bonus" list,
never both, so a user never has to reason about an axis fighting itself.

**Set-valued facets take the greatest member, not the sum.** `DynamicRange` after §2.1 is a set, and a
release carrying both Dolby Vision and HDR10+ scores `max(15, 15) = 15`, not 30. A release is not better
for labelling its dynamic range twice, and summing would make `Hybrid` releases outrank everything for a
reason nobody intended.

**Cycle-safety of the combined system.**

> **Claim.** The composite judgement — core precedence first, facet score as the sole tiebreak — is a
> total preorder on the points of a family, hence transitive and acyclic.
>
> **Proof.** §3.1 establishes that the core `Compare` is a total preorder ⪯꜀. `FacetScoring.Of` is a
> function from points to ℤ (each `FacetScore` is a function from a point's reading on one axis to an
> integer, with a fixed value for absence; a finite sum of functions is a function), so ⪯_f defined by
> *p* ⪯_f *q* ⟺ `Of(p) ≤ Of(q)` is a total preorder. The composite is the lexicographic product
> ⪯꜀ ⊗ ⪯_f, and the lexicographic product of two total preorders is a total preorder. ∎
>
> **The bound is not needed for this proof**, and the document says so rather than implying otherwise:
> acyclicity comes from `Of` being a *function of one point*, which is exactly what a global weight was
> not being used as. The bound is there for two other reasons — it keeps `Describe()` finite and
> renderable, and it stops the tier being inflated into a second, competing model, which is the failure
> mode the whole document exists to prevent.

**The sentence.** When the facet score decides, `Decide` says so in its own words — *"Upgrade: same
resolution, origin and generation, but Dolby Vision against SDR (+15)."* — and when it does not, the core
sentence stands. A facet-decided grab is always visibly a tiebreak, which is the honest thing for it to
be.

**What this means for Custom Formats, stated once, explicitly:**

| Custom Format capability | Here |
|---|---|
| Positive score on a token ("Atmos +100") | **A facet score.** Bounded to ±100, tie-only. |
| Negative score on a token ("x265 −50") | **A facet score.** Negative points are allowed. |
| `-10000` as rejection | **Gone.** `AxisRequirement` with `RequirementMode.Refuse`. Refusal was never a very negative preference. |
| "Minimum custom format score" as a quality gate | **Gone.** A cutoff is `CutoffPredicate`, over axes, conjunctive. A score cannot gate. |
| Scores tuned to outrank resolution or source | **Not supported.** §3.6's note; §8 QA-j. |
| Score arithmetic over regex-matched title fragments | **Supported through a user axis**, bounded and tie-only — which is strictly less power than upstream and, per §3.6, is the point. |

---

## 4. Computed size plausibility

### 4.1 What it replaces

Thirty `Tier(...)` rows in `MoviesLadder.cs`, of which twenty-nine carry one of two identical triples.
`DeclarativeQualityEvaluator.IsPlausibleSize` divides bytes by megabytes and by runtime minutes and
compares against the row. The tell that this was never per-rung data: `WEBRip-480p` and `HDTV-1080p` and
`CAM` all carry `(0, 100, 95)`, so the table distinguishes nothing among two thirds of its rows and then
switches to `(0, null, null)` — no ceiling at all — from `Bluray-1080p` up, which means the check does
nothing for exactly the rungs where a 60 GB mistake is possible.

### 4.2 The model

A file's size is its bitrate times its duration. A video stream's bitrate is its pixel rate times the bits
each pixel costs, and what a pixel costs depends on the codec and on how near-transparent the encode is
aiming to be — which is what `Origin` and `Generation` already say.

```
expectedVideoBitsPerSecond
    = width × height × frameRate
    × BitsPerPixel(codec)
    × MasterFactor(origin, generation)

expectedTotalBitsPerSecond = expectedVideoBitsPerSecond + AudioBitsPerSecond(audio)

expectedBytes  = expectedTotalBitsPerSecond × durationSeconds / 8
floorBytes     = expectedBytes × 0.35
ceilingBytes   = expectedBytes × 3.0   (× 2.5 again when the frame rate is unknown)
```

`width` is derived from `Resolution` by the standard 16:9 raster (`height × 16 ÷ 9`, rounded to an even
number), except at 480 and 576 lines where the DVD/SDTV rasters 720×480 and 720×576 are used.

**Bits per pixel, by codec.**

| Codec | bpp | Derivation |
|---|---:|---|
| MPEG-2 | 0.200 | ≈ 2× H.264 at equal quality; cross-checked against ATSC A/53's 19.39 Mbit/s transport rate carrying 1920×1080i30 |
| MPEG-4 part 2 (XviD, DivX) | 0.150 | ≈ 1.5× H.264; the SD-era codec, which is why a release using it is almost never high definition |
| VC-1 | 0.120 | ≈ 1.2× H.264 |
| H.264 | 0.100 | **The reference point.** Netflix's published per-title encoding ladder puts 1080p24 at ≈5 Mbit/s: 1920×1080×24 = 49.8 Mpixel/s, 5.0 ÷ 49.8 = 0.100 |
| VP9 | 0.055 | ≈ H.265 |
| H.265 | 0.055 | ≈ 50% of H.264 at equal quality, the figure the HEVC standardization work reports |
| AV1 | 0.040 | ≈ 70% of H.265 |
| H.266 (VVC) | 0.036 | ≈ 90% of AV1 at equal quality, from the VVC standardization comparisons. **New in pass 4** — corpus q123 (`…UHDBDRip.h266-GROUP`) has no codec reading at all today, because neither `VideoCodec` nor `ReleaseCodecScanner` knows the token, so its size expectation was undefined. QA-17 adds the token; §2.1 adds the member |

**Master factor, by origin.** After D-7 the table is *simpler* than pass 3's, because the generation
column collapses: an origin member either names the master's own bitstream (in which case generation is 0
by construction) or names an encode of it (in which case the rip factor applies). This is the same
observation §2.1 uses to decide which origins get a bitstream member, read from the other side.

| Origin | Factor | Derivation |
|---|---:|---|
| `HighDefinitionDiscBitstream` | 5.0 | Blu-ray permits 40 Mbit/s video, UHD Blu-ray 100 Mbit/s; 1080p24 H.264 at 0.100 × 5.0 = 24.9 Mbit/s and 2160p24 H.265 at 0.055 × 5.0 = 54.7 Mbit/s, both inside the spec and at the observed centre |
| `StandardDefinitionDiscBitstream` | 2.5 | DVD-Video's 9.8 Mbit/s total; 720×480×30 MPEG-2 at 0.200 × 2.5 = 5.2 Mbit/s, the observed DVD video rate |
| `BroadcastBitstream` | 1.5 | ATSC A/53 at 19.39 Mbit/s; 1920×1080×30 MPEG-2 at 0.200 × 1.5 = 18.7 Mbit/s |
| `Stream`, generation 0 | 1.0 | The reference point the bpp column is calibrated on |
| `HighDefinitionDisc`, `StandardDefinitionDisc`, `Broadcast`, `Stream` at generation ≥ 1 | 0.9 | A rip targets a size below its source by definition |
| any origin at generation ≥ 2 | 0.7 | A rip of a rip targets below that again |
| `CameraCapture`, `FilmPrint`, `Workprint` | 0.5 | A re-photographed projection carries far less real detail than its pixel count claims |

**Audio allowance.**

| `AudioPresentation` | kbit/s | Derivation |
|---|---:|---|
| Room capture | 128 | |
| Lossy stereo | 192 | AAC-LC transparency for stereo |
| Lossy surround | 640 | Dolby Digital's 5.1 maximum |
| Lossy object | 768 | Dolby Digital Plus with Atmos, streaming rates |
| Lossless | 4 500 | TrueHD / DTS-HD Master Audio, typical 5.1 |
| Lossless object | 6 000 | TrueHD with Atmos, typical |
| absent | 640 | |

**Cross-check against reality, in the document so it can be re-run:**

| Release | Computed centre | Observed range |
|---|---:|---|
| 1080p24 H.264 WEB-DL, lossy surround | 5.6 Mbit/s | 4–8 |
| 1080p24 H.264 Remux, lossless | 29.4 Mbit/s | 25–38 |
| 2160p24 H.265 Remux, lossless object | 60.7 Mbit/s | 50–80 |
| 1080p24 H.265 BDRip, lossy surround | 3.1 Mbit/s | 2–8 |
| 480p30 MPEG-2 DVD, lossy surround | 5.8 Mbit/s | 5–6.5 |

The `[0.35×, 3.0×]` band is wide on purpose: a legitimate encode of one point varies by roughly 3× between
a size-conscious x265 and a high-bitrate x264. The gate's job is to catch a 200 MB "2160p Remux" and a
60 GB "480p", not to police taste.

**When an input is absent — which at grab time is the common case, and which pass 3 left undefined.**
`SizeExpectation.NotAssessable` existed and pass 3 never said when it is returned. That is the interesting
half of the model, because both target failures — the 200 MB "2160p Remux" and the 60 GB "480p" — are
exactly the cases where an input is a *claim* rather than a measurement.

| Absent input | Rule | Widening |
|---|---|---|
| Frame rate | assume 24 | ceiling × 2.5 (unchanged from pass 3) |
| Codec | assume **H.264** (0.100) — the mode of the corpus and the reference point | floor × 0.5, ceiling × 1.5, because the true bpp spans 0.036–0.200 |
| Audio | assume lossy surround (640 kbit/s), as the table already says | none; it is a small share of the total |
| Dynamic range | ignored; it is not an input | — |
| Resolution | **`NotAssessable`.** There is no defensible centre — a claim-free release could be 480 or 2160, a 20× span in pixel rate, and a band that wide asserts nothing | — |
| `Origin` **and** `Generation` both absent | **`NotAssessable`.** `MasterFactor(∅, ∅)` spans 0.5–5.0, a 10× span, which is wider than the plausibility band itself | — |
| `Origin` absent, `Generation` known | assume the rip factor for the generation (0.9, or 0.7 at ≥ 2) | ceiling × 1.5 |
| `Origin` known, `Generation` absent | a bitstream member fixes it at 0; a rip member assumes 1 | none |
| Duration is `TimeSpan.Zero` | **`NotAssessable`.** Size is bitrate × duration and there is no other term | — |

Two consequences worth stating. First, `NotAssessable` is a *pass*, never a rejection: a release nobody
can assess is not thereby implausible, and a size gate that refused what it could not measure would be a
requirement in disguise — the same category error §3.2 removes from `AxisPreference`. Second, the widening
factors are deliberately asymmetric in the codec row, because assuming H.264 for what is really AV1
over-predicts size by 2.5× and would reject a perfectly good small file; the floor moves further than the
ceiling for that reason.

**One case the gate now catches that the ladder suppressed instead.** A release stating both `Remux` and
`720p` (corpus q101) is labelled `Remux-720p` by §7 — the ladder deliberately renamed it `Bluray-720p`
because *"a 720p remux is not a thing"*. Under this model the title is believed and then **measured**:
1280×720×24 × 0.100 × 5.0 = 11.1 Mbit/s expected, floor 3.9 Mbit/s. A group lying about a 2 Mbit/s encode
fails the floor and is rejected with a reason; a genuine 720p bitstream copy of an SD-era disc passes. A
computed check on the actual file is a better mechanism than a hard-coded rename, and it is available only
because the model kept the two facts separate.

### 4.3 Provenance

Stated so the difference from the replaced table is demonstrable rather than asserted.

* **Rasters** — ITU-R BT.601 / BT.709 / BT.2020 and the DVD-Video and ATSC/DVB raster definitions. Public
  standards.
* **Ceilings** — Blu-ray Disc Association's 40 Mbit/s video maximum, UHD Blu-ray's 100 Mbit/s,
  DVD-Video's 9.8 Mbit/s total, ATSC A/53's 19.39 Mbit/s transport rate. Public specifications.
* **Codec ratios** — the ≈50% HEVC-over-AVC figure from the HEVC standardization comparisons, and the
  ≈30% AV1-over-HEVC figure from the AOMedia comparisons. Published.
* **The calibration point** — Netflix's published per-title encoding work and Google's published
  recommended YouTube upload bitrates, which independently put 1080p H.264 at 5–8 Mbit/s.
* **Audio rates** — Dolby's published Dolby Digital, Dolby Digital Plus and TrueHD rate ranges, and DTS's
  for DTS-HD Master Audio.

**Why this is not the replaced table restated.** Different independent variables (codec and origin, not a
rung name), different units (bits per pixel, not megabytes per minute of runtime), different arity (6
codec rows plus 6 factor rows plus 6 audio rows against 30 rung rows), and — the decisive one —
**different domain**: this model produces an answer for combinations the rung table has no row for. Ask
it for a 1440p AV1 stream capture at 60 fps and it answers; the rung table has no such rung and would
`RoundUp` to something else's number. A table that computes answers its source could not produce is not
that source's table.

```csharp
/// <summary>What size a file at one point is expected to be.</summary>
/// <param name="ExpectedBytes">The centre of the expectation.</param>
/// <param name="FloorBytes">Below this, the file is implausibly small for what it claims.</param>
/// <param name="CeilingBytes">Above this, implausibly large.</param>
/// <param name="Basis">The computation, rendered, so a health check can explain a rejection.</param>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct SizeExpectation(
    long ExpectedBytes,
    long FloorBytes,
    long CeilingBytes,
    string Basis)
{
    /// <summary>Gets the expectation meaning "this family has no size model".</summary>
    public static SizeExpectation NotAssessable { get; }

    /// <summary>Assesses an actual size.</summary>
    /// <param name="sizeInBytes">The file's size.</param>
    /// <returns>The verdict.</returns>
    public SizeVerdict Assess(long sizeInBytes);
}

/// <summary>Whether a size is plausible for what the release claims.</summary>
public enum SizeVerdict { NotAssessable = 0, Plausible = 1, ImplausiblySmall = 2, ImplausiblyLarge = 3 }
```

**The other families.** Music and audiobooks need no table at all: `Bitrate` is already an axis, so
expected bytes is `bitrate × duration ÷ 8`, and a lossless file's expectation is
`sampleRate × depth × channels × 0.6 × duration ÷ 8` (the 0.6 being FLAC's typical compression ratio for
music, which is a measured property of the codec, not anybody's table). **Written copies return
`NotAssessable`** — an ebook's size is a function of whether it carries page images, which is already the
`TextForm` axis, so a size gate would only restate it. Saying so is better than inventing a range.

---

## 5. Shipped defaults — our opinion, stated

Each line is a claim we are making. "Their data, copied" is replaced by "our opinion, written down and
argued", and a user who disagrees changes one chip.

### 5.1 Video

```csharp
public static QualityPolicy Default { get; } = QualityPolicy.For(VideoQualityType.Instance, p => p

    // ── The core: strictly lexicographic, total, acyclic (§3.1). ──────────────────────────────
    .Prefer(a => a.Resolution).UpTo(2160).WhenUnknownRanksLowest()
    .Prefer(a => a.Origin).WhenUnknownRanksLowest()
    .Prefer(a => a.Generation).UpTo(1).WhenUnknownAssume(1)
    //                        ^^^^^^^ a ceiling in richness (§3.2): zero and one re-encode compare
    //                                equal, two does not. This is the WEBDL ≈ WEBRip equivalence.
    .Prefer(a => a.Mislabels).WhenUnknownAssume(0)
    .Prefer(a => a.Corrections).WhenUnknownAssume(0)

    // ── The facet tier: consulted only when the core above returns Same (§3.7). ───────────────
    .Facet(a => a.DynamicRange)
        .Worth(DynamicRange.HybridLogGamma, 5)
        .Worth(DynamicRange.HighDynamicRange10, 10)
        .Worth(DynamicRange.HighDynamicRange10Plus, 15)
        .Worth(DynamicRange.DolbyVisionWithFallback, 15)
        .Worth(DynamicRange.DolbyVision, 15)

    // ── Eligibility. Every refusal acts only on a stated reading (AxisRequirement.MinimumSource). ──
    .Refuse(a => a.Origin, VideoOrigin.CameraCapture, VideoOrigin.Workprint)
    .Refuse(a => a.Packaging, Packaging.DiscImage, Packaging.DiscFolder)
    // .Refuse(a => a.Flaws, VideoFlaw.Upscaled, VideoFlaw.Sample)
    //    Withheld until QA-17. No scanner emits a flaw marker, and the resolution scanner reads
    //    `4kto1080p` — the one upscale token the vocabulary knows — as a clean 1080. Shipping a
    //    refusal that silently never fires is worse than admitting we cannot see it yet.

    // ── Good enough. ─────────────────────────────────────────────────────────────────────────
    .GoodEnoughAt(a => a.Resolution, 1080)
    .GoodEnoughAt(a => a.Generation, 1));
```

**The four behaviours this default exists to hold at once**, the first two of which pass 3's could not
hold simultaneously and the second two of which pass 3's lost outright:

| Pair | How it resolves | Result |
|---|---|---|
| `WEBRip-1080p` vs `WEBDL-1080p` | Resolution ties (1080) → Origin ties (`Stream` for both) → Generation: 1 and 0 are both at or above the ceiling, so they tie → Mislabels, Corrections tie | **`Same`** ✓ |
| `Bluray-1080p` vs `Remux-1080p` | Resolution ties (1080) → **Origin decides**: `HighDefinitionDisc` (8) below `HighDefinitionDiscBitstream` (9) | **`Better`** ✓ |
| `Bluray-1080p` vs `BRRip-1080p` | Resolution ties → Origin ties (`HighDefinitionDisc`) → **Generation decides**: 1 above 2 | **`Worse`** ✓ |
| `HDTV-1080p` vs `Raw-HD` | Resolution ties → **Origin decides**: `Broadcast` (3) below `BroadcastBitstream` (4) | **`Better`** ✓, matching `MoviesLadder.cs` |

`UpgradeDecisionTests.cs:63-66` — all three cases green, with their assertion text untouched. Pass 3's
default turned the first of them red.

| Choice | Why |
|---|---|
| Resolution first | It is the one dimension a viewer notices from across the room, and the one every release states. |
| Capped at 2160 | 2160 is the highest raster consumer displays ship in volume, and an uncapped axis makes an 8K remux an upgrade nobody can play. |
| **Origin second** — changed in pass 4 | D-7 moved the master/rip cliff onto this axis, so it is now the axis that carries "how many bits the master had". Pass 3 put `Generation` here and that is precisely what broke: one axis cannot hold a cliff for discs and an equivalence for streams. Origin second also restores three orderings the ladder has and pass 3 lost — `Raw-HD` above `HDTV`, `DVD` above `DVDRip`, and a BRRip above a broadcast capture of the same resolution. |
| **Generation third, ceiling-tied at one re-encode** | With the cliff on `Origin`, what is left to `Generation` is steps that are not cliffs. Tying {0, 1} is the `WEBDL ≈ WEBRip` equivalence stated as data rather than as an omission, and it leaves the axis live for the one step that *is* a real drop — a rip of a rip. A user who wants WEB-DL ranked above WEBRip removes the ceiling: **one chip, and the chip does exactly one thing.** |
| **Dynamic range moved out of precedence into the facet tier** | It is set-valued after §2.1 and therefore nominal, so `ARXQ005` forbids it in precedence; and D-8 names it as the archetypal facet. The behaviour pass 3's tied `Ranking` bought — not chasing a re-download between HDR10+, DV-with-fallback and DV — is bought here by scoring them equally at 15. What additionally improves: dynamic range can no longer outrank a REAL. |
| Mislabel fixes above corrections | A REAL says the previous file was the *wrong content*; a PROPER says it was a *worse encode of the right content*. Wrong content dominates. **This closes D-1** — see §6.5. |
| Repack absent from the list | A repack is the same encode packaged again. It is not a fidelity change and ordering on it makes the library re-download for nothing. |
| Codec, audio, distributor, frame rate, languages absent from precedence | Ordering on codec makes a library chase re-encodes, which is a downgrade wearing an upgrade's clothes. Audio matters enormously to some and not at all through a television's speakers. All five are one facet or one chip away, and none of them ships with points. |
| Refuse camera captures and workprints | A camcorder recording of a projection and an unfinished edit are not the film. |
| Refuse upscales — **withheld until QA-17** | An upscale claims a resolution it does not have, which corrupts the first axis in the list and therefore every decision that reads it. The argument is unchanged from pass 3; what changed is that pass 4 checked whether anything can see an upscale, found nothing can, and declined to ship the refusal until something does. |
| Refuse disc images and folders | Most players will not open them, and this is the "profile decision and not a ranking one" `MoviesLadder.cs:84-86` diagnosed in its own comment and then encoded as rung 29 anyway. **Now safe to ship**, because `MinimumSource` stops a per-kind heuristic from refusing a release that never claimed to be a disc (§3.2). |
| Good enough at 1080 lines with at most one re-encode | The point at which further upgrades cost bandwidth and gain little on a typical display, stated as two independent floors rather than as one rung of a cross-product. |

**Two things this default does not do, said out loud.**

1. **One of pass 3's three refusals refused values nothing can produce.** `VideoFlaw.Upscaled` and
   `VideoFlaw.Sample` have no scanner (§1.7), and — worse — the resolution scanner reads `4kto1080p`, the
   one upscale token the vocabulary knows, as a clean `r1080p`, laundering it. Pass 3's rationale row for
   that refusal (*"an upscale claims a resolution it does not have, which corrupts the first axis in the
   list"*) is exactly right and exactly unimplemented. **QA-17 is therefore on the critical path in front
   of QA-7**, and until it lands the refusal is withheld and recorded as pending rather than shipped
   present-and-inert. A policy that appears to refuse something and does not is worse than one that admits
   it cannot see it. The two refusals that remain are both readable today.
2. **Holding a `Bluray-1080p` means the Remux is never grabbed**, because the cutoff is satisfied at 1080
   lines and one re-encode. That is intended — `Compare` says `Better`, `Decide` says
   `AlreadyGoodEnough` — and it is the same behaviour a rung cutoff at `Bluray-1080p` gives today. A user
   who wants remuxes moves the `Generation` floor to zero, which is the second chip.

### 5.2 Music

Precedence: **Generation** (a transcode is the one unambiguous defect), then **Codec** with the lossless
members tied at the top (lossless is lossless; ALAC and FLAC are the same bits in different boxes), then
**Bitrate up to 320** (above 320 kbit/s a lossy encode is not distinguishable in listening tests, so the
ceiling stops a pointless chase). **Not ordered: sample depth and sample rate** — 24-bit/192 kHz is not
audibly different from 16-bit/44.1 kHz on playback; it is an archival preference and it is one chip away.
**Refuse** `AudioFlaw.Transcoded`. **Good enough** at `Codec ≥ Aac` and `Bitrate ≥ 256`, where the bitrate
floor uses `UnknownEvidence.Ignore` so a lossless copy — which by §2.2's definition has no bitrate reading
— satisfies it vacuously.

### 5.3 Written copies

Precedence: **Format**, then **Form**, then **Generation**.

**`Format` first, changed in pass 4, and the reason is not that format matters most — it is that format
is the only axis a written release states before it is downloaded.** `Form` needs the file open (a `.pdf`
may be page images or fixed-layout text; an `.epub` may contain scans) and `Generation` needs a conversion
audit, so pass 3's `[Form, Generation]` left **both** precedence axes absent for every candidate at grab
time: `Compare` returned `Same` for every pair and the acquisition engine had no basis to choose at all.
The declared `BookFormat` order is stated as an opinion in `Describe()` — *"preferring EPUB, then AZW3,
then MOBI, then PDF, which is this platform's guess and yours to re-order"* — and `Ranking` re-orders it in
one drag, which is the honest answer to §2.3's own critique rather than a re-run of it.

`Form` and `Generation` stay in the list beneath it, where they do real work **after import**: they are
what decides an upgrade once both files have been opened.

**Not ordered:** nothing, now. **Refuse** `WrittenFlaw.Drm` — a file the user cannot open is not a copy of
the book. **Good enough** at `Form ≥ ReflowableText`, which is a post-import condition and is therefore
never satisfied at grab time — deliberately, since a written copy's cutoff should not stop the search
before anyone has looked at the file.

**Recorded in §8:** written and spoken precedence is post-import-only until a probe exists, and the first
grab is decided by `Format` alone.

### 5.4 Spoken copies

Precedence: **Container** (a chaptered M4B is what an audiobook listener actually selects on, and it is
stated in the release), then **Generation**, then **Bitrate up to 128**. The ceiling is 128 rather than
music's 320 because speech is band-limited and a 128 kbit/s stereo encode of narration is transparent —
**the clearest single piece of evidence that quality belongs to the family rather than to the platform**,
since one number cannot be right for both. **Not ordered: codec.** **Refuse** `SpokenFlaw.Abridged` — an
abridged recording is different content under the same title. **Good enough** at
`Container ≥ M4b` and `Bitrate ≥ 64`.

`Container` leads for the same reason `Format` leads for written copies: it is the readable one. Pass 3
declared no container axis at all and then had §7 render `M4B 128kbps`, a string the model could not
produce.

---

## 6. Migration

### 6.1 What is deleted, and what replaces it

| Deleted | Lines | Replaced by |
|---|---:|---|
| `Plugin.Movies/Definition/MoviesLadder.cs` | 109 | `b.Format(StandardFormatFamily.Video)` — one call |
| `Abstractions/DTOs/QualityTier.cs` | 57 | `QualityPoint` |
| `QualityTier.Weight` / `.EffectiveWeight` / `.GroupName` / `.CompareTo` | — | **no successor.** Weight was one number carrying four opinions; group name was a tie the precedence list expresses by omission |
| `QualityTier.Min/Max/PreferredSizeMbPerMinute` | — | `SizeExpectation` (§4) |
| `Abstractions/DTOs/CutoffPolicy.cs` | 46 | `CutoffPredicate` inside `QualityPolicy` |
| `Shape/ProperHandling.cs` | — | the revision axes' presence in or absence from `Precedence` |
| `Shape/QualityRevision.cs` | 46 | three ordinary axes: `Corrections`, `Mislabels`, `Repacked` |
| `Definition/RungFallback.cs`, `TierDefault`, `CrossFamilyRule` | — | per-axis `UnknownEvidence`; cross-family comparison is unrepresentable |
| `Definition/QualityDeclaration.cs` | 35 | nothing — every member was a mode the axes dissolve |
| `Quality/IQualityModel.cs` | 39 | `IQualityType` (reads) + `QualityPolicy` (decides), split because they are different questions |
| `Host/Engines/Quality/DeclarativeQualityEvaluator.cs` | 250 | `AxisQualityEvaluator`, roughly half the size and with no family-membership scan |
| **`Host/Engines/Parsing/RungResolver.cs`** | **132** | **nothing.** It exists only to interpret the rung table and becomes dead with it. Omitted from pass 3's table entirely |
| `MoviesParsing.RungTable()` (101 rows) + `ContainerFallbacks` (7 rows) | ~130 | `VideoQualityType.Read` (§6.3), **~140 lines**, plus `MoviesVideoRefinement` (~40) |
| `Movies.cs` `b.Quality.IgnoreStatedResolutionFor(...).FallbackRoundUp()` | 3 | `EvidenceSource` precedence, per-axis `PreferenceUnknown`, and §3.2's provenance rule at the grab |
| `IFormatFamilyBuilder.Ladder(...)` | — | `.Quality<TFacts, TType>()` + `.RefinedBy<TFacts, TRefinement>()` |
| `BooksQualityModel`, `MusicQualityModel`, `TvQualityModel` cross-family guards | ~400 | **one runtime guard in `QualityPolicy`**, not a structural impossibility — §1.5 argument 2, corrected |

**Also touched, and omitted from pass 3's line counts** (F-A8): `FieldValueKind.Quality`'s consumers —
`NotificationMessage`, `FieldValueText` and `ShapeTokenDeriver` — each carry a tier payload that becomes a
`QualityPoint`. §6.2 names them in prose and the table did not count them; they are roughly 60 lines of
edits across the three and belong to QA-13 and Stage E, not to QA-12.

### 6.2 `QualityTier` has a wide blast radius; stage it

`QualityTier` is referenced from `MediaFileFacts`, `MediaFileRecord`, `FieldValue`/`FieldValueKind.Quality`,
`NotificationMessage`, `FieldValueText`, `ShapeTokenDeriver`, three unconverted kinds' shapes and their
tests. It cannot go in one commit without stopping the build, so:

* **Stage A** — the framework lands *beside* the ladder. Nothing consumes it. Build stays green.
* **Stage B** — `FormatFamily` gains an optional `IQualityType`; `AxisQualityEvaluator` lands beside
  `DeclarativeQualityEvaluator`. Both paths exist; the ladder is still authoritative.
* **Stage C** — a **parity harness** runs the whole Movies corpus through both evaluators and diffs the
  grab decisions *and the rendered labels* pairwise. Every divergence is triaged as *the ladder was
  wrong*, *the axes are wrong*, or *the two are equivalent and the corpus expectation was
  over-specified*, and every one of them is registered. Only then does Movies switch and
  `MoviesLadder.cs` delete. **This is how the data is deleted without deleting the behaviour**, and it is
  the only step in the plan that is not mechanical. **§6.6 states its pass criterion and names its
  triager** — pass 3 called it the gate and gave it neither, which makes it not a gate.
* **Stage D** — TV, Music and Books convert with their kinds. All three are imperative today
  (`open-decisions.md` Part 7), so their quality models convert when they do, not before.
* **Stage E** — `QualityTier`, `CutoffPolicy`, `QualityRevision`, `ProperHandling`, `RungFallback`,
  `CrossFamilyRule`, `QualityDeclaration` and `IQualityModel` delete in one commit, together with
  `FieldValueKind.Quality`'s tier payload, which becomes a `QualityPoint`.

### 6.3 The parse boundary, stated exactly

> **The parser produces evidence. It never names a rung, and after this change there is no rung to name.**
> Everything from a release title to a typed axis reading happens in `IQualityType.Read`, which is
> ordinary C# in the contract assembly, not a declaration table in a plugin.

What stays in `MoviesParsing.cs`, unchanged: `TitlePatterns`, `Guards`, `TokenTables`, `Normalization`,
`EditionRegex`. P2-5 survives exactly as Part 6 predicted; typing the model around regex does not remove
regex.

What leaves `MoviesParsing.cs`: `RungResolutionTable`, `RungRule`, `TierId`, `ExtensionTierRule`,
`ContainerFallbacks` — **101 rows plus 7**, not 130 plus 7. Each row's *evidence* half survives as a token
mapping; each row's *rung* half has nothing to map to.

| Rung-table rows | Becomes |
|---|---|
| `R("bluray-2160-remux", "Remux-2160p", Src("bluray"), Res(2160), Remux())` | nothing — `Origin = HighDefinitionDiscBitstream`, `Resolution = 2160` are two independent readings and their combination needs no row |
| `R("bluray-remux-nores", "Remux-1080p", Src("bluray"), Remux())` | nothing — an absent resolution stays absent; the policy's `WhenUnknown` decides, in one readable place, instead of 1080 being guessed here |
| the **4 `orphan-remux-*` rows** | **one family-level line**, new in pass 4: a remux token with no source token reads `Origin = HighDefinitionDiscBitstream` at `EvidenceSource.Assumed`, because a remux is by construction a disc bitstream copy. Pass 3 left these unmapped, and the consequence was that q139 and q141 — which the ladder reads as `Remux-2160p` — rendered `Unknown` and ranked at the floor |
| `R("hdtv-mpeg2", "Raw-HD", Src("hdtv"), G("mpeg2"))` | one family-level line: a broadcast source with `Codec = Mpeg2` is the transport stream itself, so `Origin = BroadcastBitstream`. **The per-kind `mpeg2` guard deletes** — the codec is typed evidence |
| the **2 `german-remux` rows** | one family-level line keyed on the typed `Languages` axis: a disc source with a dual-language marker (`DL`/`ML`) and no rip token reads `Origin = HighDefinitionDiscBitstream`. **The per-kind `german-remux` guard deletes** — this is why §2.1 declares `Languages` |
| `R("bluray-xvid", "Bluray-480p", Src("bluray"), G("xvid-divx"))` | one line: `Codec = Mpeg4Part2` implies `Generation ≥ 1`; the resolution reading is whatever the evidence said |
| the **12** `anime-bd-*` rows *(pass 3 said 13)* | two lines in `MoviesVideoRefinement` (§1.4a): the `anime-bd` guard sets `Origin = HighDefinitionDisc` at `Assumed`; `Remux()` promotes it to `HighDefinitionDiscBitstream` |
| the 7 `anime-web-*` rows | one line in `MoviesVideoRefinement`: `Origin = Stream`, `Generation = 0`, both at `Assumed` |
| the **11 container-evidence rows** (`res-web-*`, `res-disc-*`, `res-dvd-container`) | **kept, as a family-level rule over the typed `Container` member** — and pass 3's migration table was silent on them, which is the single largest omission in it. They are *not* fallbacks: they are ordinary rung rules whose own comment explains itself — *"a bare '540p' inside a Matroska file is a stream download, not a broadcast capture"*. A `.mkv`/`.mp4` container with no source token reads `Origin = Stream` **and** `Generation = 0`, both at `Assumed`; `.iso`/`.img` reads `Packaging = DiscImage`; `.vob`/`VIDEO_TS` reads `Origin = StandardDefinitionDiscBitstream`. **The three per-kind `container-*` guards delete.** Drop these instead and q029, q030, q031 and the anime `.mkv` conventions lose `Origin` entirely |
| the 5 `res-alone-*` rows | nothing — a resolution with no source is exactly `Resolution` known and `Origin` absent, which is the truth. §7 gains a label rule so the truth still renders the resolution instead of the string `Unknown` |
| the 13 `weak-*` rows | nothing, and **this is a stated loss.** `weak-x264` reads a bare `X264` with no source as `SDTV` — a positive inference the axes model declines to make, because an x264 with no source token is at least as likely to be a web rip. Those releases become `Origin` absent and rank at the floor |
| `ContainerFallbacks` (`.mkv → WEBDL-720p`) | **nothing at all**, and note this is a *different* thing from the 11 rows above: a fallback guesses a **resolution** the release never stated, where the container-evidence rows supply an **origin** for a release that did state its resolution. Guessing the resolution was wrong more often than not; an unknown reading is now representable, so there is nothing to guess |
| `IgnoreStatedResolutionFor("cam","ts","tc","wp","dvd")` | one line at read time — when `Origin = CameraCapture`, a title-stated resolution describes the camera — **plus §3.2's provenance rule at the grab**, without which deleting this list creates the re-download loop rather than fixing it |

Sketch of the replacement, to make the size claim checkable against §1.7's ~140-line estimate:

```csharp
public static VideoQuality Read(ReleaseEvidence e)
{
    var origin = OriginOf(e);                       // ~20 source tokens, the container, the language
                                                    //   markers, the codec, the remux token — ~55 lines
    var generation = GenerationOf(e, origin);       // a bitstream origin forces 0; a rip token → 1;
                                                    //   brrip → 2; otherwise absent — ~25 lines
    var resolution = ResolutionOf(e);               // the stated value and its claim form (§1.2),
                                                    //   dropped when the origin is a camera — ~25 lines
    // …one to four lines per remaining axis…
    return new VideoQuality { Origin = origin, Generation = generation, Resolution = resolution, … };
}
```

**What this table does not claim.** Pass 3 said *"the whole ranking cascade disappears and what is left is
a token-to-axis mapping"*. Half of that is true. The **ranking** cascade genuinely disappears — nothing
downstream of `Read` collapses anything into a name. The **inference** cascade does not: `GenerationOf`
still has to decide that BRRip is 2 and BDRip is 1 from tokens that today `SrcIn("bdrip","brrip")` treats
identically, and that is a token-keyed cascade living inside a method instead of inside a table. It is
smaller, it is ordinary C#, its order is local to one function, and it is testable without a rung — but it
is the same species, and pass 4 says so rather than counting it as gone.

### 6.4 D-3 — the corpus

The corpus rows assert a **rung name** (`Tier("q001", "…TSRip…", "TELESYNC")`). Under axes the expectation
becomes a tuple, which is strictly more informative — `q001` asserts
`Origin = CameraCapture, Audio = LossyStereo` rather than a name that bundles both.

Three things happen, and the third is a caveat rather than a fix.

1. **D-3's 60 unreadable rows stop being unreadable.** They fail today because a movie *title* declaration
   declines an episode-named release with no year. A quality expectation does not go through title
   parsing at all, so a quality corpus row and a title corpus row become genuinely separate assets — which
   is D-3's option 3 (*"a rung corpus is kind-agnostic"*) arrived at structurally. Under this model the
   quality corpus belongs to the **format family**, not to the kind, so it lives beside `VideoQuality` and
   is shared by Movies and TV. `ReadsEveryCorpusCaseOntoItsDeclaredRung` un-ignores as two tests over two
   corpora.
2. **The expectation column stops being derived from anyone else's vocabulary**, because it is written in
   our axes.
3. **The release-name column is unchanged, and that is the residual.** Release names are third-party
   strings the scene wrote, already sanitized to "Movie Name", but the *selection and arrangement* of
   which several hundred to ship is the surveyed project's test suite. Re-expressing the expectations does
   not touch that. If the corpus's provenance matters for the licence question, the answer is to grow our
   own from real indexer traffic, not to relabel this one — recorded here as an owner decision, not
   resolved. **This document is a design document and not legal advice.**

### 6.5 D-1 — closed as a policy question

D-1 asks whether `QualityRevision.CompareTo` should order `version → real → repack` (the contract) or
`real → version`, with repack not ordering at all (the surveyed rule). Part 6 already suspected it was not
a contract question. It is not:

* The contract's rule is the precedence list `[Corrections, Mislabels]` with `Repacked` absent.
* The surveyed rule is the precedence list `[Mislabels, Corrections]` with `Repacked` absent.
* Both are one line in a policy. Neither is in a contract. **The type system stops having an opinion.**

**Recommendation: ship the surveyed order as the default** (§5.1), on the merits stated there — a REAL
means the previous file was the wrong content and a PROPER means it was a worse encode of the right one.

**Pass 3's acceptance test is withdrawn. It did not discriminate, it was not the same text, and it omitted
the assertions that must go red.** All three problems, in ascending order of seriousness:

1. **It was not "every assertion text untouched".** The assertions are over
   `new QualityRevision(1, 1, false).CompareTo(new QualityRevision(9, 0, false))`, and `QualityRevision` is
   deleted by **QA-16** in this same plan. Re-pointing them at `VideoQuality.Default` rewrites every
   assertion *expression*; only the *messages* survive. Saying "untouched" of a rewritten expression was
   not true.
2. **It omitted the assertions that must go red.**
   `OrdersTheRevisionAxisTheWayTheContractStatesIt` (`UpgradeDecisionTests.cs:149`) is live, green, and
   asserts the **opposite** of both contested rules — `(9,0,false) > (1,1,false)` and
   `(2,0,true) > (2,0,false)`. D-1's register entry says the divergence is measured from both sides; pass
   3's table showed one. **Adopting the surveyed order deletes that test.** That is fine and it must be
   stated, because the framing was "no assertion is weakened".
3. **It did not discriminate.** All three target assertions pass under **D-1 option (a)** — a three-line
   edit to `QualityRevision.CompareTo` reordering `real` before `version` and dropping `isRepack`. A
   falsifier the status quo passes with a three-line change is not the acceptance test for a
   twenty-package redesign.

**The acceptance test for pass 4, stated so it is checkable and chosen so the status quo cannot pass it.**
Three groups, all things only a genuinely richer model can do:

| # | Assertion | Why the status quo cannot pass it |
|---|---|---|
| **A1** | Held `WEBRip-1080p`, candidate `WEBDL-1080p` → **not** an upgrade; **and** held `Bluray-1080p`, candidate `Remux-1080p` → an upgrade. Both on one policy, with no per-title rule. | This is R-A1's pair. The ladder passes it only by carrying two unrelated mechanisms (a shared weight plus an inserted rung); pass 3's model provably could not pass it at all. Passing it with one axis member and one ceiling is the whole of D-7. |
| **A2** | q050 (`…[HEVC][GB][4K]`) renders a string carrying `2160`; q139 and q141 render `Remux-2160p`; q030 renders a `WEBDL` string. | Under pass 3's §7 all four rendered the literal string `Unknown` and ranked at the floor. Requires the orphan-remux inference, the container-evidence rule and the bare-resolution label rule together. |
| **A3** | Import a release claiming `1080p`; probe measures 720; re-offer the identical release → **`NotAnUpgrade`**. | The provenance rule (§3.2). Neither the ladder nor pass 3 passes it — the ladder only by accident of `IgnoreStatedResolutionFor`, which §6.3 deletes. |
| **A4** | A policy whose precedence is `[Origin, DynamicRange, Audio]` with every axis absent on some candidate produces **no cycle** over the corpus: for every triple, the comparison is transitive. | Pass 3 admitted the three-point cycle in §3.1. This is a property test, not an example test, and it is cheap: 249 corpus points, sampled triples. |

And two live tests that **must be deleted**, listed here so their deletion is a decision rather than a
casualty:

* `OrdersTheRevisionAxisTheWayTheContractStatesIt` (`:149`) — asserts the contract's revision order, which
  D-1 closes against. Deleted with `QualityRevision`.
* Any assertion that `QualityTier.EffectiveWeight` orders `WEBDL` above `WEBRip` — there is none today,
  and `TreatsASidewaysMoveWithinAGroupAsNoUpgrade` (`:164`) survives untouched, which is worth checking
  rather than assuming.

If A1–A4 do not go green on the shipped default, the model is wrong and this document should be revisited
before any more of it is built.

### 6.6 Work packages

Partitioned so no two implementers write the same file.

| # | Package | Files | Depends on | Notes |
|---|---|---|---|---|
| **QA-1** | Framework core | `Abstractions/Quality/{QualityAxisId,FormatFamilyId,Evidence,EvidenceSet,EvidenceSource,AxisValue,AxisReading,QualityPoint,QualityAxis,AxisForm}.cs`; *edit* `ExperimentalContracts.cs` (+`Quality = "ARX0021"`) | — | §1.1–1.2, §1.6 |
| **QA-2** | Attributes + derivation rules | `Abstractions/Quality/Attributes/AxisAttribute.cs`; `Host/Quality/QualityAxisReader.cs` | QA-1 | §1.3 |
| **QA-3** | Type seams | `Abstractions/Quality/{IQualityFacts,IQualityTypeOfT,IQualityType,IQualityTypeBuilder,ReleaseEvidence,MediaProbe}.cs` | QA-1..2 | §1.4, §1.7 |
| **QA-4** | Policy — core tier | `Abstractions/Quality/{QualityPolicy,AxisPreference,PreferenceUnknown,AxisRequirement,UnknownEvidence,CutoffPredicate,AxisFloor,QualityJudgement,GrabDecision,IQualityPolicyBuilder}.cs` | QA-1 | §3.1–3.4. Pure functions; no host dependency. **Ships with §3.1's transitivity property test** |
| **QA-5** | Policy prose renderer | `Abstractions/Quality/QualityPolicyDescription.cs` | QA-4, QA-19 | §3.5. Separate file, separate implementer — it is the UI's whole subject |
| **QA-6** | Size model | `Abstractions/Quality/{SizeExpectation,SizeVerdict}.cs`; `Abstractions/Quality/Families/VideoSizeModel.cs` | QA-1 | §4. Ships with the cross-check table **and the absent-input table** as tests |
| **QA-7** | Video family | `Abstractions/Quality/Families/{VideoQuality,VideoQualityType,VideoLabels,VideoDefaults}.cs` | QA-1..6, **QA-17**, QA-19 | §2.1, §5.1, §7 |
| **QA-8** | Audio, written, spoken families | `Abstractions/Quality/Families/{AudioQuality,WrittenQuality,SpokenQuality}*.cs` | QA-1..6 | §2.2–2.3, §5.2–5.4 |
| **QA-9** | Builder surgery | *edit* `Abstractions/Media/Builders/IStructureBuilders.cs` (delete `Ladder`, add `Quality<>` and `RefinedBy<>`), `IMediaTypeBuilder.cs` (add `Format(StandardFormatFamily)`); *edit* `Shape/FormatFamily.cs` — **and the `FamilyId` type conversion**: `FormatFamily.FamilyId` is `string` today (`FormatFamily.cs:28`), as are `MediaLevel.FormatFamilyIds` and `ItemView.FormatFamilyId`, while every signature in this document uses `FormatFamilyId` | QA-3 | §1.5. **Breaks Tv/Music/Books' shapes — expected, staged at 6.2 D.** Three more call sites than pass 3's table showed |
| **QA-10** | Host evaluator | `Host/Quality/AxisQualityEvaluator.cs`, `QualityTypeFactory.cs`, `QualityRefinementPipeline.cs`; *edit* `Host/Media/Typed/MediaTypeModelFactory.cs` | QA-1..9, QA-20 | Stage B |
| **QA-11** | Parity harness | `Host.Tests/Quality/LadderAxisParityTests.cs`; `docs/design/quality-divergences.md` | QA-10 | **Stage C, and the gate on deleting anything.** Pass criterion and triager below |
| **QA-12** | Movies switch | *edit* `Plugin.Movies/Movies.cs`, `Definition/MoviesParsing.cs`; *delete* `Definition/MoviesLadder.cs`; *edit* `Definition/MoviesCorpus.cs` | QA-11 | §6.3–6.4 |
| **QA-13** | Token binding | *edit* `Host/Engines/Naming/ShapeTokenDeriver.cs`, `NotificationMessage.cs`, `FieldValueText.cs` | QA-7 | §7. Acceptance is **byte-identical on the enumerated set plus the registered divergence list**, not byte-identical everywhere — §7 says which and why |
| **QA-14** | User axes | `Abstractions/Quality/{UserAxis,UserAxisMember,EvidenceMatch}.cs`; `Host/Quality/UserAxisEvaluator.cs` | QA-4 | §3.6. Ordinal, not nominal |
| **QA-15** | Analyzer rules | *edit* `Arronix.Analyzers/TypedMediaModelAnalyzer.cs` | QA-2 | `ARXQ001`–**`ARXQ006`** (§1.3). Ships **before** QA-7, or the guarantees are load-time again |
| **QA-16** | Old surface deletion | *delete* `DTOs/{QualityTier,CutoffPolicy}.cs`, `Shape/{QualityRevision,ProperHandling}.cs`, `Definition/{RungFallback,QualityDeclaration,TierDefault,CrossFamilyRule}.cs`, `Quality/IQualityModel.cs`, `Host/Engines/Quality/DeclarativeQualityEvaluator.cs`, `Host/Engines/Parsing/RungResolver.cs` | QA-12, and Tv/Music/Books converted | **Stage E.** Do not start it early |
| **QA-17** | **Host scanner additions — new, and blocking** | *edit* `Host/Engines/Parsing/ReleaseTagScanner.cs`, `ReleaseTokenVocabulary.cs`, `ReleaseCodecScanner.cs`; new `ReleaseDynamicRangeScanner.cs`, `ReleaseFlawScanner.cs`, `ReleaseDistributorScanner.cs` | — | Dynamic range; distributor capture (AMZN/NF/DSNP are an uncaptured lookahead today); flaw markers (upscale, sample, hardsub, watermark); scan type; frame rate; packaging tokens; `h266`/VVC; the `WxH` raster form; **un-bucketing the resolution scan** so `1080i`, `1440p` and `4kto1080p` stop collapsing onto 1080. §1.7, §5.1 |
| **QA-18** | **Live preview + profile editor — new** | client-side; `Abstractions/Quality/QualityPreview.cs` for the host half | QA-5, QA-7 | §3.5. **Ships in the same release as the editor.** Community spelling on every chip; no raw magnitude on a `Descending` axis |
| **QA-19** | **Facet tier — new** | `Abstractions/Quality/{FacetScoring,FacetScore}.cs`; *edit* `IQualityPolicyBuilder` | QA-4 | §3.7, D-8. Ships with the composed-preorder property test |
| **QA-20** | **Per-kind evidence refinement — new** | `Abstractions/Quality/IQualityRefinement.cs`; `Plugin.Movies/Definition/MoviesVideoRefinement.cs` | QA-3 | §1.4a. Without it, Movies' guard strings end up in the contract assembly |
| **QA-21** | **Language axis wiring — new** | *edit* `Host/Engines/Parsing/ReleaseLanguageScanner.cs` to surface `LanguageClaim`; `Abstractions/Quality/LanguageClaim.cs` | QA-1 | §2.1. Small, and QA-7's German-remux rule depends on it |

**Critical path:** QA-1 → QA-2/3/4 → QA-17 → QA-19 → QA-6 → QA-7 → QA-9 → QA-20 → QA-10 → QA-11 →
QA-12 → QA-13. QA-8, QA-14, QA-15, QA-18 and QA-21 are parallel (QA-21 before QA-7's German rule can be
tested). **QA-17 moved onto the critical path in front of QA-7** because the default policy in QA-7
refuses two things QA-17 has to be able to see, and pass 3 had no package for it at all. QA-11 is the one
gate that must not be skipped; QA-15 is the one most likely to be.

#### QA-11's pass criterion, and who triages a divergence

Pass 3 called QA-11 *"the gate on deleting anything"* and stated no threshold, which makes it not a gate.
The sibling `clean-room-plan.md` states 99% / 97% for its analogous gate; a percentage alone is the wrong
instrument here, because the interesting divergences are the ones this design *intends*, and a percentage
cannot tell an intended divergence from a regression. So the criterion is a register plus two numbers:

> **The gate.** Over the full Movies quality corpus, run every case through both evaluators and diff the
> grab decision *and* the rendered `{Quality Title}` / `{Quality Full}`.
>
> 1. **100% of cases are identical or carry an entry in `docs/design/quality-divergences.md`.** An
>    unregistered divergence fails the build. The register is checked in, and each entry names the case,
>    both answers, the class, and the section of this document that decided it.
> 2. **The register holds at most 40 entries.** A cap is what stops the escape hatch swallowing the test.
>    §10 currently projects **8** on the sampled thirty, so 40 across 249 is generous and still bounded.
> 3. **≥ 97% identical with no register entry at all.** The register explains the residue; if the residue
>    is a fifth of the corpus the model has not been validated, it has been described.
> 4. **Zero divergences in the over-claim direction.** A case where the axes grab and the ladder does not,
>    *and* the deciding axis's reading is at `EvidenceSource.Assumed` or weaker, fails outright and is not
>    registrable. Over-claiming promotes junk into the library; under-claiming leaves it `Unknown`.
> 5. **A1–A4 of §6.5 green**, including the re-grab regression.

**Triage.** Every divergence is triaged into one of three classes — *the ladder was wrong*, *the axes are
wrong*, *the two are equivalent and the corpus expectation was over-specified*. The triager is the
**Verify agent**, not an implementer, for the same reason it owns the harness: an implementer triaging
their own divergences is grading their own homework. **Any divergence classified *the axes are wrong* is
escalated to the owner and blocks QA-12 until it is resolved** — that class is the only one that can
require a design change, and it is exactly the class an implementer under schedule pressure would
re-classify. The other two classes the Verify agent closes on its own, with the register entry as the
record.

---

## 7. Display naming

Community vocabulary must survive. `{Quality Title}` must still render `WEBDL-1080p` and `{Quality Full}`
must still render `WEBDL-1080p Proper`, byte for byte, or every existing library's file names churn.

**The invariant that keeps rungs from growing back:**

> A label is produced from a point and is never read back for a comparison. No `Compare`, no `Admits`, no
> `IsGoodEnough` and no `Assess` anywhere touches a rendered string.

`TryParseLabel` exists for exactly two callers — reading a stored string during migration, and accepting a
label a user typed or pasted — and both convert to a point immediately.

```csharp
/// <summary>One rendering rule: a predicate over a point, and the word it renders.</summary>
/// <param name="When">What must hold, over the erased point.</param>
/// <param name="Label">The community's word for it.</param>
/// <remarks>
/// <para>
/// The predicate is a delegate rather than declared data, deliberately. It reads two or three axes at
/// once — <c>Origin = Stream and Generation = 0</c> — and a declared-data form of that is exactly the
/// predicate micro-grammar P2-2 recorded, rebuilt for rendering. A media kind never writes one; only a
/// family does, or a kind overriding its family's rendering (§7, TV), and both are host-side code.
/// </para>
/// <para>
/// <b>The authoring form and the runtime form are different types, and pass 3 left them disagreeing.</b>
/// <c>IQualityTypeBuilder.Label</c> takes an <c>Expression&lt;Func&lt;TFacts,bool&gt;&gt;</c> over the
/// typed facts; the host rewrites it onto <c>QualityPoint</c> and stores the result here as a compiled
/// <c>Func</c>. Pass 3 declared a bare <c>Func&lt;TFacts,bool&gt;</c> at the builder and asserted it was
/// "compiled against the point", which a compiled delegate cannot be. §1.4 carries the corrected
/// signature.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityLabelRule(Func<QualityPoint, bool> When, string Label);

/// <summary>How much of a point to spell.</summary>
public enum QualityLabelDetail
{
    /// <summary>The source word alone: <c>WEBDL</c>.</summary>
    Source = 0,

    /// <summary>Source and resolution: <c>WEBDL-1080p</c>. What <c>{Quality Title}</c> renders.</summary>
    Standard = 1,

    /// <summary>Standard plus the revision: <c>WEBDL-1080p Proper</c>. What <c>{Quality Full}</c> renders.</summary>
    Full = 2,

    /// <summary>Every known axis, for a diagnostic view: <c>Stream · 1080 lines · untouched · HDR10 · Atmos</c>.</summary>
    Diagnostic = 3,
}
```

**The video rules, in order; the first whose predicate holds wins.**

| # | Predicate | Label |
|---:|---|---|
| 1 | `Packaging ∈ {DiscImage, DiscFolder}` | `BR-DISK` |
| 2 | `Origin = HighDefinitionDiscBitstream` | `Remux` |
| 3 | `Origin = HighDefinitionDisc` | `Bluray` |
| 4 | `Origin = Stream, Generation = 0` | `WEBDL` |
| 5 | `Origin = Stream, Generation ≥ 1` | `WEBRip` |
| 6 | `Origin = Stream` (generation absent) | `WEBDL` |
| 7 | `Origin = BroadcastBitstream` | `Raw-HD` |
| 8 | `Origin = Broadcast, Resolution ≥ 720` | `HDTV` |
| 9 | `Origin = Broadcast` | `SDTV` |
| 10 | `Origin ∈ {StandardDefinitionDiscBitstream, StandardDefinitionDisc}` | `DVD` |
| 11 | `Origin = FilmPrint` | `TELECINE` |
| 12 | `Origin = CameraCapture, Audio ≥ LossyStereo` | `TELESYNC` |
| 13 | `Origin = CameraCapture` | `CAM` |
| 14 | `Origin = Workprint` | `WORKPRINT` |
| **15** | **`Origin` absent, `Resolution` known** | **the resolution alone: `2160p`, `540p`** |
| 16 | nothing known | `Unknown` |

Four of those rows are new or changed in pass 4, and each is a finding:

* **Rows 2, 3, 7 and 10 read the D-7 origin members instead of an `Origin` + `Generation` pair.** Simpler,
  and it is the label table that reveals the model was right: `Remux` and `Raw-HD` were always *origins*
  in the community's vocabulary, and only the ladder's single ordering forced them to be modifiers.
* **Row 6 exists because a generation-absent stream reading is the common case**, not an error. The 11
  container-evidence rules (§6.3) supply `Generation = 0` at `Assumed` so it rarely fires, but a bare
  `WEB` token with nothing else must still render `WEBDL`, which is what the ladder does.
* **Row 10 joins two origin members onto one word.** The community has one word for a DVD and the ladder
  has one rung; the model splits `StandardDefinitionDiscBitstream` from `StandardDefinitionDisc` because
  the bitrates differ by 2.8× (§2.1), and the renderer joins them back because nobody writes `DVDRip` as a
  quality name. **The axis model splits where physics splits; the label table joins where the community
  joins**, and this row is the proof that the two can disagree without either being wrong.
* **Row 15 is the fix for pass 3's largest rendering defect.** Every rule in pass 3's table keyed on
  `Origin` or `Packaging`, so `{Origin = ∅, Resolution = 2160}` fell to `Unknown` — and the suffix rule
  did not apply to `Unknown`, so *the known resolution was not even appended*. That is 5 `res-alone-*`
  rows + 4 `orphan-remux-*` rows + 11 container-evidence rows = **20 of 101 rung rules whose entire output
  today is a nameable rung and whose output under pass 3 was the string `Unknown`**. Pass 4 recovers most
  of them by reading the evidence properly (§6.3); row 15 catches the genuine remainder and renders the
  truth instead of discarding it.

**The suffix set, enumerated rather than described as a range.**

> `Remux`, `Bluray`, `WEBDL`, `WEBRip`, `HDTV` take `-{lines}p` when the resolution is known.
> **Nothing else does** — not `Raw-HD`, not `SDTV`, not `DVD`, not `BR-DISK`, and not
> `CAM`/`TELESYNC`/`TELECINE`/`WORKPRINT`.

Pass 3 said *"every row from `Bluray` down to `SDTV`"*, which is wrong in **both** directions: it includes
`Raw-HD`, `SDTV` and `DVD`, producing `Raw-HD-1080p` (q164, q165), `SDTV-480p` and `DVD-480p` where
`MoviesLadder.cs` ships `Raw-HD`, `SDTV` and `DVD`. Checked against the ladder's 30 rung names, the
suffixed set is exactly the five above.

At `Full` detail, ` Proper` is appended when **`Corrections > 0`** (base-0 after §2.1 — pass 3 said
`> 1`, from the base-1 reading it had already contradicted twice) and ` REAL` when `Mislabels > 0`,
space-joined with empty parts elided.

#### Truthful-but-novel labels: the renderer's stance, stated

Three labels this table produces that the ladder deliberately suppresses:

| Point | Renders | Ladder ships | Ladder's reason |
|---|---|---|---|
| `HighDefinitionDiscBitstream`, 480 lines (q037) | `Remux-480p` | `Bluray-480p` | — |
| `HighDefinitionDiscBitstream`, 720 lines (q101, q102) | `Remux-720p` | `Bluray-720p` | *"a 720p remux is not a thing"* (`MoviesParsing.cs:259-260`) |
| `Stream`, 540 lines (q030) | `WEBDL-540p` | `WEBDL-480p` | the resolution scanner buckets 540 → 480 |

> **The stance: render what the point says. Do not suppress a truthful label to match a rung that did not
> exist.**

Three reasons, and one cost paid rather than hidden:

1. **The point is true.** q037's title is literally `…BluRay.480i…AVC.REMUX-FraMeSToR`. It says it is a
   bitstream copy at 480 lines, and it is one. `Bluray-480p` was not a correction, it was the nearest rung
   in a taxonomy with nowhere to put the truth.
2. **The ladder's reason is a *plausibility* judgement, and §4 now makes plausibility a computation.** "A
   720p remux is not a thing" is a claim about bitrate, and the size gate tests it directly: a genuine
   720p bitstream copy is ~11 Mbit/s and a group lying about a 2 Mbit/s encode fails the floor with a
   reason the user can read. A computed check on the real file beats a hard-coded rename that also
   silences the honest case.
3. **It costs nothing in the library**, because there is no legacy upgrade path by decision
   (`open-decisions.md`; §8 QA-i) and the library is empty until the storage milestone. If that changes,
   this stance is the first thing to revisit.

**The cost, paid explicitly: QA-13's acceptance changes.** Pass 3 said *"`{Quality Title}` /
`{Quality Full}` must render byte-identical strings; that is the test"*, and with these three rows plus
row 15 it cannot pass as written. The criterion becomes:

> **Byte-identical on every case not listed in `docs/design/quality-divergences.md`, and the register is
> the same one QA-11 gates on.** Each label divergence carries the rule number above it, the ladder's
> string, the new string, and the section that decided.

**One property the ladder could not have, stated more carefully than pass 3 stated it.**

Pass 3 claimed the mapping is **total** and offered `{Origin = Stream, Resolution = 1440}` → `WEBDL-1440p`
as the showcase. The mapping is total *over the axis space* — rows 15 and 16 close it, which pass 3's did
not — but the showcase was unreachable: `ScanResolution` folds `1440p` onto 1080, so **no evidence path
produced 1440** and the example string could not be rendered by any input. QA-17 un-buckets the scan and
makes the showcase real. Totality is a property of the table; reachability is a property of the scanners,
and pass 3 asserted the second while demonstrating neither.

`Watermarked` and `Screener` labels remain *additive*: `DVDSCR` renders as `DVD` plus the flaw, so
`Diagnostic` shows the burn-in and `Standard` still shows `DVD` for a filename.

**TV overrides this table**, which is the label half of §1.5's withdrawn "zero lines": `TvShape.cs` ships
`Bluray-1080p Remux` and `Bluray-2160p Remux` where Movies ships `Remux-1080p` and `Remux-2160p`. One
shared rule list cannot render both, so the TV kind supplies a replacement for rows 2 and 3 and inherits
the rest. That is a real, small cost and it is now counted.

Other families render in their own vocabularies from the same mechanism: `FLAC 24bit`, `MP3-320`,
`AAC-256` for music (matching what users already type, without importing anyone's ladder); `EPUB`,
`PDF (scanned)` for written copies; `M4B 128kbps` for spoken — the last of which is only renderable
because §2.3 now declares a `SpokenContainer` axis for it to read.

---

## 8. Deferred, honestly

| | What | Why it is not built |
|---|---|---|
| **QA-a** | **A `Scan` axis** (progressive/interlaced/telecined). `Interlaced` rides as a flaw (§2.1). | One axis is cheap to add later and the flaw carries the observable. If the flaw proves to be doing ordering work, promote it. |
| **QA-b** | **Per-item overrides.** A user who wants one film at 2160 and the rest at 1080 holds two profiles. | The profile↔item binding is storage-milestone work and is orthogonal to the model. |
| **QA-c** | **Scalar equivalence bands** — "1080 and 1440 are the same to me". `Ceiling`/`Floor` cover the common cases. | `AxisPreference.Bands` is a one-property addition; nothing else changes. Not built because no shipped default needs it. |
| **QA-d** | **`Abridged` as a distinct work** rather than a refused flaw (§2.3). | Needs a catalog change; the flaw gets the right behaviour today. |
| **QA-e** | **Frame rate as an ordering axis.** It is declared and used by the size model, and absent from every default precedence. | Sports viewers genuinely prefer 60 fps; nobody has asked yet, and it is one chip away when they do. |
| **QA-f** | **Indexer flags** (freeleech, internal, scene) — explicitly *not* quality (§3.6). | They belong to release selection beside seeders and age. Recorded so nobody adds them here later. |
| **QA-g** | **A dedicated `Arronix.Media.Formats` assembly** instead of `Abstractions/Quality/Families/`. | The client references `Abstractions` only and needs labels and axis descriptors, so the contract assembly is where they have to be reachable from. Revisit if the assembly's size becomes a real problem rather than an aesthetic one. |
| **QA-h** | **A `[Display]` source generator** reading XML doc comments, so an axis's prose is not duplicated. | Same C7 the typed media model records. One generator would serve both. |
| **QA-i** | **Migrating existing `MediaFileRecord.Quality` rows.** | There is no legacy upgrade path by decision (`open-decisions.md`, unified-host memory), and the library is empty until the storage milestone. If that changes, `TryParseLabel` is the migration. |
| **QA-j** | **A cross-axis exchange rate** — "I prefer 2160p, but I would rather have a 1080p Remux than a 2160p WEBRip". | **The one capability a scalar weight had that this model does not**, and D-8 deliberately declines to restore it in the core (§3.6). The construct that would close it is a bounded `AxisPreference.Significance` — an integer magnitude, not a rank, used only when two adjacent precedence entries disagree — and it is a scalar coming back by another door. It is not built because L-1's real defect was that the scalar was **baked into kind data**, not that a scalar existed, and re-opening that question is worth doing deliberately rather than in an amendment pass. The partial answer available today is the cutoff formulation in §3.6. |
| **QA-k** | **Item-level completeness across coexisting families.** *"I hold the audiobook; am I still missing the book?"* | `CrossFamilyRule.NeverCompare` at least *named* this question and §1.5 deletes it without answering. `MediaLevel.FormatFamilyIds` is a list (`MediaLevel.cs:70-72`) and `FormatFamily.CoexistsWithOtherFamilies` exists precisely for this case, so the question is live. `IsGoodEnough` is per-family and item completeness is not. **Recommendation when it is built:** per-family cutoffs unchanged, plus an item-level "every coexisting family satisfied" rule owned by the **host**, not by either quality type — which keeps §1.5's family boundary intact and puts the aggregation where the item lives. |
| **QA-l** | **`Hybrid` releases** — a single encode assembled from two sources (corpus q088). | It is neither one `Origin` nor two, and modelling it needs either a set-valued `Origin` (which would make the axis nominal and destroy the core tier) or a `Sourcing` flaw. A user axis carries it today. Revisit if hybrids stop being a minority. |
| **QA-m** | **A `Scan` axis** promoted from `VideoFlaw.Interlaced`. | Unchanged from QA-a, but now with a precondition: QA-17 must first stop the resolution scanner from destroying the `i`/`p` distinction before `Read` sees it. Until then the flaw is not merely unpromoted, it is unreadable. |

---

## 9. Open questions for the owner

1. **§1.5** — the sharpening. Quality belongs to the **format family**, not to the media kind, so Movies
   and TV share `VideoQuality`. This is a deliberate departure from the brief's wording and everything
   downstream rests on it. Note that the *second* argument for it — Books' split being structural — is
   **withdrawn** in this pass; the family boundary is now defended on drift and on the file/family
   correspondence alone, which the resolution table still supports. Confirm or reject before QA-1.
2. **§5.1** — the shipped default policy for video is *our stated opinion* and will be argued with. Five
   precedence entries, one facet, three refusals and two floors: is that the list we want to defend, or is
   there a row to add or drop?
3. **§6.4** — the corpus. Re-expressing the expectation column in our axes is clearly right. Whether the
   *selection* of release names needs replacing is a licence judgement, not a design one, and is
   deliberately left to the owner.
4. **§6.5** — D-1's recommendation (ship the surveyed revision order as the default, delete the contract's
   opinion). Confirm, and D-1 closes. Note the two live tests this deletes, now listed there.
5. **§2.1 — one question, asked twice, and the tree answers it two different ways today.** `VideoOrigin`
   places `Broadcast` **and** `BroadcastBitstream` below `Stream`, which means `Raw-HD` ranks below a
   WEB-DL of the same resolution. That is `TvShape.cs`'s answer (`Raw-HD` at rank 11, below
   `WEBRip-1080p`) and the opposite of `MoviesLadder.cs`'s (rank 30, top of the ladder, with its own
   comment calling the position *"a profile decision and not a ranking one"*). The two sub-questions are
   the same question:
   - is a broadcast capture below a stream capture? (interlacing, logos and ad-breaks say yes; bitrate
     says no)
   - is an untouched transport stream below a stream capture? (same trade, larger bitrate gap)

   Confirm the ordering, or ship `BroadcastBitstream` and `Stream` tied. Whichever is chosen, TV and
   Movies must stop disagreeing, and the label override in §7 is where the divergence would otherwise
   hide.
6. **§7, new** — the renderer's stance on truthful-but-novel labels (`Remux-480p`, `Remux-720p`,
   `WEBDL-540p`). The document argues for rendering the truth and paying the QA-13 divergence, on the
   grounds that the size gate now tests the plausibility the ladder was asserting by rename. This is the
   one place pass 4 chooses a **new** behaviour over parity, and it is small and reversible: one label
   rule collapses `Remux` below 1080 lines back to `Bluray` if the owner prefers parity.
7. **§3.6 / §8 QA-j, new** — the exchange rate. D-8 confines scoring beneath the core, which means the
   "1080p Remux over 2160p WEB" profile shape does not migrate as an ordering, only as a cutoff. Confirm
   that this loss is acceptable, or re-open the bounded-`Significance` question before QA-4 rather than
   after.

---

## 10. The reality test, re-run against pass 4

The critique's method is repeated verbatim here rather than summarized, because a claim that a redesign
fixed something is only worth what its evidence is. Thirty release shapes, 28 taken verbatim from
`MoviesCorpus.cs`. For each: what the ladder produces today, what the **amended** model produces, and
whether it maps.

Legend: **✓** maps cleanly · **△** maps, but changes today's answer or label · **✗** does not map, or maps
only via material this document has not specified.

| # | Title (corpus id) | Ladder answer | Pass-4 axes reading | Pass-4 label | V |
|---|---|---|---|---|---|
| 1 | `Movie.Name.2011.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR` (q037) | `Bluray-480p` | Origin=HDDiscBitstream, Res=480, Flaws={Interlaced} | **`Remux-480p`** | △ registered (§7) |
| 2 | `Movie.Hunter.2018.720p.Blu-ray.Remux.AVC.FLAC.2.0-SiCFoI` (q101) | `Bluray-720p` | Origin=HDDiscBitstream, Res=720 | **`Remux-720p`** | △ registered (§7); size gate now tests the claim |
| 3 | `This.Wonderful.Movie.1991.German.ML.1080p.BluRay.AVC-GeRMaNSCeNEGRoUP` (q133) | `Remux-1080p` via the per-kind `german-remux` guard | Origin=HDDiscBitstream via the **family** rule over `Languages` (§6.3); guard deletes | `Remux-1080p` | ✓ *(needs QA-21)* |
| 4 | `Movie.Title.1956.German.DL.2160p.HDR.UHDBDRip.h266-GROUP` (q123) | `Bluray-2160p` | Origin=HDDisc, Gen=1, Res=2160, Codec=**H266**, DR={HDR10} | `Bluray-2160p` | ✓ *(needs QA-17)* |
| 5 | `[NOGRP][国漫][诛仙][Movie Title 2022][19][HEVC][GB][4K]` (q050) | `HDTV-2160p` | Res=2160 (marketing form), Origin absent — no source token, no container | **`2160p`** (label rule 15) | △ the ladder guessed `HDTV`; pass 3 rendered `Unknown` |
| 6 | `Movie.Title.2016.T1.UHDRemux.2160p.HEVC.Dual.AC3.5.1-TrueHD.5.1.Sub` (q139) | `Remux-2160p` | orphan-remux inference: Origin=HDDiscBitstream @`Assumed`, Res=2160 | `Remux-2160p` | ✓ |
| 7 | `Movie Name (2021) [Remux-2160p x265 HDR 10-BIT DTS-HD MA 7.1]-FraMeSToR.mkv` (q141) | `Remux-2160p` | as #6 | `Remux-2160p` | ✓ |
| 8 | `Movie.Title.2015.Open.Matte.1080i.HDTV.DD5.1.MPEG2` (q164) | `Raw-HD` | broadcast + `Codec=Mpeg2` → Origin=BroadcastBitstream; Res=1080; Flaws={Interlaced} | `Raw-HD` (**unsuffixed**) | ✓ *(needs QA-17 for the `i`)* |
| 9 | `[SubsPlease] Movie Title (540p) [AB649D32].mkv` (q030) | `WEBDL-480p` | container `.mkv` → Origin=Stream, Gen=0, both @`Assumed`; Res=**540** un-bucketed | **`WEBDL-540p`** | △ 540 is the truth; the ladder bucketed |
| 10 | `[HorribleSubs] Movie Title! 2018 [Web][MKV][h264][480p][AAC 2.0][Softsubs]` (q029) | `WEBDL-480p` via `anime-web` | `[Web]` token → Origin=Stream, Gen=0, Res=480; `anime-web` in `MoviesVideoRefinement` as backstop | `WEBDL-480p` | ✓ *(needs QA-20)* |
| 11 | `[FFF] Movie Name - 01 [BD][720p-AAC][0601BED4]` (q094) | `Bluray-720p` | Origin=HDDisc via `BD(?!$)`, Res=720, Gen absent | `Bluray-720p` | ✓ |
| 12 | `[Coalgirls]_Movie!!_01_(1920x1080_Blu-ray_FLAC)_[8370CB8F].mkv` (q107) | `Bluray-1080p` | Origin=HDDisc, Res=1080 from the raster form | `Bluray-1080p` | ✓ *(needs QA-17's `WxH`)* |
| 13 | `[coldhell] Movie v2 [BD1080p][5A45EABE].mkv` (q106) | `Bluray-1080p` | Origin=HDDisc, Res=1080, **Corrections=1** (`v2` → Version 2 − 1) | `Bluray-1080p`; `Full` → `Bluray-1080p Proper` | ✓ — `QualityRevision` already orders on version today |
| 14 | `Movie.Title.2013.BDISO` (q145) | `BR-DISK` (rung 29, grabbable) | Packaging=DiscImage from an explicit token @`ReleaseTitle` | **refused** | △ intended (L-6) |
| 15 | `Movie Title 2005 1080p USA Blu-ray AVC DTS-HD MA 5.1-PTP` (q152) | `BR-DISK` | `br-disk` heuristic contributes Packaging @`Assumed`; refusal's `MinimumSource` blocks it → Origin=HDDisc, Res=1080 | `Bluray-1080p` | △ **the silent refusal is gone** (§3.2) |
| 16 | `The German 2021 Bluray AVC` (q160) | `BR-DISK` (a *title* triggering the disc guard) | as #15; Res absent | `Bluray` | △ a false refusal removed |
| 17 | `Movie.Title.2019.German.DL.1080p.HDR.UHDBDRip.AV1-GROUP` (q114) | `Bluray-1080p` | Origin=HDDisc (rip token present, so the DL marker does not promote), Gen=1, Res=1080, Codec=Av1, DR={HDR10} | `Bluray-1080p` | ✓ |
| 18 | `Movie.Title.2012.German.DL.1080p.UHD2BD.x264-QfG` (q112) | `Bluray-1080p` | Origin=HDDisc, Gen=1, Res=1080 | `Bluray-1080p` | ✓ |
| 19 | `Movie Name 2005 1080p UHD BluRay DD+7.1 x264-LoRD.mkv` (q109) | `Bluray-1080p` | two claims at one source; §1.2's within-source rule: `LineCount` beats `MarketingName` → Res=1080 | `Bluray-1080p` | ✓ — the rule is now stated, not hard-coded |
| 20 | `Movie.Title.2005.1080p.HDDVDRip.x264` (q113) | `Bluray-1080p` | Origin=HDDisc, Gen=1, Res=1080 | `Bluray-1080p` | ✓ |
| 21 | `Movie.Title.2019.1080p.AMZN.WEB-Rip.DDP.5.1.HEVC` (q081) | `WEBRip-1080p` | Origin=Stream, Gen=1, Res=1080, Distributor=AMZN | `WEBRip-1080p` | ✓ *(label is right today; `Distributor` empty until QA-17)* |
| 22 | `Movie.Name.2024.German.AC3D.DL.2160p.Hybrid.WEB.DV.HDR10Plus.HEVC-GROUP` (q088) | `WEBDL-2160p` | Origin=Stream, Gen=0, Res=2160, **DR={DolbyVision, HDR10Plus}** as an `EvidenceSet`; facet scores max(15,15)=15 | `WEBDL-2160p` | ✓ — `Hybrid` still unmodelled (§8 QA-l) |
| 23 | `Movie.Title.2020.MULTi.1080p.WEB.H264-ALLDAYiN (S:285/L:11)` (q073) | `WEBDL-1080p` | Origin=Stream, Gen=0, Res=1080, Languages={multi} | `WEBDL-1080p` | ✓ |
| 24 | `Movie.Name.2020.1080p.AMZN.WEB…` (q071) | `WEBDL-1080p` | same | `WEBDL-1080p` | ✓ |
| 25 | `Movie.Name.S04E01.iNTERNAL.1080p.WEBRip.x264-QRUS` (q079) | `WEBRip-1080p` | Origin=Stream, Gen=1; `iNTERNAL` correctly leaves quality (§3.6) | `WEBRip-1080p` | ✓ |
| 26 | `Some Movie 2018 2160p_HD_TV` (s020) | `HDTV-2160p` | separator normalization → broadcast, no MPEG-2 → Origin=Broadcast, Res=2160 | `HDTV-2160p` | ✓ |
| 27 | `Movie.Title.2018.REPACK2.720p.HDTV.x264-aAF` | version 3, repack | **Corrections = 3 − 1 = 2**, Repacked=`Repacked` (`Evidence<Repackaging>`, not `Evidence<bool>`) | `HDTV-720p Proper` | ✓ — the axis compiles now |
| 28 | `Movie.Title.2016.1080p.KORSUB.WEBRip.x264.AAC2.0-RADARR` | `WEBRip-1080p` | Origin=Stream, Gen=1, Res=1080, Flaws={HardcodedSubtitles} | `WEBRip-1080p` | ✓ *(flaw needs QA-17; label unaffected)* |
| 29 | `Movie.2008.X264-DIMENSION` (shape of `weak-x264`) | `SDTV` (rung 8; a deliberate positive inference) | everything absent but Codec | **`Unknown`**, ranks at the floor | △ **inference deliberately dropped** (§6.3) |
| 30 | `Movie.Title.S01E01.The.Web.MT-dd` (q168) | `Unknown` | all absent | `Unknown` | ✓ |

### The new score, and what it is honestly worth

> **22 clean · 8 changed answer or label · 0 unmappable.**
> Pass 3 scored **11 · 9 · 10** on the same thirty rows.

Three qualifications, because the number on its own would be a worse claim than the one it replaces.

**1. Six of the twenty-two depend on work packages that do not exist yet.** Rows 3, 4, 8, 12, 21 and 28
need QA-17 or QA-21; rows 10 and part of 14–16 need QA-20. They count as mapping because pass 4 *specifies*
that material — an interface, a file list, a dependency edge and a place on the critical path — where pass
3 specified none of it and the critique's ✗ meant "no such material exists in the document". If QA-17 is
descoped, six of these rows revert, and QA-11's gate will say so.

**2. All eight △ rows are deliberate and every one of them is registrable.** They are the whole content of
the divergence register QA-11 gates on, projected at 8 across this sample of thirty. Grouped:

| Class | Rows | Decided in |
|---|---|---|
| Truthful-but-novel labels | 1, 2, 9 | §7 |
| A guess the ladder made and the model declines to make | 5, 29 | §6.3, §7 row 15 |
| A refusal the ladder made on a heuristic | 15, 16 | §3.2 `MinimumSource` |
| A refusal the model makes and the ladder does not | 14 | §5.1, L-6 |

Two of these are unambiguous improvements (15, 16 remove false refusals — q160 is a *title* triggering a
disc heuristic), three are the design's stated position on truth over parity (1, 2, 9), two are guesses
dropped (5, 29), and one is the L-6 fix the ladder file asked for in its own comment (14). **Row 29 is the
only one that is straightforwardly a loss**: the ladder infers `SDTV` from a bare `X264` and gets it right
often enough to be worth something; the model declines and renders `Unknown`. That is recorded rather
than argued away.

**3. A divergence this sample does not contain, found while re-running it.** At equal resolution the
declared `VideoOrigin` order ranks `DVD` **above** a WEB release — `StandardDefinitionDiscBitstream` (7)
against `Stream` (5) — while `MoviesLadder.cs` ranks `DVD` (weight 9) **below** `WEBDL-480p` (weight 11).
This is not new in pass 4; pass 3's enum had the same inversion and neither the document nor the critique
noticed, because no row of the thirty exercises it. It will show up in QA-11's full-corpus run, and it
belongs to §9 question 5's family: is a disc's own program stream really below a modern streaming encode
at the same raster? Recorded here so the harness does not discover it as a surprise.

### What the score does *not* say

The critique's framing was that the axes model is better over the clean cross-product and worse over the
messy residue, *"and the messy residue is precisely where the 101 rung rules came from"*. Pass 4 does not
overturn that framing; it answers it. The residue is handled by naming the four mechanisms it actually
needs — a within-source precedence rule, an inference for orphan tokens, typed container evidence, and a
per-kind seam for genuine dialect — rather than by hoping `Read` would absorb it. **Whether those four are
enough is a question the full 249-row corpus answers and thirty hand-mapped rows cannot.** That run is
QA-11, and it is the gate on deleting anything.

---

## Part 11 — Implemented versus deferred (gatekeeper closeout, 2026-08-17)

> **Appended, not woven in.** Everything above is the design as amended and remains the specification.
> This part records what of it exists in the tree, measured against the tree on 2026-08-17 rather than
> taken from any implementing session's report. The full work-package table with its evidence, the
> sequencing violations and the defect register are in `docs/open-decisions.md` Part 7 (continued); this
> part states the design consequences.

### 11.1 What shipped

The model exists end to end for one family. `Arronix.Abstractions.Quality` carries 46 public types under
`ARX0021`; `Arronix.Host/Engines/Quality/` carries the reader, the projection, the label renderer, the
refinement pipeline and the evaluator; `Arronix.Abstractions.Quality.Families` carries the video family —
12 axes, §7's 16-row rendering table, §5.1's shipped policy, §4's computed size model. `Arronix.Plugin.Movies`
declares `.Quality<VideoQuality, VideoQualityType>()`, and `MoviesLadder.cs` is deleted.

Four of §5.1's behaviours are green on the shipped default: the stream pair ties, the disc pair separates,
a rip of a rip is worse, and the broadcast pair separates. A1, A3 and the eight `Decide` rows are pinned.
Q6's truthful labels render as themselves — `Remux-480p`, `Remux-720p`, `WEBDL-540p`, `2160p`, `1440p` —
and round-trip.

The claim the design makes for itself over the clean cross-product is met and measured. Against the
generated corpus of 8,099 cases, the axes path reads resolution correctly on 90.47% and codec on 99.39%
where the ladder's bucket cascade reaches 71.90% and 50.76%; **3,169 resolution cases and 1,934 codec cases
are values the ladder has no spelling for at all** — 540, 576, 360, `h266`, `vp9`. That is S-A5's
un-bucketing, measured rather than argued.

### 11.2 What did not ship, and what each absence costs

| Not shipped | Cost while it is absent |
|---|---|
| **QA-8** — audio, written and spoken families | §2.2 and §2.3 are unimplemented. Tv, Music and Books still declare ladders, so §6.2's Stage E cannot begin |
| **QA-11** — the parity harness | **This was the gate on deleting anything, and the deletion happened first.** See below |
| **QA-13** — token binding | §7's labels do not reach the naming engine; `ShapeTokenDeriver` still reads `QualityRevision` |
| **QA-14** — user axes | §3.6 is unimplemented; a user cannot declare an axis of their own |
| **QA-18** — live preview and the profile editor | §3.5's whole purpose. The policy can be described in prose but nobody can see a profile's effect before saving it |
| **QA-16** — old-surface deletion | Correct: Stage E, and every ladder type still has consumers |

### 11.3 The three places the design is wrong, not merely unbuilt

**§2.1 declares two axes that cannot compile.** `Distributor` is `Evidence<T>` over a wrapper struct, which
satisfies the type constraint but has no entry in §1.3's derivation table and therefore no `AxisForm` and
no members. `Languages` is `EvidenceSet<Language>`, and `EvidenceSet<T>` requires `struct, Enum` while
`Language` is a class. The consequence is not cosmetic: **"prefer a German dub" and "refuse a dub" have no
home in any profile.** The German-remux behaviour survives only because `Read` may consult
`ReleaseEvidence.Languages` directly, which makes it a family rule rather than a user preference. Recorded
as **D-9**; §2.1 should not be read as implementable until it is answered.

**§7 row 12 over-fires.** `CameraCapture` + `Audio ≥ LossyStereo` → `TELESYNC` was written assuming
`AudioPresentation.RoomCapture` distinguishes a true cam. Nothing emits `RoomCapture`, so every camera
capture that states any audio codec at all renders `TELESYNC`. The row needs either a different
discriminator or a scanner that can produce one.

**§3.5's honest-prose requirement is not met by the renderer that implements it.** `Describe()` renders raw
magnitudes on a descending axis — *"the richest generation, and beyond 1 re-encodes it stops mattering"* —
which is the exact register §3.2 forbids.

### 11.4 QA-11, stated plainly

QA-11's note reads *"Stage C, and the gate on deleting anything"*, and §10's closing sentence — the last
line before this part — reads *"whether those four are enough is a question the full 249-row corpus answers
and thirty hand-mapped rows cannot. That run is QA-11, and it is the gate on deleting anything."*

**The run was never made.** `Host.Tests/Quality/LadderAxisParityTests.cs` and
`docs/design/quality-divergences.md` do not exist. The ladder was deleted first.

What exists instead is `Movies.Tests/Quality/QualityDivergenceRegister.cs`: every corpus row whose answer
moved, with its class and its reason, asserted identical-or-registered, capped at 30 entries and ceilinged
at 15%. It currently holds 23 of 220 rows — 21 of them evidence-scanner coverage gaps that each close by
adding a phrase to a vocabulary, and 2 a genuine modelling limit (a dubbed disc naming an *encoder* has
been re-encoded; one naming the disc's own *codec format* may not have been, and the scan folds `x265` and
`HEVC` onto one token before `Read` can tell).

That instrument is defensible and in some ways better than what QA-11 specified — it measures the model
against the corpus rather than against the ladder, and the ladder was never the authority. **But it is not
parity, and the difference should not be blurred by calling it that.** Nothing is proposed to undo:
rebuilding the ladder in order to measure it would be a worse trade than accepting the gap. What is
proposed is that §6.2's Stage E gate be re-stated against the register rather than against a harness that
will now never be built.

### 11.5 The two open behavioural failures

The clean-room programme's step 6a sets a 100% hard gate on negative cases — titles that must **not** be
read a particular way. It fails on four, across two rules, both in the rewritten evidence scanner:

- **NEG-18** — a disc medium name beside a re-encode token still claims disc-image packaging
  (`COMPLETE.BluRay.BDRip` and two siblings). Over-claiming packaging promotes a re-encode into the library
  as an untouched disc, which is the failure direction the corpus design singles out as the worse one.
- **NEG-14** — source vocabulary as the leading word of a work title is claimed as a source
  (`Bluray Morning 1977 AVC`).

Detail, triage and the ledger are in `docs/provenance/differential-report.md`. Neither was fixed by the
gatekeeper session, because that session had read the superseded implementation and writing into the
rewritten artifact from there would forfeit its provenance.

### 11.6 Analyzer rules

`ARXQ001`–`ARXQ006` (§1.3, §3.2, §3.7) ship as **tests, not as an analyzer** — an analyzer needs an
`Arronix.Analyzers` project and therefore a solution edit. `ARXQ001`–`ARXQ004` are swept over every
quality-facts type in the contract assembly; `ARXQ005`, `ARXQ006` and the facet bound are swept over every
shipped family's real default policy. Both sweeps carry a non-vacuity guard.

QA-15's note — *"Ships before QA-7, or the guarantees are load-time again"* — was not honoured: QA-7 landed
first. The guarantees were load-time-only until now, and one gap remains that only an analyzer can close —
a family compiled in a third-party plugin is governed by neither sweep.
