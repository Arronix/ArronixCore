using System.Linq;
using System.Text.Json;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Metadata;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

// The shape, providers and definition contracts are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0015
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The mapping half of the metadata engine, exercised without any network: path evaluation, the closed
/// converter set, identifier rules, member re-entry and the five derivation kinds.
/// </summary>
[TestFixture]
internal sealed class MetadataEngineMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock() => new(Now);

    private static CatalogIdRules IdRules() => new(
        [
            new IdNormalization { Scheme = "ext", Kind = IdRuleKind.PrefixPad, Prefix = "tt", PadDigitsTo = 7 },
            new IdNormalization { Scheme = "ext", Kind = IdRuleKind.TypedPrefix, Prefixes = ["ext:"] },
            new IdNormalization
            {
                Scheme = "cat",
                Kind = IdRuleKind.UrlSegment,
                AddressPattern = "catalog.example/thing/{id}",
                StripSlugAfterDigits = true,
            },
            new IdNormalization { Kind = IdRuleKind.TrailingYearSplit, YearLowerBound = 1870, YearUpperBoundYearsFromNow = 1 },
        ],
        Clock());

    [Test]
    public void ThePathSubsetWalksObjectsArraysAndLength()
    {
        using var document = JsonDocument.Parse(
            """{"a":{"b":7},"list":[{"v":1},{"v":2}],"empty":[]}""");
        var root = document.RootElement;

        JsonPathReader.FirstText(root, "$.a.b").Should().Be("7");
        JsonPathReader.Evaluate(root, "$.list[*].v").Select(JsonPathReader.Text).Should().Equal("1", "2");
        JsonPathReader.FirstText(root, "$.list.length").Should().Be("2");
        JsonPathReader.Evaluate(root, "$.missing.path").Should().BeEmpty();
    }

    [Test]
    public void PrefixPadRestoresThePrefixAndZeroPads()
        => IdRules().Normalize(ExternalId.Of("ext", "816692")).Value.Should().Be("tt0816692");

    [Test]
    public void TypedPrefixAndAddressFormsAreRecognized()
    {
        IdRules().TryRecognize("ext:816692", out var typed).Should().BeTrue();
        typed.Value.Should().Be("tt0816692");

        IdRules().TryRecognize("https://catalog.example/thing/603-the-fixture", out var pasted).Should().BeTrue();
        pasted.Scheme.Should().Be("cat");
        pasted.Value.Should().Be("603");
    }

    [Test]
    public void TheTrailingYearSplitHonorsItsBounds()
    {
        IdRules().TrySplitTrailingYear("Arrival 2016", out var title, out var year).Should().BeTrue();
        title.Should().Be("Arrival");
        year.Should().Be(2016);

        // Next year is inside the declared one-year slack; the year after is not.
        IdRules().TrySplitTrailingYear("Arrival 2027", out _, out _).Should().BeTrue();
        IdRules().TrySplitTrailingYear("Arrival 2028", out _, out _).Should().BeFalse();
        IdRules().TrySplitTrailingYear("Arrival 1850", out _, out _).Should().BeFalse();
    }

    [Test]
    public void ConvertersProduceTypedFieldValues()
    {
        var converters = new CatalogValueConverters(IdRules());

        using var document = JsonDocument.Parse(
            """{"n":9,"d":"2020-05-01","m":136,"u":"https://a.example/x","bad":"not-a-date"}""");
        var root = document.RootElement;

        converters.Convert(JsonPathReader.Evaluate(root, "$.n"), "int")!.Number.Should().Be(9);
        converters.Convert(JsonPathReader.Evaluate(root, "$.d"), "date")!.Instant!.Value.Year.Should().Be(2020);
        converters.Convert(JsonPathReader.Evaluate(root, "$.m"), "minutes")!.Duration.Should().Be(TimeSpan.FromMinutes(136));
        converters.Convert(JsonPathReader.Evaluate(root, "$.u"), "absolute-uri")!.Link.Should().NotBeNull();
        converters.Convert(JsonPathReader.Evaluate(root, "$.bad"), "date").Should().BeNull();
    }

    [Test]
    public void KeepEmptyHoldsPositionsAndDistinctDropsRepeats()
    {
        var converters = new CatalogValueConverters(IdRules());

        using var document = JsonDocument.Parse(
            """{"rows":[{"t":"x"},{"t":""},{"t":"x"}]}""");
        var matches = JsonPathReader.Evaluate(document.RootElement, "$.rows[*].t");

        converters.Convert(matches, "keep-empty")!.Items!.Select(item => item.Text)
            .Should().Equal("x", string.Empty, "x");

        // Distinct drops empties and repeats both: it serves de-duplicated title lists, never
        // position-correlated triples.
        converters.Convert(matches, "distinct")!.Items!.Select(item => item.Text)
            .Should().Equal("x");
    }

    [Test]
    public void AnUnknownConverterRefusesTheDeclaration()
    {
        var declaration = Declaration() with
        {
            Responses =
            [
                new ResponseMap
                {
                    LevelId = "entry",
                    ExternalIdPath = "$.id",
                    ExternalIdScheme = "cat",
                    Rows = [new ResponseMapRow { JsonPath = "$.title", FieldId = "title", Converter = "sparkle" }],
                },
            ],
        };

        var construction = () => new CatalogResponseMapper(
            declaration, NamingEngineTestSupport.Shape(), new CatalogValueConverters(IdRules()));

        construction.Should().Throw<ArgumentException>().WithMessage("*sparkle*");
    }

    [Test]
    public void AGroupResponseReEntersItsMembersThroughTheMemberMap()
    {
        var mapper = new CatalogResponseMapper(
            Declaration(), NamingEngineTestSupport.Shape(), new CatalogValueConverters(IdRules()));

        using var document = JsonDocument.Parse(
            """{"id":11,"name":"The Set","parts":[{"id":21,"title":"First"},{"id":22,"title":"Second"}]}""");

        var nodes = mapper.Map(document.RootElement, mapper.MapForAxis("set")!);

        nodes.Should().HaveCount(3);
        nodes[0].Id.Value.Should().Be("11");
        nodes[1].ParentId.Should().Be(nodes[0].Id);
        nodes[1].Title.Should().Be("First");
        nodes[2].Title.Should().Be("Second");
    }

    [Test]
    public void StatusStagesApplyTheTheatricalWindowClause()
    {
        var rule = StatusRule();
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            // Shown 120 days ago, no home dates recorded: past the 90-day window, so released.
            ["shown"] = FieldValue.OfInstant(Now.AddDays(-120)),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields["status"].Text.Should().Be("released");

        // Shown 30 days ago: inside the window, so the middle stage holds.
        fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["shown"] = FieldValue.OfInstant(Now.AddDays(-30)),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields["status"].Text.Should().Be("shown");

        // A home date in the future holds the middle stage; a past one releases.
        fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["shown"] = FieldValue.OfInstant(Now.AddDays(-120)),
            ["home"] = FieldValue.OfInstant(Now.AddDays(30)),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields["status"].Text.Should().Be("shown");
    }

    [Test]
    public void DateReductionTakesTheEarliestThenTheFallback()
    {
        var rule = new DerivationRule
        {
            RuleId = "release-date",
            TargetFieldId = "releaseDate",
            Kind = DerivationKind.DateReduction,
            Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["reduce"] = FieldValue.OfText("min(home, digital) ?? shown"),
            },
        };

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["home"] = FieldValue.OfInstant(Now.AddDays(10)),
            ["digital"] = FieldValue.OfInstant(Now.AddDays(5)),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields["releaseDate"].Instant.Should().Be(Now.AddDays(5));

        fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["shown"] = FieldValue.OfInstant(Now.AddDays(-1)),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields["releaseDate"].Instant.Should().Be(Now.AddDays(-1));
    }

    [Test]
    public void RegionSelectNeverFallsBackAcrossRegionsUnlessDeclared()
    {
        var rule = new DerivationRule
        {
            RuleId = "rating-region",
            TargetFieldId = "rating",
            Kind = DerivationKind.RegionSelect,
            Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["source"] = FieldValue.OfText("$.ratings[*]"),
                ["regionKey"] = FieldValue.OfText("country"),
                ["valueKey"] = FieldValue.OfText("value"),
                ["regionSetting"] = FieldValue.OfText("region"),
                ["defaultRegion"] = FieldValue.OfText("US"),
                ["fallbackToAnyRegion"] = FieldValue.OfBoolean(false),
            },
        };

        using var document = JsonDocument.Parse(
            """{"ratings":[{"country":"DE","value":"FSK 12"},{"country":"GB","value":"12A"}]}""");

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        CatalogDerivations.Apply([rule], fields, document.RootElement, Settings(("region", "GB")), Now);
        fields["rating"].Text.Should().Be("12A");

        fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        CatalogDerivations.Apply([rule], fields, document.RootElement, Settings(), Now);
        fields.Should().NotContainKey("rating");
    }

    [Test]
    public void ImageRoleSelectTakesTheFirstAbsoluteAddressPerRole()
    {
        var rule = new DerivationRule
        {
            RuleId = "image-roles",
            Kind = DerivationKind.ImageRoleSelect,
            Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["source"] = FieldValue.OfText("$.images[*]"),
                ["roleKey"] = FieldValue.OfText("kind"),
                ["urlKey"] = FieldValue.OfText("url"),
                ["roles"] = FieldValue.OfText("cover->cover, wide->wide"),
                ["requireAbsoluteUri"] = FieldValue.OfBoolean(true),
            },
        };

        using var document = JsonDocument.Parse(
            """{"images":[{"kind":"cover","url":"relative.jpg"},{"kind":"cover","url":"https://a.example/c.jpg"},{"kind":"wide","url":"https://a.example/w.jpg"}]}""");

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        CatalogDerivations.Apply([rule], fields, document.RootElement, Settings(), Now);

        fields["cover"].Link!.ToString().Should().Be("https://a.example/c.jpg");
        fields["wide"].Link.Should().NotBeNull();
    }

    [Test]
    public void ConditionalWritesOnlyWhenTheExtractsDisagree()
    {
        var rule = new DerivationRule
        {
            RuleId = "secondary-year",
            TargetFieldId = "secondaryYear",
            Kind = DerivationKind.Conditional,
            Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["sourceField"] = FieldValue.OfText("premiere"),
                ["extract"] = FieldValue.OfText("year"),
                ["notEqualToField"] = FieldValue.OfText("year"),
            },
        };

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["premiere"] = FieldValue.OfInstant(new DateTimeOffset(2019, 12, 30, 0, 0, 0, TimeSpan.Zero)),
            ["year"] = FieldValue.OfInteger(2020),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields["secondaryYear"].Number.Should().Be(2019);

        fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["premiere"] = FieldValue.OfInstant(new DateTimeOffset(2020, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            ["year"] = FieldValue.OfInteger(2020),
        };

        CatalogDerivations.Apply([rule], fields, default, Settings(), Now);
        fields.Should().NotContainKey("secondaryYear");
    }

    private static DerivationRule StatusRule() => new()
    {
        RuleId = "status-stages",
        TargetFieldId = "status",
        Kind = DerivationKind.StatusStages,
        Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["stages"] = FieldValue.OfText("announced<shown<released"),
            ["cinemaField"] = FieldValue.OfText("shown"),
            ["homeFields"] = FieldValue.OfText("home,digital"),
            ["theatricalWindowDays"] = FieldValue.OfInteger(90),
        },
    };

    private static IReadOnlyDictionary<string, string> Settings(params (string Key, string Value)[] settings)
        => settings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    internal static CatalogDeclaration Declaration() => new()
    {
        Requests =
        [
            new RequestTemplate { RequestId = "fetch", Verb = "GET", Route = "thing/{catId}" },
            new RequestTemplate { RequestId = "fetch-by-ext", Verb = "GET", Route = "thing/ext/{extId}" },
            new RequestTemplate { RequestId = "group", Verb = "GET", Route = "group/{groupCatId}" },
            new RequestTemplate
            {
                RequestId = "search",
                Verb = "GET",
                Route = "search",
                Query = [new RequestParameter("q", "{text:query:plus-separated}"), new RequestParameter("year", "{year?}")],
            },
            new RequestTemplate
            {
                RequestId = "changed",
                Verb = "GET",
                Route = "changed",
                Query = [new RequestParameter("since", "{since:iso8601}")],
            },
        ],
        Responses =
        [
            new ResponseMap
            {
                LevelId = "entry",
                ExternalIdPath = "$.id",
                ExternalIdScheme = "cat",
                Rows =
                [
                    new ResponseMapRow { JsonPath = "$.title", FieldId = "title" },
                    new ResponseMapRow { JsonPath = "$.year", FieldId = "year", Converter = "int" },
                    new ResponseMapRow { JsonPath = "$.extId", FieldId = "extId", Converter = "ext-id" },
                ],
            },
            new ResponseMap
            {
                AxisId = "set",
                ExternalIdPath = "$.id",
                ExternalIdScheme = "group-cat",
                MemberPath = "$.parts[*]",
                Rows = [new ResponseMapRow { JsonPath = "$.name", FieldId = "title" }],
            },
        ],
        IdRules =
        [
            new IdNormalization { Scheme = "ext", Kind = IdRuleKind.PrefixPad, Prefix = "tt", PadDigitsTo = 7 },
            new IdNormalization { Scheme = "ext", Kind = IdRuleKind.TypedPrefix, Prefixes = ["ext:"] },
            new IdNormalization { Kind = IdRuleKind.TrailingYearSplit, YearLowerBound = 1870, YearUpperBoundYearsFromNow = 1 },
        ],
        Delta = new DeltaSyncPolicy { BackoffMinutes = 15, FloorTo = TimeFloor.Hour },
        Settings =
        [
            new Abstractions.Providers.SettingsField
            {
                FieldId = "baseUrl",
                Name = "URL",
                ValueKind = FieldValueKind.Link,
                Role = Abstractions.Providers.SettingRole.Endpoint,
                DefaultValue = "https://catalog.example/api",
            },
            new Abstractions.Providers.SettingsField
            {
                FieldId = "region",
                Name = "Region",
                ValueKind = FieldValueKind.Text,
                Role = Abstractions.Providers.SettingRole.Value,
                DefaultValue = "US",
            },
        ],
    };
}
