using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>Where an axis reading came from, in ascending order of trust.</summary>
/// <remarks>
/// Ordered, and the order is load-bearing: when two sources disagree the later one wins, which is what
/// replaces a per-kind list of sources whose stated resolution must be ignored. A camera capture's title
/// claiming 1080p is <see cref="ReleaseTitle"/>; a probe measuring 480 lines is
/// <see cref="ContainerProbe"/>; the probe wins with no per-kind rule required.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum EvidenceSource
{
    /// <summary>Inferred by a stated default rather than read from anything. The weakest claim there is.</summary>
    Assumed = 0,

    /// <summary>Read from the release title.</summary>
    ReleaseTitle = 1,

    /// <summary>Read from the file's own name, which at least survived a download.</summary>
    FileName = 2,

    /// <summary>Read from the container's declared streams.</summary>
    ContainerProbe = 3,

    /// <summary>Measured by decoding the stream.</summary>
    StreamProbe = 4,

    /// <summary>Stated by the user, who is allowed to be wrong and is allowed to overrule us.</summary>
    UserOverride = 5,
}
