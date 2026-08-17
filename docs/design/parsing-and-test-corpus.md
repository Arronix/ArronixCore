# Release parsing and the golden-test corpus — Design (v0.3.0 milestone)

> **Status:** Design. No code has been written. This document is the direct input to the implementation
> phase for the parsing seam and for `ARCHITECTURE.md` §14's "Policy Golden Tests", which that document
> names and does not specify.
>
> **Scope:** the split between media-agnostic and per-kind parsing; the parse target under the shape
> model; the shared preprocessing chain; the golden-test corpus (format, sourcing, attribution, runner);
> cross-kind negative testing; regression discipline; and the performance controls. Purely additive.
>
> **Empirical basis:** the four real parsers and their test suites —
> `src/NzbDrone.Core/Parser/` (Sonarr, 3,656 lines) and `src/NzbDrone.Core.Test/ParserTests/`
> (26 fixtures, 1,511 `[TestCase]` rows); `_reference/Radarr` (2,652 / 14 / 1,037);
> `_reference/Lidarr` (2,778 / 11 / 440); `_reference/Readarr` (1,987 / 9 / 300);
> plus `_reference/Prowlarr/src/NzbDrone.Core/Parser/` (1,313 lines, no title parser at all).
> Every measurement below was taken on this machine against these sources; the method is stated inline.
>
> **Governing documents:** `docs/design/unified-host-runtime.md` (authoritative for the milestone),
> `ARCHITECTURE.md` §4.2/§9/§11/§14, `docs/arronix-common-extraction-plan.md` (three-tier promotion
> rule), `docs/contracts/stability.md` (experimental gate).
>
> **Where this document contradicts the unified-host spec, it says so explicitly and gives the evidence.**
> There is exactly one such place: resolution #1.

---

## How the contested calls were resolved

Format matches `docs/arronix-common-extraction-plan.md` §"How the three proposals were resolved" and
`docs/design/unified-host-runtime.md` §1. Every contested decision gets one line of reasoning.

| # | Question | Resolution | One-line reason |
|---|----------|------------|-----------------|
| 1 | Is the parse seam the 0.1.0 `IReleaseParser` producing `ParsedRelease`, as `unified-host-runtime.md` §2.11 states? | **No — `IReleaseTitleParser` producing `ReleaseReading` *replaces* it. The 0.1.0 pair is deleted, not bridged** | `ParsedRelease` (`DTOs/ParsedRelease.cs`) has ten string-ish slots and cannot express a coordinate, a multi-unit fan-out, a confidence, a provenance or a cross-kind claim — and `MatchRequest.Coordinates` (spec §2.11) has **no producer at all** without one. Two parse seams means two vocabularies, two registration paths and a lossy conversion in the hot path of every quality evaluation; a contract that cannot express the domain is replaced (§3.5). |
| 2 | Does parsing get a sixth contract area (`ARX0018`)? | **No — the new types live in `Arronix.Abstractions.Shape` under `ARX0013`** | The parse target *is* the shape's coordinate vocabulary (`CoordinateSet`, `CoordinateReading`, `AcquisitionScope`); splitting one vocabulary across two diagnostic ids buys nothing and costs a sixth set of four governance edits. The `Arronix.Abstractions.Parsing` namespace goes away entirely when `IReleaseParser` is deleted (§3.5). |
| 3 | Where does the media-agnostic tag extractor live — a contract, or an implementation? | **Implementation, in `Arronix.Host/Parsing/`; only the DTO (`ReleaseTags`) is promoted** | Tier B by the promotion rule: one implementer, no plugin ever implements it, and the host must run it *before* dispatch so a plugin could not own it even if it wanted to. |
| 4 | Does a plugin parser see the raw title or a preprocessed one? | **Both, in one `ReleaseParseRequest`** | Sonarr's `ParseTitle` needs `releaseTitle` for the release group and `simpleTitle` for the coordinate regexes (`Parser.cs:698` vs `:729`, `:757`); handing over only one forces the plugin to redo work the host already did. |
| 5 | Is the preprocessing chain declared per-kind or fixed? | **Fixed, ordered, host-owned; per-step enablement is *derived from the validated shape*, never from a kind name** | All four apps run a byte-for-byte identical chain in the same order (`Parser.cs:666-706` vs Radarr `Parser.cs:169-223`); the only variation is which steps matter, and that is derivable (a `Date` coordinate space enables the six-digit air-date step). |
| 6 | Are the pre-substitution rewrites (`ParserCommon.PreSubstitutionRegex`) agnostic? | **No — per-kind, owned by the plugin** | All 14 of Sonarr's rewrite a CJK or Korean release into `S..E..` form (`ParserCommon.cs:11-49`); Radarr's array is **empty**. They are coordinate-injection rules wearing a normalization costume. |
| 7 | Within one kind, does a parser return alternative readings? | **No — exactly one reading** | Sonarr's ambiguity is resolved by *library context* inside `ParsingService.GetEpisodes` (`ParsingService.cs:222`), which the parser does not have; alternatives would move arbitration into the layer least able to do it. Multi-*space* readings are a `CoordinateSet`, which is what it is for. |
| 8 | How is a release naming many units represented? | **`ReleaseReading.Units` — a list of `(AcquisitionScope, CoordinateSet, ordinal)`** | One list covers all four surveyed shapes: `S01E01E02` → three `Single`s, a season pack → one `SequenceSpan`, a discography → one `Ancestor`, a book omnibus → one `Single` with `ordinal` meaningful (spec §2.12 `OrdinalIsMeaningful`). |
| 9 | New enum for "how strongly does this kind claim this title"? | **No — reuse `MatchConfidence`** | `{None, Low, Medium, High, Exact}` already ships, ordered, and is already live on `IReleaseMatcher.MatchCandidate.Confidence`; inventing a parallel five-value ladder is the smell the house style exists to stop. It is currently declared in `DTOs/MatchDecision.cs`; `acquisition-pipeline.md` §9 deletes the `MatchDecision` record and moves the enum to `DTOs/MatchConfidence.cs`, which this document depends on but does not own. |
| 10 | Does the host run every kind's parser on every release? | **No — search-scoped first, then a category gate, then strict-highest claim, then a matcher tie-break, then reject** | Measured: **130 of 159** Radarr movie test titles and **53 of 120** Lidarr music test titles produce a *TV coordinate* from Sonarr's first matching report-title regex. Kind-disjointness does not exist at the regex layer; "whichever parser matches" is not a routing rule. |
| 11 | `ReleaseCandidate.MediaKind` is required, but an RSS release has no kind yet | **Add `MediaKindId.Unattributed` as a static** — *reasoning superseded; re-decide on merits (§10)* | The reason originally given was that a static member is MINOR-legal while reshaping the DTO is not. `docs/contracts/stability.md` no longer says that: below 1.0.0 the DTO may be reshaped in any release, so the honest model — a nullable kind slot — is now on the table and the sentinel must win or lose on its own merits. |
| 12 | Corpus file format | **JSON Lines (`.jsonl`), one case per line, one file per (kind, concern)** | Release titles contain commas, quotes, brackets, CJK and combining marks, so CSV escaping is precisely where corpus bugs hide; a single 2,500-case JSON document produces unreviewable diffs; YAML needs a package that is not registered. JSONL gives one diff line per case and parses on `System.Text.Json` with zero packages. |
| 13 | Where does the corpus live? | **`src/Arronix.Corpus/` — data only, no `.csproj`, no build step; located at runtime by the existing `Arronix.sln` walk-up** | `Arronix.Common.Tests/ProjectShapeTests.RepositoryRoot()` already walks up to `Arronix.sln`; reusing it means no `Content`/`EmbeddedResource` item, no copy-to-output, and corpus edits need no rebuild. |
| 14 | Are expectations total or partial? | **Partial: a case constrains only the keys it names, plus an explicit `absent[]` list for must-not-be-present** | 2,522 imported cases cannot each specify every field; requiring it turns every new tag into a 2,522-line diff. The `absent[]` escape preserves the assertions that are genuinely negative (`AbsoluteEpisodeNumbers.Should().BeEmpty()` appears in 165 Sonarr rows). |
| 15 | Import all cases at once, or curate a subset that passes? | **All at once, with an `xfail` status and a ratchet that may only decrease** | Curating first makes "import the corpus" a multi-week blocker with no intermediate value; the ratchet turns "fix parsing" into a measurable, monotonic burn-down and makes a regression impossible to merge quietly. |
| 16 | New per-plugin test projects for parsing? | **No — one runner in `Arronix.Host.Tests/Corpus/`** | Cross-kind assertions need all four plugins loaded simultaneously, which only the host test project has; four extra projects would each need three of the other four's plugins anyway. |
| 17 | Are cross-kind negatives a hand-written file? | **No — derived from every case in the corpus, with a shrinking override list** | Every case already declares its kind; "no other kind may claim it more strongly" turns 2,522 positive cases into 2,522 × 4 assertions for free. Only genuine ambiguities need a hand-written row, and their count is itself ratcheted. |
| 18 | Anonymize imported release titles, as this fork's Sonarr fixtures already are? | **No** | The hard cases *are* the real ones: `Series.Title.525`, `1776.1979.EXTENDED`, `R.I.P.D.2013`, `3%`, `24-7`, `4:44 (Deluxe Edition)`. Anonymization deletes exactly the numeral-in-title and punctuation-in-title characteristics the parser exists to survive. |
| 19 | A property-based testing package for the normalization layer? | **No package — a seeded generator + greedy shrinker in `Arronix.Common.Tests`** | Neither `FsCheck` nor `CsCheck` is registered in `Directory.Packages.props`, neither is Microsoft-owned, and the generator that matters here (release-title-shaped strings, unpaired surrogates, combining marks) is domain-specific enough that a general library supplies almost none of it. ~120 lines. |
| 20 | `RegexOptions.Compiled` or `[GeneratedRegex]`? | **`[GeneratedRegex]` on partial properties, for every pattern, everywhere** | Verified on this machine: works in a **zero-`PackageReference`** `net10.0` project (the generator ships in the SDK), is 1.6× faster than `Compiled` on a representative tag pattern (49.6 ms vs 78.2 ms / 200 k), removes ~18 ms of construction-time IL emission per 104 patterns, is trim/AOT-safe, and — decisively — gives every pattern a **name**, which is what the corpus coverage map needs. |
| 21 | `RegexOptions.NonBacktracking`? | **Mandatory for the agnostic tag layer; forbidden for the per-kind coordinate layer** | Measured: NonBacktracking accepts **13 of 15** quality patterns but only **5 of 104** report-title patterns, and where it does compile it collapses `Groups["episode"].Captures.Count` from 3 to 1 — which `Parser.cs:940`, `:941` and `:1043` depend on. Linear-time where it is free; a timeout where it is not. |
| 22 | Match timeouts | **Mandatory on every backtracking pattern, plus a hard input-length cap and a per-pattern quarantine breaker** | No timeout is set anywhere in any of the four parsers. Measured on the real 104-pattern Sonarr set: a 3.2 KB adversarial title costs **741 ms**, a 1.6 KB one **148 ms**, growing quadratically — on indexer-controlled input. |
| 23 | Pattern ordering within a kind | **Sorted by measured corpus hit frequency; asserted by a test** | Sonarr's 104-entry array is historical accretion; the corpus coverage map yields the histogram for free, and asserting the order stops it rotting silently. |
| 24 | Does `TokenSanitizer` participate in parsing? | **No — output side only** | It maps reserved device names, truncates to byte limits and de-duplicates file names (`TokenSanitizer.cs:93/133/169/230/282`); none of that is meaningful for a release title, and reusing it would silently mangle titles containing `CON` or a colon. |

---

## 1. Purpose and scope

### What this design owns

- The **split** between what the host parses out of every release regardless of media kind, and what only
  a media-kind plugin can parse.
- The **parse target**: the contract a plugin's parser returns, expressed in the shape model's coordinate
  vocabulary rather than in typed per-kind fields.
- The **preprocessing chain** the host runs once per title, and where the already-built
  `Arronix.Common/Naming/TextFolding` and `Arronix.Common/Text/StringDistance` fit (and where
  `TokenSanitizer` deliberately does not).
- The **golden corpus**: format, physical location, sourcing from the four upstream suites, attribution,
  expectation grammar, and the runner.
- **Cross-kind negative testing**, which is structurally impossible in any single *arr and which the
  measurements below show is not an optional nicety but a routing prerequisite.
- **Regression discipline** and **performance controls**.

### What it does not own

- **Matching.** Resolving a `ReleaseReading` to library units is `IReleaseMatcher` (spec §2.11) and stays
  there. Parsing produces coordinates; matching consumes them.
- **Quality ranking, cutoffs and custom formats.** The parser emits tags; `IQualityModel`,
  `QualityTier` and `FormatFamily.Ladder` rank them.
- **Naming and rename.** `IRenamePolicy`, `ILibraryLayout` and `TokenSanitizer` are the output side.
- **File metadata (MediaInfo/tags).** Embedded-metadata reading is an import-pipeline concern; parsing
  accepts its results as an input with a declared provenance, and never performs it.

### The one deviation from the authoritative spec

`unified-host-runtime.md` §2.11 states, of the four behavior seams: *"Title parsing is the
`IReleaseParser`."* That is the only place this document contradicts it, and the reason is mechanical
rather than aesthetic: §2.11's own `MatchRequest` declares

```csharp
/// <summary>Coordinates already read from the text or from file tags.</summary>
public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;
```

and nothing in the milestone produces a `CoordinateSet` from a string. `ParsedRelease` cannot: its
entire surface is `MediaKind, Title, Year?, Quality?, Codec?, AudioCodec?, Resolution?, ReleaseGroup?,
Languages?, AdditionalMetadata?` (`src/Arronix.Abstractions/DTOs/ParsedRelease.cs`). With `IReleaseParser`
as the only title seam, `MatchRequest.Coordinates` is always `Empty`, every kind re-parses the
title inside its matcher, and the coordinate vocabulary the milestone exists to establish is never used
by the code path that consumes releases. §4 of this document then has nothing to assert against.

The fix is a **replacement**: `IReleaseTitleParser` / `ReleaseReading`, declared in the same namespace as
the coordinate types they produce, take over the seam and `IReleaseParser` / `ParsedRelease` are deleted.
An earlier draft proposed keeping both and bridging between them, on the authority of the milestone's
`IIndexerProvider` decision (resolution #31). That decision has since been reversed — the legacy provider
trio and `StableProviderBridge` are deleted outright — and `docs/contracts/stability.md` now states that
below 1.0.0 a contract with no implementer outside this repository is deleted, not deprecated. §3.5 records
the withdrawal and the sequencing.

---

## 2. What is media-agnostic and what is per-kind

### 2.1 The method

Two independent lines of evidence, agreeing.

**(a) The four `Parsed*Info` models.** Every field present in all four is agnostic by construction;
every field present in one is per-kind by construction.

| Field | Sonarr `ParsedEpisodeInfo` | Radarr `ParsedMovieInfo` | Lidarr `ParsedAlbumInfo` | Readarr `ParsedBookInfo` |
|---|---|---|---|---|
| `ReleaseTitle` | ✓ | ✓ | ✓ | ✓ |
| `Quality` (`QualityModel`) | ✓ | ✓ | ✓ | ✓ |
| `ReleaseGroup` | ✓ | ✓ | ✓ | ✓ |
| `ReleaseHash` | ✓ | ✓ | ✓ | ✓ |
| `Languages` | ✓ | ✓ | — | — |
| title guess | `SeriesTitle` + `SeriesTitleInfo` | `MovieTitles[]` + `OriginalTitle` | `AlbumTitle` + `ArtistName` + `ArtistTitleInfo` | `BookTitle` + `AuthorName` + `AuthorTitleInfo` |
| year | via `SeriesTitleInfo.Year` | `Year` (int) | `ReleaseDate` (string) | `ReleaseDate` (string) |
| coordinates | `SeasonNumber`, `EpisodeNumbers[]`, `AbsoluteEpisodeNumbers[]`, `SpecialAbsoluteEpisodeNumbers[]`, `AirDate`, `SeasonPart`, `DailyPart` | — | *(track numbering lives on `ParsedTrackInfo`: `TrackNumbers[]`, `DiscNumber`, `DiscCount`)* | — |
| fan-out flags | `FullSeason`, `IsPartialSeason`, `IsMultiSeason`, `IsSeasonExtra`, `IsSplitEpisode`, `IsMiniSeries`, `Special` | — | `Discography`, `DiscographyStart`, `DiscographyEnd` | `Discography`, `DiscographyStart`, `DiscographyEnd` |
| variant label | — | `Edition` | `ReleaseVersion` | `ReleaseVersion` |
| external ids | — | `ImdbId`, `TmdbId` | `ArtistMBId`, `AlbumMBId`, `ReleaseMBId` *(on `ParsedTrackInfo`)* | — |
| other | `ReleaseTokens` | `HardcodedSubs`, `SimpleReleaseTitle` | `AlbumType`, `ExtraInfo` bag | `ExtraInfo` bag |

Sources: `src/NzbDrone.Core/Parser/Model/ParsedEpisodeInfo.cs`,
`_reference/Radarr/.../ParsedMovieInfo.cs`, `_reference/Lidarr/.../ParsedAlbumInfo.cs` and
`ParsedTrackInfo.cs`, `_reference/Readarr/.../ParsedBookInfo.cs`.

**(b) The regex field names.** A field name that appears in three or four codebases is a concern each
team re-derived independently; a name that appears once is that kind's own.

| Regex field | Sonarr | Radarr | Lidarr | Readarr |
|---|:--:|:--:|:--:|:--:|
| `RejectHashedReleasesRegex(es)` | ✓ (14) | ✓ (9) | ✓ (8) | ✓ (8) |
| `SimpleTitleRegex` | ✓ | ✓ | ✓ | ✓ |
| `NormalizeRegex` | ✓ | ✓ | ✓ | ✓ |
| `PunctuationRegex` | ✓ | ✓ | ✓ | ✓ |
| `DuplicateSpacesRegex` | ✓ | ✓ | ✓ | ✓ |
| `SpecialEpisodeWordRegex` | ✓ | ✓ | ✓ | ✓ |
| `RequestInfoRegex` | ✓ | ✓ | ✓ | ✓ |
| `WebsitePrefixRegex` / `WebsitePostfixRegex` | ✓ *(in `ParserCommon`)* | ✓ *(in `ParserCommon`)* | ✓ | ✓ |
| `CleanTorrentSuffixRegex` | ✓ *(in `ParserCommon`)* | ✓ *(in `ParserCommon`)* | ✓ | ✓ |
| `ReleaseGroupRegex` / `AnimeReleaseGroupRegex` / `CleanReleaseGroupRegex` | ✓ *(in `ReleaseGroupParser`)* | ✓ *(in `ReleaseGroupParser`)* | ✓ | ✓ |
| `ReversedTitleRegex` | ✓ | ✓ | — | — |
| `PercentRegex` | ✓ | **—** | ✓ | ✓ |
| `YearInTitleRegex` | ✓ | ✓ | **—** | ✓ |
| `SixDigitAirDateRegex` | ✓ | **—** | ✓ | ✓ |
| `CleanQualityBracketsRegex` | ✓ | ✓ | — | — |
| `ProperRegex` / `RepackRegex` / `VersionRegex` / `RealRegex` | ✓ | ✓ | ✓ | ✓ |
| `CodecRegex` | ✓ | ✓ | ✓ | ✓ |
| `ResolutionRegex` / `SourceRegex` / `RemuxRegex` | ✓ | ✓ | — | — |
| `BitRateRegex` / `SampleSizeRegex` | — | — | ✓ | — |
| `EditionRegex` | — | ✓ | — | — |
| `HardcodedSubsRegex` | — | ✓ | — | — |
| `CommonTagRegex` / `BracketRegex` / `AfterDashRegex` | — | — | ✓ | ✓ |
| `ReportTitleRegex` / `ReportMovieTitleRegex` / `ReportAlbumTitleRegex` / `ReportBookTitleRegex` | **104** | **11** | **20** | **21** |

The four gaps marked in bold are the argument by themselves. `PercentRegex` exists to make the series
*3%* parse (`Parser.cs:515,823`); Radarr does not have it and a film titled *3%* parses worse there for
no reason anybody chose. `SixDigitAirDateRegex` is byte-identical in three codebases and absent from the
fourth. This is one concern, maintained four times, drifting.

Drift is measurable, not hypothetical. `diff src/NzbDrone.Core/Parser/LanguageParser.cs
_reference/Radarr/.../LanguageParser.cs` is **610 lines**; the two `ExceptionReleaseGroupRegexExact`
lists (`ReleaseGroupParser.cs:19` in each) share 8 of ~20 entries; the two `ReleaseGroupRegex` negative
lookbehinds differ by six alternatives including `WEB-Rip`, `-ENG`, `-JAP` and `-HDRip`. `RegexReplace.cs`
is byte-identical between Sonarr and Radarr and differs in Lidarr, Readarr and Prowlarr.

### 2.2 The split, stated

**Media-agnostic — host-owned (`Arronix.Host/Parsing/`), runs once per release before any plugin sees it:**

| Concern | Evidence | Output |
|---|---|---|
| Junk / hash / obfuscation rejection | `Parser.cs:459-501` (14 patterns), Radarr 9, Lidarr 8, Readarr 8 | gate |
| Reversed-title detection and reversal | `Parser.cs:509`, Radarr `Parser.cs` `ReversedTitleRegex` | preprocessing |
| File-extension strip; extension→container | `Parser.cs:877`, `QualityParser` extension path (`QualityParserFixture.cs:475-484`) | `Container`, provenance `Extension` |
| Website prefix/postfix, torrent suffix, `[request-info]` prefixes | `ParserCommon.cs:54,58,62`, `Parser.cs:543` | preprocessing |
| Format tokens: resolution, source, video codec, remux, bit depth | `QualityParser.cs:17,49,53,56,66` | `ReleaseTags` |
| Audio tokens: codec, channels, bitrate, sample size | Lidarr `QualityParser.cs` `BitRateRegex`, `SampleSizeRegex`, `CodecRegex` | `ReleaseTags` |
| Revision: proper / repack / rerip / real / vN | `QualityParser.cs:37,40,43,46`; `Qualities/Revision.cs` | `ReleaseTags.Revision` |
| Release group, anime sub-group, exception lists, group cleanup | `ReleaseGroupParser.cs:9,19,22,24` | `ReleaseTags.ReleaseGroup` |
| Release hash / CRC | `Parser.cs:1218` `GetReleaseHash` | `ReleaseTags.Hash` |
| Language and subtitle language | `LanguageParser.cs` (7 patterns Sonarr, 7 Radarr) | `ReleaseTags.Languages` / `.SubtitleLanguages` |
| `MULTI` marker | `Parser.cs:547,882` | `ReleaseTags` |
| Hardcoded subtitles | Radarr `Parser.cs:22` | `ReleaseTags` |
| Edition / version label (open text) | Radarr `Parser.cs:18` `EditionRegex`; Lidarr/Readarr `ReleaseVersion` | `ReleaseTags.EditionLabel` |
| Year token in title position | `Parser.cs:529` `YearInTitleRegex` | `NormalizedTitle.TitleYear` |
| Six-digit air-date expansion | `Parser.cs:523`; Lidarr and Readarr identical | preprocessing, gated on the shape declaring a `Date` space |
| Title canonicalization for comparison (fold, strip articles, punctuation, case) | `Parser.cs:810 CleanSeriesTitle`, `:850 NormalizeTitle`; `StringExtensions.cs:65 RemoveAccent` | `NormalizedTitle.Canonical` |
| Scene-ness heuristic | `SceneChecker.cs` | `ReleaseTags.LooksScene` |

**Per-kind — plugin-owned (`IReleaseTitleParser`):**

| Concern | Evidence |
|---|---|
| The title guess and its alternates | `SeriesTitleInfo.AllTitles` (`Parser.cs:908-912`), `ParsedMovieInfo.MovieTitles[]`, `ArtistTitleInfo`, `AuthorTitleInfo` |
| Coordinate extraction into declared spaces | `ReportTitleRegex` ×104 / ×11 / ×20 / ×21 |
| Unit fan-out and acquisition scope | `FullSeason`, `IsMultiSeason`, `IsSplitEpisode`, `Discography{,Start,End}`, `ParsedEpisodeInfo.ReleaseType` (`ParsedEpisodeInfo.cs:94-114`) |
| Pre-substitution rewrites | `ParserCommon.cs:9-49` — 14 in Sonarr, **0** in Radarr, all injecting `S..E..` |
| Kind-specific external ids in the title | Radarr `ReportImdbId`, `ReportTmdbId`; Lidarr `*MBId` |
| Special / extra / part classification | `Parser.cs:450` `SpecialEpisodeTitleRegex`, `IsPossibleSpecialEpisode` |
| Query-side title rewriting (VA → "VA", translations, aliases) | Lidarr `Parser.cs:346`; belongs to `IReleaseQueryPlanner`, listed here so it is not mistaken for a parse concern |

**Deliberately neither:** the `Season`-as-a-level question. `season` is a `SequenceAxis` naming component 0
of the `aired` space (spec §2.4/§2.12); the parser reads the *coordinate*, never a season entity.

### 2.3 Two consequences worth stating plainly

**The indexer already supplies some of these tags and every *arr throws them away.**
`src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs:34-37` declares `Origin`, `Source`, `Container`, `Codec`
and `Resolution` — populated from the feed — and the parser re-derives all of them from the title. The
agnostic tag layer must therefore accept **pre-supplied tags with a provenance** and prefer them by rank,
which is what `TagProvenance.Indexer` in §3.3 exists for.

**Provenance is per-field and there are already three incompatible vocabularies for it.**
`Qualities/QualityDetectionSource.cs` is `{Unknown, Name, Extension, MediaInfo}`;
`MediaFiles/EpisodeImport/.../Augmenters/Quality/Confidence.cs` is `{Fallback, Default, Tag, MediaInfo}`;
`.../Augmenters/Language/Confidence.cs` is `{Default, Filename, Foldername, DownloadClientItem, MediaInfo}`.
Three enums, one idea, in one product. Arronix gets one.

---

## 3. The parse target

Namespace `Arronix.Abstractions.Shape`, diagnostic **`ARX0013`**, files under
`src/Arronix.Abstractions/Shape/`. Every type is `sealed record` or `readonly record struct` and carries
`[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]`, per
`unified-host-runtime.md` §7.2.

### 3.1 The seam

```csharp
// src/Arronix.Abstractions/Shape/IReleaseTitleParser.cs
/// <remarks>
/// <para>Reads one release title into <b>coordinates</b>, not into typed fields. The <b>only</b> title-parse
/// seam: it replaces the 0.1.0 <c>IReleaseParser</c>, whose <c>ParsedRelease</c> has no way to express a
/// coordinate, a multi-unit fan-out, a confidence or a cross-kind claim, and which leaves
/// <c>MatchRequest.Coordinates</c> with no producer at all.</para>
/// <para>Deliberately synchronous and pure. Purity is what makes a 2,500-case golden corpus a
/// millisecond-scale test rather than an integration suite, and every one of the four surveyed parsers is
/// already a static function of its input.</para>
/// </remarks>
public interface IReleaseTitleParser
{
    MediaKindId MediaKind { get; }

    ReleaseReading Parse(ReleaseParseRequest request);
}

// src/Arronix.Abstractions/Shape/ReleaseParseRequest.cs
public sealed record ReleaseParseRequest
{
    /// <summary>The original string, extension included. Sonarr's <c>title</c>.</summary>
    public required string RawText { get; init; }

    /// <summary>The host's preprocessing output (§4). Never null; degrades to <c>RawText</c>.</summary>
    public required NormalizedTitle Normalized { get; init; }

    /// <summary>What the host already read out of the string, kind-independently.</summary>
    public required ReleaseTags Tags { get; init; }

    /// <summary>Provenance of <see cref="RawText"/>. Sonarr's <c>sceneSource</c>, generalized.</summary>
    public required MatchSource Source { get; init; }

    /// <summary>Newznab categories the release arrived under, when the feed supplied any.</summary>
    public IReadOnlyList<CategoryId> Categories { get; init; } = [];

    /// <summary>Set when the release came from a search this kind requested; null on RSS and push.</summary>
    public string? SearchKindId { get; init; }
}
```

`MatchSource` is the spec's existing enum (`{ReleaseName, FileName, FolderName, DownloadClientTitle,
Tags}`, §2.11) — reused verbatim, not re-declared.

### 3.2 The reading

```csharp
// src/Arronix.Abstractions/Shape/ReleaseReading.cs
public sealed record ReleaseReading
{
    public required MediaKindId MediaKind { get; init; }

    /// <summary>
    /// How strongly this kind claims the title. Reuses the STABLE <see cref="MatchConfidence"/>.
    /// <c>None</c> means declined and every other member must be at its default.
    /// </summary>
    public required MatchConfidence Claim { get; init; }

    /// <summary>Why the parser declined or hedged. Diagnostic, never rendered as an error.</summary>
    public string? Reason { get; init; }

    /// <summary>The plugin's best guess at the work title, already trimmed of format tokens.</summary>
    public string? TitleGuess { get; init; }

    /// <summary>
    /// Sonarr's <c>SeriesTitleInfo.AllTitles</c> (<c>Parser.cs:908-912</c>) and Radarr's
    /// <c>MovieTitles[]</c>: "A (B)", "A | B" and "A AKA B" all yield two candidates.
    /// </summary>
    public IReadOnlyList<string> AlternateTitleGuesses { get; init; } = [];

    /// <summary>A year read from the title *position*, not from a coordinate space.</summary>
    public int? TitleYear { get; init; }

    /// <summary>One entry per unit or span the title names. Empty is legal with a non-None claim.</summary>
    public required IReadOnlyList<ReleaseUnitAddress> Units { get; init; }

    /// <summary>External ids read out of the title. Radarr's ReportImdbId/ReportTmdbId; Lidarr's MBIDs.</summary>
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];

    /// <summary>Tags the plugin refined or added on top of the host's. Usually the request's, unchanged.</summary>
    public required ReleaseTags Tags { get; init; }

    /// <summary>
    /// Stable id of the pattern that produced this reading (§7.2). Diagnostics and the corpus coverage
    /// map both read it; Sonarr's substitute is <c>Logger.Trace(regex)</c> at <c>Parser.cs:740</c>, which
    /// logs the pattern text because the patterns have no names.
    /// </summary>
    public string? PatternId { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static ReleaseReading Decline(MediaKindId kind, string reason);
}

// src/Arronix.Abstractions/Shape/ReleaseUnitAddress.cs
/// <remarks>
/// The answer to "one title, many units". <c>S01E01E02E03</c> is three <c>Single</c> addresses; a season
/// pack is one <c>SequenceSpan</c>; a discography is one <c>Ancestor</c>; a book omnibus is one
/// <c>Single</c> whose <see cref="Ordinal"/> is meaningful because <c>FileBinding.OrdinalIsMeaningful</c>
/// is set. All four surveyed shapes, one list.
/// </remarks>
public sealed record ReleaseUnitAddress
{
    public required AcquisitionScope Scope { get; init; }

    /// <summary>
    /// Readings across every space this address populates. A TV episode title routinely populates
    /// <c>aired</c>, <c>absolute</c> and <c>scene</c> at once — that is a bag, not three addresses.
    /// </summary>
    public required CoordinateSet Coordinates { get; init; }

    /// <summary>The level addressed, when the kind has more than one candidate. Null means the shape's.</summary>
    public MediaLevelId? LevelId { get; init; }

    /// <summary>Position of this address within the release, 0-based, in title order.</summary>
    public int Ordinal { get; init; }

    /// <summary>Marks Sonarr's <c>Special</c> / <c>IsSeasonExtra</c>: addressed but outside the sequence.</summary>
    public bool IsOutOfSequence { get; init; }
}
```

`AcquisitionScope` and `CoordinateSet`/`CoordinateReading`/`CoordinateConfidence` are the spec's existing
types (§2.3, §2.6) — reused, not re-declared. **This is the whole point of siting parsing under
`ARX0013`.**

Worked examples against the spec's §2.12 acceptance table:

| Release title | `Units` | Notes |
|---|---|---|
| `Series.Title.S03E06.DVDRip.XviD-WiDE` | 1 × `Single`, `{aired: 3.6}` | `MultiEpisodeParserFixture` baseline |
| `Series.Title.S01E01E02E03.720p` | 3 × `Single`, `{aired: 1.1}` `{1.2}` `{1.3}`, ordinals 0/1/2 | `Parser.cs:940` multi-capture |
| `Series.Title.S03.1080p.WEB-DL` | 1 × `SequenceSpan`, `{aired: 3}` | `FullSeason` |
| `[Grp] Series Title - 127 [ABCD1234]` | 1 × `Single`, `{absolute: 127, conf: asserted}` | anime absolute space |
| `Series.Title.2011.04.28.HDTV` | 1 × `Single`, `{airdate: 2011-04-28}` | `Date` kind |
| `[Grp] Series - S02E02 - 027 - Title [WEBDL-1080p v2]` | 1 × `Single`, `{aired: 2.2, absolute: 27}` | two spaces, one address |
| `Series.Title.S02E05.720p` from a *release name* on a scene-mapped series | 1 × `Single`, `{scene: 2.5, conf: unverified}` | `IsProvenanceSensitive` + `MayBeUnverified` (spec §2.3) |
| `Artist - Discography 1994-2011 FLAC` | 1 × `Ancestor`, `{}` | Lidarr `DiscographyStart/End` → the span goes in `Tags.Extra`, not a coordinate |
| `Movie.Title.2013.1080p.BluRay.x264-GRP` | 1 × `Single`, `{only: singleton}` | `CoordinateKind.Singleton` |

**Scene numbering is not a second reading.** It is one more `CoordinateReading` in the same
`CoordinateSet`, on a space whose `IsProvenanceSensitive` is true, populated only when
`request.Source == MatchSource.ReleaseName` — the exact generalization of `sceneSource`
(`ParsingService.cs:226`, `:616`). The parser stamps `CoordinateConfidence.Unverified` when it
extrapolated, mirroring `XemService.cs:132-146`.

### 3.3 The tag bag

```csharp
// src/Arronix.Abstractions/Shape/ReleaseTags.cs
/// <remarks>
/// Everything the host can read out of a release string without knowing what kind of media it is.
/// Deliberately NOT an extension of <c>ParsedRelease</c>: that record has ten flat string-ish slots chosen
/// before the tag vocabulary existed, no provenance, and no room for a tag whose value is not a string.
/// <c>ParsedRelease</c> is deleted and <c>IQualityModel</c> is retargeted at <c>ReleaseTags</c> (§3.5).
/// </remarks>
public sealed record ReleaseTags
{
    public static ReleaseTags Empty { get; }

    // --- container and source -------------------------------------------------
    public string? Container { get; init; }              // "mkv", "flac", "epub"
    public string? Source { get; init; }                 // canonical, from ReleaseTagVocabulary
    public TagProvenance SourceProvenance { get; init; }

    // --- picture --------------------------------------------------------------
    public int? ResolutionHeight { get; init; }          // 720, 1080, 2160
    public TagProvenance ResolutionProvenance { get; init; }
    public string? VideoCodec { get; init; }             // "x264", "h265", "xvid"
    public int? BitDepth { get; init; }
    public string? DynamicRange { get; init; }           // open: "hdr10", "dv", "hlg"

    // --- sound ----------------------------------------------------------------
    public string? AudioCodec { get; init; }             // "dts-hd-ma", "flac", "aac"
    public string? AudioChannels { get; init; }          // "5.1", "2.0"
    public int? BitrateKbps { get; init; }               // Lidarr BitRateRegex
    public int? SampleSizeBits { get; init; }            // Lidarr SampleSizeRegex
    public int? SampleRateHz { get; init; }

    // --- revision -------------------------------------------------------------
    /// <summary>Mirrors <c>Qualities/Revision.cs</c> exactly: PROPER→v2, REPACK2→v3, REAL is separate.</summary>
    public ReleaseRevision Revision { get; init; } = ReleaseRevision.Original;
    public TagProvenance RevisionProvenance { get; init; }

    // --- attribution ----------------------------------------------------------
    public string? ReleaseGroup { get; init; }
    public string? SubGroup { get; init; }               // anime [Group] prefix
    public string? Hash { get; init; }                   // CRC32 / release hash
    public bool LooksScene { get; init; }                // SceneChecker.cs

    // --- language -------------------------------------------------------------
    public IReadOnlyList<Language> Languages { get; init; } = [];         // STABLE DTO
    public IReadOnlyList<Language> SubtitleLanguages { get; init; } = [];
    public bool IsMultiLanguage { get; init; }                            // Parser.cs:547 MultiRegex
    public string? HardcodedSubtitles { get; init; }                      // Radarr Parser.cs:22

    // --- variant --------------------------------------------------------------
    /// <summary>Open text: "Director's Cut", "Deluxe Edition", "Remastered", "Unabridged".</summary>
    public string? EditionLabel { get; init; }

    /// <summary>
    /// One-consumer values that do not earn a slot: Lidarr's discography span, Readarr's part/set
    /// numbers, indexer flags. The precedent is <c>JobExecutionResult.ResultData</c> (spec #23).
    /// </summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; }
        = new Dictionary<string, string>();
}

public readonly record struct ReleaseRevision(int Version, int Real, bool IsRepack)
{
    public static ReleaseRevision Original { get; }   // (1, 0, false)
}

/// <remarks>
/// One vocabulary replacing the three that exist today: <c>QualityDetectionSource</c>
/// {Unknown,Name,Extension,MediaInfo}, quality <c>Confidence</c> {Fallback,Default,Tag,MediaInfo} and
/// language <c>Confidence</c> {Default,Filename,Foldername,DownloadClientItem,MediaInfo}. Ordered: a
/// higher member wins when two sources disagree. <c>Indexer</c> is new and closes a real gap —
/// <c>ReleaseInfo.cs:34-37</c> carries feed-supplied Source/Codec/Resolution/Container that every *arr
/// currently discards.
/// </remarks>
public enum TagProvenance
{
    None = 0, Extension = 1, Title = 2, Indexer = 3, Container = 4, EmbeddedMetadata = 5,
}
```

Provenance is carried for exactly three fields — source, resolution and revision — because those are
exactly the three `QualityModel` tracks (`Qualities/QualityModel.cs:14-21`). Adding a fourth is
additive; adding one per tag would be eighteen fields nobody reads.

`ReleaseTagVocabulary` is a **host-side** static class of canonical strings (`Bluray`, `WebDl`, `WebRip`,
`Hdtv`, `Dvd`, `Remux`, …) — not a contract, because a plugin never authors a tag; it reads one. Values
are compared ordinal-insensitively and the vocabulary is open at the edges (`DynamicRange`,
`EditionLabel`, `Extra`) where the surveyed sets disagree.

### 3.4 The normalized title

```csharp
// src/Arronix.Abstractions/Shape/NormalizedTitle.cs
/// <remarks>
/// The preprocessing chain's output, computed once per release and shared by every kind's parser. Four
/// codebases run this chain identically (<c>Parser.cs:666-706</c>, Radarr <c>Parser.cs:169-223</c>) and a
/// unified host that recomputed it per kind would pay for it N times.
/// </remarks>
public sealed record NormalizedTitle
{
    /// <summary>Extension removed, reversal undone, CJK brackets folded. Sonarr's <c>releaseTitle</c>.</summary>
    public required string Release { get; init; }

    /// <summary>Format tokens, site prefixes and torrent suffixes stripped. Sonarr's <c>simpleTitle</c>.</summary>
    public required string Simple { get; init; }

    /// <summary>Folded, article-stripped, punctuation-free, lower-cased. Sonarr's <c>CleanSeriesTitle</c>.</summary>
    public required string Canonical { get; init; }

    /// <summary>Removed file extension, when there was one. Feeds container detection.</summary>
    public string? Extension { get; init; }

    /// <summary>Steps that actually fired, in order. Diagnostics and the corpus both read it.</summary>
    public IReadOnlyList<string> AppliedSteps { get; init; } = [];
}
```

### 3.5 No bridges: `IReleaseParser` and `ParsedRelease` are deleted

An earlier draft of this section was titled *"Bridges, so nothing stable breaks"* and specified two shim
classes — `StableParserBridge.cs` (wrapping a registered `IReleaseParser` as a weak `IReleaseTitleParser`)
and `TagsToParsedReleaseBridge.cs` (projecting `ReleaseTags` back onto `ParsedRelease` before every
`IQualityModel.EvaluateQuality` call). **Both are withdrawn. Neither is written.**

The justification offered for them was that reshaping `ParsedRelease` would be *"a binary break for a plugin
compiled against the previous version."* There is no previous version. Arronix has never shipped, so no such
plugin exists and none can. What the plan actually bought was two parse seams, two DTOs, two registration
paths, and a **lossy conversion in the hot path of every quality evaluation and every import** — permanently,
because nothing was scheduled to remove them. `docs/contracts/stability.md` now states the governing rule
directly: below 1.0.0 any contract may be changed, renamed or deleted, and a contract with no implementer
outside this repository is deleted rather than deprecated.

**End state.** `IReleaseTitleParser` is the only title-parse seam and `ReleaseTags` is the only
media-agnostic tag representation. `Arronix.Abstractions.Parsing.IReleaseParser`,
`Arronix.Abstractions.DTOs.ParsedRelease`, `IPluginRegistry.AddReleaseParser` and its `CapabilityMatrix` row
are deleted. `IQualityModel.EvaluateQuality(ParsedRelease)` becomes
`IQualityModel.EvaluateQuality(ReleaseTags)`. Nothing is lost in the swap: every one of `ParsedRelease`'s
quality-bearing slots (`Quality`, `Resolution`, `Codec`, `AudioCodec`, `ReleaseGroup`, `Languages`,
`AdditionalMetadata`) has a typed counterpart in `ReleaseTags`, and the two that do not — `MediaKind` and
`Title`/`Year` — were never quality inputs: the kind is already `IQualityModel.MediaKind`, and the title
belongs to `ReleaseReading`. A plugin that registers only a title parser
producing no coordinates is not *bridged* into existence; it simply does not exist as a representable state.

**Sequencing — and this matters, because `IReleaseParser` is live.** Unlike the import contracts
(`acquisition-pipeline.md` §9), `IReleaseParser` has four implementers today:
`Arronix.Plugin.{Tv,Movies,Music,Books}`'s `*ReleaseParser.cs`, each registered through
`IPluginRegistry.AddReleaseParser` and each consumed by the matching `*QualityModel`. The replacement does
not exist yet. **Nothing is deleted before its successor is written.** The deletion is therefore attached to
the work packages that introduce the replacement, in this order, and the codebase never contains both:

| Step | Work package | What lands |
|---|---|---|
| 1 | **WP-21** | The replacement contracts are added: `IReleaseTitleParser`, `ReleaseParseRequest`, `ReleaseReading`, `ReleaseTags` and friends. Nothing is deleted yet — this opens the only window in which both seams exist, and WP-28 closes it inside this milestone. |
| 2 | **WP-22** | `AddReleaseTitleParser` joins `IPluginRegistry`; `CapabilityMatrix` gains the row. |
| 3 | **WP-24 / WP-25** | The four plugins gain `*ReleaseTitleParser.cs`, written against the corpus. |
| 4 | **WP-28** | The window closes. `IQualityModel.EvaluateQuality` is retargeted from `ParsedRelease` to `ReleaseTags` and the four `*QualityModel`s follow (the compiler names every call site); then `Parsing/IReleaseParser.cs`, `DTOs/ParsedRelease.cs`, `AddReleaseParser`, its `CapabilityMatrix` row, the four `*ReleaseParser.cs` and the `Arronix.Abstractions.Parsing` namespace are deleted, together with their entries in `stability.md`'s residual list and in `src/Arronix.Abstractions/README.md`. |

### 3.6 Registration and capability

`IPluginRegistry.AddReleaseParser` is **replaced**, not joined, by one line (spec §3.4), carrying the same
`parsing` capability gate (spec §4.1):

```csharp
IPluginRegistry AddReleaseTitleParser(IReleaseTitleParser parser);        // Parsing
```

`CapabilityMatrix`'s `parsing` row retargets from `IReleaseParser` to `IReleaseTitleParser`;
`CapabilityMatrixTests` and `BidirectionalCapabilityTests` (spec §7.5) then cover it with no new test. Per
the step table in §3.5 the two registration methods coexist only between WP-22 and WP-28, and WP-28's
acceptance criterion is that the solution builds with zero references to `AddReleaseParser`,
`IReleaseParser` or `ParsedRelease`.

---

## 4. Preprocessing and normalization

### 4.1 The chain

Host-owned, ordered, closed. Each step has a stable id, is individually testable, and is enabled by a
**shape-derived predicate** — never by a kind name.

| # | Step id | What it does | Enabled when | Source |
|---|---|---|---|---|
| 1 | `reject-empty` | no letter or digit → decline | always | `Parser.cs:1187` |
| 2 | `reject-password-yenc` | Usenet placeholder posts | always | `Parser.cs:1178` |
| 3 | `reject-hashed` | 14 obfuscation patterns | always | `Parser.cs:459` |
| 4 | `reject-container-folder` | `Season 3` / `Specials` bare folder names | `MatchSource` is `FolderName` | `Parser.cs:503` |
| 5 | `cap-length` | over `MaxTitleLength` → decline | always | **new** (§8.4) |
| 6 | `unreverse` | detect and undo reversed titles | always | `Parser.cs:509,647` |
| 7 | `strip-extension` | remove and record the extension | always | `Parser.cs:686` |
| 8 | `fold-fullwidth-brackets` | `【】` → `[]` | always | `Parser.cs:688` |
| 9 | *(plugin)* `pre-substitute` | the kind's own rewrite table | kind registered any | `ParserCommon.cs:9` |
| 10 | `strip-format-tokens` | resolution / codec / bit-depth / `DD5.1` | always | `Parser.cs:517` |
| 11 | `strip-site-prefix` / `-postfix` | `www.x.tld -` wrappers | always | `ParserCommon.cs:54,58` |
| 12 | `strip-tracker-suffix` | `[ettv]`, `[rarbg]`, … | always | `ParserCommon.cs:62` |
| 13 | `strip-request-info` | leading `[…][…]` runs | always | `Parser.cs:543` |
| 14 | `strip-quality-brackets` | trailing `[…]` **iff** it parses as a quality | always | `Parser.cs:706` |
| 15 | `expand-six-digit-date` | `yymmdd` → `20yy.mm.dd` | shape declares a `CoordinateKind.Date` space | `Parser.cs:523` |
| 16 | `percent-to-word` | `3%` → `3percent` | always | `Parser.cs:823` |
| 17 | `fold-diacritics` | `TextFolding.Fold` + contributed folds | always | `StringExtensions.cs:65` |
| 18 | `strip-articles` | leading `a`/`an`/`the`, and inner `and`/`or`/`of` | always | `Parser.cs:511,837` |
| 19 | `strip-punctuation` + `collapse-spaces` + `lower` | canonical form | always | `Parser.cs:850` |

Steps 1–5 gate. 6–14 produce `Release` then `Simple`. 15–16 amend `Simple`. 17–19 produce `Canonical`.

Step 9 is the only plugin-supplied step, and it is placed exactly where Sonarr places it
(`Parser.cs:690`, before `SimpleTitleRegex`). It runs per kind, so its output is per kind: the request
handed to kind *K* carries *K*'s substituted `Simple`. This is the one place the "compute once" rule
yields, and it yields because the evidence forces it — 14 rewrite rules in Sonarr, 0 in Radarr.

### 4.2 Where `Arronix.Common` fits

| Existing component | Role in parsing | Verdict |
|---|---|---|
| `Arronix.Common/Naming/TextFolding.cs:40,61` `Fold` | **Step 17.** Byte-equivalent to the four apps' `RemoveAccent()` (`StringExtensions.cs:65`) but table-contributable, and it already survives unpaired surrogates by degrading rather than throwing — which matters because a title arriving from a remote index is not guaranteed well-formed. | **Use directly.** |
| `TextFolding.BuildFoldTable` + `IDiacriticFoldingProvider` | Assembles the contributed table. Already gated `parsing \| renaming` (spec §4.1). Nothing to add. | **Use directly.** |
| `Arronix.Common/Naming/TokenSanitizer.cs` | Reserved-name mapping, byte-limit truncation, uniquifying. All output-side. | **Must not be used.** Sanitizing a release title would mangle a work legitimately called `CON` or `AUX` and would truncate on a byte budget that has no meaning for a title. |
| `Arronix.Common/Text/StringDistance.cs:48` `Levenshtein` | Fuzzy title comparison — Lidarr's `DistanceCalculator`. | **Matching, not parsing.** Referenced here only so it is not wired into the parse path by mistake. |
| `Arronix.Common/Text/StringExtensions.cs` | General helpers. | Used freely; nothing parsing-specific. |

### 4.3 The gap this exposes in `MatchRequest`

`MatchRequest` (spec §2.11) carries `Text` but no canonical form, so every `IReleaseMatcher` would
re-canonicalize — and it cannot use `TextFolding`, which lives in `Arronix.Common`, which plugins may not
reference. **Recommendation:** add `NormalizedTitle Normalized { get; init; }` to `MatchRequest`. It is
an additive field on an experimental record, it costs nothing, and without it the folding table a plugin
contributed is unreachable from the plugin's own matcher.

---

## 5. The golden-test corpus

### 5.1 What exists upstream

Measured by counting `[TestCase(` attributes and de-duplicating first string arguments:

| Suite | Fixtures | `[TestCase]` rows | Distinct title strings |
|---|---:|---:|---:|
| Sonarr → `tv` | 26 | 1,511 | 1,398 |
| Radarr → `movies` | 14 | 1,037 | 898 |
| Lidarr → `music` | 11 | 440 | 307 |
| Readarr → `books` | 9 | 300 | 214 |
| **Total** | **60** | **3,288** | **2,522** (union, de-duplicated across apps) |

The 295-case gap between the per-suite sum (2,817) and the union (2,522) is itself informative: nearly
300 titles appear in more than one suite's tests, which is the cross-kind overlap made visible.

### 5.2 Format

**JSON Lines. One case per line. One file per (kind, concern).** Rationale in resolution #12.

```
src/Arronix.Corpus/
├── _sources.json                  # upstream repos, commits, licenses, import dates
├── _manifest.json                 # file inventory + ratchet counters
├── tv/{aired,absolute,date,multi,season-pack,special,anime,unicode,junk,tags}.jsonl
├── movies/{title-year,edition,anime,junk,tags}.jsonl
├── music/{album,discography,track,junk,tags}.jsonl
├── books/{book,author,part-set,junk,tags}.jsonl
├── shared/{group,language,revision,resolution,source,hash,site-prefix}.jsonl
└── routing/{overrides,ambiguous}.jsonl
```

`shared/` holds the media-agnostic tag cases — the ones sourced from `QualityParserFixture`,
`LanguageParserFixture` and `ReleaseGroupParserFixture` across **all four** suites, merged. That merge is
the corpus doing what four separate repositories could not: 278 + 273 + 125 + 35 = 711 quality rows and
142 + 154 + 65 + 27 = 388 release-group rows become one set with one expected answer each, and every
place the four apps currently disagree becomes a case that must be decided rather than a divergence
nobody noticed.

### 5.3 The case schema

```jsonc
// tv/aired.jsonl — one line, wrapped here for legibility
{
  "id": "tv/aired/0142",
  "title": "Series.Title.S03E06.DVDRip.XviD-WiDE",
  "kind": "tv",
  "src": "release",                       // MatchSource; default "release"
  "origin": "sonarr@8f1c2ad:src/NzbDrone.Core.Test/ParserTests/SingleEpisodeParserFixture.cs:16",
  "expect": {
    "claim": "high",                      // MatchConfidence, lower-cased
    "title": "Series Title",
    "units": [ { "c": { "aired": "3.6" } } ],
    "tags": { "src": "dvd", "vcodec": "xvid", "grp": "WiDE" }
  },
  "absent": { "coords": ["absolute", "scene"] }
}
```

Grammar, complete:

| Key | Meaning |
|---|---|
| `id` | stable, immutable, `<kind>/<concern>/<4-digit>`. Never renumbered; a deleted case leaves a hole. |
| `title` | the input string, verbatim, no anonymization |
| `kind` | the kind expected to win arbitration |
| `src` | `release` \| `file` \| `folder` \| `client` \| `tags`; default `release` |
| `origin` | `<app>@<commit>:<path>:<line>` — §5.5 |
| `expect.claim` | minimum acceptable `MatchConfidence` (`none` asserts a decline) |
| `expect.title` | exact `TitleGuess` |
| `expect.alts` | exact `AlternateTitleGuesses`, order-sensitive |
| `expect.year` | `TitleYear` |
| `expect.units[]` | ordered; each `{ "scope": "single\|span\|ancestor", "c": {...}, "ord": n, "oos": bool }` |
| `expect.units[].c` | `spaceId → value`, value being `Coordinate.ToString()` — `"3.6"` (ordinal), `"2011-04-28"` (date), `"A1"` (label), `""` (singleton) |
| `expect.units[].conf` | `spaceId → unverified\|derived\|asserted\|verified`; default `asserted` |
| `expect.tags` | short-keyed `ReleaseTags` subset: `src`, `res`, `vcodec`, `acodec`, `ch`, `kbps`, `bits`, `grp`, `sub`, `hash`, `lang[]`, `sublang[]`, `rev{v,real,repack}`, `ed`, `cont`, `hc`, `multi`, `scene` |
| `expect.ids[]` | `ExternalId.ToString()` forms — `"imdb:tt0123456"` |
| `absent.coords[]` | spaces that must **not** be populated |
| `absent.tags[]` | tag keys that must be null/empty |
| `claims` | explicit cross-kind override, `kind → MatchConfidence` (§6) |
| `status` | `ok` (default) \| `xfail` \| `skip`; anything but `ok` needs `note` |
| `note` | free text; **required** when `status != "ok"` or when an expectation is changed |

**Expectations are partial.** A key absent from `expect` is unconstrained. Negative assertions are
explicit, in `absent` — which is exactly how the upstream fixtures already express them
(`result.AbsoluteEpisodeNumbers.Should().BeEmpty()` appears in all 165 rows of
`AbsoluteEpisodeNumberParserFixture` and 180 of `SingleEpisodeParserFixture`).

**Coordinate values are the round-trip string form on purpose.** `OrdinalPath.TryParse("3.6")` and
`Coordinate.ToString()` (spec §2.2/§2.3) must be exact inverses, and the corpus is the reason: every case
exercises that round-trip whether or not the shape team writes a dedicated test.

### 5.4 Physical location and the runner

No `.csproj`, no `Content` items, no copy-to-output. `Arronix.Common.Tests/ProjectShapeTests` already
carries a `RepositoryRoot()` helper that walks up from `AppContext.BaseDirectory` looking for
`Arronix.sln`; the runner reuses that shape to resolve `src/Arronix.Corpus/`. Consequences: corpus edits
need no rebuild, the diff is pure data, and no governance test that asserts an exact package or project
reference set is disturbed.

The runner is `src/Arronix.Host.Tests/Corpus/` (WP-18's project — see resolution #16):

```
Corpus/
├── CorpusCase.cs                 # the record + System.Text.Json source-generated context
├── CorpusLoader.cs               # jsonl reader, id-uniqueness and schema validation
├── CorpusHost.cs                 # boots the four plugins over the real PluginLoader, once
├── ParseCorpusTests.cs           # per-kind positive assertions
├── RoutingCorpusTests.cs         # cross-kind arbitration (§6)
├── TagCorpusTests.cs             # shared/*.jsonl against the host tag extractor alone
├── CoverageTests.cs              # pattern histogram + ordering + orphan patterns (§7.2)
└── RatchetTests.cs               # the three monotonic counters (§7.3)
```

`System.Text.Json` source-generated serialization, because `src/Directory.Build.props` sets
`TreatWarningsAsErrors=true` and reflection-based `Deserialize` raises IL2026/IL3050. (This is not
theoretical — it is the exact failure this document's own benchmark harness hit on this machine.)

Every case becomes one NUnit test case via `[TestCaseSource]`, named by `id`, so a failure names the
case and a CI log is greppable. 2,522 cases × 4 kinds of routing assertion is ~10 k assertions and runs
in single-digit seconds at the measured 0.078–0.24 ms per title.

### 5.5 Sourcing and attribution

`_sources.json`:

```jsonc
{
  "sources": [
    { "app": "sonarr",  "repo": "ArronixCore/src/NzbDrone.Core.Test/ParserTests",
      "commit": "<sha>", "license": "GPL-3.0-only", "imported": "2026-08-10",
      "note": "Fixtures in this fork are already anonymized upstream ('Series Title')." },
    { "app": "radarr",  "repo": "Radarr/Radarr",  "commit": "<sha>", "license": "GPL-3.0-only", "imported": "2026-08-10" },
    { "app": "lidarr",  "repo": "Lidarr/Lidarr",  "commit": "<sha>", "license": "GPL-3.0-only", "imported": "2026-08-10" },
    { "app": "readarr", "repo": "Readarr/Readarr","commit": "<sha>", "license": "GPL-3.0-only", "imported": "2026-08-10" }
  ]
}
```

All four upstreams and Arronix itself are GPL-3.0 (`LICENSE.md` / `LICENSE` in each tree), so importing
is license-compatible. Attribution is therefore an obligation, not a courtesy, and `origin` carries it
per case rather than in a blanket header — which also makes "why does this case expect *that*?"
answerable by opening one upstream file at one line.

**Import is a one-shot script, not a build step.** `tools/corpus-import/` reads the four fixture
directories, extracts `[TestCase]` rows with their asserting method body, maps the assertion shape to the
case schema, and writes the `.jsonl` files. Its *output* is committed; the script is kept for a future
re-import and is not referenced by any project. The upstream trees are read-only references outside this
repository and must never become a build input.

**Mapping the assertions.** Each upstream fixture's asserting method fixes the expectation shape, so the
mapping is per-method, not per-case:

| Upstream fixture | Maps to |
|---|---|
| `SingleEpisodeParserFixture.should_parse_single_episode(title, series, s, e)` | `units:[{c:{aired:"s.e"}}]`, `expect.title`, `absent.coords:["absolute"]` |
| `MultiEpisodeParserFixture` | N `Single` units |
| `SeasonParserFixture` | one `span` unit |
| `DailyEpisodeParserFixture` | `c:{airdate:"yyyy-mm-dd"}` |
| `AbsoluteEpisodeNumberParserFixture` | `c:{absolute:"n"}` |
| `AnimeMetadataParserFixture` | `tags.sub`, `tags.hash` |
| `QualityParserFixture` | `shared/…` + `expect.tags` only, no `units` |
| `LanguageParserFixture` | `shared/language.jsonl`, `tags.lang[]` |
| `ReleaseGroupParserFixture` | `shared/group.jsonl`, `tags.grp` |
| `CrapParserFixture` / `HashedReleaseFixture` | `expect.claim: "none"` |
| `NormalizeSeriesTitleFixture` / `NormalizeTitleFixture` | a fourth family, `shared/canonical.jsonl`, asserting `NormalizedTitle.Canonical` |
| `PathParserFixture` | `src: "file"` |
| `UnicodeReleaseParserFixture` | ordinary cases; kept because they are the folding tests |

Rows whose asserting method has no clean mapping (`ValidateParsedEpisodeInfoFixture`,
`SceneCheckerFixture`, `IsoLanguagesFixture`, `SlugParserFixture`, `UrlFixture`,
`FingerprintingServiceFixture` — ~230 rows) are **not** imported: they test functions that do not exist
in this design, and importing them as `skip` would be corpus debt with no burn-down path. Recorded in
§10, not hidden.

---

## 6. Cross-kind negative testing

### 6.1 The measurement that makes this mandatory

Method: extract all 104 live patterns from `ReportTitleRegex` (`src/NzbDrone.Core/Parser/Parser.cs:21-448`);
compile with `IgnoreCase | Compiled`; for each title from another app's fixtures, take the **first**
matching pattern exactly as `Parser.ParseTitle` does (`Parser.cs:729-736`); record whether that match
captured any of `season`, `episode`, `absoluteepisode` or `airyear`.

| Titles from | Count | First-pattern match | …of which captured a **TV coordinate** |
|---|---:|---:|---:|
| Radarr movie fixtures | 159 | 130 | **130** |
| Lidarr music fixtures | 120 | 53 | **53** |
| Readarr book fixtures | 117 | 53 | **53** |

Examples, all from other apps' own test suites:

```
R.I.P.D.2013.720p.BluRay.x264-SPARKS                     → season 20, episode 13
1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE              → season 19, episode 79
V.H.S.2.2013.LIMITED.720p.BluRay.x264-GECKOS             → coordinate captured
Gary Clark Jr - Live North America 2016 (2017) MP3 192kbps → coordinate captured
Fall Out Boy - 02 - Title.wav                            → season 1, episode 2
VA - The Best 101 Love Ballads (2017) MP3 [192 kbps]     → coordinate captured
```

Every one of these would be rejected downstream today, because Sonarr has no *series* to match them to.
That is the point: **the only thing making the four parsers kind-disjoint is that each runs in a process
where the other three kinds do not exist.** A unified host with one database deletes that guarantee, and
nothing in `unified-host-runtime.md` replaces it. Cross-kind negative testing is therefore not "a genuine
advantage of unification" that would be nice to have — it is the mechanism that repays a guarantee
unification takes away.

### 6.2 The routing rule

Host-owned, in `Arronix.Host/Parsing/ReleaseRouter.cs`. Four gates, in order, cheapest first:

1. **Search scope.** If the release arrived from a search — `SearchOrigin` is `Automatic`,
   `UserInvoked` or `Interactive` — the requesting kind is authoritative and is the *only* parser run.
   `ReleaseQuery.MediaKind` and `SearchKindId` (spec §2.11) already carry it. This is free and covers the
   large majority of releases the host ever parses.
2. **Category gate.** Otherwise, candidate kinds are those whose `SearchKind.Categories` intersect the
   release's categories, using the verbatim newznab taxonomy the milestone already adopted (spec §5.4,
   resolution #27). 2000s → movies, 3000s → music, 5000s → TV, 7000s → books. This is the same gate
   Prowlarr applies before dispatch (`ReleaseSearchService.cs:186`), reused on the inbound side.
3. **Strict-highest claim.** Run the remaining candidates' parsers; the strictly highest
   `ReleaseReading.Claim` wins.
4. **Matcher tie-break, then refuse.** On a tie, call each tied kind's `IReleaseMatcher`; the kind that
   resolves to a *monitored* library unit wins. If still tied, the release is **rejected** with
   `CoreErrorCode` and a health warning naming both kinds and the title. Never guess.

Step 4's refusal is deliberate and is the one behavior no existing *arr can exhibit. Guessing between
two kinds means importing a file into the wrong library and renaming it there — the one failure mode
users cannot recover from without knowing what happened.

**Feed categories are required for step 2 and `ReleaseCandidate` cannot say "unknown".**
`ReleaseCandidate` (`DTOs/ReleaseCandidate.cs`) requires `MediaKindId MediaKind` positionally. Resolution 11
adds `public static readonly MediaKindId Unattributed` to `MediaKindId` so an RSS indexer can state the
truth. Its original justification — that a static is legal where a reshape is not — no longer holds
(`stability.md` is now explicit that below 1.0.0 any contract may be reshaped), so the choice between a
sentinel and a nullable slot is open and is flagged for re-decision in §10. Categories ride in
`ReleaseCandidate.AdditionalData` under a published key until a wire type carries them; recorded as a wart
in §10, and that wart has no version-boundary excuse either.

### 6.3 How the corpus tests it

**Derived, not hand-written.** For every case in the corpus:

- the declared `kind` must claim the title at ≥ `expect.claim` (default `high`);
- **no other kind may claim it at or above that level**;
- if the case carries `absent.coords`, the *other* kinds' readings are also checked against it, which is
  what catches "movies reads a season number out of a year".

That converts 2,522 single-kind cases into ~10 k cross-kind assertions at zero authoring cost, and it
means every new TV case is automatically a movie/music/book negative.

**Overrides, budgeted.** Some titles are genuinely ambiguous — `Discovery TV - Gold Rush : 02 Road From
Hell [S04].mp4` sits in Readarr's book fixtures and is unmistakably a TV release. Such a case carries an
explicit block that replaces the derived rule:

```jsonc
{"id":"routing/0031","title":"Discovery TV - Gold Rush : 02 Road From Hell [S04].mp4",
 "origin":"readarr@<sha>:src/NzbDrone.Core.Test/ParserTests/ParserFixture.cs:88",
 "claims":{"tv":"high","books":"low","movies":"none","music":"none"},
 "note":"Genuinely a TV release living in Readarr's fixtures; books must not out-claim tv."}
```

The **number of cases carrying an explicit `claims` block is a ratchet** (§7.3). It starts at whatever
the import produces and may only go down. A regex change that makes a kind greedier shows up as a
demand to add overrides, which is a demand the ratchet refuses.

---

## 7. Regression discipline

### 7.1 The rule

**A pattern change ships with corpus cases, or it does not ship.** Three mechanisms make that
enforceable rather than aspirational.

### 7.2 Patterns are named, and coverage is measured

Every pattern is a `[GeneratedRegex]` partial property with a stable id:

```csharp
// src/Arronix.Plugin.Tv/Patterns/TvAiredPatterns.cs
internal static partial class TvAiredPatterns
{
    // id: tv/aired/s-e-basic
    [GeneratedRegex(
        @"^(?<title>.+?)(?:[-_\W](?<![)\[!]))+S(?<season>\d{1,2})(?:[ex](?<episode>\d{2,3}))+",
        RegexOptions.IgnoreCase, ParsingLimits.MatchTimeoutMs)]
    internal static partial Regex SeasonEpisodeBasic { get; }
}
```

`ReleaseReading.PatternId` carries which one fired. `CoverageTests` then asserts three things:

1. **No orphan patterns.** Every `[GeneratedRegex]` member under `src/Arronix.Plugin.*/Patterns/` is the
   `PatternId` of at least one accepted corpus case. The member list is read **from the source files on
   disk** — the same technique `ProjectShapeTests` already uses to read csproj XML — so this needs no
   contract and no reflection over plugin types, which the closed-registration design forbids.
2. **No dead cases.** Every accepted case names a pattern that exists.
3. **Order tracks frequency.** The pattern table is ordered by corpus hit count, descending, within a
   tolerance of ±3 positions. Sonarr's 104-entry array is accreted in commit order; the first-match-wins
   loop means order is semantics *and* cost, and nothing today keeps it honest.

Point 3 has a second effect worth naming: because ordering is semantic under first-match-wins, a
reordering is a behavior change, and the corpus catches it as a set of case diffs rather than as a
production incident.

### 7.3 Three ratchets

Monotonic counters in `_manifest.json`, asserted by `RatchetTests`, each allowed to move in one direction
only:

| Ratchet | Starts at | May only |
|---|---|---|
| `xfail` — imported cases that do not yet pass | whatever the import produces | decrease |
| `routingOverrides` — cases needing an explicit `claims` block | whatever the import produces | decrease |
| `orphanPatterns` — patterns with no corpus case | 0 after import; non-zero only during a WIP | decrease |

The `xfail` mechanism is what makes a wholesale import survivable. 2,522 cases will not all pass against
four freshly written plugin parsers; requiring them to would make "import the corpus" a blocking,
unbounded task with no partial value. Admitting a failing case as `xfail` with a mandatory `note` makes
the gap **visible, counted and monotonically shrinking** from day one, and — the part that actually
matters — makes a *new* failure impossible to merge, because a new failure is not `xfail` and turns the
build red.

### 7.4 Review

- Corpus files carry a `CODEOWNERS` entry. A pull request touching them needs a parsing reviewer.
- **Adding** cases is routine. **Changing** an existing case's `expect` is a behavior change and requires
  a `note` on the case recording why the old expectation was wrong. The `note` field is the audit trail;
  it lives with the case forever, next to its `origin`.
- Deleting a case requires the same, and leaves its `id` unreused.
- A new pattern arrives with: the pattern, ≥1 positive case, and — if the pattern can plausibly fire on
  another kind's titles — a routing check that the full derived cross-kind sweep still passes. The sweep
  runs on every build, so this is automatic rather than a checklist item.

### 7.5 Property-based testing for the normalization layer

Recommended, and worth the ~120 lines. **No package** (resolution #19): a seeded
`TitleGenerator`/`Shrinker` pair in `src/Arronix.Common.Tests/Naming/` and
`src/Arronix.Host.Tests/Parsing/`, driven by `[TestCase]`-supplied seeds so a failure is reproducible from
the test name alone.

The generator draws from a token alphabet mined from the corpus — separators (`.`, `_`, `-`, ` `),
brackets (`[]`, `()`, `【】`), digit runs, known tags, CJK ranges, combining marks, and **unpaired
surrogates** (which `TextFolding.Fold` explicitly handles by degrading rather than throwing — that
behavior has no test today). The shrinker greedily deletes tokens while the property still fails.

Properties worth asserting, strongest first:

| Property | Why it is the interesting one |
|---|---|
| `Coordinate.TryParse(c.ToString()) == c` for every generated coordinate | The corpus expresses every expectation as this string form; if the round-trip is lossy the whole corpus is lying. |
| `Preprocess(Preprocess(x)) == Preprocess(x)` | Sonarr applies all 14 `PreSubstitutionRegex` rules unconditionally, in order, with no confluence guarantee (`Parser.cs:690-698`); a rewrite that re-creates a pattern an earlier rewrite consumed is a live bug class. |
| `Fold(Fold(x)) == Fold(x)` | Contributed folds may expand to multiple characters (`TextFolding.cs:101`); a fold whose output re-triggers another fold is non-terminating. |
| `Canonical(x) == Canonical(x + " ")`, and for any wrapping in separators | Canonicalization exists to make two spellings compare equal; whitespace sensitivity defeats it silently. |
| Every step except `expand-six-digit-date` and `pre-substitute` is length-non-increasing | Catches a `RegexReplace` whose replacement re-inserts what it removed. The two exceptions are declared, so the assertion is exact rather than approximate. |
| No step throws on any generated input, including unpaired surrogates and lone combining marks | The parse path is the one that receives hostile remote input. |
| `Fold` never lengthens by more than `maxExpansion × length` | Bounds the memory a hostile title can cost in normalization. |

`FsCheck` and `CsCheck` were both considered. Neither is registered in `Directory.Packages.props`,
neither is Microsoft-owned, and the generator that matters here is entirely domain-specific — a general
library would supply the shrinker and little else. Declining is consistent with the dependency policy and
costs about a hundred lines.

---

## 8. Performance

All figures measured on this machine, `net10.0`, against the real 104-pattern Sonarr `ReportTitleRegex`
set extracted from `src/NzbDrone.Core/Parser/Parser.cs:21-448`.

### 8.1 What it costs today

| Scenario | Cost |
|---|---|
| Constructing 104 patterns with `RegexOptions.Compiled` | **17.8 ms** |
| Matching title, first-match-wins over the 104 | **0.078 ms** |
| Non-matching ordinary title, full sweep of 104 | **0.239 ms** |
| Adversarial non-match, 213 chars | 2.20 ms |
| Adversarial non-match, 413 chars | 8.77 ms |
| Adversarial non-match, 813 chars | 38.4 ms |
| Adversarial non-match, 1,613 chars | 148 ms |
| Adversarial non-match, 3,213 chars | **741 ms** |

Growth is quadratic — 4× the time for 2× the length — driven by the `(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?))+`
family of nested quantifiers (`Parser.cs:73,77,85,89` among others; 65 `)+` constructs in the file).
**No `matchTimeout` is set on any regex in any of the four parsers.** The input is a release title from a
remote index.

Compiled-regex construction across the whole Sonarr parser is larger than the 17.8 ms above:
`RegexOptions.Compiled` appears 183 times in Sonarr's `Parser/`, 76 in Radarr's, 67 in Lidarr's and 63 in
Readarr's. A unified host loading four plugins pays all of it.

### 8.2 The N× hazard unification introduces

At 0.239 ms per unrouted title per kind, an RSS batch of 1,000 releases costs **~0.24 s** in one *arr
today and **~0.96 s** in a naive unified host that runs all four parsers. §6.2's routing gates are
therefore a performance control as well as a correctness one: search-scoped parsing reduces the common
case to one kind, and the category gate reduces the RSS case to typically one, occasionally two.

### 8.3 `[GeneratedRegex]`, verified

Verified on this machine in a **zero-`PackageReference`** `net10.0` project — which matters because
`Arronix.Plugin.*` projects declare zero packages (spec §7.3) and the regex source generator ships in the
SDK, not in a package:

| Engine | 200 k `IsMatch` on a representative tag pattern |
|---|---|
| `[GeneratedRegex]` | **49.6 ms** |
| `new Regex(..., RegexOptions.Compiled)` | 78.2 ms |
| `[GeneratedRegex]` + `NonBacktracking` | 66.9 ms |

`[GeneratedRegex]` on partial **properties** (C# 13 / .NET 9+) works, and
`GeneratedRegexAttribute(pattern, options, matchTimeoutMilliseconds)` produces a regex that genuinely
throws `RegexMatchTimeoutException` — both confirmed by execution, not by reading documentation.

Adopt it everywhere. Three benefits, one of which is not about speed:

1. No construction-time IL emission, no first-use JIT stall, trim- and AOT-safe.
2. ~1.6× faster steady-state on this pattern shape.
3. **Every pattern becomes a named member**, which is precisely the `PatternId` §7.2 needs. The naming
   benefit is worth more than the speed.

The one thing it cannot do is a runtime-constructed pattern — relevant only to a future data-defined
provider pack (Cardigann), which the milestone already defers.

### 8.4 The controls

| Control | Setting | Rationale |
|---|---|---|
| `[GeneratedRegex]` for every pattern | mandatory | §8.3 |
| `NonBacktracking` for the agnostic tag layer | mandatory | Measured: **13 of 15** quality patterns accept it. Linear time on indexer-controlled input for a 35 % constant-factor cost. |
| `NonBacktracking` for coordinate patterns | **forbidden** | Measured: **5 of 104** accept it, and where it compiles `Groups["episode"].Captures.Count` collapses from 3 to 1 — which `Parser.cs:940`, `:941` and `:1043` require for multi-episode fan-out. |
| `matchTimeout` on every backtracking pattern | `Arronix:Parsing:MatchTimeoutMs`, default **100** | Nothing sets one today; §8.1 shows why. |
| Pattern quarantine breaker | after 3 timeouts, skip for the process lifetime, raise a health check naming the `PatternId` | A pattern that times out on one hostile title will time out on every sibling in the same RSS batch; without a breaker one poisoned feed serializes the whole sync. |
| Input length cap | `Arronix:Parsing:MaxTitleLength`, default **512**; over that, decline before any coordinate pattern runs | The cheapest control by a wide margin: 741 ms at 3.2 KB versus 2.2 ms at 213 chars. Real release titles are well under 256 characters. |
| Routing gates before parser fan-out | §6.2 | Turns the unified host's N× parse cost back into 1×. |
| Pattern table ordered by corpus hit frequency | asserted, ±3 | Order is both semantics and cost under first-match-wins. |
| `Regex.EnumerateMatches` / span overloads for gate patterns | where no captures are needed | Allocation-free; the reject/junk gates run on every title including the ones that are then discarded. |

Two performance tests belong in `Arronix.Host.Tests/Corpus/`, both asserting a **bound**, never a
duration: (a) no single corpus title exceeds the configured match timeout on any pattern; (b) the full
corpus sweep completes within a generous ceiling, so an accidental exponential lands as a red build
rather than a slow one.

---

## 9. Work packages

Numbering continues `unified-host-runtime.md` §7. Each owns exactly one directory tree; no file is
written by two. Wave numbers refer to that document's §7.1.

| WP | Owns | Files | Depends on | Wave |
|---|---|---|---|---|
| **WP-21** | `src/Arronix.Abstractions/Shape/` (**new files only**; WP-2's files are not touched) | `IReleaseTitleParser.cs`, `ReleaseParseRequest.cs`, `ReleaseReading.cs`, `ReleaseUnitAddress.cs`, `ReleaseTags.cs`, `ReleaseRevision.cs`, `TagProvenance.cs`, `NormalizedTitle.cs`. **Edits:** `MatchRequest` (+`Normalized`), `Identity/MediaKindId.cs` (+`Unattributed` static) | WP-2 | 2 |
| **WP-22** | `src/Arronix.Abstractions/Plugins/IPluginRegistry.cs` (one method) + `Arronix.Plugins/Registration/CapabilityMatrix.cs` (one row) | `AddReleaseTitleParser` | WP-5, WP-7, WP-21 | 4 |
| **WP-23** | `src/Arronix.Host/Parsing/` | `ReleasePreprocessor.cs`, `PreprocessSteps.cs`, `ReleaseTagExtractor.cs`, `TagPatterns.cs`, `ReleaseTagVocabulary.cs`, `ReleaseRouter.cs`, `PatternQuarantine.cs`, `Composition/ParsingRegistration.cs`, `Configuration/ParsingOptions.cs` | WP-8, WP-21 | 6 |
| **WP-24** | `src/Arronix.Plugin.Tv/Patterns/` + `TvReleaseTitleParser.cs` | the TV coordinate patterns (`aired`, `absolute`, `airdate`, `scene`), split by concern, all `[GeneratedRegex]` | WP-16, WP-21 | 5+ |
| **WP-25** | `src/Arronix.Plugin.{Movies,Music,Books}/Patterns/` + their `*ReleaseTitleParser.cs` | same shape; three disjoint trees, parallel | WP-13..15, WP-21 | 5+ |
| **WP-26** | `tools/corpus-import/` **and** `src/Arronix.Corpus/` | the one-shot importer; `_sources.json`, `_manifest.json`, and the generated `.jsonl` files | WP-21 | 6 |
| **WP-27** | `src/Arronix.Host.Tests/Corpus/` + `src/Arronix.Common.Tests/Naming/PropertyTests/` | `CorpusCase.cs`, `CorpusLoader.cs`, `CorpusHost.cs`, `ParseCorpusTests.cs`, `RoutingCorpusTests.cs`, `TagCorpusTests.cs`, `CoverageTests.cs`, `RatchetTests.cs`, `BoundsTests.cs`; `TitleGenerator.cs`, `Shrinker.cs`, `FoldingPropertyTests.cs`, `PreprocessPropertyTests.cs` | WP-18, WP-23..26 | 8 |
| **WP-28** | the seam swap (§3.5): `src/Arronix.Abstractions/Quality/IQualityModel.cs`, `Arronix.Abstractions/Parsing/`, `Arronix.Abstractions/DTOs/ParsedRelease.cs`, `IPluginRegistry`, `CapabilityMatrix`, the four `Arronix.Plugin.*/*ReleaseParser.cs` and `*QualityModel.cs` | **Edits:** `EvaluateQuality(ParsedRelease)` → `EvaluateQuality(ReleaseTags)`. **Deletes:** `IReleaseParser.cs`, `ParsedRelease.cs`, `AddReleaseParser`, its capability row, the four `*ReleaseParser.cs`, the `Arronix.Abstractions.Parsing` namespace, and the matching entries in `docs/contracts/stability.md` and `src/Arronix.Abstractions/README.md` | WP-24, WP-25, WP-27 | 9 |

Ordered execution, with the reason each step must precede the next:

1. **WP-21** — the contract, because everything else compiles against it.
2. **WP-22** — registration, so a plugin can actually hand the host a parser.
3. **WP-23** — the host preprocessor, tag extractor and router. Buildable and testable with *zero*
   plugins; the `shared/*.jsonl` families alone exercise it.
4. **WP-26 (import only)** — run the importer, commit the corpus, set all three ratchets at their
   imported values. Do this **before** the plugin parsers, so every parser is written against a live
   target rather than validated after the fact.
5. **WP-24** — TV first, because it carries 1,398 of the 2,522 cases, four coordinate spaces, the
   provenance-sensitive one, the sequence span and the multi-unit fan-out. If the contract is wrong, TV
   is where it breaks.
6. **WP-25** — the other three in parallel, each against its own imported cases.
7. **WP-27** — the runner, the cross-kind sweep, the coverage and ratchet tests, the property tests.
8. **WP-28** — close the window: retarget `IQualityModel` and delete `IReleaseParser` / `ParsedRelease`
   (§3.5). This is last because it is the only step that requires every plugin to already have a working
   `IReleaseTitleParser`, and it must not be skipped — leaving it undone is how the codebase ends up with
   two parse seams permanently, which is the outcome §3.5 exists to prevent.
9. Burn the `xfail` ratchet down; each reduction is an ordinary reviewed change.

**Coordination dependencies on `unified-host-runtime.md`, to be agreed before WP-21 starts:**

- **`ARX0013` is shared.** WP-21 adds `[Experimental(ExperimentalContracts.Shape, …)]` types to a
  namespace WP-2 owns. No governance edit is needed (WP-1 already registers `ARX0013`), but the two WPs
  must not both edit `MatchRequest`; this document proposes WP-21 makes that single-field edit.
- **`MediaKindId.Unattributed`** is a change to `MediaKindId`. It still needs the `stability.md` 0.3.0
  version-history entry that WP-1 owns; it no longer needs to be a *static sentinel* to avoid reshaping the
  struct, since below 1.0.0 the struct may be reshaped. Resolution 11 should be re-decided on its merits.
- **`IPluginRegistry` and `CapabilityMatrix`** are WP-5/WP-7 files; WP-22 adds one line to each and must
  be sequenced after them. WP-28 removes `AddReleaseParser` from the same two files.
- **`unified-host-runtime.md` §2.11's sentence** *"Title parsing is the `IReleaseParser`"* should be
  amended to name `IReleaseTitleParser` as the sole seam and record `IReleaseParser` as deleted at WP-28.
  That is the deviation of resolution 1, stated in the form the spec's own §9 uses.
- **`IQualityModel`** lives in `Arronix.Abstractions/Quality/`, which no WP in either document owned before
  WP-28. Its retarget from `ParsedRelease` to `ReleaseTags` (§3.5) must be agreed with whoever owns the
  quality milestone before WP-28 runs.

---

## 10. Deferred, with the trigger that un-defers it

| Deferred | Where it would live | Trigger |
|---|---|---|
| **Embedded-metadata (MediaInfo/tag) reading** | the import pipeline, not parsing | The Import milestone. `TagProvenance.EmbeddedMetadata` and `Container` exist now so the tag bag does not change shape when it lands. |
| **Feed categories as a first-class wire field** | a `ReleaseCandidate` successor | Categories ride in `AdditionalData` under a published key until then. Recorded as a wart: §6.2's category gate is only as good as that key. |
| **`FormatFamily.TagGroups`** — restricting which tag extractors run per family | `Arronix.Abstractions/Shape/FormatFamily.cs` | A **measured** cross-family false positive. Today the whole agnostic tag set runs unconditionally because it is ~30 short patterns and a resolution tag on an ebook is simply ignored by the ebook ladder. |
| **A contributed tag vocabulary (`IReleaseTagVocabularyProvider`)** | Abstractions, modeled on `IDiacriticFoldingProvider` | A kind whose tags the closed host vocabulary genuinely cannot express. `ReleaseTags.Extra` covers it until then at zero contract cost. |
| **Alternative readings within one kind** | `ReleaseReading` | A kind where the *parser* can see two incompatible interpretations that library context cannot resolve. None of the four surveyed kinds does; Sonarr's ambiguity is all resolved in `ParsingService`. |
| **Cross-level acquisition validation** | `ValidatedShape` | Already deferred by the spec §8; `ReleaseUnitAddress.LevelId` makes it *representable* now, and still unvalidated. |
| **Importing the ~230 unmapped upstream rows** (`ValidateParsedEpisodeInfoFixture`, `SceneCheckerFixture`, `IsoLanguagesFixture`, `SlugParserFixture`, `UrlFixture`, `FingerprintingServiceFixture`) | `src/Arronix.Corpus/` | The subsystem each tests actually exists. Importing them as permanent `skip` rows would be debt with no burn-down path. |
| **Audio fingerprinting** (Lidarr `FingerprintingService.cs`, 507 lines) | a music-plugin concern, needs an external binary | A music milestone. It is not parsing and must not be modeled as parsing. |
| **A Roslyn analyzer forbidding `new Regex(` in `src/Arronix.*`** | a new analyzer project | `CoverageTests` catches an unnamed pattern at build time already; an analyzer would catch it at edit time. Same reasoning as the spec's deferred `ARX1001`. |
| **Corpus sharding / parallel execution** | `Arronix.Host.Tests` | The sweep exceeding ~30 s. At the measured 0.078–0.24 ms per title it is far from that. |
| **Promotion of `IReleaseTitleParser` and `ReleaseTags` out of `[Experimental]`** | `stability.md` | 1.0.0. Below it there is no stable surface to promote into — `stability.md` §"The Stable Surface" — so this is a calendar item, not a design question. Four implementers ship in this milestone, which already satisfies the promotion rule's evidence clause. |

---

## 11. Where this design is honestly thin

Recorded rather than glossed, in the style of the spec's §2.13.

1. **The claim ladder is coarse.** `MatchConfidence` has five members and a parser must map "I matched
   pattern 3 of 104, which is quite specific" onto one of them. Reusing the stable enum is still right —
   inventing a sixth vocabulary is worse — but the mapping will be a judgment call per plugin, and the
   routing rule's step 4 (refuse rather than guess) exists partly because of it.
2. **The category gate depends on indexers populating categories.** Many private trackers do, some public
   feeds do not. When categories are absent, step 2 degrades to "all kinds are candidates" and the whole
   weight falls on the claim ladder. The corpus measures how well that works, but it cannot make a feed
   supply data it does not have.
3. **The 130/159 measurement is a regex-layer measurement, not a parse-outcome measurement.** It counts
   titles where Sonarr's first matching pattern captured a TV coordinate; the current product would then
   reject most of them at the matching stage for want of a series. The finding is not that the *arrs are
   broken — it is that their disjointness comes from process isolation, which Arronix removes. Stated
   this way it is still decisive; stated as "Sonarr mis-parses 82 % of movie titles" it would be false.
4. **`shared/*.jsonl` will surface real disagreements between the four apps.** Merging 711 quality rows
   and 388 release-group rows from four codebases that have drifted for years will produce cases where
   two upstreams expect different answers for the same string. Each is a decision somebody must make.
   That is the merge working, not failing — but it is unbudgeted work, and it lands on WP-26.
5. **Ordering-by-frequency and first-match-wins interact.** Sorting the pattern table by corpus hit count
   changes which pattern wins for titles that match several. The corpus catches the change; the ±3
   tolerance is a guess, and the first reordering will show whether it is the right one.
