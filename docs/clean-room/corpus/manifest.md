# Generation manifest

> The corpus in this directory is machine-generated. This file is what makes the run
> reproducible and checkable without the generator being in the tree: the seed, the hash of
> every file that produced it, the hash of what came out, and the result of each self-check
> the clean-room README §6 claims.

## 1. The run

| | |
|---|---|
| Generated | 2026-08-17 |
| Step | 4b, corpus generator |
| Role | generator author — **see the role conflict recorded in the clean-room README §5** |
| Seed | `20260817` |
| Cases | **8099** |
| Cases of kind `adversarial` | 21 |
| Cases of kind `negative` | 62 |
| Cases of kind `normalization` | 13 |
| Cases of kind `positive` | 723 |
| Cases of kind `structural` | 7280 |
| Output | `corpus.jsonl`, one JSON object per line |
| SHA-256 of `corpus.jsonl` | `320bbbdb28bf58b3bd0ebeead02c33744c3c092ba335b5079cfcd578884b66fb` |

## 2. The generator

The generator is a working tool, not a shipped artifact, and lives outside the repository in
the session scratchpad at `corpus-generator/`. Its files and their hashes at the time of the
run:

| File | SHA-256 |
|---|---|
| `grammar.py` | `71a9749ae3172577be8046338cc858febf0895fbcf5d28a1071d2f15ce0b3b83` |
| `shapeclass.py` | `dfee768e2ad7a5d422a231632c985c9e87231e6716834a8b0e36d553365c0519` |
| `generate.py` | `e076798b61aa79e0403d3750d0a171b6bf96cf00d01ff8bd40c85410e4cc722f` |
| `coverage.py` | `87c80379972f6c96094ebed076857f17b460203abf9cd522e777401c638e6aaa` |
| `gate.py` | `8e670c63df68beb009f24980356f87277ebdbcd4fd8426a4786794d90e74360a` |
| `manifest.py` | `e6c7e95baac5ab132677370f4ed2ede8c6e059d35af22f202e8dae6deeda546c` |

## 3. Self-checks

| Check | Result |
|---|---|
| Determinism — a second run into a clean directory produces a byte-identical file | **pass** |
| No emitted title reproduces a superseded corpus row, at exact equality | **pass**, 249 rows compared |
| No emitted title reproduces one after folding case and separators away | **pass** |
| Every case cites at least one spec rule id | **pass** |
| All nineteen declared negative classes are exercised | **pass** |
| No case cites a negative class the spec does not declare | **pass** |

Overall: **all self-checks pass**.

## 4. The structural walk, measured rather than asserted

The walk enumerates target shape classes from the nine structural dimensions and renders a
name for each. A rendered name is then classified again by the same classifier, and the two
are compared. A target that does not come back as itself is not a failure — it is a
combination that a release name cannot actually take, discovered by rendering rather than by
being argued about in advance.

| | |
|---|---:|
| Targets enumerated after the realizability filter | 7280 |
| Targets that rendered as the shape they declared | 5856 |
| Realization rate | 80.4 per cent |

The 1424 unrealized targets are recorded rather than deleted: each is a combination the
realizability rules of the generator admit and the dialect does not, and each is therefore a
candidate rule for a later pass. None of them is a gap in coverage — every one of them
rendered a name, and that name landed in some other class, which is why the walk still
reaches every class in the coverage report.

## 5. What this corpus does not contain, deliberately

- **No row of the superseded corpus, in any form.** The self-check in §3 is the evidence.
- **No real release-group name and no real work title.** The plan's R-7 classifies curated
  group-name lists as borderline material to be re-derived from our own field observations.
  There are none yet, so every group token and every work title here is invented.
- **No field-observed row.** The field corpus does not exist yet. The plan's third acceptance
  is measured on field data and is therefore recorded as not yet measurable, not as passed.
- **No expected rung, tier or ladder name.** The cell is stated in the axes vocabulary, per
  S-C10, and there is nothing in this corpus that a ladder would have to be reintroduced to
  express.

## 6. Record shape

Each line of `corpus.jsonl` is one case:

- `CaseId` — stable within a seed.
- `Title` — the synthesized release name.
- `Source` — whether the name is a release name or a folder name, which S1 TP-12 distinguishes.
- `Kind` — positive, structural, adversarial or negative.
- `ShapeId` — the template that emitted it.
- `Shape` — the nine-part structural class, computed from the emitted name.
- `Target` — on structural-walk cases, the class the case was built to be.
- `Rules` — the spec rule ids the case exercises, so a failure names a paragraph.
- `Cell` — the evidence the case was built to carry, in the axes vocabulary.
- `Expects` — the title-side readings: title, year, and any tag the case carries.
- `MustNotClaim` — the negative classes the case asserts, per S5.

