using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Arronix.Abstractions.Serialization;

/// <summary>
/// The platform's single JSON serializer, so that the host and every extension agree on wire shape.
/// </summary>
/// <remarks>
/// <para>
/// The contract exists so that extensions get the canonical options — naming policy, enum handling,
/// null handling, date handling — without referencing the implementation assembly that configures them.
/// An extension that reaches for a serializer of its own will produce payloads the host cannot read.
/// </para>
/// <para>
/// There is no <c>new()</c> constraint on the payload type: that constraint is a leftover of an older
/// serializer and it excludes exactly the immutable records this contract layer is made of.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Serialization, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IJsonSerializer
{
    /// <summary>
    /// Serializes a value to a JSON string.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The JSON representation.</returns>
    string Serialize<TValue>(TValue value);

    /// <summary>
    /// Deserializes a JSON string.
    /// </summary>
    /// <typeparam name="TValue">The target type.</typeparam>
    /// <param name="json">The JSON to read.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload was JSON null.</returns>
    TValue? Deserialize<TValue>(string json);

    /// <summary>
    /// Attempts to deserialize a JSON string, treating malformed input as a failure rather than an
    /// exception.
    /// </summary>
    /// <typeparam name="TValue">The target type.</typeparam>
    /// <param name="json">The JSON to read.</param>
    /// <param name="value">The deserialized value on success; otherwise the default.</param>
    /// <returns><see langword="true"/> when the payload was well-formed.</returns>
    bool TryDeserialize<TValue>(string json, out TValue? value);

    /// <summary>
    /// Serializes a value directly to a stream.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="destination">The stream to write to. Left open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the value has been written.</returns>
    Task SerializeAsync<TValue>(TValue value, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes a value directly from a stream.
    /// </summary>
    /// <typeparam name="TValue">The target type.</typeparam>
    /// <param name="source">The stream to read from. Left open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload was JSON null.</returns>
    ValueTask<TValue?> DeserializeAsync<TValue>(Stream source, CancellationToken cancellationToken = default);
}
