// The media-shape contracts are experimental; this extension is a reference implementer.
#pragma warning disable ARX0013
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Books.Seed;

namespace Arronix.Plugin.Books;

/// <summary>
/// Turns "acquire this" into an ordered set of queries to ask indexers.
/// </summary>
/// <remarks>
/// <para>
/// Books have the richest identifier tier of any kind - a work identifier, a thirteen-digit number per
/// manifestation and a retailer identifier per manifestation - so the first tier is unusually productive
/// and the fall-through to text is unusually rare.
/// </para>
/// <para>
/// The aliases are where the domain shows. Subtitles are stripped because indexers almost never carry
/// them; the writer's shelving form is offered alongside their display form because both appear in the
/// wild; and the collection and position are synthesized into a title because a great many releases are
/// named that way and no other kind has that problem.
/// </para>
/// </remarks>
public sealed class BooksQueryPlanner : IReleaseQueryPlanner
{
    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

    /// <inheritdoc />
    public Task<ReleaseQueryPlan> PlanAsync(
        AcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = string.Equals(
            request.SearchKindId,
            BooksShape.CatalogSearchKindId,
            StringComparison.Ordinal)
            ? PlanCatalog(request)
            : PlanWork(request);

        return Task.FromResult(plan);
    }

    /// <summary>
    /// Returns the name a candidate release for one manifestation would plausibly carry.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <param name="manifestation">The manifestation.</param>
    /// <returns>The candidate name.</returns>
    public static string CandidateTitle(SeedWork work, SeedManifestation manifestation)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(manifestation);

        var writer = BooksSeed.Writers.First(candidate => candidate.Id == work.WriterId);
        var tier = manifestation.Flavor == BooksShape.WrittenFamilyId ? "EPUB" : "M4B";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{writer.Name} - {StripSubtitle(work.Title)} ({manifestation.ReleaseDate.Year}) [{tier}]");
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
            var writer = BooksSeed.Writers.First(candidate => candidate.Id == work.WriterId);
            var stem = StripSubtitle(work.Title);
            var categories = CategoriesFor(work);

            foreach (var manifestation in BooksSeed.ManifestationsOf(work.Id))
            {
                if (manifestation.BookNumber is null && manifestation.RetailerId is null)
                {
                    continue;
                }

                var scheme = manifestation.BookNumber is not null
                    ? BooksShape.BookNumberScheme
                    : BooksShape.RetailerScheme;

                var value = manifestation.BookNumber ?? manifestation.RetailerId!;

                identified.Add(new ReleaseQuery
                {
                    MediaKind = BooksShape.Kind,
                    SearchKindId = BooksShape.WorkSearchKindId,
                    FreeText = string.Create(CultureInfo.InvariantCulture, $"{writer.Name} {stem}"),
                    Arguments =
                    [
                        new SearchArgument(
                            SearchTerm.ExternalIdentifier,
                            FieldValue.OfExternalIdentifier(ExternalId.Of(scheme, value))),
                    ],
                    Categories = CategoriesForFlavor(manifestation.Flavor),
                    Origin = request.Origin,
                });
            }

            exact.Add(new ReleaseQuery
            {
                MediaKind = BooksShape.Kind,
                SearchKindId = BooksShape.WorkSearchKindId,
                FreeText = string.Create(CultureInfo.InvariantCulture, $"{writer.Name} {stem}"),
                Arguments =
                [
                    new SearchArgument(SearchTerm.Creator, FieldValue.OfText(writer.Name)),
                    new SearchArgument(SearchTerm.WorkTitle, FieldValue.OfText(stem)),
                    new SearchArgument(SearchTerm.Year, FieldValue.OfInteger(work.ReleaseDate.Year)),
                ],
                Categories = categories,
                Origin = request.Origin,
            });

            loose.Add(new ReleaseQuery
            {
                MediaKind = BooksShape.Kind,
                SearchKindId = BooksShape.WorkSearchKindId,
                FreeText = stem,
                Aliases = AliasesFor(work, writer),
                Arguments = [new SearchArgument(SearchTerm.WorkTitle, FieldValue.OfText(stem))],
                Categories = categories,
                Origin = request.Origin,
            });
        }

        var tiers = new List<ReleaseQueryTier>();

        if (identified.Count > 0)
        {
            tiers.Add(new ReleaseQueryTier(identified));
        }

        tiers.Add(new ReleaseQueryTier(exact));
        tiers.Add(new ReleaseQueryTier(loose));

        return new ReleaseQueryPlan(tiers);
    }

    private static ReleaseQueryPlan PlanCatalog(AcquisitionRequest request)
    {
        var writers = Resolve(request.Units)
            .Select(work => work.WriterId)
            .Distinct()
            .Select(id => BooksSeed.Writers.FirstOrDefault(writer => writer.Id == id))
            .Where(writer => writer is not null)
            .Select(writer => writer!)
            .ToList();

        if (writers.Count == 0)
        {
            return new ReleaseQueryPlan([]);
        }

        var queries = writers
            .Select(writer => new ReleaseQuery
            {
                MediaKind = BooksShape.Kind,
                SearchKindId = BooksShape.CatalogSearchKindId,
                FreeText = writer.Name,
                Aliases = [writer.SortName],
                Arguments = [new SearchArgument(SearchTerm.Creator, FieldValue.OfText(writer.Name))],
                Categories = BooksShape.AllCategories,
                Limit = 200,
                Origin = request.Origin,
            })
            .ToList();

        return new ReleaseQueryPlan([new ReleaseQueryTier(queries)]);
    }

    private static IReadOnlyList<CategoryId> CategoriesFor(SeedWork work)
    {
        var flavors = BooksSeed.ManifestationsOf(work.Id)
            .Select(manifestation => manifestation.Flavor)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (flavors.Count == 1)
        {
            return CategoriesForFlavor(flavors[0]);
        }

        return BooksShape.AllCategories;
    }

    private static IReadOnlyList<CategoryId> CategoriesForFlavor(string flavor) =>
        string.Equals(flavor, BooksShape.SpokenFamilyId, StringComparison.Ordinal)
            ? BooksShape.SpokenCategories
            : BooksShape.WrittenCategories;

    private static List<string> AliasesFor(SeedWork work, SeedWriter writer)
    {
        var aliases = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{writer.Name} {StripSubtitle(work.Title)}"),
            string.Create(CultureInfo.InvariantCulture, $"{writer.SortName} {StripSubtitle(work.Title)}"),
        };

        foreach (var membership in BooksSeed.MembershipsOf(work.Id))
        {
            var collection = BooksSeed.Collections.FirstOrDefault(
                candidate => candidate.Id == membership.CollectionId);

            if (collection is null || membership.Position.Length == 0)
            {
                continue;
            }

            aliases.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{collection.Title} {membership.Position} {StripSubtitle(work.Title)}"));

            aliases.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{StripSubtitle(work.Title)} {collection.Title} Book {membership.Position}"));
        }

        return aliases;
    }

    private static IEnumerable<SeedWork> Resolve(IReadOnlyList<MediaItemRef> units)
    {
        foreach (var unit in units)
        {
            if (unit.Level == BooksShape.WorkLevel)
            {
                var work = BooksSeed.Works.FirstOrDefault(candidate => candidate.Id == unit.Id.Value);

                if (work is not null)
                {
                    yield return work;
                }

                continue;
            }

            if (unit.Level == BooksShape.ManifestationLevel)
            {
                var manifestation = BooksSeed.Manifestations.FirstOrDefault(
                    candidate => candidate.Id == unit.Id.Value);

                var work = manifestation is null
                    ? null
                    : BooksSeed.Works.FirstOrDefault(candidate => candidate.Id == manifestation.WorkId);

                if (work is not null)
                {
                    yield return work;
                }

                continue;
            }

            if (unit.Level == BooksShape.WriterLevel)
            {
                foreach (var work in BooksSeed.Works.Where(
                    candidate => candidate.WriterId == unit.Id.Value))
                {
                    yield return work;
                }
            }
        }
    }

    private static string StripSubtitle(string title)
    {
        var colon = title.IndexOf(':', StringComparison.Ordinal);

        return colon < 0 ? title : title[..colon].Trim();
    }
}
