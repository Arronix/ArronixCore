using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arronix.Media.Movies;

/// <summary>Trimming-safe serialization metadata for the movie item graph.</summary>
/// <remarks>
/// <para>
/// Hand-written because it has to be: a source generator's ordinary output is not visible to another
/// source generator in the same compilation, so the Arronix generator cannot emit this for the framework's
/// generator to read. A media kind that omits it gets <c>ARX1010</c>, which names the declaration to add.
/// </para>
/// <para>
/// <see cref="JsonSerializerDefaults.Strict"/> because a client contract payload is untrusted input. Every
/// permissive default it turns off — case-insensitive matching, unmapped members, duplicate properties,
/// null into a non-nullable member, a missing required constructor argument — is a way for a payload to
/// mean something the sender did not write.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Movie))]
internal sealed partial class MovieClientJsonContext : JsonSerializerContext;
