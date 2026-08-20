
namespace Arronix.Abstractions.Media;

/// <summary>
/// States the unit a property's value is expressed in, for presentation only.
/// </summary>
/// <remarks>
/// Rarely needed, and that is the point: a property typed as an elapsed time or a size already carries its
/// unit in its type, and a property that needs this is usually one that should have had a better type.
/// </remarks>
/// <param name="unit">The unit, as a reader would see it.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UnitAttribute(string unit) : Attribute
{
    /// <summary>
    /// Gets the unit the value is expressed in.
    /// </summary>
    public string Unit { get; } = unit;
}
