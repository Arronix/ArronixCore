#pragma warning disable ARX0016 // Intent contracts are experimental; resolving them is this file's job.

using Arronix.Abstractions.Intent;
using Arronix.Client.Browse.Presenters;
using Arronix.Client.Workbench;

namespace Arronix.Client.Rendering;

/// <summary>
/// The index of every mapping from a declared intent to something this client draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the boundary.</b> An extension declares that a media kind can be traversed along a
/// sequence; nothing in that declaration knows what a sequence looks like. The decision that a sequence
/// is drawn by one component and a cross-cutting collection by another is made here and nowhere else, so
/// a front end built on a different technology replaces this file and keeps everything else — the
/// contracts, the server, the extensions — untouched.
/// </para>
/// <para>
/// Five tables make up the whole coupling. Two of them are here because they resolve a component; the
/// other three are one file each because they resolve a style, a prompt and a control rather than a
/// component. Adding a member to any of the five vocabularies is a build failure until every table
/// answers for it, which is exactly the property that makes a claim of kind-agnosticism checkable rather
/// than merely asserted.
/// </para>
/// <list type="table">
///   <listheader><term>Table</term><description>Maps</description></listheader>
///   <item><term><see cref="PresenterFor"/></term><description>A traversal to the component that draws it.</description></item>
///   <item><term><see cref="WorkbenchFor"/></term><description>A working surface's subject to the component that drives it.</description></item>
///   <item><term><see cref="ToneMap"/></term><description>A state's valence to a style class and a text marker.</description></item>
///   <item><term><see cref="ConsequenceMap"/></term><description>A cost and a certainty requirement to how the user is asked.</description></item>
///   <item><term><see cref="AffordanceMap"/></term><description>A derived ability to the control offered for it.</description></item>
/// </list>
/// <para>
/// A sixth mapping — a field's value shape to how the value is shown and edited — lives in the two
/// components that do the showing and the editing, because in that one case the mapping <i>is</i> the
/// markup and extracting it would leave a table of names pointing at nothing.
/// </para>
/// <para>
/// What deliberately does not exist: any way for an extension to name a component. That would put the
/// choice on the wrong side of the boundary, and it is the reason this file is a switch over a closed
/// vocabulary rather than a dictionary keyed by a string.
/// </para>
/// </remarks>
public static class IntentResolver
{
    /// <summary>
    /// Gets the component that draws one kind of traversal.
    /// </summary>
    /// <param name="kind">The kind of traversal.</param>
    /// <returns>The component type.</returns>
    public static Type PresenterFor(BrowseAxisKind kind) => kind switch
    {
        BrowseAxisKind.Hierarchy => typeof(HierarchyPresenter),
        BrowseAxisKind.Sequence => typeof(SequencePresenter),
        BrowseAxisKind.Grouping => typeof(GroupingPresenter),
        BrowseAxisKind.Facet => typeof(FacetPresenter),
        BrowseAxisKind.Flat => typeof(FlatPresenter),
    };

    /// <summary>
    /// Gets the component that drives a working surface over one kind of subject.
    /// </summary>
    /// <param name="subject">What the surface operates on.</param>
    /// <returns>The component type.</returns>
    /// <remarks>
    /// All three subjects resolve to the same grid today, which is the evidence that the working surface
    /// really is one primitive rather than three workflows wearing one name. The table exists anyway, so
    /// that the day one of them earns its own component the change is one line here.
    /// </remarks>
    public static Type WorkbenchFor(WorkbenchSubject subject) => subject switch
    {
        WorkbenchSubject.LooseFiles => typeof(WorkbenchGrid),
        WorkbenchSubject.ReleaseCandidates => typeof(WorkbenchGrid),
        WorkbenchSubject.LibraryItems => typeof(WorkbenchGrid),
    };
}
