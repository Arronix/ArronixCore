using System.Text.Json.Serialization;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>
/// Source-generated serialization metadata for the whole <see cref="Movie"/> graph.
/// </summary>
/// <remarks>
/// <para>
/// A spike fixture, and it lives in a test project on purpose. G07.2 needs metadata like this inside the
/// client-safe contract assembly, addressed by a declared entry point; what that declaration looks like,
/// where it lives, and how a browser resolves it are the integration worker's to settle. What this file
/// answers is the question underneath all of that: whether the framework's generator can produce complete
/// trimming-safe metadata for this exact graph on this exact SDK, and whether a movie survives a round
/// trip through it.
/// </para>
/// <para>
/// The declaration is hand-written because it has to be. A source generator's ordinary output is not
/// visible to another source generator in the same compilation, so a platform generator cannot discover
/// <see cref="Movie"/> and emit this for the framework's generator to read. That is measured rather than
/// assumed; the measurement and the false positive that hides it are recorded in
/// <c>docs/research/g07/client-metadata-serialization.md</c>.
/// </para>
/// </remarks>
[JsonSerializable(typeof(Movie))]
internal sealed partial class MovieContractSerialization : JsonSerializerContext;
