#pragma warning disable ARX0013 // Shape contracts are experimental; levels and items are described by them.
#pragma warning disable ARX0016 // Intent contracts are experimental; traversals are declared by them.
#pragma warning disable ARX0017 // Wire contracts are experimental; published items are them.

using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Microsoft.AspNetCore.Components;

namespace Arronix.Client.Browse;

/// <summary>
/// Everything a presenter needs, and deliberately nothing more.
/// </summary>
/// <remarks>
/// <para>
/// One parameter shape for all five presenters, because the component that draws a traversal is chosen at
/// run time from the traversal's declared kind and a run-time choice needs a common surface to hand its
/// argument through.
/// </para>
/// <para>
/// A presenter is given the items and the descriptions; it never fetches. Fetching depends on the
/// traversal in ways paging and filtering make fiddly, and a presenter that could fetch would be a
/// presenter that could fetch differently from its four siblings.
/// </para>
/// </remarks>
public sealed class PresenterContext
{
    /// <summary>
    /// Gets the media kind being browsed.
    /// </summary>
    public required MediaKindDescriptor Kind { get; init; }

    /// <summary>
    /// Gets the level the page nominally holds. Individual items name their own level, which is what a
    /// presenter should read when the two can differ.
    /// </summary>
    public required LevelPresentation Level { get; init; }

    /// <summary>
    /// Gets the traversal being drawn.
    /// </summary>
    public required BrowseAxis Axis { get; init; }

    /// <summary>
    /// Gets the items on the current page.
    /// </summary>
    public required IReadOnlyList<ItemDetail> Items { get; init; }

    /// <summary>
    /// Gets the item whose contents are being shown, when the traversal has descended into one.
    /// </summary>
    public MediaItemRef? Parent { get; init; }

    /// <summary>
    /// Gets the callback raised when the user opens an item.
    /// </summary>
    public required EventCallback<ItemDetail> OnOpen { get; init; }

    /// <summary>
    /// Gets the callback raised when something has changed and the page should be read again.
    /// </summary>
    public required EventCallback OnChanged { get; init; }
}
