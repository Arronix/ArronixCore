# Differential validation — the oracle ledger

> **Historical gate record.** The evidence scanner and quality-axes implementation named below were
> removed on 2026-08-20. This file preserves what was compared at the time; it is not evidence that those
> types or paths still exist.

> **Role.** Gatekeeper. Written as the work happened, per `clean-room-plan.md` Part 6.
>
> **Not readable by the Implementer** (`clean-room-plan.md` §3.1 role table). This file carries per-case
> oracle answers and per-dimension divergence counts; both are a reconstruction channel if they flow back
> to the clean side.

---

## 1. The ledger — every oracle run, per §3.1 control 5(a)

The budget is **twelve whole-corpus runs for the entire programme**. A run counts when it *executes the
superseded implementation*. A run of the candidate alone does not query the removed artifact and therefore
does not consume budget; those are logged separately below so the distinction is auditable rather than
asserted.

| # | Timestamp (UTC) | Requester | Corpus scope | Returned across the wall |
|---|---|---|---|---|
| 1 | 2026-08-17T13:58:22Z | Gatekeeper (closeout session) | whole generated corpus, 8,099 cases | nothing — no implementer session was active |

**Budget consumed: 1 of 12. Remaining: 11.**

No run was made against any case derived from the ported corpus (§3.1 control 5(c)): the input is
`docs/clean-room/corpus/corpus.jsonl` in its entirety, which is generated from the sanitized spec and whose
generator carries a self-check that no superseded corpus row is reproduced at exact equality or after
case/separator folding.

### Candidate-only runs — no oracle executed, no budget consumed

| Timestamp (UTC) | Scope | Purpose |
|---|---|---|
| 2026-08-17T14:0xZ | 62 `negative`-kind cases | Triage the negative gate without re-querying the oracle |
| 2026-08-17T14:0xZ | 62 `negative`-kind cases | Dump the failing negative cases by case id |

---

## 2. What was compared, and what could not be

**Compared.** The rewritten evidence scan — `src/Arronix.Host/Engines/Parsing/Evidence/` read through the
shipped family `Arronix.Abstractions.Quality.Families.VideoQualityType.Read` — against the superseded scan
half, `ReleaseTagScanner.Scan` over `ReleaseTokenVocabulary`, on the six dimensions where the two
vocabularies both speak.

**Not compared, and the reason is structural rather than an omission.** The corpus's `Expects{Title, Year}`
half and negative rules NEG-1 … NEG-13 address **title claim, normalization and edition** — specs S1, S2,
S3 and S5's first thirteen rules. That rewrite **was not performed**: the Implementer session forfeited
before writing code, and `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs` still carries the eleven
superseded title patterns, the edition alternation and the stop-word expression. There is no candidate on
that half, so a differential over it would compare the oracle with itself. It is reported as **not run**,
not as passed.

**The measuring instruments are Gatekeeper-side.** Two mappings were written to make the comparison
possible: the old source vocabulary onto `VideoOrigin` (the map the deleted ladder encoded as one `source`
string per rung) and the old codec vocabulary onto the family's `Codec` members. Neither crossed to any
implementer. Where the old vocabulary has no spelling that can reach an expected value at all, the case is
counted in the `oracle-unreachable` column rather than silently scored as an oracle failure.

---

## 3. Run 1 — results

`scope=all  cases=8099  candidate-threw=0  oracle-threw=0`

| Dimension | Scored | Oracle correct | Candidate correct | Agree | Oracle-unreachable |
|---|---:|---:|---:|---:|---:|
| resolution | 8,025 | 71.90% | **90.47%** | 68.37% | 3,169 |
| origin | 8,049 | 57.04% | **75.11%** | 61.52% | 672 |
| bitstream | 8,049 | 78.78% | **82.72%** | 96.06% | 0 |
| corrections | 8,099 | 99.16% | **99.83%** | 99.09% | 0 |
| codec | 7,911 | 50.76% | **99.39%** | 51.37% | 1,934 |
| packaging | 8,099 | 89.39% | **99.88%** | 89.44% | 859 |

Whole-cell agreement (every scored dimension correct on one case):

| Corpus kind | n | Oracle | Candidate |
|---|---:|---:|---:|
| adversarial | 21 | 61.90% | **95.24%** |
| negative | 62 | 87.10% | 83.87% |
| normalization | 13 | 100.00% | 100.00% |
| positive | 723 | 48.69% | **72.06%** |
| structural | 7,280 | 23.28% | **58.35%** |

### Divergence triage — every divergence classified

| Dimension | Verdict | Count |
|---|---|---:|
| codec | new correct | 3,847 |
| origin | new correct | 2,276 |
| resolution | new correct | 2,014 |
| packaging | new correct | 852 |
| bitstream | new correct | 317 |
| corrections | new correct | 64 |
| origin | **old correct** | 821 |
| resolution | **old correct** | 524 |
| corrections | **old correct** | 10 |
| packaging | **old correct** | 3 |

**Triage of the four "old correct" classes.**

- **codec, resolution, packaging — the bulk of the candidate's lead is the un-bucketing the design
  required.** 1,934 codec cases and 3,169 resolution cases are values the old scan has *no spelling for*:
  `h266`, `vp9`, and the 540/576/360 line counts the bucket cascade folds away. This is the amendment
  `quality-axes.md` S-A5 asked for, measured.
- **origin, 821 old-correct.** Concentrated in two family `Read` rules that fire more widely than the
  corpus expects: the broadcast-bitstream inference (`HDTV` + `MPEG-2` read as `BroadcastBitstream` where
  the cell says `Broadcast`) and the dubbed-disc rule (a named language beside a dual-language marker
  beside a disc token promotes to `HighDefinitionDiscBitstream`). Both are **deliberate family rules, not
  scan defects**, and both are *both-defensible* rather than *old correct*: a `1080p HDTV MPEG-2` release
  is what the scene calls RawHD. Recorded as a **family-rule calibration question for the owner**, not as a
  regression. It is the single largest contributor to origin sitting at 75.11%.
- **resolution, 524 old-correct** and **corrections, 10 old-correct** are the residue of the evidence
  scanner reading fewer spellings than the tag scanner it replaces — the same finding the migration
  session recorded as 21 of its 23 registered corpus divergences. Every one closes by adding a phrase to
  `EvidenceVocabulary`; none by changing an axis, a policy or a rendering rule.

---

## 4. The negative gate — §3.3 / §4.2 step 6a, 100% hard

Only NEG-14 … NEG-19 are in scope: they are the negative rules about the **evidence scan**. NEG-1 … NEG-13
belong to the title-claim rewrite that was not performed.

Measured over the 62 deliberate `negative`-kind cases (candidate-only; the oracle column is vacuous in that
mode and is therefore not reported):

| Rule | n | Candidate | Verdict |
|---|---:|---:|---|
| NEG-14 — a name carrying no source vocabulary claims no origin | 6 | 83.33% | **FAIL** |
| NEG-15 — a codec name alone claims no origin | 3 | 100.00% | PASS |
| NEG-16 — a container extension alone claims no resolution | 4 | 100.00% | PASS |
| NEG-17 — a two-letter disc abbreviation outside the bracket dialect claims no origin | 3 | 100.00% | PASS |
| NEG-18 — a disc medium name plus a re-encode token claims no disc-image packaging | 4 | 25.00% | **FAIL** |
| NEG-19 — a disc medium name with no artifact statement claims no disc-image packaging | 4 | 100.00% | PASS |

### The gate FAILS. Four cases, two rules, and both are real.

```
NEG-18  Nine.Winters.2019.COMPLETE.BluRay.BDRip.x264-MERIDIAN9      claimed packaging=disc-image
NEG-18  Paper.Mountain.2021.COMPLETE.BluRay.BRRip.x264-NOVAGRP      claimed packaging=disc-image
NEG-18  Salt.And.Ember.2024.COMPLETE.BluRay.HDDVDRip.x264-ORBITFALL claimed packaging=disc-image
NEG-14  Bluray Morning 1977 AVC                                     claimed source=bluray
```

**NEG-18 is a defect in `EvidencePackagingScanner`.** It claims a disc image from `COMPLETE` beside a disc
medium name without consulting whether the same title also states a re-encode. `BDRip`, `BRRip` and
`HDDVDRip` each say outright that the artifact is not the disc. Over-claiming packaging is the failure mode
§3.3 singles out as the worse one: a false disc-image claim promotes a re-encode into the library as an
untouched disc.

**NEG-14's single failure is the known-hard case** — source vocabulary as the leading word of a work title.
The support rule in `EvidenceLexer.RequiresSupport` rescues `web` but not a full source word.

**The fix is not written here, and that is a rule rather than a preference.** §3.1 states that no session
holds two roles, and this session has read the quarantined originals and executed the oracle. Writing into
`Engines/Parsing/Evidence/` now would put dirty-side authorship inside the rewritten artifact and forfeit
the clean-room claim on the file — the same forfeit the previous Implementer session correctly declared.
What crosses back is what §3.1 control 5(b) and §4.3 prescribe: **a boolean and a spec-paragraph id.**

> To the Implementer: `EvidencePackagingScanner` fails **NEG-18**. `EvidenceSourceScanner` fails **NEG-14**.

---

## 5. Step 6a gate verdict

| Requirement | Threshold | Measured | Verdict |
|---|---|---|---|
| Agreement, synthesized corpus | ≥ 99% | 58.35% structural / 72.06% positive | **not met — see note** |
| Agreement, field corpus | ≥ 97% | no field corpus exists | **not measurable** |
| Negatives | 100%, hard | 4 failures across NEG-14 and NEG-18 | **FAIL** |
| Every divergence triaged | required | done, §3 above | PASS |
| Oracle runs ≤ 12, each logged | required | 1 of 12, logged | PASS |
| No run against a ported-corpus case | required | held | PASS |

**The note the agreement rows need, because the bare numbers are misleading in the candidate's favour and
the record should say so in both directions.** The ≥99% agreement threshold was written for a rewrite that
*reproduces* the old behaviour. This rewrite was required by `quality-axes.md` S-A5 to **diverge**: to stop
bucketing resolutions and to read codecs the old vocabulary cannot spell. Measured against the corpus's own
ground truth rather than against the oracle, the candidate is ahead on all six dimensions. Agreement with
the oracle is therefore the wrong statistic here, and reading 58% as a regression would be as wrong as
reading it as a pass. What the gate genuinely fails is the **negative** row, which is not a
divergence-by-design and has no such defence.

---

## 6. Findings for the owner

1. **Step 0 — quarantine — was never executed.** See `quarantine-manifest.md`. Control 1 is described in
   the plan as "the control the whole scheme rests on", and it was not in place for any session in this
   programme.
2. **The negative gate fails on four cases.** Two spec paragraphs, one of which (NEG-18) is an over-claim
   and is the failure direction §3.3 calls the worse one.
3. **The origin dimension sits at 75.11%** because two family `Read` rules fire wider than the corpus
   expects. Neither is a scan defect; both need an owner calibration ruling.
4. **No field corpus exists**, so the second and independent acceptance in §3.3 — the only one that
   measures the parser against input nobody arranged — is **not yet measurable**. This was already recorded
   by the spec-writing session and is unchanged.
