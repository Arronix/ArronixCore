using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>How a video stream is scanned.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ScanType
{
    /// <summary>Whole frames, one after another.</summary>
    Progressive = 0,

    /// <summary>Alternating half-height fields.</summary>
    Interlaced = 1,

    /// <summary>Film frames redistributed across fields at a different rate.</summary>
    Telecined = 2,
}
