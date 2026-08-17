using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// How a lookup key is multiplied into the spellings a release might use.
/// </summary>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum KeyExpansion
{
    /// <summary>The key is looked up as derived.</summary>
    None = 0,

    /// <summary>Roman numerals and their decimal spellings are treated as the same key.</summary>
    RomanNumerals = 1
}

/// <summary>
/// What an absent statement on the release side means for an agreement rule.
/// </summary>
/// <remarks>
/// The distinction is the whole value of the rule. A missing statement is common and harmless; a
/// contradicting one is neither, and collapsing the two loses the only case worth defending against.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum Agreement
{
    /// <summary>An absent statement fails the rule.</summary>
    Reject = 0,

    /// <summary>An absent statement satisfies the rule.</summary>
    Accept = 1
}

/// <summary>
/// A fact a parsed release states about itself, which a candidate item can be held to agree with.
/// </summary>
/// <remarks>
/// A closed, host-owned vocabulary rather than an expression over a reading type, because the reading is
/// the host's own and a lambda over it would buy nothing an enumeration member does not.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ReadingFact
{
    /// <summary>The year stated alongside the title in the release text.</summary>
    TitleYear = 0
}

/// <summary>
/// Declares how parsed readings resolve to the kind's items.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMatchBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares one ordered key layer of the entry-resolution cascade.
    /// </summary>
    /// <param name="layerId">The layer's identifier, for diagnostics.</param>
    /// <param name="keys">The spellings this layer looks an item up by.</param>
    /// <param name="expansion">How the derived key is multiplied into accepted variants.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The layering is the algorithm and declared order is semantic: a flat lookup lets an alternative
    /// spelling of one item outrank the actual title of another.
    /// </remarks>
    IMatchBuilder<TItem> Layer(
        string layerId,
        Expression<Func<TItem, IEnumerable<string?>>> keys,
        KeyExpansion expansion = KeyExpansion.None);

    /// <summary>
    /// Declares that a fact the release states must agree with something the candidate item carries.
    /// </summary>
    /// <typeparam name="TValue">The compared value's type.</typeparam>
    /// <param name="reading">The fact the release states.</param>
    /// <param name="candidates">The item-side values any one of which satisfies the rule.</param>
    /// <param name="whenAbsent">What an absent statement means.</param>
    /// <param name="floor">The least value at which the statement is a statement rather than noise.</param>
    /// <returns>This builder, for chaining.</returns>
    IMatchBuilder<TItem> Agrees<TValue>(
        ReadingFact reading,
        Expression<Func<TItem, IEnumerable<TValue?>>> candidates,
        Agreement whenAbsent = Agreement.Reject,
        double? floor = null)
        where TValue : struct;

    /// <summary>
    /// Declares that a caller-supplied scope replaces the catalog-wide search, so text disagreeing with
    /// the scoped item is a rejection rather than a match against something else.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IMatchBuilder<TItem> ScopeReplacesSearch();

    /// <summary>
    /// Declares that more than one surviving candidate is a rejection naming the contenders.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IMatchBuilder<TItem> RejectAmbiguity();

    /// <summary>
    /// Declares that more than one surviving candidate is settled by year evidence, and a residual tie is
    /// rejected.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IMatchBuilder<TItem> TiebreakAmbiguityByYear();
}

/// <summary>
/// Declares the query tiers and alias spellings an acquisition turns into.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQueryBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares one tier of the query plan. Declared order is the plan's order.
    /// </summary>
    /// <param name="tierId">The tier's identifier.</param>
    /// <param name="searchKindId">The search kind the tier implements.</param>
    /// <returns>The tier builder, which also continues this one.</returns>
    IQueryTierBuilder<TItem> Tier(string tierId, string searchKindId);

    /// <summary>
    /// Declares one row of alias spellings of the search subject, most canonical first.
    /// </summary>
    /// <param name="aliasId">The row's identifier.</param>
    /// <param name="spellings">The spellings this row contributes.</param>
    /// <param name="configure">Optional refinements to how the row is used.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueryBuilder<TItem> Alias(
        string aliasId,
        Expression<Func<TItem, IEnumerable<string?>>> spellings,
        Action<IAliasOptions>? configure = null);
}

/// <summary>
/// Declares one tier of a query plan.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// Extends the query builder so that a plan reads as one chain: a tier's refinements, then the next tier,
/// then the alias rows.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQueryTierBuilder<TItem> : IQueryBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares that the tier cannot plan without an identifier in this role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// A role rather than a scheme, so identifier search works with whichever cataloger is installed
    /// rather than only with the one the kind happened to be written against.
    /// </remarks>
    IQueryTierBuilder<TItem> RequiresIdentity(IdentifierRole role);

    /// <summary>
    /// Declares that the tier cannot plan unless a property has a value.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="property">The property that must have a value.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The rule that keeps a bare title from becoming the worst query a kind can make.
    /// </remarks>
    IQueryTierBuilder<TItem> Requires<TValue>(Expression<Func<TItem, TValue>> property);

    /// <summary>
    /// Declares a structured argument carrying an external identifier in one role.
    /// </summary>
    /// <param name="term">The kind of argument.</param>
    /// <param name="role">The identifier role the value is taken from.</param>
    /// <param name="omitWhenAbsent">Whether the argument is dropped, rather than the tier failing.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> Argument(SearchTerm term, IdentifierRole role, bool omitWhenAbsent = false);

    /// <summary>
    /// Declares a structured argument taken from a property.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="term">The kind of argument.</param>
    /// <param name="property">The property the value is taken from.</param>
    /// <param name="omitWhenAbsent">Whether the argument is dropped, rather than the tier failing.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> Argument<TValue>(
        SearchTerm term,
        Expression<Func<TItem, TValue>> property,
        bool omitWhenAbsent = false);

    /// <summary>
    /// Declares the tier's free-text query.
    /// </summary>
    /// <param name="text">The text, as an expression over the item.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> FreeText(Expression<Func<TItem, string?>> text);

    /// <summary>
    /// Declares that the tier names nothing and its categories are the whole of its gate.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> NoTerms();

    /// <summary>
    /// Declares the search origins the tier applies to. Omitting this applies it to every origin.
    /// </summary>
    /// <param name="origins">The origins.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> Origins(params SearchOrigin[] origins);

    /// <summary>
    /// Declares that one query is planned per alias spelling, rather than one query carrying many.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> FanOutPerAlias();

    /// <summary>
    /// Declares that planned queries carry the alias spellings for sources that use them.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IQueryTierBuilder<TItem> CarryAliases();
}

/// <summary>
/// Refinements to how one row of alias spellings is used.
/// </summary>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IAliasOptions
{
    /// <summary>
    /// Declares that spellings from this row are emitted only for languages the acquisition accepts,
    /// which is what makes translated-spelling fan-out affordable.
    /// </summary>
    /// <returns>These options, for chaining.</returns>
    IAliasOptions FilterByAcceptedLanguages();

    /// <summary>
    /// Declares that spellings from this row ride along as aliases only and never become a query of their
    /// own.
    /// </summary>
    /// <returns>These options, for chaining.</returns>
    IAliasOptions NeverOwnQuery();
}

/// <summary>
/// Declares quality evaluation beyond the ladder.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares source groups that have no resolution axis at all, so a stated resolution is a claim about
    /// the recording equipment rather than about the work.
    /// </summary>
    /// <param name="sourceGroups">The source groups.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityBuilder<TItem> IgnoreStatedResolutionFor(params string[] sourceGroups);

    /// <summary>
    /// Declares that evidence landing between rungs rounds up, so an unrecognized source is never treated
    /// as the worst one.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IQualityBuilder<TItem> FallbackRoundUp();

    /// <summary>
    /// Declares that evidence landing between rungs takes the nearest rung in either direction.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IQualityBuilder<TItem> FallbackNearest();
}

/// <summary>
/// Carries the release models and the code they escape to.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IParsingBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Binds how a dotted run in a release title is respaced.
    /// </summary>
    /// <param name="respace">The rewrite, given the dotted run.</param>
    /// <returns>The type builder, for chaining.</returns>
    /// <remarks>
    /// What used to be a host-owned named strategy, a role identifier, a parameter dictionary, a
    /// requirement row, a host vocabulary entry and a load-time resolution rule — for a rewrite that is
    /// three lines of code. A strategy is a method.
    /// </remarks>
    IMediaTypeBuilder<TItem> Respace(Func<string, string> respace);
}
