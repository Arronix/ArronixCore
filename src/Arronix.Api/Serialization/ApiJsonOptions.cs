using System.Text.Json;
using Arronix.Common.Serialization;

namespace Arronix.Api.Serialization;

/// <summary>
/// The single description of how this platform writes and reads JSON on the wire, shared by the REST
/// endpoints, the event stream and anything that needs to hash a payload.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists because the wire contracts cross an assembly-isolation boundary: the client compiles
/// against the contract assembly and nothing else, so it cannot share this configuration object and has to
/// reproduce it. That duplication is the one the client/server split genuinely forces, and it is made
/// survivable by keeping the configuration small, declarative and asserted by a round-trip test rather than
/// by scattering serializer settings through the endpoints.
/// </para>
/// <para>
/// It starts from the platform-wide defaults rather than restating them, and then makes exactly two
/// changes, each of which is a bug if it is left out.
/// </para>
/// </remarks>
public static class ApiJsonOptions
{
    /// <summary>
    /// Gets the options every response is written with and every request body is read with.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = Create();

    /// <summary>
    /// Applies the API's configuration to an existing options instance, for the places the framework
    /// insists on owning the instance itself.
    /// </summary>
    /// <param name="options">The options to configure.</param>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        JsonDefaults.Apply(options);

        // CHANGE 1 — dictionary keys are left exactly as they were written.
        //
        // The platform-wide default camel-cases dictionary keys, which is right when a dictionary stands in
        // for an object whose property names happen to be dynamic. Here it is wrong and silently
        // destructive: every dictionary that crosses this boundary is keyed by an IDENTIFIER an extension
        // declared — a field id, a monitor-dimension id, a workbench row id, a provider setting id — and
        // those identifiers are matched by ordinal comparison against the same extension's declaration.
        // Rewriting the first character of "APIKey" to "aPIKey" on the way out, with no matching rule on the
        // way in, breaks the round trip for exactly the payloads a settings form posts back.
        options.DictionaryKeyPolicy = null;

        // CHANGE 2 — identifiers travel as the text they are.
        //
        // Several identity types in the contract assembly are constructed through a validating factory and
        // expose no settable member, so the reflection-based reader cannot rebuild one. Left alone they
        // serialize to a shape that cannot be read back — which is a round trip that fails at run time, on
        // the client, rather than at build time here. Each converter below writes the identifier's canonical
        // text and reads it back through the same parser the rest of the platform uses.
        options.Converters.Add(new MediaKindIdJsonConverter());
        options.Converters.Add(new MediaItemIdJsonConverter());
        options.Converters.Add(new MediaLevelIdJsonConverter());
        options.Converters.Add(new PluginIdJsonConverter());
        options.Converters.Add(new ProviderIdJsonConverter());
        options.Converters.Add(new OrdinalPathJsonConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        Apply(options);
        options.MakeReadOnly();
        return options;
    }
}
