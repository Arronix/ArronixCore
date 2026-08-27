using System.Reflection;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Northmark.Shorts;

// Writes the short film the G07A browser proof reads, through this package's own generated client contract
// entry point — the same reflection lookup a browser's Client build performs, so the file in the repository
// is what the contract writes rather than a hand-authored document. One argument: where to write it.

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: Northmark.Shorts.Fixture <output-path>");
    return 2;
}

var declaration = typeof(ShortFilm).Assembly
    .GetCustomAttributes<ClientContractEntryPointAttribute>()
    .Single();

var film = new ShortFilm
{
    ExternalIds = ExternalIdSet.Of(ExternalId.Of("northmark", 4417)),
    Title = "The Lighthouse Keeper's Watch",
    TitleLanguage = Language.English,
    Overview = "A keeper's last night before the light is automated away.",
    Year = 2024,
    Genres = ["Drama"],
    Premiere = new FestivalPremiere("Sundance", 2024),
    Lifecycle = new ShortFilmTimeline
    {
        Premiered = new DateOnly(2024, 1, 20),
        EvaluatedOn = new DateOnly(2026, 8, 27),
    },
    Artwork = ArtworkSet.Of(new ArtworkImage(
        "poster",
        new Uri(
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAMCAIAAADQ/GvKAAAAEklEQVR42mNQcGjAihhGJdARABgLVAFPROX0AAAAAElFTkSuQmCC"),
        8,
        12)),
};

var bytes = declaration.Serialize(film);
var path = args[0];
var directory = Path.GetDirectoryName(path);

if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

File.WriteAllBytes(path, bytes);
Console.WriteLine($"Wrote {bytes.Length} bytes to {path}.");
return 0;
