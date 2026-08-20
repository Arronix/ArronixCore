
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks the property that carries the entity's host-minted identity.
/// </summary>
/// <remarks>
/// Exactly one property per entity, and its type must be the platform's item identifier. Both rules are
/// analyzer-enforced rather than expressed in the type system, because the alternative — a base class
/// carrying the identity — would put a host-owned member on a plugin-owned type.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IdentityAttribute : Attribute;
