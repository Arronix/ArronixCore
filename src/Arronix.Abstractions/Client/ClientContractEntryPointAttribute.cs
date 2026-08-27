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
/// A browser that has verified and loaded a contract assembly holds no compile-time knowledge of anything
/// inside it. The two ways out of that are opposites. It can enumerate the assembly's types and
/// properties, which makes the client untrimmable, defeats ahead-of-time compilation, and turns property
/// reflection into a second media schema beside the typed contracts. Or the contract can declare, once, at
/// compile time, what a client may read out of it — which is this.
/// </para>
/// <para>
/// Every fact a decision is made on is a <b>constructor argument</b>, and that is the load-bearing part of
/// the design rather than a style choice. Constructor arguments live in the custom attribute blob, so they
/// can be read by a structured metadata reader over the exact bytes received, before the runtime has been
/// handed anything, and by a host that holds the assembly without calling into it. A value exposed only by
/// an overridden property is executable code: publishing it means running the package's code, and checking
/// it means having already loaded the payload that was supposed to be checked first.
/// </para>
/// <para>
/// The behavior is the other half and is deliberately separate: <see cref="Deserialize"/> constructs the
/// owning assembly's own typed value and hands it back as <see cref="object"/>, and
/// <see cref="Project"/> turns that value into one-way presentation data. They are split because a single
/// bytes-to-fields call proves nothing about whether a typed value ever existed, and existing is the claim.
/// </para>
/// <para>
/// The class is abstract, and the runtime — not a client — constructs the generated implementation when
/// the attribute is read. Nothing here needs <see cref="System.Activator"/>, a type name assembled from
/// text, or a member the compiler did not see.
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
    /// <remarks>
    /// Covers every member's wire name, declared shape and nullability, in declaration order, transitively.
    /// Two builds that would read one payload differently have different values here, whether or not their
    /// assembly version moved.
    /// </remarks>
    public string GeneratedMetadataHash { get; }

    /// <summary>
    /// Gets the SHA-256, upper-case hexadecimal, over the field schema the generated projection produces.
    /// </summary>
    /// <remarks>
    /// Covers <see cref="Schema"/> exactly: each descriptor's identifier, name, shape, semantics,
    /// prominence, cardinality, choices and components, in declaration order.
    /// </remarks>
    public string ProjectionSchemaHash { get; }

    /// <summary>Gets the exact CLR type this entry point reads and constructs.</summary>
    /// <remarks>
    /// A constructor argument, so the blob carries the type reference itself rather than a name written
    /// down beside it. A metadata reader decodes that reference from the bytes before anything loads; the
    /// runtime resolves the same reference after. A separate name argument would be the same fact twice,
    /// and two spellings of one fact are two things that can disagree. Any display name is taken from here,
    /// after load.
    /// </remarks>
    public Type EntityType { get; }

    /// <summary>Gets the source-generated serialization metadata this entry point reads through.</summary>
    /// <remarks>
    /// The actual context, not a description of one. A consumer that wants to know what the wire shape is
    /// asks it, rather than trusting a value that was written down beside it.
    /// </remarks>
    public abstract JsonSerializerContext SerializationContext { get; }

    /// <summary>Gets the metadata for <see cref="EntityType"/> itself, the root of that graph.</summary>
    public abstract JsonTypeInfo EntityTypeInfo { get; }

    /// <summary>Gets what a projection from this entry point contains, in declaration order.</summary>
    /// <remarks>
    /// Available before any payload is, so a client can render an empty shape or refuse a disagreeing
    /// schema without having fetched an entity at all.
    /// </remarks>
    public abstract IReadOnlyList<FieldDescriptor> Schema { get; }

    /// <summary>
    /// Reads one serialized entity into this assembly's own typed value.
    /// </summary>
    /// <param name="utf8Json">The serialized entity, as UTF-8 bytes.</param>
    /// <returns>The constructed value, whose type is <see cref="EntityType"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The payload is not a readable entity.</exception>
    /// <remarks>
    /// Returns <see cref="object"/> because a client cannot name the type — that is the whole reason this
    /// exists. What it can do is ask the value what it is, which is a stronger check than any name
    /// comparison: the answer carries the assembly the value was constructed in.
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
    /// <remarks>
    /// The other end of the same generated metadata, so that whatever holds a typed value — a host, a
    /// fixture, a test — writes what this entry point reads rather than something that resembles it.
    /// </remarks>
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
