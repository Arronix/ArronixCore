# S5 — what a reader must refuse to claim

- __Spec id__: NEG. Rule ids NEG-1 through NEG-19.
- __Step__: 1a, survey to spec.
- __Role__: spec writer, dirty side of the wall.
- __Written__: 2026-08-17.
- __Read to write it__: `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs`;
  `src/Arronix.Plugin.Movies/Definition/MoviesCorpus.cs`; `docs/design/clean-room-plan.md` section 3.3;
  `docs/design/quality-axes.md`.
- __Register__: sanitized. The character rule, the ordering rule and the justification rule of the
  clean-room README §4 were run over this file and pass.
- __Derived material__: none. The __taxonomy__ below is derived from behavior, not from any row: it is
  the set of ways the rules in S1 through S4 can over-fire, enumerated by walking those rules. No case
  from the superseded corpus is reproduced, and the generator's self-check proves it — see
  `../corpus/manifest.md`.

---

## 0. Why this file exists, and why it is a hard gate

A reader that claims everything scores perfectly against a corpus of well-formed positives. It is also
useless, and worse than useless: a false claim promotes an unrelated file into the library under a work's
name, while a missing claim leaves an item unread and visible. The failure modes are not symmetric, so the
acceptance is not symmetric either. __Every class below is exercised, and every one of them passes, or the
corpus gate fails.__ There is no partial credit on a refusal: either the reader declines to claim, or it
does not.

Each class states the __shape__ that provokes the over-claim and the __refusal__ that is required. A
generated case cites the class it exercises, so a failure names a class and the class names the rule.

---

## 1. Title-side refusals

__NEG-1. A four-digit run that is the whole title is not the year.__ Shape: a name opening with a
year-shaped run followed by a second one. Refusal: the first run is title text and the year, if any, is
the later run. Rule: TP-3.

__NEG-2. A width-by-height pixel pair yields no year.__ Shape: two numbers around a lower-case x, where
the width falls inside the year band. Refusal: neither side is a year. Rule: TP-2.

__NEG-3. A year-shaped run carrying a scan-type letter is not a year.__ Shape: four digits followed
directly by the lower-case p or i. Refusal: no year from that run. Rule: TP-2.

__NEG-4. A leading parenthesised numeral is title text, not a tag and not a year.__ Shape: a name opening
with a round-bracketed number and continuing with title words. Refusal: the segment is kept in the title
span and is not read as the year. Rules: TP-6, TP-4.

__NEG-5. The leading letter of an initialism is not an article.__ Shape: a title opening with single
letters joined by full stops, the first of which is a letter that is also an article. Refusal: no article
is stripped and the stops are kept. Rules: TP-14, NM-5.

__NEG-6. A tracker host stamp is not the title.__ Shape: a name opening with a dot-joined host name and a
delimiter. Refusal: the title starts after the stamp, and the stamp appears in no emitted field. Rule:
TP-10.

__NEG-7. A checksum run is not title text, not a year and not a resolution.__ Shape: an eight-character
hexadecimal run inside a bracket-delimited segment, positioned where a reader might take it for something
else. Refusal: it is emitted as the checksum tag and as nothing else. Rule: TP-7.

__NEG-8. A re-issue version marker is not title text.__ Shape: a standalone v-plus-digits token directly
after the title. Refusal: the title ends before it. Rule: TP-8.

__NEG-9. A trailing group suffix is not title text.__ Shape: a name ending in a dash and a group token.
Refusal: the suffix is outside the title span. Rule: TP-15.

__NEG-10. A language name that is the last word of the title is not a marker.__ Shape: a title whose final
word is a language name, followed immediately by a year with no description token between. Refusal: the
title keeps the word and the year is the terminator. Rule: TP-9.

__NEG-11. A language name that is a marker is not title text.__ Shape: a name with no year before the
marker and description tokens after it. Refusal: the title ends at the marker. Rule: TP-9. This class and
NEG-10 are one test, not two: a reader that passes either alone can be passing it by refusing to fire at
all, or by always firing. Both are generated, and both must pass.

__NEG-12. Normalization never yields an empty key.__ Shape: a title consisting entirely of stop words, or
entirely of characters the folds remove. Refusal: the key is the unnormalized title. Rules: NM-4, NM-9.

__NEG-13. An edition qualifier that is an ordinary word inside a title is not an edition.__ Shape: a
qualifier appearing as a substring of a longer word, or without its word boundaries. Refusal: no edition
tag, and the title is not terminated there. Rule: ED-6.

---

## 2. Evidence-side refusals

__NEG-14. A name carrying no source vocabulary claims no origin.__ Shape: a title and an episode marker,
or a title and nothing else. Refusal: the origin reading is absent. Absent is the correct answer and it is
representable, so nothing is guessed. Rule: the axes model's treatment of an unread axis, and DG-1.

__NEG-15. A codec name alone claims no origin.__ Shape: a name whose only technical token is a codec.
Refusal: absent origin. This is a __deliberate loss__ against the superseded behavior, which inferred a
broadcast origin from a bare codec name: an encode carrying only a codec name is at least as likely to
have come from a stream as from a broadcast, so the inference had no justification and is struck. Recorded
in `../spec-refusals.md` as R-DG-2 and in `quality-axes.md` §6.3 as a stated loss.

__NEG-16. A container extension alone claims no resolution.__ Shape: a name with a container extension and
no line count anywhere. Refusal: absent resolution. Reason: a container is a wrapper and carries no
line count, so a resolution inferred from one is a guess, and an unknown reading is now representable.

__NEG-17. A two-letter disc abbreviation outside the bracket dialect claims no origin.__ Shape: the two
letters occurring inside a title word or as an initialism component, without the delimiters DG-7 requires.
Refusal: absent origin. Rule: DG-7.

__NEG-18. A disc medium name plus a re-encode token claims no disc-image packaging.__ Shape: a release
naming the disc it came from and also naming a rip, a consumer container or a re-encoding codec. Refusal:
packaging is not disc image. Rule: DG-10.

__NEG-19. A disc medium name with no artifact statement claims no disc-image packaging.__ Shape: a
release naming a disc medium and a bitstream codec, with no capacity label, no disc-image container, no
whole-disc word and no stereoscopic label. Refusal: packaging is not disc image. Rule: DG-9. This class
is the one the narrowing in DG-9 exists for, and it includes the case where the only disc word in the
name belongs to the work's own title.
