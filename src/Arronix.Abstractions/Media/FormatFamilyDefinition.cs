
namespace Arronix.Abstractions.Media;

/// <summary>A format-owned definition that a media type binds to without restating its vocabulary.</summary>
/// <typeparam name="TRepresentation">The typed representation the family contributes.</typeparam>
public sealed record FormatFamilyDefinition<TRepresentation>
    where TRepresentation : class, IRepresentation
{
    /// <summary>Gets the stable family identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the extensions that identify files belonging to this family.</summary>
    public required IReadOnlyList<string> FileExtensions { get; init; }
}
