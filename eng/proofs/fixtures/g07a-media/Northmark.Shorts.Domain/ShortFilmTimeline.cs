using System;
using Arronix.Abstractions.Media;

namespace Northmark.Shorts;

/// <summary>Where a short film has got to in its release lifecycle.</summary>
/// <remarks>Ordered from least to most available.</remarks>
public enum ShortFilmStage
{
    /// <summary>Nothing has been announced.</summary>
    Unannounced = 0,

    /// <summary>A festival showing is scheduled or has happened.</summary>
    Festival = 1,

    /// <summary>The film can be watched outside a festival.</summary>
    Public = 2
}

/// <summary>The milestones a short film's availability is decided from.</summary>
public sealed record ShortFilmTimeline : IReleaseTimeline<ShortFilmStage>
{
    /// <summary>Gets the first festival showing.</summary>
    public DateOnly? Premiered { get; init; }

    /// <summary>Gets the first showing outside a festival.</summary>
    public DateOnly? Published { get; init; }

    /// <summary>Gets the date the stage is evaluated against.</summary>
    public required DateOnly EvaluatedOn { get; init; }

    /// <summary>Gets the release stage on <see cref="EvaluatedOn"/>.</summary>
    /// <remarks>Derived from the milestones, so a payload cannot state a stage its dates do not support.</remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public ShortFilmStage Stage => StageOn(EvaluatedOn);

    /// <summary>Determines the release stage on a specified date.</summary>
    /// <param name="date">The date to evaluate.</param>
    /// <returns>The stage on <paramref name="date"/>.</returns>
    public ShortFilmStage StageOn(DateOnly date) =>
        Published <= date ? ShortFilmStage.Public
            : Premiered <= date ? ShortFilmStage.Festival
            : ShortFilmStage.Unannounced;
}
