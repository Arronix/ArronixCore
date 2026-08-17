# Quarantine manifest and residual derived-material inventory

> **Role.** Gatekeeper. Per `clean-room-plan.md` Part 6 and step 0.
>
> **Every figure below was measured against the working tree on 2026-08-17**, not carried forward from any
> session's report. Where a report and the tree disagree, the tree is recorded.

---

## 1. Step 0 was never executed

This is the first thing the manifest has to say, because everything else is read differently in its light.

`clean-room-plan.md` §3.1 control 1:

> **Input control, enforced by removal.** Before the Implementer starts, the quarantined files are moved to
> a location outside the repository and outside the agent's working directory […] This is stronger than an
> instruction not to look, and it is the control the whole scheme rests on.

**No file was moved.** 17 of the 19 class-(c) files are in the tree at their original paths.
`/Users/adam/Code/GitHub/Arronix/NzbDrone.Common` is still a sibling of the working directory. No quarantine
directory exists and no SHA-256 record was taken before this session.

**The consequence, stated plainly.** Every session in this programme ran with the quarantined material
inside its own working directory, protected only by an instruction in its brief. The plan is explicit that
this is the weaker of the two arrangements. One session — the Implementer for the title-claim rewrite —
opened `MoviesParsing.cs`, correctly recognised the breach and forfeited rather than proceeding; that
session's honesty is the reason the breach is visible at all. The remaining sessions declared compliance,
and their declarations are the whole of the evidence.

**What this does and does not cost.** It does not make any rewritten file derivative — the textual-derivation
gate passes, and structural non-convergence holds. What it costs is claim **C-2**, "the remaining code was
independently created", which Part 0 says rests on the process record rather than on the artifact. C-2 is
therefore **weaker than the plan intends**, and the honest statement to counsel is that the discipline was
enforced by instruction and by output testing, not by removal.

**Recommended remedy, unchanged from the plan:** execute Step 0 before any further rewrite session — most
urgently before the title-claim rewrite is re-attempted, since that is the one whose Implementer already
had to forfeit for exactly this reason.

### SHA-256 of every class-(c) file still in the tree, taken now

Recorded so a later session can prove none of them moved or changed between this record and the removal.

| SHA-256 (first 16) | Path |
|---|---|
| `c404c1b796f62d42` | `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs` |
| `eed946c65e3dcd12` | `src/Arronix.Plugin.Movies/Definition/MoviesCorpus.cs` |
| `b074af29b271786c` | `src/Arronix.Host/Engines/Parsing/ReleaseTokenVocabulary.cs` |
| `a5700e77fcb3943c` | `src/Arronix.Host/Engines/Parsing/ReleaseGroupScanner.cs` |
| `f8615253898f8ef3` | `src/Arronix.Host/Engines/Parsing/ReleaseLanguageScanner.cs` |
| `fd5b6b7e4c5a14f6` | `src/Arronix.Host/Engines/Parsing/ReleaseTagScanner.cs` |
| `a5d7b5d93013ec20` | `src/Arronix.Host/Engines/Parsing/ReleaseCodecScanner.cs` |
| `7beb14bcc30949f9` | `src/Arronix.Host/Engines/Naming/NamingModifiers.cs` |
| `3b845bd060d52edb` | `src/Arronix.Host.Tests/Engines/ParseEngineFixtures.cs` |
| `8a0788bf630345f6` | `src/Arronix.Host.Tests/Engines/ParseEngineRungResolutionTests.cs` |
| `071740dca2ae9bd2` | `src/Arronix.Host.Tests/Engines/ParseEngineTagScannerTests.cs` |
| `3b1700e5c35d92db` | `src/Arronix.Plugin.Movies.Tests/Quality/QualityParserTests.cs` |
| `cae315ccfb308076` | `src/Arronix.Plugin.Movies.Tests/Parsing/ReleaseGroupParserTests.cs` |
| `b0a315d9c3bffac9` | `src/Arronix.Plugin.Movies.Tests/Naming/NameFormatterTests.cs` |
| `a7de9ed1fdad7387` | `src/Arronix.Plugin.Movies.Tests/Parsing/MovieTitleParserTests.cs` |
| `e60a6c3535a2a718` | `src/Arronix.Plugin.Movies.Tests/Parsing/EditionParserTests.cs` |
| `e0ea6438b92037b9` | `src/Arronix.Plugin.Movies.Tests/Parsing/TitleNormalizerTests.cs` |
| `f24f91cca9830070` | `src/Arronix.Plugin.Movies.Tests/Parsing/RejectedReleaseTests.cs` |

---

## 2. The class-(c) inventory, re-measured

**2 of 19 files are gone. 17 remain.**

| # | File | Plan's material | State in the tree now |
|---|---|---|---|
| 1 | `Movies/Definition/MoviesLadder.cs` | 30 rungs with weights, tie-groups, size bands | **DELETED** |
| 2 | `Movies.Tests/Quality/QualityLadderTests.cs` | 18 ported rows | **DELETED** |
| 3 | `Movies/Definition/MoviesParsing.cs` | 42 expressions + 101 rung rows + 7 fallbacks | **rung table and fallbacks GONE**; **21 expressions REMAIN** — the edition alternation, the 11 title patterns, 9 guards. 472 → 261 lines |
| 4 | `Movies/Definition/MoviesCorpus.cs` | 249 ported rows | **249 rows REMAIN** (220 quality + 29 title). 19 re-expressed onto truthful labels; none removed |
| 5 | `Host/Engines/Parsing/ReleaseTokenVocabulary.cs` | 9 expressions + 49-entry extension set | **unchanged** |
| 6 | `Host/Engines/Parsing/ReleaseGroupScanner.cs` | 8 expressions + 75 curated group names | **unchanged** |
| 7 | `Host/Engines/Parsing/ReleaseLanguageScanner.cs` | 3 expressions + 50-row language table | **unchanged** |
| 8 | `Host/Engines/Parsing/ReleaseTagScanner.cs` | the 8-row resolution bucket cascade | **unchanged** |
| 9 | `Host/Engines/Naming/NamingModifiers.cs` | 2 named expressions | **unchanged** |
| 10 | `Host.Tests/Engines/ParseEngineFixtures.cs` | 31 ladder rows, 77 rung rows, 24 expressions | **unchanged — 31 ladder rows, 77 rung rows, 16 regex-shaped literals** |
| 11 | `Host.Tests/Engines/ParseEngineRungResolutionTests.cs` | 25 rows | **25 rows remain** |
| 12 | `Host.Tests/Engines/ParseEngineTagScannerTests.cs` | 23 rows | **23 rows remain** |
| 13 | `Movies.Tests/Quality/QualityParserTests.cs` | 179 ported rows | **25 `[TestCase]` rows remain**, and the file now drives all 249 `MoviesCorpus` rows through two tests. The ported *rows* left; the ported *corpus* they moved onto did not |
| 14–19 | `ReleaseGroupParserTests` (131), `NameFormatterTests` (105), `MovieTitleParserTests` (99), `EditionParserTests` (48), `TitleNormalizerTests` (40), `RejectedReleaseTests` (36) | 459 ported rows | **all six unchanged, 459 rows remain** |

### Counts against Part 2's table

| Class | Plan | Now | Change |
|---|---:|---:|---|
| (c) expressions | 88 | **~63** | −21 in `MoviesParsing.cs` (the guards that existed only to feed rung rows); `ParseEngineFixtures.cs` measured at 16 rather than 24 |
| (c) data rows | 503 | **357** | −30 ladder, −108 Movies rung/fallback, −8 bucket mappings still present; 249 corpus + 108 fixture ladder/rung remain |
| (c) test rows | 704 | **532** | −18 `QualityLadderTests`, −154 `QualityParserTests` rows; 459 across six untouched fixtures + 48 in `Host.Tests` + 25 remain |
| (c) files | 19 | **17** | −2 |

**The single largest remaining item is `ParseEngineFixtures.cs`.** It carries a complete second copy of the
ladder and the rung table — 31 rows and 77 rows — and its own remark still states they are the surveyed
application's. §3.5 disposes of it by deletion and replacement with a synthetic toy family; that is
untouched work, and it is the item most likely to be forgotten because it is a test fixture rather than
production code.

---

## 3. Class-(b) items

23 files, unchanged. The re-justification pass in §3.6 has **not** been run: no (b) item carries a
"what is this value, why this value, what breaks at other values" table. R-7 (75 group names), R-8 (50-row
language table), R-11 (the 90-day theatrical window, the 10s/30s distance grace) and the codec precedence
ordering are all still justified by a citation rather than by an argument.

One (b) item **has** been discharged, and is recorded because it is the only one: the Tv/Music/Books ladder
*orderings* (§1.5, S-C8 ii) remain undefended, but Movies' ladder ordering is gone entirely, so the
obligation on it is extinguished rather than deferred.

---

## 4. Provenance citations

| Scope | `_reference/` | Surveyed `.cs:NN` | Total sites | Step |
|---|---:|---:|---:|---|
| `src/` | 7 in 7 files | 46 in 22 files | **53** | 8a — hard gate, **not done** |
| `docs/` | 71 in 12 files | 461 in 17 files | **532** | 8b — owner decision, recommended out of scope |

`docs/` grew from the plan's 516 because this programme's own documents cite surveyed sources:
`clean-room-plan.md` 9, `quality-axes.md` 5. The `docs/` scrub is **sized here and not performed**, per the
assignment and per 4.6's recommendation of option (i).

The three false claims in `README.md:214-215,218-223` and `ARCHITECTURE.md:218,705-718` were not checked by
this session and are presumed outstanding; they are part of step 8a.

---

## 5. Derived material recorded as derived, per S-C4

| Artifact | Status | Who may read it |
|---|---|---|
| `docs/clean-room/corpus/shape-classes.md` | **derived** — reduced from the superseded corpus by a session that had read it | Gatekeeper, Surveyor. Recorded by the spec session as not readable by the Implementer |
| `docs/clean-room/corpus/corpus.jsonl` and its generator | **disputed** — the generator is Step 4b work (Implementer, clean) but was written by the session that had read the quarantined material. The spec session recorded this conflict itself | Treat as dirty-side until a clean Implementer re-derives it from the specs |
| `docs/provenance/differential-report.md` | **quarantined output** — carries per-case oracle answers | Gatekeeper only |
| `docs/provenance/gate-results.md` | **quarantined output** — carries similarity values | Gatekeeper only |

The `EvidenceCell` baseline the plan's Step 1b calls for was **not produced**: the spec session performed
the coarser shape-class reduction instead and recorded the substitution. So the fidelity measurement §3.3
specifies — "before the old corpus is deleted, each of its 249 rows is reduced to its `EvidenceCell`" — has
not been made, and the old corpus has correspondingly not been deleted.

---

## 6. License-flip readiness

**The flip is blocked.** `clean-room-plan.md` step 10 depends on steps 7, 8a and 9, and none of the three
is complete. Claim **C-1** — "no file in the tree is a copy or adaptation of *arr source" — is not yet
defensible, because the class-(c) material is still present rather than replaced.

### What blocks it, in the plan's own gate order

| Step | Gate | State |
|---|---|---|
| **0** | Quarantine complete | **not done.** 17 (c) files in place; the sibling `NzbDrone.Common` clone still beside the working directory |
| **5** | Ladder gone | **partly.** Gone for Movies; Tv, Music and Books still declare ladders with inherited orderings, and every ladder contract type still has consumers |
| **6a** | Behaviour, negatives 100% | **FAIL** — four cases, NEG-14 and NEG-18. See `differential-report.md` |
| **6b** | `Arronix.Common` re-verified against `NzbDrone.Common` | **not run** |
| **7** | Removal + textual-derivation gate green as a standing test | **not done.** The gate passes when run by hand over the rewritten files, but `DerivedMaterialTests` does not exist in `Arronix.Architecture.Tests`, so nothing refuses the material's return |
| **8a** | `src/` provenance scrub | **not done.** 53 sites. The three false claims in `README.md` and `ARCHITECTURE.md` are outstanding — and there is a **fourth site**, `README.md:63` ("Builds via `src/Sonarr.sln`"), which the plan's inventory does not list |
| **9** | `NOTICE` for Munkres; dependency re-check | **not done.** No `NOTICE` file exists. The MIT attribution obligation for `MunkresSolver.cs` is unmet **today**, independently of the GPL question |

### What remains in `src/`

- **17 of 19 class-(c) files.** ~63 expressions, 357 data rows, 532 test rows. The two deletions are
  `MoviesLadder.cs` and `QualityLadderTests.cs`.
- The largest single item is **`Arronix.Host.Tests/Engines/ParseEngineFixtures.cs`** — a complete second
  copy of the ladder (31 rows) and the rung table (77 rows) plus 16 expressions, entirely untouched, whose
  own remark still states the data is the surveyed application's.
- **`MoviesParsing.cs` still carries 21 expressions**: the ~442-character edition alternation, the eleven
  title patterns in their inherited order, the stop-word expression with its lookarounds, and nine guards
  including the ~491-character whole-disc probe. The rung table is gone; the expressions are not.
- **Six ported test fixtures are wholly untouched** — 459 rows across `ReleaseGroupParserTests`,
  `NameFormatterTests`, `MovieTitleParserTests`, `EditionParserTests`, `TitleNormalizerTests` and
  `RejectedReleaseTests`. Three of them still contain the literal string `RADARR` in row data.
- **`MoviesCorpus.cs`'s 249 rows** remain, and its own remark still states they are ported rather than
  paraphrased.
- **23 class-(b) files** with the §3.6 re-justification pass not run.
- **53 provenance citation sites.**

### What remains in `docs/`

**532 sites across 17 documents** — 71 `_reference/` paths and 461 citations of files that do not exist in
this tree, across 154 distinct surveyed filenames. Concentrated in `file-management.md` (90),
`parsing-and-test-corpus.md` (68), `movies-plugin-review.md` (64), `acquisition-pipeline.md` (45),
`authentication.md` (40), `naming-and-tokens.md` (40).

This has grown from the plan's measured 516 because this programme's own documents cite surveyed sources:
`clean-room-plan.md` (9) and `quality-axes.md` (5).

`parsing-and-test-corpus.md` additionally **quotes two actual expressions** (plan §1.4), which are class
(c) rather than class (a) and are therefore *not* covered by 4.6's option (i). They are two sites and their
removal is cheap; they should move into step **8a**, where the plan currently puts them, rather than being
left to the deferred `docs/` decision.

### Two questions the flip carries, unchanged and unanswered

1. **ImageSharp.** `Directory.Packages.props:39-42` pins 3.1.12 and reasons from the Six Labors Split
   License permitting free use "in an OSI-licensed project such as this GPLv3 one". If the owner's chosen
   license is not OSI-approved, ImageSharp becomes a paid dependency or a removal.
2. **The branch question.** `dev` and `origin/sonarr-v5-develop` still carry the Sonarr tree under GPLv3
   and are reachable in any clone; `origin/HEAD` points at `origin/dev`. Options A/B/C are in plan §4.5.
   Not verified by this session.

### The shortest honest path to an unblocked flip

1. Execute **step 0** — move the 17 files out of the tree and the sibling clone out of the workspace. This
   is also the precondition for the title-claim rewrite, whose Implementer already had to forfeit for its
   absence.
2. Delete `ParseEngineFixtures.cs`'s ladder, rung table and expressions, replacing them with a synthetic
   toy family (§3.5 — deletion, no successor needed).
3. Re-attempt the title-claim rewrite from `docs/clean-room/spec/` against a **stubbed** `MoviesParsing.cs`,
   in a fresh session — the remedy the forfeiting Implementer set out.
4. Delete `MoviesCorpus.cs` and the six ported fixtures once the generated corpus covers them.
5. Close **NEG-14** and **NEG-18**.
6. Run **step 8a** (53 sites + 4 false claims) and **step 9** (`NOTICE`).
7. Run **step 6b** against `NzbDrone.Common`.

Steps 1, 2 and 6 are cheap. Step 3 is the critical path and is the one that has already failed once.
