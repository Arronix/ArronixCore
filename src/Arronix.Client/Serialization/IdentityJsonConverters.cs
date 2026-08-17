#pragma warning disable ARX0013 // Shape contracts are experimental; this client serializes them.
#pragma warning disable ARX0014 // Plugin contracts are experimental; this client serializes the identifier.
#pragma warning disable ARX0015 // Provider contracts are experimental; this client serializes the identifier.

using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Serialization;

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
        => MediaLevelId.TryParse(reader.GetString(), out var id) ? id : default;

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
    public override PluginId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => PluginId.TryParse(reader.GetString(), out var id) ? id : default;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PluginId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
/// Reads and writes a provider identifier as its qualified text.
/// </summary>
public sealed class ProviderIdJsonConverter : JsonConverter<ProviderId>
{
    /// <inheritdoc />
    public override ProviderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ProviderId.TryParse(reader.GetString(), out var id) ? id : default;

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
        return text is not null && OrdinalPath.TryParse(text, out var path) ? path : OrdinalPath.Empty;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, OrdinalPath value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
