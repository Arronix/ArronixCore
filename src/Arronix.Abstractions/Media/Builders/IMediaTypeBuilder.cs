using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Arronix.Abstractions.Definition;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Records what a media kind's item attributes cannot say: facts that relate two things, and facts about
/// the type as a whole.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// <para>
/// The rule dividing this from the attribute vocabulary: <b>an attribute states a fact about one property
/// in isolation; the builder states a fact that relates two or more things, or that is about the type as a
/// whole.</b> "This property is the title" is intrinsic to the property. "Files bind one to one to items"
/// relates the item to the file model. "Minimum availability is a threshold over this property" relates a
/// property to a selection policy.
/// </para>
/// <para>
/// The rule has one consequence worth stating on its own: <b>an attribute never takes an identifier
/// string.</b> If a declaration has to name something else by identifier it is relating two things, so it
/// belongs here — where the reference is an expression the compiler checks and a rename refactors, rather
/// than a string nothing validates until load.
/// </para>
/// <para>
/// Everything a kind does not have costs nothing to say, because it is simply not called. No coordinate
/// spaces, no sequence axes, no variant axis, no span constraints: not calling a method is how a kind says
/// it has none, which is what removes the declared-and-empty and declared-as-the-default rows a data
/// surface accumulates.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaTypeBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Names the kind for one item and for several.
    /// </summary>
    /// <param name="singular">The name for one item.</param>
    /// <param name="plural">The name for several.</param>
    /// <returns>This builder, for chaining.</returns>
    IMediaTypeBuilder<TItem> Named(string singular, string plural);

    /// <summary>
    /// Gets the builder declaring how files bind to items.
    /// </summary>
    IFileBindingBuilder<TItem> Files { get; }

    /// <summary>
    /// Declares a format family and its quality ladder.
    /// </summary>
    /// <param name="familyId">The family's identifier.</param>
    /// <param name="name">The family's display name.</param>
    /// <returns>The family builder.</returns>
    IFormatFamilyBuilder<TItem> Format(string familyId, string name);

    /// <summary>
    /// Declares the external-identity roles the kind requires and admits, against the property that
    /// carries them.
    /// </summary>
    /// <param name="property">The item's external-identifier property.</param>
    /// <returns>The identity builder.</returns>
    IIdentityBuilder<TItem> Identity(Expression<Func<TItem, ExternalIdSet>> property);

    /// <summary>
    /// Declares a collection that cuts across the kind's items, against the property that refers to it.
    /// </summary>
    /// <typeparam name="TGroup">The group type.</typeparam>
    /// <param name="property">The item's reference to its group.</param>
    /// <returns>The group builder.</returns>
    /// <remarks>
    /// The axis's arity, its member position and whether it has a designated primary member all
    /// <i>derive</i> from the property being a single, optional reference — there is nothing to declare
    /// and nothing that can contradict the type.
    /// </remarks>
    IGroupBuilder<TItem, TGroup> Group<TGroup>(Expression<Func<TItem, TGroup?>> property)
        where TGroup : class, IMediaGroup<TItem>;

    /// <summary>
    /// Declares a selection policy that is a threshold over an ordered enumeration the item carries.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration.</typeparam>
    /// <param name="property">The item property the threshold is measured on.</param>
    /// <returns>The selection builder, which is also the handle an action parameter refers to.</returns>
    /// <remarks>
    /// Calling this twice for the same property returns the same builder rather than declaring a second
    /// facet, so an action that offers the policy as a parameter can simply name it.
    /// </remarks>
    IOrderedSelectionBuilder<TItem, TEnum> Selection<TEnum>(Expression<Func<TItem, TEnum>> property)
        where TEnum : struct, Enum;

    /// <summary>
    /// Declares a selection policy that has no backing property.
    /// </summary>
    /// <param name="facetId">The facet's identifier.</param>
    /// <param name="name">The facet's display name.</param>
    /// <returns>The selection builder.</returns>
    /// <remarks>
    /// The one place the "every reference is a property reference" claim does not hold, and it is declared
    /// by identifier for an honest reason: a policy that is per-profile rather than per-item has no
    /// property to point an expression at.
    /// </remarks>
    IThresholdSelectionBuilder Selection(string facetId, string name);

    /// <summary>
    /// Declares one way the kind can be searched for.
    /// </summary>
    /// <param name="searchKindId">The search's identifier.</param>
    /// <param name="name">The search's display name.</param>
    /// <returns>The search builder.</returns>
    ISearchBuilder<TItem> Search(string searchKindId, string name);

    /// <summary>
    /// Gets the builder declaring how parsed readings resolve to items.
    /// </summary>
    IMatchBuilder<TItem> Matching { get; }

    /// <summary>
    /// Gets the builder declaring the query tiers and alias spellings.
    /// </summary>
    IQueryBuilder<TItem> Querying { get; }

    /// <summary>
    /// Gets the builder declaring the default templates, the folder spine and the template rules.
    /// </summary>
    INamingBuilder<TItem> Naming { get; }

    /// <summary>
    /// Gets the builder declaring how an item is summarized.
    /// </summary>
    ISummaryBuilder<TItem> Summary { get; }

    /// <summary>
    /// Gets the builder declaring the parts of the intent surface derivation cannot produce.
    /// </summary>
    IIntentBuilder<TItem> Intent { get; }

    /// <summary>
    /// Gets the builder declaring the kind's actions.
    /// </summary>
    IActionBuilder<TItem> Actions { get; }

    /// <summary>
    /// Gets the builder declaring quality evaluation beyond the ladder.
    /// </summary>
    IQualityBuilder<TItem> Quality { get; }

    /// <summary>
    /// Declares a working surface over a typed row.
    /// </summary>
    /// <typeparam name="TRow">The row type, which <i>is</i> the column set.</typeparam>
    /// <param name="workbenchId">The surface's identifier.</param>
    /// <param name="name">The surface's display name.</param>
    /// <returns>The workbench builder.</returns>
    IWorkbenchBuilder<TItem, TRow> Workbench<TRow>(string workbenchId, string name);

    /// <summary>
    /// Binds the recomputation of a property marked <see cref="DerivedAttribute"/>.
    /// </summary>
    /// <typeparam name="TValue">The property's type.</typeparam>
    /// <param name="property">The derived property.</param>
    /// <param name="recompute">How its value is recomputed from the rest of the item.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Deliberately one input, one output. A kind needing a recomputation pass over several properties at
    /// once can be given one later without the attribute changing shape.
    /// </remarks>
    IMediaTypeBuilder<TItem> Derives<TValue>(
        Expression<Func<TItem, TValue>> property,
        Func<TItem, TValue> recompute);

    /// <summary>
    /// Carries the release models, which stay regular expressions and stay data.
    /// </summary>
    /// <param name="parsing">The parse declaration.</param>
    /// <returns>The parsing builder.</returns>
    IParsingBuilder<TItem> Parsing(ParseDeclaration parsing);

    /// <summary>
    /// Carries the catalog mapping, until catalogers become plugins of their own.
    /// </summary>
    /// <param name="catalog">The catalog declaration.</param>
    /// <returns>This builder, for chaining.</returns>
    IMediaTypeBuilder<TItem> Catalog(CatalogDeclaration catalog);

    /// <summary>
    /// Carries the parity corpus. Evidence is data.
    /// </summary>
    /// <param name="cases">The corpus cases.</param>
    /// <returns>This builder, for chaining.</returns>
    IMediaTypeBuilder<TItem> Corpus(IReadOnlyList<CorpusCase> cases);
}

/// <summary>
/// A selection policy already declared on a builder, as an action parameter refers to it.
/// </summary>
/// <remarks>
/// Naming the policy is what carries its choices, their order and the "at least this far along"
/// comparison into the parameter. A parameter carrying a flat list of choices instead would render four
/// alternatives where the domain has a threshold.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IDeclaredSelection
{
    /// <summary>
    /// Gets the declared facet's identifier.
    /// </summary>
    string FacetId { get; }
}
