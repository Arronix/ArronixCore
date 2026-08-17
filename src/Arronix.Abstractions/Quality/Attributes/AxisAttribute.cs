using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>Declares that a property of a quality-facts type is a quality axis.</summary>
/// <remarks>
/// The axis's <i>form</i> derives from the CLR type and this attribute's <see cref="Ordering"/>; its
/// identity derives from the property name; its prose comes from <c>[Display]</c>, which is already the
/// vocabulary's single source of prose. Nothing here says where the axis sits in anyone's preference —
/// that relates the axis to other axes and is therefore policy, not an attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class AxisAttribute : Attribute
{
    /// <summary>Gets how the axis's values relate to one another.</summary>
    public AxisOrdering Ordering { get; init; } = AxisOrdering.Ascending;

    /// <summary>Gets the unit a quantity is expressed in, for presentation only.</summary>
    public string? Unit { get; init; }
}
