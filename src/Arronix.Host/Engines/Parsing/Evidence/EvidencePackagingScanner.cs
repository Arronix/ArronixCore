using System.IO;
using System.Linq;
using Arronix.Abstractions.Quality;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads how a release is packaged and which container it names.
/// </summary>
/// <remarks>
/// <para>
/// Packaging is reported only when the release <i>states</i> it — an explicit whole-disc spelling, or a
/// disc-image extension. It is never inferred from the shape of a title. The heuristic version of this
/// question is a known over-firing one: a title whose <i>work name</i> happens to contain a disc word is
/// not a disc image, and reporting it as one silently refuses a perfectly ordinary encode.
/// </para>
/// <para>
/// The container is load-bearing rather than a fallback. A bare line count inside a streaming container
/// is a stream download and not a broadcast capture, and that reading is keyed on the container being
/// carried as a typed fact instead of reached through a per-kind guard string.
/// </para>
/// </remarks>
internal static class EvidencePackagingScanner
{
    /// <summary>
    /// Reads the packaging statement.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <param name="container">The container the file carries, when there is a file.</param>
    /// <returns>The normalized packaging token, or <see langword="null"/> when nothing stated one.</returns>
    internal static string? Read(EvidenceTokenStream stream, string? container)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var stated = EvidenceClaims.MostSpecific(stream.OfClass(EvidenceTokenClass.Packaging));
        if (stated is not null)
        {
            return stated;
        }

        return IsDiscImageExtension(container) ? EvidencePackagingTokens.DiscImage : null;
    }

    /// <summary>
    /// Reads the container.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <param name="fileName">The file's name, when the file is on disk.</param>
    /// <returns>The extension, leading dot included, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A real file's own extension outranks anything a title claimed, because it is a fact about a thing
    /// that exists rather than a claim about a thing being offered.
    /// </remarks>
    internal static string? ReadContainer(EvidenceTokenStream stream, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(extension))
            {
                return extension.ToLowerInvariant();
            }
        }

        var claims = stream.OfClass(EvidenceTokenClass.Container)
            .Select(static token => token.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return claims.Length == 1 ? claims[0] : null;
    }

    private static bool IsDiscImageExtension(string? container) =>
        container is ".iso" or ".img";
}
