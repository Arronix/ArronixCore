using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Arronix.Plugins.Loading;

/// <summary>
/// One assembly file, read into memory exactly once, before anything is loaded from it.
/// </summary>
/// <remarks>
/// <para>
/// The bytes are read once and everything is derived from them: identity, content hash, module identifier,
/// reference table and pre-execution shape, and the load itself. Two reads of a path a package owns is a
/// race in which the file inspected and the file loaded need not be the same file.
/// </para>
/// <para>
/// Nothing here executes the assembly. The two questions it answers about execution — a module initializer
/// and an entry point — exist because loading is not free of side effects: a module initializer runs when
/// the assembly loads, whether or not anything calls into it.
/// </para>
/// </remarks>
internal sealed class StagedAssembly
{
    private readonly byte[] _bytes;

    private StagedAssembly(
        string path,
        byte[] bytes,
        AssemblyIdentity identity,
        string contentHash,
        Guid moduleVersionId,
        ReadOnlyCollection<AssemblyIdentity> references,
        bool hasModuleInitializer,
        bool hasEntryPoint)
    {
        Path = path;
        _bytes = bytes;
        Identity = identity;
        ContentHash = contentHash;
        ModuleVersionId = moduleVersionId;
        References = references;
        HasModuleInitializer = hasModuleInitializer;
        HasEntryPoint = hasEntryPoint;
    }

    /// <summary>Gets the file the bytes were read from.</summary>
    public string Path { get; }

    /// <summary>Gets the CLR assembly identity the file declares.</summary>
    public AssemblyIdentity Identity { get; }

    /// <summary>Gets the SHA-256 of the staged bytes, upper-case hexadecimal.</summary>
    public string ContentHash { get; }

    /// <summary>Gets the module version identifier the compiler stamped into this exact build.</summary>
    public Guid ModuleVersionId { get; }

    /// <summary>Gets every assembly this file references, in reference-table order.</summary>
    public ReadOnlyCollection<AssemblyIdentity> References { get; }

    /// <summary>Gets a value indicating whether the file carries a module initializer.</summary>
    /// <remarks>
    /// A module initializer is a <c>.cctor</c> on the module's own <c>&lt;Module&gt;</c> type. The runtime
    /// runs it as part of loading the assembly, before any member of it is touched, so an assembly that
    /// carries one cannot be described as inert data.
    /// </remarks>
    public bool HasModuleInitializer { get; }

    /// <summary>Gets a value indicating whether the file declares a managed entry point.</summary>
    public bool HasEntryPoint { get; }

    /// <summary>Gets the number of staged bytes.</summary>
    public int Length => _bytes.Length;

    /// <summary>
    /// Reads an assembly file once and derives everything decidable about it before it is loaded.
    /// </summary>
    /// <param name="path">The file to stage.</param>
    /// <param name="staged">The staged file on success; otherwise <see langword="null"/>.</param>
    /// <param name="error">Why the file could not be staged, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the file was read and is a managed assembly.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public static bool TryStage(string path, out StagedAssembly? staged, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        staged = null;
        error = null;

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException failure)
        {
            error = failure.Message;
            return false;
        }
        catch (DirectoryNotFoundException failure)
        {
            error = failure.Message;
            return false;
        }
        catch (IOException failure)
        {
            error = $"'{path}' could not be read: {failure.Message}";
            return false;
        }
        catch (UnauthorizedAccessException failure)
        {
            error = $"'{path}' could not be read: {failure.Message}";
            return false;
        }

        try
        {
            staged = Describe(path, bytes);
            return true;
        }
        catch (BadImageFormatException failure)
        {
            error = $"'{path}' is not a readable managed assembly: {failure.Message}";
            return false;
        }
    }

    /// <summary>Renders a CLR assembly identity the way a diagnostic must state it.</summary>
    /// <param name="identity">The identity to render.</param>
    /// <returns>The rendered identity.</returns>
    public static string Describe(AssemblyIdentity identity) => identity.ToString();

    /// <summary>How the runtime compares assembly simple names, and therefore how Arronix must.</summary>
    internal static StringComparer NameComparer => AssemblyIdentity.NameComparer;

    /// <summary>Determines whether two CLR assembly identities are the same binding identity.</summary>
    /// <param name="left">One identity.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when name, version, culture and public key token all agree.</returns>
    public static bool SameIdentity(AssemblyIdentity left, AssemblyIdentity right) => left.Matches(right);

    /// <summary>Determines whether an admitted identity answers a runtime binding request.</summary>
    /// <param name="admitted">The admitted identity.</param>
    /// <param name="requested">The request the runtime made.</param>
    /// <returns><see langword="true"/> when the request names exactly the admitted identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requested"/> is <see langword="null"/>.</exception>
    public static bool SameIdentity(AssemblyIdentity admitted, AssemblyName requested)
        => admitted.Matches(AssemblyIdentity.From(requested));

    /// <summary>Loads the staged bytes into a context, without touching the file again.</summary>
    /// <param name="context">The context to load into.</param>
    /// <returns>The loaded assembly.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public Assembly LoadInto(AssemblyLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var stream = new MemoryStream(_bytes, writable: false);
        return context.LoadFromStream(stream);
    }

    /// <summary>
    /// Reads everything decidable from the staged bytes, converting every way malformed metadata can fail into
    /// the one exception type that means "this file is not a readable assembly".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A truncated or garbage file fails inside <see cref="PEReader"/> as a
    /// <see cref="BadImageFormatException"/>, which is easy to anticipate. Metadata that parses but is
    /// internally corrupt is not: a hostile culture string reaches <see cref="AssemblyName.CultureName"/> and
    /// throws <see cref="System.Globalization.CultureNotFoundException"/>, and an out-of-range handle throws
    /// from the metadata reader. Both escape as argument failures, and an escaping exception here would abort
    /// the whole installation's admission over one bad file in one package.
    /// </para>
    /// <para>
    /// So every failure to interpret the bytes becomes <see cref="BadImageFormatException"/>. That is what the
    /// file being unreadable actually means, and it is what every caller already handles per candidate.
    /// </para>
    /// </remarks>
    private static StagedAssembly Describe(string path, byte[] bytes)
    {
        try
        {
            return Interpret(path, bytes);
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (ArgumentException failure)
        {
            throw Unreadable(path, failure);
        }
        catch (InvalidOperationException failure)
        {
            throw Unreadable(path, failure);
        }
        catch (NotSupportedException failure)
        {
            throw Unreadable(path, failure);
        }
        catch (OverflowException failure)
        {
            throw Unreadable(path, failure);
        }
    }

    private static BadImageFormatException Unreadable(string path, Exception failure)
        => new($"Its metadata could not be interpreted ({failure.GetType().Name}): {failure.Message}", path, failure);

    private static StagedAssembly Interpret(string path, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException("The file carries no managed metadata.", path);
        }

        var metadata = peReader.GetMetadataReader();

        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException("The file is a module rather than an assembly.", path);
        }

        var definition = metadata.GetAssemblyDefinition();

        // A definition's blob is always a full public key, so the binding token is derived from it.
        var identity = AssemblyIdentity.Create(
            metadata.GetString(definition.Name),
            definition.Version,
            definition.Culture.IsNil ? string.Empty : metadata.GetString(definition.Culture),
            definition.PublicKey.IsNil ? [] : metadata.GetBlobBytes(definition.PublicKey),
            isFullPublicKey: true);

        var references = new List<AssemblyIdentity>(metadata.AssemblyReferences.Count);

        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);

            // A reference blob is a token unless the row says otherwise. Treating a full key as a token
            // would compare an identity against a value the runtime never binds on.
            references.Add(AssemblyIdentity.Create(
                metadata.GetString(reference.Name),
                reference.Version,
                reference.Culture.IsNil ? string.Empty : metadata.GetString(reference.Culture),
                reference.PublicKeyOrToken.IsNil ? [] : metadata.GetBlobBytes(reference.PublicKeyOrToken),
                reference.Flags.HasFlag(AssemblyFlags.PublicKey)));
        }

        return new StagedAssembly(
            path,
            bytes,
            identity,
            Convert.ToHexString(SHA256.HashData(bytes)),
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid),
            references.AsReadOnly(),
            DeclaresModuleInitializer(metadata),
            peReader.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress is not (null or 0));
    }

    private static bool DeclaresModuleInitializer(MetadataReader metadata)
    {
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);

            if (!metadata.GetString(type.Name).Equals("<Module>", StringComparison.Ordinal)
                || !type.Namespace.IsNil)
            {
                continue;
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);

                if (metadata.GetString(method.Name).Equals(".cctor", StringComparison.Ordinal)
                    && method.Attributes.HasFlag(MethodAttributes.RTSpecialName)
                    && method.Attributes.HasFlag(MethodAttributes.Static))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
