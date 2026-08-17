#pragma warning disable ARX0017 // Wire contracts are experimental; affordances are what this table maps.

using Arronix.Abstractions.Wire;

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
/// <para>
/// <b>A gap, stated rather than papered over.</b> An ability names no action, and an action names no
/// ability, so nothing in the published contracts connects the two. Each row below therefore carries the
/// identifier this client <i>expects</i> the corresponding action to be published under, and a control is
/// only ever offered when the level actually declares an action with that identifier — an ability with no
/// matching action is silently not offered rather than offered and broken. That convention is this
/// client's, and it is the one place where this project holds an expectation about a string an extension
/// chooses. The proper fix belongs in the contract layer and is reported alongside this work.
/// </para>
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
        Affordance.Monitorable => new AffordanceBinding(AffordanceControl.MonitorSwitch, "monitor.set", "Monitoring"),
        Affordance.Searchable => new AffordanceBinding(AffordanceControl.Command, "search", "Search"),
        Affordance.Refreshable => new AffordanceBinding(AffordanceControl.Command, "refresh", "Refresh"),
        Affordance.Renamable => new AffordanceBinding(AffordanceControl.Command, "rename", "Rename files"),
        Affordance.Removable => new AffordanceBinding(AffordanceControl.Command, "remove", "Remove"),
        Affordance.Browsable => new AffordanceBinding(AffordanceControl.Navigation, "", "Open"),
        Affordance.Selectable => new AffordanceBinding(AffordanceControl.VariantChooser, "variant.select", "Version"),
        Affordance.Taggable => new AffordanceBinding(AffordanceControl.Command, "tags.set", "Tags"),
        Affordance.Relocatable => new AffordanceBinding(AffordanceControl.Command, "relocate", "Move files"),
        Affordance.Downloadable => new AffordanceBinding(AffordanceControl.Command, "grab", "Get"),
    };
}

/// <summary>
/// What this client offers for one derived ability.
/// </summary>
/// <param name="Control">The kind of control drawn.</param>
/// <param name="ConventionalActionId">
/// The action identifier this client looks for. Empty when the control invokes nothing.
/// </param>
/// <param name="Label">What the control is called.</param>
public sealed record AffordanceBinding(AffordanceControl Control, string ConventionalActionId, string Label);

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
