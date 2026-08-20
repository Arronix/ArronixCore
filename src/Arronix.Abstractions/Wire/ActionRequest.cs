using System.Collections.ObjectModel;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Wire;

/// <summary>The typed wire request for invoking a published action.</summary>
/// <remarks>
/// Item identity stays typed across client and server. Only parameter values use string keys and values,
/// because their concrete type is described by the matching <see cref="Intent.ActionParameter"/>.
/// </remarks>
public sealed record ActionRequest
{
    /// <summary>Gets the concrete items the action operates on.</summary>
    public IReadOnlyList<MediaItemRef> Items { get; init; } = [];

    /// <summary>Gets the level the action operates on when its scope is a level.</summary>
    public MediaLevelId? Level { get; init; }

    /// <summary>Gets values keyed by the parameter identifiers in the action descriptor.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Gets an invocation with no subjects or parameter values.</summary>
    public static ActionRequest Empty { get; } = new();
}
