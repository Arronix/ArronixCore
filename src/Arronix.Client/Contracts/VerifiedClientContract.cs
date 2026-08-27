using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Contracts;

/// <summary>
/// One client contract this page admitted, and the exact values it was admitted from.
/// </summary>
/// <remarks>
/// Every value was read from the declaration once, during the proof, and kept. The declaration is held
/// privately so that only deserializing and projecting can reach it: reading a member again would be a
/// second answer nothing checked.
/// </remarks>
internal sealed class VerifiedClientContract
{
    private readonly ClientContractEntryPointAttribute _declaration;

    internal VerifiedClientContract(
        ClientContractEntryPointAttribute declaration,
        Type entityType,
        JsonSerializerContext serializationContext,
        JsonTypeInfo entityTypeInfo,
        IReadOnlyList<FieldDescriptor> schema)
    {
        _declaration = declaration;
        EntryPointType = declaration.GetType();
        EntityType = entityType;
        SerializationContext = serializationContext;
        EntityTypeInfo = entityTypeInfo;
        Schema = schema;
    }

    /// <summary>Gets the declaration's own type.</summary>
    internal Type EntryPointType { get; }

    /// <summary>Gets the entity this contract reads, writes and projects.</summary>
    internal Type EntityType { get; }

    /// <summary>Gets the serialization metadata the entity is transported through.</summary>
    internal JsonSerializerContext SerializationContext { get; }

    /// <summary>Gets the entity's own metadata within that context.</summary>
    internal JsonTypeInfo EntityTypeInfo { get; }

    /// <summary>Gets the fields a projection carries, in declaration order. May be empty.</summary>
    internal IReadOnlyList<FieldDescriptor> Schema { get; }

    /// <summary>Reads one serialized entity into the contract's own typed value.</summary>
    internal object Deserialize(ReadOnlySpan<byte> utf8Json) => _declaration.Deserialize(utf8Json);

    /// <summary>Projects one typed value into one-way presentation data.</summary>
    internal ProjectedEntity Project(object entity) => _declaration.Project(entity);
}
