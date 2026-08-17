# S2 — normalizing a title into comparison keys

- __Spec id__: NM. Rule ids NM-1 through NM-11.
- __Step__: 1a, survey to spec.
- __Role__: spec writer, dirty side of the wall.
- __Written__: 2026-08-17.
- __Read to write it__: `src/Arronix.Plugin.Movies/Definition/MoviesParsing.cs`;
  `src/Arronix.Plugin.Movies/Definition/MoviesCorpus.cs`; `docs/design/clean-room-plan.md`.
- __Register__: sanitized. The character rule, the ordering rule and the justification rule of the
  clean-room README §4 were run over this file and pass.
- __Derived material__: none.

---

## 0. What normalization is for, and the trap in it

Two titles that name the same work are spelled differently by different packagers: with and without the
leading article, with and without punctuation, with a spelled ampersand or the character, with a German
umlaut or its two-letter transliteration. A comparison key folds those away so a lookup finds the work.

The trap, and it is the one that decides most of the rules below: __folding is lossy, and a fold that eats
the whole title has destroyed the only thing the key was for.__ Every rule here is bounded by that.

---

## 1. The two keys

__NM-1. There are two normalized forms and they are not interchangeable.__

- The __comparison key__ is aggressive: it folds articles, stop words, punctuation and diacritics so two
  spellings of one work collide. It is for equality, never for display.
- The __query form__ is conservative: it produces something a remote search accepts, keeping words intact
  and only removing what a search index would choke on. It is for outbound requests.

Justification for keeping them separate: they have opposite failure modes. A comparison key that folds too
little misses a match that exists; a query form that folds too much returns results for a different work
entirely. One form cannot be tuned for both, and the surveyed failure of merging them is that the search
loses words the index needed.

---

## 2. The comparison key

__NM-2. Leading articles are removed from the front of the title only.__

Vocabulary, alphabetical:

- a
- an
- the

Reason: the article is a property of the language the title is written in, not of the work, and catalogues
disagree about whether to carry it. Front position only, because the same words occur inside titles as
ordinary words and removing them there changes which work the title names.

__NM-3. Stop words are removed wherever they occur.__

Vocabulary, alphabetical:

- a
- an
- and
- of
- or
- the
- à

Reason: these are the words a packager most often adds, drops or abbreviates when a name has to fit a
length limit, so they are the least reliable part of a spelling. The accented form is listed because the
same word occurs unaccented and accented in the same catalogue and the two must collide.

__NM-4. A stop word that is the entire title survives.__ When removing stop words would leave nothing, no
stop word is removed. Reason: NM-0's trap. Works whose whole title is one of these words exist, and a key
of the empty string collides with every other emptied title, which is the worst possible outcome for a
key — it is not a missed match, it is a confidently wrong one.

__NM-5. A single letter that is part of an initialism is never an article.__ When a candidate article is a
single character sitting inside a run of stop-separated single characters per TP-14, it is left alone.
Reason: the letter is a component of an abbreviation, not a word, and removing it silently renames the
work. This is the rule that keeps the initialism form of a title from being decapitated.

__NM-6. Diacritics are folded to their base letters, except where a transliteration is declared.__ Reason:
packagers strip diacritics routinely because file systems and trackers have historically been ASCII, so
the accented and unaccented spellings have to collide.

__NM-7. Four letters transliterate rather than fold.__

Vocabulary, alphabetical:

- a-umlaut becomes the two letters a and e
- o-umlaut becomes the two letters o and e
- sharp s becomes the two letters s and s
- u-umlaut becomes the two letters u and e

Reason, and this is the one place folding is wrong rather than merely lossy: in German the umlaut is a
spelling of a two-letter sequence, not an accent on a letter, and dropping the mark produces a different
word that is a real word. The transliteration is the conventional written substitution used when the mark
cannot be typed, so it is what the alternative spellings in circulation actually are. The sharp s is the
same case for the same reason.

__NM-8. A title that is entirely digits is kept as digits.__ Reason: works titled with a number exist and
are common, and a normalizer that treats a numeric title as a year or as noise loses them completely.
This is the comparison-key twin of TP-3, and the two must agree or a title the reader claimed becomes a
key that cannot find it.

__NM-9. If normalization empties the title, the key is the raw title instead.__ Reason: NM-0's trap
again, stated as the general guard rather than as NM-4's special case. Whatever combination of folds
produced the empty string, an empty key is never the right answer, and the unnormalized string is at
worst a key that only matches itself. This rule is the one that makes every other rule in this file safe
to add to: a new fold can only cost a missed match, never a wrong one.

---

## 3. The query form

__NM-10. The query pipeline is four steps and the order is forced by the steps themselves.__

1. Drop a leading article.
2. Spell the ampersand character as the word.
3. Drop apostrophes and the typographic quote marks that stand in for them, joining the letters either
   side rather than separating them.
4. Replace every remaining run of non-alphanumeric characters with one space, and collapse runs of spaces.

The ordering constraints, each justified rather than declared:

- Step 2 must precede step 4. The ampersand is a non-alphanumeric character, so step 4 would delete it,
  and the word it stands for is a word the search index has. Spelling it first is what preserves it.
- Step 3 must precede step 4. An apostrophe inside a word must close up rather than split the word, and
  step 4 splits. Reversing them turns one possessive word into two fragments, neither of which the index
  has.
- Step 1 may be anywhere before step 4 and is stated first because it is the only step that depends on
  position within the string, and position is what step 4 destroys.

Nothing else about the order is constrained, and nothing else is stated. A step whose position does not
change the result does not get a rule.

__NM-11. The query form keeps words, and words only.__ It never removes stop words. Reason: a remote index
is searching prose and the stop words are part of the phrase it indexed. Removing them is the aggressive
fold of NM-3, which belongs to the other key.
