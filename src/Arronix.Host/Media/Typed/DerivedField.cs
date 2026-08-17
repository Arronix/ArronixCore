using System.Reflection;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// One property of an entity, after derivation: the descriptor consumers read, plus what the host needs to
/// read a value back off an instance.
/// </summary>
/// <param name="Property">The property the field was derived from.</param>
/// <param name="Descriptor">The descriptor every consumer reads.</param>
/// <param name="FilterOperators">The comparisons the field's type admits.</param>
/// <param name="ElementType">
/// The element type for a multivalued field, or the property's own type otherwise, with
/// <see cref="Nullable{T}"/> unwrapped.
/// </param>
/// <param name="IsNullable">Whether the property can be absent.</param>
/// <param name="Example">The worked example the property's naming token shows, when one was written.</param>
/// <param name="IsNameable">
/// Whether a naming token is derived for the field. False for anything a template could not sensibly
/// interpolate — artwork, references, composites and lists of them.
/// </param>
internal sealed record DerivedField(
    PropertyInfo Property,
    FieldDescriptor Descriptor,
    FilterOperators FilterOperators,
    Type ElementType,
    bool IsNullable,
    string? Example,
    bool IsNameable)
{
    /// <summary>Gets the identifier every cross-reference uses.</summary>
    internal string FieldId => Descriptor.FieldId;

    /// <summary>Gets what the field means to the platform.</summary>
    internal FieldSemantics Semantics => Descriptor.Semantics;

    /// <summary>Gets whether the field carries one of the named semantics.</summary>
    /// <param name="semantics">The semantics tested for.</param>
    /// <returns><see langword="true"/> when any of them is carried.</returns>
    internal bool Carries(FieldSemantics semantics) => (Descriptor.Semantics & semantics) != FieldSemantics.None;
}
