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
/// permissive default it turns off is a way for a payload to mean something the sender did not write.
/// </para>
/// <para>
/// Metadata only: the write fast path is a generated delegate whose behavior no digest describes, so the
/// contract does without it and writes through the same metadata a reader uses.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Movie), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class MovieClientJsonContext : JsonSerializerContext;
