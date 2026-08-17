using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// Binds one named capture group of a title pattern to an output.
/// </summary>
/// <param name="GroupName">The regular-expression group name.</param>
/// <param name="Target">What the captured text becomes.</param>
/// <param name="SpaceId">The coordinate space addressed, when the target is a coordinate component.</param>
/// <param name="ComponentId">The coordinate component addressed, when the target is a coordinate component.</param>
/// <param name="Key">
/// The external-identifier scheme, tag key or release-kind value the capture lands under, for the
/// targets that need one.
/// </param>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct CaptureBinding(
    string GroupName,
    CaptureTarget Target,
    string? SpaceId = null,
    string? ComponentId = null,
    string? Key = null);
