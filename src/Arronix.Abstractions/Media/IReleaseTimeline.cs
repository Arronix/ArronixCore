
namespace Arronix.Abstractions.Media;

/// <summary>Exposes the current release stage of a media-owned timeline.</summary>
/// <typeparam name="TReleaseStage">The media type's release-stage vocabulary.</typeparam>
/// <remarks>
/// The common contract states only the mechanism every media item shares. Concrete timelines retain their
/// own milestones and stage calculation; the core does not flatten them into a universal date dictionary.
/// </remarks>
public interface IReleaseTimeline<out TReleaseStage>
    where TReleaseStage : struct, Enum
{
    /// <summary>Gets the release stage at the timeline's evaluation point.</summary>
    TReleaseStage Stage { get; }
}
