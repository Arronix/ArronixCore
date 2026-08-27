using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Client;

/// <summary>One field of a projected entity: what the field is, and the value this entity carries for it.</summary>
/// <param name="Descriptor">What the field is, exactly as the contract's generated schema declares it.</param>
/// <param name="Value">The value, tagged with the shape <see cref="FieldDescriptor.ValueKind"/> names.</param>
/// <remarks>
/// The descriptor travels with the value rather than being looked up by identifier. A client holding a
/// projection produced by an assembly it downloaded a moment ago has nowhere to look the descriptor up.
/// </remarks>
public sealed record ProjectedField(FieldDescriptor Descriptor, FieldValue Value);

/// <summary>One typed entity, projected out of a loaded contract for presentation.</summary>
/// <param name="EntityType">The exact CLR type this was projected from.</param>
/// <param name="Fields">Its fields, in the order the generated schema declares them.</param>
/// <remarks>
/// <para>
/// One-way presentation data and nothing else. This is what a typed entity looks like after the assembly
/// that owns it has read it; it is never a way to author, construct or transmit one. Nothing here can be
/// turned back into the typed value it came from, which is the property that keeps it from becoming a
/// second media definition.
/// </para>
/// <para>
/// The type itself, not its name. A consumer that wants a name asks the type for one; carrying both would
/// be one fact spelled twice, and the type is what a caller compares against the entry point it came from.
/// </para>
/// <para>
/// Deliberately not a dictionary keyed by field identifier. The order a contract declares its fields in is
/// the order it means them to be read, and a consumer reconstructing that order from a prominence rank is
/// reconstructing a decision the contract already made.
/// </para>
/// </remarks>
public sealed record ProjectedEntity(Type EntityType, IReadOnlyList<ProjectedField> Fields);
