using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares one condition an item can be in, and what that condition means.
/// </summary>
/// <remarks>
/// States are declared rather than known, because the conditions differ per media kind and a consumer that
/// recognized them by name would have compile-time knowledge of a kind. What a consumer needs is not the name but the valence, which is why <see cref="Tone"/> is here.
/// </remarks>
public sealed record StateDescriptor
{
    /// <summary>
    /// Gets the identifier of the state, which is also the field value that puts an item in it.
    /// </summary>
    public required string StateId { get; init; }

    /// <summary>
    /// Gets the state's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets what being in this state means for the user.
    /// </summary>
    public required StateTone Tone { get; init; }

    /// <summary>
    /// Gets a sentence explaining the state.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the <see cref="FieldDescriptor.FieldId"/> whose value, when it equals
    /// <see cref="StateId"/>, puts an item in this state.
    /// </summary>
    public required string SourceFieldId { get; init; }
}

/// <summary>
/// What being in a state means for the user.
/// </summary>
/// <remarks>
/// It states valence and urgency, not appearance. A command line maps it to a prefix or an exit code, a screen reader to announcement priority, a hands-free assistant to phrasing.
/// </remarks>
public enum StateTone
{
    /// <summary>A fact about the item, neither good nor bad.</summary>
    Neutral = 0,

    /// <summary>What the user wanted has happened.</summary>
    Positive = 1,

    /// <summary>Something is outstanding and may need a decision.</summary>
    Attention = 2,

    /// <summary>Something has gone wrong.</summary>
    Problem = 3,

    /// <summary>Something is under way and will change on its own.</summary>
    InProgress = 4
}
