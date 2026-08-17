#pragma warning disable ARX0013 // Shape contracts are experimental; an action's subjects are item references.

using System.Collections.ObjectModel;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Services;

/// <summary>
/// The body of an action invocation: what the action is being done to, and the values it asked for.
/// </summary>
/// <remarks>
/// <para>
/// The published surface fixes the address an action is invoked at but not the body it is invoked with,
/// so this shape is the client's half of a contract the server must mirror. It is deliberately as small
/// as the declaration allows: a list of subjects, whose length is decided by
/// <see cref="Abstractions.Intent.ActionScope"/>, and the parameter values the declaration asked for,
/// keyed by <see cref="Abstractions.Intent.ActionParameter.ParameterId"/> and carried as text because
/// that is what a declared parameter's value shape is rendered to and read from everywhere else.
/// </para>
/// <para>
/// Nothing here names a media concept, and nothing here is optional-by-convention: an action with no
/// subjects sends an empty list rather than omitting the member.
/// </para>
/// </remarks>
public sealed record ActionRequest
{
    /// <summary>
    /// Gets the items the action is being done to. Empty for an action whose scope is a level, a kind or
    /// the platform.
    /// </summary>
    public IReadOnlyList<MediaItemRef> Items { get; init; } = [];

    /// <summary>
    /// Gets the level the action applies at, when its scope is a level.
    /// </summary>
    public MediaLevelId? Level { get; init; }

    /// <summary>
    /// Gets the parameter values, keyed by the declared parameter identifier.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;
}
