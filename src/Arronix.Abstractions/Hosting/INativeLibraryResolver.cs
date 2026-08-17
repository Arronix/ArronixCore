using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Hosting;

/// <summary>
/// Maps a native library name to a file the current platform can actually load.
/// </summary>
/// <remarks>
/// <para>
/// The framework's default probing does not know that a library published under one name ships under a
/// versioned soname on some distributions. Whoever owns the native dependency knows; the platform does
/// not, and should not have to name a single native library in order to load one.
/// </para>
/// <para>
/// Resolvers are consulted in registration order until one answers.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Hosting, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface INativeLibraryResolver
{
    /// <summary>
    /// Resolves a native library name to a loadable path or platform-specific file name.
    /// </summary>
    /// <param name="libraryName">The name the managed code asked for.</param>
    /// <returns>
    /// The path or file name to load, or <see langword="null"/> when this resolver has no opinion —
    /// which lets the host fall through to the next resolver and finally to default probing.
    /// </returns>
    string? Resolve(string libraryName);
}
