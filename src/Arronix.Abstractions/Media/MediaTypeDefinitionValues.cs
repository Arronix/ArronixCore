using System.Linq.Expressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>How files satisfy the items of a media type.</summary>
public enum FileBindingDefinition
{
    /// <summary>The media type has not declared a file-bearing shape.</summary>
    None = 0,

    /// <summary>Each item owns exactly one file and each file satisfies exactly one item.</summary>
    OnePerItem = 1
}

/// <summary>One use of a format-owned representation family.</summary>
public interface IFormatUse
{
    /// <summary>Dispatches the closed representation type to the host compiler.</summary>
    void Accept(IFormatUseVisitor visitor);
}

/// <summary>The host side of a format-use definition.</summary>
public interface IFormatUseVisitor
{
    /// <summary>Visits one closed format use.</summary>
    void Visit<TRepresentation>(FormatUse<TRepresentation> use)
        where TRepresentation : class, IRepresentation;
}

/// <summary>A media type's use of one format-owned representation family.</summary>
public sealed record FormatUse<TRepresentation>(FormatFamilyDefinition<TRepresentation> Family) : IFormatUse
    where TRepresentation : class, IRepresentation
{
    /// <summary>Gets whether files carry metadata that can be read and written.</summary>
    public bool SupportsEmbeddedMetadata { get; init; }

    /// <summary>Gets whether this family may coexist with another family on one item.</summary>
    public bool CoexistsWithOtherFamilies { get; init; }

    /// <inheritdoc />
    public void Accept(IFormatUseVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }
}

/// <summary>The external identity roles a media type requires and understands.</summary>
public sealed record IdentityDefinition
{
    /// <summary>Gets the roles without which the media type cannot function.</summary>
    public IReadOnlyList<IdentifierRole> RequiredRoles { get; init; } = [];

    /// <summary>Gets additional roles retained when a cataloger supplies them.</summary>
    public IReadOnlyList<IdentifierRole> AdmittedRoles { get; init; } = [];
}

/// <summary>One typed group relationship of a media item.</summary>
public interface IGroupDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Dispatches the closed group type to the host compiler.</summary>
    void Accept(IGroupDefinitionVisitor<TItem> visitor);
}

/// <summary>The host side of a typed group relationship.</summary>
public interface IGroupDefinitionVisitor<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Visits one closed group relationship.</summary>
    void Visit<TGroup>(GroupDefinition<TItem, TGroup> group)
        where TGroup : class, IMediaGroup<TItem>;
}

/// <summary>A plural, typed relationship between items and durable groups.</summary>
public sealed record GroupDefinition<TItem, TGroup>(
    Expression<Func<TItem, IReadOnlyList<TGroup>>> Memberships,
    string SingularName,
    string PluralName) : IGroupDefinition<TItem>
    where TItem : class, IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <summary>Gets whether the group carries monitoring state.</summary>
    public bool IsMonitorable { get; init; }

    /// <summary>Gets whether the group can discover missing members.</summary>
    public bool IsDiscoverySource { get; init; }

    /// <summary>Gets how the group lives relative to its members.</summary>
    public GroupLifetime Lifetime { get; init; } = GroupLifetime.RefCounted;

    /// <inheritdoc />
    public void Accept(IGroupDefinitionVisitor<TItem> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }
}

/// <summary>One typed profile selection belonging to a media type.</summary>
public interface ISelectionDefinition<TItem> : IDeclaredSelection
    where TItem : class, IMediaItem
{
    /// <summary>Dispatches the closed selection value type to the host compiler.</summary>
    void Accept(ISelectionDefinitionVisitor<TItem> visitor);
}

/// <summary>The host side of a typed profile selection.</summary>
public interface ISelectionDefinitionVisitor<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Visits an ordered enumeration selection.</summary>
    void Visit<TValue>(OrderedSelectionDefinition<TItem, TValue> selection)
        where TValue : struct, Enum;

    /// <summary>Visits a numeric threshold selection.</summary>
    void Visit(ThresholdSelectionDefinition<TItem> selection);
}

/// <summary>An inclusive floor over an ordered enumeration carried by an item.</summary>
public sealed record OrderedSelectionDefinition<TItem, TValue>(
    Expression<Func<TItem, TValue>> Property,
    string Name,
    TValue DefaultFloor) : ISelectionDefinition<TItem>
    where TItem : class, IMediaItem
    where TValue : struct, Enum
{
    /// <summary>Gets the stable facet identifier derived from the property by the host.</summary>
    string IDeclaredSelection.FacetId => Property.Body is MemberExpression member
        ? member.Member.Name
        : throw new InvalidOperationException("An ordered selection must address an item property.");

    /// <summary>Gets the offered values. Empty means every member except negative sentinels.</summary>
    public IReadOnlyList<TValue> OfferedValues { get; init; } = Array.AsReadOnly(Enum.GetValues<TValue>());

    /// <summary>Gets the lifecycle stage at which the selection applies.</summary>
    public FacetApplication Application { get; init; } = FacetApplication.Acquisition;

    /// <inheritdoc />
    public void Accept(ISelectionDefinitionVisitor<TItem> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }
}

/// <summary>A numeric profile bound which is not stored on each item.</summary>
public sealed record ThresholdSelectionDefinition<TItem>(
    string FacetId,
    string Name,
    string Unit,
    ThresholdDirection Direction,
    double DefaultBound) : ISelectionDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the lifecycle stage at which the selection applies.</summary>
    public FacetApplication Application { get; init; } = FacetApplication.Acquisition;

    /// <inheritdoc />
    public void Accept(ISelectionDefinitionVisitor<TItem> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }
}

/// <summary>One semantic search a release source may satisfy.</summary>
public sealed record SearchDefinition(
    string Id,
    string Name,
    IReadOnlyList<SearchTerm> RequiredTerms,
    IReadOnlyList<SearchTerm> OptionalTerms);

/// <summary>The complete ordered item/release matching definition.</summary>
public sealed record MatchingDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets title-key layers in semantic precedence order.</summary>
    public IReadOnlyList<MatchKeyLayer<TItem>> Layers { get; init; } = [];

    /// <summary>Gets agreements between release readings and candidate values.</summary>
    public IReadOnlyList<IMatchAgreement<TItem>> Agreements { get; init; } = [];

    /// <summary>Gets whether a caller-supplied scope replaces catalog-wide search.</summary>
    public bool ScopeReplacesSearch { get; init; }

    /// <summary>Gets how residual ambiguity is resolved.</summary>
    public AmbiguityPolicy Ambiguity { get; init; } = AmbiguityPolicy.Reject;
}

/// <summary>One typed recomputation of a property marked as derived.</summary>
public interface IDerivationDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Dispatches the property's closed value type to the host compiler.</summary>
    void Accept(IDerivationDefinitionVisitor<TItem> visitor);
}

/// <summary>The host side of a typed derived-property definition.</summary>
public interface IDerivationDefinitionVisitor<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Visits one derived property while retaining its value type.</summary>
    void Visit<TValue>(DerivationDefinition<TItem, TValue> derivation);
}

/// <summary>A property and the ordinary function which recomputes it.</summary>
public sealed record DerivationDefinition<TItem, TValue>(
    Expression<Func<TItem, TValue>> Property,
    Func<TItem, TValue> Recompute) : IDerivationDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <inheritdoc />
    public void Accept(IDerivationDefinitionVisitor<TItem> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }
}
