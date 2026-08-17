# S3 — the edition phrase, in both of its jobs

- __Spec id__: ED. Rule ids ED-1 through ED-9.
- __Step__: 1a, survey to spec.
- __Role__: spec writer, dirty side of the wall.
- __Written__: 2026-08-17.
- __Read to write it__: `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs`;
  `docs/design/quality-axes.md`; `docs/design/clean-room-plan.md`.
- __Register__: sanitized. The character rule, the ordering rule and the justification rule of the
  clean-room README §4 were run over this file and pass.
- __Derived material__: none. One curated class of words is struck rather than restated; see
  `../spec-refusals.md` entry R-ED-1.

---

## 0. What an edition is, and why it is not a quality axis

An edition names __which cut of the work__ the file contains. It is not a property of the copy: a
director's cut at 720 lines and a theatrical cut at 2160 lines are different works in the sense that
matters to a viewer, and neither is a better copy of the other. That is why `quality-axes.md` carries the
edition as a facet rather than as an axis, and why this spec produces a tag rather than a reading.

It has a second job, and the second job is why it is specified alongside the title surface at all: an
edition phrase sits between the title and the year in one common naming shape, so it is a __title
terminator__. S1 TP-1 lists it as the second-strongest kind.

---

## 1. The shape of the phrase

__ED-1. An edition phrase is one of three constructions.__

1. A __qualifier__ followed by a __head noun__.
2. A __standalone qualifier__, which names a cut without needing a head noun because the word is already a
   description of one.
3. An __anniversary phrase__: a two-digit or three-digit number, optionally carrying an English ordinal
   suffix, followed by the word Anniversary, optionally followed by a head noun.

Justification for the three-way split rather than one flat list: the constructions have different
false-positive risks and therefore different boundary requirements. A qualifier with a head noun is
unambiguous because the head noun is doing the announcing. A standalone qualifier is a bare adjective and
can occur in a title, so it needs the full-word boundary of ED-6 to be safe. An anniversary phrase carries
a number, so it interacts with the year rule of S1 and has to be recognised before the year search rather
than after it.

__ED-2. The head nouns.__

Vocabulary, alphabetical:

- Cut
- Edition
- Version

Reason this set is three words and closed: these are the three nouns the distribution and marketing
conventions use to announce a variant cut. The set is small because a head noun's whole job is to be
recognisable, so the conventions converged on very few.

__ED-3. The qualifiers that take a head noun.__

Vocabulary, alphabetical:

- Collector
- Despecialized
- Director
- Extended
- Theatrical
- Ultimate

A qualifier ending in a possessive is written with or without the apostrophe and with either apostrophe
character, and all spellings are the same qualifier. Reason: the apostrophe is the character packagers
most often drop, because file systems and shells have historically disliked it.

__ED-4. The standalone qualifiers.__

Vocabulary, alphabetical:

- Fan Edit
- IMAX
- Open Matte
- Remastered
- Restored
- Uncensored
- Uncut
- Unrated

Reason each of these stands alone: each names the cut by naming what was done to it, so a head noun would
add nothing. Two of them are two-word phrases whose internal separator may be any of the four the dialect
uses, or absent.

__ED-5. A qualifier may be stacked in front of another qualifier, and a standalone qualifier may follow a
head noun.__ Both stackings are one edition phrase, not two. Reason: the cuts genuinely compose — a cut
can be both extended and the director's, and a director's cut can additionally be remastered — and the
packager writes the composition as it happened. Reading them as two phrases would emit two edition tags
for one release and force a consumer to decide which is the edition, which is a decision with no correct
answer.

__ED-6. Boundaries are whole words, and enclosing round brackets are optional and are not part of the
phrase.__ Reason: every qualifier here is an ordinary English word that occurs inside titles, so a
substring match claims editions that are not there. The bracketing is a packager's typography and carries
no information, so it is recognised and discarded rather than kept.

__ED-7. The separator between the words of a phrase may be any separator the dialect uses, and matching is
case-insensitive.__ Reason: the four separators are interchangeable across conventions, and casing in
release names is not meaningful — the same phrase appears in every casing there is.

__ED-8. A combined-cuts phrase is an edition.__ A small digit, the word in, and the digit one, written
with or without separators, announces that several cuts are present in one file. Reason: it is a statement
about which cut the file contains, which is what an edition is, and it needs saying because the file
genuinely differs from any single-cut copy.

---

## 2. The two jobs, stated separately

__ED-9. As a terminator, the edition phrase ends the title; as a tag, its matched text is the facet
value.__ The two jobs use one recognition and must not diverge — if the phrase that terminated the title
were recognised differently from the phrase that produced the tag, a release could be split at a boundary
that the tag then denies. Stated as a requirement rather than as an implementation: __one recognition,
two consumers.__

The facet value is the phrase as the release spelled it, normalized for case and separator only. Reason:
the value is displayed to a person and compared against a user's preference list, and both want the
spelling the packager used, not a canonical rewrite that no release carries.

---

## 3. What is deliberately not here

A group of proper nouns naming the marketing edition of one specific work each — a studio's brand name for
one film's re-release — is struck. It is curation: a list of words that exists because someone collected
one word per film, and each word's only justification is that a particular film used it. Entry R-ED-1 of
the refusal list carries the reasoning.

What replaces it is not another word list. A work's own edition names belong to the catalogue entry for
that work, where they are data about the work rather than vocabulary about the dialect, and where adding
one is an edit to a record rather than a code change. Until that is wired, releases carrying such a name
lose the edition tag and keep their title, which is the cheap failure of S1 TP-1 rather than the expensive
one.
