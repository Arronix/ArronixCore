
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property a user may change.
/// </summary>
/// <remarks>
/// Not editable is the default, because most of an entity's properties come from a catalog and writing over
/// them is lost at the next refresh. Applying this together with <see cref="DerivedAttribute"/> is a
/// contradiction and is analyzer-enforced.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class EditableAttribute : Attribute;
