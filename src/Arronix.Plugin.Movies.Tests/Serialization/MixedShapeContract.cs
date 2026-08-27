using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Abstractions.Media;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>A stage vocabulary for the fixture below.</summary>
public enum MixedStage
{
    /// <summary>The only stage.</summary>
    Ready = 0,
}

/// <summary>A timeline for the fixture below.</summary>
public sealed record MixedTimeline : IReleaseTimeline<MixedStage>
{
    /// <inheritdoc />
    [JsonIgnore]
    public MixedStage Stage => MixedStage.Ready;
}

/// <summary>
/// A value with two constructor parameters and two required members.
/// </summary>
/// <remarks>
/// The movie graph has no such type, so nothing there exercises where a required member's parameter is
/// positioned once a constructor has taken positions of its own.
/// </remarks>
public sealed class MixedFacet
{
    /// <summary>Creates one.</summary>
    /// <param name="first">The first constructor value.</param>
    /// <param name="second">The second, which has a default.</param>
    public MixedFacet(string first, int second = 9)
    {
        First = first;
        Second = second;
    }

    /// <summary>Gets the first constructor value.</summary>
    public string First { get; }

    /// <summary>Gets the second.</summary>
    public int Second { get; }

    /// <summary>Gets the first required member.</summary>
    public required string Third { get; init; }

    /// <summary>Gets the second.</summary>
    public required int Fourth { get; init; }
}

/// <summary>An item type carrying the mixed shape, so the generator publishes a contract for it.</summary>
public sealed class MixedItem : MediaItem<MixedItem, MixedTimeline, MixedStage>
{
    /// <summary>Gets the mixed-shape value.</summary>
    public MixedFacet? Facet { get; init; }
}

/// <summary>The serialization metadata for the fixture above.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MixedItem), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class MixedItemJsonContext : JsonSerializerContext;
