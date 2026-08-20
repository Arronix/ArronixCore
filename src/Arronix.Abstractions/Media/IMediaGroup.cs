
namespace Arronix.Abstractions.Media;

/// <summary>
/// A collection that cuts across a kind's items rather than containing them: monitorable, possibly
/// outliving its members, and carrying metadata of its own.
/// </summary>
/// <typeparam name="TMember">The item type whose instances belong to a group.</typeparam>
/// <remarks>
/// <para>
/// This is what closes the defect the descriptor recorded against itself. A grouping axis could declare
/// that a group exists, that it is monitorable, that it outlives its members and that it has metadata of
/// its own — and then had no way to say what that metadata <i>is</i>, so a front end could render an item
/// generically and could not render a group at all.
/// </para>
/// <para>
/// A group is a type, so its fields derive exactly as an item's do, from the same attributes and the same
/// rules. Nothing about group metadata needs a second vocabulary.
/// </para>
/// </remarks>
public interface IMediaGroup<TMember> : IMediaEntity
    where TMember : IMediaItem;
