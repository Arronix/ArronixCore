using System.Text.Json.Serialization;

namespace Arronix.Media.Movies;

/// <summary>The trimming-safe serialization metadata of the movie item graph.</summary>
/// <remarks>
/// <para>
/// One declared line, and it is here rather than generated for a measured reason: within a single
/// compilation a source generator cannot read another source generator's output, so the Arronix client
/// contract generator cannot hand this declaration to the framework's own serialization generator. Writing
/// it out is what lets that generator produce the complete, reflection-free metadata for the whole graph.
/// A media kind that omits it gets <c>ARX1010</c>, which names the exact declaration to add.
/// </para>
/// <para>
/// It is a declaration and not a schema. What crosses the wire is decided by the CLR shape of
/// <see cref="Movie"/>; nothing here restates a member, a name or a type.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Movie))]
internal sealed partial class MovieClientJsonContext : JsonSerializerContext;
