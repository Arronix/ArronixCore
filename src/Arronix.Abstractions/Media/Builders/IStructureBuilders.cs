using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Declares how files relate to the items they satisfy.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IFileBindingBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares the degenerate corner of the item-file-ordinal join: the item is its own acquisition unit
    /// and its own file bearer, both uniqueness constraints hold, and the ordinal is meaningless.
    /// </summary>
    /// <returns>The type builder, for chaining.</returns>
    /// <remarks>
    /// Sugar over the general anchor-and-unit form a kind whose file rows hang above the items they
    /// satisfy will need. That general form is not built yet, on purpose: it is the same machinery a
    /// sequenced kind needs for its acquisition-unit and file-unit split, and guessing its shape from the
    /// one degenerate case is how it comes out wrong.
    /// </remarks>
    IMediaTypeBuilder<TItem> OnePerItem();
}

/// <summary>
/// Declares one family of file formats and the quality model that reads its files.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IFormatFamilyBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares the file extensions that identify the family, leading dot included.
    /// </summary>
    /// <param name="extensions">The extensions. Disjoint across the families of one kind.</param>
    /// <returns>This builder, for chaining.</returns>
    IFormatFamilyBuilder<TItem> Extensions(params string[] extensions);

    /// <summary>
    /// Declares the family's quality model.
    /// </summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TType">The type declaring it.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// One call, where an ordered ladder was a hand-written table of every source crossed with every
    /// resolution. A quality model reads evidence onto typed axes and has no opinion at all about which of
    /// two files is better — that question belongs to the user's policy, and separating the two is the
    /// whole point of the model this replaces.
    /// </remarks>
    IFormatFamilyBuilder<TItem> Quality<TFacts, TType>()
        where TFacts : IQualityFacts
        where TType : IQualityType<TFacts>;

    /// <summary>
    /// Declares this media kind's own contribution to the family's axes.
    /// </summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TRefinement">The kind's refinement.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Optional. A kind whose releases carry no naming dialect of its own declares nothing and gets the
    /// family's reading unchanged. A refinement may only turn absence into presence or raise a reading's
    /// provenance, so a kind's heuristic can complete a reading and can never overwrite a measurement.
    /// </remarks>
    IFormatFamilyBuilder<TItem> RefinedBy<TFacts, TRefinement>()
        where TFacts : IQualityFacts
        where TRefinement : IQualityRefinement<TFacts>;

    /// <summary>
    /// Declares a per-file, kind-meaningful fact files of this family may carry.
    /// </summary>
    /// <param name="facetId">The facet's identifier.</param>
    /// <param name="name">The facet's display name.</param>
    /// <param name="titleCase">How rendered values are cased.</param>
    /// <param name="exceptions">Words rendered exactly as listed.</param>
    /// <param name="ordinalSuffixesLowerCase">Whether ordinal suffixes stay lower case under title casing.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The one declaration on the structural surface that is still identifier-keyed rather than typed, and
    /// honestly so: the fact belongs to a <i>file</i> rather than to an item, so the item type has no
    /// property to point at, and it belongs to one kind rather than to the platform, so the host-global
    /// probe vocabulary cannot own it either. The typed answer is a per-kind file-facts entity, deferred
    /// until a kind with a genuine file-unit split forces its shape.
    /// </remarks>
    IFormatFamilyBuilder<TItem> Facet(
        string facetId,
        string name,
        TechnicalFacetCase titleCase,
        IReadOnlyList<string>? exceptions = null,
        bool ordinalSuffixesLowerCase = false);

    /// <summary>
    /// Declares that files of this family carry embedded metadata that can be read and written.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IFormatFamilyBuilder<TItem> SupportsEmbeddedMetadata();

    /// <summary>
    /// Declares that an item may hold files of this family and of another at the same time, rather than
    /// the families being alternatives.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IFormatFamilyBuilder<TItem> CoexistsWithOtherFamilies();
}

/// <summary>
/// Declares which external-identifier roles a kind needs, without naming a catalog.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IIdentityBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares that the kind cannot function without an identifier in this role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>This builder, for chaining.</returns>
    IIdentityBuilder<TItem> Requires(IdentifierRole role);

    /// <summary>
    /// Declares that the kind will carry an identifier in this role when a cataloger supplies one, and
    /// does without otherwise.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>This builder, for chaining.</returns>
    IIdentityBuilder<TItem> Admits(IdentifierRole role);

    /// <summary>
    /// Declares that the assigning catalog may retire an identifier and answer it with a redirect.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Recorded as misplaced rather than hidden: whether a catalog merges duplicates and redirects is a
    /// fact about that catalog, and it moves to the cataloger's own declaration once catalogers are
    /// plugins of their own.
    /// </remarks>
    IIdentityBuilder<TItem> SupportsRedirects();
}

/// <summary>
/// Declares the four facts about a grouping axis that its member property cannot imply.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <typeparam name="TGroup">The group type.</typeparam>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IGroupBuilder<TItem, TGroup>
    where TItem : IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <summary>
    /// Names the axis for one group and for several.
    /// </summary>
    /// <param name="singular">The name for one group.</param>
    /// <param name="plural">The name for several.</param>
    /// <returns>This builder, for chaining.</returns>
    IGroupBuilder<TItem, TGroup> Named(string singular, string plural);

    /// <summary>
    /// Declares that a group carries monitoring and profile state of its own.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IGroupBuilder<TItem, TGroup> Monitorable();

    /// <summary>
    /// Declares that a group can act as a source of items to add.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IGroupBuilder<TItem, TGroup> DiscoverySource();

    /// <summary>
    /// Declares that a group survives its last member leaving the library. Reference-counted otherwise.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IGroupBuilder<TItem, TGroup> Independent();
}

/// <summary>
/// Declares a selection policy that is a threshold over an ordered enumeration.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <typeparam name="TEnum">The enumeration the threshold is measured on.</typeparam>
/// <remarks>
/// The enumeration <i>is</i> the order, so the fact that choosing a value means it and everything after it
/// is derived rather than declared — and a consumer that would otherwise render four independent choices
/// where the domain has a threshold has what it needs to render the threshold.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IOrderedSelectionBuilder<TItem, TEnum> : IDeclaredSelection
    where TItem : IMediaItem
    where TEnum : struct, Enum
{
    /// <summary>
    /// Names the policy.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <returns>This builder, for chaining.</returns>
    IOrderedSelectionBuilder<TItem, TEnum> Named(string name);

    /// <summary>
    /// Declares the floor applied when the user expresses no preference.
    /// </summary>
    /// <param name="defaultFloor">The least value allowed by default.</param>
    /// <returns>This builder, for chaining.</returns>
    IOrderedSelectionBuilder<TItem, TEnum> AtLeast(TEnum defaultFloor);

    /// <summary>
    /// Declares which members the policy offers, in enumeration order.
    /// </summary>
    /// <param name="choices">The members offered. Omitting this offers every member.</param>
    /// <returns>This builder, for chaining.</returns>
    IOrderedSelectionBuilder<TItem, TEnum> Offering(params TEnum[] choices);

    /// <summary>
    /// Declares when the policy takes effect. Defaults to gating acquisition.
    /// </summary>
    /// <param name="application">When the filter applies.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The default is gating acquisition rather than materialization, and that follows from the policy
    /// being measured on a property of the item: an item whose property the threshold reads necessarily
    /// exists, so the only thing left to refuse is the grab.
    /// </remarks>
    IOrderedSelectionBuilder<TItem, TEnum> Gates(FacetApplication application);
}

/// <summary>
/// Declares a selection policy that is a numeric bound with no backing property.
/// </summary>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IThresholdSelectionBuilder : IDeclaredSelection
{
    /// <summary>
    /// Declares that the bound is expressed in days.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    IThresholdSelectionBuilder Days();

    /// <summary>
    /// Declares the unit the bound is expressed in, for presentation only.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <returns>This builder, for chaining.</returns>
    IThresholdSelectionBuilder InUnit(string unit);

    /// <summary>
    /// Declares that values at or above the bound are kept, and the bound applied by default.
    /// </summary>
    /// <param name="defaultBound">The default bound.</param>
    /// <returns>This builder, for chaining.</returns>
    IThresholdSelectionBuilder AtLeast(double defaultBound);

    /// <summary>
    /// Declares that values at or below the bound are kept, and the bound applied by default.
    /// </summary>
    /// <param name="defaultBound">The default bound.</param>
    /// <returns>This builder, for chaining.</returns>
    IThresholdSelectionBuilder AtMost(double defaultBound);

    /// <summary>
    /// Declares when the policy takes effect. Defaults to gating acquisition.
    /// </summary>
    /// <param name="application">When the filter applies.</param>
    /// <returns>This builder, for chaining.</returns>
    IThresholdSelectionBuilder Gates(FacetApplication application);
}

/// <summary>
/// Declares one way a kind can be searched for.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// A kind declares what it needs to be able to ask; a release source declares what it can be asked.
/// Eligibility is the intersection, computed by the host, and neither side names the other's vocabulary.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface ISearchBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Declares the terms a source must support to be eligible for this search at all.
    /// </summary>
    /// <param name="terms">The required terms.</param>
    /// <returns>This builder, for chaining.</returns>
    ISearchBuilder<TItem> Requires(params SearchTerm[] terms);

    /// <summary>
    /// Declares the terms the search uses when a source supports them, and omits otherwise.
    /// </summary>
    /// <param name="terms">The optional terms.</param>
    /// <returns>This builder, for chaining.</returns>
    ISearchBuilder<TItem> Admits(params SearchTerm[] terms);

    /// <summary>
    /// Declares the categories results are expected in.
    /// </summary>
    /// <param name="categories">The category numbers.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Recorded rather than fixed: a release-category taxonomy is release-source protocol knowledge, and a
    /// media kind spelling one is the same shape of coupling as a media kind spelling a catalog's name,
    /// one layer down. Moving it needs a source-side category map, which is not this iteration.
    /// </remarks>
    ISearchBuilder<TItem> Categories(params int[] categories);
}
