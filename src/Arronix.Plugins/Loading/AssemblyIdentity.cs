using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;

namespace Arronix.Plugins.Loading;

/// <summary>
/// An immutable CLR assembly identity: the four parts that decide binding.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AssemblyName"/> is not used to carry a proved identity because it is mutable — its setters and
/// <see cref="AssemblyName.SetPublicKeyToken"/> can rewrite an identity after the bytes it came from were
/// staged and validated. Every value here is a readonly field of an immutable type.
/// </para>
/// <para>
/// The public key token is stored, never a full public key. A definition always carries the full key and a
/// reference may carry either, so both are reduced to the token the runtime actually binds on.
/// </para>
/// </remarks>
internal readonly record struct AssemblyIdentity
{
    private AssemblyIdentity(string name, Version version, string culture, string publicKeyToken)
    {
        Name = name;
        Version = version;
        Culture = culture;
        PublicKeyToken = publicKeyToken;
    }

    /// <summary>Gets the simple assembly name.</summary>
    public string Name { get; }

    /// <summary>Gets the assembly version. Never <see langword="null"/>.</summary>
    /// <remarks><see cref="System.Version"/> is itself immutable, so exposing it hands out no writable state.</remarks>
    public Version Version { get; }

    /// <summary>Gets the culture name, or the empty string for a neutral assembly.</summary>
    public string Culture { get; }

    /// <summary>Gets the public key token as upper-case hexadecimal, or the empty string when unsigned.</summary>
    public string PublicKeyToken { get; }

    /// <summary>How the runtime compares assembly simple names, and therefore how Arronix must.</summary>
    /// <remarks>
    /// Measured: a context asked for <c>toy.mixedcase</c> accepts an assembly named <c>Toy.MixedCase</c>, and
    /// a context that already loaded one spelling answers the other from its own cache without calling the
    /// resolver. Simple-name binding is case-insensitive, so two contracts differing only in case are
    /// genuinely ambiguous.
    /// </remarks>
    public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Builds an identity from metadata parts.</summary>
    /// <param name="name">The simple name.</param>
    /// <param name="version">The version, or <see langword="null"/> for 0.0.0.0.</param>
    /// <param name="culture">The culture name, or <see langword="null"/> for neutral.</param>
    /// <param name="publicKeyOrToken">The key or token blob, which may be empty.</param>
    /// <param name="isFullPublicKey">
    /// Whether the blob is a full public key rather than an already-computed token.
    /// </param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static AssemblyIdentity Create(
        string name,
        Version? version,
        string? culture,
        ReadOnlySpan<byte> publicKeyOrToken,
        bool isFullPublicKey)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new AssemblyIdentity(
            name,
            version ?? new Version(0, 0, 0, 0),
            culture ?? string.Empty,
            publicKeyOrToken.IsEmpty
                ? string.Empty
                : Convert.ToHexString(isFullPublicKey ? TokenOf(publicKeyOrToken) : publicKeyOrToken));
    }

    /// <summary>Builds an identity from a runtime binding request.</summary>
    /// <param name="requested">The requested name.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requested"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A request's public key token is already a token; the runtime never presents a full key here.
    /// </remarks>
    public static AssemblyIdentity From(AssemblyName requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        return Create(
            requested.Name ?? string.Empty,
            requested.Version,
            requested.CultureName,
            requested.GetPublicKeyToken() ?? [],
            isFullPublicKey: false);
    }

    /// <summary>
    /// Determines whether this identity is the same binding identity as another.
    /// </summary>
    /// <param name="other">The other identity.</param>
    /// <returns><see langword="true"/> when name, version, culture and token all agree.</returns>
    /// <remarks>
    /// Every part is compared because every part decides binding and the runtime checks almost none of them.
    /// Measured: returning an assembly whose simple name does not match the request fails with a
    /// <see cref="System.IO.FileLoadException"/>, and version, culture and token are not checked at all — a
    /// request for 9.9.9.9 is satisfied by a returned 1.0.0.0 without complaint. This is the only place the
    /// other three are enforced.
    /// </remarks>
    public bool Matches(AssemblyIdentity other)
        => NameComparer.Equals(Name, other.Name)
            && Version == other.Version
            && string.Equals(Culture, other.Culture, StringComparison.OrdinalIgnoreCase)
            && string.Equals(PublicKeyToken, other.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>Renders the identity the way a diagnostic must state it.</summary>
    /// <returns>The rendered identity.</returns>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Name}, Version={Version}, Culture={(Culture.Length == 0 ? "neutral" : Culture)}, PublicKeyToken={(PublicKeyToken.Length == 0 ? "null" : PublicKeyToken)}");

    /// <summary>Reduces a full public key to the eight-byte token the runtime binds on.</summary>
    private static byte[] TokenOf(ReadOnlySpan<byte> publicKey)
    {
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(publicKey, hash);

        var token = new byte[8];

        for (var index = 0; index < token.Length; index++)
        {
            token[index] = hash[hash.Length - 1 - index];
        }

        return token;
    }
}
