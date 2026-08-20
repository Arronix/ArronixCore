
using Arronix.Abstractions.Wire;
using Arronix.Abstractions.Intent;

namespace Arronix.Client.Rendering;

/// <summary>
/// Turns an ability the host derived into the control this client offers for it.
/// </summary>
/// <remarks>
/// <para>
/// The host derives what can be done with an item from the validated shape; this table decides what the
/// user sees as a result. Three abilities become controls this client draws itself, because each one is
/// rendered from a declaration the shape already carries — the monitor dimensions, the variant axis, the
/// child level. The remaining seven become entries in the item's command list.
/// </para>
/// The binding uses the platform operation enum. Its wire identifier belongs to the host projection and
/// never becomes a convention privately repeated by the client.
/// </remarks>
public static class AffordanceMap
{
    /// <summary>
    /// Gets the control this client offers for an ability.
    /// </summary>
    /// <param name="affordance">The ability.</param>
    /// <returns>The control and the action identifier it invokes.</returns>
    public static AffordanceBinding For(Affordance affordance) => affordance switch
    {
        Affordance.Monitorable => new(AffordanceControl.MonitorSwitch, StandardMediaAction.SetMonitoring, "Monitoring"),
        Affordance.Searchable => new(AffordanceControl.Command, StandardMediaAction.Search, "Search"),
        Affordance.Refreshable => new(AffordanceControl.Command, StandardMediaAction.Refresh, "Refresh"),
        Affordance.Renamable => new(AffordanceControl.Command, StandardMediaAction.Rename, "Rename files"),
        Affordance.Removable => new(AffordanceControl.Command, StandardMediaAction.Remove, "Remove"),
        Affordance.Browsable => new(AffordanceControl.Navigation, null, "Open"),
        Affordance.Selectable => new(AffordanceControl.VariantChooser, StandardMediaAction.SelectVariant, "Version"),
        Affordance.Taggable => new(AffordanceControl.Command, StandardMediaAction.SetTags, "Tags"),
        Affordance.Relocatable => new(AffordanceControl.Command, StandardMediaAction.Relocate, "Move files"),
        Affordance.Downloadable => new(AffordanceControl.Command, StandardMediaAction.Grab, "Get"),
    };
}

/// <summary>
/// What this client offers for one derived ability.
/// </summary>
/// <param name="Control">The kind of control drawn.</param>
/// <param name="StandardAction">The platform operation invoked, or null when the control invokes nothing.</param>
/// <param name="Label">What the control is called.</param>
public sealed record AffordanceBinding(
    AffordanceControl Control,
    StandardMediaAction? StandardAction,
    string Label);

/// <summary>
/// The kinds of control this client draws for a derived ability.
/// </summary>
public enum AffordanceControl
{
    /// <summary>An entry in the item's command list.</summary>
    Command = 0,

    /// <summary>A control over the level's declared monitor dimensions.</summary>
    MonitorSwitch = 1,

    /// <summary>A control over the item's competing manifestations.</summary>
    VariantChooser = 2,

    /// <summary>A way into what the item contains.</summary>
    Navigation = 3
}
