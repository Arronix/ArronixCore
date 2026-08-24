# Arronix Naming, Tokens and Renaming — Historical Design

> **Status:** Historical implementation design, partially implemented and subsequently reshaped by the typed
> media refactor. It records the reasoning behind the naming grammar, derivation, and collision model; it is
> not current path, API, or work-package authority. Use `CONTEXT.md`, `INTERFACE.md`, `ARCHITECTURE.md`, and
> the live types for current state.
>
> **Why it was deferred, verbatim:** *"§9 validation is against `IMediaKind.NamingTokens`, which is the
> Policy Engine's job; a formatter without validation is exactly what `Expansive` was."* This document
> designs the validation first and the formatter second, which is the order that inverts that objection.
>
> **Current placement note:** token-name equality is implemented by
> `src/Arronix.Common/Naming/NamingTokenName.cs`; grammar and rendering currently live under
> `src/Arronix.Host/Engines/Naming`; public semantic values remain in `Arronix.Abstractions`. The placement
> sketches and proposed work packages below are historical, not outstanding instructions.
>
> **Empirical basis:** the four production naming engines —
> `src/NzbDrone.Core/Organizer/FileNameBuilder.cs` (1,279 lines, 46 distinct token handlers),
> `_reference/Radarr/…/FileNameBuilder.cs` (37), `_reference/Lidarr/…/FileNameBuilder.cs` (41),
> `_reference/Readarr/…/FileNameBuilder.cs` (37) — **161 hand-written token handlers in total** — plus
> `src/NzbDrone.Core.Test/OrganizerTests/` (29 fixtures, 3,791 lines, of which three fixtures exist purely
> for truncation).
>
> **Then-governing documents:** `docs/design/unified-host-runtime.md` §1 resolutions **#43** (token collisions),
> **#44** (token declaration), **#6** (no polymorphic JSON), **#35** (derive, do not declare), §2 (the shape
> model), §4.1 (the load pipeline), §6.3/§6.5 (intent and the workbench);
> `docs/arronix-common-extraction-plan.md` (three-tier promotion rule); `docs/contracts/stability.md`
> (the then-current experimental gate, since removed); the then-current `ARCHITECTURE.md` §4.1, §9, §11.
>
> Related documents being written concurrently and **not duplicated here**:
> `acquisition-pipeline.md` (where a rename is triggered from an import), `storage-layer.md` (the physical
> `UnitFileLink` table), `parsing-and-test-corpus.md` (the round-trip corpus §5.6 consumes),
> `threat-model.md` (path traversal), `pwa-and-push.md` (how a preview reaches a client).

---

## 0. Resolution table — the contested calls

Each row is a decision that could reasonably have gone the other way. The reason is one line, the same
format as `docs/arronix-common-extraction-plan.md` §"How the three proposals were resolved".

| # | Question | Positions | Resolution | One-line reason |
|---|---|---|---|---|
| 1 | Does a plugin hand-list its tokens? | Spec #44: `NamingToken[]` on `MediaShape`, manifest cross-checked. Alternative: derive the whole set from the shape. | **Derive; `MediaShape.Tokens` narrows to the extras that cannot be derived** | Spec #35 already settled the principle — *"a declaration that can be derived is a declaration that can disagree"* — and the token set is 100% derivable for 158 of the 161 surveyed handlers. |
| 2 | Are `CleanTitle` / `TitleThe` / `TitleYear` / `TitleFirstCharacter` separate tokens? | All four *arrs: yes, one handler each. | **No — a closed modifier vocabulary on one token** | Sonarr's 14 series-title handlers are exactly `{clean?} × {the?} × {year \| withoutYear \| —}` + 2; a cross-product is a modifier set that someone wrote out longhand. |
| 3 | Is the modifier vocabulary open to plugins? | Readarr's `NoSub` argues yes. | **No — closed, media-agnostic; kind-specific transforms are fields** | An open transform vocabulary is a plugin-supplied formatter with no validation, which is precisely `Expansive`; Readarr's `NoSub` is a second title *field*, and declaring it as one costs nothing. |
| 4 | Optional sections: the in-brace prefix/suffix trick, or a real group? | All four *arrs: only the in-brace trick (`{ (PartNumber)}`). | **Both — keep the trick, add a real `<…>` group** | The trick conditions on exactly one token, so Sonarr had to hand-write `SeasonEpisodePatternRegex` (`FileNameBuilder.cs:60`) to condition a *composite*; a real group deletes that regex. |
| 5 | Delimiter for the optional group | `[...]`, `{[...]}`, `<...>` | **`<…>`** | `<` and `>` are illegal in a name on Windows and are already stripped by `TokenSanitizer` (`Naming/TokenSanitizer.cs:58`), so they can never be literal output — a free delimiter with zero escaping burden. |
| 6 | Multi-unit rendering (one file, N units) | Sonarr: a 6-value `MultiEpisodeStyle` enum dispatched in app code (`FileNameBuilder.cs:480`, again at `:549`). | **A `{span}`…`{/span}` group in the template; delete the enum** | All six styles are `(head, tail, first-and-last-only?)` triples, so they are template text; an enum forces the *host* to know six renderings of a coordinate it is not allowed to understand. |
| 7 | Which template applies to this item? | Sonarr: `SeriesType` dispatch in code (`FileNameBuilder.cs:164-178`). `lidarr-music.md` §6: *"the naming seam must let a plugin choose which pattern to apply"* — i.e. a callback. | **THIRD — plugin-declared `NamingSlot`s with declared guards; user supplies the template per slot** | A callback puts naming policy back inside the plugin where nothing can validate it; the four surveyed dispatches are all expressible as guards over coordinates and counts, so declared data suffices. |
| 8 | Casing/spacing: implicit from the token's own spelling, or explicit modifiers? | Sonarr/Radarr/Lidarr: implicit (`{series title}` lowercases; `{Series.Title}` dots the spaces — `FileNameBuilder.cs:882-893`). | **Explicit modifiers only** | Two mechanisms for one capability, where the only argument for the second is continuity with another product's documentation and another product's user templates. No *arr template is ever carried into Arronix — there is no upgrade path (§11) — so there are no habits to preserve, and a later migration script can rewrite `{series title}` → `{Series Title:lower}` mechanically. Keeping it would also entangle casing with canonicalization, and canonicalization is the token-collision key (§5.2): a token's spelling would be simultaneously meaningless (for lookup) and meaningful (for rendering). |
| 9 | Format specifier after `:` | Sonarr: `int.TryParse` for length, `value.ToString(split[1])` for numbers — an unrestricted .NET format string (`FileNameBuilder.cs:952`). | **A closed, per-`FieldValueKind` format grammar** | `{Series Title:00}` and `{Episode:x8}` are both accepted today and both produce garbage; a grammar keyed on the token's declared value kind makes them compile errors in the editor. |
| 10 | Truncation unit | Sonarr mixes both: budgets in UTF-8 bytes (`:205`, `:237`), `:N` in UTF-16 chars (`Truncate`, `:1208-1218`). | **Budgets always in UTF-8 bytes; `:N` counts grapheme clusters; the effective cap is the byte-wise `min`** | Two units in one algorithm is how `should_truncate_titles_measuring_series_title_bytes` came to exist as a *regression* fixture; users think in characters and file systems count bytes, so both are honored, once, explicitly. |
| 11 | Which token absorbs the overflow? | Sonarr: one hard-coded elastic token, the episode title (`GetLengthWithoutEpisodeTitle`, `:1142`). | **A derived elasticity rank; leaf-most `Title` field first** | Hard-coding "the episode title" is the same hard-coding §9 exists to remove; elasticity falls out of `FieldSemantics.Title` plus level depth with no new declaration. |
| 12 | Illegal-character policy | Per-platform, or the union of all platforms? | **The union — restated, not re-litigated** | Already settled in `TokenSanitizer` (`:23-29`) with the right reason: a library written on Linux is read over SMB from Windows, and the failure surfaces months later as a file nobody can open. |
| 13 | Colon handling | All four *arrs: a dedicated `ColonReplacementFormat` enum with a `Smart` mode. | **A general substitution map, seeded with the colon defaults** | The colon is not special — it is just the illegal character that appears most often in titles; one validated map beats one enum plus a fixed `BadCharacters`/`GoodCharacters` pair of arrays (`FileNameBuilder.cs:111-112`). |
| 14 | Ellipsis | Sonarr swaps `...` for a `{{ellipsis}}` sentinel, formats, then swaps back (`:191`, `:218`, `:275`, `:298`, `:1069`, `:1224`). | **No sentinel — separator collapsing runs over the token stream, not the flattened string** | The sentinel exists only because `FileNameCleanupRegex` (`:74`) collapses `..` in the *output*; keeping the boundaries means the collapse can be told which runs are literal and which are inserted. |
| 15 | Who implements `IRenamePolicy`? | A 0.1.0 contract gated on `renaming`, with `AddRenamePolicy` on `IPluginRegistry`. | **Nobody — the host projects naming, and the plugin-facing contract is cut to a token contributor and renamed `INamingTokenContributor`** | Templates must be validated against a vocabulary the host owns; a plugin that both defines and applies naming is a formatter without validation, again. Two of the three methods are therefore never called on a plugin instance, so they are **deleted now** rather than recorded as a wart with a 1.0 disposition (§7.4). |
| 16 | `LibraryPathSpec.FolderTemplate` + `CustomTokens` | A single template string that cannot express a two-level layout, plus a property inviting a second, unvalidated token source. | **Both deleted; `LibraryPathSpec` carries the ordered `NamingSlot` list directly** | A dictionary of arbitrary token→value pairs bypasses derivation, collision checking and validation in one property — the `Expansive` failure mode with a nicer type, and `Arronix.Plugin.Tv` already reads it as an untyped layout flag, which is the failure happening rather than threatened. And a *single* `FolderTemplate` cannot say series→season, artist→album or author→book, so projecting the real layout onto it is lossy by construction (§7.4). Documenting a field as dead, or projecting a wrong answer into it for readers that do not exist, both cost more than deleting it. |
| 17 | Same-kind token collisions | Spec #43 rule 3: *"cannot arise — two plugins claiming one `MediaKindId` is already `MediaKindConflict`."* | **Correct across plugins, wrong within one shape; add a `ShapeDefect`** | A shape with a level named `Season` and a sequence axis named `Season` derives the same canonical token twice, from one plugin, and no cross-plugin rule catches it. |
| 18 | Round-trip validation (render a sample, re-parse it) | Sonarr: `FileNameValidationService` does this and returns a hard `ValidationFailure` (`FileNameValidationService.cs:19`). | **Keep it, as a *warning*, driven through `IReleaseMatcher`** | It catches real damage (a template that renders names its own importer cannot read), but a plugin may legitimately decline to round-trip its own file names, and a warning does not block a user who knows that. |
| 19 | When are templates validated? | Sonarr: at config save only. | **At config save, at plugin load (the slot defaults), and asserted at write time** | A default template shipped in a plugin that fails validation is a plugin defect and must not reach a user's settings page; the write-time assertion is the guard against a shape change invalidating a stored template. |
| 20 | Where does the engine live? | Common (it has the sanitizer) vs Host (it has the shape registry). | **Split: grammar + write in `Arronix.Common/Naming/`, derivation + registry + planning in `Arronix.Host/Naming/`** | The grammar takes only `MediaShape` and `ItemView` (both Abstractions) and is pure; derivation needs `ValidatedShape` and planning needs `IMediaStore`, which are host-side by §4.4/§4.6. |

---

## 1. Purpose, scope, and the shape of the subsystem

### 1.1 What this subsystem owns

Everything between *"a user typed a template into a settings field"* and *"a file exists at a path"*:

1. **Derivation** — computing the token vocabulary of a media kind from its validated shape.
2. **Registration** — publishing that vocabulary, resolving collisions, and exposing it to the client.
3. **Compilation** — parsing a template into a validated, cached object graph, with precise diagnostics.
4. **Binding** — resolving each token against an `ItemView`, a `MediaFileRecord` and host state.
5. **Materialization** — sanitizing, folding, substituting, collapsing, truncating, and joining into a path.
6. **Planning** — computing a rename plan (current path → proposed path) for preview and for commit.

### 1.2 What it does not own

- **Moving files.** That is `IFileTransferService` and the import pipeline (`acquisition-pipeline.md`).
- **Deciding a root folder.** That is `LibraryFacet.RootFolderPath`, host library configuration.
- **Parsing a release title.** That is `IReleaseTitleParser` and `IReleaseMatcher`. Naming consumes
  the matcher only for the §5.6 round-trip *warning*.
- **Any media concept whatsoever.** No exported type or member in Abstractions, Common, Host or Api may
  contain `Series`, `Episode`, `Season`, `Movie`, `Album`, `Track`, `Artist`, `Book`, `Author` or
  `Edition` (`MediaNeutralityTests`, spec §7.5). Every media noun in this document appears as *data* —
  a `MediaLevel.Name` string supplied by a plugin — never as an identifier.

### 1.3 Placement

```text
src/Arronix.Abstractions/DTOs/
└── NamingToken.cs                        WIDENED in place (§2.2); no wrapper type is added

src/Arronix.Abstractions/Naming/          ARX0009 (existing area)
├── NamingTokenOrigin.cs
├── NamingElasticity.cs
├── NamingSlot.cs                         plugin-declared template slots + guards
├── NamingGuard.cs
├── NamingDiagnostic.cs                   code, severity, span, suggestion
├── NamingDiagnosticCode.cs
├── NamingDiagnosticReport.cs
├── IDiacriticFoldingProvider.cs          (already ships, unchanged)
└── INamingTokenContributor.cs            IRenamePolicy, renamed and cut to one method (§7.4)

src/Arronix.Common/Naming/                pure, no host state
├── TokenSanitizer.cs                     (already ships, unchanged)
├── TextFolding.cs                        (already ships, unchanged)
├── DefaultDiacriticFoldingProvider.cs    (already ships, unchanged)
├── NamingTokenName.cs                    canonicalization — the collision key
├── PathLimits.cs                         byte budgets, from configuration
├── SubstitutionMap.cs                    validated character substitutions
├── PathMaterializer.cs                   sanitize → substitute → collapse → truncate → join
└── Templates/
    ├── NamingTemplateLexer.cs
    ├── NamingTemplateParser.cs           text → NamingTemplateNode tree
    ├── NamingTemplateNode.cs             Literal | Token | Optional | Span | PathSeparator
    ├── NamingTokenRef.cs                 name + modifiers + format specifier + affixes
    ├── NamingModifier.cs                 the closed modifier vocabulary
    ├── NamingFormatSpec.cs               per-FieldValueKind format grammar
    ├── CompiledNamingTemplate.cs         validated, immutable, cacheable
    └── NamingTemplateWriter.cs           CompiledNamingTemplate + bindings → string

src/Arronix.Host/Naming/                  needs ValidatedShape and IMediaStore
├── NamingTokenDeriver.cs                 THE shape → token set function
├── ITokenRegistry.cs / TokenRegistry.cs  per-kind catalog + collision rules (spec §4.5, WP-7 names it)
├── HostGlobalTokens.cs                   the reserved, kind-independent vocabulary
├── NamingTemplateCompiler.cs             parse + validate against a kind's catalog
├── NamingProfile.cs / INamingProfileStore.cs
├── NamingSlotSelector.cs                 evaluates NamingGuards, first match wins
├── NamingBindingBuilder.cs               ItemView + MediaFileRecord + links → bindings
├── IRenamePlanner.cs / RenamePlanner.cs  preview and commit plans
├── RenamePolicyProjection.cs             the host's naming projection (internal; §7.4)
├── LibraryLayoutProjection.cs            the host's ILibraryLayout implementation, over slots
└── NamingSampleService.cs                synthetic samples for the settings editor
```

`Arronix.Plugins` gains nothing. A plugin's entire naming surface is the shape it already declares, plus an
optional `INamingTokenContributor` (§7.4).

---

## 2. Token derivation — the shape *is* the vocabulary

### 2.1 The empirical result that makes this work

Take the four surveyed apps' token names and try one rule:

> **`{<MediaLevel.Name> <FieldDescriptor.Name>}`**

| Surveyed token | Level `Name` | Field `Name` | Match |
|---|---|---|---|
| `{Series Title}` (Sonarr) | Series | Title | exact |
| `{Episode Title}` (Sonarr) | Episode | Title | exact |
| `{Movie Title}` (Radarr) | Movie | Title | exact |
| `{Artist Name}` (Lidarr) | Artist | Name | exact |
| `{Album Title}` (Lidarr) | Album | Title | exact |
| `{Album Disambiguation}` (Lidarr) | Album | Disambiguation | exact |
| `{Album Genre}` (Lidarr) | Album | Genre | exact |
| `{Album Type}` (Lidarr) | Album | Type | exact |
| `{Author Name}` (Readarr) | Author | Name | exact |
| `{Author Sort Name}` (Readarr, spelled `SortName`) | Author | Sort Name | exact after canonicalization |
| `{Book Subtitle}` (Readarr) | Book | Subtitle | exact |
| `{Edition Year}` (Readarr) | Edition | Year | exact |
| `{Series Year}` (Sonarr) | Series | Year | exact |
| `{Movie Certification}` (Radarr) | Movie | Certification | exact |

Fourteen of fourteen. The *arrs are already writing a derivation by hand; they simply have nowhere to
derive it from. `NamingTokenDeriver` is that function, and `MediaShape` (spec §2.10) is its input.

The rule reproduces the observed names **except** where an *arr shortened them inconsistently — Radarr's
`{Release Year}` for `movie.Year`, Lidarr's `{Release Year}` for `album.Year`. Arronix is greenfield with
**no upgrade path from any existing install**, so the systematic name wins and `{Movie Release Year}` is
what a Movies plugin publishes. Nothing has to migrate, so nothing does.

### 2.2 The derivation rules, complete

`NamingTokenDeriver.Derive(ValidatedShape shape)` returns `IReadOnlyList<NamingToken>`, ordered
root level → leaf level then by `Prominence`. Every rule below is total and deterministic.

| # | Source in the shape | Derived token name | `Origin` | Notes |
|---|---|---|---|---|
| D1 | `MediaLevel.Fields[]` where `ValueKind` is nameable (see D1a) | `{<Level.Name> <Field.Name>}` | `LevelField` | `Elasticity = Elastic` iff `Semantics` has `Title`; `IsInjective` iff `Semantics` has `Identity`. |
| D1a | — | — | — | Excluded value kinds: `Artwork`, `Link`, `Reference` (see D2), and any field whose `Prominence` is `Diagnostic`. A cover-art URL is not a file name. |
| D2 | `MediaLevel.Fields[]` where `ValueKind == Reference` | `{<Level.Name> <Field.Name>}` → the referenced item's `ItemView.Title`; plus `{<Level.Name> <Field.Name> <SubField.Name>}` for the referenced level's `Title`/`Identity`/`SortKey` fields | `LevelField` | **One hop only, never recursive.** This is Lidarr's `{Track ArtistName}` / `{Track ArtistMbId}` — the Various-Artists case where `child.owner != container.owner` (`lidarr-music.md` §(b)). |
| D3 | `CoordinateSpace.Components[]` for every space the level admits | `{<Component.Name>}` | `Coordinate` | `Elasticity = Rigid`, `IsInjective = true`. Component names are unique within a kind by validation (§5.2). |
| D4 | `CoordinateSpace` where `Kind == Date` | `{<Space.Name>}` | `Coordinate` | Sonarr's `{Air Date}`. Format grammar is the date grammar (§4.5). |
| D5 | `CoordinateSpace` where `Kind == Label` | `{<Space.Name>}` | `Coordinate` | Equatable, not orderable — so it may not be a `{span}` component (diagnostic `ARN0031`). |
| D6 | `SequenceAxis` | `{<Axis.Name>}` bound to `Components[ComponentIndex]` of `SpaceId` | `SequenceAxis` | Sonarr's `{Season}`, Lidarr's `{Medium}`. Redundant with D3 when the axis names the same component — deduplicated by canonical name, axis wins (it carries `Exceptions`). |
| D7 | `SequenceAxis.PolicyFields[]` *(requested amendment, §12.1)* | `{<Axis.Name> <Field.Name>}` | `SequenceAxis` | Lidarr's `{Medium Name}` / `{Medium Format}`, which read `Medium { Number, Name, Format }` (`Lidarr Organizer/FileNameBuilder.cs:345-348`). |
| D8 | `SequenceAxis.Exceptions[]` | *no token* — but the exception's `Name` becomes the value of D6 when the coordinate matches | — | This is what replaces every hard-coded `SeasonNumber > 0` in Sonarr's naming, and what makes `SpecialsFolderFormat` unnecessary as a separate config field. |
| D9 | `GroupingAxis` | `{<Axis.Name>}` (the group's title) | `GroupingAxis` | Radarr's `{Movie Collection}` → `{Collection}`; Readarr's `{Book Series}` → `{Series}`. |
| D10 | `GroupingAxis` where `Position != None` | `{<Axis.Name> Position}` | `GroupingAxis` | Readarr's `{Book SeriesPosition}`, whose value is the **string** `SeriesBookLink.Position` (`"2.5"`, `"1-3"`, `""`). Value kind is `Text`, never `Ordinal`. |
| D11 | `GroupingAxis` where `Arity == ManyToMany` | D9 and D10 are emitted **only if** `HasPrimaryMember` | `GroupingAxis` | `readarr-books.md` §2.3 names this impedance mismatch precisely: *"it must pick a primary — the tokens are single-valued while the relation is many-valued."* Without a declared primary the token is **not derived at all**, rather than silently rendering the first row of an unordered set. |
| D12 | `LevelIdentity.ExternalIds[]` | `{<Level.Name> <Scheme.Name>}` | `ExternalId` | Level prefix is **mandatory**: Lidarr carries the same MusicBrainz scheme on three levels (`{Artist MbId}`, `{Album MbId}`, `{Track ArtistMbId}`), so an unprefixed form is ambiguous by construction. |
| D13 | `FileBinding.OrdinalIsMeaningful == true` | `{Part Number}`, `{Part Count}` | `FileBinding` | Readarr's multi-file audiobook. Values come from `UnitFileLink.Ordinal` and the link count, not from a plugin field — the host owns the join. Canonicalizes identically to Readarr's `{PartNumber}` / `{PartCount}`. |
| D14 | `FormatFamily` | `{Format Family}` | `FormatFamily` | The family the file belongs to, resolved from its extension. Readarr's ebook/audiobook split becomes a legible name component instead of a quality-ladder band. |
| D15 | `FormatFamily.TechnicalFacets[]` *(requested amendment, §12.1)* | `{<Facet.Name>}` | `FormatFamily` | Where the 15 `{MediaInfo …}` tokens go. Declared per family, populated by a host-side probe (deferred, §11). |
| D16 | `MediaShape.Tokens[]` | the declared name verbatim | `Contributed` | The escape hatch: anything the rules above cannot produce. Requires an `INamingTokenContributor` registration and the `renaming` capability, or the shape is rejected (§5.3). |

Every derived descriptor is complete without a plugin writing a line of naming code:

```csharp
// src/Arronix.Abstractions/DTOs/NamingToken.cs   — WIDENED IN PLACE, not wrapped
namespace Arronix.Abstractions.DTOs;

/// <remarks>
/// <para>
/// The 0.1.0 record carried four members — name, description, example, required — which is what a user
/// needs to *see* and nothing a validator needs. It is widened here rather than wrapped by a second type.
/// An earlier draft of this document introduced a <c>NamingTokenDescriptor</c> that composed beside it,
/// on the reasoning that <i>"adding properties to a stable positional record would change its equality and
/// its <c>ToString</c>"</i>. Both statements are true and neither matters: at the time of writing this
/// record has zero producers and zero consumers — <c>unified-host-runtime.md</c> #44 calls it "the
/// currently-unused DTO" and §2.10 says "which nothing currently returns" — so there is no equality
/// comparison and no <c>ToString</c> anywhere that could change. Below 1.0.0 nothing here is frozen
/// (<c>docs/contracts/stability.md</c>).
/// </para>
/// <para>
/// The wrapper's real cost was downstream and permanent: <c>NamingCatalogView</c> would have exposed
/// <c>IReadOnlyList&lt;NamingTokenDescriptor&gt;</c>, so every client, CLI and TUI would read
/// <c>tokens[i].token.name</c> forever to reach the field a user actually sees.
/// </para>
/// <para>
/// The positional constructor becomes an init-only property set in the same change. The four original
/// members keep their names and meanings.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Naming, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record NamingToken
{
    /// <summary>The token as it is written in a template, e.g. <c>Series Title</c>.</summary>
    public required string Name { get; init; }

    /// <summary>One line the client shows next to the token in a picker.</summary>
    public required string Description { get; init; }

    /// <summary>A rendered sample, e.g. <c>The Expanse</c>. Shown in the picker and in the live preview.</summary>
    public required string ExampleValue { get; init; }

    /// <summary>Whether a template that omits this token is incomplete for its slot.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Lower-cased, separator-stripped. THE collision key and THE lookup key.</summary>
    public required string CanonicalName { get; init; }

    /// <summary>Decides which format specifiers and which modifiers are legal (§4.4, §4.5).</summary>
    public required FieldValueKind ValueKind { get; init; }

    public required NamingTokenOrigin Origin { get; init; }

    /// <summary>Null for host globals; set for everything derived from a level.</summary>
    public MediaLevelId? LevelId { get; init; }

    public string? SpaceId { get; init; }
    public string? ComponentId { get; init; }
    public string? FieldId { get; init; }
    public string? AxisId { get; init; }

    /// <summary>The value may be a set; a format specifier may filter it (§4.5).</summary>
    public bool Multivalued { get; init; }

    /// <summary>Whether this token may be shortened to fit a byte budget, and in what order (§6.5).</summary>
    public NamingElasticity Elasticity { get; init; } = NamingElasticity.Rigid;

    /// <summary>
    /// True when two distinct units at <see cref="LevelId"/> are guaranteed to render different values.
    /// This is what makes the "template cannot produce unique names" diagnostic decidable (§5.5).
    /// </summary>
    public bool IsInjective { get; init; }

    /// <summary>Deeper levels lose their text first when a name is too long. Root = 0.</summary>
    public int Depth { get; init; }
}

public enum NamingTokenOrigin
{
    HostGlobal = 0, LevelField = 1, Coordinate = 2, SequenceAxis = 3,
    GroupingAxis = 4, ExternalId = 5, FileBinding = 6, FormatFamily = 7, Contributed = 8,
}

/// <remarks>
/// Replaces Sonarr's single hard-coded elastic token. <c>Droppable</c> is what makes
/// <c>{Quality Full}</c> vanish rather than truncate when a name is over budget — Sonarr has no way to
/// say that, so it shortens the episode title while keeping a quality tag nobody was reading.
/// </remarks>
public enum NamingElasticity { Rigid = 0, Elastic = 1, Droppable = 2 }
```

### 2.3 Host-global tokens

Kind-independent, reserved, and **not** derivable from any shape because their values come from host-owned
state: the file record, the quality model, the custom-format calculator and the template itself.

| Token | Value kind | Elasticity | Source |
|---|---|---|---|
| `{Quality Title}` | `Quality` | Droppable | `MediaFileRecord.Quality` via `IQualityModel` |
| `{Quality Full}` | `Quality` | Droppable | title + proper + real, space-joined, empty parts elided |
| `{Quality Proper}` / `{Quality Real}` | `Text` | Droppable | revision |
| `{Custom Formats}` | `Text` (multivalued) | Droppable | the custom-format calculator; `:A,B` / `:-A,B` filter |
| `{Custom Format}` | `Text` | Droppable | requires a format specifier naming one format |
| `{Release Group}` | `Text` | Elastic | `MediaFileRecord`; the surveyed `{ReleaseGroup:12}` / `{ReleaseGroup:-17}` cases |
| `{Release Hash}` | `Text` | Droppable | `MediaFileRecord` |
| `{Original Title}` | `Text` | Elastic | the release name the file arrived under |
| `{Original Filename}` | `Text` | Elastic | the file's own stem |
| `{Languages}` | `Language` (multivalued) | Droppable | `MediaFileRecord.Languages`; `:EN+JA` / `:-EN` filter |
| `{Ext}` | `Text` | Rigid | the extension **without** its dot; see §6.6 |
| `{Preferred Words}` | `Text` (multivalued) | Droppable | **reserved, not implemented** — see below |

**Reserved-but-unimplemented is a real state and must be modeled.** `{Preferred Words}` is named in the
spec's §4.1 step-5 host-global list and has no implementation in this milestone. It is registered in
`HostGlobalTokens` with `IsImplemented = false`: a plugin colliding with it is still **rejected** (the
host's meaning is not negotiable, and it will be implemented), while a *user* template referencing it gets
diagnostic `ARN0007` — *"`{Preferred Words}` is reserved and not yet available"* — rather than silently
rendering empty. Sonarr's behavior for an unknown token is `m => string.Empty` (`FileNameBuilder.cs:872`),
which turns every typo into a silently-shortened file name.

### 2.4 What the plugin still declares

`MediaShape.Tokens` narrows to **contributed extras only** (resolution #1). The plugin declares a token
there when — and only when — no rule D1–D15 can produce it. In the four reference plugins of the
unified-host milestone, the expected count is **zero**.

The load-time cross-check (spec §4.1 pipeline step 13) becomes stronger, not weaker:

```text
published(kind)  = derived(validatedShape) ∪ contributed(MediaShape.Tokens)
                   ∪ hostGlobals

step 13 asserts:  ∀ t ∈ MediaShape.Tokens :  t ∉ derived(validatedShape)     → else ARN0101 (redundant declaration)
                  ∀ t ∈ manifest.tokens[] :  canonical(t) ∈ published(kind)  → else PluginPolicyDeclarationInvalid 2007
                  contributed ≠ ∅            ⇒ token contributor registered → else PluginCapabilityUnsatisfied 2005
```

The manifest's `tokens[]` is now a *documentation* claim checked against a computed truth, which is exactly
the §4.1 step-4 check the spec calls *"the part that **is** checkable"*.

---

## 3. Template grammar

### 3.1 The four *arr grammars are four regexes for one language

```text
Sonarr    FileNameBuilder.cs:47   (?<escaped>\{\{|\}\})|\{(?<prefix>[- ._\[(]*)(?<token>…)(?::(?<customFormat>[ ,a-z0-9+-]+…))?(?<suffix>[- ._)\]]*)\}
Lidarr    FileNameBuilder.cs:37   identical to Sonarr but customFormat lacks the comma
Radarr    FileNameBuilder.cs:41   Sonarr's, minus the escape branch, plus a (?<tag>…imdb…|edition-…) wrapper
Readarr   FileNameBuilder.cs:36   Sonarr's, minus the escape branch, customFormat is [a-z0-9]+ only
```

Four dialects, one intended language, and the differences are all accidents: Radarr and Readarr cannot
escape a literal brace at all; Readarr cannot write `{Custom Formats:-Xvid}` because its `customFormat`
class excludes `-`. Arronix specifies **one** grammar, in EBNF, and compiles it with a hand-written
recursive-descent parser rather than a regex — because the `<…>` and `{span}` constructs nest, and a nesting
language is not a regular one.

### 3.2 Grammar

```ebnf
template        = { segment } ;
segment         = literal | token | optional-group | span-group | path-separator | escape ;

path-separator  = "/" | "\" ;                     (* both accepted, canonicalized *)
escape          = "{{" | "}}" ;                   (* renders a literal brace *)
literal         = { ? any char except '{' '}' '<' '>' '/' '\' ? } ;

token           = "{" , affix , token-name , [ ":" , format-spec ] , affix , "}" ;
affix           = { "-" | " " | "." | "_" | "[" | "]" | "(" | ")" } ;
token-name      = word , { separator , word } ;
word            = alnum , { alnum } ;
separator       = "-" | " " | "." | "_" ;

optional-group  = "<" , { segment } , ">" ;

span-group      = "{span" , [ ":" , component-ref ] , { span-option } , "}" ,
                  { segment } ,                    (* head — rendered for the first unit *)
                  [ "{|}" , { segment } ] ,        (* tail — rendered for each further unit *)
                  "{/span}" ;
span-option     = " range" | " sep=" , quoted ;
component-ref   = space-id , "." , component-id ;
```

Three things follow immediately:

- `<` and `>` are **grammar only**. They can never be literal output because `TokenSanitizer` strips them
  (`Naming/TokenSanitizer.cs:58`) — so no escape form is needed and none is defined.
- A token name is matched **canonically**: lower-cased with separators removed. `{Series Title}`,
  `{series.title}` and `{SERIES_TITLE}` are the same token. This is Sonarr's
  `FileNameBuilderTokenEqualityComparer` (`FileNameBuilderTokenEqualityComparer.cs:28`) promoted from an
  implementation detail to a grammar rule.
- The affixes inside the braces are the conditional construct all four *arrs already have.

### 3.3 Optional sections — two mechanisms, and why both

**(a) In-brace affixes.** Readarr's default template is
`{Book Title}` + separator + `{Author Name} - {Book Title}{ (PartNumber)}` (`Readarr NamingConfig.cs:13`).
The `{ (PartNumber)}` construct is prefix `" ("`, token `PartNumber`, suffix `")"`, and the affixes are
emitted **only when the token resolves to a non-empty value** (`FileNameBuilder.cs:895-898`). Kept
verbatim: it is the single most-used idiom in real user templates.

**(b) The `<…>` group.** The affix trick conditions on exactly one token. Sonarr needed to condition on a
*composite* — `S{season:00}E{episode:00}` as a unit — and had no construct for it, so it hand-wrote
`SeasonEpisodePatternRegex` (`FileNameBuilder.cs:60`) to find the composite in the template text, extract
its parts, and re-emit them. That regex, plus `EpisodeFormat` and `AbsoluteEpisodeFormat` (which exist,
per `sonarr-tv-and-crosscut.md`, *"purely to let a pattern be decomposed and re-joined"*), is what a real
group construct deletes.

**Semantics.** An optional group renders **iff at least one token inside it resolved to a non-empty
value**. Nested groups evaluate innermost-first. A group containing no token is a diagnostic (`ARN0022`) —
it is unconditional literal text wearing a conditional's clothes.

```text
{Movie Title} < ({Movie Release Year})><[{Edition Tags}]> {Quality Full}
    year present, edition absent →  The Thing (1982) Bluray-1080p
    both absent                  →  The Thing Bluray-1080p
```

### 3.4 Span groups — one file, many units

Sonarr renders "this file contains units 3, 4 and 5" six different ways, selected by a `MultiEpisodeStyle`
enum (`FileNameBuilder.cs:1260-1267`) and dispatched twice, once for the ordinal pattern and once for the
absolute pattern (`:480`, `:549`). The mechanism underneath is uniform and is stated plainly in
`FormatNumberTokens` (`:912-923`): **iteration 0 renders one fragment, iterations 1..n render another.**

That is a head and a tail. Expose them:

```text
{span:aired.episode}…head…{|}…tail…{/span}
```

All six surveyed styles, from template text alone, with no enum anywhere in the host:

| Sonarr style | Arronix template fragment | Output for units 3,4,5 in season 1 |
|---|---|---|
| `Extend` | `{span:aired.episode}S{Season:00}E{Episode:00}{\|}-{Episode:00}{/span}` | `S01E03-04-05` |
| `Duplicate` | `{span:aired.episode}S{Season:00}E{Episode:00}{\|} S{Season:00}E{Episode:00}{/span}` | `S01E03 S01E04 S01E05` |
| `Repeat` | `{span:aired.episode}S{Season:00}E{Episode:00}{\|}E{Episode:00}{/span}` | `S01E03E04E05` |
| `Scene` | `{span:aired.episode}S{Season:00}E{Episode:00}{\|}-E{Episode:00}{/span}` | `S01E03-E04-E05` |
| `Range` | `{span:aired.episode range}S{Season:00}E{Episode:00}{\|}-{Episode:00}{/span}` | `S01E03-05` |
| `PrefixedRange` | `{span:aired.episode range}S{Season:00}E{Episode:00}{\|}-E{Episode:00}{/span}` | `S01E03-E05` |

Rules:

1. The unit set is `IMediaStore.LinksForFileAsync(file)`, ordered by the level's **canonical** coordinate
   space (`OrdinalPath` is `IComparable`), then by `ItemView.SortIndex` for `Label` spaces.
2. `range` iterates first and last only — Sonarr's `FormatRangeNumberTokens` (`:940-950`).
3. When exactly one unit is bound, the tail never renders and the group is indistinguishable from plain
   text. No special case.
4. Tokens **inside** the group resolve per-iteration; tokens **outside** it resolve against the first unit.
   That reproduces `AddSeasonTokens(tokenHandlers, episodes.First().SeasonNumber)` (`:519`) without the
   silent assumption, because a file spanning two values of a component with
   `SpanRule.MustNotSpan` is already rejected at link time by `InMemoryMediaStore` (spec §4.6).
5. `component-ref` is optional. Omitted, the span iterates units without binding a coordinate — used for
   text tokens, e.g. `{span}{Episode Title}{|} + {Episode Title}{/span}`, which is Sonarr's
   `GetEpisodeTitles` joined with `"+"` (`:1029-1038`) expressed as template text.
6. A span whose `component-ref` names a `Label` space is `ARN0031`: labels are equatable, not orderable
   (spec §2.3), so "first and last" is undefined.
7. Nesting a span inside a span is `ARN0032`. No surveyed case needs it and the semantics of a
   cross-product of two coordinate axes in one file name are not obvious enough to guess.

### 3.5 Modifiers — the cross-product, collapsed

```text
{Series Title:clean+the+year}
{Series Title:first}
{Author Name:upper}
```

The modifier vocabulary is **closed and media-agnostic**, applied left to right, and legality is checked
against the token's `ValueKind`.

| Modifier | Legal on | Effect | Replaces |
|---|---|---|---|
| `clean` | `Text` | `&`→`and`, scene punctuation stripped, diacritics folded via `TextFolding` | `CleanTitle` (`FileNameBuilder.cs:302-309`) |
| `the` | `Text` + `Semantics.Title` | leading article moved to the end: `The Expanse` → `Expanse, The` | `TitleThe` (`:311-314`) |
| `year` | `Text` + `Semantics.Title` | appends ` (YYYY)` from the level's `Semantics.Date`\|`Integer` year field, if not already present | `TitleYear` (`:326-340`) |
| `noyear` | `Text` + `Semantics.Title` | strips a trailing ` (YYYY)` | `TitleWithoutYear` (`:361-366`) |
| `first` | `Text` | first alphanumeric grapheme, folded and upper-cased; `_` when there is none in the first two positions | `TitleFirstCharacter` (`:368-381`) |
| `fold` | `Text` | `TextFolding.Fold` only | — |
| `lower` / `upper` / `title` | `Text` | casing. `title` is the one the surveyed implicit rule could not express at all | Sonarr's implicit casing rule (`:882-893`), replaced |
| `dot` / `kebab` / `snake` | `Text` | space → `.` / `-` / `_` | Sonarr's implicit separator rule (`:895-898`), replaced |

Sonarr's 14 series-title handlers are `{clean?} × {the?} × {year | noyear | —}` plus `first` plus the
standalone `Year` field — a cross-product someone wrote out longhand. One token and this modifier set
reproduce all 14, all 10 of Radarr's movie/collection title handlers, and all 9 of Readarr's book/subtitle
handlers — plus, via `lower`/`upper`/`dot`/`kebab`/`snake`, everything the surveyed *implicit* spelling
rule could do and one thing (`title`) it could not.

**Kind-specific transforms are fields, not modifiers.** Readarr's `NoSub` variants (`{Book TitleNoSub}`,
`{Book TitleTheNoSub}`, `{Book CleanTitleNoSub}`) are not a ninth modifier: "the title without its
subtitle" is a *book* concept and a media-agnostic core must not learn it. The Books plugin declares two
title fields — `Title` and `Title Without Subtitle` — and the derivation emits two tokens, each of which
takes the full modifier set. The vocabulary stays closed; nothing is lost.

**There is exactly one casing mechanism, and it is the explicit modifier.** The surveyed implicit rule —
a token spelled entirely lower-case renders lower-case, entirely upper-case renders upper-case, an embedded
separator replaces spaces in the value — is **not carried**. An earlier draft kept both and added a
precedence rule ("an explicit modifier always wins over the implicit reading of the same token"); that
rule, and the second comparer it needed, are deleted.

The reason for dropping it is not that it is a bad feature. It is that the only argument for keeping it was
continuity with the *arr products' documentation and with templates users wrote for those products, and
neither survives the product decision: **no *arr template is carried across** (§11 — there is no upgrade
path, which is also why token aliases are refused). If *arr template import is ever wanted it belongs in a
separate, later, version-specific migration script, which can rewrite `{series title}` → `{Series
Title:lower}` and `{Series.Title}` → `{Series Title:dot}` mechanically. That is a converter run once, not a
second grammar carried forever.

The technical cost of keeping it was real and is worth naming: a token's spelling would have to be
*meaningless* for lookup (canonicalization folds `{Series Title}`, `{series.title}` and `{SERIES_TITLE}`
together — §3.2) and *meaningful* for rendering, at the same time. Canonicalization is also the
token-collision key (§5.2). One grammar with one rule per capability keeps those two jobs separate.

---

## 4. Compilation

### 4.1 The compiler seam

```csharp
// src/Arronix.Host/Naming/NamingTemplateCompiler.cs   (Tier B — host-side, NOT promoted)
public interface INamingTemplateCompiler
{
    /// <summary>Parses and validates against a kind's published vocabulary and a slot's obligations.</summary>
    NamingCompilation Compile(NamingCompileRequest request);
}

public sealed record NamingCompileRequest
{
    public required MediaKindId Kind { get; init; }
    public required string Template { get; init; }
    /// <summary>Which slot this template fills — decides the obligations in §5.5.</summary>
    public required string SlotId { get; init; }
}

public sealed record NamingCompilation
{
    /// <summary>Non-null iff <see cref="Diagnostics"/> contains no <c>Error</c>.</summary>
    public CompiledNamingTemplate? Template { get; init; }
    public required NamingDiagnosticReport Diagnostics { get; init; }
    /// <summary>Every token the template actually reads. Drives the "does this need a metadata refresh"
    /// question that Sonarr answers with <c>RequiresEpisodeTitle</c> / <c>RequiresAbsoluteEpisodeNumber</c>
    /// (<c>FileNameBuilder.cs:395-441</c>) — two bespoke methods, replaced by one set.</summary>
    public required IReadOnlySet<string> ReferencedTokens { get; init; }
    /// <summary>A rendered sample, for the settings editor. Empty when compilation failed.</summary>
    public string Sample { get; init; } = string.Empty;
}
```

Compilation is pure and its result is immutable, so `CompiledNamingTemplate` is cached by
`(kind, canonicalizedTemplate, shapeVersion)`. Sonarr caches five separate derived facts about a pattern in
five `ICached<>` instances (`FileNameBuilder.cs:41-45`); one compiled object supersedes all five.

### 4.2 Diagnostics — the contract

```csharp
// src/Arronix.Abstractions/Naming/NamingDiagnostic.cs
[Experimental(ExperimentalContracts.Naming, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record NamingDiagnostic
{
    public required NamingDiagnosticCode Code { get; init; }
    public required NamingDiagnosticSeverity Severity { get; init; }
    /// <summary>A complete sentence, addressed to a user, naming what to do. Never an exception message.</summary>
    public required string Message { get; init; }
    /// <summary>Zero-based UTF-16 offset and length into the template. Lets any front end underline the
    /// exact characters — which is the difference between "invalid format" and a usable editor.</summary>
    public required NamingSpan Span { get; init; }
    public string? TokenName { get; init; }
    /// <summary>A replacement the front end may offer verbatim. Populated for typos and wrong-level uses.</summary>
    public string? Suggestion { get; init; }
}

public readonly record struct NamingSpan(int Start, int Length);
public enum NamingDiagnosticSeverity { Error = 0, Warning = 1, Information = 2 }

public sealed record NamingDiagnosticReport
{
    public static NamingDiagnosticReport Empty { get; }
    public required IReadOnlyList<NamingDiagnostic> Diagnostics { get; init; }
    public bool HasErrors { get; }
}
```

The type is in **Abstractions**, not host-side, because it crosses the client boundary: `Arronix.Client`
may reference only `Arronix.Abstractions` (spec §6.4's placement rule), and the settings editor renders
these. It crosses no plugin boundary, but the widened rule — *"a type lives in Abstractions **iff** it
crosses an assembly-isolation boundary"* — covers it exactly.

### 4.3 The diagnostic catalog

| Code | Severity | Condition | Message shape |
|---|---|---|---|
| `ARN0001` UnknownToken | Error | canonical name not in `published(kind)` | *"`{Serise Title}` is not a token for TV. Did you mean `{Series Title}`?"* — suggestion from `StringDistance` (`Arronix.Common/Text/StringDistance.cs`) over canonical names, threshold 2. |
| `ARN0002` TokenNotAvailableAtLevel | Error | token exists for the kind but its `LevelId` is not the slot's level or an ancestor of it | *"`{Episode Title}` cannot be used in a folder name for Series."* |
| `ARN0003` TokenNotAvailableInSlot | Error | token's `Origin` is `FileBinding` or `FormatFamily` and the slot's target is a folder | a file's part number is not a property of a folder. |
| `ARN0004` UnbalancedBrace | Error | `{` with no `}`, or `}}` unpaired | span points at the offending brace. |
| `ARN0005` UnbalancedGroup | Error | `<` with no `>`, `{span}` with no `{/span}` | — |
| `ARN0006` EmptyPathComponent | Error | two consecutive separators, or a leading/trailing one | *"This template produces an empty folder name."* |
| `ARN0007` ReservedTokenUnavailable | Error | `IsImplemented == false` host global | names the token and says it is reserved. |
| `ARN0008` AbsolutePathInTemplate | Error | a drive letter, a UNC prefix, or a leading separator | a naming template is always relative to a root folder. |
| `ARN0009` DotSegment | Error | a `.` or `..` path component | path traversal; also covered by `threat-model.md`. |
| `ARN0020` FormatSpecInvalidForKind | Error | e.g. `:00` on `Text`, `:yyyy` on `Ordinal`, `:16` on `Boolean` | *"`:00` pads a number; `{Series Title}` is text. Use `:16` to shorten it."* |
| `ARN0021` FormatSpecUnparseable | Error | the specifier does not match the grammar for the token's kind | — |
| `ARN0022` OptionalGroupWithoutToken | Warning | `<…>` containing only literals | it will always render; the group is doing nothing. |
| `ARN0023` ModifierNotLegalForKind | Error | `:the` on an `Integer`, `:year` on a field with no year sibling | — |
| `ARN0024` DuplicateModifier | Warning | `:clean+clean` | — |
| `ARN0030` SpanComponentUnknown | Error | `component-ref` names no declared space/component | — |
| `ARN0031` SpanOverUnorderedSpace | Error | the referenced space is `Label` | first and last are undefined over an unordered space. |
| `ARN0032` NestedSpan | Error | a span inside a span | — |
| `ARN0033` SpanWhereFileHoldsOneUnit | Warning | `FileBinding.AtMostOneUnitPerFile == true` | the tail will never render; probably a copied template. |
| `ARN0040` **NoDistinguishingToken** | Error | slot target is `FileName` and the template contains no token with `IsInjective` for the slot's unit level, nor `{Original Filename}` / `{Original Title}` | *"This template gives every episode the same name. Add a numbering token such as `{Episode}`, or `{Original Filename}`."* — see §5.5. |
| `ARN0041` MissingTitleInLibraryFolder | Error | slot target is `LevelFolder` on the `LibraryEntry` level with no `Semantics.Title` token | Sonarr's `ValidSeriesFolderFormat` (`FileNameValidation.cs:48`), generalized. |
| `ARN0042` MissingAxisInSequenceFolder | Error | slot target is `SequenceFolder` with no token bound to the axis component | Sonarr's `ValidSeasonFolderFormat` (`:56`), generalized. |
| `ARN0050` LiteralIllegalCharacter | Warning | the literal text contains a character `TokenSanitizer` will strip | *"`?` will be removed from the file name."* — a warning, because stripping is well-defined and some users type it knowingly. |
| `ARN0051` LiteralReservedName | Error | a path component is literally `CON`, `NUL`, `LPT1`, … | `TokenSanitizer.IsReservedName`. |
| `ARN0060` RoundTripFailed | Warning | the rendered sample does not re-match to the sample's units through `IReleaseMatcher` | see §5.6. |
| `ARN0070` SampleExceedsLimits | Warning | the sample renders over the component or path budget even before real data | the template is structurally too long. |
| `ARN0101` RedundantTokenDeclaration | Error (load) | `MediaShape.Tokens` restates a derivable token | shape defect; §5.3. |

Only `ARN0060` and `ARN0070` need data; everything else is decidable from the template plus the shape,
which is what makes configuration-time validation complete rather than advisory.

### 4.4 Modifier legality

`NamingModifier.IsLegalFor(FieldValueKind, FieldSemantics)` is a `FrozenDictionary` lookup, asserted
exhaustive by a test that enumerates `FieldValueKind` (20 members) × the modifier set (10) — 200 cells,
none of them a judgment call at runtime.

### 4.5 Format specifiers, per value kind

Sonarr accepts any string after the colon and hands it to `int.TryParse` or `value.ToString(format)`
(`FileNameBuilder.cs:952-960`, `:1230-1235`). `{Episode:x8}` therefore renders hexadecimal and
`{Series Title:00}` renders the title unchanged. Both are accepted at save time and both are wrong.

| Token `ValueKind` | Specifier grammar | Meaning | Rejects |
|---|---|---|---|
| `Ordinal`, `Integer`, `Count` | `0{1,6}` | zero-padding width | `x8`, `N2`, `16` — a width of 16 on a number is a padding request nobody meant |
| `Decimal`, `Ratio` | `0{1,3}(\.0{1,3})?` | fixed-point | everything else |
| `Date` | a whitelist: `yyyy`, `yyyy-MM`, `yyyy-MM-dd`, `dd-MM-yyyy`, `MM-dd-yyyy`, `yyyy.MM.dd`, `yyyy MM dd` | date layout | arbitrary custom format strings, which are a locale trap |
| `Text`, `MultilineText`, `Quality`, `FilePath` | `-?[1-9][0-9]{0,2}` | grapheme cap; negative keeps the **tail** | `00`, `0` |
| any, `Multivalued == true` | `-?name(,name)*` | include list, or exclude list when prefixed `-` | an empty list |
| `Language` | `-?CODE(\+CODE)*` | ISO 639-1 include/exclude | non-codes |
| `Boolean`, `Instant`, `Duration`, `ByteSize`, `Enumerated`, `ExternalIdentifier` | *(none accepted)* | — | any specifier → `ARN0020` |

Two behaviors from Sonarr are deliberately **not** carried:

- `{Air Date}` replacing `-` with spaces unconditionally (`FileNameBuilder.cs:606`). It is a hidden
  reformat of a date the user asked for by name; `{Air Date:yyyy MM dd}` says it out loud.
- The `+` wildcard suffix on language filters that appends a literal `--` when the filter matched a subset
  (`:793-797`). Nobody can explain it and no test covers its intent.

---

## 5. The token registry, collisions and validation gates

### 5.1 `ITokenRegistry`

Named by the spec at §4.5 and allocated a file in WP-7 (`Registry/TokenRegistry.cs`). Specified here:

```csharp
// src/Arronix.Host/Naming/ITokenRegistry.cs   (Tier B — held host-side)
public interface ITokenRegistry
{
    /// <summary>The kind-independent vocabulary, including the reserved-but-unimplemented entries.</summary>
    IReadOnlyList<NamingToken> Globals { get; }

    /// <summary>Globals plus everything derived from the kind's shape plus its contributed extras.</summary>
    IReadOnlyList<NamingToken> ForKind(MediaKindId kind);

    bool TryResolve(MediaKindId kind, string canonicalName,
                    [NotNullWhen(true)] out NamingToken? descriptor);

    /// <summary>Ranked near-misses for <c>ARN0001</c>, by edit distance over canonical names.</summary>
    IReadOnlyList<NamingToken> Suggest(MediaKindId kind, string canonicalName, int limit = 3);
}
```

Held host-side, per the three-tier rule: zero plugin implementers and zero plugin consumers. A plugin
reading another kind's token vocabulary would be a §4 cross-plugin call.

### 5.2 Canonicalization is the collision key

```csharp
// src/Arronix.Common/Naming/NamingTokenName.cs
public static class NamingTokenName
{
    /// <summary>Lower-cases and removes every separator and non-alphanumeric character.</summary>
    /// <remarks>
    /// This is Sonarr's <c>FileNameBuilderTokenEqualityComparer</c> promoted from a dictionary comparer to
    /// a first-class identity. It has to be the collision key, not just the lookup key: checking collisions
    /// on the literal spelling lets a plugin declare <c>{ReleaseGroup}</c> beside the host's
    /// <c>{Release Group}</c>, and the two would then be the same token to the writer and different tokens
    /// to the validator.
    /// </remarks>
    public static string Canonicalize(string tokenName);
    public static bool AreSame(string left, string right);
}
```

Because canonicalization is also what makes `{Series.Title}` work (§3.5), the collision rule and the
casing/spacing feature are the *same* mechanism. That is the argument for keeping both.

### 5.3 Collision rules — spec #43, implemented, and corrected

The spec's three rules, restated with the enforcement point:

| Rule | Outcome | Enforced in |
|---|---|---|
| 1. A kind's token collides with a **host global** (implemented or reserved) | **Reject the plugin**, `PluginTokenConflict` 2006 | `TokenRegistry.Register`, load pipeline step 14 |
| 2. Two plugins declare the same token for **different media kinds** | **Allow** | registry is keyed `(MediaKindId, canonicalName)` |
| 3. Two plugins declare the same token for the **same media kind** | Cannot arise — `MediaKindConflict` 3002 already fired at step 15 | — |

**Rule 3 is right about plugins and wrong about shapes.** Nothing in it prevents *one* shape from deriving
one canonical name twice. Concretely, all of these are reachable from a well-formed `MediaShape` that
passes every rule in `ValidatedShape`:

- a `MediaLevel` named `Season` and a `SequenceAxis` named `Season` → `{Season}` twice (D1 and D6);
- two levels named `Release` (a variant level and a grouping axis) → `{Release}` twice (D1 and D9);
- a level field named `Part Number` where `FileBinding.OrdinalIsMeaningful` is true → `{Part Number}`
  twice (D1 and D13);
- a field named `Format Family` on any level → collides with D14.

Each of these silently makes one of the two tokens unreachable, and the user's template then names a value
they did not intend. So a **fourth rule** is added and enforced one step earlier:

> **Rule 4.** Two derived tokens of one shape sharing a canonical name is a **shape defect**:
> `ShapeDefect.DuplicateNamingToken`, `CoreErrorCode.PluginShapeInvalid` (2009), raised at load pipeline
> **step 11**, before step 14 ever sees the kind. The defect message names both sources
> (*"level `season` field `Name` and sequence axis `season` both derive `{Season}`"*), because a defect a
> plugin author cannot locate is a defect they cannot fix.

Rule 4 sits inside `ValidatedShape.TryValidate` as an additional entry in its defect list (spec #40), so it
costs one rule and no new machinery.

### 5.4 The load pipeline, amended

Two of the spec's sixteen steps gain content; none is added or reordered.

| Step | Was | Now |
|---|---|---|
| 11 `ValidatedShape.TryValidate` | shape self-consistency | **+ token derivation runs; rule 4 (duplicate derived token) is a defect; every `NamingSlot.DefaultTemplate` is compiled and must produce zero `Error` diagnostics** |
| 13 manifest `tokens[]` cross-check | *"the part that is checkable"* | **exact:** `manifest.tokens[] ⊆ published(kind)`; `MediaShape.Tokens ∩ derived = ∅` (`ARN0101`); contributed tokens require an `INamingTokenContributor` registration |

Compiling a plugin's own default templates at load is the point of resolution #19: a Books plugin shipping
`{Book Title}{ (PartNumber)}` when its `FileBinding.OrdinalIsMeaningful` is false ships a token that does
not exist, and the right time to find that out is `dotnet build` on the plugin's own test project, not a
user's settings page.

### 5.5 `ARN0040` — the diagnostic the four *arrs each hard-coded

Sonarr has three near-identical validators asserting that a template distinguishes one episode from
another (`FileNameValidation.cs:74-132`):

```csharp
// ValidStandardEpisodeFormatValidator, :85-87
return FileNameBuilder.SeasonEpisodePatternRegex.IsMatch(value) ||
       (FileNameBuilder.SeasonRegex.IsMatch(value) && FileNameBuilder.EpisodeRegex.IsMatch(value)) ||
       FileNameValidation.OriginalTokenRegex.IsMatch(value);
```

with `ValidDailyEpisodeFormatValidator` adding `AirDateRegex` and `ValidAnimeEpisodeFormatValidator` adding
`AbsoluteEpisodePatternRegex`. Three validators, five regexes, one idea: *a file-name template must contain
something that differs between two units of the same parent.*

Derived, it is one predicate over `NamingToken.IsInjective`:

```csharp
// src/Arronix.Host/Naming/NamingTemplateCompiler.cs
private bool DistinguishesUnits(CompiledNamingTemplate template, MediaLevelId unitLevel) =>
    template.ReferencedTokens.Any(name =>
        _tokens.TryResolve(_kind, name, out var d)
        && (d.Origin is NamingTokenOrigin.HostGlobal
                && d.CanonicalName is "originalfilename" or "originaltitle"
            || d.IsInjective && d.LevelId == unitLevel));
```

`IsInjective` is set by derivation, not by hand: true for every component of the level's **canonical**
coordinate space (spec §2.3 — *"the space identity and completeness are measured in"*), for every field
carrying `FieldSemantics.Identity`, and for every `ExternalId` token at that level. It is deliberately
false for non-canonical coordinate spaces: Sonarr's `scene` space is provenance-sensitive and may be
unverified, so a template built only on `{Scene Episode}` genuinely can collide, and today nothing says so.

The Movies plugin gets this for free and correctly: its `movie` level's canonical space is `Singleton`,
which has no components, but its `Title` field carries `Semantics.Identity`, so `{Movie Title}` satisfies
`ARN0040` — matching Radarr, which requires nothing at all here and is right not to.

### 5.6 `ARN0060` — the round trip, demoted to a warning

`FileNameValidationService` renders a sample, re-parses it with `Parser.ParseTitle`, and fails the config
save when the coordinates do not survive (`FileNameValidationService.cs:21-39`). The idea is excellent —
it catches a template whose own importer cannot read its output — and the enforcement level is wrong,
because a user who deliberately names files in a scheme their indexer will never see should not be blocked.

Generalized, it costs nothing new:

```csharp
var sample  = writer.Write(compiled, syntheticBindings);          // §8.3
var outcome = await matcher.MatchAsync(new MatchRequest
{
    MediaKind = kind, Text = sample, Source = MatchSource.FileName, Scope = syntheticParent
}, ct);

if (!outcome.Units.SetEquals(syntheticUnits))
    diagnostics.Add(NamingDiagnostics.RoundTripFailed(sample, outcome));   // ARN0060, Warning
```

`IReleaseMatcher` is registered under the `matching` capability and may be absent; then the check is
skipped rather than failed. The message quotes the sample and the units the matcher returned, so the user
can see precisely what was lost.

---

## 6. Path materialization

### 6.1 Pipeline

Rendering is a **token stream**, not a string, until the last step. That single change removes the
`{ellipsis}` sentinel dance (resolution #14) and makes separator collapsing safe.

```text
CompiledNamingTemplate + bindings
  → write            : each node produces a (text, provenance) fragment; provenance ∈ {Literal, TokenValue, Inserted}
  → substitute       : SubstitutionMap over TokenValue fragments only (§6.3)
  → sanitize         : TokenSanitizer.SanitizeComponent over TokenValue fragments only
  → drop affixes     : a token that rendered empty takes its prefix and suffix with it
  → collapse         : runs of [- ._] spanning fragments reduce to one, EXCEPT inside an Inserted fragment
  → trim             : leading/trailing separators per path component
  → truncate         : §6.5, in UTF-8 bytes, per component and against the whole path
  → reserved-name fix: TokenSanitizer.SanitizeComponent's device-name rule, per component
  → join             : components with the platform separator; extension appended to the last (§6.6)
  → uniquify         : TokenSanitizer.MakeUnique against the destination directory (§6.7)
```

Sanitizing `TokenValue` fragments **only** is the reason literal template text keeps `ARN0050` as a warning
rather than an error: a user who types `?` into a template gets it stripped and is told, while a title
containing `?` is stripped without comment because that is not a decision they made.

### 6.2 Character rules: the union, restated

`TokenSanitizer` already applies the union of Windows and POSIX restrictions on every platform, and its
`<remarks>` gives the reason (`Naming/TokenSanitizer.cs:23-29`): *"A library written on Linux is read over
SMB from Windows, restored onto a NAS and synchronized to a Mac."* This design changes none of it. The
illegal set is `" * / : < > ? \ |` plus the ASCII control range; the trailing rules strip `.` and space;
the reserved device names are checked against the stem before the first dot.

What is added is a **length policy that is not the union**, because a union of length limits would be
useless — §6.4.

### 6.3 Substitutions replace `ColonReplacementFormat`

All four *arrs special-case one character:

```csharp
// FileNameBuilder.cs:1160-1200 — Sonarr, and near-verbatim in the other three
if (namingConfig.ColonReplacementFormat == ColonReplacementFormat.Smart) {
    result = result.Replace(": ", " - ");
    result = result.Replace(":", "-");
}
…
for (var i = 0; i < BadCharacters.Length; i++)
    result = result.Replace(BadCharacters[i], namingConfig.ReplaceIllegalCharacters ? GoodCharacters[i] : "");
```

A six-value enum for the colon, and two parallel fixed arrays for the other eight. Generalize to one
validated table:

```csharp
// src/Arronix.Common/Naming/SubstitutionMap.cs
public sealed record SubstitutionRule(string Match, string Replacement, bool WordBoundaryOnly = false);

public sealed class SubstitutionMap
{
    /// <summary>Reproduces the surveyed defaults exactly: <c>": " → " - "</c>, <c>":" → "-"</c>,
    /// <c>"/" → "+"</c>, <c>"\\" → "+"</c>, <c>"?" → "!"</c>, <c>"*" → "-"</c>, and deletion for the rest.</summary>
    public static SubstitutionMap Default { get; }

    /// <summary>Applied to token values before sanitizing, longest match first, single pass.</summary>
    public string Apply(string value);

    /// <summary>Every replacement must itself survive <see cref="TokenSanitizer.SanitizeComponent"/>
    /// unchanged. A user mapping <c>":" → "/"</c> would otherwise silently create a directory.</summary>
    public static bool TryCreate(IReadOnlyList<SubstitutionRule> rules, out SubstitutionMap map,
                                 out IReadOnlyList<NamingDiagnostic> diagnostics);
}
```

`ColonReplacementFormat.Smart` — the two-rule form `": " → " - "` then `":" → "-"` — is a genuinely good
default and is kept as exactly that: two rules in `Default`, not a mode.

**The trade-off, stated:** a free-form table lets a user write a rule that makes every name worse. The
mitigation is that `TryCreate` rejects any replacement that is not already legal, and the settings editor
shows a live sample. Compared with the alternative — one enum per character anyone ever complains about —
this is the cheaper failure mode.

### 6.4 Limits

```csharp
// src/Arronix.Common/Naming/PathLimits.cs
public sealed record PathLimits
{
    /// <summary>UTF-8 bytes for a single component. 255 on ext4/APFS/NTFS/SMB; 143 on eCryptfs.</summary>
    public required int MaxComponentBytes { get; init; }
    /// <summary>UTF-8 bytes for the whole path. 4096 on Linux; 259 on Windows without long paths.</summary>
    public required int MaxPathBytes { get; init; }
    public static PathLimits Detect(IOperatingSystemInfo os);
}
```

Configuration: `Arronix:Naming:MaxComponentBytes`, `Arronix:Naming:MaxPathBytes` under the standard
`AddValidatedOptions<NamingOptions>` pattern, defaulting to `Detect`. This is the modern replacement for
`NzbDrone.Common.Disk.LongPathSupport`, which reads the environment variables `MAX_PATH` and `MAX_NAME`
(`LongPathSupport.cs:24`, `:47`) — the same idea, given a name and a validated options type.

**Why limits are per-installation while characters are the union.** A character rejected anywhere breaks
the file everywhere, so the union is right. A *length* limit is not like that: applying Windows' 259-byte
path limit on a Linux host with a 12-level-deep library would mangle every name for no benefit, and
applying Linux's 4,096 on Windows would produce files Windows cannot open. Limits are therefore detected
and overridable; characters are not.

### 6.5 Truncation — the part Sonarr has three fixtures for

Three test fixtures exist for nothing else: `TruncatedEpisodeTitlesFixture` (189 lines),
`TruncatedReleaseGroupFixture` (94), `TruncatedSeriesTitleFixture` (57). Their expectations encode five
distinct requirements, all of which this design must reproduce.

**(a) The budget is bytes, and it is computed twice.**

```text
componentBudget(i) = min(limits.MaxComponentBytes,
                         limits.MaxPathBytes − bytes(parentPath) − separators − Σ bytes(components ≠ i))
last component additionally subtracts bytes(extension)
```

Sonarr does exactly this: `maxPathSegmentLength = Math.Min(MaxFileNameLength, maxPath)` and, for the final
segment only, `maxPathSegmentLength -= extension.GetByteCount()` (`FileNameBuilder.cs:205-209`); and
`BuildFilePath` pre-subtracts the parent folder — `remainingPathLength = MaxFilePathLength -
seasonPath.GetByteCount() - 1` (`:237`). **Both are kept.** One bug is not: Sonarr computes the parent
folder's length *after* that folder was itself built and possibly truncated by a different code path, so
the two budgets can disagree. Here `RenamePlanner` materializes folder components first, measures the
result, and passes the measured value — one source of truth per plan.

**(b) The elastic token is derived, not hard-coded.** Sonarr computes
`maxEpisodeTitleLength = maxPathSegmentLength - GetLengthWithoutEpisodeTitle(component)` (`:211`) — it
renders the component once with the episode title blanked, subtracts, and re-renders. The mechanism is
right and the hard-coded token is not. Generalized:

```text
1. write the component with every Elastic and Droppable token blanked   → rigidBytes
2. slack = componentBudget − rigidBytes
3. if slack ≥ Σ natural length of elastic tokens → nothing to do
4. otherwise, in order:
     a. drop Droppable tokens, deepest Depth first, until it fits
     b. shrink Elastic tokens, deepest Depth first, each to its share of the remaining slack
     c. if it still does not fit, truncate the whole component
```

Deepest-first is the surveyed behavior and the intuitive one: the leaf title is the long, variable part,
and the library-entry title is the part a user scans a directory listing for. An explicit `:N` in the
template **pins** a token — it is capped at `N` and removed from the elastic pool, which is what makes
`{ReleaseGroup:12}` behave as `TruncatedReleaseGroupFixture` expects while the series title still absorbs
the rest.

**(c) Multi-byte, correctly.** Every cut goes through `TokenSanitizer.TruncateComponent` /
`TrimToUtf8Budget`, which already walks grapheme clusters (`Naming/TokenSanitizer.cs:381-395`) and
therefore never splits a surrogate pair and never separates a base letter from its combining mark.
`should_truncate_titles_measuring_series_title_bytes` and
`should_truncate_titles_measuring_episode_title_bytes_middle` (`TruncatedEpisodeTitlesFixture.cs:150`,
`:176`) are exactly this case and become two rows in a table-driven test.

**(d) Two units in one algorithm — fixed.** Sonarr's path budgets are bytes (`GetByteCount()`), while its
`:N` specifier is UTF-16 chars (`Truncate`, `FileNameBuilder.cs:1208-1218`: `input.Length <=
Math.Abs(maxLength)`). Resolution #10: **`:N` counts grapheme clusters** — what a user means by
"16 characters" — and the effective limit for a token is
`min(graphemeCap, byteBudgetShare)` **evaluated in bytes**. Both are honored and neither is guessed.

**(e) Range truncation over a set.** When an elastic token appears inside a `{span}` and the units'
combined values overflow, the writer collapses the set the way `GetEpisodeTitle` does
(`FileNameBuilder.cs:1040-1084`): first + `…` + last if both fit; else first + `…`; else the first,
truncated, + `…`. That behavior is worth keeping verbatim — it is what makes
`should_truncate_with_ellipsis_between_first_and_last_episode_titles` produce a name a human can read.

**(f) The ellipsis has no sentinel.** Sonarr replaces `...` with `{{ellipsis}}`, formats, then swaps back
(`:191`, `:218`, `:1069`, `:1224`), purely so its `FileNameCleanupRegex` — `([- ._])(\1)+`, `:74` — does not
eat the dots it just inserted. Because Arronix collapses over a fragment stream with provenance, an
`Inserted` fragment is exempt from collapsing by construction and no sentinel exists. The user-visible
ellipsis is `…` (U+2026), one grapheme and three UTF-8 bytes, configurable to `...` for users who want it.

**(g) Reverse truncation.** `:-N` keeps the tail: `{Release Group:-17}` →
`…ASixFourImpala`. Kept, and one surveyed off-by-one is not: Sonarr's
`{Series CleanTitle:-13}` yields `...Mr. Sisko` — twelve characters, not thirteen — because the reverse
path trims a separator *after* budgeting (`:1224`). Here the trim happens first and the budget is then
filled, so `:-13` yields thirteen graphemes.

### 6.6 The extension

The extension is not a token in the template and never has been in any surveyed app; it is appended after
materialization and its bytes are subtracted from the last component's budget. `{Ext}` exists as a host
global for the rare template that wants the extension *inside* a name (`Show - S01E01 [mkv].mkv`); using it
does not suppress the appended extension, and doing so would be a surprise, so `ARN0050`'s sibling warning
fires when a template ends with a literal `.` followed by `{Ext}`.

`TokenSanitizer.TruncateComponent` already refuses to keep an extension that would consume more than half
the budget (`:184-188`), which is the correct call for a pathological name and needs no change.

### 6.7 Uniqueness

After materialization, `TokenSanitizer.MakeUnique(fileName, isTaken, maxLengthInBytes)` numbers a colliding
name ` (2)`, ` (3)`, … fitting the number *inside* the budget rather than appending past it
(`:282-318`). Two subtleties:

1. `isTaken` must exclude the file being renamed, or every rename of a file to its own name numbers it.
2. A collision that `MakeUnique` resolves is **surfaced in the rename preview** as an informational row
   annotation, not hidden. Two units rendering the same name usually means the template is wrong
   (`ARN0040` should have caught it) or metadata is duplicated, and both are worth seeing.

---

## 7. Rename as an intent-level operation

### 7.1 The plan

```csharp
// src/Arronix.Host/Naming/IRenamePlanner.cs   (Tier B)
public interface IRenamePlanner
{
    /// <summary>Computes proposed paths for every file anchored under <paramref name="scope"/>.
    /// Read-only: touches no disk beyond existence probes for uniqueness.</summary>
    Task<RenamePlan> PlanAsync(MediaItemRef scope, RenameOptions options, CancellationToken ct = default);

    Task<RenamePlan> PlanForFilesAsync(IReadOnlyList<MediaFileId> files, RenameOptions options,
                                       CancellationToken ct = default);
}

public sealed record RenamePlan(MediaKindId Kind, IReadOnlyList<RenamePlanEntry> Entries);

public sealed record RenamePlanEntry
{
    public required MediaFileId File { get; init; }
    public required MediaItemRef Anchor { get; init; }
    /// <summary>Every unit the file satisfies, in canonical coordinate order. Length > 1 is the
    /// multi-unit case and is why the entry is keyed on the FILE, never on a unit.</summary>
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required string CurrentPath { get; init; }
    public required string ProposedPath { get; init; }
    public required bool Changed { get; init; }
    /// <summary>Which slot produced it — shown so a user can tell why two files named differently.</summary>
    public required string SlotId { get; init; }
    public IReadOnlyList<NamingDiagnostic> Notes { get; init; } = [];
}
```

### 7.2 Preview and bulk rename, rendered generically

The client must render this without knowing what a media kind is. Both primitives already exist in the
spec's §6 intent vocabulary; nothing new is invented.

**The action** (`PluginIntentSurface.Actions`, derived by the host — not declared by the plugin, per
spec #35, since it is computable from `Affordance.Renamable`):

```csharp
new ActionDescriptor
{
    ActionId = "rename", Name = "Rename files",
    Scope = ActionScope.Selection, TargetLevelId = shape.FileBinding.AnchorLevelId,
    Consequence = Consequence.Costly, Confirmation = ConfirmationRequirement.Acknowledge,
    ConsequenceStatement = "Files will be moved on disk. Hard links and seeding torrents may be affected.",
    LongRunning = true,
}
```

**The preview** is a `WorkbenchDescriptor` (spec §6.5) — the one primitive designed for *"a declared,
editable row/column grid over a … proposal"*:

```csharp
new WorkbenchDescriptor
{
    WorkbenchId = "rename-preview", Name = "Rename files",
    Subject = WorkbenchSubject.LibraryItems,
    TargetLevelId = shape.FileBinding.AnchorLevelId,
    Columns =
    [
        new WorkbenchColumn { Field = Fields.CurrentPath, Editable = false },   // FieldValueKind.FilePath
        new WorkbenchColumn { Field = Fields.ProposedPath, Editable = false },
        new WorkbenchColumn { Field = Fields.Units,        Editable = false },  // Count, Prominence.Secondary
        new WorkbenchColumn { Field = Fields.Slot,         Editable = false },  // Text, Prominence.Detail
    ],
    Inputs = [],                       // the scope arrives as the request path
    CommitLabel = "Rename",
    CommitConsequence = Consequence.Costly,
    CommitConfirmation = ConfirmationRequirement.Acknowledge,
    AllowsRowExclusion = true,
}
```

`AllowsRowExclusion = true` delivers something no surveyed app has: **per-file opt-out**. Sonarr, Radarr,
Lidarr and Readarr all offer rename-everything-under-this-item or nothing
(`RenameEpisodeFileService.cs:20-22` — three overloads, all whole-scope). The workbench primitive gives
partial rename for free, and it is the single most-requested behavior in this subsystem.

The plan is produced host-side, so the workbench is served by `WorkbenchBroker` (spec WP-8) directly rather
than through `IMediaItemSource.ProposeAsync`. This is the one workbench whose proposal the host owns, and
it is worth noting so an implementer does not go looking for the plugin seam.

### 7.3 Interaction with the unit↔file binding

The join is `UnitFileLink(Unit, File, Ordinal?)` (spec §4.6), and rename must respect five properties of it.

1. **The plan is keyed on the file, never on a unit.** With `AtMostOneUnitPerFile == false` a unit-keyed
   plan produces N entries proposing N different names for one file. `RenameEpisodeFileService` iterates
   files and gathers `episodesInFile` for exactly this reason (`:99-108`).
2. **The folder comes from the anchor; the name comes from the units.** `FileBinding.AnchorLevelId` and
   `UnitLevelId` differ in one surveyed kind and it is not an accident: Lidarr's `TrackFile.AlbumId`
   anchors the file at the acquisition unit while `Track.TrackFileId` satisfies the completeness unit
   (`lidarr-music.md` §1.2). So the level-folder templates walk the ancestor chain of the **anchor**, and
   the file-name template's `{span}` iterates the **units**.
3. **Rename never edits the binding.** A preview that could change which units a file satisfies is a manual
   import, and that is a different workbench with a different consequence. `RenamePlanEntry` carries paths
   only.
4. **`Ordinal` is a value, not a decision.** When `OrdinalIsMeaningful`, `{Part Number}` reads
   `UnitFileLink.Ordinal` and `{Part Count}` reads the link count for that unit (D13). Reordering parts is,
   again, a different operation.
5. **A file whose units span an anchor boundary cannot exist**, because `InMemoryMediaStore` enforces
   `SpanConstraint` on `LinkAsync` (spec §4.6). The writer may therefore assume `Units` share an anchor,
   and asserts it rather than defending against it.

**Ordering of the commit** matters and is stated: entries are applied **deepest path first** so that a
folder rename never invalidates a pending file path, and a folder that becomes empty is removed only after
every entry beneath it succeeded. A partial failure leaves a consistent tree and reports which entries
applied — the plan is a list of independent moves, not a transaction, and pretending otherwise on a file
system would be a lie.

### 7.4 `IRenamePolicy` and `ILibraryLayout` — cut to what they actually are

Both are 0.1.0 contracts gated on `renaming`, both have an `IPluginRegistry` method (spec §3.4), and both
carry members no plugin instance is ever asked to run. An earlier draft of this section left those members
in place and recorded them as a wart "with a 1.0 disposition", on the reasoning that the surface could not
change before 1.0. It can — `docs/contracts/stability.md` — and leaving them would ship four plugins
carrying dead method bodies, a host projection implementing two methods nobody calls, and a `renaming`
capability that grants more than it means. **They are cut now, in this milestone.**

#### `IRenamePolicy` → `INamingTokenContributor`

```csharp
// src/Arronix.Abstractions/Naming/INamingTokenContributor.cs   (renamed from IRenamePolicy)
public interface INamingTokenContributor
{
    MediaKindId MediaKind { get; }
    Task<IReadOnlyDictionary<string, string>> ResolveTokensAsync(MediaItemId itemId, CancellationToken ct = default);
}
```

`GenerateFileNameAsync` and `ValidateTemplate` are **deleted**, and the interface is renamed to what is
left: a token contributor. A plugin that both defines and applies naming is a formatter with no validation,
which is what resolution #12 of the extraction plan refused — so those two methods were never going to be
called, and an interface member that is never called is not a reserved seam, it is a promise the host
cannot keep. `ResolveTokensAsync`'s signature *is* a token contribution (`{name → value}`) and it is the
seam for rule D16: the host merges its result over the derived bindings and refuses any key not declared as
a contributed token in `MediaShape.Tokens` (which load-pipeline step 13 has already cross-checked). An
undeclared key is dropped with a `Warning`-level telemetry event naming the plugin and the key.

The host still **projects** the full naming behavior, exactly as it projects `IMediaKind` from shape +
manifest (spec §3.2); `RenamePolicyProjection` becomes an internal host type (`Arronix.Host/Naming/`) with
no contract counterpart, since nothing outside the host ever needed to see it.

**Cost, stated:** `Arronix.Plugin.{Tv,Movies,Music,Books}` each implement the three-method interface today.
Cutting it deletes two method bodies from each and updates one registration call — the compiler names every
site. Doing it after a third-party plugin exists would cost what it costs today plus a coordination problem;
there is no third-party plugin, so it costs the four edits.

#### `ILibraryLayout` and `LibraryPathSpec`

`LibraryPathSpec` (`DTOs/LibraryPathSpec.cs`) carries a **single** `FolderTemplate`, which cannot express
series→season, artist→album or author→book — every surveyed kind needs at least two folder levels. The real
layout is `NamingProfile.Layout`, an ordered list of `NamingSlot`s (§8.1). **`LibraryPathSpec` carries that
list**; `FolderTemplate` and `CustomTokens` are deleted.

`LibraryLayoutProjection` stays — it is the host's `ILibraryLayout` implementation and something must be
there when a plugin registers no layout — but the **back-mapping inside it is deleted**. An earlier draft
had it map the *leaf* folder template onto `FolderTemplate` "for the benefit of anything reading the stable
contract", leaving `CustomTokens` null. That is a bridge between an old shape and a new one, written for
readers that do not exist, and it is **silently lossy**: collapsing a multi-level layout into one string
means anything that ever did read `FolderTemplate` would get a *wrong* answer rather than no answer. Wrong
is worse than absent. The projection now works in ordered slots end to end, with nothing to collapse.

`CustomTokens` is not merely unpopulated. `Arronix.Plugin.Tv`'s layout reads it today, as an untyped
`"true"`/`"false"` flag deciding whether to emit the season folder — which is exactly the failure mode the
property invites: a layout decision expressed as an arbitrary string pair that no validator can see. Under
the ordered-slot list that decision is a slot that is present or absent, and it is validated like every
other slot.

**Cost, stated:** all four plugins implement `ILibraryLayout` (`TvNaming.cs`, `MoviesNaming.cs`,
`MusicNaming.cs`, `BooksNaming.cs`), so this is one record edit plus four small plugin edits, and the TV
flatten flag becomes a declared slot.

---

## 8. Configuration and the wire surface

### 8.1 `NamingSlot` — plugin declares the slots, the user fills them

`lidarr-music.md` §6 asks for *"the naming seam must let a plugin choose which pattern to apply per item,
not just which tokens to substitute."* This design gives the plugin the *declaration* and keeps the
*choosing* mechanical, because a plugin callback selecting a template is a policy nothing can validate.

```csharp
// src/Arronix.Abstractions/Naming/NamingSlot.cs
[Experimental(ExperimentalContracts.Naming, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record NamingSlot
{
    public required string SlotId { get; init; }             // "standard", "dated", "sequenceless", "multi-part"
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required NamingSlotTarget Target { get; init; }
    /// <summary>Required for LevelFolder; names the level whose folder this is.</summary>
    public MediaLevelId? LevelId { get; init; }
    /// <summary>Required for SequenceFolder.</summary>
    public string? SequenceAxisId { get; init; }
    /// <summary>Null means "always applies" — the fallback. Exactly one such slot per target is required.</summary>
    public NamingGuard? Guard { get; init; }
    /// <summary>Compiled at plugin load; zero Error diagnostics or the plugin is quarantined (§5.4).</summary>
    public required string DefaultTemplate { get; init; }
    /// <summary>Lower first. Ties are a shape defect, not a coin toss.</summary>
    public int Order { get; init; }
}

public enum NamingSlotTarget { FileName = 0, LevelFolder = 1, SequenceFolder = 2 }

/// <remarks>
/// A tagged record with a <c>Kind</c> discriminator and nullable payload slots, per spec #6 — no
/// <c>[JsonPolymorphic]</c>, because this value reaches the browser.
/// </remarks>
public sealed record NamingGuard
{
    public required NamingGuardKind Kind { get; init; }
    public string? SpaceId { get; init; }
    public string? ComponentId { get; init; }
    public string? FieldId { get; init; }
    public string? Value { get; init; }
    public string? FormatFamilyId { get; init; }
    public int Threshold { get; init; }
    public bool Negated { get; init; }
}

public enum NamingGuardKind
{
    Always = 0,
    /// <summary>The unit carries a reading in this coordinate space.</summary>
    CoordinatePresent = 1,
    /// <summary>EVERY unit bound to the file carries one. Sonarr's anime fallback, exactly.</summary>
    CoordinatePresentForAllUnits = 2,
    /// <summary>Distinct values of a component across the parent's children ≥ Threshold.</summary>
    DistinctComponentCountAtLeast = 3,
    /// <summary>Units bound to this file ≥ Threshold.</summary>
    UnitCountAtLeast = 4,
    /// <summary>Files bound to this unit ≥ Threshold.</summary>
    FileCountForUnitAtLeast = 5,
    FieldEquals = 6,
    FormatFamilyIs = 7,
}
```

All four surveyed dispatches, as declared data:

| Surveyed dispatch | Guard |
|---|---|
| Sonarr daily format (`FileNameBuilder.cs:168-170`) | `CoordinatePresent { SpaceId = "airdate" }` |
| Sonarr anime format, **including its graceful degradation** (`:173-177` — *"use anime format only if every episode actually has an absolute number"*) | `CoordinatePresentForAllUnits { SpaceId = "absolute" }` |
| Sonarr specials folder (`SpecialsFolderFormat`, `:294`) | not a slot at all — D8 makes the `SequenceException(0, "Specials")` the *value* of `{Season}` |
| Lidarr multi-disc format (`Lidarr FileNameBuilder.cs:107-110`) | `DistinctComponentCountAtLeast { SpaceId = "medium-track", ComponentId = "medium", Threshold = 2 }` |
| Readarr multi-part naming (`Readarr FileNameBuilder.cs:322-327`) | `FileCountForUnitAtLeast { Threshold = 2 }` |
| Readarr ebook vs audiobook (implicit today, via quality bands) | `FormatFamilyIs { FormatFamilyId = "audiobook" }` |

`NamingSlotSelector` evaluates guards in `Order` and takes the first match, falling back to the
`Guard == null` slot. Guards are pure functions of the shape, the unit set and the file record — no I/O,
no plugin call, deterministic, and therefore reproducible in a preview.

Sonarr's `SpecialsFolderFormat` disappearing is worth a sentence: it exists only because Sonarr has no way
to say "season 0 is called Specials". `SequenceException(0, "Specials", ExcludedFromCompleteness: true)`
says it once, and the same declaration already fixes completeness counting (spec §4.6). One config field
and one naming branch removed by a declaration that had to exist anyway.

### 8.2 `NamingProfile`

```csharp
// src/Arronix.Host/Naming/NamingProfile.cs   (Tier B — host-side; the client sees NamingProfileView)
public sealed record NamingProfile
{
    public required MediaKindId Kind { get; init; }
    /// <summary>When false, imported files keep their original name — Sonarr's <c>RenameEpisodes</c>.</summary>
    public bool RenameFiles { get; init; }
    /// <summary>slotId → the user's template. A missing entry falls back to the slot's default.</summary>
    public required IReadOnlyDictionary<string, string> Templates { get; init; }
    public required IReadOnlyList<SubstitutionRule> Substitutions { get; init; }
    /// <summary>Elide a level's folder entirely — Sonarr's <c>SeasonFolder</c> bit, generalized.</summary>
    public IReadOnlyList<MediaLevelId> SuppressedFolderLevels { get; init; } = [];
    public IReadOnlyList<string> SuppressedSequenceFolders { get; init; } = [];
}
```

One profile per kind, stored by `INamingProfileStore` (in-memory now, relational at the storage milestone).
Every write goes through `INamingTemplateCompiler`; a profile with an `Error` diagnostic is rejected at the
API boundary and never persisted, which is resolution #19's second gate.

### 8.3 Samples

`NamingSampleService` renders each slot's template against **synthetic** bindings so the settings editor
shows output before any library exists. Sonarr does this with hand-built static objects
(`FileNameSampleService.cs:43-120` — three `Series`, three `Episode`, five `EpisodeFile` instances, all
literal). Generalized: bindings are synthesized from the shape itself —
`FieldDescriptor.Name` and the token's `NamingToken.ExampleValue` supply text, coordinate spaces supply
`OrdinalPath(1, 1)` and `OrdinalPath(1, 2)` for a two-unit sample, `SequenceException` values supply the
exception sample. No per-kind sample data, and adding a media kind adds no sample code.

Five samples are produced per kind, mirroring what the four *arrs each hand-wrote: one unit, multiple
units, each guarded slot that can be triggered, the library-entry folder, and each sequence folder
including its exception.

### 8.4 Wire surface

```csharp
// src/Arronix.Abstractions/Wire/NamingCatalogView.cs   (ARX0017)
public sealed record NamingCatalogView(
    MediaKindId Kind,
    IReadOnlyList<NamingToken> Tokens,
    IReadOnlyList<NamingSlot> Slots,
    IReadOnlyList<NamingModifierInfo> Modifiers);

public sealed record NamingModifierInfo(string Id, string Name, string Description,
                                        IReadOnlyList<FieldValueKind> LegalOn);

public sealed record NamingProfileView(
    MediaKindId Kind, bool RenameFiles,
    IReadOnlyDictionary<string, string> Templates,
    IReadOnlyList<SubstitutionRule> Substitutions,
    IReadOnlyList<string> SuppressedFolderLevels,
    IReadOnlyList<string> SuppressedSequenceFolders);

public sealed record NamingPreviewView(
    string Template, string Sample, NamingDiagnosticReport Diagnostics);
```

Endpoints on `Arronix.Api` (WP-11's `KindEndpoints` and a new `NamingEndpoints`):

```text
GET  /api/v1/kinds/{kind}/naming/catalog          → NamingCatalogView
GET  /api/v1/kinds/{kind}/naming/profile          → NamingProfileView
PUT  /api/v1/kinds/{kind}/naming/profile          → NamingProfileView | 400 + NamingDiagnosticReport
POST /api/v1/kinds/{kind}/naming/preview          → NamingPreviewView          (live, per keystroke, debounced)
GET  /api/v1/kinds/{kind}/items/{id}/rename       → WorkbenchProposal          (rename-preview)
POST /api/v1/kinds/{kind}/workbenches/rename-preview/commit  → ActionResult
```

The client renders a token picker from `NamingCatalogView` — grouped by `Origin`, ordered by `Depth` then
`Prominence`, each entry showing `NamingToken.Description` and `NamingToken.ExampleValue`. That is the
whole reason `NamingToken` is reused and widened rather than replaced by a third representation (spec #44),
and it is the whole reason a bare `string[]` in the manifest is insufficient. Because it was *widened* and
not *wrapped*, a client reads `tokens[i].description` rather than `tokens[i].token.description`
(§2.2).

**Nothing in this surface names a UI technology.** `NamingCatalogView` is a vocabulary; a CLI prints it as
a table, a TUI as a completion list, a web client as a picker. `IntentVocabularyTests` (spec §7.5) passes
without an allow-list entry.

---

## 9. Test corpus

| Fixture | Asserts | Derived from |
|---|---|---|
| `NamingTokenDeriverTests` | the four reference shapes derive exactly the expected descriptor sets, cell for cell | spec §2.12's acceptance table |
| `DerivationReproducesSurveyedTokensTests` | for each of the four *arrs, every one of its 161 handlers is either reproduced by a rule, reproduced by a modifier, or listed in a documented `NotCarried` set with a reason | the greps in this document's header |
| `NamingTokenNameTests` | canonicalization folds `{Release Group}`/`{ReleaseGroup}`/`{release.group}` together | `FileNameBuilderTokenEqualityComparer.cs:28` |
| `TokenCollisionTests` | rules 1–4; specifically that a shape with a level and an axis of the same name is `PluginShapeInvalid` | §5.3 |
| `NamingTemplateParserTests` | the EBNF, including nested `<…>`, `{span}` head/tail/`range`, `{{`/`}}`, both path separators | §3.2 |
| `NamingDiagnosticCorpusTests` | one malformed template per `NamingDiagnosticCode`, each producing that code **and only that code**, with the expected `Span` | §4.3 |
| `MultiUnitSpanTests` | the six `MultiEpisodeStyle` outputs, reproduced from template text | `MultiEpisodeFixture.cs` (284 lines) |
| `ModifierCrossProductTests` | the 12 title-variant combinations reproduce Sonarr's 12 handlers | `CleanTitle*`/`Title*` fixtures (12 files) |
| `TruncationTests` | every case in the three truncation fixtures, plus the `:-13` off-by-one **corrected** | `Truncated*Fixture.cs` |
| `PathMaterializerTests` | substitution, collapsing across fragment boundaries, reserved names, trailing dot/space, `<>` never surviving | `ColonReplacementFixture.cs`, `ReservedDeviceNameFixture.cs`, `ReplaceCharacterFixure.cs`, `CleanFilenameFixture.cs` |
| `NamingSlotSelectorTests` | each of the six surveyed dispatches, including anime's graceful degradation | §8.1's table |
| `RenamePlannerTests` | file-keyed entries; anchor≠unit folder resolution; ordinal parts; deepest-first commit ordering; self-rename does not number itself | `RenameEpisodeFileService.cs`, `BuildFilePathFixture.cs` |
| `RoundTripWarningTests` | `ARN0060` fires when the matcher loses a coordinate, and is skipped when no matcher is registered | `FileNameValidationService.cs` |
| `NamingWireRoundTripTests` | every `NamingDiagnostic`, `NamingSlot`, `NamingGuard` and `NamingToken` round-trips byte-identically under the server's and the client's `JsonSerializerOptions` | spec §7.5's `WireRoundTripTests` pattern |

`NamingDiagnosticCorpusTests` is the fixture that discharges resolution #12's original objection. Until it
is green there is a formatter without validation, and that is what was refused.

---

## 10. Work packages

Same partition rule as the unified-host spec: each WP owns one directory tree, no file is written twice,
every WP builds its own `.csproj` to zero warnings under `TreatWarningsAsErrors` / `AnalysisLevel=6.0-all`.

| WP | Owns | Creates / edits | Depends on |
|---|---|---|---|
| **N-1** | `src/Arronix.Abstractions/Naming/` + governance | **edits** `DTOs/NamingToken.cs` (widened in place per §2.2 — the four 0.1.0 members become init-only properties and the validation members are added; **no** `NamingTokenDescriptor` is created); `NamingTokenOrigin.cs`, `NamingElasticity.cs`, `NamingSlot.cs`, `NamingGuard.cs`, `NamingDiagnostic.cs`, `NamingDiagnosticCode.cs`, `NamingDiagnosticReport.cs`, `NamingSpan.cs`; **edits** `docs/contracts/stability.md` (0.3.0 history row under the existing `ARX0009` entry) | unified-host WP-2 (`Shape`) |
| **N-2** | `src/Arronix.Abstractions/Shape/` — **two files only** | **edits** `SequenceAxis.cs` (`PolicyFields`), `FormatFamily.cs` (`TechnicalFacets`) — see §12.1. Coordinate with the owner of unified-host WP-2; if that WP is unstarted, fold N-2 into it | WP-2 |
| **N-3** | `src/Arronix.Common/Naming/` — **new files only** | `NamingTokenName.cs`, `PathLimits.cs`, `SubstitutionMap.cs`, `PathMaterializer.cs`, `Templates/*` (8 files). `TokenSanitizer.cs` and `TextFolding.cs` are **not edited** | N-1 |
| **N-4** | `src/Arronix.Host/Naming/` | `NamingTokenDeriver.cs`, `HostGlobalTokens.cs`, `ITokenRegistry.cs`, `TokenRegistry.cs`, `NamingTemplateCompiler.cs`, `NamingSlotSelector.cs`, `NamingBindingBuilder.cs`, `NamingProfile.cs`, `INamingProfileStore.cs`, `InMemoryNamingProfileStore.cs`, `NamingSampleService.cs`, `RenamePolicyProjection.cs`, `LibraryLayoutProjection.cs` (slots end to end; no `FolderTemplate` back-mapping), `Composition/NamingRegistration.cs`. **Edits in Abstractions (§7.4):** `Naming/IRenamePolicy.cs` → `Naming/INamingTokenContributor.cs` with `GenerateFileNameAsync` and `ValidateTemplate` deleted; `DTOs/LibraryPathSpec.cs` carries the ordered `NamingSlot` list and drops `FolderTemplate` + `CustomTokens`; `IPluginRegistry.AddRenamePolicy` renamed; the four `Arronix.Plugin.*/*Naming.cs` follow | N-3, unified-host WP-8 |
| **N-5** | `src/Arronix.Host/Media/ValidatedShape.cs` — **one method** | adds rule 4 (duplicate derived token) to the defect list; adds default-template compilation to step 11 | N-4 |
| **N-6** | `src/Arronix.Host/Naming/RenamePlanner.cs` + `IRenamePlanner.cs` + the rename workbench in `Intent/WorkbenchBroker.cs` | the plan, the workbench proposal, the commit path | N-4, unified-host WP-8 |
| **N-7** | `src/Arronix.Abstractions/Wire/` — **new files only** | `NamingCatalogView.cs`, `NamingProfileView.cs`, `NamingPreviewView.cs`, `NamingModifierInfo.cs` | N-1 |
| **N-8** | `src/Arronix.Api/Endpoints/NamingEndpoints.cs` | the six routes in §8.4 | N-6, N-7, unified-host WP-11 |
| **N-9** | `src/Arronix.Client/Naming/` | the template editor, token picker and rename workbench view — all generic over `NamingCatalogView` | N-7, unified-host WP-12 |
| **N-10** | `src/Arronix.Common.Tests/Naming/` — **new files only** | `NamingTokenNameTests`, `NamingTemplateParserTests`, `PathMaterializerTests`, `TruncationTests`, `SubstitutionMapTests` | N-3 |
| **N-11** | `src/Arronix.Host.Tests/Naming/` | `NamingTokenDeriverTests`, `DerivationReproducesSurveyedTokensTests`, `TokenCollisionTests`, `NamingDiagnosticCorpusTests`, `MultiUnitSpanTests`, `ModifierCrossProductTests`, `NamingSlotSelectorTests`, `RenamePlannerTests`, `RoundTripWarningTests` | N-6, unified-host WP-13..16 |
| **N-12** | `src/Arronix.Api.Tests/Naming/` | `NamingWireRoundTripTests`, `NamingEndpointTests` | N-8 |

**Waves.** `N-1 ‖ N-2` → `N-3 ‖ N-7` → `N-4` → `N-5 ‖ N-6 ‖ N-10` → `N-8 ‖ N-9` → `N-11 ‖ N-12`.

**Ordering against the unified-host milestone.** N-1/N-2 must land before or with WP-2; N-4 onwards depend
on WP-8. The cheapest sequencing is to treat N-1 and N-2 as amendments *inside* WP-2 and start the rest
after wave 5. Nothing here blocks waves 0–4.

---

## 11. Deferred, with the trigger

| Deferred | Where it would live | Trigger |
|---|---|---|
| **`IFileTechnicalProbe` and the `{MediaInfo …}` values** | `Arronix.Host/Media/` | The import milestone. `FormatFamily.TechnicalFacets` ships **now** as a declaration so the token names exist and validate; the probe that populates them is a separate subsystem with a native dependency. Until then those tokens resolve `Absent` and a template using one gets an `Information` diagnostic. |
| **`{Custom Formats}` values** | the custom-format calculator | The quality milestone. The token is a host global today, declared and validated, resolving empty. |
| **`{Preferred Words}`** | — | A preferred-word feature exists. Reserved now so a plugin cannot claim the name (§2.3). |
| **Persisted `NamingProfile`** | `storage-layer.md` | The storage milestone. `INamingProfileStore` is the seam; `InMemoryNamingProfileStore` is the implementation. |
| **Localized token names and descriptions** | — | A second locale, and the `ILocalizedStrings` seam the spec already defers. Token *canonical names* are locale-invariant by construction, so localization is additive and affects display only. |
| **Nested spans** | `NamingTemplateParser` | A kind whose file legitimately spans two coordinate axes. `ARN0032` today. |
| **Per-root-folder naming profiles** | `NamingProfile` gains a key | A user with two libraries wanting two schemes. Purely additive: the profile store's key widens. |
| **Token aliases** | `TokenRegistry` | A migration from another product. There is no upgrade path (product decision), so there is nothing to alias to. |
| **Importing a legacy *arr* naming template** | a separate, later, version-specific migration script — **never the grammar** | A user migrating from a specific *arr version, once such a script exists. It rewrites `{series title}` → `{Series Title:lower}`, `{Series.Title}` → `{Series Title:dot}` and `{CleanTitle}` → `{Series Title:clean}` mechanically and emits Arronix templates. This is why §3.5 carries **one** casing mechanism: a converter run once beats a second grammar carried forever, and it is also where a token-alias table would live if one is ever needed. |
| **Plugin-contributed modifiers** | — | **Never.** An open transform vocabulary is a plugin-supplied formatter with no validation, which is the thing resolution #12 refused. Kind-specific transforms are fields (§3.5). |
| **Rename-time hard-link / seeding awareness** | `acquisition-pipeline.md` | Owned there. `ActionDescriptor.ConsequenceStatement` warns the user today. |

---

## 12. Requested amendments to `unified-host-runtime.md`

Both are **additive** to `Arronix.Abstractions.Shape` (`ARX0013`) and both are forced by surveyed tokens
that have nowhere else to come from. Neither changes an existing member.

### 12.1 The two shape additions

```csharp
// src/Arronix.Abstractions/Shape/SequenceAxis.cs   — ADD
/// <summary>
/// Fields on the per-(parent, coordinate) policy record. Populated only when
/// <see cref="HasPolicyRecord"/> is true.
/// </summary>
public IReadOnlyList<FieldDescriptor> PolicyFields { get; init; } = [];

// src/Arronix.Abstractions/Shape/FormatFamily.cs   — ADD
/// <summary>
/// Technical properties a file of this family carries, declared so the host can name them without
/// knowing what they mean. Populated by a host-side probe (deferred, §11).
/// </summary>
public IReadOnlyList<FieldDescriptor> TechnicalFacets { get; init; } = [];
```

`PolicyFields` also corrects an error in the spec's §2.12 acceptance table, which records the music
`medium` axis as `HasPolicyRecord = false`. Lidarr's `Medium` is `{ Number, Name, Format }`
(`Music/Model/Medium.cs`) and its `{Medium Name}` / `{Medium Format}` tokens read it directly
(`Lidarr Organizer/FileNameBuilder.cs:345-348`). It **is** a policy record; the cell should read `true`
with `PolicyFields = [Name, Format]`.

### 12.2 The three clarifications

| # | Spec statement | Amendment |
|---|---|---|
| 1 | §1 #43 rule 3: *"The same token for the same media kind cannot arise."* | True across plugins, false within one shape. Add rule 4 (§5.3) and the `ShapeDefect.DuplicateNamingToken` defect. |
| 2 | §1 #44 / §2.10: `MediaShape.Tokens` is *"the source of truth"* for the token set. | The **shape** is the source of truth; `MediaShape.Tokens` narrows to the contributed extras the derivation cannot produce, and the host publishes `derived ∪ contributed ∪ globals`. Restating a derivable token becomes `ARN0101`. |
| 3 | §4.1 pipeline step 11 (`ValidatedShape.TryValidate`) | Also runs token derivation and compiles every `NamingSlot.DefaultTemplate`. A plugin whose own default template does not validate is quarantined at load, not at a user's settings page. |

---

## 13. Deviations from `ARCHITECTURE.md` §9

| # | §9 statement | Deviation | Justification |
|---|---|---|---|
| 1 | *"Plugins declare tokens (`{SeriesTitle}`, `{EpisodeNumber}`, etc.)"* | Plugins declare a **shape**; the host derives the tokens. Declaration narrows to what cannot be derived, expected to be empty for all four reference plugins. | The spec's own #35 principle: a declaration that can be derived is a declaration that can disagree. 161 hand-written handlers across four apps are 161 opportunities for exactly that disagreement. |
| 2 | *"Core … validates naming templates only use tokens from the target media kind + globally allowed tokens"* | Implemented as stated, **plus** level scoping, format-specifier legality, modifier legality, structural obligations per slot, and a round-trip warning. | Token membership alone accepts `{Episode Title}` in a library folder and `{Series Title:00}` everywhere; the surveyed apps each hand-wrote the missing checks, three times over in Sonarr's case. |
| 3 | *"Conflicting token names across plugins are namespaced or rejected (strategy under evaluation)"* | Neither namespaced nor merely rejected: four rules (§5.3), two enforcement points, `PluginTokenConflict` 2006 and `PluginShapeInvalid` 2009. | Namespacing was already rejected by spec #43; the fourth rule is new and catches the case that rule genuinely misses. |
| 4 | *"Provides a formatting pipeline (token resolution, sanitization, collision handling, truncation rules)"* | Delivered in the order **validation → derivation → formatting**, and split across three assemblies by what each needs. | Formatting first is what the extraction plan's resolution #12 refused, by name. |
| 5 | §4.1 manifest example writes `"tokens": ["{SeriesTitle}"]` | Already amended by the unified-host spec to `NamingToken[]`; this design further narrows the array's meaning to contributed extras. | The manifest claim becomes checkable against a computed truth, which is the §4.1 step-4 check the spec wanted and could not perform. |

---

## 14. Summary of the position

The token system's difficulty has never been formatting. It is that four applications each maintain a
hand-written vocabulary of ~40 tokens with no machine-readable statement of what a token *is*, so every
validation rule has to be hand-written per kind — three near-identical validators in one file
(`FileNameValidation.cs:74-132`), five regexes, two bespoke `Requires*` methods, five caches, and a
sentinel-swap to protect an ellipsis from a cleanup regex.

The shape model removes the premise. A media kind that has declared its levels, its coordinate spaces, its
fields, its sequence and grouping axes, its external identifier schemes and its file binding has already
said everything a token vocabulary needs to know — and it said it in a form that carries value kinds,
ordering, injectivity and depth. Derivation turns that into `NamingToken`s — one record, widened in place,
not a stable four-member DTO with a validator's wrapper around it; the tokens turn every hand-written
validation rule into a predicate; and the predicates are what make a formatter safe to ship.
