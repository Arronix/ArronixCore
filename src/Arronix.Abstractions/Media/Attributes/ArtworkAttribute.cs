
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property holding images that represent the entity.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ArtworkAttribute : Attribute;
