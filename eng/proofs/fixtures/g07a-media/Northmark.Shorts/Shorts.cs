using System.Collections.Generic;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;

namespace Northmark.Shorts.Extension;

/// <summary>Defines the short-film media kind.</summary>
public sealed partial class Shorts() :
    MediaType<ShortFilm, ReleaseTarget<ShortFilm>, Release<Video>, ShortFilmReleaseParser>(
        MediaKindId.FromString("shorts"),
        "Short",
        "Shorts",
        formats: [new FormatUse<Video>(VideoFormat.Definition)],
        availability: new OrderedSelectionDefinition<ShortFilm, ShortFilmStage>(
            film => film.Status,
            "Minimum availability",
            ShortFilmStage.Public))
{
    /// <summary>Gets the semantic searches a release source can satisfy for a short film.</summary>
    public override IReadOnlyList<SearchDefinition> Searches { get; } =
    [
        new("short", "Short film", [SearchTerm.WorkTitle], [SearchTerm.Year])
    ];

    /// <summary>Gets how a release's title is matched to a film.</summary>
    /// <remarks>Declared rather than defaulted: admission requires at least one key layer.</remarks>
    public override MatchingDefinition<ShortFilm> Matching { get; } = new()
    {
        Layers = [new("own-title", film => new[] { film.Title })]
    };

    /// <summary>Gets the typed source-query plan.</summary>
    /// <remarks>Declared rather than defaulted: admission requires at least one tier.</remarks>
    public override QueryDefinition<ShortFilm> Querying { get; } = new()
    {
        Tiers =
        [
            new("text", "short")
            {
                Arguments =
                [
                    new QueryPropertyArgument<ShortFilm, string>(SearchTerm.WorkTitle, film => film.Title)
                ],
                FreeText = film => film.Title
            }
        ]
    };
}
