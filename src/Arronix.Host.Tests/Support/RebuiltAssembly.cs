using System.IO;
using System.Linq;
using Arronix.Plugins.Loading;

namespace Arronix.Host.Tests.Support;

/// <summary>
/// Writes a copy of an assembly that is a different build of the same identity.
/// </summary>
/// <remarks>
/// The module version identifier is what a compiler stamps into every build, so rewriting it produces
/// exactly the file a name-and-version comparison cannot tell apart from the original: same simple name,
/// same version, same culture, same public key token, different bytes. That is the case the private-copy
/// and identity rules exist for, and a test that copied the original byte for byte would not reach it.
/// </remarks>
internal static class RebuiltAssembly
{
    /// <summary>Writes a different build of the same assembly identity.</summary>
    /// <param name="source">The assembly to rebuild from.</param>
    /// <param name="destination">Where to write the rebuilt copy.</param>
    /// <returns>The destination path.</returns>
    /// <exception cref="InvalidOperationException">The source is unreadable, or its identifier is not found.</exception>
    public static string Write(string source, string destination)
    {
        if (!StagedAssembly.TryStage(source, out var staged, out var error))
        {
            throw new InvalidOperationException($"'{source}' could not be staged: {error}");
        }

        var bytes = File.ReadAllBytes(source);
        var original = staged!.ModuleVersionId.ToByteArray();
        var offset = IndexOf(bytes, original);

        if (offset < 0)
        {
            throw new InvalidOperationException(
                $"'{source}' does not carry its own module identifier in its bytes, so it cannot be rebuilt.");
        }

        // One byte is enough: the identifier is different, so this is a different build.
        bytes[offset] = (byte)(bytes[offset] ^ 0xFF);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, bytes);

        return destination;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle))
            {
                return start;
            }
        }

        return -1;
    }
}
