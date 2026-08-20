
namespace Arronix.Abstractions.Hosting;

/// <summary>
/// Reads the operating system's identity from one particular source — a release file, a system call, a
/// vendor-specific configuration file.
/// </summary>
/// <remarks>
/// <para>
/// Probes are supplied by platform packs, which are host-trusted and selected by runtime identifier
/// rather than declared through a manifest. Several probes are typically registered at once; the host
/// asks each supported probe in registration order and takes the first answer.
/// </para>
/// <para>
/// This is the clearest evidence that the seam is real rather than speculative: every implementation
/// that exists today already lives outside the platform assembly.
/// </para>
/// </remarks>
public interface IOsVersionProbe
{
    /// <summary>
    /// Gets a value indicating whether this probe can run on the current machine. A probe that reads a
    /// file reports whether the file exists; a probe that shells out reports whether the tool is
    /// present.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Reads the operating system identity.
    /// </summary>
    /// <returns>
    /// The identity, or <see langword="null"/> when this probe ran but could not determine one.
    /// Implementations return <see langword="null"/> rather than throwing: a probe that cannot answer
    /// is ordinary, and the host simply moves on to the next.
    /// </returns>
    OsVersionDescriptor? Read();
}
