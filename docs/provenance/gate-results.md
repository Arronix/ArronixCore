# Textual-derivation gate — results and invocation log

> **Role.** Gatekeeper. Per `clean-room-plan.md` §4.3 and Part 6.
>
> **Not readable by the Implementer.** This file carries similarity values, and a similarity value is a
> gradient an implementer can hill-climb toward maximal similarity that still passes (S-C2).
>
> **No quarantined expression is reproduced anywhere in this file.** Locations are cited as
> `file:line`; the text is never quoted.

---

## 1. Invocation log

§4.3: *"A burst of forty runs against one file is itself the finding."* Every invocation, timestamped.

| # | Timestamp (UTC) | Requester | Scope | Verdict |
|---|---|---|---|---|
| 1 | 2026-08-17T14:1xZ | Gatekeeper (closeout session) | all rewritten files vs all quarantined expressions | PASS |

One invocation. No file was re-run.

---

## 2. What was compared

**Quarantined side — 84 regex-shaped expressions** across the six class-(c) files that still carry them:

| File | Expressions |
|---|---:|
| `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs` | 21 |
| `src/Arronix.Host.Tests/Engines/ParseEngineFixtures.cs` | 16 |
| `src/Arronix.Host/Engines/Parsing/ReleaseTokenVocabulary.cs` | 9 |
| `src/Arronix.Host/Engines/Parsing/ReleaseGroupScanner.cs` | 8 |
| `src/Arronix.Host/Engines/Naming/NamingModifiers.cs` | 4 |
| `src/Arronix.Host/Engines/Parsing/ReleaseLanguageScanner.cs` | 3 |

Plus the same files' shorter literals, which the extractor keeps whenever they carry two or more
metacharacters. **These files are still in the tree** — the gate is comparing against in-tree originals
rather than against a quarantine, because Step 0 never ran. See `quarantine-manifest.md`.

**Rewritten side — 62 files** across `src/Arronix.Host/Engines/Parsing/Evidence/` and
`src/Arronix.Abstractions/Quality/`. Exactly one carries any expression at all.

---

## 3. Per-file verdict

| Rewritten file | Expressions | Longest | Verdict |
|---|---:|---:|---|
| `src/Arronix.Host/Engines/Parsing/Evidence/EvidenceLexer.cs` | 7 | 34 chars | **PASS** |
| every other rewritten file (61) | 0 | — | vacuously PASS |

No exact fingerprint match at any length. No expression in the 41–120 band exists, so the 0.60 similarity
rule had nothing to gate.

**40-character design rule (§3.2, gate 4a): PASS.** The longest rewritten expression is 34 characters. The
structural-non-convergence requirement in control 3 is met by construction: the rewrite is a token-class
lexer over a phrase table with maximal munch, not one alternation per token class, and its seven
expressions each serve exactly one token class.

---

## 4. Merger-exemption list, §3.2 / gate 7

**Empty.** No exemption was claimed, because none was needed: no rewritten expression is an exact
fingerprint match against a quarantined one, so the ≤40-character exact-match rule never fired.

Gate 4.2 step 7 requires the exemption list to *match Part 2's classification exactly*. An empty list
trivially satisfies it, and this row is recorded so that a later reader can tell an empty list from an
un-run gate.

---

## 5. Highest similarity observed, with the Gatekeeper's justification

The highest similarity between any rewritten and any quarantined expression is **0.642**. It is above the
0.60 threshold, so it is recorded and justified here rather than left to be rediscovered — even though the
threshold does not apply to it.

| Similarity | Rewritten (len) | Against | Why it is not a derivation |
|---:|---|---|---|
| 0.642 | `EvidenceLexer.cs`, 27 chars, the upscale-transformation shape | `ParseEngineFixtures.cs:139` (62 chars) | Both are anchored digit-run expressions, so they share `^ ( \d { , } ) $`. The rewritten one matches an upscale statement of the form *from-height to-height*; the quarantined one at that line is a title-and-year pattern. No vocabulary, no lookaround and no semantic overlap. |
| 0.609 | `EvidenceLexer.cs`, 14 chars, the frame-rate shape | three separate quarantined sites, 18–23 chars | A 14-character anchored `digits + word` expression. Three unrelated quarantined expressions reach the same score, which is itself the evidence: the similarity is measuring shared regex punctuation on very short strings, not shared authorship. |
| 0.604 | `EvidenceLexer.cs`, 32 chars, the pixel-dimension shape | `ParseEngineFixtures.cs:139` (62 chars) | Same digit-run punctuation. The rewritten expression reads a `width x height` raster the quarantined side has no equivalent for at all — the old scan reaches 2160 only through a marketing-name fallback. |

**Judgment.** All three pairs sit in the ≤40-character band, which §3.2 exempts from similarity comparison
precisely because short expressions converge on shared syntax. The metric behaves as the plan predicted:
*"the residual risk is concentrated in short expressions where convergence is expected and where merger
makes convergence harmless."* None of the three is exempted by name, because none needed to be — each
passes the rule that actually applies to it, exact-match, on its own.

---

## 6. The second standing test — `NoSourceFileCitesASurveyedApplicationSourcePath`

`DerivedMaterialTests` does not exist in `Arronix.Architecture.Tests`. Run by hand:

| Scope | `_reference/` paths | Surveyed `.cs:NN` citations | Gate |
|---|---:|---:|---|
| `src/` | **7** in 7 files | **46** across 22 files | **FAIL** — 53 sites |
| `docs/` | 71 in 12 files | 461 across 17 files | out of scope under 4.6 option (i) |

The `src/` figure is **unchanged from the plan's own count**: step 8a has not been performed. The ten
distinct surveyed filenames still cited from `src/` are `QualityParser.cs` (13), `DistanceCalculator.cs`
(8), `FileNameBuilder.cs` (8), `MovieService.cs` (5), `MoviesCataloger.cs` (5), `SkyHookProxy.cs` (3),
`MoviesReleaseMatcher.cs` (1), `Distance.cs` (1), and two others.

`docs/` has grown from the plan's measured 516 sites to **532**, because this programme's own new documents
cite surveyed sources: `clean-room-plan.md` (9) and `quality-axes.md` (5). Under 4.6 the recommendation is
option (i) — scope the gate to `src/` — so this is reported, not actioned.

---

## 7. Gate verdict

| Gate | Verdict |
|---|---|
| §4.3 `NoExpressionInTheTreeIsTextuallyDerivedFromAQuarantinedExpression` (rewritten files) | **PASS** |
| §3.2 40-character design rule | **PASS** |
| §3.2 merger-exemption list matches Part 2 | **PASS** (empty) |
| §4.3 `NoSourceFileCitesASurveyedApplicationSourcePath` | **FAIL** — 53 sites, step 8a not performed |

**Scope statement, so the PASS is not read as more than it is.** The first gate passed over the material
that *was* rewritten — the evidence scanner and the quality-axes contracts. It says nothing about the
class-(c) expressions that are **still in the tree unrewritten**: the 21 in `MoviesParsing.cs`, the 16 in
`ParseEngineFixtures.cs` and the 21 across the three host scanners. Those are not gate failures; they are
work not yet done, and they are inventoried in `quarantine-manifest.md`.
