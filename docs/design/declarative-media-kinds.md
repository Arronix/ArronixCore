# Declarative Media Kinds — Design

> **Status: SUPERSEDED (2026-08-17).** Kept for the record and for its reasoning, which is still the best
> account of *why* a media kind should be declarative. What it got wrong was the medium: it carried the
> declarations in strings, and six hand-rolled sub-grammars grew inside them.
>
> **Read `typed-media-model.md` instead.** The authoring surface is now typed C# — an entity whose
> properties and attributes are the schema, plus a fluent configuration — and the host *derives* the
> descriptors this document specifies. The descriptors survive; the way they are written down does not.
>
> Concretely gone from the code as of this date: `MediaKindDefinition` (§1), `IPluginRegistry.AddMediaKind`
> (§1), `StrategyBinding` and the host strategy vocabulary (§13), `RequiredVocabulary`, and
> `NotificationDeclaration`'s deep link, link templates, occasion phrases and artwork role order. Everything
> else in this document is still live, reached through `MediaKindModel` rather than `MediaKindDefinition`.
>
> ---
>
> **Original status:** Design. No code has been written against this document.
>
> **Owner's direction (verbatim intent, designed to, not paraphrased):** media-kind plugins need **no
> imperative logic at all**. The abstraction becomes rich enough — while staying simple to use — that every
> media-type plugin **looks the same**: identical structure, with only the small differences between kinds
> encoded as data — schema, naming, release models, coordinate grammars, ladders, search templates. The
> declarations remain **C#** (records, collection expressions — an internal declarative surface, never an
> external YAML/JSON DSL). Target: **~96% of the *arr family consolidated into the common core.** Plugins
> that do real integration work — download clients, encoders, file management, notification transports —
> are a different category and stay imperative.
>
> **Inputs:** the two declarative-surface audits (Movies+Books 20,327 lines; Tv+Music 9,999 lines — every
> `.cs` file read, every unit classified D/E/R), `docs/design/unified-host-runtime.md` (spec),
> `docs/design/movies-plugin-review.md` (the six contract insufficiencies),
> `docs/design/parsing-and-test-corpus.md` (the agnostic/per-kind parse split and the
> `IReleaseTitleParser` seam), `docs/design/naming-and-tokens.md` (token derivation D1–D16),
> `docs/design/acquisition-pipeline.md` (83.6% / 92.4% core share), `docs/design/storage-layer.md`
> (catalog/library split), `docs/design/threat-model.md` (T-01), and the reference *arrs under
> `_reference/`.
>
> **Evidence discipline:** every claim of the form "this imperative code is expressible as data" is backed
> by an audit row that was itself cross-checked against *arr source. Where the audits flagged risk, the
> risk is carried into this design as a budget or a deferred row, never absorbed silently.

---

## 1. `MediaKindDefinition` — the aggregate root

### 1.1 What it is

One value. A media-kind plugin returns exactly one `MediaKindDefinition`, and its module collapses to:

```csharp
public sealed class MoviesPluginModule : IPluginModule
{
    public PluginId Id { get; } = PluginId.FromString(MoviesIds.PluginIdValue);

    public void Configure(IPluginContext context) =>
        context.Registry.AddMediaKind(MoviesDefinition.Create());
}
```

Compare today's `MoviesPluginModule.Configure` (`MoviesPluginModule.cs:35-81`): fourteen registrations,
eleven of them handing the host an imperative object. Under this design the host constructs every engine
from the definition, and the plugin constructs nothing.

The aggregate follows `MediaShape`'s own placement rule (`Shape/MediaShape.cs:10-21`): **pure data — no
delegates, no types, no interfaces** — which is what lets one declaration serve three consumers (the
extension declares it, the host executes and publishes it, a client renders from it) and what makes a
media kind installable from a registry *by reading it* (§4.4).

The sections are exactly the owner's "small differences". Everything not listed per kind has a sensible
default, and the four reference kinds exercise every section:

| Section | The "small difference" it encodes | Movies | Tv | Music | Books |
|---|---|---|---|---|---|
| `Shape` | schema: levels, coordinate spaces, grouping axes, ladders, facets, search kinds | ✔ | ✔ | ✔ | ✔ |
| `Intent` | browse axes, actions, states, workbenches | ✔ | ✔ | ✔ | ✔ |
| `Parsing` | release models: title patterns, token tables, rung-resolution decision table | ✔ | ✔ | ✔ | ✔ |
| `Matching` | resolution tables, key layers, confidence rows, variant-choice features | ✔ | ✔ | ✔ | ✔ |
| `Querying` | search templates, alias templates, coordinate grammar | ✔ | ✔ | ✔ | ✔ |
| `Quality` | evaluation defaults beyond the ladder (ladder itself lives on `Shape`) | ✔ | ✔ | ✔ | ✔ |
| `Naming` | default templates, multi-unit styles, folder spine, token fallbacks | ✔ | ✔ | ✔ | ✔ |
| `Catalog` | metadata request/response mapping (Cardigann-style, as C#) | ✔ | ✔ | ✔ | ✔ |
| `Notifications` | occasion phrases, field weights, link templates | ✔ | default | default | default |
| `Strategies` | named host strategies with parameters (§3) | 1 | 1 | 2 | 1 |

### 1.2 The record, exact

New contract area `Arronix.Abstractions.Definition`, diagnostic **`ARX0019`**, every type
`sealed record` / `readonly record struct`, `[Experimental]` per the standing convention. Reused types are
reused, never re-declared: `MediaShape`, `PluginIntentSurface`, `MatchSource`, `MatchConfidence`,
`AcquisitionScope`, `CoordinateSet`, `FieldValue`, `QualityTier`, and the parsing design's `ReleaseTags` /
`NormalizedTitle` / `ReleaseReading` (`parsing-and-test-corpus.md` §3).

```csharp
// src/Arronix.Abstractions/Definition/MediaKindDefinition.cs
/// <remarks>
/// <para>The complete declaration of one media kind: structure, intent, and every per-kind input the
/// host's media engines need. Pure data. A plugin that returns one of these and nothing else ships no
/// executable media logic — the host builds the parser, matcher, planner, quality evaluator, namer and
/// catalog mapper from it (§2), and the plugin assembly can be unloaded after capture (§4.3).</para>
/// <para>Anything a section cannot express is, by design, NOT expressible by growing the section. The two
/// escape routes are a host-owned named strategy with declared parameters (<see cref="Strategies"/>, §3)
/// or — last — a budgeted per-kind code escape that reclassifies the plugin as hybrid (§3.4). The
/// declaration vocabulary itself never grows to swallow an algorithm.</para>
/// </remarks>
public sealed record MediaKindDefinition
{
    /// <summary>The kind's structure. Levels, coordinate spaces, grouping axes, file binding,
    /// format families and their ladders, selection facets, search kinds. Unchanged from today.</summary>
    public required MediaShape Shape { get; init; }

    /// <summary>How the kind is worked with: browse axes, sorts, filters, actions, states,
    /// external surfaces, workbenches. Unchanged from today; already pure data.</summary>
    public required PluginIntentSurface Intent { get; init; }

    /// <summary>Release models: how this kind reads a release title into coordinates and how token
    /// evidence resolves to a ladder rung. The agnostic layer (§2.2) runs first and is host-owned.</summary>
    public required ParseDeclaration Parsing { get; init; }

    /// <summary>How parsed readings resolve to catalog entries and units: entry-resolution cascade
    /// parameters, per-release-kind unit resolution, confidence rows, variant choice.</summary>
    public required MatchDeclaration Matching { get; init; }

    /// <summary>Search templates: query tiers per search kind, alias templates, the coordinate
    /// grammar releases are spelled in, limits by origin.</summary>
    public required QueryDeclaration Querying { get; init; }

    /// <summary>Quality evaluation beyond the ladder: default-resolution rows, extension fallbacks,
    /// cross-family rule. Defaults to pure ladder derivation.</summary>
    public QualityDeclaration Quality { get; init; } = QualityDeclaration.LadderDerived;

    /// <summary>Default templates, template-selection rows, multi-unit filename styles, the folder
    /// spine, token fallbacks. Tokens themselves are DERIVED from <see cref="Shape"/>
    /// (naming-and-tokens.md D1–D15); this section holds only what derivation cannot know.</summary>
    public NamingDeclaration Naming { get; init; } = NamingDeclaration.Default;

    /// <summary>Cardigann-style metadata mapping: request templates, response field maps, derivation
    /// rules, id normalizations, delta-sync and paging policy. Null when the kind has no catalog
    /// authority (none of the four reference kinds is null).</summary>
    public CatalogDeclaration? Catalog { get; init; }

    /// <summary>Occasion phrases, summary field weights, artwork role order, link templates.
    /// Default renders a host-generic summary from prominent fields.</summary>
    public NotificationDeclaration Notifications { get; init; } = NotificationDeclaration.Default;

    /// <summary>Bindings of host-owned named strategies with per-kind parameters (§3). Referencing an
    /// unknown strategy id, or an unknown parameter, is a load failure — never a silent fallback.</summary>
    public IReadOnlyList<StrategyBinding> Strategies { get; init; } = [];
}
```

Registration is one new method, gated by the existing `Capability.MediaKind`:

```csharp
// IPluginRegistry — added; §5 retires the eleven per-seam methods it subsumes
IPluginRegistry AddMediaKind(MediaKindDefinition definition);
```

### 1.3 Validation — parse, don't validate, extended

`ValidatedShape` (spec §4.4) becomes the first stage of `ValidatedDefinition`. Every cross-reference in
the definition is resolved at load or the plugin is refused with a diagnostic naming the row: a
`TitlePattern` capture naming an undeclared coordinate component; a `RungRule` naming a tier id absent
from every ladder; a `QueryTierTemplate` naming an undeclared `SearchKindId`; a `StrategyBinding` naming
an unregistered strategy; a `NamingDeclaration` template referencing an underivable token
(naming-and-tokens `ARN0101`/step-13 cross-check applies unchanged). Two rules deserve their own line:

- **Rule order is semantic and load-checked.** The audits verified (Radarr
  `QualityParser.cs:117-118`, audit A §5.5) that ordered tables *are* the algorithm — pre-release before
  broadcast, weak signals last, extension fallback only when all else is silent. `ValidatedDefinition`
  preserves declared order byte-for-byte, round-trip tests assert it, and no engine may sort a rule table.
- **The predicate vocabulary is closed** (§2.9). A predicate that cannot be written is a strategy or an
  escape, never a grammar extension. This is the single rule that keeps the surface from becoming a
  worse programming language.

---

## 2. The engines

Every E classification in the audits lands in exactly one host engine. The table is the map; the
subsections give the declaration types and signatures. Line counts are the audits' E masses, quoted so
the absorption is checkable.

| # | Engine | Host location | Absorbs (audit E lines) | Declaration that drives it |
|---|---|---|---:|---|
| E1 | Normalization engine | `Arronix.Host/Parsing/` (exists per parsing design §4) | ~480 (Movie/Tv normalizers, roman expansion) | `NormalizationOptions` |
| E2 | Title parse engine | `Arronix.Host/Media/Engines/DeclarativeTitleParser` | ~1,760 (title isolation, dispatch, projection ×4) | `ParseDeclaration.TitlePatterns`, `PreRewrites` |
| E3 | Tag & rung engine | host-global scanners + `RungResolutionTable` executor | ~1,900 (quality token parsers, codec/format readers ×4) | host `ReleaseTagVocabulary` + `TokenTable`, `RungResolutionTable` |
| E4 | Quality evaluator | `DeclarativeQualityEvaluator` | ~957 (four `*QualityModel`s) | ladder on `Shape` + `QualityDeclaration` |
| E5 | Match engine | `DeclarativeMatcher : IReleaseMatcher` | ~1,470 (four matchers) | `MatchDeclaration` + strategies |
| E6 | Query templater | `DeclarativeQueryPlanner : IReleaseQueryPlanner` | ~1,090 (four planners) | `QueryDeclaration` |
| E7 | Naming renderer + layout | naming-and-tokens engine (`TemplateCompiler`, `NamingTokenDeriver`) | ~2,000 (formatters, rename policies, layouts ×4) | derived tokens + `NamingDeclaration` |
| E8 | Metadata mapper | `DeclarativeCatalogMapper : ICataloger` + host facet evaluator | ~2,740 (catalogers ×4, facet application) | `CatalogDeclaration` |
| E9 | Item store, query & workbench engine | storage milestone (`storage-layer.md`); host item rows | ~3,600 (four item sources, catalogs/indexes) | `FieldDescriptor` semantics + workbench recipes |
| E10 | Notification renderer | host renderer on the new summary seam (review A10) | ~450 | `NotificationDeclaration` |
| E11 | Language engine | host-global (parsing design §2.2) | ~165 | host data; per-kind additions via `TokenTable` |
| E12 | Bootstrap | loader builds engines; module is one line | ~250 | the definition itself |

Not in this table: the seeded indexer/curator fixtures (~1,760 lines) — development fixtures, not media
logic; they move to test assemblies (§5.6). The curated-list engine's paged-fetch/truncation machinery
(~800) is integration-side plumbing (`PagedFetch<T>` per review §5.1) and lands in `Arronix.Common`, not
in a media engine.

### 2.1 The two-layer parse, restated as declaration + engine

The split is settled by `parsing-and-test-corpus.md` §2.2 and this design does not reopen it: junk
rejection, reversal, extension strip, website prefixes, format/audio tokens, revision, group, hash,
language, `MULTI`, hardcoded subs, edition label, year-in-title, six-digit dates, canonicalization and
scene-ness are **host-owned and run once per release** before any kind sees the text. The per-kind layer
is exactly: the title guess and alternates, coordinate extraction into declared spaces, unit fan-out,
pre-substitution rewrites, kind-specific external ids, special/extra classification.

The per-kind layer is where the four plugins today ship ~1,760 imperative lines, and the audits verified
it is an ordered pattern table at real scale (Radarr `Parser.cs`: 43 ordered regexes + shared
post-processing; audit B row `TvTitleParser`). The engine executes; the kind declares:

```csharp
// src/Arronix.Abstractions/Definition/ParseDeclaration.cs
public sealed record ParseDeclaration
{
    /// <summary>Per-kind normalization parameters for the host chain: leading-article list,
    /// punctuation class, transliteration rows (umlauts), query-text rewrites (& → and).</summary>
    public NormalizationOptions Normalization { get; init; } = NormalizationOptions.Default;

    /// <summary>Sonarr's ParserCommon pre-substitutions (14 rows; 0 in Radarr): regex → replacement,
    /// applied to the normalized text before the pattern list runs.</summary>
    public IReadOnlyList<RewriteRule> PreRewrites { get; init; } = [];

    /// <summary>THE ordered pattern list. First pattern whose regex matches and whose guards pass
    /// produces the reading. Order is semantic (§1.3).</summary>
    public required IReadOnlyList<TitlePattern> TitlePatterns { get; init; }

    /// <summary>Named regexes referenced by rules and guards, declared once. The BluRayDisk
    /// mega-regex (MoviesReleaseParser.cs:959-962) lives here — data at the edge of maintainability,
    /// flagged in audit A §5.5, and still data.</summary>
    public IReadOnlyList<GuardPattern> Guards { get; init; } = [];

    /// <summary>Per-kind token tables layered over the host scanners: token/prefix → tag values.
    /// Books' 17-row format table; Music's 10-row codec table + VBR/kbps/24bit rows.</summary>
    public IReadOnlyList<TokenTable> TokenTables { get; init; } = [];

    /// <summary>The ordered decision table resolving tag evidence to a ladder rung (§2.3).</summary>
    public required RungResolutionTable RungResolution { get; init; }

    /// <summary>Ids of budgeted per-kind code escapes (§3.4). Expected empty for Movies, Music and
    /// Books; TV budgets the degenerate forms (number-words, 101-vs-S1E01).</summary>
    public IReadOnlyList<string> EscapeIds { get; init; } = [];
}

public sealed record TitlePattern
{
    /// <summary>Stable id; flows to ReleaseReading.PatternId for corpus coverage (parsing §7.2).</summary>
    public required string PatternId { get; init; }

    public required string Regex { get; init; }

    /// <summary>Which text provenances this pattern may fire on. Radarr's folder-only
    /// "year - title" pattern is a one-element list.</summary>
    public IReadOnlyList<MatchSource> Sources { get; init; } = [];

    /// <summary>Named-group → output bindings. A capture may target a coordinate component, the
    /// title text, a title year, an external id, the release-kind discriminator or a tag.</summary>
    public required IReadOnlyList<CaptureBinding> Captures { get; init; }

    /// <summary>Guard references that must hold (or must not) for the pattern to claim the match.</summary>
    public IReadOnlyList<GuardRef> Guards { get; init; } = [];

    /// <summary>Multi-capture and range expansion: S01E01E02E03 → three Single addresses;
    /// E01-E25 with MaxSpan = 25 (Sonarr's cap, carried as data).</summary>
    public RangeExpansion? Expansion { get; init; }

    /// <summary>The AcquisitionScope this pattern's reading claims (Single / SequenceSpan / Ancestor).</summary>
    public AcquisitionScope Scope { get; init; }
}

public readonly record struct CaptureBinding(
    string GroupName, CaptureTarget Target,
    string? SpaceId = null, string? ComponentId = null, string? Key = null);

public enum CaptureTarget
{
    CoordinateComponent = 0, TitleText = 1, AlternateTitle = 2, TitleYear = 3,
    ExternalId = 4, ReleaseKind = 5, Tag = 6,
}

public readonly record struct RewriteRule(string Regex, string Replacement);
public readonly record struct GuardPattern(string GuardId, string Regex);
public readonly record struct GuardRef(string GuardId, bool Negated = false);
public sealed record RangeExpansion
{
    public required string FromGroup { get; init; }
    public required string ToGroup { get; init; }
    public int MaxSpan { get; init; } = 25;
    public bool EmitAsSpan { get; init; }      // false → N Singles; true → one SequenceSpan
}
```

The engine:

```csharp
// src/Arronix.Host/Media/Engines/DeclarativeTitleParser.cs
/// <summary>One instance per validated definition. Implements the parsing design's seam verbatim —
/// downstream consumers cannot tell a declared kind from a hand-written one.</summary>
internal sealed class DeclarativeTitleParser : IReleaseTitleParser
{
    public DeclarativeTitleParser(ValidatedDefinition definition, ParseEscapeSet escapes);
    public MediaKindId MediaKind { get; }
    public ReleaseReading Parse(ReleaseParseRequest request);
}
```

Worked check against the audits: TV's five-branch dispatch (`TvTitleParser`, 375 E lines) is five
`TitlePattern` rows with `ReleaseKind` captures; the `S01E01.x264 → "episodes 1, 264"` trap is dodged the
same way the plugin dodges it — by the separator class inside the declared regex
(`TvReleaseParser.cs:583`), which is data. Movies' AKA split is two `AlternateTitle` captures; hashed
release rejection is already host-layer.

### 2.2 The agnostic tag layer is not a declaration

Stated to prevent scope creep: the host's scanners (source, resolution, codec, audio, revision, group,
language, hash) are host **code with host data**, not per-kind declarations. A kind may *extend* token
recognition through `TokenTable` (Books' `RETAIL`, Music's `24bit`) and may *consume* everything through
`RungResolutionTable`, but the shared vocabulary lives once, in `ReleaseTagVocabulary`, because a token
like `x264` means the same thing to every kind, and four copies of that table was exactly the surveyed
disease (review §5.1: group parsing already duplicated between Movies and Tv plugins).

### 2.3 The rung-resolution decision table

The audits' single hardest parse unit — `MovieQualityTokenParser`, 610 E lines — reduces to ~30 decision
rows over `(source-group × stated-resolution × remux × brDisk × codec)` **only if** the vocabulary carries
two things a naive table lacks (audit A finding #1, verified against Radarr):

```csharp
public sealed record RungResolutionTable
{
    /// <summary>Ordered rows. Order is semantic and validated on round-trip (§1.3).</summary>
    public required IReadOnlyList<RungRule> Rules { get; init; }

    /// <summary>First- or last-match-wins. Radarr's source scan takes the LAST match
    /// (QualityParser.cs:117-118: SourceRegex.Matches(...).LastOrDefault()); a first-match-only
    /// vocabulary cannot express it.</summary>
    public required RuleSelection Selection { get; init; }

    /// <summary>Container/extension fallback rows, consulted only when every rule is silent.</summary>
    public IReadOnlyList<ExtensionTierRule> ContainerFallbacks { get; init; } = [];

    /// <summary>The tier when nothing matched. References FormatFamily.Unknown by id.</summary>
    public required string UnknownTierId { get; init; }
}

public sealed record RungRule
{
    public required string RuleId { get; init; }
    /// <summary>Closed-vocabulary predicate over ReleaseTags fields and guard refs (§2.9).
    /// The ~6 Radarr guard predicates (BR-disk, MPEG-2→raw, XviD→480, unseparated
    /// "bluray1080p", 848x480 pixel forms, German-remux) are guard refs on rows, not code.</summary>
    public required TagPredicate When { get; init; }
    public required string TierId { get; init; }
}

public enum RuleSelection { FirstMatch = 0, LastMatch = 1 }
public readonly record struct ExtensionTierRule(string Extension, string TierId);
```

Cross-check: Readarr's `QualityParser` is *already* a flat named-group→tier map with an extension
fallback and a category fallback (audit A §5.2) — the Books table (17 rows) is conservative. The one
input Readarr consumes that today's plugin does not — release **categories** as a quality hint — is a
`TagPredicate` subject (`Categories` is on `ReleaseParseRequest`), so the declaration carries it.

### 2.4 The quality evaluator — the ladder is the model

The audits confirm the session's finding on all four kinds: `IsUpgrade`/`MeetsCutoff` are bare rank
comparisons; evaluation is rung lookup plus a handful of default rows (Tv: 3 rows; Music: threshold rows
320/256/192/300; Books: one generic rule). The evaluator is a pure function of the declared ladder —
**after** the review's A4/A5 fixes land, which this surface requires (§5.4): `QualityTier` gains
`int Weight` (equal weights = quality groups; fixes the live `MeetsCutoff` bug pinned by
`QualityLadderTests.TheContractsOwnCutoffCheckDisagreesWithTheDomainForAGroupedRung`) and
`QualityRevision? Revision` (PROPER-of-a-REPACK stops losing to a plain REPACK), and size limits move out
of `AdditionalAttributes` onto the tier.

```csharp
public sealed record QualityDeclaration
{
    public static QualityDeclaration LadderDerived { get; }

    /// <summary>Default-resolution rows applied before rung lookup: Movies' 6 per-source rows;
    /// Tv's null-source → WEBDL-{res} and tv+480p → SDTV rows.</summary>
    public IReadOnlyList<TierDefault> Defaults { get; init; } = [];

    /// <summary>"Round up, never down" vs nearest — Movies' declared fallback mode.</summary>
    public RungFallback Fallback { get; init; } = RungFallback.RoundUp;

    /// <summary>Books' rule, generalized: never compare across families; a declared cross-family
    /// cutoff reads mismatch ⇒ satisfied. One engine rule deletes BooksQualityModel entirely.</summary>
    public CrossFamilyRule CrossFamily { get; init; } = CrossFamilyRule.NeverCompare;
}

// src/Arronix.Host/Media/Engines/DeclarativeQualityEvaluator.cs
internal sealed class DeclarativeQualityEvaluator   // replaces the four IQualityModel implementations
{
    public DeclarativeQualityEvaluator(ValidatedDefinition definition);
    public MediaKindId MediaKind { get; }
    public QualityTier Evaluate(ReleaseTags tags);                       // parsing design §3.5 retarget
    public bool IsUpgrade(QualityTier current, QualityTier candidate);   // weight, then revision
    public bool MeetsCutoff(QualityTier quality, CutoffPolicy cutoff);   // Weight comparison (A4)
}
```

`IQualityModel` as a plugin seam is retired with the last hand-written model (§5); the evaluator is
host-internal, one per kind.

### 2.5 The match engine — a strategy family, honestly

**The one place a naive single-vocabulary claim would be poisoned** (audit A finding #2, verified):
Movies/Books fit "layered-key-lookup" (Radarr `MovieService.cs:126ff`), TV is a per-release-kind
coordinate-resolution table (`TvReleaseMatcher.cs:16` — the self-described most media-specific code in the
platform — reduced in audit B finding #1), and Music is generic assignment over feature distances (Lidarr
`IdentificationService.cs:364` → `Munkres`). One operator chain for all four would be an over-claim. A
**family of host-owned strategies with declared parameters** is not — and Lidarr is the existence proof
that the hardest case is already data + generic solver (`DistanceCalculator.cs:89-174`: a closed 6-operator
feature vocabulary; `Distance.cs:11`: weights in a literal dictionary).

```csharp
public sealed record MatchDeclaration
{
    /// <summary>The generic entry cascade, parameterized: external-id order (imdb before tmdb),
    /// scope-replaces-search rule, title key layers, agreement predicate, tiebreaks.</summary>
    public required EntryResolution Entry { get; init; }

    /// <summary>Per release-kind unit resolution: which spaces, in what order, under what gates.
    /// TV's five-branch switchboard as five rows; Movies/Books have one Singleton row.</summary>
    public required IReadOnlyList<UnitResolutionRule> Units { get; init; }

    /// <summary>(basis × provenance flags) → MatchConfidence. TV's is 4 rows; Music's 3.</summary>
    public required IReadOnlyList<ConfidenceRule> Confidence { get; init; }

    /// <summary>Variant choice as candidate-distance features (Music's ChoosePressing; Books'
    /// manifestation choice). Null when the shape has no VariantAxis level.</summary>
    public VariantChoiceDeclaration? Variant { get; init; }
}

public sealed record EntryResolution
{
    /// <summary>External-id schemes in precedence order.</summary>
    public required IReadOnlyList<string> IdentifierOrder { get; init; }

    /// <summary>Ordered key layers. Each layer = a normalizer id + a key template over fields,
    /// grouping axes and coordinate components. Books' collection-synthesized layer is the template
    /// "{collection}{position}{title}" — which is why grouping axes are match INPUTS, not just
    /// browse structure (audit A finding #8), and why review A2 is a prerequisite (§5.4).</summary>
    public required IReadOnlyList<MatchLayer> Layers { get; init; }

    /// <summary>Movies' year-agreement predicate (year ∨ secondaryYear, absent-agrees); Books'
    /// creator scoping with the comma-swap sort-name rule. Closed vocabulary (§2.9).</summary>
    public IReadOnlyList<AgreementRule> Agreements { get; init; } = [];

    /// <summary>Ambiguity policy: reject-with-reason (Movies) vs year tiebreak (Tv).</summary>
    public AmbiguityPolicy Ambiguity { get; init; } = AmbiguityPolicy.Reject;
}

public sealed record MatchLayer
{
    public required string LayerId { get; init; }
    public required string KeyTemplate { get; init; }       // "{title}", "{collection}{position}{title}"
    public required string NormalizerId { get; init; }      // host normalizer, e.g. "strip-non-alnum-upper"
    public IReadOnlyList<string> ExpanderIds { get; init; } = [];   // "roman-numeral-variants"
}

public sealed record UnitResolutionRule
{
    /// <summary>The ReleaseKind discriminator captured at parse (§2.1), or null = default row.</summary>
    public string? ReleaseKind { get; init; }

    /// <summary>Spaces to try, in order. TV's alias-first gate — UsesAliasNumbering AND
    /// source == ReleaseName — derives from the space's declared IsProvenanceSensitive plus the
    /// entry field; the engine enforces it, the row just orders the spaces.</summary>
    public required IReadOnlyList<SpaceAttempt> Spaces { get; init; }

    /// <summary>Run expansion for span scopes (full season → member units), including
    /// FileBinding-derived expansion (Music: match resolves at the anchor, units are the selected
    /// variant's running order — zero per-kind code; audit B finding #4).</summary>
    public SpanExpansion Expansion { get; init; } = SpanExpansion.None;
}

public readonly record struct ConfidenceRule(
    MatchBasis Basis, CoordinateConfidence? CoordinateConfidence, MatchConfidence Result);

public sealed record VariantChoiceDeclaration
{
    /// <summary>Priority-ordered features from the closed operator set (Lidarr's six):
    /// String, Bool, Number, Ratio, Equality, Priority — each with a weight and a source path.</summary>
    public required IReadOnlyList<DistanceFeature> Features { get; init; }
}

public sealed record DistanceFeature
{
    public required string FeatureId { get; init; }
    public required DistanceOperator Operator { get; init; }
    public required string Subject { get; init; }         // field/coordinate path
    public double Weight { get; init; } = 1.0;
    public string? ProvenanceGate { get; init; }          // "source==ReleaseName" for year-match
}

public enum DistanceOperator { String = 0, Bool = 1, Number = 2, Ratio = 3, Equality = 4, Priority = 5 }
```

The engine implements the existing seam, so the acquisition pipeline and import path are untouched:

```csharp
// src/Arronix.Host/Media/Engines/DeclarativeMatcher.cs
internal sealed class DeclarativeMatcher : IReleaseMatcher
{
    public DeclarativeMatcher(ValidatedDefinition definition, IMediaStoreReader store,
        StrategyRegistry strategies);
    public MediaKindId MediaKind { get; }
    public Task<MatchOutcome> MatchAsync(MatchRequest request, CancellationToken ct = default);
}
```

Two engine rules the declaration does *not* carry, because they are consequences of the shape:
span-constraint enforcement (today triplicated in the Tv plugin — matcher, workbench commit, rename;
audit B finding #8 — enforced once, engine-side, from `Shape.FileBinding.SpanConstraints`) and the
non-preferred-pressing warning (a consequence of declared `CompletenessIsVariantRelative`).

### 2.6 The query templater

Prowlarr's Cardigann is the at-scale proof (500+ indexers as data) that query construction is templating.
The four planners (~1,090 E lines) declare:

```csharp
public sealed record QueryDeclaration
{
    /// <summary>Per SearchKindId: ordered tiers of query templates. Tier 1 = identifier/coordinate
    /// arguments; tier 2 = text with aliases. Movies' both-ids-in-one-query, tmdb-first is one
    /// template row; the year-required-for-text rule is a template constraint.</summary>
    public required IReadOnlyList<QueryTierTemplate> Tiers { get; init; }

    /// <summary>Alias string templates over fields, grouping axes and the coordinate grammar:
    /// "{collection} {pos} {stem}", "{credit} discography", "S{00} Complete", date in 3 spellings.</summary>
    public IReadOnlyList<AliasTemplate> Aliases { get; init; } = [];

    /// <summary>How coordinates are spelled in the community's release grammar: "S{00}E{00}",
    /// absolute "{000}", vinyl side-letters. Shared with naming's multi-unit styles by id.</summary>
    public required CoordinateGrammar Grammar { get; init; }

    /// <summary>Result limits by SearchOrigin (Rss/Automatic/UserInvoked/Interactive/ReleasePush).</summary>
    public IReadOnlyList<OriginLimit> Limits { get; init; } = [];

    /// <summary>Credit substitutions: Music's Various Artists → "VA" pair.</summary>
    public IReadOnlyList<CreditSubstitution> Substitutions { get; init; } = [];
}

// src/Arronix.Host/Media/Engines/DeclarativeQueryPlanner.cs
internal sealed class DeclarativeQueryPlanner : IReleaseQueryPlanner
{
    public DeclarativeQueryPlanner(ValidatedDefinition definition, IMediaStoreReader store);
    public MediaKindId MediaKind { get; }
    public Task<ReleaseQueryPlan> PlanAsync(AcquisitionRequest request, CancellationToken ct = default);
}
```

Translated-title fan-out filtered by accepted languages — the one piece of query construction the current
contract makes impossible (review A14) — is an engine behavior gated on the A14 amendment
(`AcquisitionRequest.AcceptedLanguages`), declared as a boolean on the alias template
(`FilterByAcceptedLanguages`), and it applies identically to TV scene names and music artist aliases.

### 2.7 The naming renderer

Already designed; this section only wires it in. `naming-and-tokens.md` establishes tokens are **derived**
from the shape (D1–D16; 161 surveyed handlers → derivation + 10 modifiers), the grammar is one language,
and the engine (`TemplateCompiler`, `NamingTokenDeriver`, path materialization) is host-owned. The audits
found the remaining per-kind naming content is exactly four kinds of data, which is `NamingDeclaration`:

```csharp
public sealed record NamingDeclaration
{
    public static NamingDeclaration Default { get; }

    /// <summary>slot id ("file", "folder", per-level) → default template text.</summary>
    public IReadOnlyDictionary<string, string> DefaultTemplates { get; init; }

    /// <summary>Condition rows choosing among templates: Tv's addressing-scheme → template with
    /// missing-coordinate degradation; Music's carriers > 1 → MultiCarrierTemplate; Books'
    /// PartCount>1 → MultiPart / labeled-primary → Collected / else Single.</summary>
    public IReadOnlyList<TemplateSelectionRule> Selection { get; init; } = [];

    /// <summary>The 6-row multi-unit style table {joiner, repeat-prefix, range-only,
    /// re-state-outer} (TvNaming RenderInnerOrdinals, audit B finding #9).</summary>
    public IReadOnlyList<MultiUnitStyle> MultiUnitStyles { get; init; } = [];

    /// <summary>Folder spine: "{root}/{Series TitleYear}/{axis folder}"; axis folder from declared
    /// SequenceException ("Specials") + "Season {00}" template. Music's "pressing never in the
    /// path" is already forced by the shape (variant level is not file-bearing).</summary>
    public required string FolderSpine { get; init; }

    /// <summary>Token fallback rows: {Track Artist} = recording credit ?? work credit.</summary>
    public IReadOnlyList<TokenFallbackRule> Fallbacks { get; init; } = [];
}
```

`IRenamePolicy` (two never-invoked methods, no file parameter — review A3) and `ILibraryLayout` retire;
the naming engine is invoked by the import/rename pipeline directly (§5.5).

### 2.8 The metadata mapper — Cardigann parity, as C#

`MoviesCataloger`'s 1,248 E lines are request templates, response field maps and parameterized derivation
rules; the other three catalogers are the same shape smaller. The declaration:

```csharp
public sealed record CatalogDeclaration
{
    /// <summary>Named request templates: route template, verb, argument bindings, body template.
    /// Executed by the host over the host's HTTP gateway (§4.2) — the definition never sees a socket.</summary>
    public required IReadOnlyList<RequestTemplate> Requests { get; init; }

    /// <summary>Response → MetadataNode field maps per level: JSON path → field id, with value
    /// converters from a closed set (date, int, trim, join).</summary>
    public required IReadOnlyList<ResponseMap> Responses { get; init; }

    /// <summary>Parameterized derivations: Movies' status stages incl. the 90-day
    /// theatrical-window parameter; release-date reduction; image-role selection;
    /// certification-region selection.</summary>
    public IReadOnlyList<DerivationRule> Derivations { get; init; } = [];

    /// <summary>Id normalizations: imdb "tt" prefix + zero-pad-7; URL-segment extraction patterns;
    /// trailing-year split with bounds (1870..now+1).</summary>
    public IReadOnlyList<IdNormalization> IdRules { get; init; } = [];

    /// <summary>Changed-since window policy: backoff 15 min, floor to hour — as parameters.</summary>
    public DeltaSyncPolicy? Delta { get; init; }

    /// <summary>Max pages; truncation-is-failure. Feeds ICurator completeness reporting.</summary>
    public PagingPolicy Paging { get; init; } = PagingPolicy.Default;
}

// src/Arronix.Host/Media/Engines/DeclarativeCatalogMapper.cs
internal sealed class DeclarativeCatalogMapper : ICataloger
{
    public DeclarativeCatalogMapper(ValidatedDefinition definition, IHttpGateway gateway,
        IJsonSerializer json, TimeProvider clock);
    // ICataloger implemented in full: SearchAsync, GetAsync (declared SelectionPolicy facets applied
    // by the host facet evaluator at materialization), ChangedSinceAsync per Delta.
}
```

The facet evaluator is host-side and generic — Books' cataloger already proves it (audit A: thresholds,
flags, enumerations read from declared `SelectionPolicy`; "work with zero surviving manifestations is
dropped" is an engine rule). Cardigann's own equilibrium — data by default, native code for hard cases —
is preserved: a catalog protocol the template vocabulary cannot express is an **integration plugin**
registering a real `ICataloger` (§4), not a richer template grammar.

### 2.9 The predicate vocabulary — closed, tiny, final

Used by `RungRule.When`, pattern guards, agreement rules and template-selection rows:

```csharp
/// <summary>A conjunction of atoms. No disjunction (write two rows), no negation of conjunctions
/// (atoms carry their own Negated), no arithmetic beyond comparison, no recursion, no user
/// functions. Anything else is a strategy (§3) or an escape (§3.4). This enum is APPEND-ONLY and
/// every addition requires a resolution-table row in this document's successor.</summary>
public sealed record TagPredicate(IReadOnlyList<PredicateAtom> All);

public sealed record PredicateAtom
{
    public required string Subject { get; init; }        // "tags.Source", "tags.ResolutionHeight",
                                                         // "categories", "guard:br-disk", "capture:res"
    public required PredicateOp Op { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
    public bool Negated { get; init; }
}

public enum PredicateOp
{
    Equals = 0, In = 1, Present = 2, GreaterOrEqual = 3, LessOrEqual = 4,
    GuardMatches = 5, Contains = 6,
}
```

### 2.10 What the item-source seam becomes

`IMediaItemSource` exists only because this milestone has no persistence (its own remarks say so;
established session fact). The storage design's catalog/library split gives the host item rows; the four
item sources' filter/sort/accessor machinery (~1,900 E lines Movies+Books, ~1,000 Tv+Music) is derivable
from `FieldDescriptor` semantics — Music's `Matches`/`Canonical`/`Sort` already contain zero per-kind
switches (audit B finding #7), which is the direct evidence the query engine needs no per-kind code. The
workbench proposal recipes are compositions of engine calls (parse → match → quality), and commit
validation re-checks declared `FileBinding`/`SpanConstraints` — both become host workbench-engine
behavior. The review's A1 (no action seam) is answered at the same time: for a declarative kind, actions
bind to a **closed host verb vocabulary** (monitor, search, refresh, rescan, rename-preview, add, remove,
exclude — all host state or engine invocations); `ActionDescriptor` gains a `HostVerb` binding, and the
review's `PerformAsync` is the interim seam that retires with the store (§5.6). No new declaration type is
needed here beyond the intent surface that already exists.

---

## 3. The strategy escape hatch

### 3.1 The rule that keeps the surface honest

> **Needing logic is the signal to write a host strategy — never to grow the declaration.**

A strategy is host-owned, named, versioned with the host, and parameterized by declared data. The
declaration references it; the plugin ships no code for it. When even a strategy cannot cover a case, the
budgeted code escape (§3.4) reclassifies the plugin as hybrid — visibly, in its manifest — rather than
letting the declarative surface silently rot.

### 3.2 The registry

```csharp
// src/Arronix.Abstractions/Definition/StrategyBinding.cs
public sealed record StrategyBinding
{
    /// <summary>The role being filled: "title-respace", "track-assignment", "scene-offset",
    /// "position-label-class", "file-clustering". Closed per host version.</summary>
    public required string Role { get; init; }

    /// <summary>The host strategy chosen for the role. Unknown id = load failure.</summary>
    public required string StrategyId { get; init; }

    /// <summary>Typed parameters, validated against the strategy's declared parameter schema.</summary>
    public IReadOnlyDictionary<string, FieldValue> Parameters { get; init; }
        = new Dictionary<string, FieldValue>();
}

// src/Arronix.Host/Media/Strategies/StrategyRegistry.cs
internal sealed class StrategyRegistry
{
    public IStrategyDescriptor Describe(string role, string strategyId);   // parameter schema, version
    public TStrategy Resolve<TStrategy>(StrategyBinding binding) where TStrategy : IHostStrategy;
}

internal interface IHostStrategy { string StrategyId { get; } string Role { get; } }
```

### 3.3 The initial strategy inventory — every R item from both audits

| Role | Strategy | Parameters (declared) | Covers (audit evidence) | Confidence |
|---|---|---|---|---|
| `title-respace` | `dotted-title-respace` | exception words (`a`, `dr`) | `MovieTitleParser.RejoinAcronyms`, 45 lines — the only genuine algorithm in 15.9k lines of Movies | High |
| `position-label-class` | `numeric-label-test` | numeric-parse test over labeled memberships | `BooksCataloger.IsPartOrSet`, 20 lines ("all labels present, none numeric ⇒ omnibus/fragment") | High |
| `scene-offset` | `offset-extrapolation` | mapped anchor set, extrapolation rule (constant offset), confidence downgrade | XEM-style mapping ingest; modeled as data at `TvSeed.cs:589-607`; writes the `AliasIsUnverified` flag the model already reserves | High — the consumption side is already pure data |
| `track-assignment` | `assignment-matching` (Munkres) | `DistanceFeature[]` weights (§2.5 vocabulary) | `MusicItemSource.cs:460-471` positional stub self-documents this replacement; Lidarr `Munkres.cs` = 504 generic lines, zero music nouns | High — verified |
| `variant-choice` | `candidate-distance` | `DistanceFeature[]` | Music `ChoosePressing`; Books manifestation choice | High — Lidarr already externalizes weights as data |
| `file-clustering` | `folder-tag-cluster` | signals: folder boundary, tag album/artist equality, fuzzy-title threshold | Lidarr `TrackGroupingService` (253 lines) — pre-step of the import workbench | **Medium** — the one candidate that could regress to R-hard if per-kind signals multiply. Watch item. |

R-hard, for the code as audited: **empty**. All 65 residual lines across four plugins (0.21%) resolve to
the first two rows. The four absent-but-inevitable residuals (audit B's honest list) resolve to rows 3–6.

### 3.4 The budgeted code escape — and the honest R-hard list

Cardigann's real-world equilibrium — 500+ definitions as data **and** the hard cases in native code — is
the design precedent, adopted deliberately rather than pretending the tables cover 100%:

```csharp
// The ONE per-kind code seam a media-kind plugin may register. Doing so reclassifies the plugin
// as HYBRID in its manifest and in the published MediaKindDescriptor — visibly, not silently.
public interface IReleaseParseEscape
{
    string EscapeId { get; }              // referenced from ParseDeclaration.EscapeIds
    /// <summary>Runs only when every declared TitlePattern declined. May return null (decline).</summary>
    ReleaseReading? TryParse(ReleaseParseRequest request);
}
IPluginRegistry AddReleaseParseEscape(IReleaseParseEscape escape);   // Capability.Parsing
```

Expected use across the four reference kinds: **Movies 0, Books 0, Music 0, TV 1–2** (number-words,
101-vs-S1E01 ambiguity — the post-regex validation hair audit B row `TvTitleParser` flags). The budget is
enforced socially, not mechanically: each escape must name the corpus cases the tables cannot express, and
a release removing an escape is a win the corpus can prove.

**The honest R-hard statement:** nothing currently written is R-hard. The standing risks, ranked: (1)
`file-clustering` regressing to per-kind signals; (2) TV degenerate parse forms exceeding the escape
budget; (3) a future kind (podcasts, comics) whose matching fits no registered strategy — in which case
the rule of §3.1 applies: the strategy family grows host-side, and the definition vocabulary does not.

---

## 4. Two plugin categories, made explicit

### 4.1 The categories

| | **Media-kind plugin** | **Integration plugin** |
|---|---|---|
| Ships | one `MediaKindDefinition` (pure data) | imperative providers: `IDownloader`, `INotifier`, real `IIndexer`/`ICataloger`/`ICurator`, `IImportPipeline`, encoders, file management |
| Executable logic | none (hybrid: budgeted escapes only) | yes — that is its job |
| Network / storage | never granted; host engines act on its behalf (§4.2) | granted per manifest, scoped decorators |
| Threat posture | **T-01 vanishes** (§4.3) | T-01 mitigated by WP-T1 deny-list, unchanged |
| Distribution | registry-reviewable by reading the definition (§4.4) | code review required |
| Examples | Movies, Tv, Music, Books; future: comics, podcasts, audiodrama | SABnzbd/qBittorrent clients, Discord/webhook notifiers, TMDb/MusicBrainz live catalogers, Trakt curators, ffmpeg encoders |

The existing four plugins are both at once today; §5 separates them: the media semantics become
definitions, the seeded transports/fixtures move to test assemblies, and any future *live* metadata or
indexer transport that exceeds the template vocabulary is written as an integration plugin — possibly by
the same author, in a different assembly, under different grants.

### 4.2 Mapping onto the existing capability enum — no new members

The `Capability` enum (`Plugins/Capability.cs`) does not change. What changes is **how a capability is
satisfied**: for a definition-only plugin, the definition's sections *are* the registrations.

| Capability | Integration plugin (today's model, unchanged) | Media-kind plugin (definition-satisfied) |
|---|---|---|
| `MediaKind` | — | gates `AddMediaKind`; implied requirement of every definition |
| `Parsing` | `AddReleaseParseEscape` (hybrid only) | satisfied by `Parsing` section |
| `Matching` | `IAcquisitionPolicy` (acquisition design §3.5) | satisfied by `Matching` section |
| `Indexing` | `AddIndexer` (real sources) | satisfied by `Querying` section |
| `Quality` | — (evaluator is host-internal) | satisfied by `Quality` section + ladder |
| `Renaming` | `AddDiacriticFolding` etc. | satisfied by `Naming` section |
| `Metadata` | `AddCataloger` (live transports) | satisfied by `Catalog` section |
| `Notification` | `AddNotifier` (transports) | satisfied by `Notifications` section |
| `Curation` | `AddCurator` | — (curated lists are integrations; review A11) |
| `Import`, `Download` | integration seams, unchanged | never |
| `Network`, `Storage` | grantable per manifest | **structurally ungrantable** (§4.3) |
| `TelemetrySink` | grantable | never |

The manifest gains one field: `"mode": "definition" | "hybrid" | "integration"`. The existing
both-directions check (a registration the manifest does not cover is refused; a declared capability with
no registration quarantines) generalizes: in `definition` mode, the checked "registrations" are the
definition's non-default sections.

### 4.3 What changes in the loader for a definition-only plugin

Three changes, all in `Arronix.Plugins`:

1. **The reference scan flips from deny-list to allow-list.** WP-T1's `PluginReferenceInspector`
   (threat-model) scans `TypeRef`/`MemberRef`/`ImplMap` with a deny-list for integration plugins. A
   `mode: definition` plugin is held to the strict inverse: every referenced type must be in
   `Arronix.Abstractions` or the small BCL set record construction needs (collections,
   `System.Runtime` compiler services). Any `ImplMap` row, any `HttpClient`/`File`/`Process`/
   `Reflection.Emit` reference, is a **load failure** — not a risk score. This is checkable before any
   plugin code runs, and it is what makes the T-01 claim mechanical rather than aspirational: **a plugin
   whose assembly can only construct records has no ungated BCL to abuse.**
2. **Capture, then unload.** `Configure` runs once, `AddMediaKind` captures the definition as data, the
   host validates it into `ValidatedDefinition`, builds the engines — and the plugin's
   `AssemblyLoadContext` is eligible for unload. No plugin code remains resident. (Hybrid mode keeps the
   ALC alive for its registered escapes; that is the visible price of the escape.)
3. **Grant derivation.** The loader computes the satisfied-capability set from the definition's sections
   (§4.2 table) and refuses a `Network`/`Storage` request in `definition` mode at manifest read, with a
   diagnostic pointing at this section.

### 4.4 The distribution consequence

Because the definition is data and the host already publishes `MediaKindDescriptor` over the wire, a
definition-only plugin is **reviewable by reading**: the registry (plugin-distribution design) can render
a kind's complete behavior — every pattern, every rule row, every template — without executing anything,
and two versions diff as data. This is the security payoff named in the session facts: T-01 (BCL ungated
in-process, severity Critical, "cannot be mitigated in-process") does not need mitigating for this
category, because the category ships nothing to gate.

---

## 5. Migration path from today's four plugins

Constraint: builds stay 0/0 and the 1,340-test baseline stays green after every phase. The governing
mechanics: every engine implements an **existing** seam, so downstream consumers never see the swap; a
seam is deleted only after all four kinds run on the engine (compiler-guided, per the parsing design's
own WP-21→28 pattern, which this plan extends rather than invents).

### 5.1 What survives as declaration, verbatim or near-verbatim

Already-D files move into `Create()` builders with mechanical edits only: the four `*Shape.cs` (3,542 D
lines), `*Intent.cs`/`*Vocabulary.cs` (2,506), ladder tables, token tables
(`TvReleaseParser.cs:237-274` moves verbatim), search-kind declarations, wire resource records, settings
schemas. Seed data (~2,704 pure rows) moves to test fixtures — it was always a stand-in for the metadata
pipeline and must stop flattering any line count (audit A caveat 2).

### 5.2 What converts from E to declaration rows

Per kind: title patterns + pre-rewrites; guard patterns; rung-resolution rows; match layers, unit
resolution rows, confidence rows; distance features; query tiers + alias templates + coordinate grammar;
naming templates + selection rows + multi-unit styles + folder spine; catalog request/response/derivation
rows; notification phrase/weight tables. The audits' per-unit tables are the itemized conversion list —
each E row names exactly the data column this section converts.

### 5.3 What moves into `Arronix.Host` engines

The twelve engines of §2's table, absorbing ~17,070 E lines' worth of logic written once. Plus the
strategy implementations of §3.3 (Munkres port, distance calculator, clustering, offset extrapolation —
the first two are line-for-line portable from Lidarr's already-generic code).

### 5.4 The contract prerequisites — where the movies-plugin-review intersects

Several of the review's six structural insufficiencies are not adjacent work but **prerequisites of this
surface**, because a declaration cannot reference contract slots that do not exist:

| Review finding | Why this surface needs it | Lands in |
|---|---|---|
| A4 `QualityTier.Weight` | ladder-derived evaluator; quality groups; fixes the live `MeetsCutoff` bug | Phase 0 |
| A5 `QualityTier.Revision` | upgrade rule = weight-then-revision as a pure function; un-flattens REPACK2 | Phase 0 |
| A3 `MediaFileFacts` + file-parameterized naming | 24 of 39 tokens are file properties; D14/D15 derivation targets | Phase 0 (record), Phase 3 (naming) |
| A8 structured parse output | superseded by `ReleaseTags`/`ReleaseReading` (parsing design deletes `ParsedRelease`) | Phase 1 |
| A9 token registry + widened `NamingToken` | naming derivation D1–D16 | Phase 3 (already specified in naming doc) |
| A2 grouping-axis fields/identity/reference | Books' collection-synthesized match layer and query aliases template over the axis; groups as action subjects | Phase 0 (record), Phase 4 (match) |
| A13 `MatchBasis` | `ConfidenceRule` keys on it | Phase 0 |
| A14 `AcceptedLanguages` | alias fan-out filter (§2.6) | Phase 4 |
| A6/A7 typed filters + grouping slot | host item-query engine | Phase 6 (with storage) |
| A1 action seam | closed host verbs for declarative kinds (§2.10) | Phase 6 |
| A10 summary-renderer seam | `NotificationDeclaration` needs a seam to land on — today the renderer is unreachable (audit A finding #10) | Phase 5 |

### 5.5 The phases

| Phase | Lands | Converts | Retires | Test discipline |
|---|---|---|---|---|
| 0 | Contract amendments bundle (table above, Phase-0 rows) | — | — | existing tests updated in-place; the two pinned quality-bug tests flip from documenting the bug to asserting the fix |
| 1 | Parse: `ParseDeclaration` + `DeclarativeTitleParser` + host tag layer — **amends WP-21..28**: the four plugins write parse declarations instead of four hand-written `IReleaseTitleParser`s | Movies first (richest), then Tv, Music, Books | `IReleaseParser`, `ParsedRelease`, `AddReleaseParser` (per parsing design §3.5, unchanged schedule) | the golden corpus is the safety net; parity run: declared parser vs old parser over the corpus before each deletion |
| 2 | `DeclarativeQualityEvaluator` + `QualityDeclaration` | four `*QualityModel`s become declarations | `IQualityModel` as a plugin seam | ladder/upgrade/cutoff tests rehomed against the evaluator |
| 3 | Naming engine (per naming doc) + `NamingDeclaration` | four naming/layout files | `IRenamePolicy`, `ILibraryLayout` | naming fixture suites rehomed; template round-trip order checks added |
| 4 | `DeclarativeMatcher`, `DeclarativeQueryPlanner`, `StrategyRegistry` + initial strategies | four matchers + planners | hand-written `IReleaseMatcher`/`IReleaseQueryPlanner` impls (seams stay — engines implement them) | matcher/planner test suites drive the engine through the same seam; ambiguity/confidence tables asserted row-by-row |
| 5 | `DeclarativeCatalogMapper` + facet evaluator + A10 summary seam + `NotificationDeclaration` | four catalogers; movies notification renderer becomes data | seeded transports → test fixtures | cataloger tests run the mapper against fixture transports |
| 6 | Storage milestone integration: host item rows, item-query engine, workbench engine, host verbs | four item sources dissolve | `IMediaItemSource`, `IMediaIdResolver` | the largest rehoming; workbench/action tests move to host suites |
| 7 | `AddMediaKind` consolidation; manifest `mode`; loader allow-list + capture-unload | four modules → one line each | the eleven subsumed `IPluginRegistry` methods | architecture tests assert a definition-mode plugin references nothing outside the allow-list |

Phases 1–5 are independent of the storage milestone and can land against the in-memory store; phase 6 is
the storage design's own schedule. Within each phase the order is: engine + declaration types → Movies
converts (reference fidelity) → remaining three → deletion. The codebase never contains an orphaned seam
and never contains two implementations of one kind's behavior outside a phase's conversion window.

### 5.6 What each plugin file becomes (Movies as the worked example)

| Today | Becomes |
|---|---|
| `MoviesShape.cs` (1,379) | `Shape` section, minus ~383 workaround lines the Phase-0 amendments delete (review §7) |
| `MoviesReleaseParser.cs` (2,179) | ~250 pattern/guard/table rows in `Parsing`; agnostic mass → host |
| `MoviesQualityModel.cs` (830) | ladder rows on `Shape` + ~40 `Quality` rows |
| `MoviesReleaseMatcher.cs` (513) | ~60 `Matching` rows + one `title-respace` binding |
| `MoviesQueryPlanner.cs` (307) | ~60 `Querying` rows |
| `MoviesNaming.cs` (1,247) | ~40 `Naming` rows; engine mass → host |
| `MoviesIntent.cs` (965) | `Intent` section, verbatim |
| `MoviesItemSource.cs` (1,381) | dissolves into host store + declared workbench recipes (Phase 6) |
| `Providers/MoviesCataloger.cs` (2,008) | ~200 `Catalog` rows + wire records; transport → fixture |
| `Providers/MoviesCurator.cs` (1,402) | integration-category curator (kept imperative) or deferred; filter constraints → A12 settings fields |
| `Providers/MoviesIndexer.cs` (775) | profiles stay declared; fixture → test assembly |
| `Providers/MoviesNotificationRenderer.cs` (507) | ~60 `Notifications` rows |
| `Seed/*` (2,356) | test fixtures |
| `MoviesPluginModule.cs` (82) | 8 lines |

---

## 6. The consolidation arithmetic — stated honestly

### 6.1 The measured base

Across all four plugins (30,326 lines, every file audited):

| | Lines | Share |
|---|---:|---:|
| D — already declaration | 13,191 | 43.5% |
| E — engine-able (each row cross-checked against *arr source) | 17,070 | 56.3% |
| R — residual, all strategy-coverable | 65 | 0.21% |
| R-hard | **0** | 0% |

Corroborating measures from the sibling designs: acquisition decision layer **83.6%** of roles / **92.4%**
of shipped classes core-owned; parsing's agnostic layer host-owned with per-kind patterns as data at
Radarr scale; naming's 161 surveyed handlers → derivation + 10 modifiers; quality evaluation a pure
function of the ladder on all four kinds.

### 6.2 What "~96% consolidated" means, and on which measure it holds

Three different measures, three different numbers — conflating them would be the dishonest version:

1. **Per-kind imperative logic** (the owner's direction, taken literally): after this surface, a
   media-kind plugin ships 0 imperative lines (Movies, Music, Books) to ≤ ~100 budgeted escape lines
   (TV), against today's ~17,135 imperative lines across four kinds. **>99% on this measure** — but only
   because the logic moved, not vanished; it runs once in twelve engines instead of four times in
   plugins.
2. **The *arr family's differing code**: of the code that today differs per *arr (the audits' D+E+R
   universe plus the decision layer), the share that becomes *shared* core = the E mass (56.3%) plus the
   D mass's derivable half (tokens, accessors, indexes) executed by shared engines. Combined with the
   83.6%/92.4% decision-layer figures and the review's measured Radarr residue (≈6,650 lines, §8, most of
   which is exactly the data this design declares), the supportable statement is: **≥96% of the *arr
   family's per-app code consolidates into the common core, with the residue being declaration data plus
   strategy parameters** — *provided* the four deferred residuals (§3.3 rows 3–6) are built host-side.
   This is the owner's number, and it holds on this measure with that proviso.
3. **Lines a kind still ships** (declarations are still lines): Movies ≈ 5,000–5,600 (from 15,936), Tv ≈
   2,600–3,000 (from 5,896), Music ≈ 2,000–2,300 (from 4,103), Books ≈ 1,700–2,000 (from 4,391) —
   roughly **a third of today's mass, all data**, XML docs included, seeds excluded on both sides.
   Line-count is the weakest measure (this repo's doc mandate inflates it ~25% against Radarr; review
   §8), which is why it is reported last.

### 6.3 What erodes the number, ranked

1. `file-clustering` regressing per-kind (§3.3 watch item) — costs ~250 lines × affected kinds.
2. TV parse escapes exceeding budget — each escape is ~30–80 lines of real per-kind code.
3. A fifth kind whose match semantics need a new strategy — costs a host strategy, not plugin code, but
   costs it before that kind can be pure-data.
4. Live catalog protocols beyond the template vocabulary — costs an integration plugin, which is the
   designed outcome, not an erosion of *this* surface.

---

## 7. Resolution table and deferred list

### 7.1 Resolutions — the contested calls

| # | Question | Positions | Resolution | One-line reason |
|---|---|---|---|---|
| 1 | One aggregate root, or keep per-seam registrations? | Per-seam is additive and already exists; one root is a bigger bang. | **One `MediaKindDefinition`, one `AddMediaKind`.** | The owner's direction is structural identity across kinds; eleven registrations is eleven chances to differ, and the definition-mode loader (§4.3) needs one capture point to reason about. |
| 2 | Internal C# or external DSL? | Cardigann proves YAML works at scale. | **C# records, per the owner, final.** | The type checker is the schema validator; `ValidatedDefinition` gets cross-references for free; and the wire projection (§4.4) gives registries the readable form a DSL would have — without shipping a parser as attack surface. |
| 3 | One match vocabulary or a strategy family? | A single operator chain is more uniform. | **Strategy family with declared parameters.** | Verified over-claim otherwise: Lidarr is assignment (`Munkres`), Radarr is layered lookup, TV is coordinate resolution — audit A finding #2 names this the one place a naive claim is poisoned. |
| 4 | Is rule order data or presentation? | A canonical sort would ease diffing. | **Order is semantic, preserved and round-trip-validated.** | Radarr's parser comments say order *is* the algorithm; audit A §5.5 makes silent reordering the top fidelity risk. |
| 5 | One shared ordered-rule grammar for title patterns and rung tables? | Audit A left it open. | **One structural spine (ordered rows, selection mode, guard refs), two typed row vocabularies.** | The shapes rhyme but the row types differ (captures vs predicates); a merged row type would carry dead slots in both uses. |
| 6 | Do code escapes live inside the definition? | Convenient co-location. | **No. Escapes are separate registrations that flip the plugin to `hybrid`.** | The definition must stay pure data or the loader's allow-list, the unload-after-capture, and the read-to-review story (§4.3–4.4) all collapse. |
| 7 | New capability enum members for definition mode? | A `Definition` capability is tempting. | **No new members; sections satisfy existing capabilities; manifest gains `mode`.** | The enum's own remarks warn against members that make the has-matching-registration check vacuous; `mode` is orthogonal to *what* is contributed. |
| 8 | Who makes the network call for a declarative cataloger? | Plugin holds `Network`; or host executes. | **Host engine executes `CatalogDeclaration` over the host gateway, attributed and rate-limited under the plugin's id.** | Cardigann precedent exactly; a definition-only plugin must be structurally unable to hold `Network` (§4.3) or T-01 returns. |
| 9 | Quality: does the evaluator need a per-kind seam at all? | `IQualityModel` exists with four implementers. | **No — host-internal evaluator; the seam retires in Phase 2.** | All four models are rung lookup + rank comparison once A4/A5 land; a seam with no possible divergent implementation is ceremony. |
| 10 | Actions for a declarative kind: `PerformAsync` (review A1) or host verbs? | A1's fix is one plugin method. | **Closed host-verb vocabulary bound from `ActionDescriptor`; A1's method is the interim seam, retired Phase 6.** | A plugin method is imperative logic — the one thing this category may not have; every declared action across all four kinds is host state or an engine invocation. |
| 11 | Does the definition carry seed data? | Seeds made the milestone demo-able. | **No. Seeds move to test fixtures.** | The seed is a stand-in for the metadata pipeline (audit A caveat 2); baking fixtures into a production declaration confuses catalog truth with test data. |
| 12 | Predicate grammar richness? | Real parsers have hairy conditions. | **Closed, conjunctive, seven operators, append-only (§2.9).** | Every grammar extension is a Turing-tarpit step; the guards + strategy + escape ladder absorbs the hair with visibility at each rung. |
| 13 | Strategy unknown at load: warn or fail? | Warning lets old definitions limp on newer hosts. | **Load failure, always.** | A kind silently missing its assignment strategy imports files wrong; the capability model's own rule (misspelled = load failure, never silently unenforceable) applies verbatim. |
| 14 | Notification rendering: wait for A10 or ship data now? | The Movies renderer is complete and unreachable. | **`NotificationDeclaration` ships with Phase 5, when the A10 seam exists.** | Declaring against a seam that does not exist repeats the exact defect the review found (a correct implementation of a nonexistent contract). |
| 15 | Where do the four curators go? | They read like media code. | **Integration category, unchanged model.** | They are paged HTTP clients of external list services (review A11 even wants them multi-kind); their declarable half is the A12 settings-constraint work, not a media-kind section. |

### 7.2 Deferred — with the trigger that un-defers each

| Deferred | Why now is wrong | Trigger |
|---|---|---|
| `file-clustering` strategy implementation | medium-confidence classification; signals may prove per-kind | the import workbench milestone; regression to R-hard reopens §3.3 |
| Scene-mapping ingest (`offset-extrapolation` producer side) | needs the metadata pipeline; consumption is already data | metadata pipeline milestone |
| `assignment-matching` engine (Munkres port) | needed only when file→unit workbench commits for real | Phase 6 / import milestone |
| Serialized-definition distribution format (signing, diffing, registry render) | wire projection exists; format design belongs with plugin-distribution | first registry-installed third-party kind |
| `FormatFamily.TechnicalFacets` / MediaInfo probe tokens (D15) | already deferred by the naming design; probe engine absent | probe engine lands |
| Per-kind parse-escape budget tooling (corpus-case naming, coverage of escapes) | first real escape is TV's, Phase 1 | TV conversion in Phase 1 |
| A fifth `SearchKind` grammar for date-window sweeps | no kind needs it; grammar is append-only | a kind declares a windowed sweep |
| Multi-kind curators (review A11) and OAuth settings (A12) | integration-side contracts, orthogonal to this surface | curator rework |
| `IPluginDataStore` for plugin-owned catalogs | storage design defers it with the same reasoning | first kind that must persist rather than project |
| Declarative *indexer* definitions (full Cardigann parity for release sources) | this design covers media kinds; sources are integrations | a registry of data-defined sources becomes a product goal |

### 7.3 The acceptance test this design must pass

When Phase 7 closes: four plugin modules of ≤10 lines each; zero imperative media logic outside
`Arronix.Host` except TV's named escapes; the golden corpus green through `DeclarativeTitleParser` for
all four kinds; the 1,340-test baseline (rehomed, not weakened) green; a definition-mode plugin loadable,
capturable, unloadable, and renderable as data by the registry; and `grep -r "movie\|film"` over
`Arronix.{Abstractions,Common,Host,Api,Plugins}` still returning only doc-comment examples.
