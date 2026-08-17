# S4 — the three release-naming dialects a movie kind still has to read

- __Spec id__: DG. Rule ids DG-1 through DG-14.
- __Step__: 1a, survey to spec.
- __Role__: spec writer, dirty side of the wall.
- __Written__: 2026-08-17.
- __Read to write it__: `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs`;
  `src/Arronix.Plugin.Movies/Definition/MoviesCorpus.cs`; `docs/design/quality-axes.md` sections 1.4a,
  2.1, 6.3 and 10; `docs/design/clean-room-plan.md`.
- __Register__: sanitized. The character rule, the ordering rule and the justification rule of the
  clean-room README §4 were run over this file and pass.
- __Derived material__: none. Two constructions are struck and one inference is deliberately narrowed;
  see `../spec-refusals.md` entries R-DG-1, R-DG-2 and R-DG-3.

---

## 0. Scope, and the contract these rules run under

`quality-axes.md` §1.4a leaves the media kind exactly three guard classes after the family takes over
everything that can be read from typed evidence. This file specifies those three and nothing else. The
other twenty-one guards of the superseded surface are either family rules over typed readings, or host
scanner vocabulary, and the clean-room README §2 lists them with the reason each is out of scope.

__DG-1. What a dialect rule is allowed to do.__ These rules run after the family has read the release, and
the seam they run in permits only two moves: turn an absent reading into a present one, or raise the
evidence source of a reading that is already present. A dialect rule can never lower a source and can
never overwrite a stronger reading. Justification, and it is the reason the seam is shaped this way: every
rule in this file is a heuristic over a naming habit, and a heuristic that can overwrite a measurement
taken from the file itself is a bug generator. The constraint makes the whole class of that bug
unreachable rather than merely discouraged.

Consequently __every reading contributed by this file is contributed at the assumed source__, which is the
weakest one. A probe of the file, or an explicit token the shared scan read, always wins.

---

## 1. Dialect one — the fansub bracket conventions

__DG-2. What the dialect is.__ Fan-subtitling distributions name their files as a bracket-delimited group
prefix, the work, and then a run of bracket-delimited segments carrying the technical facts, ending in a
checksum. Inside those segments the separators are dropped, because the bracket is already the delimiter.
That is the whole of the dialect and it is why a kind needs rules for it at all: __the shared token scan
is bounded by separators, and this dialect has none inside its segments.__

__DG-3. A two-letter disc abbreviation inside a bracket segment names a high-definition disc source.__ It
appears bare, or joined directly to a line count with no separator between them. When it is present the
kind contributes an origin of high-definition disc, at the assumed source.

Justification: the abbreviation is the fansub conventions' spelling for the disc release of a work, in a
position where nothing else in the dialect occupies two letters. What makes the rule safe is the position,
not the token: the same two letters occur inside titles as initials and as ordinary words, so the rule
requires the bracket or separator delimiters and fires nowhere else. Stating the position requirement is
what keeps this from being a substring match that claims a disc source for every work whose title contains
those letters.

__DG-4. When the same name also carries a bitstream-copy token, the origin is the disc bitstream rather
than the disc.__ Justification: a bitstream copy of a disc is not an encode of it, and the axes model
carries the two as separate origin members precisely because holding the master is a different bitrate
class from holding an encode of it. The bitstream token is explicit and the disc abbreviation is
positional, so combining them loses nothing.

__DG-5. A line count joined to the disc abbreviation with no separator is a resolution reading.__
Contributed at the assumed source, for the reason in DG-2: the shared scan cannot see it because there is
no separator to bound it. It is assumed rather than stated because the segment may be describing the
source disc rather than the file.

__DG-6. The three-letter stream word inside a bracket segment names a streaming origin at generation
zero.__ Both readings at the assumed source.

Justification for the origin: the word is the dialect's spelling for a service capture, in the same
segment position as DG-3's abbreviation. Justification for the generation, which is the part that needs
one: the fansub workflow for a service capture muxes a subtitle track into the service's own stream rather
than re-encoding the video, so the count of lossy re-encodes since the service's stream is zero. A group
that does re-encode says so with a codec token, and DG-1 means the explicit codec reading wins.

__DG-7. Neither DG-3 nor DG-6 fires outside a bracket-delimited segment or outside separator
delimiters.__ Stated as its own rule because it is the rule that bounds the other four, and because a
negative assertion in S5 pins it.

---

## 2. Dialect two — the whole-disc image

__DG-8. What is being detected, and what it contributes.__ Some releases are a copy of an entire disc
rather than a file extracted from one. The reading is a packaging of disc image, at the assumed source,
and __nothing else__: not an origin, not a resolution, not a generation. Those come from the typed
evidence the family already read. Reason for contributing only the one reading: packaging is what
the detection is evidence of, and a rule that also set an origin or a resolution would be inferring two
more things from one observation.

This is narrower than the superseded surface, deliberately, and the narrowing is the point of DG-9.

__DG-9. A disc image requires positive disc-image evidence. A disc medium name is not enough.__

Positive disc-image evidence, alphabetical:

- a capacity label, meaning a medium abbreviation joined to one of the standard disc capacities in
  gigabytes
- a disc-image or disc-structure container, meaning a filename extension or folder name that is a whole
  disc rather than a single title
- a stereoscopic disc label
- a whole-disc word, meaning a word announcing that the release is the complete disc or an untouched one

At least one of these must be present. Justification, and it is the correction: naming the disc a release
came __from__ is something almost every disc-sourced release does, so treating the medium name as evidence
of a whole-disc copy claims the image reading for ordinary encodes. The distinguishing evidence is a
statement about the __artifact__ — its capacity, its container, its completeness — because only a whole
disc has those properties to state. The superseded surface inferred the image reading from a medium name
plus a bitstream codec name, and `quality-axes.md` §10 records two cases where that inference is
observably wrong, one of them a name whose only disc word is part of the work's title. Requiring an
artifact statement removes the whole class.

__DG-10. Any evidence of re-encoding refuses the disc-image reading outright.__

Re-encode evidence, alphabetical:

- a lossy re-encoding codec name, meaning a codec whose presence in the name announces that someone
  produced the file rather than copied it
- a rip token, meaning any of the vocabulary that names the act of extracting from a medium
- a single-file consumer container extension
- a stream-source name

Justification: a disc image contains the disc's own structure, so every one of these contradicts it
directly. A file that is a consumer container is not a disc image, whatever else the name says. Stated as
a refusal rather than as a weighting because there is nothing to weigh: the two claims cannot both be
true, and DG-1 forbids a heuristic from winning against evidence.

__DG-11. A stated resolution does not refuse the disc-image reading.__ Justification, stated because it is
the one place a reader might expect a refusal and must not apply one: a packager describing a disc image
routinely states the resolution __of the disc__, and doing so is not a claim to have re-encoded anything.
A capacity label beside a line count is a coherent description of a disc image and is common.

__DG-12. The disc-image reading says nothing about which disc.__ A standard-definition disc image and a
high-definition disc image are both disc images, and the origin axis is where the difference lives. The
superseded surface entangled the two because its output was a single rung name that had to pick one;
packaging and origin are separate axes and the entanglement does not survive. This is a simplification and
it deletes a special case rather than adding one.

---

## 3. Dialect three — the legacy resolution spellings

__DG-13. A pre-numeric high-resolution broadcast label reads as 720 lines, at the assumed source.__ The
label is a two-part scene abbreviation meaning a high-resolution widescreen broadcast encode, from the
period before releases stated line counts numerically.

Justification: the label was coined to distinguish a broadcast encode that was above standard definition
at a time when exactly one such format was in circulation, so the label names that format by elimination.
The reading is assumed rather than stated because the label does not carry a number and the inference is
about which era the name comes from. What makes it safe to keep at all is DG-1: any explicit line count
anywhere in the name wins over it.

__DG-14. A line count spelling that the separator-bounded scan could not reach is still a line count.__
When a name carries a resolution spelling inside a bracket-delimited segment, or joined to a neighbouring
token with no separator, the kind contributes it at the assumed source.

Justification: the spelling is unambiguous — a line count followed by a scan-type letter has no other
meaning in the dialect — so the only reason it went unread is a scanning boundary, which is an artifact of
how the reader works rather than a fact about the release. Contributed at the assumed source rather than
as stated, for one reason: a second line count elsewhere in a name that already stated one is more often
describing the source than the file, and DG-1 makes the stated reading win.

__What is not here.__ The superseded surface also carried three pixel-pair spellings as kind guards.
They are host scanner vocabulary under QA-17, not a kind's dialect: a width-by-height pair is a
resolution spelling in every media kind there is, and there is nothing about movies in it. Refusal list
entry R-DG-3.
