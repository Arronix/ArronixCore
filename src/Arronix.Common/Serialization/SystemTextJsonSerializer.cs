using System.IO;
using System.Text.Json;
using Arronix.Abstractions.Serialization;


namespace Arronix.Common.Serialization;

/// <summary>
/// The platform's serializer, over the canonical options in <see cref="JsonDefaults"/>.
/// </summary>
/// <remarks>
/// <para>
/// The type is thin on purpose. Its value is not what it does but what it removes: an extension that
/// resolves this service cannot accidentally serialize with a different naming policy, a different null
/// handling or a different timestamp format, because it never sees an options instance to get wrong.
/// </para>
/// <para>
/// Nothing here constrains the payload type. The legacy serializer required a public parameterless
/// constructor, a leftover of a serializer that no longer ships, and that requirement excludes precisely
/// the immutable records the contract layer is made of.
/// </para>
/// </remarks>
public sealed class SystemTextJsonSerializer : IJsonSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance using the platform's canonical options.
    /// </summary>
    public SystemTextJsonSerializer()
        : this(JsonDefaults.Compact)
    {
    }

    /// <summary>
    /// Initializes a new instance using a supplied options instance.
    /// </summary>
    /// <param name="options">The options to serialize with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Intended for a host that has to serialize into an established external shape, and for tests. It is
    /// not a general invitation: a component that takes this overload has opted out of the guarantee the
    /// contract exists to give.
    /// </remarks>
    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The value is written as its declared type. A hierarchy whose derived members must survive the round
    /// trip declares that with <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>, which
    /// also writes the discriminator needed to read it back.
    /// </remarks>
    public string Serialize<TValue>(TValue value) => JsonSerializer.Serialize(value, _options);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The payload is malformed or does not match the target type.</exception>
    public TValue? Deserialize<TValue>(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize<TValue>(json, _options);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only a malformed or mismatched payload is reported as a failure. An empty or absent payload counts as
    /// malformed, because the alternative — throwing from a method whose whole purpose is not to — moves the
    /// failure to the one place the caller said it did not want it.
    /// </remarks>
    public bool TryDeserialize<TValue>(string json, out TValue? value)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            value = default;
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<TValue>(json, _options);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public Task SerializeAsync<TValue>(
        TValue value,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return JsonSerializer.SerializeAsync(destination, value, _options, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The payload is malformed or does not match the target type.</exception>
    public ValueTask<TValue?> DeserializeAsync<TValue>(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return JsonSerializer.DeserializeAsync<TValue>(source, _options, cancellationToken);
    }
}
