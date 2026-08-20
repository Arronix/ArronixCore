
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks the property reporting the condition an entity is in.
/// </summary>
/// <remarks>
/// At most one property per entity, and it must be an enumeration — analyzer-enforced. One state is derived
/// per enum member, so the states, their identifiers and their order come from the enum rather than from a
/// hand-written table that has to be kept in step with it. Where the members carry a meaningful order, the
/// enum <i>is</i> the order, and a threshold over it is an ordinary comparison rather than a lookup into a
/// rank function no consumer can read.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class StatusAttribute : Attribute;
