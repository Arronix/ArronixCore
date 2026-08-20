
namespace Arronix.Abstractions.Media;

/// <summary>
/// A typed statement of what an acquisition is intended to cover.
/// </summary>
/// <remarks>
/// Targets are ephemeral intent, not catalog entities and not provider queries. A movie target may name
/// one work; a television target may name an episode, a season, or a bounded span. The media extension
/// owns that shape and the common selection engine remains generic over it.
/// </remarks>
public interface IReleaseTarget;
