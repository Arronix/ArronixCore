using System.ComponentModel;
using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>A field projection emitted from a CLR property during compilation.</summary>
/// <remarks>
/// This is an in-process plugin-to-host bridge, not a wire descriptor or an authoring schema. The getter is
/// generated against the closed property type, so renaming or changing that property fails the plugin build
/// instead of becoming a runtime reflection error.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CompiledField
{
    /// <summary>Gets the CLR property name used by typed cross-references.</summary>
    public required string PropertyName { get; init; }

    /// <summary>Gets the property's declared CLR type.</summary>
    public required Type PropertyType { get; init; }

    /// <summary>Gets the field descriptor projected to generic consumers.</summary>
    public required FieldDescriptor Descriptor { get; init; }

    /// <summary>Gets the comparisons admitted by the property's compiled value shape.</summary>
    public required FilterOperators FilterOperators { get; init; }

    /// <summary>Gets the list element or scalar value type with nullable wrappers removed.</summary>
    public required Type ElementType { get; init; }

    /// <summary>Gets whether the scalar value may be absent.</summary>
    public bool IsNullable { get; init; }

    /// <summary>Gets the example used by naming-token documentation.</summary>
    public string? Example { get; init; }

    /// <summary>Gets whether a naming template may interpolate this field.</summary>
    public bool IsNameable { get; init; }

    /// <summary>Gets whether the author explicitly marked this property as an identity.</summary>
    public bool ExplicitIdentity { get; init; }

    /// <summary>Gets the generated value reader.</summary>
    public required Func<object, object?> Read { get; init; }

    /// <summary>Gets generated readers for a composite value's components.</summary>
    public IReadOnlyList<CompiledField> Components { get; init; } = [];
}

/// <summary>The compile-time projection of one entity or workbench-row type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CompiledEntityShape
{
    /// <summary>Gets the exact CLR type projected by this shape.</summary>
    public required Type EntityType { get; init; }

    /// <summary>Gets its fields in declaration order.</summary>
    public required IReadOnlyList<CompiledField> Fields { get; init; }
}

/// <summary>The generated shapes reachable from one media type definition.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledShapeCatalog
{
    private readonly IReadOnlyDictionary<Type, CompiledEntityShape> _byType;

    /// <summary>Initializes a generated shape catalog.</summary>
    public CompiledShapeCatalog(CompiledEntityShape item, IReadOnlyList<CompiledEntityShape> related)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(related);

        Item = item;
        _byType = related
            .Prepend(item)
            .ToDictionary(static shape => shape.EntityType);
    }

    /// <summary>Gets the media type's item shape.</summary>
    public CompiledEntityShape Item { get; }

    /// <summary>Gets the compiled shape for an item, group, or workbench row.</summary>
    public CompiledEntityShape Get(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _byType.TryGetValue(type, out var shape)
            ? shape
            : throw new ArgumentException(
                $"No compile-time field projection was generated for '{type.FullName}'.",
                nameof(type));
    }
}
