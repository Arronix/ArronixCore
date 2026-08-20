
namespace Arronix.Abstractions.Media;

/// <summary>A title and optional description of a media entity in one language.</summary>
/// <param name="Title">The title in that language.</param>
/// <param name="Overview">The synopsis or description in that language, when supplied.</param>
/// <remarks>
/// This is the localized form of the two common <see cref="IMediaEntity"/> text facts. Media-specific
/// localized payloads remain free to define richer records when they genuinely carry additional facts.
/// </remarks>
public sealed record ItemInfo(string Title, string? Overview);
