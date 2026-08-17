using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Arronix.Abstractions.Media;

/// <summary>
/// A fact about the file being named that is host-owned rather than a property of any media kind.
/// </summary>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum FileFact
{
    /// <summary>The release name the file was acquired under.</summary>
    SceneName = 0,

    /// <summary>The file's name as it arrived, extension excluded.</summary>
    OriginalFileName = 1
}

/// <summary>
/// What a user's template does and does not mention, as a template rule sees it.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// This is what turns a validity rule that is genuinely a disjunction with an exclusivity between its
/// branches into ordinary code. A per-token "is required" flag can express a conjunction and nothing else,
/// so a kind whose rule is not a conjunction had no way to state it.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface INamingTemplateFacts<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Reports whether the template mentions the token derived from a property.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> when the template mentions it.</returns>
    bool Has<TValue>(Expression<Func<TItem, TValue>> property);

    /// <summary>
    /// Reports whether the template mentions the token for a host-owned file fact.
    /// </summary>
    /// <param name="fact">The fact.</param>
    /// <returns><see langword="true"/> when the template mentions it.</returns>
    bool Has(FileFact fact);
}

/// <summary>
/// Declares the naming data derivation cannot know: the default templates a kind ships, the folder spine
/// they are assembled into, and the rules a user's own template must satisfy.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// Templates still contain a token grammar, and that is correct: a user types these. What changes is that
/// every token in one is derived from a property and checked against the derived token set at build time,
/// rather than resolved against a hand-maintained list of constants.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface INamingBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares the default file-name template.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> File(string template);

    /// <summary>
    /// Declares the default item-folder template.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> Folder(string template);

    /// <summary>
    /// Declares the default folder template for groups on one axis.
    /// </summary>
    /// <typeparam name="TGroup">The group type.</typeparam>
    /// <param name="template">The template.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> GroupFolder<TGroup>(string template)
        where TGroup : class, IMediaGroup<TItem>;

    /// <summary>
    /// Declares the folder spine: the fixed skeleton of segments a library path is assembled from, with
    /// optional segments bracketed.
    /// </summary>
    /// <param name="spine">The spine.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> Spine(string spine);

    /// <summary>
    /// Declares that the spine's group segment is inserted when the user has asked to group by an axis and
    /// the item belongs to a group on it.
    /// </summary>
    /// <typeparam name="TGroup">The group type.</typeparam>
    /// <param name="ruleId">The rule's identifier, for diagnostics.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Both halves of the old condition are now implied rather than written. Whether the user asked is a
    /// host-owned option about the axis, not a fact about the kind; whether the item has a group is
    /// already knowable from the axis being a single, optional reference.
    /// </remarks>
    INamingBuilder<TItem> WhenGroupingBy<TGroup>(string ruleId)
        where TGroup : class, IMediaGroup<TItem>;

    /// <summary>
    /// Declares a rule a user's file template must satisfy before it is saved.
    /// </summary>
    /// <param name="ruleId">The rule's identifier, for diagnostics.</param>
    /// <param name="requirement">The sentence shown when a template fails the rule.</param>
    /// <param name="isSatisfied">The rule.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> RequireInFileTemplate(
        string ruleId,
        string requirement,
        Func<INamingTemplateFacts<TItem>, bool> isSatisfied);

    /// <summary>
    /// Declares where a property's token takes its value from when the property itself is empty.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="property">The property whose token needs a fallback.</param>
    /// <param name="order">The file facts tried in order; the first with a value wins.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> Fallback<TValue>(
        Expression<Func<TItem, TValue>> property,
        params FileFact[] order);

    /// <summary>
    /// Declares what a name falls back to when the whole template renders to nothing.
    /// </summary>
    /// <param name="fact">The file fact used instead.</param>
    /// <returns>This builder, for chaining.</returns>
    INamingBuilder<TItem> FallbackForEmptyResult(FileFact fact);
}
