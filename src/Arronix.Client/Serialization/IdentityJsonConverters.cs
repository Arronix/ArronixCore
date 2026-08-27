
using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Serialization;

/// <summary>Reads and writes a media-kind identifier as its canonical text.</summary>
public sealed class MediaKindIdJsonConverter : JsonConverter<MediaKindId>
{
    /// <inheritdoc />
    public override MediaKindId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || string.IsNullOrWhiteSpace(reader.GetString()))
        {
            throw new JsonException("A media-kind identifier must be a non-empty JSON string.");
        }

        return MediaKindId.FromString(reader.GetString()!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MediaKindId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Reads and writes a host-assigned media item identifier as its numeric wire value.</summary>
public sealed class MediaItemIdJsonConverter : JsonConverter<MediaItemId>
{
    /// <inheritdoc />
    public override MediaItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => MediaItemId.FromInt64(reader.GetInt64());

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MediaItemId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value.Value);
    }
}

/// <summary>
/// Reads and writes a media level identifier as the text it was created from.
/// </summary>
/// <remarks>
/// The identity is a brand with a private constructor, so the serializer has no way to rebuild one from
/// an object with a single property. Text is the right form anyway: it is what the shape declares, what a
/// route carries and what an operator reads in a log.
/// </remarks>
public sealed class MediaLevelIdJsonConverter : JsonConverter<MediaLevelId>
{
    /// <inheritdoc />
    public override MediaLevelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => MediaLevelId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("A level identifier must be a non-empty string.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MediaLevelId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
/// Reads and writes an extension identifier as its text.
/// </summary>
public sealed class PluginIdJsonConverter : JsonConverter<PluginId>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">
    /// The token is not a string, or the text is not a well-formed identifier.
    /// </exception>
    /// <remarks>
    /// Refused rather than defaulted. A default identifier compares equal to every other unreadable one, so
    /// defaulting silently merges packages the writer never merged.
    /// </remarks>
    public override PluginId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"An extension identifier must be a JSON string, not {reader.TokenType}.");
        }

        return PluginId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException(
                "An extension identifier must be lower-case alphanumeric segments separated by dots, starting with a letter.");
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">The value is the default, which names no extension.</exception>
    public override void Write(Utf8JsonWriter writer, PluginId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value == default)
        {
            throw new JsonException("A default extension identifier names nothing and must not be written.");
        }

        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Reads and writes a provider identifier as its qualified text.
/// </summary>
public sealed class ProviderIdJsonConverter : JsonConverter<ProviderId>
{
    /// <inheritdoc />
    public override ProviderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ProviderId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("A provider identifier must be of the form 'extension:local'.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ProviderId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
/// Reads and writes an ordinal tuple as its dotted text.
/// </summary>
/// <remarks>
/// The tuple is stored inline and exposes no per-component property, so without this it would serialize
/// as its length and read back as nothing — a silent data loss in the value the whole ordering of a
/// library depends on.
/// </remarks>
public sealed class OrdinalPathJsonConverter : JsonConverter<OrdinalPath>
{
    /// <inheritdoc />
    public override OrdinalPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return OrdinalPath.Empty;
        }

        return OrdinalPath.TryParse(text, out var path)
            ? path
            : throw new JsonException("An ordinal path must be up to four dot-separated integers.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, OrdinalPath value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
