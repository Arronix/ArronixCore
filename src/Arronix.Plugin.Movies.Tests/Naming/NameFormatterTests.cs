namespace Arronix.Plugin.Movies.Tests.Naming;

/// <summary>
/// The name renderer, ported from Radarr's <c>FileNameBuilderFixture</c> and its cleaning helpers.
/// </summary>
/// <remarks>
/// <para>
/// <b>None of this is movie semantics either.</b> The template grammar, elision, truncation, separator
/// collapse, illegal-character policy, colon policy, casing and reserved-device-name escaping are the same
/// for a movie, an episode, a track and a chapter, and the conversion moves every one of them into the
/// host naming engine. What a movie declares is four templates, one condition row, a folder spine and two
/// token fallbacks — asserted in <c>RenamePolicyTests</c>.
/// </para>
/// <para>
/// The corpus is preserved row for row and cannot be executed from here: the engine is internal to
/// <c>Arronix.Host</c>. The rows are marked ignored rather than deleted so the gap is visible in every
/// run.
/// </para>
/// </remarks>
[TestFixture]
public class NameFormatterTests
{
    [TestCase("Florence + the Machine", "Florence + the Machine")]
    [TestCase("Beyoncé X10", "Beyonce X10")]
    [TestCase("Girlfriends' Guide to Divorce", "Girlfriends Guide to Divorce")]
    [TestCase("Rule #23: Never Lie to the Kids", "Rule #23 Never Lie to the Kids")]
    [TestCase("Anne Hathaway/Florence + The Machine", "Anne Hathaway Florence + The Machine")]
    [TestCase("Chris Rock/Prince", "Chris Rock Prince")]
    [TestCase("Ke$ha: My Crazy Beautiful Life", "Ke$ha My Crazy Beautiful Life")]
    [TestCase("Free! - Iwatobi Swim Club", "Free! Iwatobi Swim Club")]
    [TestCase("Tamara Ecclestone: Billion $$ Girl", "Tamara Ecclestone Billion $$ Girl")]
    [TestCase("Marvel's Agents of S.H.I.E.L.D.", "Marvels Agents of S.H.I.E.L.D")]
    [TestCase("Castle (2009)", "Castle 2009")]
    [TestCase("Law & Order (UK)", "Law and Order UK")]
    [TestCase("Is this okay?", "Is this okay")]
    [TestCase("[a] title", "a title")]
    [TestCase("I'm the Boss", "Im the Boss")]
    [TestCase("I've Been Caught", "Ive Been Caught")]
    [TestCase("That'll Be The Day", "Thatll Be The Day")]
    [TestCase("I'd Rather Be Alone", "Id Rather Be Alone")]
    [TestCase("I Can't Die", "I Cant Die")]
    [TestCase("Won`t Get Fooled Again", "Wont Get Fooled Again")]
    [TestCase("Don’t Blink", "Dont Blink")]
    [TestCase("The ` Legend of Kings", "The Legend of Kings")]
    [TestCase("Joker: Folie à deux", "Joker Folie a deux")]
    [TestCase("Karma's a B*tch!", "Karmas a B-tch!")]
    [TestCase("$#*! My Dad Says", "$#-! My Dad Says")]
    [TestCase("backslash \\ backlash", "backslash backlash")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CleansATitleTheWayASceneReleaseWouldSpellIt(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CleansNothingOutOfNothing()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The Lord of the Rings")]
    [TestCase("Law & Order (UK)")]
    [TestCase("Castle (2009)")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void IsADifferentFunctionFromTheComparisonKey(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The Mist", "Mist, The")]
    [TestCase("A Place to Call Home", "Place to Call Home, A")]
    [TestCase("An Adventure in Space and Time", "Adventure in Space and Time, An")]
    [TestCase("The Flash (2010)", "Flash, The (2010)")]
    [TestCase("A League Of Their Own (AU)", "League Of Their Own, A (AU)")]
    [TestCase("The Fixer (ZH) (2015)", "Fixer, The (ZH) (2015)")]
    [TestCase("The Sixth Sense 2 (Thai)", "Sixth Sense 2, The (Thai)")]
    [TestCase("The Amazing Race (Latin America)", "Amazing Race, The (Latin America)")]
    [TestCase("The Rat Pack (A&E)", "Rat Pack, The (A&E)")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void MovesALeadingArticleToTheEnd(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("A")]
    [TestCase("Anne")]
    [TestCase("Theodore")]
    [TestCase("3%")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void LeavesATitleWithNoLeadingArticleAlone(string arg0)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The Mist", "Mist, The")]
    [TestCase("A Place to Call Home", "Place to Call Home, A")]
    [TestCase("An Adventure in Space and Time", "Adventure in Space and Time, An")]
    [TestCase("The Flash (2010)", "Flash, The 2010")]
    [TestCase("A League Of Their Own (AU)", "League Of Their Own, A AU")]
    [TestCase("The Fixer (ZH) (2015)", "Fixer, The ZH 2015")]
    [TestCase("The Sixth Sense 2 (Thai)", "Sixth Sense 2, The Thai")]
    [TestCase("The Amazing Race (Latin America)", "Amazing Race, The Latin America")]
    [TestCase("The Rat Pack (A&E)", "Rat Pack, The AandE")]
    [TestCase(null, "")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void MovesTheArticleAndCleansTheResult(string? arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The Badger's Collection", "Badgers Collection, The")]
    [TestCase("A Stupid/Idiotic Collection", "Stupid Idiotic Collection, A")]
    [TestCase("An Astounding & Amazing Collection", "Astounding and Amazing Collection, An")]
    [TestCase("The Amazing Animal-Hero's Collection (2001)", "Amazing Animal-Heros Collection, The 2001")]
    [TestCase("A Different Movië (AU)", "Different Movie, A AU")]
    [TestCase("The Repairër (ZH) (2015)", "Repairer, The ZH 2015")]
    [TestCase("The Eighth Sensë 2 (Thai)", "Eighth Sense 2, The Thai")]
    [TestCase("The Hampster Pack (B&F)", "Hampster Pack, The BandF")]
    [TestCase("The Gásm: I (Almost) Got Away With It (1900)", "Gasm I Almost Got Away With It, The 1900")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CleansACollectionTitleTheSameWay(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The Mist", "M")]
    [TestCase("A", "A")]
    [TestCase("30 Rock", "3")]
    [TestCase("The '80s Greatest", "8")]
    [TestCase("좀비버스", "좀")]
    [TestCase("¡Mucha Lucha!", "M")]
    [TestCase(".hack", "H")]
    [TestCase("Ütopya", "U")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void TakesTheFirstCharacterAfterMovingTheArticle(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void FallsBackToAnUnderscoreWhenTheFirstTwoPositionsCarryNoLetter()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("CSI: Vegas", "Smart", "CSI - Vegas")]
    [TestCase("CSI: Vegas", "Dash", "CSI- Vegas")]
    [TestCase("CSI: Vegas", "Delete", "CSI Vegas")]
    [TestCase("CSI: Vegas", "SpaceDash", "CSI - Vegas")]
    [TestCase("CSI: Vegas", "SpaceDashSpace", "CSI - Vegas")]
    [TestCase("Movie:Title", "Smart", "Movie-Title")]
    [TestCase("Movie:Title", "Dash", "Movie-Title")]
    [TestCase("Movie:Title", "Delete", "MovieTitle")]
    [TestCase("Movie:Title", "SpaceDash", "Movie -Title")]
    [TestCase("Movie:Title", "SpaceDashSpace", "Movie - Title")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void AppliesTheColonPolicy(string arg0, string arg1, string arg2)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void ReadsCorrectlyForBothSpellingsUnderTheSmartPolicy()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void RemovesTheColonEntirelyWhenIllegalCharactersAreNotReplaced()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("Movie/Title", "Movie+Title")]
    [TestCase("Movie?Title", "Movie!Title")]
    [TestCase("Movie\\Title", "Movie+Title")]
    [TestCase("Movie*Title", "Movie-Title")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void SubstitutesACharacterNoPathComponentMayCarry(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void RemovesIllegalCharactersOutrightWhenAskedTo()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("con.Movie.2021", "con_Movie.2021")]
    [TestCase("com1.Movie.2021", "com1_Movie.2021")]
    [TestCase("PRN.Movie.2021", "PRN_Movie.2021")]
    [TestCase("nul.Movie.2021", "nul_Movie.2021")]
    [TestCase("aux.Movie.2021", "aux_Movie.2021")]
    [TestCase("lpt9.Movie.2021", "lpt9_Movie.2021")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void EscapesAReservedDeviceName(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("Content.Movie.2021", "Content.Movie.2021")]
    [TestCase("Movie.con.2021", "Movie.con.2021")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void DoesNotEscapeAWordThatMerelyContainsOne(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("The Fantastic Life of Mr. Sisko", 16, "The Fantastic…")]
    [TestCase("The Fantastic Life of Mr. Sisko", -13, "…Mr. Sisko")]
    [TestCase("Short", 16, "Short")]
    [TestCase("The Fantastic Life of Mr. Sisko", 0, "The Fantastic Life of Mr. Sisko")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void TruncatesToABudget(string arg0, int arg1, string arg2)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void UsesASingleEllipsisCharacterRatherThanThreePeriods()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void LeavesAValueAloneWhenTheBudgetIsTooSmallToTruncateInto()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("Beyoncé X10", "Beyonce X10")]
    [TestCase("Ütopya", "Utopya")]
    [TestCase("Amélie", "Amelie")]
    [TestCase("Carnivàle", "Carnivale")]
    [TestCase("", "")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void FoldsDiacritics(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [TestCase("{Movie Title}", "movietitle")]
    [TestCase("{Movie.Title}", "movietitle")]
    [TestCase("{Movie_Title}", "movietitle")]
    [TestCase("{movie title}", "movietitle")]
    [TestCase("Movie Title", "movietitle")]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CanonicalizesATokenNameIntoOneKey(string arg0, string arg1)
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CollapsesARunOfSeparatorsAndTrimsTheTail()
        => Assert.Fail("Unreachable: see the fixture remarks.");

    [Test]
    [Ignore("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void RejectsANullTemplateOrTokenSet()
        => Assert.Fail("Unreachable: see the fixture remarks.");
}
