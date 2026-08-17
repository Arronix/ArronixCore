using System.IO;
using System.Text.Json;
using Arronix.Abstractions.Serialization;

#pragma warning disable ARX0010 // Serialization contracts are experimental; this assembly implements them.

namespace Arronix.Common.Serialization;

/// <summary>
/// Convenience over <see cref="IJsonSerializer"/> for the case where a missing payload is a failure.
/// </summary>
/// <remarks>
/// <para>
/// Every method here extends the serializer contract. Nothing extends <see cref="object"/>: the legacy
/// helpers did, which put a <c>ToJson</c> on every type in the codebase including every value type, where
/// it also boxed.
/// </para>
/// <para>
/// The methods exist to close one specific hole. A JSON <c>null</c> deserializes to a null, and the legacy
/// codebase papered over that with a <c>?? new T()</c> fallback that turned an empty response from a remote
/// service into a valid-looking object with every field at its default — a bug that surfaces far from its
/// cause. These report it where it happens instead.
/// </para>
/// </remarks>
public static class JsonSerializationExtensions
{
    /// <summary>
    /// Deserializes a payload that is required to carry a value.
    /// </summary>
    /// <typeparam name="TValue">The target type.</typeparam>
    /// <param name="serializer">The serializer.</param>
    /// <param name="json">The JSON to read.</param>
    /// <returns>The deserialized value, never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="serializer"/> or <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="JsonException">
    /// The payload is malformed, does not match the target type, or is JSON null.
    /// </exception>
    public static TValue DeserializeRequired<TValue>(this IJsonSerializer serializer, string json)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(json);

        var value = serializer.Deserialize<TValue>(json);

        if (value is null)
        {
            throw MissingPayload<TValue>();
        }

        return value;
    }

    /// <summary>
    /// Deserializes a stream that is required to carry a value.
    /// </summary>
    /// <typeparam name="TValue">The target type.</typeparam>
    /// <param name="serializer">The serializer.</param>
    /// <param name="source">The stream to read from. Left open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized value, never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="serializer"/> or <paramref name="source"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="JsonException">
    /// The payload is malformed, does not match the target type, or is JSON null.
    /// </exception>
    public static async ValueTask<TValue> DeserializeRequiredAsync<TValue>(
        this IJsonSerializer serializer,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(source);

        var value = await serializer.DeserializeAsync<TValue>(source, cancellationToken).ConfigureAwait(false);

        if (value is null)
        {
            throw MissingPayload<TValue>();
        }

        return value;
    }

    private static JsonException MissingPayload<TValue>() =>
        new($"The payload carried no {typeof(TValue).Name}; a value was required.");
}
