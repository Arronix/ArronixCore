using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Matching;
using FluentAssertions;


namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The declarative matcher end to end: the identifier short-circuit, scope replacement, unit
/// resolution and the confidence rows, all against the seam the imperative plugins implement.
/// </summary>
[TestFixture]
internal sealed class MatchEngineMatcherTests
{
    [Test]
    public async Task ATitleAndYearResolveThroughTheSingletonUnit()
    {
        var reader = new MatchEngineFixtures.StubReader()
            .Add(
                MatchEngineFixtures.Entry(1, "Alpha", year: 2020),
                MatchEngineFixtures.SingletonUnit(11, "Alpha"));

        var matcher = MatchEngineFixtures.Matcher(MatchEngineFixtures.Declaration(), reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request("Alpha", parsedYear: "2020"));

        outcome.Units.Should().ContainSingle().Which.Should().Be(MatchEngineFixtures.UnitRef(11));
        outcome.Basis.Should().Be(MatchBasis.TitleWithYear);
        outcome.Confidence.Should().Be(MatchConfidence.High);
    }

    [Test]
    public async Task IdentifiersShortCircuitTitleResolutionInDeclaredPrecedence()
    {
        // 'aaa' is declared before 'bbb': a request carrying both trusts the 'aaa' resolution even when
        // 'bbb' is listed first on the request (MoviesReleaseMatcher.cs:134-135 generalization).
        var wanted = MatchEngineFixtures.Entry(
            1,
            "Completely Different",
            externalIds: [ExternalId.Of("aaa", 7)]);
        var decoy = MatchEngineFixtures.Entry(
            2,
            "Also Different",
            externalIds: [ExternalId.Of("bbb", 9)]);

        var reader = new MatchEngineFixtures.StubReader()
            .Add(wanted, MatchEngineFixtures.SingletonUnit(11, "Completely Different"))
            .Add(decoy, MatchEngineFixtures.SingletonUnit(22, "Also Different"));

        var matcher = MatchEngineFixtures.Matcher(MatchEngineFixtures.Declaration(), reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Some Release Name",
            externalIds: [ExternalId.Of("bbb", 9), ExternalId.Of("aaa", 7)]));

        outcome.Units.Should().ContainSingle().Which.Should().Be(MatchEngineFixtures.UnitRef(11));
        outcome.Basis.Should().Be(MatchBasis.Identifier);
        outcome.Confidence.Should().Be(MatchConfidence.Exact);
    }

    [Test]
    public async Task AnIdentifierWhoseEntryDisagreesWithTheReadingIsSkippedNotTrusted()
    {
        // The wrong-identifier defense: the year agreement guards every identifier resolution.
        var mislabeled = MatchEngineFixtures.Entry(
            1,
            "Alpha",
            year: 1994,
            externalIds: [ExternalId.Of("aaa", 7)]);
        var actual = MatchEngineFixtures.Entry(2, "Alpha", year: 2017);

        var reader = new MatchEngineFixtures.StubReader()
            .Add(mislabeled, MatchEngineFixtures.SingletonUnit(11, "Alpha"))
            .Add(actual, MatchEngineFixtures.SingletonUnit(22, "Alpha"));

        var matcher = MatchEngineFixtures.Matcher(MatchEngineFixtures.Declaration(), reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha",
            parsedYear: "2017",
            externalIds: [ExternalId.Of("aaa", 7)]));

        outcome.Units.Should().ContainSingle().Which.Should().Be(MatchEngineFixtures.UnitRef(22));
        outcome.Basis.Should().Be(MatchBasis.TitleWithYear);
        outcome.Warnings.Should().ContainSingle(warning => warning.Contains("aaa:7"));
    }

    [Test]
    public async Task AScopeReplacesTheSearchAndAgreementReportsScopeBasis()
    {
        var scoped = MatchEngineFixtures.Entry(1, "Alpha", year: 2020);
        var other = MatchEngineFixtures.Entry(2, "Beta", year: 2020);

        var reader = new MatchEngineFixtures.StubReader()
            .Add(scoped, MatchEngineFixtures.SingletonUnit(11, "Alpha"))
            .Add(other, MatchEngineFixtures.SingletonUnit(22, "Beta"));

        var matcher = MatchEngineFixtures.Matcher(MatchEngineFixtures.Declaration(), reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha",
            scope: MatchEngineFixtures.EntryRef(1)));

        outcome.Units.Should().ContainSingle().Which.Should().Be(MatchEngineFixtures.UnitRef(11));
        outcome.Basis.Should().Be(MatchBasis.Scope);
        outcome.Confidence.Should().Be(MatchConfidence.Medium);
    }

    [Test]
    public async Task TextDisagreeingWithTheScopeIsARejectionNeverAMatchElsewhere()
    {
        var scoped = MatchEngineFixtures.Entry(1, "Alpha", year: 2020);
        var elsewhere = MatchEngineFixtures.Entry(2, "Beta", year: 2020);

        var reader = new MatchEngineFixtures.StubReader()
            .Add(scoped, MatchEngineFixtures.SingletonUnit(11, "Alpha"))
            .Add(elsewhere, MatchEngineFixtures.SingletonUnit(22, "Beta"));

        var matcher = MatchEngineFixtures.Matcher(MatchEngineFixtures.Declaration(), reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Beta",
            scope: MatchEngineFixtures.EntryRef(1)));

        outcome.Units.Should().BeEmpty();
        outcome.RejectionReason.Should().Contain(MatchEngineFixtures.EntryRef(1).ToString());
    }

    [Test]
    public async Task CoordinatesAddressUnitsWithinAScopedEntryAndTheBasisSaysSo()
    {
        var entry = MatchEngineFixtures.Entry(1, "Alpha", year: 2020);
        var reader = new MatchEngineFixtures.StubReader().Add(
            entry,
            MatchEngineFixtures.Unit(
                11,
                "First",
                new CoordinateReading("abs", Coordinate.OfOrdinals(OrdinalPath.Of(1)), CoordinateConfidence.Verified)),
            MatchEngineFixtures.Unit(
                12,
                "Second",
                new CoordinateReading("abs", Coordinate.OfOrdinals(OrdinalPath.Of(2)), CoordinateConfidence.Verified)));

        var declaration = MatchEngineFixtures.Declaration();
        declaration = declaration with
        {
            Units = [new UnitResolutionRule { Spaces = [new SpaceAttempt { SpaceId = "abs" }] }],
        };

        var matcher = MatchEngineFixtures.Matcher(declaration, reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha",
            scope: MatchEngineFixtures.EntryRef(1),
            coordinates: CoordinateSet.Of(
                new CoordinateReading("abs", Coordinate.OfOrdinals(OrdinalPath.Of(2)), CoordinateConfidence.Asserted))));

        outcome.Units.Should().ContainSingle().Which.Should().Be(MatchEngineFixtures.UnitRef(12));
        outcome.Basis.Should().Be(
            MatchBasis.Coordinate,
            "coordinates addressed the unit within an already-resolved entry");
        outcome.Coordinates.TryGet("abs", out var used).Should().BeTrue();
        used.Confidence.Should().Be(CoordinateConfidence.Asserted);
    }

    [Test]
    public async Task ASpanReadingExpandsToItsSequenceMembers()
    {
        var entry = MatchEngineFixtures.Entry(1, "Alpha", year: 2020);
        var reader = new MatchEngineFixtures.StubReader().Add(
            entry,
            MatchEngineFixtures.Unit(
                11,
                "One-One",
                new CoordinateReading("grid", Coordinate.OfOrdinals(OrdinalPath.Of(1, 1)), CoordinateConfidence.Verified)),
            MatchEngineFixtures.Unit(
                12,
                "One-Two",
                new CoordinateReading("grid", Coordinate.OfOrdinals(OrdinalPath.Of(1, 2)), CoordinateConfidence.Verified)),
            MatchEngineFixtures.Unit(
                21,
                "Two-One",
                new CoordinateReading("grid", Coordinate.OfOrdinals(OrdinalPath.Of(2, 1)), CoordinateConfidence.Verified)));

        var declaration = MatchEngineFixtures.Declaration();
        declaration = declaration with
        {
            Units =
            [
                new UnitResolutionRule
                {
                    Spaces = [new SpaceAttempt { SpaceId = "grid" }],
                    Expansion = SpanExpansion.SequenceMembers,
                },
            ],
        };

        var matcher = MatchEngineFixtures.Matcher(declaration, reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha",
            scope: MatchEngineFixtures.EntryRef(1),
            coordinates: CoordinateSet.Of(
                new CoordinateReading("grid", Coordinate.OfOrdinals(OrdinalPath.Of(1)), CoordinateConfidence.Asserted))));

        outcome.Units.Should().BeEquivalentTo(
            new[] { MatchEngineFixtures.UnitRef(11), MatchEngineFixtures.UnitRef(12) });
    }

    [Test]
    public async Task ATitleLookupAttemptResolvesTheUnitNumberingCouldNot()
    {
        // The F-1 amendment: a release naming a unit by title, resolved where the store lives.
        var entry = MatchEngineFixtures.Entry(1, "Alpha", year: 2020);
        var reader = new MatchEngineFixtures.StubReader().Add(
            entry,
            MatchEngineFixtures.Unit(
                11,
                "Ordinary Part",
                new CoordinateReading("abs", Coordinate.OfOrdinals(OrdinalPath.Of(1)), CoordinateConfidence.Verified)),
            MatchEngineFixtures.Unit(
                12,
                "Winter Special",
                new CoordinateReading("abs", Coordinate.OfOrdinals(OrdinalPath.Of(2)), CoordinateConfidence.Verified)));

        var declaration = MatchEngineFixtures.Declaration();
        declaration = declaration with
        {
            Units =
            [
                new UnitResolutionRule
                {
                    Spaces =
                    [
                        new SpaceAttempt { SpaceId = "abs" },
                        new SpaceAttempt
                        {
                            SpaceId = "abs",
                            Kind = SpaceAttemptKind.TitleLookup,
                            NormalizerId = MatchKeyNormalizers.CleanTitle,
                        },
                    ],
                },
            ],
        };

        var matcher = MatchEngineFixtures.Matcher(declaration, reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha Winter Special",
            parsedTitle: "Alpha",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DeclarativeMatcher.UnitTitleMetadataKey] = "Winter Special",
            }));

        outcome.Units.Should().ContainSingle().Which.Should().Be(MatchEngineFixtures.UnitRef(12));
        outcome.Coordinates.TryGet("abs", out var stamped).Should().BeTrue(
            "the successful lookup is stamped with the attempt's space");
        stamped.Value.Ordinals.Should().Be(OrdinalPath.Of(2));
    }

    [Test]
    public async Task TheMatchedLayersPreferredSpaceSteersAttemptOrder()
    {
        var entry = MatchEngineFixtures.Entry(2, "Something Else", alternativeTitles: ["Alpha"]);
        var reader = new MatchEngineFixtures.StubReader().Add(
            entry,
            MatchEngineFixtures.Unit(
                11,
                "First",
                new CoordinateReading("canon", Coordinate.OfOrdinals(OrdinalPath.Of(5)), CoordinateConfidence.Verified),
                new CoordinateReading("community", Coordinate.OfOrdinals(OrdinalPath.Of(7)), CoordinateConfidence.Verified)),
            MatchEngineFixtures.Unit(
                12,
                "Second",
                new CoordinateReading("canon", Coordinate.OfOrdinals(OrdinalPath.Of(7)), CoordinateConfidence.Verified),
                new CoordinateReading("community", Coordinate.OfOrdinals(OrdinalPath.Of(5)), CoordinateConfidence.Verified)));

        var declaration = MatchEngineFixtures.Declaration();
        declaration = declaration with
        {
            Entry = declaration.Entry with
            {
                Layers = [.. declaration.Entry.Layers.Select(layer =>
                    string.Equals(layer.LayerId, "alternative-titles", StringComparison.Ordinal)
                        ? layer with { PreferSpaceId = "community" }
                        : layer)],
            },
            Units =
            [
                new UnitResolutionRule
                {
                    Spaces =
                    [
                        new SpaceAttempt { SpaceId = "canon" },
                        new SpaceAttempt { SpaceId = "community" },
                    ],
                },
            ],
        };

        var matcher = MatchEngineFixtures.Matcher(declaration, reader);

        // The reading states 7 in both spaces; matched through the community alias, the community
        // numbering must be consulted first.
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha",
            coordinates: CoordinateSet.Of(
                new CoordinateReading("canon", Coordinate.OfOrdinals(OrdinalPath.Of(7)), CoordinateConfidence.Asserted),
                new CoordinateReading("community", Coordinate.OfOrdinals(OrdinalPath.Of(7)), CoordinateConfidence.Asserted))));

        outcome.Units.Should().ContainSingle().Which.Should().Be(
            MatchEngineFixtures.UnitRef(11),
            "the alias-matched entry tries the community numbering first");
    }

    [Test]
    public async Task ABindingUnitsExpansionResolvesTheAnchorAndReturnsItsChildren()
    {
        var entry = MatchEngineFixtures.Entry(1, "Alpha", year: 2020);
        var anchor = MatchEngineFixtures.SingletonUnit(11, "Alpha");
        var reader = new MatchEngineFixtures.StubReader()
            .Add(entry, anchor)
            .AddChildren(
                MatchEngineFixtures.UnitRef(11),
                MatchEngineFixtures.Unit(111, "Child One"),
                MatchEngineFixtures.Unit(112, "Child Two"));

        var declaration = MatchEngineFixtures.Declaration();
        declaration = declaration with
        {
            Units =
            [
                new UnitResolutionRule
                {
                    Spaces = [new SpaceAttempt { SpaceId = "only" }],
                    Expansion = SpanExpansion.BindingUnits,
                },
            ],
        };

        var matcher = MatchEngineFixtures.Matcher(declaration, reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request("Alpha", parsedYear: "2020"));

        outcome.Units.Should().BeEquivalentTo(
            new[] { MatchEngineFixtures.UnitRef(111), MatchEngineFixtures.UnitRef(112) },
            options => options.WithStrictOrdering());
    }

    [Test]
    public async Task ConfidenceRowsFilterBySourceInDeclaredOrder()
    {
        var entry = MatchEngineFixtures.Entry(1, "Alpha");
        var reader = new MatchEngineFixtures.StubReader()
            .Add(entry, MatchEngineFixtures.SingletonUnit(11, "Alpha"));

        var matcher = MatchEngineFixtures.Matcher(MatchEngineFixtures.Declaration(), reader);

        var fromFile = await matcher.MatchAsync(MatchEngineFixtures.Request(
            "Alpha",
            source: MatchSource.FileName));

        fromFile.Basis.Should().Be(MatchBasis.TitleOnly);
        fromFile.Confidence.Should().Be(
            MatchConfidence.Low,
            "the Medium row is gated to release names and the generic TitleOnly row is next");
    }

    [Test]
    public async Task AMissingConfidenceRowReportsLowWithAWarningInsteadOfInventingAnAnswer()
    {
        var declaration = MatchEngineFixtures.Declaration() with
        {
            Confidence = [new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact)],
        };

        var entry = MatchEngineFixtures.Entry(1, "Alpha");
        var reader = new MatchEngineFixtures.StubReader()
            .Add(entry, MatchEngineFixtures.SingletonUnit(11, "Alpha"));

        var matcher = MatchEngineFixtures.Matcher(declaration, reader);
        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request("Alpha"));

        outcome.Confidence.Should().Be(MatchConfidence.Low);
        outcome.Warnings.Should().ContainSingle(warning => warning.Contains("No confidence row"));
    }

    [Test]
    public async Task NothingInTheLibraryMeansAReasonedRejection()
    {
        var matcher = MatchEngineFixtures.Matcher(
            MatchEngineFixtures.Declaration(),
            new MatchEngineFixtures.StubReader());

        var outcome = await matcher.MatchAsync(MatchEngineFixtures.Request("Anything"));

        outcome.Units.Should().BeEmpty();
        outcome.Confidence.Should().Be(MatchConfidence.None);
        outcome.RejectionReason.Should().NotBeNull();
    }

    [Test]
    public void AnUnknownNormalizerRefusesTheEngineAtConstruction()
    {
        var declaration = MatchEngineFixtures.Declaration();
        declaration = declaration with
        {
            Entry = declaration.Entry with
            {
                Layers =
                [
                    new MatchLayer
                    {
                        LayerId = "bad",
                        KeyTemplate = "{title}",
                        NormalizerId = "no-such-normalizer",
                    },
                ],
            },
        };

        var act = () => MatchEngineFixtures.Matcher(declaration, new MatchEngineFixtures.StubReader());

        act.Should().Throw<InvalidOperationException>().WithMessage("*no-such-normalizer*");
    }

}
