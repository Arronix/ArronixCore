using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arronix.Client.Serialization;

/// <summary>
/// The one description of how this client reads and writes the platform's wire contracts.
/// </summary>
/// <remarks>
/// Client and API are independently compiled, so this is the Client-owned declaration of their shared wire
/// forms: case-insensitive camel-case property names, untouched dictionary keys, omitted nulls, trailing
/// comma tolerance, strict ordinary numbers, camel-case enum names with legacy integer reads, and the
/// identifier converters below. It intentionally contains no server implementation dependency.
/// </remarks>
public static class ApiJsonOptions
{
    private static readonly JsonSerializerOptions SharedOptions = CreateOptions();

    /// <summary>
    /// Gets the configuration every request, response and live event is serialized with.
    /// </summary>
    public static JsonSerializerOptions Default => SharedOptions;

    /// <summary>
    /// Creates a fresh, independently mutable copy of the configuration.
    /// </summary>
    /// <returns>The configuration.</returns>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();

        Configure(options);
        return options;
    }

    /// <summary>
    /// Applies this client's converters to an existing configuration, for the live-event protocol, which
    /// owns its own.
    /// </summary>
    /// <param name="options">The configuration to add the converters to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AllowTrailingCommas = true;
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        options.Converters.Add(new MediaKindIdJsonConverter());
        options.Converters.Add(new MediaItemIdJsonConverter());
        options.Converters.Add(new MediaLevelIdJsonConverter());
        options.Converters.Add(new PluginIdJsonConverter());
        options.Converters.Add(new ProviderIdJsonConverter());
        options.Converters.Add(new OrdinalPathJsonConverter());
    }
}
