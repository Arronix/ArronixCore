
using Arronix.Abstractions.Media;

namespace Arronix.Media.Movies;

/// <summary>A movie catalog and library item.</summary>
public sealed class Movie : MediaItem<Movie, MovieReleaseTimeline, MovieReleaseStage>;
