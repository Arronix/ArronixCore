using System.Linq;

namespace Arronix.Plugin.Books.Seed;

/// <summary>
/// One writer in the seeded catalog.
/// </summary>
public sealed record SeedWriter
{
    /// <summary>Gets the identifier, unique within the writer level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the writer's name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the writer's shelving form.</summary>
    public required string SortName { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>Gets long-form prose about the writer.</summary>
    public string Overview { get; init; } = string.Empty;

    /// <summary>Gets the writer's genres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];
}

/// <summary>
/// One work in the seeded catalog.
/// </summary>
public sealed record SeedWork
{
    /// <summary>Gets the identifier, unique within the work level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the writer this work belongs to.</summary>
    public required int WriterId { get; init; }

    /// <summary>Gets the work's full title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the date the work was first published.</summary>
    public required DateOnly ReleaseDate { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>Gets the part of the title after the colon.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Gets the catalog's measure of prominence.</summary>
    public double Popularity { get; init; }
}

/// <summary>
/// One manifestation of a work.
/// </summary>
public sealed record SeedManifestation
{
    /// <summary>Gets the identifier, unique within the manifestation level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the work this manifestation manifests.</summary>
    public required int WorkId { get; init; }

    /// <summary>Gets the manifestation's title, which may differ from the work's.</summary>
    public required string Title { get; init; }

    /// <summary>Gets which format family the manifestation belongs to.</summary>
    public required string Flavor { get; init; }

    /// <summary>Gets the catalog's own words for the carrier.</summary>
    public required string CarrierDescription { get; init; }

    /// <summary>Gets the house that issued the manifestation.</summary>
    public required string Issuer { get; init; }

    /// <summary>Gets the language the manifestation is in.</summary>
    public required string LanguageCode { get; init; }

    /// <summary>Gets the date the manifestation was issued.</summary>
    public required DateOnly ReleaseDate { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>Gets how many pages the manifestation runs to, where it has pages.</summary>
    public int? PageCount { get; init; }

    /// <summary>Gets how long the manifestation runs for, where it is spoken.</summary>
    public TimeSpan? RunningTime { get; init; }

    /// <summary>
    /// Gets how many files a complete copy of this manifestation consists of. One for a written copy,
    /// several for a spoken one split by chapter.
    /// </summary>
    public int PartCount { get; init; } = 1;

    /// <summary>Gets the thirteen-digit book number, where the manifestation has one.</summary>
    public string? BookNumber { get; init; }

    /// <summary>Gets the retailer's identifier, where the manifestation has one.</summary>
    public string? RetailerId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the catalog would choose this manifestation when nothing else is
    /// known. A catalog opinion, not library state.
    /// </summary>
    public bool IsCatalogPreference { get; init; }
}

/// <summary>
/// One collection a work may belong to.
/// </summary>
public sealed record SeedCollection
{
    /// <summary>Gets the identifier, unique within the collection axis.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the collection's title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>Gets a value indicating whether the collection's positions are numbered at all.</summary>
    public bool IsNumbered { get; init; } = true;
}

/// <summary>
/// One work's membership of one collection.
/// </summary>
/// <param name="CollectionId">The collection.</param>
/// <param name="WorkId">The member work.</param>
/// <param name="Position">
/// The label the collection gives the work. A number, a fractional number, a range or the empty string,
/// which is precisely why it is not an integer.
/// </param>
/// <param name="SortIndex">The ordering key, which the label cannot always supply.</param>
/// <param name="IsPrimary">Whether the work is a core entry rather than a companion.</param>
public readonly record struct SeedMembership(
    int CollectionId,
    int WorkId,
    string Position,
    long SortIndex,
    bool IsPrimary);

/// <summary>
/// A small, fixed catalog this extension projects, standing in for a metadata pipeline that does not
/// exist yet.
/// </summary>
/// <remarks>
/// It is chosen to exercise the awkward cases rather than the tidy ones: one work carries both a written
/// and a spoken manifestation, one spoken manifestation is split across many files, one work belongs to
/// two collections at once, one collection spans two writers, and one membership label is fractional while
/// another is a range and another is empty.
/// </remarks>
public static class BooksSeed
{
    /// <summary>Gets the seeded writers.</summary>
    public static IReadOnlyList<SeedWriter> Writers { get; } =
    [
        new SeedWriter
        {
            Id = 1,
            Name = "Ursula K. Le Guin",
            SortName = "Le Guin, Ursula K.",
            CatalogId = "874602",
            Overview = "An American author of speculative fiction, poetry and essays.",
            Genres = ["Science fiction", "Fantasy"],
        },
        new SeedWriter
        {
            Id = 2,
            Name = "Mary Shelley",
            SortName = "Shelley, Mary",
            CatalogId = "11139",
            Overview = "An English novelist and essayist.",
            Genres = ["Gothic", "Science fiction"],
        },
    ];

    /// <summary>Gets the seeded works.</summary>
    public static IReadOnlyList<SeedWork> Works { get; } =
    [
        new SeedWork
        {
            Id = 101,
            WriterId = 1,
            Title = "The Left Hand of Darkness",
            ReleaseDate = new DateOnly(1969, 3, 1),
            CatalogId = "18423",
            Popularity = 1_240_000,
        },
        new SeedWork
        {
            Id = 102,
            WriterId = 1,
            Title = "The Dispossessed: An Ambiguous Utopia",
            Subtitle = "An Ambiguous Utopia",
            ReleaseDate = new DateOnly(1974, 5, 1),
            CatalogId = "13651",
            Popularity = 980_000,
        },
        new SeedWork
        {
            Id = 103,
            WriterId = 1,
            Title = "The Word for World Is Forest",
            ReleaseDate = new DateOnly(1972, 1, 1),
            CatalogId = "68021",
            Popularity = 210_000,
        },
        new SeedWork
        {
            Id = 104,
            WriterId = 2,
            Title = "Frankenstein",
            ReleaseDate = new DateOnly(1818, 1, 1),
            CatalogId = "35031085",
            Popularity = 4_100_000,
        },
    ];

    /// <summary>Gets the seeded manifestations.</summary>
    public static IReadOnlyList<SeedManifestation> Manifestations { get; } =
    [
        new SeedManifestation
        {
            Id = 201,
            WorkId = 101,
            Title = "The Left Hand of Darkness",
            Flavor = BooksShape.WrittenFamilyId,
            CarrierDescription = "Kindle Edition",
            Issuer = "Ace Books",
            LanguageCode = "eng",
            ReleaseDate = new DateOnly(2017, 4, 25),
            CatalogId = "34842320",
            PageCount = 304,
            BookNumber = "9780441007318",
            RetailerId = "B008YOA9AS",
            IsCatalogPreference = true,
        },
        new SeedManifestation
        {
            // The manifestation that makes two families necessary. It is not a better copy of the written
            // one and it is not a worse one; it is a different artifact, and no single ladder can say so.
            Id = 202,
            WorkId = 101,
            Title = "The Left Hand of Darkness (Unabridged)",
            Flavor = BooksShape.SpokenFamilyId,
            CarrierDescription = "Audible Audio",
            Issuer = "Audible Studios",
            LanguageCode = "eng",
            ReleaseDate = new DateOnly(2012, 8, 21),
            CatalogId = "34842321",
            RunningTime = TimeSpan.FromMinutes(587),

            // Split by chapter: many files, one manifestation, and the ordinal on each link is what says
            // which is which.
            PartCount = 24,
            RetailerId = "B008YOA9B1",
        },
        new SeedManifestation
        {
            Id = 203,
            WorkId = 101,
            Title = "La main gauche de la nuit",
            Flavor = BooksShape.WrittenFamilyId,
            CarrierDescription = "Broche",
            Issuer = "Le Livre de Poche",
            LanguageCode = "fra",
            ReleaseDate = new DateOnly(2006, 2, 1),
            CatalogId = "34842322",
            PageCount = 320,
            BookNumber = "9782253072591",
        },
        new SeedManifestation
        {
            Id = 204,
            WorkId = 102,
            Title = "The Dispossessed",
            Flavor = BooksShape.WrittenFamilyId,
            CarrierDescription = "ebook",
            Issuer = "Harper Voyager",
            LanguageCode = "eng",
            ReleaseDate = new DateOnly(2011, 9, 13),
            CatalogId = "34842323",
            PageCount = 387,
            BookNumber = "9780061054884",
            IsCatalogPreference = true,
        },
        new SeedManifestation
        {
            Id = 205,
            WorkId = 103,
            Title = "The Word for World Is Forest",
            Flavor = BooksShape.WrittenFamilyId,
            CarrierDescription = "Paperback",
            Issuer = "Tor Books",
            LanguageCode = "eng",
            ReleaseDate = new DateOnly(2010, 4, 27),
            CatalogId = "34842324",
            PageCount = 189,
            BookNumber = "9780765324641",
            IsCatalogPreference = true,
        },
        new SeedManifestation
        {
            Id = 206,
            WorkId = 104,
            Title = "Frankenstein; or, The Modern Prometheus",
            Flavor = BooksShape.WrittenFamilyId,
            CarrierDescription = "ebook",
            Issuer = "Project Gutenberg",
            LanguageCode = "eng",
            ReleaseDate = new DateOnly(1993, 10, 1),
            CatalogId = "34842325",
            PageCount = 280,
            IsCatalogPreference = true,
        },
        new SeedManifestation
        {
            Id = 207,
            WorkId = 104,
            Title = "Frankenstein (Unabridged)",
            Flavor = BooksShape.SpokenFamilyId,
            CarrierDescription = "MP3 CD",
            Issuer = "LibriVox",
            LanguageCode = "eng",
            ReleaseDate = new DateOnly(2008, 3, 4),
            CatalogId = "34842326",
            RunningTime = TimeSpan.FromMinutes(508),
            PartCount = 26,
        },
    ];

    /// <summary>Gets the seeded collections.</summary>
    public static IReadOnlyList<SeedCollection> Collections { get; } =
    [
        new SeedCollection { Id = 301, Title = "Hainish Cycle", CatalogId = "40474" },
        new SeedCollection { Id = 302, Title = "SF Masterworks", CatalogId = "51391", IsNumbered = false },
    ];

    /// <summary>Gets the seeded collection memberships.</summary>
    /// <remarks>
    /// Every awkward case the label has to carry appears here: an ordinary number, a fractional position
    /// for an interstitial work, an empty label for an unnumbered collection, a work belonging to two
    /// collections at once, and a collection spanning two writers.
    /// </remarks>
    public static IReadOnlyList<SeedMembership> Memberships { get; } =
    [
        new SeedMembership(301, 101, "4", 4, true),
        new SeedMembership(301, 102, "6", 6, true),
        new SeedMembership(301, 103, "5.5", 5, false),
        new SeedMembership(302, 101, string.Empty, 1, false),
        new SeedMembership(302, 104, string.Empty, 2, false),
    ];

    /// <summary>
    /// Returns the manifestations of one work, in catalog order.
    /// </summary>
    /// <param name="workId">The work.</param>
    /// <returns>Its manifestations.</returns>
    public static IReadOnlyList<SeedManifestation> ManifestationsOf(int workId) =>
        Manifestations.Where(manifestation => manifestation.WorkId == workId).ToList();

    /// <summary>
    /// Returns the collections one work belongs to.
    /// </summary>
    /// <param name="workId">The work.</param>
    /// <returns>Its memberships.</returns>
    public static IReadOnlyList<SeedMembership> MembershipsOf(int workId) =>
        Memberships.Where(membership => membership.WorkId == workId).ToList();

    /// <summary>
    /// Returns the membership the naming tokens should read when several exist.
    /// </summary>
    /// <param name="workId">The work.</param>
    /// <returns>The designated membership, or <see langword="null"/> when the work belongs to none.</returns>
    /// <remarks>
    /// The impedance mismatch the primary-member flag exists to resolve: a token is single-valued and the
    /// relation is not, so one member has to be designated or the token is arbitrary.
    /// </remarks>
    public static SeedMembership? PrimaryMembershipOf(int workId)
    {
        var memberships = MembershipsOf(workId);

        if (memberships.Count == 0)
        {
            return null;
        }

        foreach (var membership in memberships)
        {
            if (membership.IsPrimary)
            {
                return membership;
            }
        }

        return memberships[0];
    }
}
