using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;


namespace Arronix.Api.Serialization;

/// <summary>
/// Writes a media-kind identifier as its text and reads it back the same way.
/// </summary>
internal sealed class MediaKindIdJsonConverter : JsonConverter<MediaKindId>
{
    /// <inheritdoc />
    public override MediaKindId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? throw new JsonException("A media-kind identifier must be a non-empty string.")
            : MediaKindId.FromString(text);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MediaKindId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Writes an item identifier as the number it is.
/// </summary>
/// <remarks>
/// Without this it travels as an object wrapping a number, which is technically readable and reads badly
/// beside the <c>level:id</c> form the same value takes in a path segment.
/// </remarks>
internal sealed class MediaItemIdJsonConverter : JsonConverter<MediaItemId>
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
/// Writes a level identifier as its text and reads it back through the same parser the host uses.
/// </summary>
internal sealed class MediaLevelIdJsonConverter : JsonConverter<MediaLevelId>
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
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Writes an extension identifier as its text and reads it back through the same parser the loader uses,
/// so a value that could never have been loaded cannot be smuggled in through a request body.
/// </summary>
internal sealed class PluginIdJsonConverter : JsonConverter<PluginId>
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
/// Writes a provider identifier as its qualified text and reads it back through the same parser.
/// </summary>
internal sealed class ProviderIdJsonConverter : JsonConverter<ProviderId>
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
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Writes an ordinal tuple as its dotted text.
/// </summary>
/// <remarks>
/// Dotted text preserves the one property the value exists for — it sorts the same way the tuple compares,
/// component by component — which an object with a length and an indexer would not.
/// </remarks>
internal sealed class OrdinalPathJsonConverter : JsonConverter<OrdinalPath>
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
