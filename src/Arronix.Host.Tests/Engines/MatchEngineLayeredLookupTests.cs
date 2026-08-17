using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Matching;
using FluentAssertions;

// Every contract named here is experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The layered-key-lookup strategy: the Radarr-verified layer ordering, the reading-side expanders, the
/// year-agreement predicate and the ambiguity policies.
/// </summary>
[TestFixture]
internal sealed class MatchEngineLayeredLookupTests
{
    private static readonly LayeredKeyLookupStrategy Strategy = new();

    private static EntryResolutionInput Input(
        string title,
        long? year = null,
        params ItemView[] candidates) => new()
    {
        Titles = [title],
        ReadingValues = new Dictionary<string, long?>(StringComparer.Ordinal)
        {
            ["reading.TitleYear"] = year,
        },
        Candidates = candidates,
    };

    [Test]
    public void AnEntrysOwnTitleOutranksAnotherEntrysAlternativeSpelling()
    {
        // The layering is the algorithm: an alternative spelling of one entry must never outrank the
        // actual title of another (MovieService.cs:126-158 layer order).
        var own = MatchEngineFixtures.Entry(1, "Alpha");
        var aliased = MatchEngineFixtures.Entry(2, "Something Else", alternativeTitles: ["Alpha"]);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Alpha", candidates: [aliased, own]));

        outcome.Entry.Should().NotBeNull();
        outcome.Entry!.Ref.Should().Be(MatchEngineFixtures.EntryRef(1));
        outcome.LayerId.Should().Be("own-title");
    }

    [Test]
    public void TheAlternativeTitleLayerAnswersWhenTheOwnTitleLayerIsSilent()
    {
        var aliased = MatchEngineFixtures.Entry(2, "Something Else", alternativeTitles: ["Alpha"]);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Alpha", candidates: [aliased]));

        outcome.Entry.Should().NotBeNull();
        outcome.LayerId.Should().Be("alternative-titles");
    }

    [Test]
    public void ARomanNumeralSpellingMatchesThroughTheExpanderLayer()
    {
        // Radarr rewrites the READING key into both numeral spellings (FindByTitleCandidates); the
        // stored titles are never rewritten.
        var entry = MatchEngineFixtures.Entry(1, "Chapter II");

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Chapter 2", candidates: [entry]));

        outcome.Entry.Should().NotBeNull();
        outcome.LayerId.Should().Be("roman-rewrite");
    }

    [Test]
    public void TheOriginalTitleIsTheSameLayerAsTheDisplayTitle()
    {
        var entry = MatchEngineFixtures.Entry(1, "Displayed", originalTitle: "Le Original");

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Le Original", candidates: [entry]));

        outcome.Entry.Should().NotBeNull();
        outcome.LayerId.Should().Be("own-title");
    }

    [Test]
    public void AStatedYearSelectsAmongEntriesSharingATitle()
    {
        var older = MatchEngineFixtures.Entry(1, "Twin", year: 1994);
        var newer = MatchEngineFixtures.Entry(2, "Twin", year: 2017);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Twin", year: 2017, candidates: [older, newer]));

        outcome.Entry.Should().NotBeNull();
        outcome.Entry!.Ref.Should().Be(MatchEngineFixtures.EntryRef(2));
        outcome.Basis.Should().Be(MatchBasis.TitleWithYear, "a present year that agreed corroborates");
    }

    [Test]
    public void ASecondaryYearAgreesWhereThePrimaryDisagrees()
    {
        var entry = MatchEngineFixtures.Entry(1, "Solo", year: 2017, secondaryYear: 2018);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Solo", year: 2018, candidates: [entry]));

        outcome.Entry.Should().NotBeNull();
        outcome.Basis.Should().Be(MatchBasis.TitleWithYear);
    }

    [Test]
    public void AWrongYearRejectsRatherThanMatchingTheOnlyCandidate()
    {
        var entry = MatchEngineFixtures.Entry(1, "Solo", year: 2017);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Solo", year: 1999, candidates: [entry]));

        outcome.Entry.Should().BeNull("a missing year is harmless; a wrong year is not");
    }

    [Test]
    public void AnAbsentYearAgreesAndTheBasisSaysTitleOnly()
    {
        var entry = MatchEngineFixtures.Entry(1, "Solo", year: 2017);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Solo", candidates: [entry]));

        outcome.Entry.Should().NotBeNull();
        outcome.Basis.Should().Be(MatchBasis.TitleOnly, "nothing corroborated the title");
    }

    [Test]
    public void AYearBelowTheDeclaredMinimumIsNoiseNotEvidence()
    {
        var entry = MatchEngineFixtures.Entry(1, "Solo", year: 2017);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Solo", year: 1200, candidates: [entry]));

        outcome.Entry.Should().NotBeNull("a parsed 'year' below the minimum is treated as absent");
        outcome.Basis.Should().Be(MatchBasis.TitleOnly);
    }

    [Test]
    public void AmbiguityIsRejectedWithTheContendersNamed()
    {
        var first = MatchEngineFixtures.Entry(1, "Twin", year: 1994);
        var second = MatchEngineFixtures.Entry(2, "Twin", year: 2017);

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Twin", candidates: [first, second]));

        outcome.Entry.Should().BeNull();
        outcome.Contenders.Should().HaveCount(2);
        outcome.RejectionReason.Should().Contain(MatchEngineFixtures.EntryRef(1).ToString())
            .And.Contain(MatchEngineFixtures.EntryRef(2).ToString());
    }

    [Test]
    public void TheYearTiebreakPrefersThePrimaryStatementOverTheSecondary()
    {
        var viaSecondary = MatchEngineFixtures.Entry(1, "Twin", year: 1980, secondaryYear: 2017);
        var viaPrimary = MatchEngineFixtures.Entry(2, "Twin", year: 2017);

        var declaration = MatchEngineFixtures.Declaration().Entry with
        {
            Ambiguity = AmbiguityPolicy.TiebreakByYear,
        };

        var outcome = Strategy.Resolve(declaration, Input("Twin", year: 2017, candidates: [viaSecondary, viaPrimary]));

        outcome.Entry.Should().NotBeNull();
        outcome.Entry!.Ref.Should().Be(MatchEngineFixtures.EntryRef(2));
    }

    [Test]
    public void NoLayerMatchingMeansARejectionNamingTheSilence()
    {
        var entry = MatchEngineFixtures.Entry(1, "Alpha");

        var outcome = Strategy.Resolve(
            MatchEngineFixtures.Declaration().Entry,
            Input("Unrelated", candidates: [entry]));

        outcome.Entry.Should().BeNull();
        outcome.RejectionReason.Should().NotBeNull();
    }

    [Test]
    public void TheMatchedLayerReportsItsPreferredSpace()
    {
        var declaration = MatchEngineFixtures.Declaration().Entry;
        declaration = declaration with
        {
            Layers = [.. declaration.Layers.Select(layer =>
                string.Equals(layer.LayerId, "alternative-titles", StringComparison.Ordinal)
                    ? layer with { PreferSpaceId = "community" }
                    : layer)],
        };

        var aliased = MatchEngineFixtures.Entry(2, "Something Else", alternativeTitles: ["Alpha"]);

        var outcome = Strategy.Resolve(declaration, Input("Alpha", candidates: [aliased]));

        outcome.PreferSpaceId.Should().Be(
            "community",
            "which spelling matched carries numbering information for unit resolution");
    }
}
