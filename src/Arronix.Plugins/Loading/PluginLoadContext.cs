using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Plugins;


namespace Arronix.Plugins.Loading;

/// <summary>
/// The isolation boundary: one load context per extension.
/// </summary>
/// <remarks>
/// <para>
/// Two rules do all the work here, and the order between them is the whole design.
/// </para>
/// <para>
/// The first rule is a hard deny, evaluated before anything else. Every host implementation assembly and
/// every legacy assembly is refused with a throw. Returning <see langword="null"/> for them — the reflex
/// that "let the default context try" is harmless — would silently <i>grant</i> the extension exactly what
/// the deny list exists to withhold, because the default context can resolve all of them.
/// </para>
/// <para>
/// The second rule is the opposite, and it is the single most common way a plugin host is broken. The host
/// contract assembly must resolve to the host's own instance, so it returns <see langword="null"/> and lets
/// the default context win. An extension that loaded its own copy would implement a different runtime type
/// from the one the host asks for, every cast would fail, and the failure would present as "the extension
/// registered nothing" rather than as a load error.
/// </para>
/// <para>
/// The third rule is the second, distinct sharing path and it is not the default context's. Contracts
/// published by packages are admitted once into the Host-owned contract context, and this context resolves
/// only the ones its own package declared a dependency on. Everything else the extension brought with it is
/// loaded privately from a byte array, so no file stays locked and the context remains collectible.
/// </para>
/// <para>
/// Anything that matches none of the four rules is refused rather than handed to the default context, where
/// Host, the API and everything the host itself loaded already live.
/// </para>
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// The assemblies an extension may never resolve. Prefix matched, ordinal.
    /// </summary>
    private static readonly ImmutableArray<string> BlockedPrefixes =
    [
        "Arronix.Common",
        "Arronix.Plugins",
        "Arronix.Host",
        "Arronix.Api",
        "Arronix.Client",
        "NzbDrone",
        "Sonarr"
    ];

    /// <summary>
    /// The root contract assembly that must unify with the host's instance.
    /// </summary>
    private const string SharedContractAssembly = "Arronix.Abstractions";

    /// <summary>
    /// Assembly-name prefixes that belong to the runtime or to a shared framework the host already carries.
    /// </summary>
    private static readonly string[] SharedFrameworkPrefixes =
    [
        "System.",
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore.",
        "Microsoft.CSharp",
        "Microsoft.VisualBasic",
        "Microsoft.Win32."
    ];

    private static readonly string[] SharedFrameworkNames =
    [
        "System",
        "mscorlib",
        "netstandard",
        "WindowsBase"
    ];

    private readonly AssemblyDependencyResolver _resolver;
    private readonly INativeLibraryResolver? _nativeResolver;
    private readonly PackageContractScope _contracts;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoadContext"/> class.
    /// </summary>
    /// <param name="plugin">The extension this context belongs to.</param>
    /// <param name="entryAssemblyPath">The full path of the extension's entry assembly.</param>
    /// <param name="nativeLibraryResolver">
    /// An optional resolver consulted when a native library is not found beside the extension.
    /// </param>
    /// <param name="contracts">
    /// This package's scoped view of the installation's shared contracts. Package-scoped rather than the
    /// whole store: a package resolves only what it and its declared closure published. A caller that
    /// deliberately shares nothing states that with <see cref="PackageContractScope.Empty"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="entryAssemblyPath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="contracts"/> is <see langword="null"/>.</exception>
    internal PluginLoadContext(
        PluginId plugin,
        string entryAssemblyPath,
        INativeLibraryResolver? nativeLibraryResolver,
        PackageContractScope contracts)
        : base(name: $"arronix-plugin:{plugin}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentNullException.ThrowIfNull(contracts);

        Plugin = plugin;
        EntryAssemblyPath = entryAssemblyPath;
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _nativeResolver = nativeLibraryResolver;
        _contracts = contracts;
    }

    /// <summary>
    /// Gets the extension this context belongs to.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the full path of the extension's entry assembly.
    /// </summary>
    public string EntryAssemblyPath { get; }

    /// <summary>
    /// Gets the assembly names an extension may never resolve.
    /// </summary>
    public static ImmutableArray<string> BlockedAssemblyPrefixes => BlockedPrefixes;

    /// <summary>
    /// Determines whether an assembly name is one an extension may never resolve.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns><see langword="true"/> when the name is blocked.</returns>
    public static bool IsBlocked(string? assemblyName)
        => assemblyName is not null
            && BlockedPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Determines whether an assembly name is the one contract assembly that must unify with the host's own
    /// instance.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns><see langword="true"/> when the name is the host contract assembly.</returns>
    internal static bool IsHostContract(string? assemblyName)
        => string.Equals(assemblyName, SharedContractAssembly, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the extension's entry assembly.
    /// </summary>
    /// <returns>The entry assembly, loaded into this context.</returns>
    /// <exception cref="FileNotFoundException">The entry assembly is not where the manifest said it was.</exception>
    /// <remarks>
    /// Loaded from a byte array so the file is not held open. A locked assembly file is the reason a host
    /// cannot replace an extension without a restart, and it also defeats collection.
    /// </remarks>
    public Assembly LoadEntryAssembly()
    {
        if (!File.Exists(EntryAssemblyPath))
        {
            throw new FileNotFoundException(
                $"The entry assembly declared by extension '{Plugin}' was not found.",
                EntryAssemblyPath);
        }

        return LoadFromBytes(EntryAssemblyPath);
    }

    /// <summary>Loads the entry assembly from bytes the loader already staged.</summary>
    /// <param name="staged">The staged entry assembly.</param>
    /// <returns>The entry assembly, loaded into this context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="staged"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The pipeline decides admissibility from metadata and then loads. Those must be one read of one file:
    /// two reads of a path the package owns is a race in which the assembly judged and the assembly that
    /// runs need not be the same one.
    /// </remarks>
    internal Assembly LoadEntryAssembly(StagedAssembly staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        return staged.LoadInto(this);
    }

    /// <inheritdoc />
    /// <exception cref="PluginIsolationException">
    /// The extension asked for a host implementation assembly or a legacy assembly.
    /// </exception>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        var name = assemblyName.Name;

        // 1. Hard deny, before any fallback. Order is load-bearing: see the remarks on the type.
        if (IsBlocked(name))
        {
            throw new PluginIsolationException(name!, Plugin.ToString());
        }

        // 2. The host contract assembly and the shared framework: yield to the default context so the
        //    extension's types and the host's types are the same types. Above rule 3 on purpose, so nothing
        //    about package contracts can dislodge Arronix.Abstractions unification.
        if (IsHostContract(name) || IsSharedFramework(name))
        {
            return null;
        }

        // 3. The shared contracts this package may bind to: not a path, not a version match, not a copy of
        //    the same bytes, but the one Assembly object every publisher and dependant in the closure was
        //    given. Above the private resolver, so a private copy that reached a folder cannot win.
        var shared = _contracts.Resolve(assemblyName);
        if (shared is not null)
        {
            return shared;
        }

        // 4. The extension's own private closure.
        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        if (path is not null)
        {
            return LoadFromBytes(path);
        }

        // 5. Nothing else. Returning null here hands the request to the default context, where Host, the
        //    API and everything the host itself loaded already live, so a package with an unresolved
        //    dependency would silently bind to whatever the process happens to have.
        throw new FileNotFoundException(
            $"Extension '{Plugin}' requested '{assemblyName}', which is neither the host contract assembly, "
            + "the shared framework, a shared contract it depends on, nor part of its own payload.",
            assemblyName.Name);
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is not null)
        {
            return LoadUnmanagedDllFromPath(path);
        }

        var resolved = _nativeResolver?.Resolve(unmanagedDllName);
        return resolved is not null ? NativeLibrary.Load(resolved) : nint.Zero;
    }

    internal static bool IsSharedFramework(string? name)
    {
        if (name is null)
        {
            return false;
        }

        foreach (var candidate in SharedFrameworkNames)
        {
            if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var prefix in SharedFrameworkPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Assembly LoadFromBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        return LoadFromStream(stream);
    }
}
