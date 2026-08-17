# The clean-room plan — removing *arr-derived material and flipping the license

> **Purpose.** After this plan executes, nothing in the `arronix/main` tree derives from GPL-licensed *arr
> sources, and `LICENSE.md` can be replaced with the owner's chosen license.
>
> **State verified against the tree, 2026-08-17.** `arronix/main` is a single-commit orphan root
> (`bbec8cec8`, 884 tracked files). Build 0 errors / 0 warnings; 2,480 tests passing. Every line reference
> below was read, not taken from a prior summary; where a prior summary was wrong, the correction is
> stated.
>
> **Scope.** This document is the inventory, the classification, the replacement method and the sequencing.
> It does **not** design the quality model that replaces the ladder — that is
> [`quality-axes.md`](./quality-axes.md), and this plan refers to it rather than duplicating it.
>
> **Amended 2026-08-17 against the adversarial critique.** The critique's verdict on this document was
> *proceed with amendments; the amendments are additive, none is blocking*, and its verification of the
> inventory (101 rung rules + 7 fallbacks, 29 guards, 11 patterns, 249 corpus rows, 686 fixture rows) came
> back correct except where noted below. Eight findings are applied in place: the oracle is budgeted
> (S-C1, §3.1 control 5); the fingerprint gate reports pass/fail, not a score (S-C2, §3.1 role table and
> 4.3); sanitization is extended from syntactic to semantic (S-C3, §3.1 control 2); the `EvidenceCell`
> baseline is restated in the axes vocabulary and recorded as derived (S-C4/S-C10, §3.3); negative cases
> are added to the cell and gated at 100% (S-C5, §3.3); the documentation scrub is re-sized and split
> (S-C6, new 4.6 and Steps 8a/8b); the 40-character merger exemption is reconciled with the exact-match
> rule (S-C7, §3.2); and `ReleaseTagScanner.cs` plus four assertion-carrying fixtures join the inventory,
> with every published total re-derived (S-C8, §1.2, §1.3, §2.2). **Two amendments depend on the sibling
> design's resolved decisions and cite them by name rather than by quotation, because that document is
> being amended in parallel: `D-7` (Remux is its own `Origin` member) and `D-8` (a lexicographic
> precedence core with bounded facet scores consulted only on core ties). Both are recorded as RESOLVED in
> `docs/open-decisions.md` Part 7.**
>
> **This is engineering analysis, not legal advice.** The classification below is a conservative
> engineering judgment about what is safe to keep. The owner should have counsel confirm the final license
> call, in particular Part 2's borderline rows and Part 4.5's history question. Nothing here is a
> representation that any specific item is or is not copyrightable.

---

## Part 0 — What the plan has to make true

Two claims have to become defensible, and they are not the same claim.

| # | Claim | What makes it true |
|---|---|---|
| **C-1** | No file in the tree is a copy or adaptation of *arr source. | Every item classified (c) in Part 2 is gone or independently rewritten, and a standing gate refuses its return. |
| **C-2** | The remaining code was independently created. | A provenance record: the spec, the agent briefs, who could read what, and the gate results — kept as an artifact, not reconstructed later from memory. |

C-1 is achievable by deletion and rewrite and is mechanically checkable. C-2 is a *process* claim, and the
only thing that can ever support it is a contemporaneous record. Part 3.1 is therefore not ceremony; it is
the deliverable that C-2 rests on.

**The claim the repository already makes, and cannot currently support.** `README.md:220-222` states that
"the `Arronix.*` sources are written fresh rather than derived from it," and that the repository "still
contains the Sonarr v5 tree (`src/NzbDrone.*`, `src/Sonarr.*`, `frontend/`)." On this branch both halves
are wrong: the legacy tree is **absent** (`src/` holds only `Arronix.*` projects), and the `Arronix.*`
sources include 88 verbatim expressions and 503 data rows carried across from Radarr. `README.md:214-215`
adds a third false claim — that the legacy application "still builds from `src/Sonarr.sln`" — and
`ARCHITECTURE.md:218` and `:705-718` repeat the stale tree claim. Correcting those two documents is a step
in Part 4, not an aside — the false half is precisely the assertion the license flip depends on.

---

## Part 1 — Inventory, verified against the tree

Sweep method: `grep -rniE "radarr|sonarr|lidarr|readarr|prowlarr|servarr|nzbdrone"` over `src/`, `docs/`
and the root markdown, excluding `bin/` and `obj/`; then every hit read in place; then a second sweep for
regex-bearing files (`GeneratedRegex`, `Regex =`, `const string …Regex`) to catch derived expressions whose
comments do not name a source. 73 files carry a textual mention. The tables below are the files that carry
*material*, not merely a citation.

`_reference/` — the local clone directory that 7 doc-comments cite by path — **does not exist in this
repository**. The only *arr source still on the machine is
`/Users/adam/Code/GitHub/Arronix/NzbDrone.Common` (a standalone modernized extraction of Sonarr's
`NzbDrone.Common`, namespace `Sonarr.Common.*`), which is a sibling of `ArronixCore`, not part of it.
Step 0 quarantines it.

### 1.1 The Movies media kind — three files

| File | Lines | What it carries | Class |
|---|---|---|---|
| `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs` | 472 | **`EditionRegex`** (:39-40) — a ~500-character alternation referenced by three patterns and one guard. **11 title patterns** (:99-156) under the header comment *"Verbatim expressions; the order is Radarr's"* (:96-97). **29 guard expressions** (:161-211), including the `br-disk` mega-regex with nested lookaheads (:167-168) and `german-remux` (:172-173). **`StopWordExpression`** (:53-54) with its lookarounds, comment-attributed to "Radarr's NormalizeRegex stop words" (:48-50). **101 rung-resolution rows + 7 container fallbacks** (:237-391), the header describing them as "the 610-line branch cascade of MovieQualityTokenParser as ordered rows" (:232-236) and the fallback table as "Radarr's thirty-container table collapsed" (:377-378). Plus 7 transliteration rows (:58-62), 5 query rewrites (:74-81), 2 token rows (:226-227), and provenance comments at :84-85 and :151. | **(c)** |
| `src/Arronix.Plugin.Movies/Definition/MoviesLadder.cs` | 109 | **30 rungs** (:38-39 Unknown, :42-88 the ladder) carrying name, rank, **weight**, source, resolution, **tie-group name** and **min/max/preferred MB-per-minute size band**. The class remark states it is "every source × resolution × modifier rung Radarr ships" (:7-9) and attributes the WEBDL/WEBRip weight tie to Radarr by name (:12-18). | **(c)** |
| `src/Arronix.Plugin.Movies/Definition/MoviesCorpus.cs` | 319 | **249 curated cases**: 168 quality cases (:38-208), 52 separator cross-product cases (:210-269), 29 title cases (:271-302). The remark states they "are ported rather than paraphrased — a case rewritten to suit an implementation stops being evidence" (:14-19) and that episode-shaped rows are "kept verbatim from the surveyed corpus" (:26-29). Titles are anonymized (`Movie Name`, `Some.Movie`) while selection, ordering and expected values are preserved. | **(c)** |

**The anonymization is evidence of copying, not a cure for it.** A row reading
`"Movie.Name.2011.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR"` retains the release group, the token sequence,
the ordering and the expected rung; only the work's title was swapped. What made the corpus valuable —
selection and arrangement — is exactly what survived the edit.

### 1.2 The host engines — the largest block, and the one prior summaries missed

The session's brief predicted three Movies files. The sweep found the same material, and more of it, in
`Arronix.Host`, where the kind-agnostic scanners live.

| File | Lines | What it carries | Class |
|---|---|---|---|
| `src/Arronix.Host/Engines/Parsing/ReleaseTokenVocabulary.cs` | 140 | **9 source-generated expressions.** `SourceTags()` (:56-79) is documented as "Radarr's `SourceRegex` **verbatim** (`QualityParser.cs:17-37`)" — an 18-alternative named-group alternation. Then `ResolutionTags()` (:82-86), `AlternativeResolutionTags()`, `ProperTag()`, `RepackTag()`, `VersionTag()`, `RealTag()`, `RemuxTag()` (:128-132), each carrying a `QualityParser.cs` line citation. Plus a **49-entry container-extension set** (:42-50) attributed to Radarr's `MediaFileExtensions`. | **(c)** expressions; **(a)** extension set |
| `src/Arronix.Host/Engines/Parsing/ReleaseGroupScanner.cs` | 138 | **8 expressions** (:91-137), documented at :10-17 as "a port of Radarr's `ReleaseGroupParser.cs`, order preserved". Includes the `TrailingGroup()` expression with its 20-term negative lookbehind (:92), and **four curated name lists**: 16 exact-match group names (:110), 30 bracket-suffix group names (:116), 24 obfuscation suffixes (:122), 5 tracker suffixes (:134). | **(c)** expressions; **(b)** name lists |
| `src/Arronix.Host/Engines/Parsing/ReleaseLanguageScanner.cs` | 197 | **3 expressions** (:180-196) including the `ShortCodes()` alternation, and a **50-row spelled-out language table** (:25-77). Provenance at :11-21 names `LanguageParser.cs`. The German `DL`/`ML` dual-audio rule (:119-132) is described in its own comment as "the one genuinely non-obvious rule". | **(c)** expressions; **(b)** table |
| `src/Arronix.Host/Engines/Parsing/ReleaseCodecScanner.cs` | 80 | Two token→value tables, 13 video and 18 audio rows (:15-28), attributed at :6-12 to `QualityParser.cs:66-67` and the media-info token tables. Order encodes precedence. | **(a)** rows; **(b)** ordering |
| `src/Arronix.Host/Engines/Parsing/ReleaseTagScanner.cs` | 163 | **Added by the critique (S-C8 i); missed by the original sweep because it carries no expression of its own** — it consumes `ReleaseTokenVocabulary`'s. What it carries is three ported *decisions*, and its own remark (:13-18) states the provenance: "A port of the scan halves of Radarr's `QualityParser.ParseQualityName` and `ParseQualityModifiers` … via `MovieQualityTokenParser`." (1) **Last-match-wins on the source scan** (:37-47), citing `SourceRegex.Matches(...).LastOrDefault()`. (2) The **resolution bucket cascade** (:68-112) — eight ordered mappings that fold `1080i`, `1440p`, `FHD` and `4kto1080p` all onto 1080, `UHD`/`[4K]` onto 2160 only as a fallback. (3) The **PROPER/REPACK version arithmetic** (:114-157) — "REPACK2 is version three". | **(c)** the bucket cascade; **(b)** the other two |
| `src/Arronix.Host/Engines/Naming/NamingModifiers.cs` | 199 | **`CleanTitle()`** (:186-188) — a 130-character punctuation alternation with possessive-suffix lookaheads — and **`TitlePrefix()`** (:194) `^(The|An|A) (.*?)((?: *\([^)]+\))*)$`. Attributed at :58-62 to `FileNameBuilder.cs:302-381`. The other two expressions (`[\/]`, ` \((19|20)\d{2}\)$`) are trivial. | **(c)** the two named; **(b)** the trivial two |
| `src/Arronix.Host/Engines/Naming/NameSubstitutions.cs` | 94 | The 5-rule default substitution table (:31-38) reproducing the surveyed "smart colon" behavior, attributed at :16-22 to `FileNameBuilder.cs:1160-1200`. | **(b)** |
| `src/Arronix.Host/Engines/Naming/TemplateRenderer.cs` | 429 | Behavior citations at :35-38, :168-169, :396-397 (the `([- ._])\1+` separator collapse). No long expression is carried; the rules are restated in code. | **(b)** |
| `src/Arronix.Host/Engines/Naming/MultiUnitStyleRenderer.cs`, `GraphemeText.cs`, `TokenBinding.cs`, `ShapeTokenDeriver.cs`, `DeclarativeRenamePolicy.cs` | 109-252 each | Behavior citations only (:17-18, :12, :7-8, :130, :132). Each states a rule and names where the rule was observed. | **(a)/(b)** |
| `src/Arronix.Host/Engines/Matching/MatchKeyNormalizers.cs` | 76 | The `clean-title` chain (:51-60) "ports the semantics of Radarr's `Parser.CleanMovieTitle`" (:14-15). Four operations, each individually obvious. | **(b)** |
| `src/Arronix.Host/Engines/Matching/MatchKeyExpanders.cs` | 81 | A 20-row arabic↔roman numeral table (:21-42), ported per :10-15 from `MovieService.cs:160-186`. Roman numerals are facts; the 1-20 bound and the descending order are the borrowed choices. | **(a)** rows; **(b)** bound |
| `src/Arronix.Host/Engines/Matching/LayeredKeyLookupStrategy.cs`, `DeclarativeMatcher.cs` | 343, 601 | Cascade *ordering* ported from `MovieService.FindByTitle` (:18-26, :20-21). Structure, no expression. | **(b)** |
| `src/Arronix.Host/Engines/Matching/DistanceAccumulator.cs`, `DistanceFeatureCatalog.cs`, `AssignmentOverFeaturesStrategy.cs` | 213, 142, 219 | Lidarr's distance model: the accumulator shape (:10-11), five per-feature computations with their constants — 10-second grace, 30-second cap, now-relative year maximum (`DistanceFeatureCatalog.cs:60-80`) — and the assignment pipeline (:15). | **(b)** |
| `src/Arronix.Host/Engines/Matching/MunkresSolver.cs` | 360 | A port of Lidarr's `Munkres.cs`, which its own header (:8-13) records as **MIT-licensed work of Robert A. Pilgrim, Murray State University**. Not GPL-derived; a different obligation. | **third-party, see 2.4** |
| `src/Arronix.Host/Engines/Metadata/CatalogDerivations.cs`, `CatalogIdRules.cs` | 354, 226 | The status derivation including the **90-day theatrical-window clause** (:27, :105) attributed to `SkyHookProxy.cs:292-314`, and the four identifier rule kinds (:17). | **(b)** |
| `src/Arronix.Host/Engines/Parsing/RungResolver.cs`, `Engines/Quality/DeclarativeQualityEvaluator.cs` | 132, 250 | Executors, not data: the resolver's first-passing-row semantics (:14-15) and the evaluator's comparison and size gate (:34-35, :187) reproduce the surveyed *behavior* over data supplied by the kind. | **(a)/(b)** |

**Why the bucket cascade is (c) and not (b).** Each individual mapping (`1080p → 1080`) is a fact. The
*selection* of which distinct spellings collapse onto one bucket — that `1080i` loses its interlace, that
`1440p` has no bucket of its own, that the literal upscale token `4kto1080p` is laundered into a clean 1080
— is identical to the source's and is a judgment, not a fact. Tie-break rule applies. This file is also the
one the sibling design depends on most: `quality-axes.md`'s S-A5 amendment un-buckets this scan, so the
rewrite is required on engineering grounds regardless of the classification.

### 1.3 Test fixtures and corpora — 704 ported rows across 11 files, plus four assertion sites

| File | Rows | Provenance stated in the file | Class |
|---|---:|---|---|
| `src/Arronix.Plugin.Movies.Tests/Quality/QualityParserTests.cs` | 179 | ":7 — ported from Radarr's `ParserTests/QualityParserFixture`"; :268 notes Radarr runs the same cross-product | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Parsing/ReleaseGroupParserTests.cs` | 131 | ":6 — ported from Radarr's `ParserTests/ReleaseGroupParserFixture`" | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Naming/NameFormatterTests.cs` | 105 | ":4 — ported from Radarr's `FileNameBuilderFixture`" | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Parsing/MovieTitleParserTests.cs` | 99 | ":14 — ported from Radarr's `ParserTests/ParserFixture`". Two rows still contain the literal string `RADARR` (:286-287) and one the title `Radarr.Under.Water…` (:254) | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Parsing/EditionParserTests.cs` | 48 | ":9 — ported from Radarr's `ParserTests/EditionParserFixture`" | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Parsing/TitleNormalizerTests.cs` | 40 | ":9 — ported from Radarr's `ParserTests/NormalizeTitleFixture`" | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Parsing/RejectedReleaseTests.cs` | 36 | ":7 — ported from Radarr's `ParserTests/CrapParserFixture`". Two rows carry `RADARR` (:139-140) | **(c)** |
| `src/Arronix.Host.Tests/Engines/ParseEngineRungResolutionTests.cs` | 25 | ":11 — the answers are Radarr's" | **(c)** |
| `src/Arronix.Host.Tests/Engines/ParseEngineTagScannerTests.cs` | 23 | ":12 — Radarr's `QualityParser` scan half" | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Quality/QualityLadderTests.cs` | 18 | ":23 — Radarr's twenty-nine rungs, by name. Listed rather than derived" | **(c)** |
| `src/Arronix.Plugin.Movies.Tests/Quality/UpgradeDecisionTests.cs` | 15 | Pins D-1's divergence from the surveyed comparer; the rule, not the data | **(b)** |

**And four further fixtures whose *expected values* are stated in the file to be the surveyed
application's** (S-C8 iii). These carry no ported row tables, so they add nothing to the (c) row counts;
what they carry is a justification that points at Radarr or Lidarr instead of at us, which is exactly the
(b) re-justification obligation in 3.6:

| File | Line | What it says | Class |
|---|---|---|---|
| `src/Arronix.Host.Tests/Engines/QualityEngineEvaluatorTests.cs` | :33-38 | "the declared weights make them one group, and the evaluator reproduces **Radarr's answer**" — the grouped WEBRip/WEBDL cutoff case | **(b)** |
| `src/Arronix.Host.Tests/Engines/MatchEngineLayeredLookupTests.cs` | :13-15, :67-68 | "the **Radarr-verified** layer ordering"; "Radarr rewrites the READING key into both numeral spellings (`FindByTitleCandidates`)" | **(b)** |
| `src/Arronix.Host.Tests/Engines/MatchEngineAssignmentTests.cs` | :139-140 | "the declared weights decide, exactly as **Lidarr's** externalized weight table does (`Distance.cs:11-32`)" — also one of the 46 `.cs:NN` citations Step 8a scrubs from `src/` | **(b)** |
| `src/Arronix.Plugin.Movies.Tests/Declaration/DeclarationSectionTests.cs` | :554-555 | "The rule **Radarr** actually has is about the host's own `{Original Title}` token…" | **(b)** |

Their disposition is not deletion. Each assertion is defended in our own words — *what behavior is being
pinned, why that behavior, what breaks under the alternative* — or the assertion is dropped. A test whose
stated reason for its expected value is "that is what the surveyed application answers" is evidence of
derivation even when the code under test is clean.

**And a second full copy of the ladder and the rung table, in the host's own tests.** This is the item no
prior summary lists:

`src/Arronix.Host.Tests/Engines/ParseEngineFixtures.cs` (402 lines) states at :26-30 that "the ladder and
the rung table … are deliberately the surveyed application's — Radarr's twenty-nine rungs with the grouped
WEBRip/WEBDL weights, and its `QualityParser.ParseQualityName` branch cascade as ordered rows". It carries
**31 ladder rows** (:72-108), **20 guard expressions**, **2 title patterns** including the verbatim
`title-then-year` expression, **2 token rows** (:207-208) and **77 rung rows** (:222 onward). Deleting the
three Movies files without this one leaves the material in the tree.

Fixtures checked and **clear**: `Support/ShapeFixtures.cs` (2-rung toy ladder), `TypedMedia/TypedKindFixture.cs`
(2 rungs), `Media/ShapeDefectCorpusTests.cs` (deliberately malformed toys),
`Movies.Tests/Fixtures/MoviesSeedData.cs` (1,959 lines — its own remark at :44-53 records that synopses are
written rather than taken from a catalog and artwork points at a reserved example host; the one *arr
mention, `MoviesSeed.cs:174`, is a behavioral comparison).

### 1.4 Documentation and repository metadata

| File | What it carries | Class |
|---|---|---|
| `docs/design/servarr-prior-art.md` | 451 lines, 117 mentions. An investigation record built from the GitHub API and local clones: repository inventories, dependency tables, byte-identical-file percentages. Facts about a public project, independently gathered. | **(a)** |
| `docs/design/naming-and-tokens.md` (105), `unified-host-runtime.md` (95), `parsing-and-test-corpus.md` (80), `file-management.md` (63), and 11 others | Behavioral descriptions with `file.cs:line` citations — **566 of them across 17 files**, not the handful the first sweep implied; see 4.6 for the measured split and the scoping decision. `parsing-and-test-corpus.md` quotes **two** actual expressions (:913, :1012); no other doc quotes source. | **(a)**, except the two quoted expressions **(c)** |
| `README.md:214-215,218-223`, `ARCHITECTURE.md:218,705-718` | The stale license and legacy-tree claims described in Part 0. `README.md:214-215` adds a third: "The legacy Sonarr application is unaffected and **still builds from `src/Sonarr.sln`**" — false on this branch, where `src/` holds only `Arronix.*` projects. Missed by the first sweep; added by the critique. | correction required |
| `LICENSE.md` | GNU GPL v3, 675 lines. | the flip target |
| `Directory.Packages.props:40-42` | The ImageSharp pin comment reasons from "an OSI-licensed project such as this GPLv3 one". A license change may invalidate that reasoning — see 4.4. | consequence |
| `src/Arronix.Plugins/Loading/PluginLoadContext.cs:53-54`, `src/Arronix.Architecture.Tests/Naming/LegacyNameTests.cs:26`, `Plugins.Tests/…:34-35` | The strings `"NzbDrone"` and `"Sonarr"` as **blocked assembly-name prefixes** and as a naming-hygiene assertion. Functional; they must **stay**. | **(a)** |

### 1.5 What the sweep cleared

Stated because a negative finding costs the plan work it would otherwise have to do:

- **`Arronix.Plugin.Tv`, `.Music`, `.Books` release parsers are clean.** 13, 6 and 7 expressions
  respectively, all short and structurally unlike the surveyed forms
  (`^(?<title>.+?)[\s._\-]+S(?<season>\d{1,4})…` versus Radarr's `(?:[-_\W](?<![)\[!]))+` idiom). No
  citation, no fingerprint. They were written for this tree.
- **`Arronix.Abstractions` carries no derived data.** `QualityTier` is a record; the ladders live in the
  kinds.
- **`Arronix.Common` was already produced by a clean-room exercise** — `docs/arronix-common-extraction-plan.md:5-10`
  names `NzbDrone.Common` as *source material* and states plainly: "This delivery is a **clean-room
  reimplementation**, not a copy-paste rename." Spot-check confirms the character: `Utf8StringWriter` is
  9 lines in `Sonarr.Common` and 56 in `Arronix.Common.Xml`, with a different encoding instance, an
  invariant-culture constructor pair and no shared line. That exercise is the precedent this plan
  generalizes — and Step 6b re-runs its gate against it rather than assuming it.
- **Kind ladders outside Movies** (`TvShape.cs:261-280` 20 rungs, `MusicShape.cs:601-614` 12,
  `BooksShape.cs:585-612` 8) resemble the corresponding *arr ladders in vocabulary but not in weights, ties
  or size bands — they carry none of the three; the constructors take `(name, rank, source, resolution[,
  format[, depth]])` and nothing else. **But the ranks are the surveyed applications' orderings**, and R-6's
  own reasoning applies to them: `MusicShape` ranks `MP3-VBR` above `MP3-320` and `BooksShape` ranks
  `PDF < MOBI < AZW3 < EPUB`, both of which are inherited orderings, and ordering is the one thing these
  ladders do carry (S-C8 ii). They are **(b) with a re-justification obligation on the order specifically**,
  not "clear". `quality-axes.md` supersedes them on their own conversion milestones, which is when the
  re-justification is due — and its §2.3 already argues that `PDF < MOBI < AZW3 < EPUB` is a device
  preference stated as a fact, so at least one of the three orders will not survive being defended.
- **Newznab category numbers** (`Movies.cs:114,119` — 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060; and
  the equivalents in the other three shapes) are an interop taxonomy published for interoperability.
  **(a)**; keep.

---

## Part 2 — Classification

### 2.1 The three classes, and the tie-break rule

| Class | Definition | Disposition |
|---|---|---|
| **(a)** Uncopyrightable functional fact | Scene vocabulary (`BluRay`, `x264`, `REPACK`), newznab category numbers, torznab semantics, ISO language codes, file extensions, identifier syntaxes, roman numerals, algorithm *behavior*. Facts and methods of operation. | **Keep.** Re-expression is waste. |
| **(b)** Thin or borderline | An ordering, a threshold, a curated list, a small table where the *selection* is someone's judgment. Individually weak; collectively they can amount to a compilation. | **Re-derive or re-justify.** Each row must be defended on its own merits in our own words, or dropped. |
| **(c)** Clearly expressive or copied | Regular expressions of non-trivial length, curated corpus rows with their expected values, comment prose, any artifact whose own comment says "verbatim" or "ported". | **Replace or delete.** No item leaves (c) by being edited. |

**The tie-break rule: uncertain ⇒ (c).** A wrong (c) call costs a rewrite. A wrong (a) call costs the
license claim. The asymmetry is total, so the rule is not "balance the risk" but "spend the effort."

### 2.2 Counts

**Re-verified after the critique's inventory findings (S-C8). Three of these numbers moved; the table below
is the corrected one and every figure is re-derived from the 1.x tables rather than carried forward.**

| Class | Expressions | Data rows | Test rows | Files affected |
|---|---:|---:|---:|---:|
| **(c)** | **88** | **503** | **704** | **19** |
| **(b)** | 6 | ~264 | 61 + 4 sites | 23 |
| **(a)** | — | ~140 | — | 12 |

(c) expressions — **unchanged at 88**: 42 in `MoviesParsing.cs` (1 edition + 11 patterns + 29 guards +
1 stop-word), 9 in `ReleaseTokenVocabulary.cs`, 8 in `ReleaseGroupScanner.cs`, 3 in
`ReleaseLanguageScanner.cs`, 2 in `NamingModifiers.cs`, 24 in `ParseEngineFixtures.cs`. `ReleaseTagScanner.cs`
adds none — it carries no expression of its own, which is why the regex-bearing sweep missed it.

(c) data rows — **495 → 503**: 249 corpus + 30 ladder + 108 Movies rung/fallback + 108 fixture ladder/rung
+ **8 resolution-bucket mappings** in `ReleaseTagScanner.ScanResolution` (7 numeric branches + the
`UHD`/`[4K]` fallback).

(c) test rows — **686 → 704**, an arithmetic gap rather than new material. The prior figure summed only
seven of the eight (c) fixtures in `Movies.Tests` (179 + 131 + 105 + 99 + 48 + 40 + 36 = 638) and silently
dropped `QualityLadderTests.cs`'s 18 rows. Corrected: 656 in `Movies.Tests` across 8 files, 48 in
`Host.Tests` across 2 files (25 + 23), total **704 across the 10 fixtures**, plus `ParseEngineFixtures.cs`
as the eleventh file (whose 108 rows are counted as data, above, not as test rows).

(c) files — **17 → 19**: 3 in `Arronix.Plugin.Movies` + **5** in `Arronix.Host/Engines` (`ReleaseTokenVocabulary`,
`ReleaseGroupScanner`, `ReleaseLanguageScanner`, `NamingModifiers`, **`ReleaseTagScanner`**) + 11 test
fixtures (the 10 in 1.3 plus `ParseEngineFixtures.cs`). The prior "6 in `Arronix.Host/Engines`, 8 test
fixtures" split did not reconcile with the 1.2 and 1.3 tables it summarized; this one does.

(b) files — **19 → 23**: the four assertion sites added to 1.3.

Counts are of *sites*, not of unique material: the fixture copies duplicate the plugin's. That is
deliberate — each site has to be dealt with separately, and counting unique material would understate the
work.

### 2.3 Contested calls, resolved

| # | Item | The case for (a) | The case for (c) | Call | Reason |
|---|---|---|---|---|---|
| **R-1** | `SourceTags()` — the 18-alternative source scan | It enumerates scene vocabulary, which is factual, and every implementation must recognize the same tokens. | 18 named groups, a specific alternation order, hand-tuned negative lookaheads (`BD(?!$)`, `(?:AMZN\|NF\|DP)[. -]WEB[. -](?!Rip)`) that encode *judgments about ambiguity*, not vocabulary. Its own comment says "verbatim". | **(c)** | The tokens are facts; this arrangement of them is not. Merger fails at this length. |
| **R-2** | `br-disk` guard (nested lookaheads, ~700 chars) | It detects a factual property — whole-disc structure. | There are many ways to express it and this is one person's. | **(c)** | Unambiguous. |
| **R-3** | `\b(?<proper>proper)\b` and the other short markers | One reasonable expression exists. | It was copied. | **(a)** | Merger. Independent authorship converges here and a gate flagging it would flag noise. Threshold set in 3.2. |
| **R-4** | The 249 corpus rows | Release names are facts about the world; some are public strings. | Selection, arrangement, expected values and the anonymizing edit are all authorship. | **(c)** | Compilation. And the file's own remark documents the copying. |
| **R-5** | 29-rung ladder with weights, tie-groups, size bands | Rung *names* are scene vocabulary. | Rank, weight, the WEBDL≈WEBRip tie and 29 MB-per-minute rows are a designed model. | **(c)** | Superseded by `quality-axes.md` regardless; classification only decides whether a copy may be kept for reference. It may not. |
| **R-6** | 101 rung-resolution rows | Each row states an inference any implementer would make. | The *cascade order* is the copied artifact — the file says so — and order is the semantics. | **(c)** | The rows are individually thin and jointly the copied thing. |
| **R-7** | Curated release-group name lists (75 names) | Group names are facts; the list exists because those groups exist. | Which 75 got listed is maintenance labor and editorial selection. | **(b)** | Re-derive from observed titles in our own field corpus. If a name reappears, it reappears because we saw it. |
| **R-8** | 50-row language table | Language names and ISO codes are facts. | Which 50 and the `truefrench→fr` / `latino→es-419` mappings are choices. | **(b)** | Regenerate from ISO 639 plus our own observed tokens; keep only mappings we can justify. |
| **R-9** | 49-entry container-extension set | It is the set of media containers. | Someone assembled it. | **(a)** | Facts, and the platform must recognize the same set to function. |
| **R-10** | Doc citations `QualityParser.cs:17-37` | Citing a public work is not copying it. | It is documentary evidence of derivation. | **(a)**, scrub anyway | Not a copyright problem; a *provenance* problem. A citation next to a rewritten expression invites the conclusion the rewrite was not clean. Part 4 Step 8a removes them from `src/`; 4.6 sizes the `docs/` question and scopes it out. |
| **R-11** | 90-day theatrical window, 10s/30s distance grace | Thresholds are facts about the domain. | They are calibrations someone chose. | **(b)** | Keep only with our own justification written; where we cannot justify one, make it configurable with our own default. |
| **R-12** | `MunkresSolver` | — | — | **third-party** | See 2.4. |

### 2.4 The one item that is not a GPL question

`MunkresSolver.cs` is a port of Lidarr's `Munkres.cs`, whose header in the source records it as MIT-licensed
work of **Robert A. Pilgrim, Murray State University**. Three consequences the GPL flip does not address:

1. MIT is compatible with any license the owner is likely to choose, so it does **not** block the flip.
2. MIT requires the copyright notice and permission text be retained. The current file cites the origin in
   a doc-comment but the tree carries **no `NOTICE` file**. That is an obligation the project is presently
   not meeting, and it is independent of the GPL question.
3. The path of least ambiguity is a `NOTICE` naming the original author and license (Step 9). Reimplementing
   the Hungarian algorithm from a textbook is also viable — it is a published algorithm — but it buys
   nothing here and costs a correctness risk on 360 lines of index-heavy code.

**Deferred, honestly:** whether Lidarr's own port added copyrightable expression on top of Pilgrim's. Our
port went through theirs. The conservative reading is that a `NOTICE` should credit both the algorithm's
author and the fact that the port followed an existing C# implementation.

---

## Part 3 — The replacement method

### 3.1 The clean-room discipline, including the part that is hard because the workers are AI agents

**The classical arrangement.** Two teams. The *dirty* team may read the original and writes a functional
specification. The *clean* team never sees the original and implements only from the spec. A third party
reviews the spec on the way across and strips anything that is expression rather than function. The
artifact that survives is the record: who read what, when, and what crossed the wall.

**Why a "fresh agent" is not automatically a clean team, and saying otherwise would be false.** Radarr,
Sonarr and Lidarr are public repositories. A language model may have seen `QualityParser.cs` in
pretraining. A new session clears *context*, not weights. So the honest statement is:

> Fresh context removes the ability to consult the original. It does not remove the possibility of
> reproducing it from memory. The discipline therefore cannot rest on the implementer's claimed ignorance;
> it has to rest on **input control the implementer cannot circumvent** plus **output testing that does not
> care what the implementer knew**.

That reframing is what makes the rest of this section constructive rather than ceremonial.

**Roles and read permissions.** Each is a separate agent session with a separate brief. No session holds
two roles.

| Role | May read | May **not** read | Produces |
|---|---|---|---|
| **Surveyor** (dirty) | The quarantined files (`MoviesParsing.cs`, `ReleaseTokenVocabulary.cs`, …), the corpus, the docs | — | `docs/design/release-token-grammar.md` — the functional spec |
| **Spec Editor** (wall) | The spec only | The quarantined files | The sanitized spec, plus a written refusal list of what was stripped |
| **Implementer** (clean) | The sanitized spec, `Arronix.Abstractions`, the new corpus, the **cross-product cell specification** (3.3) | The quarantined files, the ported fixtures, the old docs' cited line numbers, `git show` of any quarantined path, **the derived `EvidenceCell` baseline**, **`gate-results.md`**, **`differential-report.md`** | The new lexer, grammar and axes evidence reader |
| **Gatekeeper** (machine) | Everything, including the quarantined files and the derived baseline | — | **To the Implementer: a per-file boolean and, on failure, a spec-paragraph id — nothing else.** To the record: the full verdict, location and similarity. **Never quotes the original in either.** |

**The read restrictions on the Implementer are the control, so they are enumerated rather than implied.**
Three of the seven now-listed exclusions are additions the critique forced: the `EvidenceCell` baseline is
*derived from* the quarantined corpus and inherits its status (S-C4, and F-C3 asked who may read it — the
answer is the Gatekeeper and the Surveyor, not the Implementer); `gate-results.md` carries similarity
values, which are a gradient (S-C2); `differential-report.md` carries per-case divergences, which are an
oracle (S-C1). Each of the three would otherwise leak the quarantined material back across the wall in
numeric form.

**Six controls, in the order they carry weight.**

1. **Input control, enforced by removal.** Before the Implementer starts, the quarantined files are moved
   to a location outside the repository and outside the agent's working directory, and
   `/Users/adam/Code/GitHub/Arronix/NzbDrone.Common` moves with them. The Implementer cannot read what is
   not there. This is stronger than an instruction not to look, and it is the control the whole scheme
   rests on.
2. **Spec sanitization — syntactic *and* semantic.** The syntactic half is enforced mechanically: the
   sanitized spec must contain **zero regular-expression metacharacters**, **zero named capture groups**,
   and **zero verbatim alternation lists**. It states vocabulary as prose word lists, ambiguities as
   sentences ("`BD` at end of string is not a source marker"), and ordering only where ordering is
   behaviorally required. A grep gate over the spec file enforces the first two; the Spec Editor's refusal
   list documents the third.

   **That half is not sufficient, and the plan previously stopped there** (S-C3). A grep for
   metacharacters detects *expression*. It does not detect **arrangement** — and arrangement is precisely
   what R-1 and R-6 identify as the copied thing ("hand-tuned negative lookaheads that encode judgments
   about ambiguity"; "the cascade order is the copied artifact — the file says so — and order is the
   semantics"). A word list plus an ordered list of ambiguity cases with their resolutions *is* the
   arrangement, rewritten in English. The example paragraph in 3.2 carries three of Radarr's judgments
   verbatim in prose, and passes the grep gate cleanly.

   **The semantic mechanism is independent re-specification from behavior, not redaction of spellings.**
   Every ordering constraint and every ambiguity case in the spec must carry a **functional justification
   that stands without reference to the original**: an observable reason, grounded in cases from *our* field
   corpus, for why the rule has to be that way. The register is the difference between

   > *"`WEB` alone counts only beside a codec or resolution token, because `WEB` alone also appears in work
   > titles — field cases 12, 88, 341."*

   and

   > *"`WEB` alone counts only beside a codec or resolution token."* (justification: that is what the
   > surveyed application does)

   The first is a functional requirement; the second is a transcribed judgment. This makes the Spec
   Editor's job **decidable rather than aesthetic**: strike every constraint whose only support is "that is
   what it does", and every ordering whose support is "that is the order it is in". The struck items go to
   `spec-refusals.md` with the reason, and a rule struck here is not lost — it re-enters only if the
   Implementer's own field cases independently demand it, at which point it is ours.

   The cost is stated rather than glossed: some real behaviors will fail to find a justification and will be
   dropped, and the parse will be worse in those cases until field evidence re-derives them. That is the
   same regression 3.3 records for the corpus, arriving through the other door.
3. **Structural non-convergence, required by design.** The Implementer is instructed to produce a
   *different shape*: a token-class lexer plus a small ordered rule set (see 3.2), not one alternation per
   token class. This is also what P2-5 asks for on engineering grounds — "bound and structure it" — so the
   legal and engineering goals point the same way. A structurally different artifact is far better evidence
   of independent creation than a shorter regex that happens to differ.
4. **Output testing that does not care what the implementer knew** — the fingerprint gate in 4.3. This is
   the control that actually bounds the risk, because it tests the artifact rather than the process.
5. **Behavioral validation against a black box — *budgeted*.** The old implementation may be used as an
   *oracle* — run it, compare answers — because behavior is not the thing being protected. It is run
   through its public seam (`IReleaseParser.Parse`) against the **new** corpus, and its source is never
   shown to the Implementer.

   **But an unbounded oracle is a reconstruction channel, and it partly undoes control 1** (S-C1). Control 1
   is the control the whole scheme rests on: the original is physically absent. An oracle that answers an
   unlimited number of queries, with per-case expected values, and a ≥99%/≥97% agreement gate, is an
   instruction to *converge on the original's behavior by iteration* — and for the specific artifacts this
   plan itself classifies (c), the disputed material **is** behavior. "Behavior is not protected" is true of
   functional facts (`720p` means 720 lines) and false of R-1's judgments about ambiguity and R-6's cascade
   order. Query the black box enough times with enough granularity and you have differentially reconstructed
   the thing that was removed. This is the classic way a clean room is lost in practice, and it is lost
   without anyone reading a line of source.

   Three bounds, all recorded in `differential-report.md`:

   **(a) A hard query budget: twelve whole-corpus oracle runs for the entire programme.** Not twelve per
   case, per file or per step — twelve, total, across Steps 6a and any re-run the triage provokes. Each run
   submits the whole corpus as one batch and returns one aggregate report. Each run is timestamped and
   attributed to the role that requested it. **The thirteenth run requires a written owner exemption
   recorded in the provenance file**; exhausting the budget is a hard stop that routes the remaining work
   back through the spec, which is where the wall is. Twelve is sized to be generous for its legitimate
   purpose and useless for its illegitimate one: a baseline run plus roughly one confirmation run per spec
   revision cycle needs single digits, while iterating a lexer to convergence against an oracle needs
   hundreds. If twelve feels tight, the correct response is a spec fix, not a fourteenth run.

   **(b) Aggregate-only feedback to the Implementer.** What crosses back is a pass rate and a count of
   divergences **by token class** — never a case id, never an expected value, never a per-case diff.
   Per-case triage is the Gatekeeper's alone, and the Gatekeeper's output to the Implementer is a
   spec-paragraph id ("rule S-14 is under-specified"). A divergence therefore always routes the fix through
   the *specification*, and never through "make case q037 come out like this".

   **(c) No oracle run against any case derived from the ported corpus.** The oracle runs against
   synthesized and field cases only. Running the old implementation over cases reduced from its own test
   corpus measures nothing except how faithfully the reduction preserved it, and it re-couples the two
   artifacts the plan is separating.
6. **A contemporaneous provenance record**, Part 6. Written as the work happens. A record reconstructed
   afterwards is worth much less and everyone involved knows it.

**What this buys, and what it does not.** It buys: no access to the original at implementation time, an
artifact structurally unlike the original, a mechanical check that no expression survived textually, and a
record, and — after the amendments — a bounded, logged count of every query the clean side made of the
dirty side. It does not buy: proof that no memorized fragment influenced a choice. Nothing available buys
that.
The residual risk is concentrated in short expressions where convergence is expected and where merger makes
convergence harmless — which is why 3.2 sets the gate threshold by length and exempts the (a)-classified
expressions by name rather than pretending a short match is never a match.

### 3.2 Regexes — a spec-based rewrite

**The spec.** The Surveyor produces `docs/design/release-token-grammar.md` containing, for each token class:
the vocabulary as a word list; the boundary rules in prose; every known ambiguity as a named case with the
required resolution and *why*; and the evidence each class contributes. It contains no expressions. Example
of the required register:

> **Source class, `web-download`.** Spelled `WEB-DL`, `WEB DL`, `WEBDL`, or a vendor form (`AmazonHD`,
> `iTunesHD`, `NetflixHD`). A bare `WEB` counts only when adjacent to a codec token or a resolution token,
> because `WEB` alone also appears in work titles. `WEB` immediately followed by `Rip` is a *different*
> class and must not be claimed here.

**The target shape.** Not a set of replacement alternations — a lexer over token classes, with the
ambiguity rules as explicit, individually testable predicates. Proposed surface, in the tree's conventions:

```csharp
namespace Arronix.Host.Engines.Parsing;

/// <summary>One functional category of release-title vocabulary.</summary>
public enum ReleaseTokenClass
{
    Source, Resolution, VideoCodec, AudioCodec, Revision, Remux,
    Container, Language, Edition, GroupMarker, Checksum, Unknown
}

/// <summary>One recognized token and where it sat in the title.</summary>
public readonly record struct ReleaseToken(
    ReleaseTokenClass Class, string Value, int Start, int Length);

/// <summary>Splits a release title into classified tokens. One pass, no backtracking.</summary>
internal interface IReleaseLexer
{
    IReadOnlyList<ReleaseToken> Tokenize(ReadOnlySpan<char> title);
}

/// <summary>One ambiguity rule from the grammar spec, individually testable.</summary>
/// <param name="RuleId">The spec's own identifier for this rule, so a failure names a paragraph.</param>
internal readonly record struct TokenDisambiguationRule(
    string RuleId,
    ReleaseTokenClass Class,
    Func<ReadOnlyMemory<char>, int, bool> Accepts);
```

Regular expressions are not banned — a short anchored expression is often the clearest way to state one
rule — but no expression may exceed **40 characters** or serve more than one token class. That single
constraint makes a mega-alternation structurally impossible and puts every expression below the length
where the fingerprint gate has to make a judgment call.

**The gate threshold, and why it is a length.**

| Expression length | Gate behavior | Reason |
|---|---|---|
| ≤ 40 chars **and named on the merger-exemption list** | Exempt from similarity comparison **and from exact-match**, by name | Merger. `\b(?<proper>proper)\b` has essentially one form and independent authorship converges on it |
| ≤ 40 chars, not on the list | Exempt from similarity comparison; exact-match against a quarantined expression fails | Short does not mean inevitable; the exemption is earned per expression, not by length alone |
| 41-120 chars | Similarity ≥ 0.60 fails and requires a Gatekeeper-written justification or a rewrite | Convergence is possible but should be explicable |
| > 120 chars | Refused by the 40-character design rule before the gate ever sees it | The design forbids it |

**Why the merger exemption is now a named list rather than a blanket length rule** (S-C7). The previous
table said "≤ 40 chars — exempt from similarity comparison; **exact-match still fails**", which
contradicted R-3 directly: R-3 classifies `\b(?<proper>proper)\b` (21 chars) as **(a) keep**, on merger,
with the observation that "a gate flagging it would flag noise." Both rules cannot hold. Any independent
implementer writes exactly `\bproper\b`; forcing a different spelling only to satisfy the gate produces
gratuitous divergence — which is *worse* evidence of independence, because it reads as evasion — and adds a
real regression risk on a marker that must be case-insensitive and word-bounded.

The resolution is the classification, not the length. Every expression classified **(a)** in Part 2 is
exempt from the exact-match rule **by name**, with its classification as the recorded justification, and
the exemption list is published in `gate-results.md` so that the exemption is auditable rather than silent.
An expression that is short but *not* (a)-classified gets no exemption: it is short because someone chose
to write it that way, and a match is then still a match. Adding a name to the exemption list is a
classification decision under Part 2's tie-break rule (uncertain ⇒ (c), i.e. no exemption), made by the
Gatekeeper and recorded, not a convenience the Implementer can invoke.

### 3.3 Corpus — synthesis plus field gathering, with fidelity measured

**Generate, do not copy.** The token grammar is a generative grammar: given the vocabulary and the shape
templates, a synthesizer emits titles with their expected evidence already known, because it built them.

```csharp
namespace Arronix.Corpus.Generation;

/// <summary>
/// The evidence a synthesized title is built to carry — its cell in the coverage space.
/// Stated in the axes vocabulary, not the ladder's: see the note below on D-7 and S-C10.
/// </summary>
public readonly record struct EvidenceCell(
    Origin Origin,                          // Remux is a member here, not a flag — D-7
    int? Generation,
    int? ResolutionHeight,
    Packaging Packaging,
    int? Corrections,
    string? VideoCodec,
    char Separator,
    string ShapeId,
    IReadOnlyList<string> MustNotClaim);    // the negative half — S-C5

/// <summary>A generated case: the title, what it must be read as, and what it must not be read as.</summary>
public readonly record struct SynthesizedCase(
    string CaseId, string Title, EvidenceCell Cell);

/// <summary>Emits titles from the grammar. Deterministic in the seed, so a run is reproducible.</summary>
public interface ITitleSynthesizer
{
    IEnumerable<SynthesizedCase> Generate(int seed, IReadOnlyList<EvidenceCell> cells);
}
```

**The cell is stated in the axes vocabulary from the start, not translated into it later** (S-C10). The
first draft of this record carried `SourceClass`, `IsRemux` and `Revision` — the *ladder's* vocabulary,
reducing corpus rows whose expected values are rung names (`"Remux-1080p"`, `"WEBDL-480p"`). Those names
are exactly what `quality-axes.md` deletes. Since the reduction sat at position 2 on the critical path and Step 5
(mapping token classes onto the axes) sits at position 5, that draft froze the fidelity baseline in a
vocabulary the project abandons three steps later, and left the translation between them undefined.

Two things make the corrected shape available now rather than blocked on the sibling design. **D-7** is
resolved: the untouched-disc case is its own `Origin` member, so `Origin` is a single closed axis carrying
the disc distinction and `IsRemux` has no separate existence to translate. **D-8** is resolved: the primary
axes are a fixed lexicographic core, so the cell's field list is the core, and the facet-scoring tier below
it is a *policy* concern that a coverage cell never has to represent. The cell's fields are therefore
defined to be exactly the primary axes named in `quality-axes.md` §2.1 as amended under D-7 — if that set
moves, this record moves with it, and the two documents keep needing **one** vocabulary rather than two
plus a translation. This is also strictly better engineering: the same cells become the direct baseline for
that document's QA-11 parity harness.

**Field corpus.** Synthesis covers the grammar; it does not cover the world's inventiveness. The second
source is real titles gathered from **non-*arr** origins: public indexer API responses the project queries
itself, tracker RSS feeds, torrent and usenet index pages, and — the durable one — titles contributed by
users when a parse fails, under the project's own contribution terms. A gathered title is a fact; the
gathering is ours. Provenance is recorded per row.

```csharp
/// <summary>Where a corpus row came from, recorded per row so provenance survives review.</summary>
public enum CorpusOrigin { Synthesized, FieldObserved, UserContributed }
```

**The negative half of the corpus, which the first draft dropped entirely** (S-C5). The cell as originally
written had no way to say *"this title must **not** be read as X"* — and that is what a large part of the
shipped corpus actually asserts:

| Case | The assertion |
|---|---|
| q166 `Some.Movie.S02E15`, q167, q168 `…The.Web.MT-dd` | **`Unknown`** — no source may be claimed at all |
| t029 `The.Good.German.2006.720p.BluRay.x264-RlsGrp` | `German` here is a **title word**, not a language marker |
| t016 `Der.Movie.James.German.Bluray…1998` | `German` here **is** a marker — the pair is the test, not either row |
| t002 `1776.1979.EXTENDED…` | `1776` is a **title**, not a year |
| t008 `(500).Days.Of.Movie.(2009)…` | a leading parenthetical is title, not year |
| t010 `A.I.Artificial.Movie.(2001)` | `A.` is an acronym part, not the article |
| t012/t013 `www.Torrenting.com - Movie…` | the tracker prefix is not the title |

A generator that emits only well-formed positives, plus a coverage metric that cannot see a negative, plus
a differential gate that measures agreement on positives, together **produce and reward a lexer that
over-claims**: it can score 100% on cells while failing every row above. Over-claiming is also the worse
failure mode in production — a false source claim promotes junk into the library, while a missed claim
merely leaves an item `Unknown` and visible.

So `MustNotClaim` is part of the cell, the negative taxonomy is derived from the old rows **mechanically**
as part of Step 1b (the same reduction pass, in the same role, under the same read restrictions), and its
acceptance is **100%, a hard gate — not 97%**. A negative case has no partial credit: either the parser
declines to claim, or it does not.

**Fidelity, measured rather than assumed.** Before the old corpus is deleted, each of its 249 rows is
reduced — by the Surveyor, mechanically — to its `EvidenceCell`, positive and negative halves together. The
cells are kept; the rows are not.

**The cells are derived material and are recorded as such** (S-C4). It is tempting to say the cells "carry
no expression" and stop there, and the first draft did. That is true of expression and beside the point:
R-4 classifies the corpus (c) as a **compilation**, on the grounds that *selection* and *arrangement* are
the authorship. A set of cells reduced from that corpus inherits the selection. Three consequences, all of
which the plan now takes:

1. The cells are entered in `quarantine-manifest.md` as **derived**, with their derivation recorded — the
   source rows, the reduction rule, the role that ran it. They are not a fresh artifact and they are not
   described as one.
2. **They are not readable by the Implementer** (see the role table above, and F-C3). What the Implementer
   receives is the *cross-product specification* — the axis value ranges and the shape templates — which is
   generative and un-derived. The cells stay with the Gatekeeper as a measuring instrument.
3. **The acceptance changes from "cover the baseline" to "cover the cross-product."** Aiming at the
   baseline is aiming at the old compilation's selection. Walking the cross-product makes the baseline a
   floor that falls out as a by-product of covering a larger, independently-defined space — the same
   `Missing`-empty result, reached without the old selection ever being the target.

```csharp
/// <summary>Compares two corpora by the evidence cells they exercise.</summary>
public static class CorpusCoverage
{
    public static CoverageReport Compare(
        IReadOnlyList<EvidenceCell> baseline, IReadOnlyList<EvidenceCell> candidate);
}

/// <param name="Missing">Baseline cells the candidate does not reach. Must be empty to pass.</param>
/// <param name="NegativesMissed">Baseline negative assertions the candidate does not exercise. Must be empty.</param>
public sealed record CoverageReport(
    int BaselineCells,
    int CoveredCells,
    int NewCells,
    IReadOnlyList<EvidenceCell> Missing,
    IReadOnlyList<string> NegativesMissed);
```

**Acceptance, in three parts.** `Missing` empty and `NewCells > 0`, as before — but reached by walking the
cross-product rather than by targeting the baseline, so the baseline is a floor and not an objective.
`NegativesMissed` empty, at 100%, per S-C5. And a **second, independent acceptance on the field corpus**,
which is un-derived by construction: the field set is where "did we build a parser or did we rebuild
*theirs*" is actually answerable, because nothing about it came from the old compilation. The first two
gates measure the generator against a space we defined; only the third measures the parser against input
nobody arranged.

**What is genuinely lost, stated rather than glossed.** `MoviesCorpus.cs:14-16` is right that "every case
below is a release name that broke something once." Cell coverage does not reproduce that. A generated
title is well-formed by construction; the value of the ported rows was that they were *malformed in ways
nobody would think to generate* — `Movie.Name.1993..BD25.ISO` with its double dot,
`[Coalgirls]_Movie!!_01_(1920x1080_Blu-ray_FLAC)_[8370CB8F].mkv`, `The German 2021 Bluray AVC` where the
language word is the title. Three mitigations, none of them a full replacement:

1. **Adversarial generation** — the synthesizer emits deliberate malformations (doubled separators, missing
   separators, tokens inside the title span, mixed separators in one title) as their own shape templates.
2. **The field corpus**, which is the real answer and accrues over time rather than on the milestone.
3. **A regression rule**: every parse defect reported after the flip is added as a case before it is fixed.

Honest accounting: at flip time the corpus will have **broader** systematic coverage and **narrower**
adversarial coverage than the ported one. That is a real, temporary regression in parse robustness and it
should be stated in the release notes, not discovered by users.

### 3.4 Ladder data — superseded, not rewritten

`MoviesLadder.cs`'s 30 rungs, `MoviesParsing.cs`'s 101 rung-resolution rows and their two copies in
`ParseEngineFixtures.cs` are **not** re-derived. They are deleted, and the quality model that replaces them
is [`quality-axes.md`](./quality-axes.md) — evidence along orthogonal typed axes, with ordering, cutoff and
interchangeability moved into user-owned policy, and size plausibility computed as a bitrate expectation
rather than stored as 29 MB-per-minute rows.

Three dependencies this plan places on that document, recorded here so none is assumed:

- **The evidence axes must be readable from the token classes in 3.2.** If the axes design needs evidence
  the lexer does not produce, that is a spec change, and the spec is written before the lexer.
- **The same sentence applies to the corpus cells**, which the first draft did not say (S-C10). The
  `EvidenceCell` in 3.3 is stated in the axes vocabulary, so if that vocabulary moves, the cell moves with
  it and the baseline is re-derived — it is never translated after the fact.
- **The size-band rows are replaced by a computation, not by 29 new numbers.** A computed bitrate
  expectation over (resolution x codec x generation) is our model; a hand-written table of the same 29
  values would be the same data with new authorship claimed, which is the outcome to avoid.

**What that document's resolved decisions settle for this plan.** **D-7** — the untouched-disc case
becomes its own `Origin` member rather than a `Generation` step — is the one this plan consumes directly:
it fixes the cell's field list, and it means the reduction of the 249 rows never has to carry an `IsRemux`
flag that the axes model would then have to re-absorb. **D-8** — a strictly lexicographic core of primary
axes with an optional bounded facet-scoring tier consulted only on core ties — settles that the coverage
space is the *core* axes and nothing else: facet scores are policy, not evidence, so no cell, no coverage
metric and no differential comparison in this plan has to represent them. Both are recorded as RESOLVED in
`docs/open-decisions.md` Part 7 and are cited by name here rather than quoted, because the sibling document
is being amended in parallel.

### 3.5 Items replaced by deletion

Not everything needs a successor. These go and nothing takes their place:

| Item | Why deletion suffices |
|---|---|
| `ParseEngineFixtures.cs` ladder, guards, rung table (108 rows + 24 expressions) | Host-engine tests should run against a *synthetic* kind, not a copy of a real one. Replacing them with a toy family is less code and a better test. |
| The 10 ported test fixtures (704 rows) | Their successors are the generated corpus plus per-rule unit tests keyed to spec paragraph ids — which is strictly better failure reporting than a row that says only "this title, that rung". |
| The 7 `_reference/…` doc-comment citations **in `src/`** (7 files, all `Engines/Matching`) | The behavior they justify is stated in the sentence next to them; the path adds nothing but provenance exposure. This is the cheap half; the `docs/` half is 62 further hits and is **not** the same work item — see 4.6. |
| `MoviesCorpus.cs` in its entirety | Superseded by the generated corpus. |

### 3.6 (b)-class items — the re-justification pass

(b) items are not rewritten; each is defended or dropped. The output is one table row per item, in the file
next to the item, in our own words, answering: *what is this value, why this value, and what breaks at
other values.* An item whose defense cannot be written is dropped or made configurable with a default we
can defend. Applies to R-7, R-8, R-11, the cascade orderings, the codec precedence tables, and the
`NameSubstitutions` default table.

---

## Part 4 — Sequencing, gates, and the license flip

### 4.1 The ordered steps

| # | Step | Owner role | Depends on | Parallel with |
|---|---|---|---|---|
| **0** | **Quarantine.** Move the **19** (c) files to an out-of-tree quarantine directory; move `NzbDrone.Common` out of the workspace; record SHA-256 of each quarantined file and of the tree. | Gatekeeper | — | — |
| **1a** | **Survey → spec.** Write `release-token-grammar.md`, every ordering constraint and ambiguity case carrying its own functional justification (control 2). | Surveyor | 0 | 2 |
| **1b** | **Baseline reduction.** Reduce the 249 corpus rows to `EvidenceCell` baselines **in the axes vocabulary**, positive and negative halves together. | Surveyor | 0, **2** | 1a |
| **2** | **Quality-axes design.** `quality-axes.md` lands, amended; **D-7 and D-8 are the parts this plan depends on** and both are resolved. | (sibling) | — | 1a |
| **3** | **Spec sanitization, syntactic and semantic.** Strip expression; strike every constraint whose only justification is the original's behavior; publish the refusal list. | Spec Editor | 1a | — |
| **4a** | **Lexer and rules.** Implement `IReleaseLexer` and the disambiguation rules from the sanitized spec. | Implementer | 3 | 4b |
| **4b** | **Corpus generator + field gathering.** Implement `ITitleSynthesizer` over the cross-product specification; stand up the field-corpus intake. | Implementer | 3 | 4a |
| **5** | **Axes evidence reader.** Map token classes onto the axes; delete the ladder and the rung table. | Implementer | 2, 4a | — |
| **6a** | **Differential validation.** Old parser as oracle over the new corpus, **within the twelve-run budget**; triage every divergence; return aggregates only. | Gatekeeper | 1b, 4b, 5 | 6b |
| **6b** | **Common re-verification.** Run the fingerprint gate over `Arronix.Common` against `NzbDrone.Common`, verifying rather than assuming the earlier clean-room exercise. | Gatekeeper | 0 | 6a |
| **7** | **Removal.** Delete the quarantined files and the ported fixtures from the tree; the fingerprint gate goes green and becomes a standing architecture test. | Implementer | 6a | 8a |
| **8a** | **`src/` provenance scrub.** Remove the 7 `_reference/…` citations and the 46 `.cs:NN` citations from `src/`; correct `README.md:214-215,218-223` and `ARCHITECTURE.md:218,705-718`; remove the two quoted expressions from `parsing-and-test-corpus.md`. **53 sites plus 3 documents; hard gate.** | Surveyor | 3 | 7 |
| **8b** | **`docs/` citation decision and, if taken, the scrub.** 62 `_reference/` hits and 454 surveyed-source `.cs:NN` citations across 17 documents. **Owner decision first (4.6); not a gate on the flip.** | Owner, then Surveyor | 8a | 9, 10 |
| **9** | **NOTICE and dependency re-check.** Add `NOTICE` for Munkres; re-check every package license against the chosen license (4.4). | Gatekeeper | 7 | — |
| **10** | **The flip.** Replace `LICENSE.md`; rewrite the README license section; decide the branch question (4.5). | Owner | 7, 8a, 9 | — |

Critical path: 0 → 1a → 3 → 4a → 5 → 6a → 7 → 10. Steps 2, 1b, 4b, 6b and 8a run beside it; **8b runs
beside or after the flip**, because it is a provenance-hygiene preference rather than a C-1 requirement
(4.6). Two dependency changes the amendments force: 1b now depends on Step 2, because the cells are stated
in the axes vocabulary (S-C10); and 6a now depends on 1b, because the negative baseline is part of what it
validates.

### 4.2 The gate on each step

| Step | Gate | Passes when |
|---|---|---|
| 0 | Quarantine complete | `rg -l "QualityParser\|FileNameBuilder\|MovieService" src/` returns only files scheduled for rewrite; the sibling clone is not under any agent's working directory; **all 19 (c) files listed in the manifest** |
| 1a | Spec is justified | Every ordering constraint and every ambiguity case carries a functional justification that does not reference the original |
| 1b | Baseline extracted | 249 rows reduced to cells **in the axes vocabulary**; the cell set contains no title strings; the negative taxonomy is non-empty and recorded as derived |
| 3 | Spec is clean | Zero regex metacharacters and zero named groups in the spec file; refusal list non-empty; **zero constraints remaining whose only support is "that is what it does"** |
| 4a | Design rule holds | No expression in new parsing code exceeds 40 characters; every disambiguation rule cites a spec paragraph id |
| 4b | Coverage | `CoverageReport.Missing` empty; `NewCells > 0`; **`NegativesMissed` empty (100%, hard)**; the field-corpus acceptance passes independently |
| 5 | Ladder gone | No `QualityTier` weight, tie-group or MB-per-minute value survives outside `quality-axes` types |
| 6a | Behavior | ≥ **99%** agreement on the synthesized corpus, ≥ **97%** on the field corpus, **100% on negatives**; every divergence triaged as *new correct* / *old correct* / *both defensible*, with counts recorded; **oracle runs ≤ 12 and every run logged with its timestamp and requester**; no run against a case derived from the ported corpus |
| 6b | Common clean | Fingerprint gate green against `NzbDrone.Common` |
| 7 | Textual derivation | The two tests in 4.3 green; **the (a)-exemption list in `gate-results.md` matches Part 2's classification exactly** |
| 8a | Provenance hygiene, `src/` | Zero `_reference/` paths and zero `\.cs:\d+` citations of surveyed sources in **`src/`**; the three false README/ARCHITECTURE claims corrected |
| 8b | Provenance hygiene, `docs/` | **Whichever of 4.6's two options the owner takes, recorded with its reason.** If (ii): zero `_reference/` paths and zero surveyed-source `\.cs:\d+` citations in `docs/` |
| 9 | Attribution | `NOTICE` exists and names every retained third-party work; no dependency's license conflicts with the chosen license |
| 10 | The flip | `LICENSE.md` replaced; README and ARCHITECTURE claims match the tree |

### 4.3 The standing gate, in `Arronix.Architecture.Tests`

The gate reads the quarantined originals — a machine comparing is not a human copying — and it **never**
prints the original expression, so a failure report is not itself a copy.

**Its output to the Implementer is a boolean, and nothing else** (S-C2). The first draft had the gate
produce "pass/fail plus a location", and 4.3's own summary sentence exposed a similarity value. That value
is a **gradient**, and a gradient over quarantined text is an oracle in the sense of S-C1 — with more force,
because the target is the expression itself rather than the behavior: mutate the expression, re-run the
gate, read the score, converge. Hill-climbing a similarity metric toward 0.59 produces an expression that
is maximally close to the original while passing, which is the exact opposite of what the gate exists to
establish.

So the interface is split:

| Recipient | Receives | Does not receive |
|---|---|---|
| **Implementer** | A per-file boolean, and on failure the **spec-paragraph id** the file implements | The similarity value, the location within the file, the number of failing expressions, `gate-results.md` |
| **`gate-results.md`** (record) | Per-file verdict, location, similarity value, the (a)-exemption list, **and every invocation with its timestamp and requester** | — |
| **Gatekeeper** | Everything | — |

The invocation log is not bookkeeping; it is a detector. **A burst of forty runs against one file is itself
the finding**, and it is visible in the record whether or not any individual run failed. The pattern of
querying is evidence about the process in a way no single verdict is.

**One part of the amendment is deliberately not taken, and the reason matters.** The critique asked for
"a file-level boolean **and nothing else**." A bare boolean is taken; "nothing else" is not. An Implementer
told only *fail* has no route to a fix except to change something and re-run — which is precisely the
hill-climbing loop the amendment exists to prevent, arrived at by removing every alternative. Pairing the
boolean with the **spec-paragraph id** the failing file implements costs nothing (the id is ours, written
on our side of the wall, and carries no information about the original) and routes the fix back through the
specification, which is the same destination S-C1 chose for oracle divergences. The two amendments then
agree: **every signal that crosses back to the clean side is a pointer into our own spec, never a
measurement against theirs.**

```csharp
namespace Arronix.Architecture.Tests.Provenance;

/// <summary>
/// Normalizes an expression to its structural fingerprint: capture-group names removed, whitespace and
/// inline flags collapsed, literals lowercased. Two expressions with the same fingerprint are the same
/// expression wearing different names.
/// </summary>
internal static class ExpressionFingerprint
{
    internal static string Of(string expression);

    /// <summary>Token-level similarity in [0, 1] over the fingerprinted forms.</summary>
    internal static double Similarity(string left, string right);
}

[TestFixture]
public sealed class DerivedMaterialTests
{
    /// <summary>
    /// No expression in the tree matches a quarantined expression: exact fingerprint equality fails at any
    /// length except for the named (a)-classified merger exemptions (3.2); similarity at or above the
    /// configured threshold fails for expressions longer than 40 characters.
    /// </summary>
    /// <remarks>
    /// The failure message names the file and nothing else. The threshold, the measured similarity and the
    /// location are written to <c>gate-results.md</c>, which the Implementer may not read (S-C2).
    /// </remarks>
    [Test]
    public void NoExpressionInTheTreeIsTextuallyDerivedFromAQuarantinedExpression();

    /// <summary>
    /// No file under <c>src/</c> cites a surveyed application's source path or line number. The scope is
    /// <c>src/</c> and not <c>docs/</c> — see 4.6; if the owner takes option (ii) there, the scope widens.
    /// </summary>
    [Test]
    public void NoSourceFileCitesASurveyedApplicationSourcePath();
}
```

The quarantine location is supplied by an environment variable; when it is unset the tests report
**inconclusive**, never pass. A gate that silently passes because it could not find its input is worse than
no gate — that failure mode is exactly F-5's "the gate passed means less than it appears to."

### 4.4 Dependency licenses — the consequence the flip creates

`Directory.Packages.props:40-42` pins `SixLabors.ImageSharp` at 3.1.12 and reasons that the Six Labors
Split License "permits free use in an **OSI-licensed** project such as this GPLv3 one." If the owner's
chosen license is **not** OSI-approved, that permission does not carry over and ImageSharp becomes a paid
dependency or a removal. This is the clearest instance of a general rule, so Step 9 re-checks **every**
package, not only this one. Flagged now because it can change the license decision itself, and discovering
it after the flip would be expensive.

### 4.5 The git history, and what it does and does not imply

**Facts, verified.** `arronix/main` is `bbec8cec8`, a **single-commit orphan root** with 884 files; there is
no Sonarr history behind it. The repository also holds `dev` (`f876c0a23`), `remotes/origin/sonarr-v5-develop`
(`a4f210855`) and three `copilot/*` branches, and `origin/HEAD` points at `origin/dev`. `git show
dev:LICENSE.md` returns the GPL v3 text: **those branches carry the Sonarr tree under GPLv3, and they are
reachable in any clone.**

**What that implies.**

- The repository, as published, still distributes GPL-licensed work. Removing `LICENSE.md` from the clean
  branch while the legacy branches remain leaves that work distributed without its license text, which is a
  GPL compliance problem in its own right — the opposite of the intended outcome.
- A reader arriving at the default branch (`dev`) sees a GPL project. The license a project *appears* to be
  under matters for adoption and for good faith.

**What it does not imply.**

- It does not make the clean branch a derivative work. License obligations attach to a *work*, not to a
  repository, a remote or a shared object database. A file that copies nothing from GPL sources is not
  encumbered because a sibling branch does.
- Rewriting or dropping history is **not** required for C-1. Deleting branches destroys evidence and buys
  nothing legally; it is a distribution decision, not a compliance one.

**Recommended, for the owner to choose in Step 10:**

| Option | What happens | Trade |
|---|---|---|
| **A — new repository** | Publish `arronix/main` alone as a fresh repository under the new license; keep the current repository as an archive, GPL intact, default branch unchanged. | Cleanest reading; loses stars/issues continuity |
| **B — same repository, both licenses** | `LICENSE.md` becomes the new license; `LICENSE.legacy-gplv3.md` retained and referenced from the README as governing the legacy branches; default branch moved to `arronix/main`. | Keeps continuity; requires a README that explains two licenses without ambiguity |
| **C — delete the legacy branches** | Remove `dev`, `sonarr-v5-develop` and the `copilot/*` branches after archiving them elsewhere. | Simplest end state; destroys the project's own history and the archive has to live somewhere anyway |

**One check before any of them:** has any build containing derived material been *distributed*? The tree
reports build 0/0 with no release artifacts, so the expected answer is no. If binaries were ever published,
those recipients received them under GPLv3 and that grant does not retract — it does not affect the
relicensing of the rewritten work going forward, but it should be recorded rather than discovered.

### 4.6 The documentation scrub, re-sized

**The first draft mis-sized this by roughly an order of magnitude** (S-C6). It listed "the 7 `_reference/…`
doc-comment citations" and carried the scrub as a single parallel row, while writing a gate that reads
*"zero `_reference/` paths and zero `\.cs:\d+` citations of surveyed sources in `src/` **and** `docs/`."*
Counted against the tree, 2026-08-17:

| | `_reference/` hits | files | `<file>.cs:NN` citations | files |
|---|---:|---:|---:|---:|
| `src/` | **7** | 7 (all `Engines/Matching`) | **46** | — |
| `docs/` | **62** | 12 | **566** | 17 |

The `docs/` figure needs one further split the critique did not have to make, because it decides the size
of the work. Of the 566 citations, **112 point at files that exist in this tree** (`MoviesShape.cs`,
`MoviesIntent.cs`, `MoviesReleaseParser.cs`, …) — those are ordinary internal cross-references and the gate
was never about them. **454 point at files that do not exist here**, across **153 distinct surveyed
filenames**, led by `Parser.cs` (47), `FileNameBuilder.cs` (30), `NzbDroneLogger.cs` (13),
`DiskTransferService.cs` (13), `QualityParser.cs` (7). Those 454 are the real target, and they are
concentrated in documents whose *substance* is the comparison: `file-management.md` (104 citations),
`movies-plugin-review.md` (95), `parsing-and-test-corpus.md` (75), `acquisition-pipeline.md` (45),
`observability.md` (44), `naming-and-tokens.md` (44), `authentication.md` (40). `servarr-prior-art.md`
alone carries 30 of the 62 `_reference/` hits in 451 lines.

**The contradiction this creates with Part 5 #5.** That deferral says the docs are *"scrubbed of citations,
not rewritten"* and *"remain accurate descriptions of a public work after the citations go"*. Both cannot
be true at this size. A design document that says "the 90-day theatrical window is derived at
`SkyHookProxy.cs:292-314`" does not become an accurate document when the citation is deleted; it becomes an
unsupported assertion. Removing 454 load-bearing citations from documents built on them **is** the
"separate and much larger rewrite" that the same paragraph puts out of scope.

**So the gate splits, and the second half becomes an owner decision rather than a hidden work package.**

| | Scope | Size | Status |
|---|---|---|---|
| **8a** | `src/` → zero `_reference/`, zero surveyed `.cs:NN` | **53 sites** across ~7 files, plus 3 false claims in `README.md` / `ARCHITECTURE.md` | **Hard gate. Cheap. Blocks the flip.** |
| **8b** | `docs/` → the 62 `_reference/` hits and the 454 surveyed citations | **516 sites across 17 documents**, several of which lose their evidentiary basis | **Owner decision. Does not block the flip.** |

Two options for 8b, and the reason 8a is not one of them: a `_reference/` path inside a shipped source file
sits next to a rewritten expression and invites the conclusion that the rewrite was not clean. That is a
provenance problem in the artifact itself, it is 53 sites, and it is settled. The documents are a different
question.

- **(i) Scope the gate to `src/` and keep the design documents as what they are** — openly comparative
  analyses of a public project, citations intact. R-10 already concludes that citing a public work is not a
  copyright problem, only a provenance one, and the provenance exposure of a *design document* that says
  plainly "we studied this and here is where we diverged" is very different from that of a source file that
  says it beside 700 characters of regex. Honest comparative analysis is also, on its own, evidence of a
  disciplined process rather than against one.
- **(ii) Budget the documentation rewrite as its own work item**, sized at 516 sites and seven substantial
  documents, and accept that some of them become shorter and weaker, or get rewritten from the tree as it
  now stands rather than from the comparison that produced it.

**Recommendation, and it is marked as the editor's choice pending the owner's ruling: take (i) now, and
leave (ii) available.** It is the minimal resolution consistent with the critique, it removes the
contradiction with Part 5 #5 by scoping the promise to what the promise can cover, and it does not spend a
large rewrite to buy something R-10 says was never a copyright exposure. What (i) costs is that a reader of
`docs/design/` sees a project that studied Radarr closely and says so — which is true, and which the
provenance record in Part 6 says anyway.

---

## Part 5 — Deferred, and stated as deferred

1. **Whether (b) items survive the re-justification pass.** 3.6 defines the method, not the outcome. Some
   rows will fail to be defensible and will be dropped; how many is unknown until it runs.
2. **Adversarial parse robustness at flip time.** 3.3 states the regression honestly and the mitigations
   partially. The field corpus closes it over months, not before the flip.
3. **The Lidarr-layer question on Munkres** (2.4). Conservative `NOTICE` wording is proposed; whether more
   is needed is a question for counsel.
4. **`Arronix.Common`'s clean-room exercise is re-verified, not re-done** (Step 6b). If the gate finds a
   hit, this plan gains a work package it does not currently size.
5. **The documentation citation question — now sized, and now an owner decision.** The previous wording
   here ("the docs are scrubbed of citations, not rewritten … they remain accurate descriptions of a public
   work after the citations go") was measured and does not hold: the scrub is 516 sites across 17
   documents, and several of them lose their evidentiary basis when the citations go rather than surviving
   intact. 4.6 replaces this deferral with two costed options and a recommendation. What remains genuinely
   deferred is only the owner's ruling between them, and it does not block the flip.
6. **Tv, Music and Books ladders** (1.5) are (b) and are superseded by `quality-axes.md` on their own
   conversion milestones, not by this plan.
7. **No estimate of elapsed time** is offered. The dependency graph in 4.1 is the schedule; converting it
   to dates needs the sibling design's shape first.

---

## Part 6 — The provenance record

Kept under `docs/provenance/` and written as the work happens, because its whole value is that it was not
reconstructed afterwards.

| Artifact | Written at | Contains |
|---|---|---|
| `quarantine-manifest.md` | Steps 0, 1b | Every quarantined path, its SHA-256, where it moved, when. **Plus the `EvidenceCell` baseline recorded as *derived*, with its derivation: source rows, reduction rule, and the role that ran it** (S-C4) |
| `release-token-grammar.md` (+ `.sanitized.md`) | Steps 1a, 3 | The spec before and after the wall |
| `spec-refusals.md` | Step 3 | What the Spec Editor stripped, and why — **including every constraint struck for having no justification beyond the original's behavior** (S-C3) |
| `agent-briefs.md` | Steps 1a, 1b, 3, 4 | The verbatim brief each role received, including the read restrictions — **the Implementer's brief enumerating the baseline, `gate-results.md` and `differential-report.md` as unreadable** |
| `coverage-report.md` | Step 4b | `CoverageReport` output: baseline cells, covered, new, missing, **negatives missed**, and the field-corpus acceptance separately |
| `differential-report.md` | Step 6a | Agreement rates and every divergence with its triage verdict. **Plus the oracle ledger: every run of the budgeted twelve with its timestamp, requester and corpus scope, and what was returned across the wall** (S-C1). Not readable by the Implementer |
| `gate-results.md` | Steps 6b, 7 | Fingerprint-gate output per file: pass, or location and similarity; **the (a)-classified merger-exemption list by name** (S-C7); **and the invocation log — every run, timestamped and attributed** (S-C2). Not readable by the Implementer |

**Two of these files are now themselves quarantined material**, which is the point the amendments kept
arriving at: `differential-report.md` holds per-case oracle answers and `gate-results.md` holds similarity
scores against the originals. Each is a faithful record precisely because it is detailed, and each would
function as a reconstruction channel if it flowed back to the clean side. The record is complete and the
wall holds only because the two properties are separated by *who reads it*, not by what is written down.

---

## Summary

**Inventory.** **19** files carry clearly-derived material: 3 in `Arronix.Plugin.Movies`, **5** in
`Arronix.Host/Engines`, 11 test fixtures across two test projects — including a **second complete copy of
the ladder and rung table in `Arronix.Host.Tests/Engines/ParseEngineFixtures.cs`** that no prior summary
lists, and **`ReleaseTagScanner.cs`**, which the regex-bearing sweep missed because it carries no
expression of its own — only three ported decisions, one of them the resolution bucket cascade. 23 further
files carry borderline material, including four test fixtures whose expected values are stated in the file
to be the surveyed application's. `Arronix.Plugin.Tv`, `.Music` and `.Books` release parsers are clean, but
their ladder *orderings* are inherited and owe a re-justification. `Arronix.Common` came from an earlier
clean-room exercise and is re-verified rather than assumed.

**Classification.** 88 expressions, **503** data rows and **704** test rows are class (c) and must be
replaced or deleted; ~264 data rows, 61 test rows and 4 assertion sites are class (b) and must be
re-justified in our own words; ~140 rows are class (a) functional facts and stay. Uncertain classifications
were resolved to (c). The 686 → 704 move is an arithmetic correction, not new material: the prior figure
summed seven of the eight (c) fixtures in `Movies.Tests`.

**Sequencing.** Twelve steps, critical path 0 → 1a → 3 → 4a → 5 → 6a → 7 → 10, with the quality-axes
design, the baseline reduction, the corpus generator, the `Arronix.Common` re-verification and the `src/`
provenance scrub running beside it. Each step has a stated gate.

**The four controls the critique tightened, because each was a channel back around the wall.** The
black-box oracle is **budgeted at twelve whole-corpus runs**, returns aggregates only, and never runs
against a case derived from the ported corpus — an unbounded oracle over material whose disputed portion
*is* behavior is differential reconstruction, and it partly undoes the physical-removal control the whole
scheme rests on. The fingerprint gate reports the Implementer a **boolean and a spec-paragraph id**, never
a similarity score, because a score is a gradient to hill-climb toward maximal similarity that still
passes. Spec sanitization is **semantic as well as syntactic**: every ordering constraint and ambiguity
case carries a functional justification that stands without reference to the original, and the Spec Editor
strikes what cannot supply one. And the corpus baseline is stated in the **axes vocabulary** from the
start, is recorded as derived material the Implementer may not read, keeps its **negative** cases at a
100% hard gate, and is a floor reached by walking the cross-product rather than a target aimed at.

**Two questions the flip carries** and one it no longer does. It must settle the ImageSharp OSI-license
clause, and which of three branch strategies the repository adopts given that `dev` and
`sonarr-v5-develop` still carry GPL-licensed Sonarr code even though `arronix/main` is a single-commit
orphan root with no Sonarr history behind it. It no longer carries the documentation scrub: that was
mis-sized by an order of magnitude, is 53 sites in `src/` (a hard gate) and 516 sites across 17 documents
in `docs/` (an owner decision, recommended to be scoped out), and the two are no longer one row.
