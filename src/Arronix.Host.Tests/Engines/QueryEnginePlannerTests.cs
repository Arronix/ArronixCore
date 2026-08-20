using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Search;
using FluentAssertions;


namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The declarative query templater: identifier tiers before text fallback, alias fan-out under the
/// declared constraints, origin behavior, and the coordinate grammar.
/// </summary>
[TestFixture]
internal sealed class QueryEnginePlannerTests
{
    private static readonly MediaKindId Kind = MediaKindId.FromString("fixture");
    private static readonly MediaLevelId WorkLevel = MediaLevelId.FromString("work");

    private static readonly AcquisitionScope SingleScope = new() { Kind = AcquisitionScopeKind.Single };

    private static readonly IReadOnlyList<SearchKind> SearchKinds =
    [
        new SearchKind
        {
            SearchKindId = "full",
            Name = "Full",
            TargetLevelId = WorkLevel,
            Scope = SingleScope,
            Categories = [CategoryId.FromInt(2000)],
        },
        new SearchKind
        {
            SearchKindId = "full-id",
            Name = "Full by identifier",
            TargetLevelId = WorkLevel,
            Scope = SingleScope,
            Categories = [CategoryId.FromInt(2000), CategoryId.FromInt(2010)],
        },
    ];

    private static QueryDeclaration Declaration() => new()
    {
        Tiers =
        [
            new QueryTierTemplate
            {
                TierId = "identifier",
                SearchKindId = "full-id",
                Order = 1,
                Arguments =
                [
                    new QueryArgument(SearchTerm.ExternalIdentifier, "{catId}", Scheme: "cat"),
                    new QueryArgument(SearchTerm.ExternalIdentifier, "{dogId}", Scheme: "dog", OmitWhenAbsent: true),
                ],
                FreeTextTemplate = "{title:query}",
                CarryAliases = true,
            },
            new QueryTierTemplate
            {
                TierId = "text",
                SearchKindId = "full",
                Order = 2,
                FreeTextTemplate = "{title:query} {year}",
                RequiredFields = ["year"],
                FanOutPerAlias = true,
                Arguments =
                [
                    new QueryArgument(SearchTerm.WorkTitle, "{title:query}"),
                    new QueryArgument(SearchTerm.Year, "{year}"),
                ],
                CarryAliases = true,
            },
            new QueryTierTemplate
            {
                TierId = "sweep",
                SearchKindId = "full",
                Order = 1,
                Origins = [SearchOrigin.Rss],
                FreeTextTemplate = string.Empty,
            },
        ],
        Aliases =
        [
            new AliasTemplate { AliasId = "display-title", Template = "{title:query}", Order = 1 },
            new AliasTemplate { AliasId = "original-title", Template = "{originalTitle:query}", Order = 2 },
            new AliasTemplate
            {
                AliasId = "translated-titles",
                Template = "{translatedTitles:query}",
                Order = 3,
                FilterByAcceptedLanguages = true,
                NeverOwnQuery = true,
            },
        ],
        Grammar = CoordinateGrammar.None,
        Limits =
        [
            new OriginLimit(SearchOrigin.Interactive, 100),
            new OriginLimit(SearchOrigin.Rss, 200),
        ],
        Substitutions = [new CreditSubstitution("Assorted Contributors", "AC")],
    };

    private static ItemView Work(
        long id,
        string title,
        long? year = null,
        string? originalTitle = null,
        IReadOnlyList<ExternalId>? externalIds = null,
        IReadOnlyList<(string Text, string LanguageCode)>? translatedTitles = null,
        CoordinateSet? coordinates = null)
    {
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        if (year is { } declared)
        {
            fields["year"] = FieldValue.OfInteger(declared);
        }

        if (originalTitle is not null)
        {
            fields["originalTitle"] = FieldValue.OfText(originalTitle);
        }

        if (translatedTitles is not null)
        {
            fields["translatedTitles"] = new FieldValue
            {
                Kind = FieldValueKind.Composite,
                Items = translatedTitles
                    .Select(translated => FieldValue.OfComposite(
                    [
                        FieldValue.OfText(translated.Text),
                        FieldValue.OfLanguage(new Language(translated.LanguageCode, translated.LanguageCode)),
                    ]))
                    .ToArray(),
            };
        }

        return new ItemView
        {
            Ref = new MediaItemRef(Kind, WorkLevel, MediaItemId.FromInt64(id)),
            Title = title,
            Fields = fields,
            ExternalIds = externalIds ?? [],
            Coordinates = coordinates ?? CoordinateSet.Empty,
        };
    }

    private sealed class StubReader : IQueryItemReader
    {
        private readonly Dictionary<MediaItemRef, ItemView> _items = [];

        internal StubReader Add(ItemView item)
        {
            _items[item.Ref] = item;
            return this;
        }

        public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.TryGetValue(reference, out var item) ? item : null);
    }

    private static DeclarativeQueryPlanner Planner(
        StubReader reader,
        QueryDeclaration? declaration = null,
        IReadOnlyList<SearchKind>? searchKinds = null) =>
        new(Kind, declaration ?? Declaration(), searchKinds ?? SearchKinds, reader);

    private static AcquisitionRequest Request(
        ItemView unit,
        string searchKindId = "full",
        SearchOrigin origin = SearchOrigin.Interactive,
        IReadOnlyList<Language>? acceptedLanguages = null) => new()
    {
        MediaKind = Kind,
        SearchKindId = searchKindId,
        Units = [unit.Ref],
        Origin = origin,
        AcceptedLanguages = acceptedLanguages ?? [],
    };

    [Test]
    public async Task TheIdentifierTierPlansBeforeTheTextTier()
    {
        var work = Work(
            1,
            "Dune",
            year: 2021,
            externalIds: [ExternalId.Of("cat", 438631), ExternalId.Of("dog", "tt1160419")]);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        plan.Tiers.Should().HaveCount(2, "identifier at order 1, text at order 2");

        var identifier = plan.Tiers[0].Queries.Should().ContainSingle().Subject;
        identifier.SearchKindId.Should().Be("full-id");
        identifier.Arguments.Should().HaveCount(2, "both identifiers ride in ONE query");
        identifier.Arguments[0].Value.External.Should().Be(
            ExternalId.Of("cat", 438631),
            "the declared argument order puts the preferred scheme first");
        identifier.Arguments[1].Value.External.Should().Be(ExternalId.Of("dog", "tt1160419"));
        identifier.FreeText.Should().Be("Dune", "free text rides along for sources that filter text by id");

        plan.Tiers[1].Queries.Should().OnlyContain(query => query.SearchKindId == "full");
    }

    [Test]
    public async Task AMissingOptionalIdentifierIsOmittedNotFatal()
    {
        var work = Work(1, "Dune", year: 2021, externalIds: [ExternalId.Of("cat", 438631)]);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        var identifier = plan.Tiers[0].Queries.Should().ContainSingle().Subject;
        identifier.Arguments.Should().ContainSingle("the 'dog' identifier is declared OmitWhenAbsent");
    }

    [Test]
    public async Task AMissingRequiredIdentifierSkipsTheTierForTheUnit()
    {
        var work = Work(1, "Dune", year: 2021);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        plan.Tiers.Should().HaveCount(1, "without the required identifier only the text tier plans");
        plan.Tiers[0].Queries.Should().OnlyContain(query => query.SearchKindId == "full");
    }

    [Test]
    public async Task TheTextTierFansOutOnePerSpellingWithTheYearAppended()
    {
        var work = Work(1, "Dune", year: 2021, originalTitle: "Diuna");

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        var texts = plan.Tiers.Single().Queries.Select(query => query.FreeText).ToArray();
        texts.Should().BeEquivalentTo(
            new[] { "Dune 2021", "Diuna 2021" },
            options => options.WithStrictOrdering(),
            "one query per spelling, most canonical first, year appended");
    }

    [Test]
    public async Task AWorkWithoutItsRequiredFieldGetsNoTextQueryAtAll()
    {
        // "Dune" alone is the worst query this kind can make; an unreleased work has nothing to
        // search for yet.
        var work = Work(1, "Dune", externalIds: [ExternalId.Of("cat", 438631)]);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        plan.Tiers.Should().HaveCount(1);
        plan.Tiers[0].Queries.Should().OnlyContain(query => query.SearchKindId == "full-id");
    }

    [Test]
    public async Task TranslatedSpellingsRideAsAliasesOnlyAndAreLanguageFiltered()
    {
        var work = Work(
            1,
            "Dune",
            year: 2021,
            translatedTitles: [("La Dune", "fr"), ("Der Wanderplanet", "de")]);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(
            work,
            acceptedLanguages: [new Language("fr", "French")]));

        var textQueries = plan.Tiers.Single().Queries;
        textQueries.Select(query => query.FreeText).Should().BeEquivalentTo(
            new[] { "Dune 2021" },
            "a never-own-query row multiplies aliases, never searches");

        textQueries[0].Aliases.Should().Contain("La Dune", "French is accepted")
            .And.NotContain("Der Wanderplanet", "German is not");
    }

    [Test]
    public async Task TheSweepDisplacesTheGeneralTiersForItsOrigin()
    {
        var work = Work(1, "Dune", year: 2021, externalIds: [ExternalId.Of("cat", 438631)]);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(
            work,
            origin: SearchOrigin.Rss));

        var sweep = plan.Tiers.Should().ContainSingle().Subject.Queries.Should().ContainSingle().Subject;
        sweep.FreeText.Should().BeEmpty("a sweep names nothing");
        sweep.Limit.Should().Be(200, "the declared origin limit applies");
        sweep.Categories.Should().BeEquivalentTo(new[] { CategoryId.FromInt(2000) });
    }

    [Test]
    public async Task TheOriginLimitAppliesToEveryPlannedQuery()
    {
        var work = Work(1, "Dune", year: 2021, externalIds: [ExternalId.Of("cat", 438631)]);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        plan.Tiers.SelectMany(tier => tier.Queries).Should().OnlyContain(query => query.Limit == 100);
    }

    [Test]
    public async Task ABareFieldArgumentCarriesTheTypedValueNotItsSpelling()
    {
        var work = Work(1, "Dune", year: 2021);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        var textQuery = plan.Tiers[0].Queries[0];
        var yearArgument = textQuery.Arguments.Single(argument => argument.Term == SearchTerm.Year);
        yearArgument.Value.Kind.Should().Be(FieldValueKind.Integer);
        yearArgument.Value.Number.Should().Be(2021);
    }

    [Test]
    public async Task ACreditedNameSubstitutionRewritesTheSpelling()
    {
        var work = Work(1, "Assorted Contributors", year: 2001);

        var plan = await Planner(new StubReader().Add(work)).PlanAsync(Request(work));

        plan.Tiers[0].Queries.Select(query => query.FreeText).Should().Contain(
            "AC 2001",
            "the community writes the substitute, so queries must too");
    }

    [Test]
    public async Task ACoordinateSpellingRendersZeroPaddedComponents()
    {
        var declaration = Declaration() with
        {
            Tiers =
            [
                new QueryTierTemplate
                {
                    TierId = "positioned",
                    SearchKindId = "full",
                    Order = 1,
                    FreeTextTemplate = "{title:query} {coord:grid}",
                },
            ],
            Aliases = [],
            Grammar = new CoordinateGrammar
            {
                Spellings =
                [
                    new CoordinateSpelling { SpellingId = "grid", SpaceId = "grid", Template = "S{00}E{00}" },
                ],
            },
        };

        var work = Work(
            1,
            "Alpha",
            year: 2020,
            coordinates: CoordinateSet.Of(
                new CoordinateReading("grid", Coordinate.OfOrdinals(OrdinalPath.Of(1, 5)), CoordinateConfidence.Verified)));

        var plan = await Planner(new StubReader().Add(work), declaration).PlanAsync(Request(work));

        plan.Tiers.Should().ContainSingle()
            .Which.Queries.Should().ContainSingle()
            .Which.FreeText.Should().Be("Alpha S01E05");
    }

    [Test]
    public async Task TwoUnitsRenderingTheSameQueryAreDeduplicatedWithinTheTier()
    {
        var first = Work(1, "Dune", year: 2021);
        var second = Work(2, "Dune", year: 2021);
        var reader = new StubReader().Add(first).Add(second);

        var request = Request(first) with { Units = [first.Ref, second.Ref] };
        var plan = await Planner(reader).PlanAsync(request);

        plan.Tiers[0].Queries.Should().ContainSingle("identical renderings collapse to one query");
    }

    [Test]
    public void ATierNamingAnUndeclaredSearchKindRefusesTheEngineAtConstruction()
    {
        var declaration = Declaration() with
        {
            Tiers =
            [
                new QueryTierTemplate
                {
                    TierId = "broken",
                    SearchKindId = "no-such-kind",
                    FreeTextTemplate = "{title}",
                },
            ],
        };

        var act = () => Planner(new StubReader(), declaration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*broken*")
            .WithMessage("*no-such-kind*");
    }

    [Test]
    public async Task ARequestNamingAnUndeclaredSearchKindIsRefused()
    {
        var work = Work(1, "Dune", year: 2021);
        var planner = Planner(new StubReader().Add(work));

        var act = () => planner.PlanAsync(Request(work, searchKindId: "no-such-kind"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
