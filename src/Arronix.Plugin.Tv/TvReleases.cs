
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;
using Arronix.Format.Video;

namespace Arronix.Plugin.Tv;

/// <summary>A typed episode coordinate in one of television's legitimate addressing schemes.</summary>
public abstract record EpisodeCoordinate
{
    private EpisodeCoordinate()
    {
    }

    /// <summary>A season-and-episode coordinate.</summary>
    public sealed record Ordinal(int Season, int Episode) : EpisodeCoordinate;

    /// <summary>An absolute episode number.</summary>
    public sealed record Absolute(int Number) : EpisodeCoordinate;

    /// <summary>A broadcast-date coordinate.</summary>
    public sealed record AirDate(DateOnly Date) : EpisodeCoordinate;
}

/// <summary>One unit requested from a television series.</summary>
public sealed record EpisodeTarget(string SeriesTitle, EpisodeCoordinate Coordinate) : IReleaseTarget;

/// <summary>An acquisition intent that can contain one episode, a bounded span or a complete season.</summary>
public sealed record TelevisionReleaseTarget(IReadOnlySet<EpisodeTarget> Episodes) : IReleaseTarget;

/// <summary>A publication interpreted as television, including every unit it states it covers.</summary>
public sealed record TelevisionRelease(
    string SeriesTitle,
    IReadOnlySet<EpisodeCoordinate> Episodes,
    Video? Representation) : IRelease
{
    public string Title => SeriesTitle;

    public int? Year => null;

    public string? Edition => null;
}

/// <summary>The television media type's default fragment over video-backed releases.</summary>
public static class TelevisionReleasePolicy
{
    /// <summary>Gets the compiled default. Channel is intentionally not globally ordered.</summary>
    public static ReleasePolicy<TelevisionRelease> Default { get; } =
        ReleasePolicy<TelevisionRelease>.Compile(policy =>
            VideoReleasePolicyDefaults.Configure(policy, release => release.Representation));
}
