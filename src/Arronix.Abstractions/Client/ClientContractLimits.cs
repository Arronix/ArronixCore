using System.ComponentModel;

namespace Arronix.Abstractions.Client;

/// <summary>How large a graph a client contract may describe.</summary>
/// <remarks>
/// One set of bounds for every walk over a shape a contract produced — its serialization graph, its
/// projection schema — so that two walks cannot drift into disagreeing about what is describable. Real
/// shapes are orders of magnitude smaller; these are the point past which a shape is refused rather than
/// followed.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ClientContractLimits
{
    /// <summary>The deepest nesting a contract may describe.</summary>
    public const int MaxDepth = 32;

    /// <summary>The most values a contract may describe, across the whole graph.</summary>
    public const int MaxNodes = 4096;
}
