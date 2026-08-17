# Coverage report — the generated corpus against the superseded corpus's shape classes

> **Derived material, S-C4.** The shape-class inventory of the superseded corpus below is
> reduced from that corpus and inherits its status. It is a measuring instrument, it is not
> readable by the Implementer, and no row of the superseded corpus appears in this directory
> in any form. The reduction was run in memory and the rows were discarded; only class keys
> and counts were written.

A **shape class** is the nine-part structural tuple defined in the clean-room README §7,
computed by one classifier for both corpora. It records what form a name takes. It carries no
expected value and no vocabulary judgement, which is what makes it a legitimate coverage
target where a cell baseline would not be: covering it cannot mean reproducing anyone's
selection.

## 1. The headline numbers

| | Superseded corpus | Generated corpus |
|---|---:|---:|
| Rows | 249 | 8099 |
| Distinct shape classes | 51 | 6333 |
| Shape classes shared | 51 | 51 |
| Shape classes reached only by this one | 0 | 6282 |

**Shape-class coverage: 51 of 51, 100.0 per cent.** New classes reached that the superseded
corpus never exercised: **6282**.

Generated rows by kind: adversarial 21, negative 62, normalization 13, positive 723, structural 7280.

## 2. Shape classes the superseded corpus reaches and the generator does not

None.

## 3. Axis-value coverage of the generated corpus

Every value the generator was built to emit, and how many distinct values of each field the
corpus actually contains. A field with one value is a field the corpus does not exercise.

| Cell field | Distinct values | Values |
|---|---:|---|
| `Corrections` | 3 | 0, 1, 2 |
| `Generation` | 3 | 0, 1, 2 |
| `Origin` | 10 | Broadcast, BroadcastBitstream, CameraCapture, FilmPrint, HighDefinitionDisc, HighDefinitionDiscBitstream, StandardDefinitionDisc, StandardDefinitionDiscBitstream, Stream, Workprint |
| `Packaging` | 3 | DiscFolder, DiscImage, SingleFile |
| `ResolutionHeight` | 7 | 1080, 2160, 360, 480, 540, 576, 720 |
| `Separator` | 5 | dot, hyphen, mixed, space, underscore |
| `ShapeId` | 150 | adjacent-year-pair, adversarial-doubled-separator, adversarial-missing-separator, adversarial-mixed-separator, adversarial-source-word-in-title, adversarial-trailing-punctuation, adversarial-transliterated-title, bracket-suffix-no-year, bracketed-resolution, checksum-leading, codec-only, container-only, … |
| `VideoCodec` | 8 | Av1, H264, H265, H266, Mpeg2, Mpeg4Part2, Vc1, Vp9 |

## 4. Negative-assertion coverage

The plan gates negatives at 100 per cent: every class exercised, every one passing. This
table is the first half — that the corpus exercises them. The second half is the
Implementer's to satisfy.

| Negative class | Cases exercising it |
|---|---:|
| `NEG-1` | 2641 |
| `NEG-10` | 3 |
| `NEG-11` | 75 |
| `NEG-12` | 5 |
| `NEG-13` | 3 |
| `NEG-14` | 712 |
| `NEG-15` | 3 |
| `NEG-16` | 4 |
| `NEG-17` | 3 |
| `NEG-18` | 4 |
| `NEG-19` | 4 |
| `NEG-2` | 3 |
| `NEG-3` | 3 |
| `NEG-4` | 1634 |
| `NEG-5` | 3 |
| `NEG-6` | 1394 |
| `NEG-7` | 2332 |
| `NEG-8` | 10 |
| `NEG-9` | 1904 |

Classes declared in `../spec/S5-negative-assertions.md`: NEG-1 through NEG-19. Classes
exercised here: **19 of 19**.

## 5. Rule coverage

Every case cites the spec rules it exercises, so a failure names a paragraph. A rule with no
case is a rule nothing tests.

| Rule | Cases |
|---|---:|
| `DG-1` | 47 |
| `DG-3` | 35 |
| `DG-4` | 4 |
| `DG-5` | 18 |
| `DG-6` | 20 |
| `DG-7` | 58 |
| `DG-8` | 859 |
| `DG-9` | 863 |
| `DG-10` | 4 |
| `DG-11` | 19 |
| `DG-12` | 6 |
| `DG-13` | 3 |
| `DG-14` | 1859 |
| `ED-1` | 65 |
| `ED-2` | 18 |
| `ED-3` | 18 |
| `ED-4` | 8 |
| `ED-5` | 4 |
| `ED-6` | 25 |
| `ED-7` | 4 |
| `ED-8` | 3 |
| `ED-9` | 65 |
| `NM-1` | 13 |
| `NM-2` | 381 |
| `NM-3` | 2 |
| `NM-4` | 5 |
| `NM-5` | 4 |
| `NM-6` | 6 |
| `NM-7` | 5 |
| `NM-8` | 2 |
| `NM-9` | 5 |
| `NM-10` | 3 |
| `NM-11` | 3 |
| `TP-1` | 8008 |
| `TP-2` | 6123 |
| `TP-3` | 2641 |
| `TP-4` | 1899 |
| `TP-5` | 1059 |
| `TP-6` | 1634 |
| `TP-7` | 2334 |
| `TP-8` | 10 |
| `TP-9` | 82 |
| `TP-10` | 1394 |
| `TP-11` | 3 |
| `TP-12` | 424 |
| `TP-13` | 7749 |
| `TP-14` | 3 |
| `TP-15` | 1916 |
| `TP-16` | 3 |
| `UA-1` | 2 |

Declared rule ids across the five spec files: **72**. Cited by at least one case: **69**.

Declared and not cited by any case: `DG-2`, `INV-1`, `INV-2`.
Each of these states what a thing _is_ — an invariant, or the description of a dialect —
rather than what a reader must _do_ with one, so there is nothing for a case to assert.
They are listed rather than quietly excluded.

## 6. Shape templates

| Template | Cases |
|---|---:|
| `adjacent-year-pair` | 2 |
| `adversarial-doubled-separator` | 4 |
| `adversarial-missing-separator` | 4 |
| `adversarial-mixed-separator` | 4 |
| `adversarial-source-word-in-title` | 2 |
| `adversarial-trailing-punctuation` | 4 |
| `adversarial-transliterated-title` | 3 |
| `bracket-suffix-no-year` | 3 |
| `bracketed-resolution` | 4 |
| `checksum-leading` | 3 |
| `codec-only` | 3 |
| `container-only` | 4 |
| `declared-ambiguity` | 2 |
| `disc-abbreviation-substring` | 3 |
| `disc-folder` | 4 |
| `disc-image-capacity` | 15 |
| `disc-image-complete` | 6 |
| `disc-image-standard-definition` | 2 |
| `disc-medium-no-artifact` | 1 |
| `disc-word-in-title` | 3 |
| `disc-word-with-consumer-container` | 1 |
| `disc-word-with-rip` | 3 |
| `dynamic-range` | 8 |
| `edition-after-year` | 14 |
| `edition-anniversary` | 4 |
| `edition-before-year` | 14 |
| `edition-combined-cuts` | 3 |
| `edition-qualifier-head` | 18 |
| `edition-stacked` | 4 |
| `edition-standalone` | 8 |
| `edition-substring` | 3 |
| `fansub-prefix-hash` | 22 |
| `fansub-prefix-year` | 18 |
| `fansub-versioned` | 8 |
| `folder-year-first` | 16 |
| `group-suffix` | 4 |
| `initialism-title` | 3 |
| `language-final-title` | 3 |
| `language-marker-negative` | 3 |
| `language-marker-no-year` | 72 |
| `leading-parenthetical-title` | 2 |
| `legacy-high-resolution-label` | 3 |
| `no-evidence` | 3 |
| `normalization` | 13 |
| `numeric-title` | 3 |
| `paren-year` | 77 |
| `pixel-pair` | 3 |
| `resolution-not-year` | 3 |
| `revision` | 12 |
| `scene-title-year-evidence` | 380 |
| `stop-word-title` | 3 |
| `tracker-stamp` | 8 |
| `tracker-stamp-negative` | 2 |
| `version-marker` | 2 |
| `walk-dot-bracket-adjacent-pair` | 58 |
| `walk-dot-bracket-after-title` | 58 |
| `walk-dot-bracket-bracketed` | 58 |
| `walk-dot-bracket-none` | 58 |
| `walk-dot-host-adjacent-pair` | 94 |
| `walk-dot-host-after-title` | 94 |
| `walk-dot-host-bracketed` | 94 |
| `walk-dot-host-none` | 94 |
| `walk-dot-numeric-adjacent-pair` | 94 |
| `walk-dot-numeric-after-title` | 94 |
| `walk-dot-numeric-bracketed` | 94 |
| `walk-dot-paren-adjacent-pair` | 94 |
| `walk-dot-paren-after-title` | 94 |
| `walk-dot-paren-bracketed` | 94 |
| `walk-dot-paren-none` | 94 |
| `walk-dot-word-adjacent-pair` | 94 |
| `walk-dot-word-after-title` | 94 |
| `walk-dot-word-bracketed` | 94 |
| `walk-dot-word-lead` | 94 |
| `walk-dot-word-none` | 94 |
| `walk-hyphen-bracket-adjacent-pair` | 38 |
| `walk-hyphen-bracket-after-title` | 38 |
| `walk-hyphen-bracket-bracketed` | 38 |
| `walk-hyphen-bracket-none` | 38 |
| `walk-hyphen-host-adjacent-pair` | 64 |
| `walk-hyphen-host-after-title` | 64 |
| `walk-hyphen-host-bracketed` | 64 |
| `walk-hyphen-host-none` | 64 |
| `walk-hyphen-numeric-adjacent-pair` | 64 |
| `walk-hyphen-numeric-after-title` | 64 |
| `walk-hyphen-numeric-bracketed` | 64 |
| `walk-hyphen-paren-adjacent-pair` | 64 |
| `walk-hyphen-paren-after-title` | 64 |
| `walk-hyphen-paren-bracketed` | 64 |
| `walk-hyphen-paren-none` | 64 |
| `walk-hyphen-word-adjacent-pair` | 64 |
| `walk-hyphen-word-after-title` | 64 |
| `walk-hyphen-word-bracketed` | 64 |
| `walk-hyphen-word-lead` | 64 |
| `walk-hyphen-word-none` | 64 |
| `walk-mixed-bracket-adjacent-pair` | 38 |
| `walk-mixed-bracket-after-title` | 38 |
| `walk-mixed-bracket-bracketed` | 38 |
| `walk-mixed-bracket-none` | 38 |
| `walk-mixed-numeric-adjacent-pair` | 62 |
| `walk-mixed-numeric-after-title` | 62 |
| `walk-mixed-numeric-bracketed` | 62 |
| `walk-mixed-paren-adjacent-pair` | 62 |
| `walk-mixed-paren-after-title` | 62 |
| `walk-mixed-paren-bracketed` | 62 |
| `walk-mixed-paren-none` | 62 |
| `walk-mixed-word-adjacent-pair` | 62 |
| `walk-mixed-word-after-title` | 62 |
| `walk-mixed-word-bracketed` | 62 |
| `walk-mixed-word-lead` | 62 |
| `walk-mixed-word-none` | 62 |
| `walk-space-bracket-adjacent-pair` | 58 |
| `walk-space-bracket-after-title` | 58 |
| `walk-space-bracket-bracketed` | 58 |
| `walk-space-bracket-none` | 58 |
| `walk-space-host-adjacent-pair` | 94 |
| `walk-space-host-after-title` | 94 |
| `walk-space-host-bracketed` | 94 |
| `walk-space-host-none` | 94 |
| `walk-space-numeric-adjacent-pair` | 94 |
| `walk-space-numeric-after-title` | 94 |
| `walk-space-numeric-bracketed` | 94 |
| `walk-space-paren-adjacent-pair` | 94 |
| `walk-space-paren-after-title` | 94 |
| `walk-space-paren-bracketed` | 94 |
| `walk-space-paren-none` | 94 |
| `walk-space-word-adjacent-pair` | 94 |
| `walk-space-word-after-title` | 94 |
| `walk-space-word-bracketed` | 94 |
| `walk-space-word-lead` | 94 |
| `walk-space-word-none` | 94 |
| `walk-underscore-bracket-adjacent-pair` | 58 |
| `walk-underscore-bracket-after-title` | 58 |
| `walk-underscore-bracket-bracketed` | 58 |
| `walk-underscore-bracket-none` | 58 |
| `walk-underscore-host-adjacent-pair` | 94 |
| `walk-underscore-host-after-title` | 94 |
| `walk-underscore-host-bracketed` | 94 |
| `walk-underscore-host-none` | 94 |
| `walk-underscore-numeric-adjacent-pair` | 94 |
| `walk-underscore-numeric-after-title` | 94 |
| `walk-underscore-numeric-bracketed` | 94 |
| `walk-underscore-paren-adjacent-pair` | 94 |
| `walk-underscore-paren-after-title` | 94 |
| `walk-underscore-paren-bracketed` | 94 |
| `walk-underscore-paren-none` | 94 |
| `walk-underscore-word-adjacent-pair` | 94 |
| `walk-underscore-word-after-title` | 94 |
| `walk-underscore-word-bracketed` | 94 |
| `walk-underscore-word-lead` | 94 |
| `walk-underscore-word-none` | 94 |

