#pragma warning disable ARX0013 // Shape contracts are experimental; these tests cover an implementation of them.

using System;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv.Tests.Numbering;

/// <summary>
/// Proves that all three addressing schemes survive the whole journey through the shape model.
/// </summary>
/// <remarks>
/// <para>The round trip under test is: <b>release title → parsed position → matched unit → coordinate
/// reading → declared coordinate space → rendered file name</b>. Every one of those steps is a different
/// contract, and the claim the media-shape model has to earn is that the same five contracts carry three
/// mutually incompatible numbering schemes without a schema field, a subclass or a second extension.</para>
/// <para>Each scheme is exercised against a library entry that actually uses it, and the canonical space is
/// exercised against an entry that does <i>not</i> — because the predicates read the release, not the
/// entry, and an entry addressed by flat ordinals still has to resolve a canonical pair correctly.</para>
/// </remarks>
[TestFixture]
public sealed class NumberingRoundTripTests
{
    private TvCatalog _catalog = null!;
    private MediaShape _shape = null!;
    private TvReleaseMatcher _matcher = null!;
    private TvReleaseParser _parser = null!;
    private TvRenamePolicy _naming = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = TvCatalog.CreateSeeded();
        _shape = new TvShape().Shape;
        _matcher = new TvReleaseMatcher(_catalog);
        _parser = new TvReleaseParser();
        _naming = new TvRenamePolicy(_catalog);
    }

    // ---------------------------------------------------------------- scheme 1: the canonical ordinal pair

    [Test]
    public void OrdinalPairIsParsedIntoTheCanonicalSpace()
    {
        Assert.That(
            TvTitleParser.TryParse("The.Expanse.S01E04.1080p.WEB-DL.x264-NTb", out var parsed),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Kind, Is.EqualTo(TvReleaseKind.SeasonEpisode));
            Assert.That(parsed.SeriesTitle, Is.EqualTo("The Expanse"));
            Assert.That(parsed.SeasonNumber, Is.EqualTo(1));
            Assert.That(parsed.EpisodeNumbers, Is.EqualTo(new[] { 4 }));
            Assert.That(parsed.IsMultiUnit, Is.False);
            Assert.That(parsed.Resolution, Is.EqualTo("1080p"));
            Assert.That(parsed.Source, Is.EqualTo("webdl"));
            Assert.That(parsed.ReleaseGroup, Is.EqualTo("NTb"));
        });
    }

    [Test]
    public void OrdinalPairResolvesToOneUnitAndCarriesTheCanonicalReading()
    {
        var outcome = Match("The.Expanse.S01E04.1080p.WEB-DL.x264-NTb", MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.RejectionReason, Is.Null);
            Assert.That(outcome.Units, Has.Count.EqualTo(1));
            Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.High));
            Assert.That(TitleOf(outcome.Units[0]), Is.EqualTo("CQB"));
        });

        AssertReadingIsInDeclaredSpace(outcome.Coordinates, TvIds.AiredSpaceId, CoordinateKind.Ordinal);

        Assert.That(outcome.Coordinates.TryGet(TvIds.AiredSpaceId, out var reading), Is.True);
        Assert.That(reading.Value.Ordinals, Is.EqualTo(OrdinalPath.Of(1, 4)));
    }

    [Test]
    public void TheAlternativeSpellingOfTheOrdinalPairResolvesIdentically()
    {
        var canonical = Match("The.Expanse.S01E04.1080p.WEB-DL-NTb", MatchSource.ReleaseName);
        var crossed = Match("The Expanse 1x04 1080p WEB-DL-NTb", MatchSource.ReleaseName);

        Assert.That(crossed.Units, Is.EqualTo(canonical.Units));
    }

    [Test]
    public void AnOrdinalPairResolvesEvenForAnEntryWhoseDeclaredSchemeIsFlat()
    {
        // The predicates read the release, not the library entry. This is the single most important
        // behavior in the extension and the one a per-kind addressing scheme cannot express.
        var series = _catalog.Series.Single(entry => entry.Title == "Cowboy Bebop");
        Assert.That(series.AddressingScheme, Is.EqualTo(TvAddressingSchemes.Flat));

        // Read off a disk, so the provenance-sensitive alias overlay is out of the picture and the canonical
        // space answers - even though the entry's own declared scheme is the flat one.
        var fromDisk = Match("Cowboy.Bebop.S01E03.1080p.BluRay.x264-GRP", MatchSource.FileName);

        // Read from an indexer, the same coordinate is an alias coordinate, and it resolves one unit lower.
        var fromIndexer = Match("Cowboy.Bebop.S01E03.1080p.BluRay.x264-GRP", MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(fromDisk.Units, Has.Count.EqualTo(1));
            Assert.That(TitleOf(fromDisk.Units[0]), Is.EqualTo("Honky Tonk Women"));
            Assert.That(TitleOf(fromIndexer.Units[0]), Is.EqualTo("Stray Dog Strut"));
        });
    }

    // ---------------------------------------------------------------------- scheme 2: the calendar space

    [Test]
    public void ACalendarDateIsParsedIntoTheDateSpace()
    {
        Assert.That(
            TvTitleParser.TryParse("The.Nightly.Review.2024.01.16.1080p.WEB-DL.x264-GRP", out var parsed),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Kind, Is.EqualTo(TvReleaseKind.AirDate));
            Assert.That(parsed.SeriesTitle, Is.EqualTo("The Nightly Review"));
            Assert.That(parsed.AirDate, Is.EqualTo(new DateOnly(2024, 1, 16)));
            Assert.That(parsed.SeasonNumber, Is.Null, "a date carries no outer ordinal");
        });
    }

    [Test]
    public void ACalendarDateResolvesToOneUnitAndCarriesTheDateReading()
    {
        var outcome = Match("The.Nightly.Review.2024.01.16.1080p.WEB-DL.x264-GRP", MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.RejectionReason, Is.Null);
            Assert.That(outcome.Units, Has.Count.EqualTo(1));
            Assert.That(TitleOf(outcome.Units[0]), Is.EqualTo("Tuesday"));
            Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.High));
        });

        AssertReadingIsInDeclaredSpace(outcome.Coordinates, TvIds.AirDateSpaceId, CoordinateKind.Date);

        Assert.That(outcome.Coordinates.TryGet(TvIds.AirDateSpaceId, out var reading), Is.True);
        Assert.That(reading.Value.Date, Is.EqualTo(new DateOnly(2024, 1, 16)));
    }

    [Test]
    public void ACalendarUnitStillCarriesACanonicalReadingAsWell()
    {
        // The catalog numbers the run even though no release will ever address it that way. Both readings
        // are present at once, which is what a bag of coordinates buys and a pair of columns does not.
        var outcome = Match("The.Nightly.Review.2024.01.16.1080p.WEB-DL-GRP", MatchSource.ReleaseName);
        var unit = _catalog.Episodes.Single(episode => episode.Id == outcome.Units[0].Id.Value);
        var readings = TvCatalog.CoordinatesOf(unit);

        Assert.Multiple(() =>
        {
            Assert.That(readings.TryGet(TvIds.AiredSpaceId, out var canonical), Is.True);
            Assert.That(canonical.Value.Ordinals, Is.EqualTo(OrdinalPath.Of(2024, 2)));
            Assert.That(readings.TryGet(TvIds.AirDateSpaceId, out _), Is.True);
        });
    }

    // ------------------------------------------------------------------- scheme 3: the flat ordinal space

    [Test]
    public void AFlatOrdinalIsParsedIntoTheFlatSpace()
    {
        Assert.That(
            TvTitleParser.TryParse("Cowboy.Bebop.-.05.1080p.BluRay.x264-GRP", out var parsed),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Kind, Is.EqualTo(TvReleaseKind.Absolute));
            Assert.That(parsed.SeriesTitle, Is.EqualTo("Cowboy Bebop"));
            Assert.That(parsed.AbsoluteNumbers, Is.EqualTo(new[] { 5 }));
            Assert.That(parsed.SeasonNumber, Is.Null);
        });
    }

    [Test]
    public void ABracketedGroupPrefixIsUnderstood()
    {
        Assert.That(
            TvTitleParser.TryParse("[SubGroup] Cowboy Bebop - 03 [1080p][HEVC].mkv", out var parsed),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Kind, Is.EqualTo(TvReleaseKind.Absolute));
            Assert.That(parsed.SeriesTitle, Is.EqualTo("Cowboy Bebop"));
            Assert.That(parsed.AbsoluteNumbers, Is.EqualTo(new[] { 3 }));
            Assert.That(parsed.ReleaseGroup, Is.EqualTo("SubGroup"));
        });
    }

    [Test]
    public void AFlatOrdinalFromAFileNameResolvesAgainstTheCatalogSpace()
    {
        // A file name is not a release name, so the provenance-sensitive alias space is not consulted.
        var outcome = Match("Cowboy.Bebop.-.05.1080p.BluRay.x264-GRP", MatchSource.FileName);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Units, Has.Count.EqualTo(1));
            Assert.That(TitleOf(outcome.Units[0]), Is.EqualTo("Ballad of Fallen Angels"));
            Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.High));
            Assert.That(outcome.Warnings, Is.Empty);
        });

        AssertReadingIsInDeclaredSpace(outcome.Coordinates, TvIds.AbsoluteSpaceId, CoordinateKind.Ordinal);
    }

    // ------------------------------------------------------ the provenance-sensitive alias overlay

    [Test]
    public void TheSameFlatOrdinalResolvesDifferentlyDependingOnProvenance()
    {
        var fromDisk = Match("Cowboy.Bebop.-.05.1080p.BluRay-GRP", MatchSource.FileName);
        var fromIndexer = Match("Cowboy.Bebop.-.05.1080p.BluRay-GRP", MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(TitleOf(fromDisk.Units[0]), Is.EqualTo("Ballad of Fallen Angels"));
            Assert.That(
                TitleOf(fromIndexer.Units[0]),
                Is.EqualTo("Gateway Shuffle"),
                "the release community's numbering is offset by one from the catalog's");
            Assert.That(fromDisk.Units[0], Is.Not.EqualTo(fromIndexer.Units[0]));
            Assert.That(fromIndexer.Confidence, Is.EqualTo(MatchConfidence.Medium));
        });

        AssertReadingIsInDeclaredSpace(
            fromIndexer.Coordinates,
            TvIds.SceneAbsoluteSpaceId,
            CoordinateKind.Ordinal);
    }

    [Test]
    public void AnUnverifiedAliasReadingLowersConfidenceAndRaisesAWarning()
    {
        var outcome = Match("Cowboy.Bebop.-.06.1080p.BluRay-GRP", MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(TitleOf(outcome.Units[0]), Is.EqualTo("Ballad of Fallen Angels"));
            Assert.That(
                outcome.Confidence,
                Is.EqualTo(MatchConfidence.Low),
                "the mapping for this unit is extrapolated, not confirmed");
            Assert.That(outcome.Warnings, Has.Some.Contains("extrapolated"));
        });
    }

    [Test]
    public void AnEntryThatDoesNotOptIntoTheAliasSpaceIgnoresItEvenForAReleaseName()
    {
        var outcome = Match("The.Expanse.S01E04.1080p.WEB-DL-NTb", MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(TitleOf(outcome.Units[0]), Is.EqualTo("CQB"));
            Assert.That(outcome.Coordinates.TryGet(TvIds.SceneSpaceId, out _), Is.False);
        });
    }

    // ---------------------------------------------------------------------------- the sequence exception

    [Test]
    public void TheReservedOutOfRunOrdinalIsAddressableAndDeclaredExcludedFromCompleteness()
    {
        var outcome = Match("The.Expanse.S00E01.1080p.WEB-DL-GRP", MatchSource.ReleaseName);
        var axis = _shape.Levels
            .Single(level => level.Id == TvIds.EpisodeLevel)
            .SequenceAxes
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Units, Has.Count.EqualTo(1));
            Assert.That(TitleOf(outcome.Units[0]), Is.EqualTo("Behind the Scenes"));
            Assert.That(
                axis.Exceptions.Single(exception =>
                    exception.Value == TvIds.SpecialsOrdinal).ExcludedFromCompleteness,
                Is.True);
        });
    }

    // ------------------------------------------------------------------------------ the sequence span

    [Test]
    public void AWholeRunReleaseResolvesToEveryUnitInThatRun()
    {
        var outcome = Match("The.Expanse.S01.COMPLETE.1080p.BluRay.x264-GRP", MatchSource.ReleaseName);
        var expected = _catalog.EpisodesInSeason(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.RejectionReason, Is.Null);
            Assert.That(outcome.Units, Has.Count.EqualTo(expected.Count));
            Assert.That(expected, Has.Count.EqualTo(6));
            Assert.That(
                outcome.Units.Select(unit => unit.Id.Value).OrderBy(id => id),
                Is.EqualTo(expected.Select(unit => unit.Id).OrderBy(id => id)));
        });
    }

    // --------------------------------------------------------------------- coordinates round-trip as text

    [Test]
    public void EveryReadingASeededUnitCarriesNamesADeclaredSpaceOfTheMatchingKind()
    {
        var admitted = _shape.Levels
            .Single(level => level.Id == TvIds.EpisodeLevel)
            .CoordinateSpaceIds;

        foreach (var unit in _catalog.Episodes)
        {
            foreach (var reading in TvCatalog.CoordinatesOf(unit).Readings)
            {
                var space = _shape.CoordinateSpaces.Single(candidate => candidate.SpaceId == reading.SpaceId);

                Assert.Multiple(() =>
                {
                    Assert.That(admitted, Does.Contain(reading.SpaceId));
                    Assert.That(reading.Value.Kind, Is.EqualTo(space.Kind));
                    Assert.That(
                        space.MayBeUnverified || reading.Confidence != CoordinateConfidence.Unverified,
                        Is.True,
                        $"'{reading.SpaceId}' produced an unverified reading without declaring it may");
                });
            }
        }
    }

    [Test]
    public void OrdinalCoordinatesRoundTripThroughTheirTextForm()
    {
        foreach (var unit in _catalog.Episodes)
        {
            foreach (var reading in TvCatalog.CoordinatesOf(unit).Readings
                .Where(reading => reading.Value.Kind == CoordinateKind.Ordinal))
            {
                var text = reading.Value.Ordinals.ToString();

                Assert.Multiple(() =>
                {
                    Assert.That(OrdinalPath.TryParse(text, out var parsed), Is.True);
                    Assert.That(parsed, Is.EqualTo(reading.Value.Ordinals));
                });
            }
        }
    }

    [Test]
    public void OrdinalCoordinatesOrderTheUnitsOfARunCorrectly()
    {
        var run = _catalog.EpisodesInSeason(1, 1)
            .Select(unit => OrdinalPath.Of(unit.SeasonNumber, unit.EpisodeNumber))
            .ToList();

        for (var index = 1; index < run.Count; index++)
        {
            Assert.That(run[index - 1] < run[index], Is.True, "ordinals must be strictly increasing");
        }
    }

    // ------------------------------------------------------------------- the parser's stable-contract form

    [Test]
    public void TheStableParserContractCarriesEachSchemeInItsBag()
    {
        var ordinal = _parser.Parse("The.Expanse.S01E04.1080p.WEB-DL-NTb");
        var calendar = _parser.Parse("The.Nightly.Review.2024.01.16.1080p.WEB-DL-GRP");
        var flat = _parser.Parse("Cowboy.Bebop.-.05.1080p.BluRay-GRP");

        Assert.Multiple(() =>
        {
            Assert.That(
                ordinal!.AdditionalMetadata![TvReleaseFields.AddressingScheme],
                Is.EqualTo(TvAddressingSchemes.Ordinal));
            Assert.That(ordinal.AdditionalMetadata[TvReleaseFields.Season], Is.EqualTo("1"));
            Assert.That(ordinal.AdditionalMetadata[TvReleaseFields.Episodes], Is.EqualTo("4"));

            Assert.That(
                calendar!.AdditionalMetadata![TvReleaseFields.AddressingScheme],
                Is.EqualTo(TvAddressingSchemes.Calendar));
            Assert.That(calendar.AdditionalMetadata[TvReleaseFields.AirDate], Is.EqualTo("2024-01-16"));

            Assert.That(
                flat!.AdditionalMetadata![TvReleaseFields.AddressingScheme],
                Is.EqualTo(TvAddressingSchemes.Flat));
            Assert.That(flat.AdditionalMetadata[TvReleaseFields.AbsoluteNumbers], Is.EqualTo("5"));
        });
    }

    // -------------------------------------------------------------------- each scheme renders its own name

    [Test]
    public void EachSchemeSelectsAndFillsItsOwnNameTemplate()
    {
        var ordinalUnit = _catalog.Episodes.Single(unit => unit.Title == "CQB");
        var calendarUnit = _catalog.Episodes.Single(unit => unit.Title == "Tuesday");
        var flatUnit = _catalog.Episodes.Single(unit => unit.Title == "Ballad of Fallen Angels");

        Assert.Multiple(() =>
        {
            Assert.That(Render(ordinalUnit), Is.EqualTo("The Expanse - S01E04 - CQB"));
            Assert.That(Render(calendarUnit), Is.EqualTo("The Nightly Review - 2024-01-16 - Tuesday"));
            Assert.That(Render(flatUnit), Is.EqualTo("Cowboy Bebop - 005 - Ballad of Fallen Angels"));
        });
    }

    [Test]
    public void TheFlatTemplateDegradesToTheCanonicalOneWhenTheCoordinateIsAbsent()
    {
        var series = _catalog.Series.Single(entry => entry.Title == "Cowboy Bebop");
        var withoutFlat = _catalog.Episodes.First(unit => unit.SeriesId == series.Id) with
        {
            AbsoluteNumber = null
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                TvRenamePolicy.TemplateFor(series, withoutFlat),
                Is.EqualTo(TvRenamePolicy.OrdinalTemplate));
            Assert.That(
                TvRenamePolicy.TemplateFor(
                    series,
                    _catalog.Episodes.First(unit => unit.SeriesId == series.Id)),
                Is.EqualTo(TvRenamePolicy.FlatTemplate));
        });
    }

    // ------------------------------------------------------------------------------------ rejection paths

    [TestCase("Some.Unknown.Show.S01E01.1080p-GRP", TestName = "an unknown library entry is rejected")]
    [TestCase("The.Expanse.S09E01.1080p-GRP", TestName = "an out-of-range coordinate is rejected")]
    [TestCase("The.Nightly.Review.2030.01.01.1080p-GRP", TestName = "an unknown date is rejected")]
    public void UnresolvableReleasesAreRejectedWithAReason(string releaseTitle)
    {
        var outcome = Match(releaseTitle, MatchSource.ReleaseName);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Units, Is.Empty);
            Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.None));
            Assert.That(outcome.RejectionReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void AnExternalIdentifierRaisesConfidenceToExact()
    {
        var outcome = _matcher.Match(new MatchRequest
        {
            MediaKind = TvIds.MediaKind,
            Text = "The.Expanse.S01E04.1080p.WEB-DL-NTb",
            Source = MatchSource.ReleaseName,
            ExternalIds = [ExternalId.Of(TvIds.TvdbScheme, "280619")]
        });

        Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.Exact));
    }

    private MatchOutcome Match(string releaseTitle, MatchSource source)
        => _matcher.Match(new MatchRequest
        {
            MediaKind = TvIds.MediaKind,
            Text = releaseTitle,
            Source = source
        });

    private string TitleOf(MediaItemRef reference)
        => _catalog.TryGetEpisode(reference.Id.Value, out var unit) && unit is not null
            ? unit.Title
            : throw new InvalidOperationException(
                $"'{reference.ToString()}' is not a unit in the seeded catalog.");

    private string Render(TvEpisodeRecord unit)
    {
        var series = _catalog.Series.Single(entry => entry.Id == unit.SeriesId);

        return _naming
            .GenerateFileNameAsync(
                MediaItemId.FromInt64(unit.Id),
                file: null,
                TvRenamePolicy.TemplateFor(series, unit))
            .GetAwaiter()
            .GetResult();
    }

    private void AssertReadingIsInDeclaredSpace(
        CoordinateSet coordinates,
        string spaceId,
        CoordinateKind expectedKind)
    {
        var space = _shape.CoordinateSpaces.SingleOrDefault(candidate => candidate.SpaceId == spaceId);

        Assert.Multiple(() =>
        {
            Assert.That(space, Is.Not.Null, $"'{spaceId}' must be declared on the shape");
            Assert.That(space!.Kind, Is.EqualTo(expectedKind));
            Assert.That(
                coordinates.TryGet(spaceId, out var reading),
                Is.True,
                $"the outcome must carry a reading in '{spaceId}'");
            Assert.That(reading.Value.Kind, Is.EqualTo(expectedKind));
            Assert.That(
                _shape.Levels
                    .Single(level => level.Id == TvIds.EpisodeLevel)
                    .CoordinateSpaceIds,
                Does.Contain(spaceId),
                "the unit level must admit the space the reading names");
        });
    }
}
