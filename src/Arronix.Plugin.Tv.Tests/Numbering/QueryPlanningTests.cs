
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Providers;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv.Tests.Numbering;

/// <summary>
/// Proves that each declared search kind plans a query, and that eligibility is pure set intersection.
/// </summary>
/// <remarks>
/// The reference implementation needed seven <c>Fetch</c> overloads for this media kind, one for each
/// combination of addressing scheme and breadth. Here the arity lives in data: four declared search kinds,
/// one planner, and an eligibility rule neither the media kind nor the indexer can express in the other's
/// vocabulary. Nothing below mentions a wire parameter name outside the indexer's own bindings.
/// </remarks>
[TestFixture]
public sealed class QueryPlanningTests
{
    private TvCatalog _catalog = null!;
    private TvQueryPlanner _planner = null!;
    private MediaShape _shape = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = TvCatalog.CreateSeeded();
        _planner = new TvQueryPlanner(_catalog);
        _shape = new TvShape().Shape;
    }

    [Test]
    public void AUnitSearchPlansAnIdentifierTierAndThenATextTier()
    {
        var unit = Unit(1, 1, 4);
        var plan = Plan(TvIds.UnitSearchKindId, unit);

        Assert.That(plan.Tiers, Has.Count.EqualTo(2), "identifier first, then text");

        var identifier = plan.Tiers[0].Queries.Single();
        var text = plan.Tiers[1].Queries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(
                identifier.Arguments.Any(argument => argument.Term == SearchTerm.ExternalIdentifier),
                Is.True);
            Assert.That(
                identifier.Arguments
                    .Where(argument => argument.Term == SearchTerm.Ordinal)
                    .Select(argument => argument.ComponentId),
                Is.EquivalentTo(new[] { TvIds.SeasonComponentId, TvIds.EpisodeComponentId }));
            Assert.That(identifier.FreeText, Does.Contain("S01E04"));

            Assert.That(
                text.Arguments.Any(argument => argument.Term == SearchTerm.ExternalIdentifier),
                Is.False,
                "the fallback tier must be answerable by an indexer that takes no identifier");
            Assert.That(text.FreeText, Does.Contain("S01E04"));
        });
    }

    [Test]
    public void EveryOrdinalArgumentNamesADeclaredComponentOfADeclaredSpace()
    {
        var plan = Plan(TvIds.UnitSearchKindId, Unit(1, 1, 4));
        var space = _shape.CoordinateSpaces.Single(candidate => candidate.SpaceId == TvIds.AiredSpaceId);

        foreach (var argument in plan.Tiers
            .SelectMany(tier => tier.Queries)
            .SelectMany(query => query.Arguments)
            .Where(argument => argument.Term == SearchTerm.Ordinal))
        {
            Assert.That(
                space.Components.Select(component => component.ComponentId),
                Does.Contain(argument.ComponentId));
        }
    }

    [Test]
    public void AnEntryUsingTheAliasSpaceOffersItsAliasCoordinateAsAnAlias()
    {
        // Cowboy Bebop's release-community numbering is offset by one, so the string the community posts
        // under is not the string the catalog would produce. Supplying both is the extension's business
        // and nobody else's.
        var unit = _catalog.Episodes.Single(episode => episode.Title == "Gateway Shuffle");
        var plan = Plan(TvIds.UnitSearchKindId, unit);
        var aliases = plan.Tiers[1].Queries.Single().Aliases;

        Assert.Multiple(() =>
        {
            Assert.That(aliases, Has.Some.Contains("S01E05"), "the alias coordinate");
            Assert.That(plan.Tiers[1].Queries.Single().FreeText, Does.Contain("S01E04"), "the canonical one");
        });
    }

    [Test]
    public void AWholeRunSearchPlansOneQueryPerRunCarryingOnlyTheOuterOrdinal()
    {
        var plan = Plan(TvIds.SeasonPackSearchKindId, Unit(1, 1, 1), Unit(1, 1, 2), Unit(1, 1, 3));
        var query = plan.Tiers.Single().Queries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(query.FreeText, Does.Contain("S01"));
            Assert.That(query.FreeText, Does.Not.Contain("E01"));
            Assert.That(
                query.Arguments.Single().ComponentId,
                Is.EqualTo(TvIds.SeasonComponentId),
                "a span query addresses the axis, not a unit");
            Assert.That(query.Aliases, Has.Some.Contains("Complete"));
        });
    }

    [Test]
    public void ACalendarSearchRendersTheDateIntoTheTextAndOffersBothSpellings()
    {
        var unit = _catalog.Episodes.Single(episode => episode.Title == "Tuesday");
        var plan = Plan(TvIds.DailySearchKindId, unit);
        var query = plan.Tiers.Single().Queries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(query.FreeText, Does.Contain("2024-01-16"));
            Assert.That(query.Arguments.Single().Term, Is.EqualTo(SearchTerm.Date));
            Assert.That(query.Aliases, Has.Some.Contains("2024 01 16"));
            Assert.That(query.Aliases, Has.Some.Contains("2024.01.16"));
        });
    }

    [Test]
    public void AWholeEntrySearchCarriesOnlyTheWorkTitle()
    {
        var plan = Plan(TvIds.SeriesSearchKindId, Unit(1, 1, 1));
        var query = plan.Tiers.Single().Queries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(query.Arguments.Single().Term, Is.EqualTo(SearchTerm.WorkTitle));
            Assert.That(query.FreeText, Is.EqualTo("The Expanse"));
        });
    }

    [Test]
    public void AnUnknownSearchKindPlansNothing()
        => Assert.That(Plan("no-such-search", Unit(1, 1, 1)).Tiers, Is.Empty);

    [Test]
    public void EligibilityIsSetIntersectionAndNeitherSideNamesTheOthersNouns()
    {
        var profiles = Describe().SearchProfiles;

        foreach (var searchKind in _shape.SearchKinds)
        {
            var eligible = profiles.Where(profile =>
                searchKind.RequiredTerms.All(term => profile.Terms.Contains(term))
                && searchKind.Categories.Intersect(profile.Categories).Any())
                .ToList();

            Assert.That(
                eligible,
                Is.Not.Empty,
                $"'{searchKind.SearchKindId}' has no eligible profile on the reference indexer");
        }
    }

    [Test]
    public void ASearchKindWithNoRequiredTermIsServedByACategoryOnlyProfile()
    {
        var calendar = _shape.SearchKinds.Single(kind => kind.SearchKindId == TvIds.DailySearchKindId);
        var textOnly = Describe().SearchProfiles.Single(profile => profile.ProfileId == "text");

        Assert.Multiple(() =>
        {
            Assert.That(calendar.RequiredTerms, Is.Empty);
            Assert.That(
                calendar.RequiredTerms.All(term => textOnly.Terms.Contains(term)),
                Is.True,
                "the empty set is a subset of everything, which is what makes a category-only gate legal");
            Assert.That(calendar.Categories.Intersect(textOnly.Categories).Any(), Is.True);
        });
    }

    [Test]
    public void TheIndexerRefusesATermItNeverDeclared()
    {
        var indexer = new TvIndexer(
            PluginId.FromString(TvIds.PluginIdValue),
            _catalog,
            TimeProvider.System);

        var result = indexer
            .SearchAsync(
                Invocation(),
                new ReleaseQuery
                {
                    MediaKind = TvIds.MediaKind,
                    SearchKindId = TvIds.UnitSearchKindId,
                    FreeText = "The Expanse",
                    Origin = SearchOrigin.Automatic,
                    Arguments = [new SearchArgument(SearchTerm.Issuer, FieldValue.OfText("Syfy"))]
                })
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.Releases, Is.Empty);
            Assert.That(result.Warnings, Is.Not.Empty);
        });
    }

    [Test]
    public void TheIndexerAnswersAPlannedQueryAndIncludesAMultiUnitRelease()
    {
        var indexer = new TvIndexer(
            PluginId.FromString(TvIds.PluginIdValue),
            _catalog,
            TimeProvider.System);

        var plan = Plan(TvIds.UnitSearchKindId, Unit(1, 1, 1));

        var result = indexer
            .SearchAsync(Invocation(), plan.Tiers[0].Queries.Single())
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.Releases, Is.Not.Empty);
            Assert.That(
                result.Releases.Any(release => release.Title.Contains("S01E01E02", StringComparison.Ordinal)),
                Is.True,
                "the synthetic feed must reach the multi-unit branch of the matcher");
        });
    }

    private IndexerProfile Describe()
        => new TvIndexer(PluginId.FromString(TvIds.PluginIdValue), _catalog, TimeProvider.System)
            .DescribeAsync(Invocation())
            .GetAwaiter()
            .GetResult();

    private static ProviderInvocation Invocation() => new(
        new ProviderDefinition
        {
            Id = 1,
            Provider = ProviderId.Create(PluginId.FromString(TvIds.PluginIdValue), TvIndexer.LocalId),
            Family = ProviderFamily.Indexer,
            Name = "Reference feed",
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TvIndexer.BaseUrlSetting] = "https://catalog.invalid",
                [TvIndexer.ApiKeySetting] = "secret"
            }
        },
        new InMemorySessionStore(),
        "correlation");

    private ReleaseQueryPlan Plan(string searchKindId, params TvEpisodeRecord[] units)
        => _planner
            .PlanAsync(new AcquisitionRequest
            {
                MediaKind = TvIds.MediaKind,
                SearchKindId = searchKindId,
                Units = [.. units.Select(TvCatalog.ReferenceTo)],
                Origin = SearchOrigin.Automatic
            })
            .GetAwaiter()
            .GetResult();

    private TvEpisodeRecord Unit(int seriesId, int season, int episode)
        => _catalog.TryGetByAired(seriesId, season, episode, out var unit) && unit is not null
            ? unit
            : throw new InvalidOperationException("The seeded catalog has no such unit.");

    private sealed class InMemorySessionStore : IProviderSessionStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(
            string key,
            string? value,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
