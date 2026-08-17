# S1 — reading a release name into a work title, a year, and title-side tags

- __Spec id__: TP. Rule ids in this file are TP-1 through TP-16, plus the invariants INV-1 and INV-2 and
  the declared unresolvable ambiguity UA-1. Ids are stable: a gate failure names one and nothing else.
- __Step__: 1a, survey to spec.
- __Role__: spec writer, dirty side of the wall.
- __Written__: 2026-08-17.
- __Read to write it__: `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs`;
  `src/Arronix.Plugin.Movies/Definition/MoviesCorpus.cs`; `docs/design/clean-room-plan.md`;
  `docs/design/quality-axes.md`. No clone of any surveyed application was opened, and none is on this
  machine inside this repository.
- __Register__: sanitized. The character rule, the ordering rule and the justification rule of the
  clean-room README §4 were run over this file and pass.
- __Derived material__: none. Every rule below is stated from behavior and carries its own justification.
  What could not be justified is struck and is listed in `../spec-refusals.md`.

---

## 0. What this spec is for

A release name is one string that carries two unrelated things: the name of a work, and a description of
the file. A reader has to split them. Everything downstream depends on the split — the catalogue lookup
uses the left half and the quality reading uses the right half — and both halves fail badly when the split
is wrong, so the split is specified before either.

This spec states the split, the year, and the three title-side tags a movie release spells: a fansub
subgroup, a release checksum, and an edition. It states nothing about quality: the reader described here
never names a source, a resolution or a rung. What it produces is a title span, a year when the name
carries one, and tags.

---

## 1. The two invariants the whole split rests on

__INV-1. The work title comes first.__ A release name opens with the name of the work and continues with
facts about the file. Justification, and it is structural rather than borrowed: the name has to be sorted
and searched by the thing it is a copy of, and every distribution convention in circulation puts the work
first for that reason. A convention that put the codec first would be unusable in a directory listing.
Nothing downstream of this spec would work under the opposite invariant, and the invariant is testable —
a name that violates it is unreadable by any implementation, ours included.

__INV-2. The boundary is marked, not inferred.__ The point where the work title stops is marked by a
terminator: a token that belongs to the file description and cannot belong to a work title. A reader finds
the boundary by finding the first credible terminator, not by guessing where a title ought to end.
Justification: the alternative is a length heuristic or a dictionary of titles, and both fail on the first
work whose name contains a number or a technical word. The terminator kinds are TP-1.

---

## 2. Terminator kinds and their precedence

__TP-1. Six terminator kinds, tried in the order of how much independent evidence each needs.__ The
kinds, from most evidence to least:

1. __Bracket-prefix with checksum__ — a bracket-delimited segment at the very start __and__ an
   eight-character hexadecimal bracket-delimited run later in the name. Two independent signals.
2. __Edition then year__ — an edition phrase from S3 __and__ a year candidate after it. Two signals.
3. __Language marker with following evidence__ — a language name from the marker vocabulary, __no__ year
   candidate before it, __and__ at least one file-description token between the marker and the next year
   candidate or the end. Three conditions. TP-9.
4. __Year__ — a single year candidate meeting TP-2. One signal.
5. __Bracket suffix with no year anywhere__ — a bracket-delimited segment and no year candidate in the
   whole name. TP-11.
6. __Description run__ — no terminator of any kind was found, so the title is the whole name with the
   trailing file-description run removed. TP-16.

__The justification for this order is the ordering principle itself, and it is general.__ A rule that
fires only on a conjunction of independent signals is less likely to fire spuriously than a rule that
fires on one signal, because the probability that several unrelated signals all appear by accident in a
work title is much smaller than the probability that one does. Ordering by conjunction strength therefore
minimises false splits, and a false split is the expensive failure: it sends a wrong title to the
catalogue, which returns a wrong work, which files the release under the wrong thing. A missed split
leaves the title too long, the lookup fails, and the item stays visible and unfiled. Prefer the cheap
failure. This is the same asymmetry the clean-room plan states for the corpus at S-C5, applied one layer
earlier.

Two consequences worth stating, because they are testable:

- The order is derived, not declared. An implementation that computes the conjunction strength of each
  candidate terminator and takes the strongest reaches the same answers, and is a legitimate
  implementation of TP-1.
- Adding a seventh kind does not require re-deciding the order. It is placed by how many independent
  conditions it needs.

---

## 3. The year

__TP-2. A year candidate is a four-digit decimal run inside the plausible band, not adjacent to a
disqualifier.__

The band runs from 1800 through 2099. Justification, ours: the lower bound has to sit below the earliest
catalogued moving-image work, which is late in the nineteenth century, and works are sometimes dated by
the year of the source they adapt, so a margin below that is cheap. The upper bound has to sit above the
current year by enough to admit announced and pre-release works without needing a code change each decade.
Any band containing roughly 1880 through roughly 2040 satisfies both; the band is stated as a round two
centuries because a round number needs no maintenance and the failure mode at either end is a four-digit
number in a title being read as a year, which TP-3 already handles.

Disqualifiers, each with its own reason:

- __A vertical-resolution letter immediately after.__ A run followed directly by a lower-case p or i is a
  resolution spelling, not a year. Reason: the resolution vocabulary spells a line count followed by the
  scan-type letter, and the line counts 1080 and 1440 and 2160 fall inside or near the band. This is the
  single most common collision in the whole surface.
- __A digit immediately before or after.__ A four-digit run inside a longer digit run is part of that run.
  Reason: a longer number is a size, a bitrate, an identifier or a pixel dimension, and none of them is a
  year.
- __A pixel-pair neighbour.__ A run that is one side of a width-by-height pair is a dimension. Reason: the
  pair spelling puts two numbers around a lower-case x with no separator, and the width side of the common
  high-definition pair falls inside the band. Without this rule the pair 1920x1080 yields the year 1920.
- __An adjacent year candidate after it.__ When two candidates sit next to each other with only a
  separator between, the __later__ one is the year and the earlier one is title. Reason: this follows from
  INV-1 alone, and needs no separate judgment — the title precedes the year, so of two year-shaped runs in
  sequence the second is the one in the year position.

__TP-3. A name whose only year-shaped run is at the very start states a numeric title, not a bare year.__
When the strongest terminator would leave the title empty, and the name begins with a year-shaped run, the
run is the title and the reader looks for the next candidate. Justification: a reading that produces no
title is not a reading at all — nothing downstream can use it — so between two available readings the one
that yields a title wins. This is a general preference, stated once, and it also settles the numeric-title
family that a band-based year rule would otherwise eat.

__TP-4. A year candidate inside a bracket-delimited segment is still a year.__ Reason: several
distribution conventions parenthesise or bracket the year specifically to separate it from the title, so
refusing bracketed candidates would lose the cases where the packager was being _more_ explicit, not less.

---

## 4. Segments at the edges of the name

__TP-5. A square-bracket-delimited segment is never title text, wherever it sits.__ At the start it names
the distributing group; at the end it names a checksum, a tracker, or a run of technical facts. Reason:
the square bracket is the dialect's tag delimiter and is used for nothing else. A work title containing a
square bracket does not occur, because the character is not typographic and file systems and trackers
treat it as structure.

__TP-6. A round-bracket-delimited segment at the start of a name is title text when title words follow
it.__ Reason, and it is the difference from TP-5: round brackets are typographic. Titles genuinely open
with a parenthesised numeral or qualifier, and the dialect does not use round brackets to mark a
distributing group. So the leading round-bracket segment is kept, and the reader keeps looking for a
terminator after it.

__TP-7. An eight-character hexadecimal run inside a bracket-delimited segment is a file checksum.__ It is
recorded as a tag and is never title text, never a year, and never evidence about quality. Reason: eight
hexadecimal characters is the printed width of a thirty-two-bit cyclic redundancy check, which the fansub
conventions append so a downloader can verify the file. It is a fact about the encoding of the number, not
a judgment: any run of that shape in that position is that checksum.

__TP-8. A lower-case letter v followed by one or two digits, standing alone between separators, is a
re-issue version marker and terminates the title.__ Reason: the marker is release metadata — the same
work, re-encoded and re-published by the same group — and a work title does not carry a standalone
v-plus-digits token. The width bound of two digits is ours and is stated as a bound rather than a fact:
past ninety-nine re-issues the token stops being credible as a version, and a longer run is more likely an
abbreviation inside the title.

__TP-9. A language marker terminates the title only when file-description evidence follows it before the
next year candidate.__

This is the rule that replaces a struck construct, so the replacement is stated in full and the reason for
both halves is stated with it. A language name appears in release names in two entirely different jobs. It
can be the last word of the work's own title. It can be a scene marker announcing the audio the release
carries, in a naming convention that omits the year altogether and lets the marker do the terminator's job.
The reader has to tell them apart with no catalogue to consult.

The discriminator is what comes __after__ the marker:

- A marker followed, before the next year candidate or before the end of the name, by at least one token
  from the file-description vocabulary — a source name, a resolution spelling, a codec name, an audio
  format, a dual-audio marker — is doing the marker job. It sits inside the description run, so the title
  ended before it.
- A marker followed immediately by a year candidate, with no description token between, is the last word
  of the title. The year that follows is the terminator, and it is a stronger one.

Justification, without reference to any other implementation: a marker is a description token, and
description tokens cluster. The dialect writes the whole file description as one contiguous run at the end
of the name, so a token that belongs to that run has other members of the run beside it, and a token that
has only the year beside it is not in the run — it is the last word before it. That is a property of the
convention itself and is checkable on any name.

A second condition, from TP-1: the marker rule does not apply when a year candidate __precedes__ the
marker. Reason: the year is a stronger terminator and has already split the name, so the marker is inside
the description run by construction and there is nothing left for it to terminate.

__What was struck, and why.__ The superseded surface restricted this rule to two named language spellings
and additionally suppressed it after two named English words. Neither restriction has a functional
justification: both are lists of words that a particular collection of names happened to contain, which is
precisely the register S-C3 requires to be struck. The evidence-follows test replaces them and does the
same work through a stated property rather than through a word list, and it does it for every language in
the vocabulary rather than for two. The cost of lifting the restriction is UA-1.

__UA-1. Declared unresolvable ambiguity.__ When a work's title ends in a language name and the release
states no year at all, the evidence-follows test terminates the title one word early, and no rule over the
string alone can do better — the information needed is a catalogue of titles, which the reader does not
have. The chosen resolution and its cost:

- The reader emits the short title, because TP-1's asymmetry says the cheap failure is the one that leaves
  work for the layer above.
- The layer above is the catalogue lookup, which is where the ambiguity is actually resolvable: a lookup
  that fails on the short form retries with the marker word restored. This spec does not specify that
  retry — it belongs to the matching surface — but it records that the retry is the intended remedy, so
  the ambiguity is handed on deliberately rather than lost.

__TP-10. A leading host-name stamp is not the title.__ A name that opens with a dot-joined run whose first
label is the conventional web prefix, or whose last label is a public suffix, and which is followed by a
separator dash before the rest of the name, opens with a tracker's watermark. The stamp is dropped and the
title starts after it. Reason: trackers stamp their own domain onto the file name for advertising, always
at the front and always with a delimiter after it, and a host name is recognisable by structure alone
without a list of trackers. Stating it structurally is what keeps this rule from becoming a curated list
of tracker names, which would be borderline curated material with a maintenance burden and no justification.

__TP-11. When the name carries no year candidate at all, a bracket-delimited segment terminates the
title.__ Reason: at least one distribution convention names its files as the work title followed by a
bracketed tracker tag and nothing else. With no year anywhere, the bracket is the only boundary marker in
the name, and TP-5 already establishes that a bracket segment is not title text.

__TP-12. Folder names are read year-first as well as title-first.__ When the input is a directory name
rather than a release name, a leading year candidate followed by the rest of the name reads as the year
followed by the title. Reason, and it is why the rule is restricted to folders: a folder name is written
by a person organising a library and people sort libraries chronologically, so year-first is a library
convention; a release name is written by the packager and follows INV-1. Applying the folder reading to
release names would claim every release whose work title begins with a number, which TP-3 exists to
protect.

---

## 5. What the title span contains once it is found

__TP-13. The title span is the text before the terminator, with separator and punctuation runs trimmed
from its end only.__ Interior punctuation is kept. Reason: a colon, an exclamation mark, an apostrophe and
an interior full stop are all part of real titles and removing them loses information that the catalogue
lookup wants; a trailing run is an artifact of where the terminator sat and carries nothing.

__TP-14. A run of single letters joined by full stops is an initialism and keeps its stops.__ A letter
followed by a full stop, followed by another letter followed by a full stop, is one token. Reason: the
dialect uses the full stop both as a word separator and as the punctuation inside an initialism, and the
two are told apart by the width of what sits between the stops — single characters mean initialism. Two
things depend on this: the title keeps its stops rather than being re-spaced into single letters, and the
leading-article rule of S2 must not treat the first letter of an initialism as an article.

__TP-15. A trailing group suffix is not title text and is not read here.__ A name ends with a dash and the
name of the group that published it. It is out of this spec's scope — the group surface belongs to the
lexer — but it is stated because the terminator search must not mistake the dash for a separator inside a
title, and because TP-16 needs it.

__TP-16. With no terminator of any kind, the title is the name minus its trailing description run.__ The
description run is the maximal suffix consisting only of file-description tokens, separators and a group
suffix. Reason: with no boundary marker the only remaining evidence about where the title ends is which
tokens cannot be title words, and the description vocabulary is exactly that set. This is the last resort
and it is the weakest rule in the file; it is stated last because TP-1 tries it last.

---

## 6. What this reader emits

Per input, at most:

- the title span, per TP-13;
- the year, per TP-2, absent when the name states none — absent is a real answer and is not guessed;
- the subgroup tag, from a leading bracket segment per TP-5;
- the checksum tag, per TP-7;
- the edition tag, per S3.

__Absence is emitted as absence.__ A reader that cannot find a year says so. Nothing in this spec permits
inventing one, and the negative assertions of S5 pin that.
