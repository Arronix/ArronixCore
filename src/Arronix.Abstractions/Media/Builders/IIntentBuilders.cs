using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Declares the parts of the intent surface derivation cannot produce.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// Nearly all of a kind's browse axes, orderings, filters and states derive from the item's attributes and
/// the compiler's own knowledge of its types. Only the exceptions are written here — the name of the
/// unpartitioned traversal, an ordering whose useful end is not the one its type implies, a field worth
/// filtering but not worth an axis, and what a state means for the user.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IIntentBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Names the traversal used when the user has expressed no preference.
    /// </summary>
    /// <param name="axisId">The traversal's identifier.</param>
    /// <param name="name">The traversal's display name.</param>
    /// <returns>This builder, for chaining.</returns>
    IIntentBuilder<TItem> DefaultBrowse(string axisId, string name);

    /// <summary>
    /// Overrides the default direction of the ordering derived for a property.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="property">The property ordered by.</param>
    /// <param name="ascending">Whether the useful end is the beginning.</param>
    /// <returns>This builder, for chaining.</returns>
    IIntentBuilder<TItem> Sort<TValue>(Expression<Func<TItem, TValue>> property, bool ascending);

    /// <summary>
    /// Suppresses the browse axis derived for a property, leaving its filter and ordering in place.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="property">The property.</param>
    /// <returns>This builder, for chaining.</returns>
    IIntentBuilder<TItem> Hide<TValue>(Expression<Func<TItem, TValue>> property);

    /// <summary>
    /// Declares what being in one state means for the user.
    /// </summary>
    /// <typeparam name="TEnum">The status enumeration.</typeparam>
    /// <param name="member">The state.</param>
    /// <param name="tone">What it means.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Tone is the one part of a state a consumer cannot derive: without it, knowing whether a state is
    /// good news would mean recognizing a particular kind's state names, which is compile-time knowledge of
    /// a media kind.
    /// </remarks>
    IIntentBuilder<TItem> StateTone<TEnum>(TEnum member, StateTone tone)
        where TEnum : struct, Enum;
}

/// <summary>
/// Declares the things a user may ask the platform to do with the kind.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// Actions stay declared: the host executes them and the plugin has no code path into one. What the typed
/// surface changes is that a condition is a predicate over the item rather than a two-state field that has
/// to exist for the condition to be statable.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IActionBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares an action.
    /// </summary>
    /// <param name="actionId">The identifier the action is invoked by.</param>
    /// <param name="name">The display name, phrased as an instruction.</param>
    /// <param name="consequence">How much it costs and how far it can be undone.</param>
    /// <param name="scope">What it operates on.</param>
    /// <returns>The action builder, which also continues this one.</returns>
    IActionStepBuilder<TItem> Add(string actionId, string name, Consequence consequence, ActionScope scope);

    /// <summary>
    /// Declares an action over the groups on one axis.
    /// </summary>
    /// <typeparam name="TGroup">The group type.</typeparam>
    /// <param name="actionId">The identifier the action is invoked by.</param>
    /// <param name="name">The display name, phrased as an instruction.</param>
    /// <param name="consequence">How much it costs and how far it can be undone.</param>
    /// <returns>The action builder, which also continues this one.</returns>
    /// <remarks>
    /// A grouping axis is neither a level nor a kind, so before a group was a type an action over
    /// collections had to be filed under the whole kind as the least wrong answer available.
    /// </remarks>
    IActionStepBuilder<TItem> AddForGroup<TGroup>(string actionId, string name, Consequence consequence)
        where TGroup : class, IMediaGroup<TItem>;
}

/// <summary>
/// Refines the action last declared.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IActionStepBuilder<TItem> : IActionBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares that the action outlives the request that starts it, so the caller is told it was accepted
    /// rather than that it finished.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IActionStepBuilder<TItem> LongRunning();

    /// <summary>
    /// Declares that the user must affirm the consequence once, and what they are affirming.
    /// </summary>
    /// <param name="consequenceStatement">A plain-language statement of what the action will do.</param>
    /// <returns>This builder, for chaining.</returns>
    IActionStepBuilder<TItem> Acknowledge(string consequenceStatement);

    /// <summary>
    /// Declares that the user must reproduce a value proving they read what they are about to do.
    /// </summary>
    /// <param name="consequenceStatement">A plain-language statement of what the action will do.</param>
    /// <returns>This builder, for chaining.</returns>
    IActionStepBuilder<TItem> TypeToConfirm(string consequenceStatement);

    /// <summary>
    /// Declares when the action is available, as a predicate over the subject.
    /// </summary>
    /// <param name="predicate">The condition.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The closed defect: a condition that had to name a two-state field could not say "has a value" about
    /// a field that is not two-state, which is the condition most often actually wanted.
    /// </remarks>
    IActionStepBuilder<TItem> EnabledWhen(Expression<Func<TItem, bool>> predicate);

    /// <summary>
    /// Declares a two-state parameter.
    /// </summary>
    /// <param name="parameterId">The key the value is supplied under.</param>
    /// <param name="name">The parameter's display name.</param>
    /// <param name="defaultValue">The value used when the caller supplies none.</param>
    /// <param name="required">Whether the action can run without it.</param>
    /// <returns>This builder, for chaining.</returns>
    IActionStepBuilder<TItem> Parameter(
        string parameterId,
        string name,
        bool defaultValue = false,
        bool required = false);

    /// <summary>
    /// Declares a parameter carrying an external identifier in one role.
    /// </summary>
    /// <param name="parameterId">The key the value is supplied under.</param>
    /// <param name="name">The parameter's display name.</param>
    /// <param name="role">The identifier role the value fills.</param>
    /// <param name="required">Whether the action can run without it.</param>
    /// <returns>This builder, for chaining.</returns>
    IActionStepBuilder<TItem> Parameter(
        string parameterId,
        string name,
        IdentifierRole role,
        bool required = false);

    /// <summary>
    /// Declares a parameter whose choices are an enumeration's members.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration.</typeparam>
    /// <param name="parameterId">The key the value is supplied under.</param>
    /// <param name="name">The parameter's display name.</param>
    /// <param name="defaultValue">The member used when the caller supplies none.</param>
    /// <returns>This builder, for chaining.</returns>
    IActionStepBuilder<TItem> Parameter<TEnum>(string parameterId, string name, TEnum defaultValue)
        where TEnum : struct, Enum;

    /// <summary>
    /// Declares a parameter that is a selection policy already declared on the kind.
    /// </summary>
    /// <param name="selection">The policy.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Naming the policy is what carries its choices, their order and its threshold comparison into the
    /// parameter, instead of flattening a threshold into a list of alternatives.
    /// </remarks>
    IActionStepBuilder<TItem> Parameter(IDeclaredSelection selection);
}

/// <summary>
/// Declares a working surface whose rows are a type.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <typeparam name="TRow">The row type, which is the column set.</typeparam>
/// <remarks>
/// A column list and the proposal that fills it used to have to agree by convention — the same identifier
/// written twice, in two places, checked by nothing. The row type is the column set, so they cannot
/// disagree.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IWorkbenchBuilder<TItem, TRow>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares what the surface operates on, which is what tells a consumer where to offer it.
    /// </summary>
    /// <param name="subject">The subject.</param>
    /// <returns>This builder, for chaining.</returns>
    IWorkbenchBuilder<TItem, TRow> Subject(WorkbenchSubject subject);

    /// <summary>
    /// Declares a value a consumer must collect before asking for a proposal.
    /// </summary>
    /// <param name="inputId">The key the value is supplied under.</param>
    /// <param name="name">The input's display name.</param>
    /// <returns>This builder, for chaining.</returns>
    IWorkbenchBuilder<TItem, TRow> Input(string inputId, string name);

    /// <summary>
    /// Declares an input carrying an external identifier in one role.
    /// </summary>
    /// <param name="inputId">The key the value is supplied under.</param>
    /// <param name="name">The input's display name.</param>
    /// <param name="role">The identifier role the value fills.</param>
    /// <returns>This builder, for chaining.</returns>
    IWorkbenchBuilder<TItem, TRow> Input(string inputId, string name, IdentifierRole role);

    /// <summary>
    /// Declares the commit.
    /// </summary>
    /// <param name="label">The commit's name, phrased as an instruction.</param>
    /// <param name="consequence">How much committing costs and how far it can be undone.</param>
    /// <returns>The type builder, for chaining.</returns>
    IMediaTypeBuilder<TItem> Commit(string label, Consequence consequence);
}
