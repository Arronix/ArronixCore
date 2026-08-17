namespace Arronix.Plugin.Books;

/// <summary>
/// The field identifiers this extension declares, in one place.
/// </summary>
public static class BooksFields
{
    /// <summary>The writer's name. Carries the title semantic at the root level.</summary>
    public const string Name = "name";

    /// <summary>The sorting form of a name or title.</summary>
    public const string SortName = "sort-name";

    /// <summary>The title of a work or of one of its manifestations.</summary>
    public const string Title = "title";

    /// <summary>The part of a title that follows the colon.</summary>
    public const string Subtitle = "subtitle";

    /// <summary>What tells two same-named subjects apart.</summary>
    public const string Disambiguation = "disambiguation";

    /// <summary>Long-form prose about a subject.</summary>
    public const string Overview = "overview";

    /// <summary>Free-text classification of a subject.</summary>
    public const string Genres = "genres";

    /// <summary>The date a work was first published or a manifestation was issued.</summary>
    public const string ReleaseDate = "release-date";

    /// <summary>Rating value multiplied by rating count, the catalog's measure of prominence.</summary>
    public const string Popularity = "popularity";

    /// <summary>The label the collection this work belongs to gives it.</summary>
    public const string CollectionPosition = "series-position";

    /// <summary>The name of the collection this work belongs to.</summary>
    public const string CollectionName = "series";

    /// <summary>How many manifestations of a work exist.</summary>
    public const string ManifestationCount = "edition-count";

    /// <summary>The upstream description of a manifestation's carrier.</summary>
    public const string CarrierDescription = "format";

    /// <summary>
    /// Which format family a manifestation belongs to. Declared as a field as well as carried on the
    /// projection, because it is the axis a reader filters and groups by.
    /// </summary>
    public const string Flavor = "flavor";

    /// <summary>The house that issued a manifestation.</summary>
    public const string Issuer = "publisher";

    /// <summary>How many pages a manifestation runs to.</summary>
    public const string PageCount = "page-count";

    /// <summary>How long a spoken manifestation runs for.</summary>
    public const string RunningTime = "running-time";

    /// <summary>The language a manifestation is in.</summary>
    public const string Language = "language";

    /// <summary>The thirteen-digit book number of a manifestation.</summary>
    public const string BookNumber = "isbn13";

    /// <summary>The retailer's identifier for a manifestation.</summary>
    public const string RetailerId = "asin";

    /// <summary>The external catalog's identifier for a subject.</summary>
    public const string CatalogId = "goodreads-id";

    /// <summary>The subject's condition, as one of the declared states.</summary>
    public const string State = "state";

    /// <summary>An image representing a subject.</summary>
    public const string Artwork = "cover";

    /// <summary>The monitor dimension asking whether an item is wanted.</summary>
    public const string Wanted = "wanted";

    /// <summary>The monitor dimension asking what to do with works that appear later.</summary>
    public const string FutureOutput = "future-items";
}

/// <summary>
/// The selection facets this extension declares - the third profile axis.
/// </summary>
/// <remarks>
/// Books need this axis more than any other kind: a writer's upstream bibliography is unbounded and
/// mostly noise - foreign reprints, boxed sets, single chapters sold separately, companion volumes - and
/// without a filter applied before rows are created, adding one writer creates hundreds of items nobody
/// wants. Every facet here is declared as applying at materialization for that reason.
/// </remarks>
public static class BooksFacets
{
    /// <summary>Which languages of manifestation should exist at all.</summary>
    public const string Language = "language";

    /// <summary>The prominence a work must reach to be worth a row.</summary>
    public const string MinimumPopularity = "min-popularity";

    /// <summary>The length a manifestation must reach to be worth a row.</summary>
    public const string MinimumPages = "min-pages";

    /// <summary>Whether to drop works whose publication date is unknown.</summary>
    public const string SkipUndated = "skip-missing-date";

    /// <summary>Whether to drop manifestations carrying no public identifier.</summary>
    public const string SkipUnidentified = "skip-missing-identifier";

    /// <summary>Whether to drop omnibuses and single-part extracts.</summary>
    public const string SkipPartsAndSets = "skip-parts-and-sets";

    /// <summary>Whether to drop works that are companion entries rather than core ones.</summary>
    public const string SkipCollectionSecondary = "skip-series-secondary";
}

/// <summary>
/// The states this extension declares an item may be in.
/// </summary>
public static class BooksStates
{
    /// <summary>Wanted, published, and no file satisfies it.</summary>
    public const string Missing = "missing";

    /// <summary>At least one file satisfies it.</summary>
    public const string Held = "have";

    /// <summary>Held in part: some of the parts a manifestation spans are absent.</summary>
    public const string Partial = "partial";

    /// <summary>Satisfied, but by a copy below the configured ceiling.</summary>
    public const string Upgradable = "upgradable";

    /// <summary>Announced but not yet published, so its absence is not a gap.</summary>
    public const string Unpublished = "unpublished";
}
