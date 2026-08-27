using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arronix.Client.Serialization;

/// <summary>
/// The one description of how this client reads and writes the platform's wire contracts.
/// </summary>
/// <remarks>
/// <para>
/// The client and the server are separately compiled and separately deployed, so the serializer
/// configuration is the one thing about the wire that genuinely exists twice. That duplication is
/// deliberate — the alternative is a shared implementation assembly in the browser — and it is guarded by
/// a round-trip test that reads both configurations and requires them to agree byte for byte.
/// </para>
/// <para>
/// <b>The server must mirror exactly this:</b> web defaults (camel-cased names, case-insensitive reads,
/// numeric values accepted from strings), nulls omitted when writing, enumerations on the wire as their
/// numbers, and the converters below. Enumerations stay numeric because every one of them is a
/// closed vocabulary switched over exhaustively at both ends; a name would add a second spelling of a
/// value that already has one.
/// </para>
/// <para>
/// Converters exist only for the identities that cannot survive a round trip without one — an identifier
/// whose only constructor is private, and an ordinal tuple whose components are not properties. Every
/// other identity in the contract layer is a positional record and is left to the serializer's own
/// handling, because the fewer hand-written rules there are, the fewer there are to diverge.
/// </para>
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
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

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

        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new MediaKindIdJsonConverter());
        options.Converters.Add(new MediaLevelIdJsonConverter());
        options.Converters.Add(new PluginIdJsonConverter());
        options.Converters.Add(new ProviderIdJsonConverter());
        options.Converters.Add(new OrdinalPathJsonConverter());
    }
}
