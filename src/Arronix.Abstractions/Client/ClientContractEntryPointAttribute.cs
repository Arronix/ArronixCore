using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Client;

/// <summary>
/// The declared way a client-safe assembly says what it holds, without a client enumerating it.
/// </summary>
/// <remarks>
/// <para>
/// A browser that has loaded a contract assembly knows nothing about what is inside it, and enumerating
/// its types would make the client untrimmable and turn property reflection into a second media schema.
/// The contract declares what a client may read instead.
/// </para>
/// <para>
/// Every fact a decision is made on is a <b>constructor argument</b>, so it lives in the custom attribute
/// blob: readable from the received bytes before the runtime is handed them, and by a host that holds the
/// assembly without calling into it. A value behind an overridden property is executable code.
/// </para>
/// <para>
/// <see cref="Deserialize"/> and <see cref="Project"/> are separate because one bytes-to-fields call proves
/// nothing about whether a typed value existed. The class is abstract so the runtime, not a client,
/// constructs the generated implementation.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage(
    "Design",
    "CA1813:Avoid unsealed attributes",
    Justification = "The declaration read from metadata is this base type; each generated implementation is sealed.")]
public abstract class ClientContractEntryPointAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClientContractEntryPointAttribute"/> class.
    /// </summary>
    /// <param name="entityType">The exact CLR type this entry point reads and constructs.</param>
    /// <param name="generatedMetadataHash">
    /// The SHA-256, upper-case hexadecimal, over the member graph the generated reader accepts.
    /// </param>
    /// <param name="projectionSchemaHash">
    /// The SHA-256, upper-case hexadecimal, over the field schema the generated projection produces.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A hash is <see langword="null"/>, empty or white space.</exception>
    protected ClientContractEntryPointAttribute(
        Type entityType,
        string generatedMetadataHash,
        string projectionSchemaHash)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedMetadataHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionSchemaHash);

        EntityType = entityType;
        GeneratedMetadataHash = generatedMetadataHash;
        ProjectionSchemaHash = projectionSchemaHash;
    }

    /// <summary>
    /// Gets the SHA-256, upper-case hexadecimal, over the member graph the generated reader accepts.
    /// </summary>
    /// <remarks>Two builds that would read one payload differently differ here, whatever their version says.</remarks>
    public string GeneratedMetadataHash { get; }

    /// <summary>
    /// Gets the SHA-256, upper-case hexadecimal, over the field schema the generated projection produces.
    /// </summary>
    /// <remarks>Covers <see cref="Schema"/> exactly, in declaration order.</remarks>
    public string ProjectionSchemaHash { get; }

    /// <summary>Gets the exact CLR type this entry point reads and constructs.</summary>
    /// <remarks>
    /// A constructor argument, so the blob carries the reference itself: a metadata reader decodes it before
    /// anything loads and the runtime resolves it after. Any display name is taken from here, after load.
    /// </remarks>
    public Type EntityType { get; }

    /// <summary>Gets the source-generated serialization metadata this entry point reads through.</summary>
    /// <remarks>The actual context, so a consumer asks it what the wire shape is.</remarks>
    public abstract JsonSerializerContext SerializationContext { get; }

    /// <summary>Gets the metadata for <see cref="EntityType"/> itself, the root of that graph.</summary>
    public abstract JsonTypeInfo EntityTypeInfo { get; }

    /// <summary>Gets what a projection from this entry point contains, in declaration order.</summary>
    /// <remarks>Available before any payload is.</remarks>
    public abstract IReadOnlyList<FieldDescriptor> Schema { get; }

    /// <summary>
    /// Reads one serialized entity into this assembly's own typed value.
    /// </summary>
    /// <param name="utf8Json">The serialized entity, as UTF-8 bytes.</param>
    /// <returns>The constructed value, whose type is <see cref="EntityType"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The payload is not a readable entity.</exception>
    /// <remarks>
    /// <see cref="object"/> because a client cannot name the type. It can ask the value what it is, and the
    /// answer carries the assembly it was constructed in.
    /// </remarks>
    public abstract object Deserialize(ReadOnlySpan<byte> utf8Json);

    /// <summary>
    /// Serializes one typed value of this entry point's entity type into exactly the bytes
    /// <see cref="Deserialize"/> accepts.
    /// </summary>
    /// <param name="entity">A value of <see cref="EntityType"/>.</param>
    /// <returns>The serialized entity, as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is not of <see cref="EntityType"/>.</exception>
    /// <remarks>The other end of the same metadata, so a holder of a typed value writes what this reads.</remarks>
    public abstract byte[] Serialize(object entity);

    /// <summary>
    /// Projects one typed value of this entry point's entity type into one-way presentation data.
    /// </summary>
    /// <param name="entity">A value of <see cref="EntityType"/>.</param>
    /// <returns>The projection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="entity"/> is not of <see cref="EntityType"/>.</exception>
    public abstract ProjectedEntity Project(object entity);
}
