using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Client;

/// <summary>One field of a projected entity: what the field is, and the value this entity carries for it.</summary>
/// <param name="Descriptor">What the field is, exactly as the contract's generated schema declares it.</param>
/// <param name="Value">The value, tagged with the shape <see cref="FieldDescriptor.ValueKind"/> names.</param>
/// <remarks>
/// The descriptor travels with the value: a client holding a projection from an assembly it downloaded a
/// moment ago has nowhere to look one up.
/// </remarks>
public sealed record ProjectedField(FieldDescriptor Descriptor, FieldValue Value);

/// <summary>One typed entity, projected out of a loaded contract for presentation.</summary>
/// <param name="EntityType">The exact CLR type this was projected from.</param>
/// <param name="Fields">Its fields, in the order the generated schema declares them.</param>
/// <remarks>
/// One-way presentation data: nothing here can be turned back into the typed value it came from, which is
/// what keeps it from becoming a second media definition. The type itself rather than its name, because a
/// caller compares it against the entry point it came from. A list rather than a dictionary, because the
/// order a contract declares its fields in is a decision it already made.
/// </remarks>
public sealed record ProjectedEntity(Type EntityType, IReadOnlyList<ProjectedField> Fields);
