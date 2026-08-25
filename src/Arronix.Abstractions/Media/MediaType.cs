using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Releases;

namespace Arronix.Abstractions.Media;

/// <summary>The typed definition base for one media kind.</summary>
/// <typeparam name="TItem">The kind's durable catalog/library item type.</typeparam>
/// <typeparam name="TTarget">The kind's ephemeral acquisition-target type.</typeparam>
/// <typeparam name="TRelease">The kind's interpreted release type.</typeparam>
/// <typeparam name="TParser">The statically dispatched parser that produces that release type.</typeparam>
/// <param name="kind">The stable identifier used by manifests, registrations, and item references.</param>
/// <param name="singularName">The display name for one item.</param>
/// <param name="pluralName">The display name for several items.</param>
/// <param name="formats">The non-empty set of format-owned representation families this kind composes.</param>
/// <param name="availability">The kind's minimum-availability selection.</param>
/// <param name="files">How files satisfy this media type's items.</param>
/// <remarks>
/// The definition is an ordinary object with constructor-owned invariants and overridable media-specific
/// members. Registration captures one definition together with its closed generic types; Host reads it
/// before deriving a kind-blind runtime projection. The projection is discovery data, never a second schema.
/// </remarks>
public abstract class MediaType<TItem, TTarget, TRelease, TParser>(
    MediaKindId kind,
    string singularName,
    string pluralName,
    IReadOnlyList<IFormatUse> formats,
    ISelectionDefinition<TItem> availability,
    FileBindingDefinition files = FileBindingDefinition.OnePerItem) : IMediaTypeDefinition
    where TItem : class, IMediaItem
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease
    where TParser : IReleaseParser<TRelease>
{
    /// <summary>Gets the build-time-generated projection used by the registration bridge.</summary>
    /// <remarks>The source generator supplies this member. Media authors neither implement nor call it.</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public abstract CompiledShapeCatalog CompiledShapes { get; }

    /// <summary>Gets the media kind identifier. It must equal the manifest's declared kind.</summary>
    public MediaKindId Kind { get; } = kind;

    /// <summary>Gets the name for one item.</summary>
    public string SingularName { get; } = singularName;

    /// <summary>Gets the name for several items.</summary>
    public string PluralName { get; } = pluralName;

    /// <summary>Gets how files satisfy this media type's items.</summary>
    public FileBindingDefinition Files { get; } = files;

    /// <summary>Gets the format-owned representation families this media type composes.</summary>
    public IReadOnlyList<IFormatUse> Formats { get; } = RequireFormats(formats);

    /// <summary>Gets the external identity roles this media type requires and understands.</summary>
    public virtual IdentityDefinition Identity { get; } = new();

    /// <summary>Gets the durable grouping relationships its items may participate in.</summary>
    public virtual IReadOnlyList<IGroupDefinition<TItem>> Groups => [];

    /// <summary>Gets the media type's minimum-availability selection.</summary>
    public ISelectionDefinition<TItem> Availability { get; } =
        availability ?? throw new ArgumentNullException(nameof(availability));

    /// <summary>Gets additional profile selections specific to this media type.</summary>
    public virtual IReadOnlyList<ISelectionDefinition<TItem>> AdditionalSelections => [];

    /// <summary>Gets the semantic searches a release source may satisfy.</summary>
    public virtual IReadOnlyList<SearchDefinition> Searches => [];

    /// <summary>Gets the ordered item/release matching definition.</summary>
    public virtual MatchingDefinition<TItem> Matching { get; } = new();

    /// <summary>Gets the media-owned defaults and preferences over interpreted releases.</summary>
    public virtual ReleasePolicy<TRelease>? ReleasePolicy => null;

    /// <summary>Gets the typed source-query plan.</summary>
    public virtual QueryDefinition<TItem> Querying { get; } = new();

    /// <summary>Gets the typed naming choices not derivable from the item shape.</summary>
    public virtual NamingDefinition<TItem> Naming { get; } = new();

    /// <summary>Gets the typed item and group summary definitions.</summary>
    public virtual SummaryDefinition<TItem> Summary { get; } = new();

    /// <summary>Gets typed exceptions to the intent surface derived from item attributes.</summary>
    public virtual IntentDefinition<TItem> Intent { get; } = new();

    /// <summary>Gets typed decision workbenches whose row types are their schemas.</summary>
    public virtual IReadOnlyList<IWorkbenchDefinition<TItem>> Workbenches => [];

    /// <summary>Gets typed recomputations for properties marked as derived.</summary>
    public virtual IReadOnlyList<IDerivationDefinition<TItem>> Derivations => [];

    /// <inheritdoc />
    IMediaTypeRegistration IMediaTypeDefinition.Capture() => MediaTypeRegistration.For(this, CompiledShapes);

    private static IReadOnlyList<IFormatUse> RequireFormats(IReadOnlyList<IFormatUse> value)
    {
        ArgumentNullException.ThrowIfNull(value, "formats");
        if (value.Count == 0)
        {
            throw new ArgumentException(
                "A media type must compose at least one format family.",
                "formats");
        }

        return [.. value];
    }
}
