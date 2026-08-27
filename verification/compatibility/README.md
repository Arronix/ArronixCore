# Compatibility evidence ledger

This directory records compatibility requirements independently of the NUnit names which currently exercise
them. It is the G01 omission baseline: it makes every known skipped case visible without claiming that the
current regression material is complete, independently authored, or production proof.

## Canonical files

- `baseline.json` identifies the two NUnit captures, records their immutable aggregate counts, and carries the
  monotonic current skip count.
- `sources.jsonl` records evidence provenance and whether a source may satisfy a compatibility claim.
- `requirements.jsonl` is the semantic inventory. Requirements remain stable when tests move or are renamed.
- `cases.jsonl` binds the 302 executed baseline skips to requirements, their published NUnit identities, their
  CLR method source, and any additional compiled support documents used by a data-driven witness.
- `replacements.jsonl` is intentionally empty at G01. A later replacement must be explicit here.
- `schema/` contains the JSON Schema 2020-12 contract for each record type.
- `../ci/required-tests.tsv` is the eight-column, append-only registry of durable passing sentinels which guard
  the proof mechanism rather than transient product outcomes.

JSONL files are UTF-8, contain exactly one compact JSON object per line, have no blank lines, and are sorted
by their primary identifier. IDs, published bindings, expected semantics, and source digests are immutable
after publication. Moving or rewriting a test requires a new stable case and an explicit replacement edge;
editing the current ledger alongside the test does not rewrite the prior committed contract.

Existing corpus IDs such as `t012` are retained. Other baseline rows receive an opaque `cNNN` suffix within
their semantic requirement. That suffix is an assigned identity, not a live ordinal: adding, moving, or
renaming a test never renumbers it.

The four published Movie-title rows `t012`, `t013`, `t023`, and `t027` live in
`MovieTitleCompatibilityCorpus.cs`, an immutable support document separate from the ordinary representative
`MovieTitleCorpus.cs`. Routine additions or corrections to representative parser coverage therefore cannot
silently rewrite those four ledger bindings.

## Baseline truth

The captured Movies run contains 663 cases: 362 passed and 301 skipped. The captured Architecture run contains
178 cases: 177 passed and one skipped. The ledger therefore contains exactly 302 case records. The 301 Movies
skips come from 118 `[Ignore]` declarations which expand to 292 parameterized cases plus nine runtime
`Assert.Ignore` results. Counting attributes is not a valid substitute for consuming the completed test run.

`currentSkipCount` must equal the fresh full-rail result exactly. A lower observed count fails until the value is
reduced in the ledger, which commits the improvement; comparison with the prior ledger then forbids raising it.

The checkout digest is necessary but not sufficient source proof. Under pinned .NET 11 Preview 7 SDK
`11.0.100-preview.7.26381.103`, the proof rail performs one non-incremental solution build and retains its binlog
as the authoritative practical record of the actual `Csc` invocations:
their final source inputs, embedded inputs, warning policy, and build configuration. Registered primary and
support files must be actual compile inputs of their declared test project; manual embedded inputs and source
remapping directives are rejected.

For every registered case, the validator selects the exact NUnit leaf by its published full-name digest and
requires that leaf to belong to the declared fixture method. It opens NUnit's exact executed assembly path,
requires that path to be the declared test project's output, and resolves exactly one declared CLR method. The
matching associated Portable PDB must bind the method's visible sequence points to the locked primary source and
must contain exact embedded source bytes matching that repository file. Async and iterator bodies are followed
from their generated state machine back to the declared method. Data-driven cases may lock support documents;
each must also be a compile input and have byte-identical embedded source in the PDB.

The required passing sentinels use the same layered path. Their eight-column registry records stable ID,
project, exact NUnit full name, fixture, method, primary source, primary digest, and optional support sources. It
is append-only across revisions and contains three durable G01 invariants; transient outcomes do not qualify for
permanent sentinel status. These checks establish practical provenance for the one recorded build and test run.
They do not claim cryptographic attestation, a trusted compiler, or hermetic causal proof.

Five zero-case requirements keep absent evidence visible:

- a revision-pinned, independently audited upstream feature inventory;
- an independently regenerated clean-room corpus;
- a non-empty, bounded Scene/PTP field corpus;
- supported-platform and filesystem coverage; and
- the currently empty set of owner-approved Arronix divergences.

The zero approved-divergence count does not turn candidate differences into approvals. Candidate divergence,
unknown/lost expected behaviour, unclassified ownership, and missing evidence all continue to count as missing
proof.

## Provenance wall

`source.r00.movies-regressions` is eligible only as an omission baseline. Its upstream provenance has not been
pinned and some rows are known ports. Passing one of those cases later does not by itself establish clean-room
or source-*arr parity.

The 2026-08-17 generated corpus remains ineligible: one session held dirty-side and generator roles, the
independent Spec Editor pass is absent, the generator is not checked in, and its schema describes a removed
quality model. The shape inventory and historical differential/similarity reports remain derived or
Gatekeeper-only material. They must not be read as current executable proof. No field corpus currently exists.

`proofUse` is therefore a hard boundary:

- `eligible` evidence may satisfy the stated requirement;
- `baseline-only` evidence may prove that an omission has not disappeared, but may not establish parity; and
- `ineligible` evidence may be inventoried but may not close a requirement.

Closure-eligible evidence must also identify a repository-relative artifact at its pinned revision, reconcile
its declared case count with its executable cases, and satisfy the evidence class's provenance rules. Ordinary
compatibility evidence must be current, independently produced, normally accessible, and revision-pinned.
Architecture governance has a separate non-media rule; an owner-decision record is authority for a disposition,
not an executable compatibility witness.

## Replacement and ratchet rules

Each replacement record has exactly one `fromCaseId`; topology and semantic outcome are separate fields.
`one-to-one` has exactly one target. `partition` registers at least two targets and requires a pinned owner
decision, but it cannot reach `verified` until an aggregate semantic-composition contract exists; distinct opaque
digests alone do not prove that several cases jointly cover the source. The outcome must match the source case and requirement disposition: `equivalent`, `ownership-correct`,
`evidence-recovered`, `approved-divergence`, or `scope-correction`. Equivalent and known one-to-one recovery
preserve the source semantic digest. Ownership correction moves to a different assigned owner; approved
divergence and scope correction move to a different requirement and change the locked semantics. Recovering an
unknown lost expectation requires explicit review. An owner may instead resolve a scope-correction candidate by
retaining its locked semantics through an equivalent replacement and the same prior, pinned decision process.

Every target must be an introduced, permanent executable case. It closes an edge either by passing from its
locked, provenance-eligible source or by being closed through its own verified, acyclic replacement. Closure is
evaluated to a fixed point, so an unchanged `A -> B -> C` history remains valid after both `A` and `B` stop
executing. A replacement, its targets, their evidence, and any required owner-decision source must already exist
unchanged in the prior committed ledger before `verified` status can retire the source case; review and closure
therefore cannot be manufactured together in one change. A decision reference resolves to a pinned, current
`arronix-owner-decision` source attached to the source requirement, not an arbitrary or unrelated string. `partial`, `candidate`, merely documented,
provenance-ineligible, cyclic, or disposition-mismatched replacements do not retire the source case. Test output
cannot self-attest semantic proof by echoing a digest supplied by the ledger. Replacement-witness is an
introduction role, not a semantic disposition, so a permanent target can itself be replaced later without
changing its published identity; an evidence-recovery witness may therefore gain an equivalent successor while
remaining attached to its original evidence-gap requirement.

A validator consuming these files and fresh test results must reject:

- a skipped result with no registered case;
- more than 302 total skips;
- a fresh skip count different from the committed current count, or an increase from the prior ledger;
- disappearance of a registered case, even when the total skip count falls;
- a changed NUnit binding, expected row, disposition, or source-file digest relative to the prior ledger;
- a rewritten requirement, provenance source, baseline, or replacement graph under an existing stable ID;
- a newly skipped, renamed, removed, or vacuously weakened fixture;
- a declared primary or support source which is not an actual compiler input of its test project, an unapproved
  embedded input, a source-remapping directive, or compiler evidence from a different build;
- an executed fixture or sentinel whose exact NUnit leaf, declared method, executed assembly, associated PDB,
  primary source, support document, or embedded source bytes do not match the locked compiled provenance;
- removal or rebinding of a published required proof sentinel;
- a replacement whose target is unresolved, skipped, failed, provenance-ineligible, partial, cyclic, or reused;
  and
- any report which counts a candidate divergence or an ineligible source as compatibility proof.

The CI wrapper materializes the ledger from the PR base or pre-push revision and passes it as the historical
ratchet input. Local dirty-ledger runs compare with `HEAD`; clean runs compare with `HEAD^`. The first G01
publication is the bootstrap ledger and necessarily has no older ledger to compare.

The compatibility matrix is generated by joining requirements, cases, sources, replacements, prior committed
state, and fresh results. Do not maintain a second hand-authored status table.

## Generated classification inventory

`Arronix.Compatibility.Ratchet validate --classification-report <file>` emits a stable, versioned JSON inventory
under the CI compatibility artifact directory. It projects declared ledger facts and observed skip counts only:
requirement ownership/disposition/gates, zero-case controls, and each attached source's exact proof-use, artifact,
and evidence-class status. It is not a compatibility matrix, parity claim, approval, closure decision, or proof;
baseline-only and ineligible sources, candidate divergences, and provisional or unresolved owners remain explicitly
classified as such.
