#pragma warning disable ARX0013 // Shape contracts are experimental; these tests cover an implementation of them.

using System;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Tv.Tests.Shape;

/// <summary>
/// Asserts that the declared shape says what the acceptance table says it says.
/// </summary>
/// <remarks>
/// These are not tautologies. Every assertion below corresponds to a cell of the cross-media acceptance
/// table, and the whole point of the television extension is that it is the kind whose cells are hardest to
/// fill. A change that quietly drops the provenance flags, the sequence exception or the span constraint
/// would still compile and would still parse titles correctly — and would silently lose a behavior the
/// reference implementation needed a dedicated exception type and a hand-written mapping service for.
/// </remarks>
[TestFixture]
public sealed class TvShapeDeclarationTests
{
    private MediaShape _shape = null!;
    private MediaLevel _series = null!;
    private MediaLevel _episode = null!;

    [SetUp]
    public void SetUp()
    {
        _shape = new TvShape().Shape;
        _series = _shape.Levels.Single(level => level.Id == TvIds.SeriesLevel);
        _episode = _shape.Levels.Single(level => level.Id == TvIds.EpisodeLevel);
    }

    [Test]
    public void TheShapeHasTwoLevelsFormingALinearChain()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_shape.Levels, Has.Count.EqualTo(2));
            Assert.That(_series.Parent, Is.Null, "the library entry is the root");
            Assert.That(_episode.Parent, Is.EqualTo(TvIds.SeriesLevel));
            Assert.That(_shape.Kind.Value, Is.EqualTo(TvIds.MediaKindValue));
        });
    }

    [Test]
    public void RolesAreDistributedAcrossTheTwoLevels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_series.Roles, Is.EqualTo(MediaLevelRoles.LibraryEntry));
            Assert.That(
                _episode.Roles,
                Is.EqualTo(MediaLevelRoles.AcquisitionUnit
                    | MediaLevelRoles.CompletenessUnit
                    | MediaLevelRoles.FileBearing));
            Assert.That(_series.Variant, Is.Null, "this media kind has no variant axis");
            Assert.That(_episode.Variant, Is.Null);
        });
    }

    [Test]
    public void BothLevelsCarryACatalogRecordAndALibraryRecord()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_series.Identity.HasCatalogRecord, Is.True);
            Assert.That(_series.Identity.HasLibraryRecord, Is.True);
            Assert.That(
                _series.Identity.SupportsIdentifierRedirects,
                Is.True,
                "catalog identifiers get merged upstream and must not orphan a library entry");
            Assert.That(
                _series.Identity.ExternalIds.Single(scheme => scheme.IsPrimary).Scheme,
                Is.EqualTo(TvIds.TvdbScheme));
        });
    }

    [Test]
    public void TheUnitLevelAdmitsFiveCoordinateSpacesOfWhichExactlyOneIsCanonical()
    {
        var admitted = _shape.CoordinateSpaces
            .Where(space => _episode.CoordinateSpaceIds.Contains(space.SpaceId))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(admitted, Has.Count.EqualTo(5));
            Assert.That(admitted.Count(space => space.IsCanonical), Is.EqualTo(1));
            Assert.That(
                admitted.Single(space => space.IsCanonical).SpaceId,
                Is.EqualTo(TvIds.AiredSpaceId));
            Assert.That(
                _series.CoordinateSpaceIds,
                Is.Empty,
                "the library entry has no position; only units do");
        });
    }

    [Test]
    public void TheThreeAddressingSchemesEachHaveADeclaredSpaceOfTheRightKind()
    {
        var aired = Space(TvIds.AiredSpaceId);
        var flat = Space(TvIds.AbsoluteSpaceId);
        var calendar = Space(TvIds.AirDateSpaceId);

        Assert.Multiple(() =>
        {
            Assert.That(aired.Kind, Is.EqualTo(CoordinateKind.Ordinal));
            Assert.That(
                aired.Components.Select(component => component.ComponentId),
                Is.EqualTo(new[] { TvIds.SeasonComponentId, TvIds.EpisodeComponentId }));
            Assert.That(aired.IsDense, Is.True, "a gap in the canonical space means a missing unit");

            Assert.That(flat.Kind, Is.EqualTo(CoordinateKind.Ordinal));
            Assert.That(flat.Components, Has.Count.EqualTo(1));
            Assert.That(flat.IsDense, Is.False, "out-of-run units never receive a flat ordinal");

            Assert.That(calendar.Kind, Is.EqualTo(CoordinateKind.Date));
            Assert.That(calendar.Components, Is.Empty, "a date space has no ordinal components");
            Assert.That(calendar.IsDense, Is.False, "a weekday schedule has a hole every weekend");
        });
    }

    [Test]
    public void TheAliasSpacesAreProvenanceSensitiveAndMayBeUnverified()
    {
        foreach (var spaceId in new[] { TvIds.SceneSpaceId, TvIds.SceneAbsoluteSpaceId })
        {
            var space = Space(spaceId);

            Assert.Multiple(() =>
            {
                Assert.That(space.IsCanonical, Is.False, $"'{spaceId}' is an alias, never the yardstick");
                Assert.That(space.IsProvenanceSensitive, Is.True);
                Assert.That(space.MayBeUnverified, Is.True);
            });
        }
    }

    [Test]
    public void TheSequenceAxisIsNotALevelAndCarriesAPolicyRecordAndAnException()
    {
        var axis = _episode.SequenceAxes.Single();
        var exception = axis.Exceptions.Single();

        Assert.Multiple(() =>
        {
            Assert.That(
                _shape.Levels.Any(level => level.Id.Value == axis.AxisId),
                Is.False,
                "the sequence axis must not also be a level");
            Assert.That(axis.SpaceId, Is.EqualTo(TvIds.AiredSpaceId));
            Assert.That(axis.ComponentIndex, Is.EqualTo(0), "it names the outer component");
            Assert.That(
                axis.HasPolicyRecord,
                Is.True,
                "a monitored bit and artwork exist per entry and ordinal");
            Assert.That(exception.Value, Is.EqualTo(TvIds.SpecialsOrdinal));
            Assert.That(
                exception.ExcludedFromCompleteness,
                Is.True,
                "an absent out-of-run unit is not a gap");
        });
    }

    [Test]
    public void SequenceAxisComponentIndexResolvesWithinItsDeclaredSpace()
    {
        var axis = _episode.SequenceAxes.Single();
        var space = Space(axis.SpaceId);

        Assert.Multiple(() =>
        {
            Assert.That(space.Kind, Is.EqualTo(CoordinateKind.Ordinal));
            Assert.That(axis.ComponentIndex, Is.InRange(0, space.Components.Count - 1));
            Assert.That(
                space.Components[axis.ComponentIndex].ComponentId,
                Is.EqualTo(TvIds.SeasonComponentId));
        });
    }

    [Test]
    public void TheFileBindingIsOneFileToManyUnitsWithASpanConstraint()
    {
        var binding = _shape.FileBinding;
        var constraint = binding.SpanConstraints
            .Single(span => span.ComponentId == TvIds.SeasonComponentId);

        Assert.Multiple(() =>
        {
            Assert.That(binding.AnchorLevelId, Is.EqualTo(TvIds.EpisodeLevel));
            Assert.That(binding.UnitLevelId, Is.EqualTo(TvIds.EpisodeLevel));
            Assert.That(binding.AtMostOneFilePerUnit, Is.True, "a unit has at most one file");
            Assert.That(
                binding.AtMostOneUnitPerFile,
                Is.False,
                "a file may satisfy several units - the whole point of this media kind");
            Assert.That(
                binding.OrdinalIsMeaningful,
                Is.False,
                "an ordinal only means something when a unit spans files, which is the other arrangement");

            Assert.That(constraint.SpaceId, Is.EqualTo(TvIds.AiredSpaceId));
            Assert.That(constraint.Rule, Is.EqualTo(SpanRule.MustNotSpan));
            Assert.That(
                binding.SpanConstraints.Single(span => span.ComponentId == TvIds.EpisodeComponentId).Rule,
                Is.EqualTo(SpanRule.MaySpan));
        });
    }

    [Test]
    public void EverySpanConstraintResolvesToADeclaredSpaceAndComponent()
    {
        foreach (var constraint in _shape.FileBinding.SpanConstraints)
        {
            var space = Space(constraint.SpaceId);

            Assert.That(
                space.Components.Any(component => component.ComponentId == constraint.ComponentId),
                Is.True,
                $"'{constraint.SpaceId}.{constraint.ComponentId}' must resolve");
        }
    }

    [Test]
    public void ThreeAcquisitionScopesAreDeclaredIncludingASequenceSpanAndAnAncestor()
    {
        var unit = RequireSearchKind(TvIds.UnitSearchKindId);
        var pack = RequireSearchKind(TvIds.SeasonPackSearchKindId);
        var whole = RequireSearchKind(TvIds.SeriesSearchKindId);

        Assert.Multiple(() =>
        {
            Assert.That(unit.Scope.Kind, Is.EqualTo(AcquisitionScopeKind.Single));

            Assert.That(pack.Scope.Kind, Is.EqualTo(AcquisitionScopeKind.SequenceSpan));
            Assert.That(
                pack.Scope.SequenceAxisId,
                Is.EqualTo(TvIds.SeasonAxisId),
                "a span names an axis, which no level identifier could");

            Assert.That(whole.Scope.Kind, Is.EqualTo(AcquisitionScopeKind.Ancestor));
            Assert.That(whole.Scope.AncestorLevelId, Is.EqualTo(TvIds.SeriesLevel));
        });
    }

    [Test]
    public void EverySearchKindTargetsAnExistingLevelAndReferencesADeclaredAxisOrAncestor()
    {
        foreach (var searchKind in _shape.SearchKinds)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    _shape.Levels.Any(level => level.Id == searchKind.TargetLevelId),
                    Is.True,
                    $"'{searchKind.SearchKindId}' targets an unknown level");

                Assert.That(
                    searchKind.Categories.Count,
                    Is.GreaterThan(0),
                    "the category gate must be usable");

                if (searchKind.Scope.Kind == AcquisitionScopeKind.SequenceSpan)
                {
                    Assert.That(
                        _episode.SequenceAxes.Any(axis => axis.AxisId == searchKind.Scope.SequenceAxisId),
                        Is.True);
                }

                if (searchKind.Scope.Kind == AcquisitionScopeKind.Ancestor)
                {
                    Assert.That(
                        _shape.Levels.Any(level => level.Id == searchKind.Scope.AncestorLevelId),
                        Is.True);
                }
            });
        }
    }

    [Test]
    public void SearchKindsDeclareNoIndexerConcept()
    {
        var terms = _shape.SearchKinds
            .SelectMany(searchKind => searchKind.RequiredTerms.Concat(searchKind.OptionalTerms))
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(terms, Does.Contain(SearchTerm.Ordinal));
            Assert.That(terms, Does.Contain(SearchTerm.Date));
            Assert.That(terms, Does.Contain(SearchTerm.WorkTitle));
            Assert.That(
                RequireSearchKind(TvIds.DailySearchKindId).RequiredTerms,
                Is.Empty,
                "almost no indexer accepts a date parameter, so the calendar search gates on categories only");
            Assert.That(
                _shape.SearchKinds.SelectMany(kind => kind.Categories).Select(category => category.Value),
                Has.All.InRange(5000, 5999));
        });
    }

    [Test]
    public void ExactlyOneFieldPerLevelCarriesTitleSemantics()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                _series.Fields.Count(field => field.Semantics.HasFlag(FieldSemantics.Title)),
                Is.EqualTo(1));
            Assert.That(
                _episode.Fields.Count(field => field.Semantics.HasFlag(FieldSemantics.Title)),
                Is.EqualTo(1));
            Assert.That(
                _series.Fields.Select(field => field.FieldId).Distinct().Count(),
                Is.EqualTo(_series.Fields.Count));
            Assert.That(
                _episode.Fields.Select(field => field.FieldId).Distinct().Count(),
                Is.EqualTo(_episode.Fields.Count));
        });
    }

    [Test]
    public void NoNumberingFieldExistsOnTheUnitLevel()
    {
        // Six numbering members live on the surveyed row. All six are coordinates here, and a regression
        // that reintroduced one as a field would be exactly the schema-per-scheme mistake the coordinate bag
        // exists to prevent.
        var forbidden = new[] { "season", "episode", "absolute", "scene", "airdate", "number" };

        foreach (var field in _episode.Fields)
        {
            foreach (var fragment in forbidden)
            {
                Assert.That(
                    field.FieldId.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                        && field.FieldId != TvEpisodeFields.AirDate,
                    Is.False,
                    $"'{field.FieldId}' looks like a numbering column");
            }
        }
    }

    [Test]
    public void TheSelectionFacetHidesRatherThanDeletes()
    {
        var facet = _shape.SelectionFacets.Single();

        Assert.Multiple(() =>
        {
            Assert.That(facet.FacetId, Is.EqualTo(TvIds.SeasonKindFacetId));
            Assert.That(facet.AppliesToLevelId, Is.EqualTo(TvIds.EpisodeLevel));
            Assert.That(facet.Kind, Is.EqualTo(SelectionFacetKind.Enumerated));
            Assert.That(
                facet.Application,
                Is.EqualTo(FacetApplication.Visibility),
                "excluding out-of-run units must hide rows, never delete them");
            Assert.That(facet.DefaultAllowed, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void OneFormatFamilyIsDeclaredWithARankDistinctLadder()
    {
        var family = _shape.FormatFamilies.Single();

        Assert.Multiple(() =>
        {
            Assert.That(family.FamilyId, Is.EqualTo(TvIds.VideoFamilyId));
            Assert.That(family.Ladder, Is.Not.Empty);
            Assert.That(
                family.Ladder.Select(tier => tier.Rank).Distinct().Count(),
                Is.EqualTo(family.Ladder.Count));
            Assert.That(
                family.Ladder.Any(tier => tier.Name == family.Unknown!.Name),
                Is.False,
                "the unknown tier must not be a rung on the ladder");
            Assert.That(family.FileExtensions, Does.Contain(".mkv"));
        });
    }

    [Test]
    public void MonitoringIsSplitAcrossTwoOrthogonalDimensions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                _series.MonitorDimensions.Single().DimensionId,
                Is.EqualTo(TvIds.FutureItemsDimensionId));
            Assert.That(
                _series.MonitorDimensions.Single().Kind,
                Is.EqualTo(MonitorDimensionKind.Enumerated));
            Assert.That(
                _episode.MonitorDimensions.Single().DimensionId,
                Is.EqualTo(TvIds.WantedDimensionId));
            Assert.That(
                _episode.MonitorDimensions.Single().Kind,
                Is.EqualTo(MonitorDimensionKind.Toggle));
        });
    }

    [Test]
    public void NoGroupingAxisIsDeclared()
        => Assert.That(
            _shape.GroupingAxes,
            Is.Empty,
            "a cross-cutting collection belongs to other media kinds; declaring none costs one omitted property");

    private CoordinateSpace Space(string spaceId)
        => _shape.CoordinateSpaces.Single(space => space.SpaceId == spaceId);

    private SearchKind RequireSearchKind(string searchKindId)
        => _shape.SearchKinds.Single(kind => kind.SearchKindId == searchKindId);
}
