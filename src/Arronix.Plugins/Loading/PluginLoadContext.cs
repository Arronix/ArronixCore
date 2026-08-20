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
/// The second rule is the opposite, and it is the single most common way a plugin host is broken. The
/// contract assembly must resolve to the host's own instance, so it returns <see langword="null"/> on
/// purpose and lets the default context win. If an extension loaded its own copy, the interface it
/// implements would be a different runtime type from the one the host asks for, every cast would fail, and
/// the failure would present as "the extension registered nothing" rather than as a load error. It is
/// silent, it is baffling, and it has its own test.
/// </para>
/// <para>
/// Everything else the extension brought with it is loaded from a byte array rather than from a path, so no
/// file stays locked and the context remains genuinely collectible. Collectibility is switched on from the
/// first day even though nothing unloads yet: it costs nothing now and forecloses nothing later.
/// </para>
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// The assemblies an extension may never resolve. Prefix matched, ordinal.
    /// </summary>
    private static readonly string[] BlockedPrefixes =
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

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoadContext"/> class.
    /// </summary>
    /// <param name="plugin">The extension this context belongs to.</param>
    /// <param name="entryAssemblyPath">The full path of the extension's entry assembly.</param>
    /// <param name="nativeLibraryResolver">
    /// An optional resolver consulted when a native library is not found beside the extension.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="entryAssemblyPath"/> is blank.</exception>
    public PluginLoadContext(
        PluginId plugin,
        string entryAssemblyPath,
        INativeLibraryResolver? nativeLibraryResolver = null)
        : base(name: $"arronix-plugin:{plugin}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);

        Plugin = plugin;
        EntryAssemblyPath = entryAssemblyPath;
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _nativeResolver = nativeLibraryResolver;
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
    public static IReadOnlyList<string> BlockedAssemblyPrefixes => BlockedPrefixes;

    /// <summary>
    /// Determines whether an assembly name is one an extension may never resolve.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns><see langword="true"/> when the name is blocked.</returns>
    public static bool IsBlocked(string? assemblyName)
        => assemblyName is not null
            && BlockedPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));

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

        // 2. The one shared contract assembly, plus the shared framework: yield to the default context so
        //    the extension's types and the host's types are the same types.
        if (string.Equals(name, SharedContractAssembly, StringComparison.Ordinal)
            || IsSharedFramework(name))
        {
            return null;
        }

        // 3. The extension's own dependency closure.
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromBytes(path);
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

    private static bool IsSharedFramework(string? name)
    {
        if (name is null)
        {
            return false;
        }

        foreach (var candidate in SharedFrameworkNames)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var prefix in SharedFrameworkPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
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
