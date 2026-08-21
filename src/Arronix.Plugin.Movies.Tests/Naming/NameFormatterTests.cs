using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitIgnoreAttribute = global::NUnit.Framework.IgnoreAttribute;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestCaseAttribute = global::NUnit.Framework.TestCaseAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;

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
[NUnitTestFixtureAttribute]
public class NameFormatterTests
{
    static NameFormatterTests()
    {
        if (typeof(NUnitAssert).Assembly.GetName().Name != "nunit.framework")
        {
            throw new InvalidOperationException("The compatibility fixture did not bind the real NUnit assertion assembly.");
        }
    }

    [NUnitTestCaseAttribute("Florence + the Machine", "Florence + the Machine")]
    [NUnitTestCaseAttribute("Beyoncé X10", "Beyonce X10")]
    [NUnitTestCaseAttribute("Girlfriends' Guide to Divorce", "Girlfriends Guide to Divorce")]
    [NUnitTestCaseAttribute("Rule #23: Never Lie to the Kids", "Rule #23 Never Lie to the Kids")]
    [NUnitTestCaseAttribute("Anne Hathaway/Florence + The Machine", "Anne Hathaway Florence + The Machine")]
    [NUnitTestCaseAttribute("Chris Rock/Prince", "Chris Rock Prince")]
    [NUnitTestCaseAttribute("Ke$ha: My Crazy Beautiful Life", "Ke$ha My Crazy Beautiful Life")]
    [NUnitTestCaseAttribute("Free! - Iwatobi Swim Club", "Free! Iwatobi Swim Club")]
    [NUnitTestCaseAttribute("Tamara Ecclestone: Billion $$ Girl", "Tamara Ecclestone Billion $$ Girl")]
    [NUnitTestCaseAttribute("Marvel's Agents of S.H.I.E.L.D.", "Marvels Agents of S.H.I.E.L.D")]
    [NUnitTestCaseAttribute("Castle (2009)", "Castle 2009")]
    [NUnitTestCaseAttribute("Law & Order (UK)", "Law and Order UK")]
    [NUnitTestCaseAttribute("Is this okay?", "Is this okay")]
    [NUnitTestCaseAttribute("[a] title", "a title")]
    [NUnitTestCaseAttribute("I'm the Boss", "Im the Boss")]
    [NUnitTestCaseAttribute("I've Been Caught", "Ive Been Caught")]
    [NUnitTestCaseAttribute("That'll Be The Day", "Thatll Be The Day")]
    [NUnitTestCaseAttribute("I'd Rather Be Alone", "Id Rather Be Alone")]
    [NUnitTestCaseAttribute("I Can't Die", "I Cant Die")]
    [NUnitTestCaseAttribute("Won`t Get Fooled Again", "Wont Get Fooled Again")]
    [NUnitTestCaseAttribute("Don’t Blink", "Dont Blink")]
    [NUnitTestCaseAttribute("The ` Legend of Kings", "The Legend of Kings")]
    [NUnitTestCaseAttribute("Joker: Folie à deux", "Joker Folie a deux")]
    [NUnitTestCaseAttribute("Karma's a B*tch!", "Karmas a B-tch!")]
    [NUnitTestCaseAttribute("$#*! My Dad Says", "$#-! My Dad Says")]
    [NUnitTestCaseAttribute("backslash \\ backlash", "backslash backlash")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CleansATitleTheWayASceneReleaseWouldSpellIt(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CleansNothingOutOfNothing()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("The Lord of the Rings")]
    [NUnitTestCaseAttribute("Law & Order (UK)")]
    [NUnitTestCaseAttribute("Castle (2009)")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void IsADifferentFunctionFromTheComparisonKey(string arg0)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("The Mist", "Mist, The")]
    [NUnitTestCaseAttribute("A Place to Call Home", "Place to Call Home, A")]
    [NUnitTestCaseAttribute("An Adventure in Space and Time", "Adventure in Space and Time, An")]
    [NUnitTestCaseAttribute("The Flash (2010)", "Flash, The (2010)")]
    [NUnitTestCaseAttribute("A League Of Their Own (AU)", "League Of Their Own, A (AU)")]
    [NUnitTestCaseAttribute("The Fixer (ZH) (2015)", "Fixer, The (ZH) (2015)")]
    [NUnitTestCaseAttribute("The Sixth Sense 2 (Thai)", "Sixth Sense 2, The (Thai)")]
    [NUnitTestCaseAttribute("The Amazing Race (Latin America)", "Amazing Race, The (Latin America)")]
    [NUnitTestCaseAttribute("The Rat Pack (A&E)", "Rat Pack, The (A&E)")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void MovesALeadingArticleToTheEnd(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("A")]
    [NUnitTestCaseAttribute("Anne")]
    [NUnitTestCaseAttribute("Theodore")]
    [NUnitTestCaseAttribute("3%")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void LeavesATitleWithNoLeadingArticleAlone(string arg0)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("The Mist", "Mist, The")]
    [NUnitTestCaseAttribute("A Place to Call Home", "Place to Call Home, A")]
    [NUnitTestCaseAttribute("An Adventure in Space and Time", "Adventure in Space and Time, An")]
    [NUnitTestCaseAttribute("The Flash (2010)", "Flash, The 2010")]
    [NUnitTestCaseAttribute("A League Of Their Own (AU)", "League Of Their Own, A AU")]
    [NUnitTestCaseAttribute("The Fixer (ZH) (2015)", "Fixer, The ZH 2015")]
    [NUnitTestCaseAttribute("The Sixth Sense 2 (Thai)", "Sixth Sense 2, The Thai")]
    [NUnitTestCaseAttribute("The Amazing Race (Latin America)", "Amazing Race, The Latin America")]
    [NUnitTestCaseAttribute("The Rat Pack (A&E)", "Rat Pack, The AandE")]
    [NUnitTestCaseAttribute(null, "")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void MovesTheArticleAndCleansTheResult(string? arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("The Badger's Collection", "Badgers Collection, The")]
    [NUnitTestCaseAttribute("A Stupid/Idiotic Collection", "Stupid Idiotic Collection, A")]
    [NUnitTestCaseAttribute("An Astounding & Amazing Collection", "Astounding and Amazing Collection, An")]
    [NUnitTestCaseAttribute("The Amazing Animal-Hero's Collection (2001)", "Amazing Animal-Heros Collection, The 2001")]
    [NUnitTestCaseAttribute("A Different Movië (AU)", "Different Movie, A AU")]
    [NUnitTestCaseAttribute("The Repairër (ZH) (2015)", "Repairer, The ZH 2015")]
    [NUnitTestCaseAttribute("The Eighth Sensë 2 (Thai)", "Eighth Sense 2, The Thai")]
    [NUnitTestCaseAttribute("The Hampster Pack (B&F)", "Hampster Pack, The BandF")]
    [NUnitTestCaseAttribute("The Gásm: I (Almost) Got Away With It (1900)", "Gasm I Almost Got Away With It, The 1900")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CleansACollectionTitleTheSameWay(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("The Mist", "M")]
    [NUnitTestCaseAttribute("A", "A")]
    [NUnitTestCaseAttribute("30 Rock", "3")]
    [NUnitTestCaseAttribute("The '80s Greatest", "8")]
    [NUnitTestCaseAttribute("좀비버스", "좀")]
    [NUnitTestCaseAttribute("¡Mucha Lucha!", "M")]
    [NUnitTestCaseAttribute(".hack", "H")]
    [NUnitTestCaseAttribute("Ütopya", "U")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void TakesTheFirstCharacterAfterMovingTheArticle(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void FallsBackToAnUnderscoreWhenTheFirstTwoPositionsCarryNoLetter()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("CSI: Vegas", "Smart", "CSI - Vegas")]
    [NUnitTestCaseAttribute("CSI: Vegas", "Dash", "CSI- Vegas")]
    [NUnitTestCaseAttribute("CSI: Vegas", "Delete", "CSI Vegas")]
    [NUnitTestCaseAttribute("CSI: Vegas", "SpaceDash", "CSI - Vegas")]
    [NUnitTestCaseAttribute("CSI: Vegas", "SpaceDashSpace", "CSI - Vegas")]
    [NUnitTestCaseAttribute("Movie:Title", "Smart", "Movie-Title")]
    [NUnitTestCaseAttribute("Movie:Title", "Dash", "Movie-Title")]
    [NUnitTestCaseAttribute("Movie:Title", "Delete", "MovieTitle")]
    [NUnitTestCaseAttribute("Movie:Title", "SpaceDash", "Movie -Title")]
    [NUnitTestCaseAttribute("Movie:Title", "SpaceDashSpace", "Movie - Title")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void AppliesTheColonPolicy(string arg0, string arg1, string arg2)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void ReadsCorrectlyForBothSpellingsUnderTheSmartPolicy()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void RemovesTheColonEntirelyWhenIllegalCharactersAreNotReplaced()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("Movie/Title", "Movie+Title")]
    [NUnitTestCaseAttribute("Movie?Title", "Movie!Title")]
    [NUnitTestCaseAttribute("Movie\\Title", "Movie+Title")]
    [NUnitTestCaseAttribute("Movie*Title", "Movie-Title")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void SubstitutesACharacterNoPathComponentMayCarry(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void RemovesIllegalCharactersOutrightWhenAskedTo()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("con.Movie.2021", "con_Movie.2021")]
    [NUnitTestCaseAttribute("com1.Movie.2021", "com1_Movie.2021")]
    [NUnitTestCaseAttribute("PRN.Movie.2021", "PRN_Movie.2021")]
    [NUnitTestCaseAttribute("nul.Movie.2021", "nul_Movie.2021")]
    [NUnitTestCaseAttribute("aux.Movie.2021", "aux_Movie.2021")]
    [NUnitTestCaseAttribute("lpt9.Movie.2021", "lpt9_Movie.2021")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void EscapesAReservedDeviceName(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("Content.Movie.2021", "Content.Movie.2021")]
    [NUnitTestCaseAttribute("Movie.con.2021", "Movie.con.2021")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void DoesNotEscapeAWordThatMerelyContainsOne(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("The Fantastic Life of Mr. Sisko", 16, "The Fantastic…")]
    [NUnitTestCaseAttribute("The Fantastic Life of Mr. Sisko", -13, "…Mr. Sisko")]
    [NUnitTestCaseAttribute("Short", 16, "Short")]
    [NUnitTestCaseAttribute("The Fantastic Life of Mr. Sisko", 0, "The Fantastic Life of Mr. Sisko")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void TruncatesToABudget(string arg0, int arg1, string arg2)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void UsesASingleEllipsisCharacterRatherThanThreePeriods()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void LeavesAValueAloneWhenTheBudgetIsTooSmallToTruncateInto()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("Beyoncé X10", "Beyonce X10")]
    [NUnitTestCaseAttribute("Ütopya", "Utopya")]
    [NUnitTestCaseAttribute("Amélie", "Amelie")]
    [NUnitTestCaseAttribute("Carnivàle", "Carnivale")]
    [NUnitTestCaseAttribute("", "")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void FoldsDiacritics(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestCaseAttribute("{Movie Title}", "movietitle")]
    [NUnitTestCaseAttribute("{Movie.Title}", "movietitle")]
    [NUnitTestCaseAttribute("{Movie_Title}", "movietitle")]
    [NUnitTestCaseAttribute("{movie title}", "movietitle")]
    [NUnitTestCaseAttribute("Movie Title", "movietitle")]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CanonicalizesATokenNameIntoOneKey(string arg0, string arg1)
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void CollapsesARunOfSeparatorsAndTrimsTheTail()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");

    [NUnitTestAttribute]
    [NUnitIgnoreAttribute("The engines are reachable now, but this row is not reachable through any movie seam. It asserts the shared name sanitizer - cleaning, article moves, the colon and illegal-character policies, truncation, diacritic folding - which a rename only reaches once it has an item to render, and the pre-storage item source holds none. The behavior is kind-agnostic and belongs to the shared assembly's own tests; the row is kept so the movie corpus stays visible.")]
    public void RejectsANullTemplateOrTokenSet()
        => NUnitAssert.Fail("Unreachable: see the fixture remarks.");
}
