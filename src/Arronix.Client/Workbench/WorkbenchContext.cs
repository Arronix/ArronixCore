#pragma warning disable ARX0016 // Intent contracts are experimental; working surfaces are declared by them.
#pragma warning disable ARX0017 // Wire contracts are experimental; the kind description is one.

using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Wire;
using Microsoft.AspNetCore.Components;

namespace Arronix.Client.Workbench;

/// <summary>
/// Everything the component driving a working surface needs.
/// </summary>
/// <remarks>
/// The same arrangement as a browse presenter, and for the same reason: the component is chosen at run
/// time from what the surface operates on, so it needs a parameter shape that does not depend on which
/// component was chosen.
/// </remarks>
public sealed class WorkbenchContext
{
    /// <summary>
    /// Gets the media kind the surface belongs to.
    /// </summary>
    public required MediaKindDescriptor Kind { get; init; }

    /// <summary>
    /// Gets what the surface declared about itself.
    /// </summary>
    public required WorkbenchDescriptor Descriptor { get; init; }

    /// <summary>
    /// Gets the decisions the extension proposed, as the user has left them.
    /// </summary>
    public required IReadOnlyList<WorkbenchRow> Rows { get; init; }

    /// <summary>
    /// Gets the rows the user has taken out of the commit.
    /// </summary>
    public required IReadOnlyCollection<string> ExcludedRowIds { get; init; }

    /// <summary>
    /// Gets the callback raised when a row's value has been changed.
    /// </summary>
    public required EventCallback<WorkbenchRow> OnRowChanged { get; init; }

    /// <summary>
    /// Gets the callback raised when a row is taken out of, or put back into, the commit.
    /// </summary>
    public required EventCallback<string> OnRowToggled { get; init; }
}
