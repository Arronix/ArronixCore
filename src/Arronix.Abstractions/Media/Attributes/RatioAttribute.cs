
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a fractional property whose value is a proportion, where one means whole.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RatioAttribute : Attribute;
