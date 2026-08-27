
using System.Text.Json.Serialization;
using Arronix.Abstractions.Media;

namespace Arronix.Media.Movies;

/// <summary>A movie's position in its release lifecycle.</summary>
/// <remarks>Values are ordered from least to most available.</remarks>
public enum MovieReleaseStage
{
    /// <summary>No release date has been announced.</summary>
    Tba = 0,

    /// <summary>A future theatrical or home release is scheduled.</summary>
    Announced = 1,

    /// <summary>The movie is in its theatrical release window.</summary>
    InCinemas = 2,

    /// <summary>The movie is available through a home release channel.</summary>
    Released = 3
}

/// <summary>Release milestones used to determine a movie's availability.</summary>
public sealed record MovieReleaseTimeline : IReleaseTimeline<MovieReleaseStage>
{
    private const int TheatricalWindowDays = 90;

    /// <summary>Gets the first theatrical release.</summary>
    public DateOnly? InCinemas { get; init; }

    /// <summary>Gets the first retail physical-media release.</summary>
    public DateOnly? Physical { get; init; }

    /// <summary>Gets the first retail digital release.</summary>
    public DateOnly? Digital { get; init; }

    /// <summary>Gets the explicit date on which <see cref="Stage"/> was evaluated.</summary>
    public required DateOnly EvaluatedOn { get; init; }

    /// <summary>Gets the earliest home release, or the theatrical release when no home date is known.</summary>
    /// <remarks>Not written to the wire: it is the earliest of the dates the payload already carries.</remarks>
    [JsonIgnore]
    public DateOnly? AvailableOn => Earliest(Physical, Digital) ?? InCinemas;

    /// <summary>Gets the release stage on <see cref="EvaluatedOn"/>.</summary>
    /// <remarks>
    /// Not written to the wire. The milestones and the evaluation date decide it, and a consumer that read
    /// a stage out of a payload would be reading whichever stage the sender chose rather than the one this
    /// timeline's own rule produces.
    /// </remarks>
    [JsonIgnore]
    public MovieReleaseStage Stage => StageOn(EvaluatedOn);

    /// <summary>Determines the release stage on a specified date.</summary>
    /// <param name="date">The date to evaluate.</param>
    /// <returns>The release stage on <paramref name="date"/>.</returns>
    public MovieReleaseStage StageOn(DateOnly date)
    {
        if (Physical <= date || Digital <= date)
        {
            return MovieReleaseStage.Released;
        }

        if (InCinemas is { } cinema)
        {
            var noHomeRelease = Physical is null && Digital is null;
            return noHomeRelease && date > cinema.AddDays(TheatricalWindowDays)
                ? MovieReleaseStage.Released
                : date >= cinema ? MovieReleaseStage.InCinemas : MovieReleaseStage.Announced;
        }

        return Physical is null && Digital is null
            ? MovieReleaseStage.Tba
            : MovieReleaseStage.Announced;
    }

    private static DateOnly? Earliest(DateOnly? left, DateOnly? right) =>
        (left, right) switch
        {
            ({ } first, { } second) => first <= second ? first : second,
            ({ } first, null) => first,
            (null, { } second) => second,
            _ => null
        };
}
