
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property that tells apart two entities which would otherwise share a title.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DisambiguationAttribute : Attribute;
