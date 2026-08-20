
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property reporting how far along something is.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ProgressAttribute : Attribute;
