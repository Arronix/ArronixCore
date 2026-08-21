using System.Security.Cryptography;
using System.Text;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Creates the canonical content digests used by compatibility bindings.</summary>
public static class CompatibilityDigest
{
    /// <summary>Hashes one canonical UTF-8 value.</summary>
    public static string Sha256(string canonicalValue)
    {
        ArgumentNullException.ThrowIfNull(canonicalValue);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue)))
            .ToLowerInvariant();
    }
}
