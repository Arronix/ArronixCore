using Arronix.Abstractions.Media;

namespace Arronix.Plugin.Movies.Tests.Support;

/// <summary>Lets the digest-pinned legacy fixture reach the now-hidden registration bridge.</summary>
internal static class MediaTypeDefinitionTestExtensions
{
    internal static IMediaTypeRegistration Capture(this IMediaTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Capture();
    }
}
