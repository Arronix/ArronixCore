using Arronix.Abstractions.Media;

namespace Northmark.Shorts;

/// <summary>A short film, as this catalog and library shapes one.</summary>
public sealed class ShortFilm : MediaItem<ShortFilm, ShortFilmTimeline, ShortFilmStage>
{
    /// <summary>Gets the festival the film premiered at, when it premiered at one.</summary>
    public FestivalPremiere? Premiere { get; init; }
}

/// <summary>A film's first public showing at a festival.</summary>
/// <param name="Festival">The festival's name.</param>
/// <param name="Edition">The year of the edition it showed in.</param>
public sealed record FestivalPremiere(string Festival, int Edition);
