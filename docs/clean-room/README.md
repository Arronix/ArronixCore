# Clean-room stage 1 — the sanitized specifications and the corpus generator

> **What this directory is.** The wall-crossing artifacts for the clean-room programme described in
> [`../design/clean-room-plan.md`](../design/clean-room-plan.md): the behavioral specifications for the
> release-naming surface that survives the quality-axes amendment, the refusal list of what was struck on
> the way across, and the synthesized corpus with its generation manifest and coverage measurement.
>
> **What it is not.** It is not an implementation, it is not a description of any expression, and it is not
> a translation of one. Every rule below is stated as a requirement on behavior — what a reader must claim,
> what it must refuse to claim, and the observable reason the requirement exists.

---

## 1. Contents

| Path | Step | Role that wrote it | Readable by the Implementer |
|---|---|---|---|
| `spec/S1-title-claim.md` | 1a | Spec writer | yes |
| `spec/S2-title-normalization.md` | 1a | Spec writer | yes |
| `spec/S3-edition-vocabulary.md` | 1a | Spec writer | yes |
| `spec/S4-dialect-guards.md` | 1a | Spec writer | yes |
| `spec/S5-negative-assertions.md` | 1a | Spec writer | yes |
| `spec-refusals.md` | 3 (partial — see §5) | Spec writer | yes |
| `corpus/corpus.jsonl` | 4b | Generator author | yes |
| `corpus/manifest.md` | 4b | Generator author | yes |
| `corpus/coverage-report.md` | 4b | Generator author | yes |
| `corpus/shape-classes.md` | 4b | Generator author | **no — derived material, see §4** |

`corpus/corpus.jsonl` is 8,099 cases and about 5 MB. It is machine-generated and deterministic in its
seed, so it is regenerable rather than precious: `corpus/manifest.md` carries the seed, the SHA-256 of the
output and the SHA-256 of every generator file, which is what makes a re-run checkable.

The generator itself is a working tool rather than a shipped artifact and lives outside the repository, in
this session's scratchpad at `corpus-generator/`. Six files: `grammar.py` for the vocabulary and the
invented names, `shapeclass.py` for the one classifier both corpora are measured with, `generate.py` for
the templates and the structural walk, `coverage.py` for the measurement, `gate.py` for the sanitization
gate of §4, and `manifest.py` for the self-checks. If the owner wants it in the tree it should be moved
deliberately and re-homed, not copied — see §5 on which side of the wall it was written on.

---

## 2. Scope — which surface these specs cover, and which they deliberately do not

The clean-room plan's Part 1 inventory counts 42 class-(c) expressions in
`src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs`: one edition alternation, eleven title patterns,
twenty-nine guards and one stop-word expression. `quality-axes.md` §6.3 states which of them survive the
axes conversion at all, and §1.4a states which guards remain as per-kind release-naming dialect once the
family reads typed evidence. This directory specifies exactly that residue:

**In scope.**

- The eleven ordered title patterns, together as one terminator-selection rule — `spec/S1-title-claim.md`.
- The stop-word, leading-article, transliteration and query-rewrite surface — `spec/S2-title-normalization.md`.
- The edition alternation, in both of its jobs: as a title terminator and as the source of the `edition`
  facet — `spec/S3-edition-vocabulary.md`.
- The three guard classes §1.4a leaves as dialect: the fansub bracket conventions `anime-bd` and
  `anime-web`, the whole-disc probe `br-disk`, and the legacy scene resolution spellings `hr-ws` and the
  four `text-` guards — `spec/S4-dialect-guards.md`.

**Out of scope, with the reason.** Twenty-one of the twenty-nine guards are not specified here because the
amendment does not leave them in the kind:

- `german-remux`, `mpeg2`, `container-web`, `container-disc` and `container-dvd` become family-level rules
  over typed evidence — the `Languages`, `Codec` and `Container` readings — per §6.3. A spec for them here
  would specify a thing that is being deleted.
- `px-848x480`, `px-1280x720`, `px-1920x1080` become a host scanner under QA-17. Pixel-pair spellings are
  host vocabulary, not a kind's dialect.
- `raw-hd`, `xvid-divx`, `x264-alone`, `bracket-webdl`, `bracket-hdtv`, `word-dvd`, `word-bluray`, the three
  unseparated Blu-ray spellings, `hdtv-spaced` and `sdtv-spaced` are all host-scanner vocabulary: they name
  a source or a codec, which is what the shared token scan reads. They belong to the lexer specification,
  which is a different work item with a different owner.
- The rung-resolution table and the container fallbacks are deleted rather than respecified. §6.3 is the
  record of what each row becomes.

---

## 3. The provenance header every spec file carries

The plan's Part 6 requires the record to be contemporaneous and to state who could read what. Applied
per file, that is seven fields, and every file under `spec/` opens with them:

- **Spec id** — the stable prefix its rule ids carry. A gate failure names a rule id and nothing else, so
  the prefix is the routing address.
- **Step** — which of the plan's ordered steps produced it.
- **Role** — which role wrote it, and therefore what it was allowed to read.
- **Written** — the date.
- **Read to write it** — every input, by path. This is the field that makes the wall auditable.
- **Register** — the sanitization state, and which gate was run over the file.
- **Derived material** — whether the file inherits the status of anything quarantined, per S-C4.

---

## 4. The sanitization gate, stated so it is mechanical

The plan's control 2 requires the sanitized spec to carry zero regular-expression metacharacters, zero
named capture groups and zero verbatim alternation lists, and says a grep gate enforces the first two.
A literal reading of "zero metacharacters" over prose is not runnable — every sentence ends in a full stop,
which is a metacharacter. The gate is therefore stated exactly, and it is stricter than the loose reading
rather than weaker:

**Character rule.** No file under `spec/` may contain any of: backslash, open or close parenthesis, open or
close square bracket, open or close brace, vertical bar, caret, dollar, asterisk, plus, question mark.
Twelve characters, banned outright. This subsumes the named-group ban — a named group cannot be written
without a parenthesis — and it subsumes escape sequences, character classes, quantifiers, anchors and
alternation. It also costs the prose its parentheses and its markdown emphasis asterisks, which is a real
cost and is paid: emphasis is written with underscores and asides are written with commas or dashes.

**Ordering rule — the semantic half.** A banned vertical bar removes alternation syntax but not
_arrangement_, and S-C3 is explicit that arrangement is the copied thing. So every vocabulary list in a
spec file is introduced by a line ending in the words `alphabetical:` and the bullet run that follows must
be in ascending order. Sortedness is checked mechanically. This preserves the vocabulary, which is class
(a) fact, and destroys the ordering, which is not — an alphabetized list cannot carry someone else's
alternation order because it carries no order of its own.

**Justification rule — the other semantic half.** Every ordering constraint and every ambiguity case in a
spec file carries a functional justification that stands without reference to any other implementation.
The register is a stated observable reason, not an appeal to what some other program does. A constraint
that could not be given one is not in the spec: it is in `spec-refusals.md` with the reason it was struck
and, where one was available, the independently justified rule that replaced it.

**The gate is runnable.** `corpus-generator/gate.py` implements all three checks and prints one line per
file. Its result at the time of writing is recorded in `spec-refusals.md` §4.

**Files the gate does not cover.** This README and `spec-refusals.md` are meta-documents: they have to be
able to name the banned characters and to describe what was struck, so holding them to the character rule
would make them unwritable. They carry no rule that an implementation reads.

---

## 5. Where the plan's discipline was not fully satisfiable, stated rather than glossed

Three items. Each is a matter for the owner, and none is silently resolved.

1. **This session held two roles.** The plan's §3.1 says plainly that no session holds two roles, and the
   assignment that produced this directory asked one session to be both the spec writer, which is the
   Surveyor and is on the dirty side of the wall, and the author of the corpus generator, which Step 4b
   assigns to the Implementer and which is on the clean side. The generator was therefore written by a
   role that had read the material the Implementer may not read. What was done about it is in §6 below;
   what it means is that **the generator must be treated as dirty-side work**: either a clean Implementer
   re-derives it from these spec files, or the conflict is accepted and recorded.
2. **The Spec Editor pass is still owed.** Step 3 puts a separate role at the wall whose whole job is to
   strike what the spec writer could not bring itself to strike. This directory contains a self-audit
   standing in for that pass — the refusal list is written by the same session that wrote the specs. A
   self-audit is weaker than a second reader and is not claimed to be equivalent.
3. **The field corpus does not exist yet.** The plan's control 2 wants each justification grounded in cases
   from our own field corpus, and §3.3's third acceptance is measured on field data. There is none: nothing
   has been gathered. Every justification here is therefore grounded in either a stated structural property
   of the naming convention or a synthesized case by id, and the third acceptance is recorded as
   **not yet measurable** rather than as passed.

---

## 6. What was done to keep the generator as clean as a two-role session allows

Stated as controls, so the owner can judge them rather than take a claim.

- **The generator reads the spec files and nothing else.** Its vocabulary tables, its shape templates and
  its negative classes are transcribed from `spec/`, and every one of its emitted cases carries the rule
  ids it exercises. A case that cites no rule is a generator bug and its self-check fails on one.
- **No emitted title reproduces a case from the superseded corpus.** The generator's own self-check
  compares every emitted title against the superseded corpus's rows and fails if any string matches, at
  either exact equality or after separator folding. The check is run and its result is in `corpus/manifest.md`.
- **The work titles and the group tokens are synthetic and are not scene names.** R-7 in the plan's Part 2
  classifies curated release-group name lists as class (b) and says they are to be re-derived from our own
  field observations. Since there are none yet, the generator uses invented group tokens that are not the
  names of real groups, and invented work titles. Nothing in the corpus is a curated list of anyone's.
- **The superseded corpus is reduced to shape classes only, never to rows or to cells.** The coverage
  measurement this stage publishes is against structural classes — see §7 — and the row-level reduction to
  `EvidenceCell` baselines that Step 1b calls for is **not** performed here and is not in this directory.
  `corpus/shape-classes.md` is nonetheless derived material under S-C4, is recorded as such, and is not
  readable by the Implementer.

---

## 7. The coverage measurement, and why it is against shape classes

§3.3 of the plan is emphatic that aiming at the superseded corpus's baseline is aiming at its selection,
which is the authorship R-4 classifies as class (c). The acceptance it asks for is coverage of a
cross-product we defined, with the baseline falling out as a floor.

The measurement published here is therefore deliberately coarser than the cell baseline. A **shape class**
is a tuple of nine structural properties of a release name, computed by the same classifier for both
corpora: the dominant separator, the kind of the leading segment, where the year sits, how many
bracket-delimited segments there are, whether a checksum run is present, whether a container extension is
present, whether a trailing group suffix is present, whether a season and episode marker is present, and
which malformation if any the name carries. A shape class says what _form_ a name takes. It says nothing
about which forms someone chose to collect, and it carries no expected value at all.

The numbers are in `corpus/coverage-report.md`.
