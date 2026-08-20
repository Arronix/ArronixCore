using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;


namespace Arronix.Plugins.Manifest;

/// <summary>
/// Reads an extension's declaration file.
/// </summary>
/// <remarks>
/// <para>
/// The reader <i>is</i> the schema validation. Deserializing into types whose members are required, with
/// unmapped members rejected, checks exactly what a hand-written schema document would check — and it
/// cannot drift from the types the way a separate schema can, because there is only one definition of the
/// shape. A schema document ships beside the assembly as an editor aid and is never consulted at runtime.
/// </para>
/// <para>
/// Comments are permitted. An operator editing a manifest by hand will want to leave a note next to a
/// capability, and refusing that buys nothing.
/// </para>
/// </remarks>
public static class PluginManifestReader
{
    /// <summary>
    /// The file name an extension's declaration is discovered under.
    /// </summary>
    public const string FileName = "plugin.json";

    private static readonly JsonSerializerOptions ReaderOptions = CreateOptions();

    /// <summary>
    /// Reads a declaration from text.
    /// </summary>
    /// <param name="json">The declaration text.</param>
    /// <param name="origin">Where the text came from, for the failure message.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentException"><paramref name="json"/> or <paramref name="origin"/> is blank.</exception>
    /// <exception cref="ArronixException">
    /// The text is not well-formed, omits a required member or carries a member the format does not define.
    /// The failure carries <see cref="CoreErrorCode.PluginManifestInvalid"/>.
    /// </exception>
    public static PluginManifest Read(string json, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        PluginManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, ReaderOptions);
        }
        catch (JsonException failure)
        {
            throw new ArronixException(
                CoreErrorCode.PluginManifestInvalid,
                $"The extension manifest at '{origin}' could not be read: {failure.Message}",
                failure);
        }

        return manifest
            ?? throw new ArronixException(
                CoreErrorCode.PluginManifestInvalid,
                $"The extension manifest at '{origin}' is empty.");
    }

    /// <summary>
    /// Reads a declaration from a file.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="ArronixException">
    /// The file is missing or its contents are not a valid declaration. The failure carries
    /// <see cref="CoreErrorCode.PluginManifestInvalid"/>.
    /// </exception>
    /// <remarks>
    /// Read through the platform's own file APIs rather than through the file-system contract, deliberately.
    /// Extension loading must not be blocked by a subsystem that has not been built yet, and the loader
    /// reads only paths the operator configured.
    /// </remarks>
    public static PluginManifest ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;

        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException failure)
        {
            throw new ArronixException(
                CoreErrorCode.PluginManifestInvalid,
                $"The extension manifest at '{path}' could not be opened: {failure.Message}",
                failure);
        }
        catch (UnauthorizedAccessException failure)
        {
            throw new ArronixException(
                CoreErrorCode.PluginManifestInvalid,
                $"The extension manifest at '{path}' could not be opened: {failure.Message}",
                failure);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArronixException(
                CoreErrorCode.PluginManifestInvalid,
                $"The extension manifest at '{path}' is empty.");
        }

        return Read(json, path);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        };

        options.Converters.Add(new NamingTokenJsonConverter());

        // Frozen so that the one shared instance cannot be mutated by anything that gets hold of it, and
        // populating the default resolver because this assembly does not use source-generated metadata.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// Reads a naming token written either as a descriptor or as a bare name.
/// </summary>
/// <remarks>
/// The documented manifest example writes tokens as bare strings. Widening a bare string into a descriptor
/// with an empty description keeps every manifest written against that example parsing, while letting an
/// extension that wants to supply token help do so. An additive superset, not a break.
/// </remarks>
internal sealed class NamingTokenJsonConverter : JsonConverter<NamingToken>
{
    public override NamingToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString() ?? string.Empty;
            return new NamingToken(name, string.Empty, string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A naming token must be written as a name or as an object.");
        }

        string? tokenName = null;
        var description = string.Empty;
        var exampleValue = string.Empty;
        var isRequired = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("A naming token object may contain only members.");
            }

            var member = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("A naming token member has no value.");
            }

            switch (member)
            {
                case "name":
                    tokenName = reader.GetString();
                    break;
                case "description":
                    description = reader.GetString() ?? string.Empty;
                    break;
                case "exampleValue":
                    exampleValue = reader.GetString() ?? string.Empty;
                    break;
                case "isRequired":
                    isRequired = reader.GetBoolean();
                    break;
                default:
                    throw new JsonException($"'{member}' is not a member of a naming token.");
            }
        }

        if (tokenName is null)
        {
            throw new JsonException("A naming token must declare a name.");
        }

        return new NamingToken(tokenName, description, exampleValue, isRequired);
    }

    public override void Write(Utf8JsonWriter writer, NamingToken value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteString("description", value.Description);
        writer.WriteString("exampleValue", value.ExampleValue);
        writer.WriteBoolean("isRequired", value.IsRequired);
        writer.WriteEndObject();
    }
}
