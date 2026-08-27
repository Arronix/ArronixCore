
using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;

namespace Arronix.Client.Rendering;

/// <summary>
/// Reads a published item through the description of its level, without knowing what kind of thing it is.
/// </summary>
/// <remarks>
/// <para>
/// Every function here answers a question a view has — what is this item's picture, which of its fields
/// belong on a card, what conditions is it in — using only the level's declared fields and the kind's
/// declared states. There is no other way for this application to answer them: it has never heard of a
/// single one of the media kinds it renders.
/// </para>
/// <para>
/// The whole of this file is why a fifth media kind needs no change here. It asks the description what
/// the title field is; it does not know a title when it sees one.
/// </para>
/// </remarks>
public static class ItemProjection
{
    /// <summary>
    /// Gets the field that identifies an item to a person.
    /// </summary>
    /// <param name="level">The level's description.</param>
    /// <returns>The field, or <see langword="null"/> when the level declares none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    public static FieldDescriptor? TitleField(LevelPresentation level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return level.Fields.FirstOrDefault(field => field.Semantics.HasFlag(FieldSemantics.Title));
    }

    /// <summary>
    /// Gets the fields worth showing at or above an importance rank, in the order they were declared.
    /// </summary>
    /// <param name="level">The level's description.</param>
    /// <param name="upTo">The least important rank included.</param>
    /// <returns>The fields.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<FieldDescriptor> FieldsUpTo(LevelPresentation level, Prominence upTo)
    {
        ArgumentNullException.ThrowIfNull(level);

        return level.Fields
            .Where(field => field.Prominence <= upTo)
            .Where(field => !field.Semantics.HasFlag(FieldSemantics.Title))
            .Where(field => field.ValueKind is not FieldValueKind.Artwork)
            .ToList();
    }

    /// <summary>
    /// Gets an item's picture.
    /// </summary>
    /// <param name="level">The level's description.</param>
    /// <param name="item">The item.</param>
    /// <returns>The address of the image, or <see langword="null"/> when the item has none.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static Uri? Artwork(LevelPresentation level, ItemDetail item)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(item);

        return Artwork(level, item.Item);
    }

    /// <summary>Gets a published item's picture through its declared level.</summary>
    /// <param name="level">The level's description.</param>
    /// <param name="item">The published item.</param>
    /// <returns>The address of the image, or <see langword="null"/> when the item has none.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static Uri? Artwork(LevelPresentation level, ItemView item)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(item);

        foreach (var field in level.Fields)
        {
            var isArtwork = field.ValueKind is FieldValueKind.Artwork
                || field.Semantics.HasFlag(FieldSemantics.Artwork);

            if (!isArtwork || !item.Fields.TryGetValue(field.FieldId, out var value))
            {
                continue;
            }

            if (value.IsAbsent)
            {
                continue;
            }

            if (value.Address is { } link)
            {
                return link;
            }

            var first = value.Items?.FirstOrDefault(candidate => candidate.Address is not null);
            if (first?.Address is { } firstLink)
            {
                return firstLink;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the conditions an item is currently in.
    /// </summary>
    /// <param name="intent">What the kind declared about itself.</param>
    /// <param name="item">The item.</param>
    /// <returns>The declared states, in the order the extension declared them.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The host publishes the identifiers of the states an item is in; the descriptions of what those
    /// states mean come from the kind's declaration. Joining them here is the only reason this client can
    /// draw a condition it has never heard the name of.
    /// </remarks>
    public static IReadOnlyList<StateDescriptor> States(PluginIntentSurface intent, ItemDetail item)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(item);

        if (item.StateIds.Count == 0)
        {
            return [];
        }

        return intent.States
            .Where(state => item.StateIds.Contains(state.StateId, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Gets how far along an item is, between zero and one.
    /// </summary>
    /// <param name="progress">What the host counted.</param>
    /// <returns>The proportion, or <see langword="null"/> when nothing is wanted beneath the item.</returns>
    public static double? Completion(ProgressSummary? progress)
        => progress is { Want: > 0 } counted
            ? Math.Clamp((double)counted.Have / counted.Want, 0d, 1d)
            : null;

    /// <summary>
    /// Gets the level's description within a kind.
    /// </summary>
    /// <param name="kind">The media kind's description.</param>
    /// <param name="level">The level.</param>
    /// <returns>The level's description, or <see langword="null"/> when the kind has no such level.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public static LevelPresentation? Level(MediaKindDescriptor kind, MediaLevelId level)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return kind.Levels.FirstOrDefault(candidate => candidate.Level == level);
    }

    /// <summary>
    /// Gets the level a kind is entered at: the one nothing else contains.
    /// </summary>
    /// <param name="kind">The media kind's description.</param>
    /// <returns>The root level's description, or <see langword="null"/> when the kind declares no levels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public static LevelPresentation? RootLevel(MediaKindDescriptor kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var root = kind.Shape.Levels.FirstOrDefault(level => level.Parent is null);
        return root is null
            ? (kind.Levels.Count > 0 ? kind.Levels[0] : null)
            : Level(kind, root.Id);
    }

    /// <summary>
    /// Gets the level contained by another, when there is one.
    /// </summary>
    /// <param name="kind">The media kind's description.</param>
    /// <param name="level">The containing level.</param>
    /// <returns>The contained level's description, or <see langword="null"/> when the level is a leaf.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public static LevelPresentation? ChildLevel(MediaKindDescriptor kind, MediaLevelId level)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var child = kind.Shape.Levels.FirstOrDefault(candidate => candidate.Parent == level);
        return child is null ? null : Level(kind, child.Id);
    }

    /// <summary>
    /// Gets the chain of levels from the root down to one level, inclusive.
    /// </summary>
    /// <param name="kind">The media kind's description.</param>
    /// <param name="level">The level the chain ends at.</param>
    /// <returns>The chain, outermost first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<MediaLevelId> Ancestry(MediaKindDescriptor kind, MediaLevelId level)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var chain = new List<MediaLevelId>();
        var current = kind.Shape.Levels.FirstOrDefault(candidate => candidate.Id == level);

        while (current is not null && chain.Count <= kind.Shape.Levels.Count)
        {
            chain.Insert(0, current.Id);
            current = current.Parent is { } parent
                ? kind.Shape.Levels.FirstOrDefault(candidate => candidate.Id == parent)
                : null;
        }

        return chain;
    }

    /// <summary>
    /// Gets a standard platform action, when the level offers it.
    /// </summary>
    /// <param name="level">The level's description.</param>
    /// <param name="standardAction">The operation looked for.</param>
    /// <returns>The action, or <see langword="null"/> when the level declares none with that identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    public static ActionDescriptor? Action(LevelPresentation level, StandardMediaAction? standardAction)
    {
        ArgumentNullException.ThrowIfNull(level);

        return standardAction is null
            ? null
            : level.Actions.FirstOrDefault(action => action.StandardAction == standardAction);
    }

    /// <summary>
    /// Gets whether an action is available for an item, honoring the field the declaration gates it on.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="item">The item, or <see langword="null"/> for an action with no subject.</param>
    /// <returns><see langword="true"/> when the action may be offered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public static bool IsAvailable(ActionDescriptor action, ItemDetail? item)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.EnabledWhenFieldId is not { Length: > 0 } gate)
        {
            return true;
        }

        if (item is null)
        {
            return true;
        }

        return item.Item.Fields.TryGetValue(gate, out var value) && value.Flag == true;
    }
}
