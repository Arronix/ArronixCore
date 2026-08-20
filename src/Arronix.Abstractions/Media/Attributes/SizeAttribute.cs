
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a whole-number property that reports a size in bytes.
/// </summary>
/// <remarks>
/// A bare whole number is an integer, and a consumer that renders an integer renders digits. Saying it is a
/// size is what lets every consumer format it for its reader without the kind choosing a unit.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SizeAttribute : Attribute;
