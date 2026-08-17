// The media-shape contracts are experimental; this extension is a reference implementer.
#pragma warning disable ARX0013
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Music.Seed;

namespace Arronix.Plugin.Music;

/// <summary>
/// Turns "acquire this" into an ordered set of queries to ask indexers.
/// </summary>
/// <remarks>
/// <para>
/// Tiers, not a list: identifier-bearing queries first, then exact title, then a loosened title with the
/// aliases this kind knows about. The first tier that yields anything wins, so a precise query is never
/// diluted by a broad one running alongside it.
/// </para>
/// <para>
/// Aliases are the extension's business and nobody else's. Compilations are listed under a collective
/// credit rather than by contributor, punctuation is dropped inconsistently, and a remastered pressing is
/// as often named after its year as after its title. The platform cannot know any of that and does not
/// have to.
/// </para>
/// </remarks>
public sealed class MusicQueryPlanner : IReleaseQueryPlanner
{
    /// <summary>The collective credit compilations are listed under.</summary>
    public const string CollectiveCredit = "Various Artists";

    /// <summary>The abbreviation indexers overwhelmingly use for that credit.</summary>
    public const string CollectiveCreditAbbreviation = "VA";

    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

    /// <inheritdoc />
    public Task<ReleaseQueryPlan> PlanAsync(
        AcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = string.Equals(
            request.SearchKindId,
            MusicShape.CatalogSearchKindId,
            StringComparison.Ordinal)
            ? PlanCatalog(request)
            : PlanWork(request);

        return Task.FromResult(plan);
    }

    /// <summary>
    /// Returns the name a candidate release for one pressing would plausibly carry.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <param name="pressing">The pressing.</param>
    /// <returns>The candidate name.</returns>
    public static string CandidateTitle(SeedWork work, SeedPressing pressing)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(pressing);

        var performer = MusicSeed.Performers.First(candidate => candidate.Id == work.PerformerId);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{performer.Name} - {pressing.Title} ({pressing.ReleaseDate.Year}) [FLAC]");
    }

    private static ReleaseQueryPlan PlanWork(AcquisitionRequest request)
    {
        var works = Resolve(request.Units).ToList();

        if (works.Count == 0)
        {
            return new ReleaseQueryPlan([]);
        }

        var identified = new List<ReleaseQuery>();
        var exact = new List<ReleaseQuery>();
        var loose = new List<ReleaseQuery>();

        foreach (var work in works)
        {
            var performer = MusicSeed.Performers.First(candidate => candidate.Id == work.PerformerId);
            var credit = CreditFor(performer.Name);

            identified.Add(new ReleaseQuery
            {
                MediaKind = MusicShape.Kind,
                SearchKindId = MusicShape.WorkSearchKindId,
                FreeText = string.Create(CultureInfo.InvariantCulture, $"{credit} {work.Title}"),
                Arguments =
                [
                    new SearchArgument(
                        SearchTerm.ExternalIdentifier,
                        FieldValue.OfExternalIdentifier(
                            ExternalId.Of(MusicShape.CatalogScheme, work.CatalogId))),
                ],
                Categories = MusicShape.AudioCategories,
                Origin = request.Origin,
            });

            exact.Add(new ReleaseQuery
            {
                MediaKind = MusicShape.Kind,
                SearchKindId = MusicShape.WorkSearchKindId,
                FreeText = string.Create(CultureInfo.InvariantCulture, $"{credit} {work.Title}"),
                Arguments =
                [
                    new SearchArgument(SearchTerm.Creator, FieldValue.OfText(credit)),
                    new SearchArgument(SearchTerm.WorkTitle, FieldValue.OfText(work.Title)),
                    new SearchArgument(
                        SearchTerm.Year,
                        FieldValue.OfInteger(work.ReleaseDate.Year)),
                ],
                Categories = MusicShape.AudioCategories,
                Origin = request.Origin,
            });

            loose.Add(new ReleaseQuery
            {
                MediaKind = MusicShape.Kind,
                SearchKindId = MusicShape.WorkSearchKindId,
                FreeText = work.Title,
                Aliases = AliasesFor(performer.Name, work.Title),
                Arguments = [new SearchArgument(SearchTerm.WorkTitle, FieldValue.OfText(work.Title))],
                Categories = MusicShape.AudioCategories,
                Origin = request.Origin,
            });
        }

        return new ReleaseQueryPlan(
        [
            new ReleaseQueryTier(identified),
            new ReleaseQueryTier(exact),
            new ReleaseQueryTier(loose),
        ]);
    }

    private static ReleaseQueryPlan PlanCatalog(AcquisitionRequest request)
    {
        var performers = Resolve(request.Units)
            .Select(work => work.PerformerId)
            .Distinct()
            .Select(id => MusicSeed.Performers.FirstOrDefault(performer => performer.Id == id))
            .Where(performer => performer is not null)
            .Select(performer => performer!)
            .ToList();

        if (performers.Count == 0)
        {
            return new ReleaseQueryPlan([]);
        }

        var queries = performers
            .Select(performer => new ReleaseQuery
            {
                MediaKind = MusicShape.Kind,
                SearchKindId = MusicShape.CatalogSearchKindId,
                FreeText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{CreditFor(performer.Name)} discography"),
                Aliases =
                [
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{CreditFor(performer.Name)} complete collection"),
                ],
                Arguments =
                [
                    new SearchArgument(
                        SearchTerm.Creator,
                        FieldValue.OfText(CreditFor(performer.Name))),
                ],
                Categories = MusicShape.AudioCategories,
                Limit = 200,
                Origin = request.Origin,
            })
            .ToList();

        return new ReleaseQueryPlan([new ReleaseQueryTier(queries)]);
    }

    private static IEnumerable<SeedWork> Resolve(IReadOnlyList<MediaItemRef> units)
    {
        foreach (var unit in units)
        {
            if (unit.Level == MusicShape.WorkLevel)
            {
                var work = MusicSeed.Works.FirstOrDefault(candidate => candidate.Id == unit.Id.Value);
                if (work is not null)
                {
                    yield return work;
                }

                continue;
            }

            if (unit.Level == MusicShape.PerformerLevel)
            {
                foreach (var work in MusicSeed.Works.Where(
                    candidate => candidate.PerformerId == unit.Id.Value))
                {
                    yield return work;
                }
            }
        }
    }

    private static string CreditFor(string performerName) =>
        string.Equals(performerName, CollectiveCredit, StringComparison.OrdinalIgnoreCase)
            ? CollectiveCreditAbbreviation
            : performerName;

    private static List<string> AliasesFor(string performerName, string title)
    {
        var aliases = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{CreditFor(performerName)} {title}"),
        };

        var stripped = new string(title.Where(character => character != '\'' && character != '.').ToArray());

        if (!string.Equals(stripped, title, StringComparison.Ordinal))
        {
            aliases.Add(stripped);
        }

        return aliases;
    }
}
