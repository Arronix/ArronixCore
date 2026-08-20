
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property that is not a field of the entity at all.
/// </summary>
/// <remarks>
/// The escape for a helper an entity happens to expose. It is opt-out rather than opt-in because an entity
/// whose fields had to be enumerated one by one would drift from the type the first time somebody added a
/// property, which is the drift the typed model exists to remove.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IgnoreAttribute : Attribute;
