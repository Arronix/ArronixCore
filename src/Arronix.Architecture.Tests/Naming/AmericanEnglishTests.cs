using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Naming;

/// <summary>
/// Rule 7 - the delivery is written in American English.
/// </summary>
/// <remarks>
/// <para>
/// This is not a style preference dressed up as a rule. The BCL is en-US - <c>Initialize</c>,
/// <c>Normalize</c>, <c>Serialize</c>, <c>Color</c>, <c>CodeAnalysis</c> - so an en-GB identifier sits
/// beside a BCL member that spells the same word differently, and every call site has to remember which
/// of the two it is looking at. One dialect is cheaper than two, and en-US is the one the framework
/// already picked.
/// </para>
/// <para>
/// <b>Words, not substrings.</b> Every check runs over words split out of an identifier by
/// <see cref="SourceScanner.Words"/>, and matches whole words only. That is what lets the rule reject
/// <c>NormalisedTitle</c> while accepting <c>CodeAnalysis</c>, <c>CancellationToken</c>,
/// <c>OperationCanceledException</c>, <c>advertises</c> and <c>otherwise</c>. A governance rule that
/// over-reports is a governance rule that gets suppressed, so the lexicon below is deliberately short: it
/// carries the pairs this codebase actually hit plus the systematic <c>-ise/-isation</c> family, and
/// nothing speculative.
/// </para>
/// <para>
/// <b>Words left out on purpose.</b> <c>grey</c> is a valid CSS keyword and markup lives in this tree, so
/// it is not policed. <c>analyses</c> is the legitimate en-US plural of <i>analysis</i>, so the
/// <c>analyse</c> family is listed by hand with that one form omitted. <c>cancellation</c> is correct
/// en-US despite the double L, so only <c>cancelled</c> and <c>cancelling</c> are refused. Words that are
/// correct in both dialects - <c>otherwise</c>, <c>exercise</c>, <c>promise</c>, <c>surprise</c>,
/// <c>compromise</c>, <c>advertise</c>, <c>prologue</c>, <c>towards</c> - are absent by construction.
/// </para>
/// <para>
/// <b>Scope.</b> Code identifiers and string literals across every <c>Arronix.*</c> project, test projects
/// included. Comments and Markdown are not policed mechanically: prose sometimes has to quote an external
/// source verbatim, and a rule that cannot tell a quotation from an error would fail the build for being
/// accurate. The legacy trees are out of scope entirely.
/// </para>
/// </remarks>
[TestFixture]
public class AmericanEnglishTests
{
    /// <summary>
    /// The one file this rule cannot read: its own lexicon, which has to spell the refused words out in
    /// order to refuse them.
    /// </summary>
    /// <remarks>
    /// The same carve-out <see cref="LegacyNameTests"/> needs for the loader's deny list. It is one file,
    /// named exactly, and <see cref="TheOnlyExcludedFileIsThisOneAndItReallyDoesNeedExcluding"/> proves
    /// both that it needs excluding and that nothing else is hiding behind it.
    /// </remarks>
    private const string LexiconFile = "src/Arronix.Architecture.Tests/Naming/AmericanEnglishTests.cs";

    /// <summary>
    /// Stems whose en-GB <c>-is-</c> is en-US <c>-iz-</c>, written in their en-US form.
    /// </summary>
    /// <remarks>
    /// Stored en-US and flipped to en-GB at load, so this array reads as the spelling the codebase wants.
    /// </remarks>
    private static readonly string[] AmericanStems =
    [
        "anonymiz", "authoriz", "canonicaliz", "categoriz", "centraliz", "characteriz", "customiz",
        "deserializ", "formaliz", "generaliz", "initializ", "localiz", "materializ", "minimiz",
        "moderniz", "normaliz", "optimiz", "organiz", "parameteriz", "prioritiz", "recogniz", "realiz",
        "sanitiz", "serializ", "standardiz", "summariz", "synchroniz", "synthesiz", "tokeniz", "utiliz",
        "virtualiz", "visualiz"
    ];

    /// <summary>
    /// The suffixes an <c>-ise</c> stem takes. None of them is empty, which is what keeps the legitimate
    /// nouns <c>synthesis</c> and <c>analysis</c> out of the net.
    /// </summary>
    private static readonly string[] StemSuffixes =
    [
        "e", "ed", "es", "ing", "er", "ers", "ation", "ations", "able", "ability"
    ];

    /// <summary>
    /// Pairs that no rule generates, written en-GB to en-US.
    /// </summary>
    private static readonly (string British, string American)[] IrregularPairs =
    [
        ("behaviour", "behavior"), ("behaviours", "behaviors"), ("behavioural", "behavioral"),
        ("colour", "color"), ("colours", "colors"), ("coloured", "colored"), ("colouring", "coloring"),
        ("flavour", "flavor"), ("flavours", "flavors"), ("flavoured", "flavored"),
        ("favour", "favor"), ("favours", "favors"), ("favoured", "favored"),
        ("favourite", "favorite"), ("favourites", "favorites"), ("favourable", "favorable"),
        ("honour", "honor"), ("honours", "honors"), ("honoured", "honored"), ("honouring", "honoring"),
        ("neighbour", "neighbor"), ("neighbours", "neighbors"), ("neighbouring", "neighboring"),
        ("endeavour", "endeavor"), ("endeavours", "endeavors"),

        ("catalogue", "catalog"), ("catalogues", "catalogs"),
        ("catalogued", "cataloged"), ("cataloguing", "cataloging"),
        ("dialogue", "dialog"), ("dialogues", "dialogs"),
        ("analogue", "analog"), ("analogues", "analogs"),

        ("analyse", "analyze"), ("analysed", "analyzed"), ("analysing", "analyzing"),
        ("analyser", "analyzer"), ("analysers", "analyzers"),
        ("paralyse", "paralyze"), ("paralysed", "paralyzed"), ("paralysing", "paralyzing"),

        ("labelled", "labeled"), ("labelling", "labeling"), ("unlabelled", "unlabeled"),
        ("cancelled", "canceled"), ("cancelling", "canceling"),
        ("modelled", "modeled"), ("modelling", "modeling"), ("modeller", "modeler"),
        ("travelled", "traveled"), ("travelling", "traveling"), ("traveller", "traveler"),
        ("signalled", "signaled"), ("signalling", "signaling"),
        ("levelled", "leveled"), ("levelling", "leveling"),
        ("fuelled", "fueled"), ("fuelling", "fueling"),

        ("licence", "license"), ("licences", "licenses"),
        ("licenced", "licensed"), ("licencing", "licensing"),
        ("defence", "defense"), ("defences", "defenses"),
        ("offence", "offense"), ("offences", "offenses"),
        ("pretence", "pretense"),
        ("practise", "practice"), ("practised", "practiced"),
        ("practises", "practices"), ("practising", "practicing"),

        ("artefact", "artifact"), ("artefacts", "artifacts"),
        ("judgement", "judgment"), ("judgements", "judgments"),
        ("theatre", "theater"), ("theatres", "theaters"),
        ("centre", "center"), ("centres", "centers"), ("centred", "centered"),
        ("programme", "program"), ("programmes", "programs"),
        ("acknowledgement", "acknowledgment"), ("acknowledgements", "acknowledgments"),
        ("fulfil", "fulfill"), ("fulfils", "fulfills"), ("fulfilment", "fulfillment"),
        ("instalment", "installment"), ("instalments", "installments"),
        ("enrolment", "enrollment"), ("enrolments", "enrollments"),
        ("skilful", "skillful"),
        ("misspelt", "misspelled"), ("learnt", "learned"),
        ("whilst", "while"), ("amongst", "among")
    ];

    private static readonly Dictionary<string, string> Lexicon = BuildLexicon();

    /// <summary>Gets every project the spelling rule speaks about.</summary>
    public static IEnumerable<string> PolicedProjects => RepositoryLayout.AllProjects;

    [Test]
    [TestCaseSource(nameof(PolicedProjects))]
    public void ProjectSpellsEveryIdentifierInAmericanEnglish(string projectName)
    {
        var offenders = SourceScanner
            .CodeIdentifiers(projectName, "*.cs", "*.razor")
            .Where(static entry => !string.Equals(entry.File, LexiconFile, StringComparison.Ordinal))
            .SelectMany(static entry => SourceScanner
                .Words(entry.Token)
                .Select(word => (entry.File, entry.Line, Word: word)))
            .Select(static found => (found.File, found.Line, found.Word, American: AmericanFormOf(found.Word)))
            .Where(static found => found.American is not null)
            .Select(static found => $"{found.File}:{found.Line}: '{found.Word}' -> '{found.American}'")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' spells an identifier in British English. The codebase is en-US.");
    }

    [Test]
    public void TheLexiconCoversTheFamiliesItClaimsTo()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AmericanFormOf("Normalised"), Is.EqualTo("Normalized"));
            Assert.That(AmericanFormOf("catalogue"), Is.EqualTo("catalog"));
            // Matching ignores case; the correction comes back in the casing the word was written in,
            // which for an all-caps word means the leading capital is preserved and the rest is not.
            Assert.That(AmericanFormOf("BEHAVIOUR"), Is.EqualTo("Behavior"));
            Assert.That(AmericanFormOf("Labelled"), Is.EqualTo("Labeled"));
            Assert.That(AmericanFormOf("cancelled"), Is.EqualTo("canceled"));
            Assert.That(AmericanFormOf("serialisation"), Is.EqualTo("serialization"));
            Assert.That(AmericanFormOf("Sanitise"), Is.EqualTo("Sanitize"));
            Assert.That(AmericanFormOf("initialiser"), Is.EqualTo("initializer"));
            Assert.That(AmericanFormOf("Materialisation"), Is.EqualTo("Materialization"));
        });
    }

    [Test]
    public void TheLexiconLeavesCorrectlySpelledWordsAlone()
    {
        // The false-positive controls. Each of these matched a naive substring scan during the en-US
        // sweep, and each is correct as written - BCL names, en-US words that merely end in -ise or -ise
        // -shaped letters, and the nouns the -is stems would swallow if a bare stem were allowed.
        string[] correct =
        [
            "Normalize", "Catalog", "Behavior", "Labeled", "Canceled", "Cancellation", "CancellationToken",
            "OperationCanceledException", "CodeAnalysis", "AnalysisLevel", "analysis", "analyses",
            "synthesis", "emphasis", "basis", "otherwise", "exercise", "promise", "surprise", "compromise",
            "advertise", "advertises", "revised", "precise", "concise", "premise", "disguise", "improvised",
            "supervise", "franchise", "enterprise", "pairwise", "likewise", "memberwise", "prologue",
            "epilogue", "monologue", "towards", "installed", "controlling", "enrolled", "realism",
            "Calibre", "license", "program", "Raised", "ActivityRaised", "Serialize", "Initialize"
        ];

        var wrongly = correct
            .Where(static word => AmericanFormOf(word) is not null)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(wrongly, Is.Empty, "The lexicon rejects a word that is already correct.");
    }

    [Test]
    public void TheOnlyExcludedFileIsThisOneAndItReallyDoesNeedExcluding()
    {
        var britishWordsInTheLexiconFile = SourceScanner
            .CodeIdentifiers(nameof(Arronix) + ".Architecture.Tests", "*.cs")
            .Where(static entry => string.Equals(entry.File, LexiconFile, StringComparison.Ordinal))
            .SelectMany(static entry => SourceScanner.Words(entry.Token))
            .Count(static word => AmericanFormOf(word) is not null);

        Assert.Multiple(() =>
        {
            // The carve-out is load-bearing: without it this file fails its own rule.
            Assert.That(
                britishWordsInTheLexiconFile,
                Is.GreaterThan(0),
                "The lexicon file no longer contains the words it exists to refuse, so the exclusion is "
                + "now hiding nothing and should be deleted.");

            // And it is a carve-out for one file, not a directory or a pattern.
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(RepositoryLayout.Root, LexiconFile)), Is.True);
        });
    }

    [Test]
    public void TheRuleActuallyReadsEveryProject()
    {
        Assert.Multiple(() =>
        {
            // A spelling rule that scanned nothing would pass. Prove there is something to scan.
            Assert.That(RepositoryLayout.AllProjects, Is.Not.Empty);

            Assert.That(
                RepositoryLayout.AllProjects,
                Is.SupersetOf(RepositoryLayout.MediaNeutralProjects),
                "The spelling rule must cover at least the shipped assemblies.");

            Assert.That(
                RepositoryLayout.AllProjects.Where(static name => name.EndsWith(".Tests", StringComparison.Ordinal)),
                Is.Not.Empty,
                "Test projects are in scope; a test method name is read like any other identifier.");

            foreach (var project in RepositoryLayout.AllProjects)
            {
                Assert.That(
                    SourceScanner.CodeIdentifiers(project, "*.cs", "*.razor").Take(1),
                    Is.Not.Empty,
                    $"No source was read for '{project}', so its rule would pass by finding nothing.");
            }
        });
    }

    /// <summary>
    /// Returns the en-US spelling of a word, or <see langword="null"/> when the word is already correct.
    /// </summary>
    /// <param name="word">One word, as split out of an identifier.</param>
    /// <returns>The correction, casing preserved, or <see langword="null"/>.</returns>
    private static string? AmericanFormOf(string word)
    {
        if (!Lexicon.TryGetValue(word, out var american))
        {
            return null;
        }

        // Give the correction back in the casing it was written in, so a failure message can be pasted
        // straight into the offending identifier.
        return char.IsUpper(word[0]) && american.Length > 0
            ? char.ToUpperInvariant(american[0]) + american[1..]
            : american;
    }

    private static Dictionary<string, string> BuildLexicon()
    {
        var lexicon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var americanStem in AmericanStems)
        {
            // "normaliz" -> "normalis". The flip is done here rather than written out so that this file
            // states the wanted spelling and derives the unwanted one.
            var britishStem = string.Concat(americanStem.AsSpan(0, americanStem.Length - 1), "s");

            foreach (var suffix in StemSuffixes)
            {
                lexicon[britishStem + suffix] = americanStem + suffix;
            }
        }

        foreach (var (british, american) in IrregularPairs)
        {
            lexicon[british] = american;
        }

        return lexicon;
    }
}
