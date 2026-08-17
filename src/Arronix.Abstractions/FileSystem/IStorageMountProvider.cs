using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// Enumerates the machine's storage mounts.
/// </summary>
/// <remarks>
/// Mount enumeration is irreducibly platform-specific — each platform keeps its mount table somewhere
/// different and describes it differently — so implementations are supplied by platform packs. The
/// platform itself ships only the portable fallback, which reports what the framework's own drive
/// enumeration can see and nothing more.
/// </remarks>
[Experimental(ExperimentalContracts.FileSystem, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IStorageMountProvider
{
    /// <summary>
    /// Gets a value indicating whether this provider can read the mount table on the current machine.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Enumerates every mount this provider can see.
    /// </summary>
    /// <returns>The mounts, possibly empty.</returns>
    IReadOnlyList<IStorageMount> EnumerateMounts();

    /// <summary>
    /// Finds the mount a path lives on.
    /// </summary>
    /// <param name="path">The path to locate.</param>
    /// <returns>
    /// The mount containing the longest prefix of the path, or <see langword="null"/> when no mount
    /// matches.
    /// </returns>
    IStorageMount? FindMount(string path);
}
