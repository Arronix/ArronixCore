using Arronix.Abstractions.Media;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// One property of an entity, after derivation: the descriptor consumers read, plus what the host needs to
/// read a value back off an instance.
/// </summary>
/// <param name="Compiled">The build-time-generated property projection.</param>
internal sealed record DerivedField(CompiledField Compiled)
{
    internal string PropertyName => Compiled.PropertyName;

    internal Type PropertyType => Compiled.PropertyType;

    internal Func<object, object?> Read => Compiled.Read;

    internal IReadOnlyList<CompiledField> Components => Compiled.Components;

    internal FieldDescriptor Descriptor => Compiled.Descriptor;

    internal FilterOperators FilterOperators => Compiled.FilterOperators;

    internal Type ElementType => Compiled.ElementType;

    internal bool IsNullable => Compiled.IsNullable;

    internal string? Example => Compiled.Example;

    internal bool IsNameable => Compiled.IsNameable;

    internal bool ExplicitIdentity => Compiled.ExplicitIdentity;

    /// <summary>Gets the identifier every cross-reference uses.</summary>
    internal string FieldId => Descriptor.FieldId;

    /// <summary>Gets what the field means to the platform.</summary>
    internal FieldSemantics Semantics => Descriptor.Semantics;

    /// <summary>Gets whether the field carries one of the named semantics.</summary>
    /// <param name="semantics">The semantics tested for.</param>
    /// <returns><see langword="true"/> when any of them is carried.</returns>
    internal bool Carries(FieldSemantics semantics) => (Descriptor.Semantics & semantics) != FieldSemantics.None;
}
