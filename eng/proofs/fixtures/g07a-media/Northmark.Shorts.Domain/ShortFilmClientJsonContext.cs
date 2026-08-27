using System.Text.Json;
using System.Text.Json.Serialization;

namespace Northmark.Shorts;

/// <summary>Trimming-safe serialization metadata for the short-film item graph.</summary>
/// <remarks>
/// Declared by hand because one source generator cannot read another's output in the same compilation.
/// Omitting it is reported as <c>ARX1010</c>; the strict, metadata-only options are the ones the Arronix
/// client contract describes, and anything else is refused rather than published under a mismatched hash.
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShortFilm), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class ShortFilmClientJsonContext : JsonSerializerContext;
