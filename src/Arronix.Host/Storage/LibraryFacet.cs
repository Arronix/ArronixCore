using Arronix.Abstractions.Shape;

// The shape contracts are experimental; the library facet is keyed by one of their identity types.
#pragma warning disable ARX0013

namespace Arronix.Host.Storage;

/// <summary>
/// The user-owned half of an item.
/// </summary>
/// <remarks>
/// <para>
/// The surveyed applications each invented a catalog/library split independently, and each of them
/// discovered the same thing: what a metadata source says about a work and what a user has decided about it
/// are different rows with different lifetimes. Here the split is the runtime topology — the extension
/// projects the catalog and the host stores this, the part the user owns — so the host never has to
/// invent a metadata pipeline it does not have in order to show a library.
/// </para>
/// <para>
/// Path lives here rather than on every level because only the library-entry level has one; a facet for a
/// deeper level simply leaves it null.
/// </para>
/// </remarks>
public sealed record LibraryFacet
{
    /// <summary>
    /// Gets the item this facet belongs to.
    /// </summary>
    public required MediaItemRef Ref { get; init; }

    /// <summary>
    /// Gets the user's answer on each monitoring axis, keyed by dimension identifier.
    /// </summary>
    /// <remarks>
    /// A dictionary rather than a boolean because monitoring is not one axis. One surveyed application needs
    /// three orthogonal ones at once, and collapsing them loses the distinction between "I want this" and
    /// "I want whatever this produces next".
    /// </remarks>
    public IReadOnlyDictionary<string, string> Monitor { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the chosen manifestation, set on the parent of a variant level.
    /// </summary>
    /// <remarks>
    /// The at-most-one invariant is enforced here, in one place, for every media kind. Two of the surveyed
    /// applications hand-roll the same check inside a repository, each for its own entity.
    /// </remarks>
    public MediaItemRef? SelectedVariant { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host may change <see cref="SelectedVariant"/> on its own when a
    /// file arrives or a catalog refresh runs.
    /// </summary>
    public bool AutoSwitchVariant { get; init; } = true;

    /// <summary>
    /// Gets where this item's files live. Library-entry levels only.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets the root folder <see cref="Path"/> sits under.
    /// </summary>
    public string? RootFolderPath { get; init; }

    /// <summary>
    /// Gets the platform tags applied to the item.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets when the user added the item.
    /// </summary>
    public DateTimeOffset? AddedAt { get; init; }
}
