using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Declares how one of the kind's items is summarized for a destination outside the platform.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// What is deliberately not here: a deep link, an outbound catalog address, and the rows that read host
/// state rather than item state. A summary names the <i>item</i> and the host resolves a link for whichever
/// surface is asking, which is the only form that works for a command line as well as for a browser; an
/// address at a catalog belongs to whoever owns the identifier; and quality, total size and languages are
/// facts the host holds for every kind and supplies for every kind.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface ISummaryBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares the headline.
    /// </summary>
    /// <param name="headline">The headline, as an expression over the item.</param>
    /// <param name="maxLength">The greatest length a destination is assumed to accept.</param>
    /// <returns>This builder, for chaining.</returns>
    ISummaryBuilder<TItem> Headline(Expression<Func<TItem, string?>> headline, int maxLength = 256);

    /// <summary>
    /// Declares the body text.
    /// </summary>
    /// <param name="body">The body, as an expression over the item.</param>
    /// <param name="maxLength">The greatest length a destination is assumed to accept.</param>
    /// <returns>This builder, for chaining.</returns>
    ISummaryBuilder<TItem> Body(Expression<Func<TItem, string?>> body, int maxLength = 300);

    /// <summary>
    /// Declares one labelled row of the summary.
    /// </summary>
    /// <param name="label">The row's label.</param>
    /// <param name="value">The value, as an expression over the item.</param>
    /// <param name="weight">Whether the row is carried wherever the summary is, or only where there is room.</param>
    /// <returns>This builder, for chaining.</returns>
    ISummaryBuilder<TItem> Field(
        string label,
        Expression<Func<TItem, object?>> value,
        SummaryFieldWeight weight = SummaryFieldWeight.Secondary);

    /// <summary>
    /// Declares how a group the item belongs to is summarized.
    /// </summary>
    /// <typeparam name="TGroup">The group type.</typeparam>
    /// <param name="property">The item's reference to its group.</param>
    /// <param name="configure">The group's summary.</param>
    /// <returns>This builder, for chaining.</returns>
    ISummaryBuilder<TItem> Group<TGroup>(
        Expression<Func<TItem, TGroup?>> property,
        Action<IGroupSummaryBuilder<TItem, TGroup>> configure)
        where TGroup : class, IMediaGroup<TItem>;
}

/// <summary>
/// Declares how a group on one axis is summarized.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <typeparam name="TGroup">The group type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IGroupSummaryBuilder<TItem, TGroup>
    where TItem : IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <summary>
    /// Declares the group's headline.
    /// </summary>
    /// <param name="headline">The headline, as an expression over the group.</param>
    /// <returns>This builder, for chaining.</returns>
    IGroupSummaryBuilder<TItem, TGroup> Headline(Expression<Func<TGroup, string?>> headline);

    /// <summary>
    /// Declares one labelled row of the group's summary.
    /// </summary>
    /// <param name="label">The row's label.</param>
    /// <param name="value">The value, as an expression over the group.</param>
    /// <param name="weight">Whether the row is carried wherever the summary is, or only where there is room.</param>
    /// <returns>This builder, for chaining.</returns>
    IGroupSummaryBuilder<TItem, TGroup> Field(
        string label,
        Expression<Func<TGroup, object?>> value,
        SummaryFieldWeight weight = SummaryFieldWeight.Secondary);
}
