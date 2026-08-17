using System.Linq;

namespace Arronix.Plugin.Music.Seed;

/// <summary>
/// One performer in the seeded catalog.
/// </summary>
public sealed record SeedPerformer
{
    /// <summary>Gets the identifier, unique within the performer level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the performer's name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the performer's sorting form.</summary>
    public required string SortName { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>Gets whether the performer is a person, a group and so on.</summary>
    public required string PerformerKind { get; init; }

    /// <summary>Gets what tells this performer apart from same-named others.</summary>
    public string? Disambiguation { get; init; }

    /// <summary>Gets long-form prose about the performer.</summary>
    public string Overview { get; init; } = string.Empty;

    /// <summary>Gets the performer's genres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];
}

/// <summary>
/// One work in the seeded catalog.
/// </summary>
public sealed record SeedWork
{
    /// <summary>Gets the identifier, unique within the work level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the performer this work belongs to.</summary>
    public required int PerformerId { get; init; }

    /// <summary>Gets the work's title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the date the work was first issued.</summary>
    public required DateOnly ReleaseDate { get; init; }

    /// <summary>Gets the work's primary classification.</summary>
    public required string PrimaryKind { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>Gets the work's secondary classifications.</summary>
    public IReadOnlyList<string> SecondaryKinds { get; init; } = [];

    /// <summary>Gets what tells this work apart from same-named others.</summary>
    public string? Disambiguation { get; init; }
}

/// <summary>
/// One competing pressing of a work.
/// </summary>
public sealed record SeedPressing
{
    /// <summary>Gets the identifier, unique within the pressing level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the work this pressing manifests.</summary>
    public required int WorkId { get; init; }

    /// <summary>Gets the pressing's title, which may differ from the work's.</summary>
    public required string Title { get; init; }

    /// <summary>Gets whether the pressing is official, promotional, unofficial or fabricated.</summary>
    public required string Provenance { get; init; }

    /// <summary>Gets where the pressing was issued.</summary>
    public required string Country { get; init; }

    /// <summary>Gets the company that issued the pressing.</summary>
    public required string Issuer { get; init; }

    /// <summary>Gets how the pressing was carried, and across how many carriers.</summary>
    public required string CarrierFormat { get; init; }

    /// <summary>Gets the date the pressing was issued.</summary>
    public required DateOnly ReleaseDate { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the catalog would choose this pressing when nothing else is
    /// known. This is a catalog opinion, not library state: which pressing a library has actually
    /// selected is the host's to store, on the work above.
    /// </summary>
    public bool IsCatalogPreference { get; init; }

    /// <summary>Gets what tells this pressing apart from its siblings.</summary>
    public string? Disambiguation { get; init; }
}

/// <summary>
/// One recording within a pressing's running order.
/// </summary>
public sealed record SeedRecording
{
    /// <summary>Gets the identifier, unique within the recording level.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the pressing whose running order this recording sits in.</summary>
    public required int PressingId { get; init; }

    /// <summary>Gets the recording's title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets which carrier of the pressing the recording sits on.</summary>
    public required int Carrier { get; init; }

    /// <summary>Gets the recording's place on that carrier.</summary>
    public required int Position { get; init; }

    /// <summary>Gets the recording's place in the pressing as a whole.</summary>
    public required int AbsolutePosition { get; init; }

    /// <summary>Gets how long the recording runs for.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the lettered side position, where the carrier has sides.</summary>
    public string? SidePosition { get; init; }

    /// <summary>Gets the performer credited on this recording, when it is not the work's.</summary>
    public string? Performer { get; init; }

    /// <summary>Gets the external catalog's identifier.</summary>
    public required string CatalogId { get; init; }
}

/// <summary>
/// A small, fixed catalog this extension projects, standing in for a metadata pipeline that does not
/// exist yet.
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately not empty and deliberately not uniform. Two works carry two pressings each with
/// different running orders, because the whole point of the variant level is that "complete" means a
/// different number depending on which pressing is selected. One pressing spans two carriers, because a
/// sequence axis with only ever one value proves nothing.
/// </para>
/// <para>
/// It is a catalog, not a library: nothing here records what the user wants, which pressing they chose
/// or which files they hold. Those are the host's to store, and this extension cannot see them.
/// </para>
/// </remarks>
public static class MusicSeed
{
    /// <summary>Gets the seeded performers.</summary>
    public static IReadOnlyList<SeedPerformer> Performers { get; } =
    [
        new SeedPerformer
        {
            Id = 1,
            Name = "Radiohead",
            SortName = "Radiohead",
            CatalogId = "a74b1b7f-71a5-4011-9441-d0b5e4122711",
            PerformerKind = "group",
            Overview = "An English rock band formed in Abingdon in 1985.",
            Genres = ["Rock", "Alternative", "Electronic"],
        },
        new SeedPerformer
        {
            Id = 2,
            Name = "Miles Davis",
            SortName = "Davis, Miles",
            CatalogId = "561d854a-6a28-4aa7-8c99-323e6ce46c2a",
            PerformerKind = "person",
            Overview = "An American trumpeter, bandleader and composer.",
            Genres = ["Jazz"],
        },
    ];

    /// <summary>Gets the seeded works.</summary>
    public static IReadOnlyList<SeedWork> Works { get; } =
    [
        new SeedWork
        {
            Id = 101,
            PerformerId = 1,
            Title = "OK Computer",
            ReleaseDate = new DateOnly(1997, 5, 21),
            PrimaryKind = "album",
            SecondaryKinds = ["studio"],
            CatalogId = "0b2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0001",
        },
        new SeedWork
        {
            Id = 102,
            PerformerId = 1,
            Title = "In Rainbows",
            ReleaseDate = new DateOnly(2007, 10, 10),
            PrimaryKind = "album",
            SecondaryKinds = ["studio"],
            CatalogId = "0b2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0002",
        },
        new SeedWork
        {
            Id = 103,
            PerformerId = 2,
            Title = "Kind of Blue",
            ReleaseDate = new DateOnly(1959, 8, 17),
            PrimaryKind = "album",
            SecondaryKinds = ["studio"],
            CatalogId = "0b2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0003",
        },
    ];

    /// <summary>Gets the seeded pressings.</summary>
    public static IReadOnlyList<SeedPressing> Pressings { get; } =
    [
        new SeedPressing
        {
            Id = 201,
            WorkId = 101,
            Title = "OK Computer",
            Provenance = "official",
            Country = "GB",
            Issuer = "Parlophone",
            CarrierFormat = "CD",
            ReleaseDate = new DateOnly(1997, 5, 21),
            CatalogId = "1c2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0201",
            IsCatalogPreference = true,
        },
        new SeedPressing
        {
            // The pressing that makes the variant axis load-bearing: a later edition with a second carrier
            // of previously unissued recordings. Selecting it changes the denominator of "complete" from
            // twelve to seventeen without a single file moving.
            Id = 202,
            WorkId = 101,
            Title = "OK Computer OKNOTOK 1997 2017",
            Provenance = "official",
            Country = "XW",
            Issuer = "XL Recordings",
            CarrierFormat = "2xCD",
            ReleaseDate = new DateOnly(2017, 6, 23),
            CatalogId = "1c2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0202",
            Disambiguation = "20th anniversary edition",
        },
        new SeedPressing
        {
            Id = 203,
            WorkId = 102,
            Title = "In Rainbows",
            Provenance = "official",
            Country = "GB",
            Issuer = "XL Recordings",
            CarrierFormat = "CD",
            ReleaseDate = new DateOnly(2007, 12, 28),
            CatalogId = "1c2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0203",
            IsCatalogPreference = true,
        },
        new SeedPressing
        {
            Id = 204,
            WorkId = 103,
            Title = "Kind of Blue",
            Provenance = "official",
            Country = "US",
            Issuer = "Columbia",
            CarrierFormat = "LP",
            ReleaseDate = new DateOnly(1959, 8, 17),
            CatalogId = "1c2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0204",
            IsCatalogPreference = true,
        },
        new SeedPressing
        {
            Id = 205,
            WorkId = 103,
            Title = "Kind of Blue (Legacy Edition)",
            Provenance = "official",
            Country = "US",
            Issuer = "Legacy",
            CarrierFormat = "2xCD",
            ReleaseDate = new DateOnly(2008, 9, 30),
            CatalogId = "1c2a8f6b-4e5a-4b0e-9c33-1c4a4b8b0205",
            Disambiguation = "Legacy edition",
        },
    ];

    /// <summary>Gets the seeded recordings.</summary>
    public static IReadOnlyList<SeedRecording> Recordings { get; } = BuildRecordings();

    /// <summary>
    /// Returns the pressings of one work, in catalog order.
    /// </summary>
    /// <param name="workId">The work.</param>
    /// <returns>Its pressings.</returns>
    public static IReadOnlyList<SeedPressing> PressingsOf(int workId) =>
        Pressings.Where(pressing => pressing.WorkId == workId).ToList();

    /// <summary>
    /// Returns the running order of one pressing.
    /// </summary>
    /// <param name="pressingId">The pressing.</param>
    /// <returns>Its recordings, in absolute order.</returns>
    public static IReadOnlyList<SeedRecording> RecordingsOf(long pressingId) =>
        Recordings.Where(recording => recording.PressingId == pressingId)
            .OrderBy(recording => recording.AbsolutePosition)
            .ToList();

    private static List<SeedRecording> BuildRecordings()
    {
        var recordings = new List<SeedRecording>();
        var nextId = 1001;

        AddCarrier(
            recordings,
            ref nextId,
            201,
            1,
            null,
            [
                ("Airbag", 284), ("Paranoid Android", 383), ("Subterranean Homesick Alien", 267),
                ("Exit Music (For a Film)", 264), ("Let Down", 299), ("Karma Police", 261),
                ("Fitter Happier", 117), ("Electioneering", 230), ("Climbing Up the Walls", 285),
                ("No Surprises", 229), ("Lucky", 259), ("The Tourist", 324),
            ]);

        AddCarrier(
            recordings,
            ref nextId,
            202,
            1,
            null,
            [
                ("Airbag", 284), ("Paranoid Android", 383), ("Subterranean Homesick Alien", 267),
                ("Exit Music (For a Film)", 264), ("Let Down", 299), ("Karma Police", 261),
                ("Fitter Happier", 117), ("Electioneering", 230), ("Climbing Up the Walls", 285),
                ("No Surprises", 229), ("Lucky", 259), ("The Tourist", 324),
            ]);

        AddCarrier(
            recordings,
            ref nextId,
            202,
            2,
            null,
            [
                ("I Promise", 268), ("Man of War", 292), ("Lift", 283),
                ("Lull", 145), ("Meeting in the Aisle", 189),
            ]);

        AddCarrier(
            recordings,
            ref nextId,
            203,
            1,
            null,
            [
                ("15 Step", 237), ("Bodysnatchers", 242), ("Nude", 255), ("Weird Fishes / Arpeggi", 318),
                ("All I Need", 228), ("Faust Arp", 129), ("Reckoner", 290), ("House of Cards", 328),
                ("Jigsaw Falling into Place", 249), ("Videotape", 280),
            ]);

        AddCarrier(
            recordings,
            ref nextId,
            204,
            1,
            "A",
            [("So What", 545), ("Freddie Freeloader", 589), ("Blue in Green", 337)]);

        AddCarrier(
            recordings,
            ref nextId,
            204,
            1,
            "B",
            [("All Blues", 691), ("Flamenco Sketches", 566)]);

        AddCarrier(
            recordings,
            ref nextId,
            205,
            1,
            null,
            [
                ("So What", 545), ("Freddie Freeloader", 589), ("Blue in Green", 337),
                ("All Blues", 691), ("Flamenco Sketches", 566), ("Flamenco Sketches (alternate take)", 569),
            ]);

        AddCarrier(
            recordings,
            ref nextId,
            205,
            2,
            null,
            [
                ("On Green Dolphin Street", 585), ("Fran-Dance", 350), ("Stella by Starlight", 285),
                ("Love for Sale", 715), ("Fran-Dance (alternate take)", 350),
            ]);

        return recordings;
    }

    private static void AddCarrier(
        List<SeedRecording> recordings,
        ref int nextId,
        int pressingId,
        int carrier,
        string? sideLetter,
        IReadOnlyList<(string Title, int Seconds)> entries)
    {
        var positionOnCarrier = recordings.Count(
            recording => recording.PressingId == pressingId && recording.Carrier == carrier);
        var absolute = recordings.Count(recording => recording.PressingId == pressingId);

        for (var index = 0; index < entries.Count; index++)
        {
            positionOnCarrier++;
            absolute++;

            recordings.Add(new SeedRecording
            {
                Id = nextId++,
                PressingId = pressingId,
                Title = entries[index].Title,
                Carrier = carrier,
                Position = positionOnCarrier,
                AbsolutePosition = absolute,
                Duration = TimeSpan.FromSeconds(entries[index].Seconds),
                SidePosition = sideLetter is null
                    ? null
                    : sideLetter + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                CatalogId = "2c2a8f6b-4e5a-4b0e-9c33-"
                    + (nextId - 1).ToString("D12", System.Globalization.CultureInfo.InvariantCulture),
            });
        }
    }
}
