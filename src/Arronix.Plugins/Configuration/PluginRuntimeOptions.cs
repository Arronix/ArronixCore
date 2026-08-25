using System.ComponentModel.DataAnnotations;

namespace Arronix.Plugins.Configuration;

/// <summary>
/// What an operator may grant one extension.
/// </summary>
/// <remarks>
/// Grants are per extension and default to nothing. An extension holding the storage privilege with no
/// granted root can reach no files at all, and that is the correct outcome: the privilege says the extension
/// is <i>allowed</i> to be given folders, not that it is entitled to the machine.
/// </remarks>
public sealed class PluginAccessOptions
{
    /// <summary>
    /// Gets the folders the extension may read and write. Everything else is refused regardless of what
    /// the operating system would have permitted.
    /// </summary>
    public IList<string> GrantedRoots { get; } = [];

    /// <summary>
    /// Gets the only remote hosts the extension may reach. Empty means any host that is not denied. A
    /// pattern beginning with a dot matches a domain and everything beneath it.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>
    /// Gets the remote hosts the extension may never reach. Checked before the allow list.
    /// </summary>
    public IList<string> DeniedHosts { get; } = [];
}

/// <summary>
/// Operator control over extension loading.
/// </summary>
public sealed class PluginRuntimeOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Plugins";

    /// <summary>
    /// Gets or sets a value indicating whether extensions are loaded at all.
    /// </summary>
    /// <remarks>
    /// A switch rather than a documented procedure for emptying a folder. An operator diagnosing a problem
    /// needs to be able to start the platform with nothing loaded, and needs to do it without destroying the
    /// evidence.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets the identifiers of the extensions permitted to declare the telemetry-sink privilege.
    /// </summary>
    /// <remarks>
    /// A sink reads every telemetry event in the process — every message, tag and correlation identifier
    /// the host and every other extension raised — and, being an outbound network consumer by construction,
    /// can send it anywhere. That is not something a package grants itself by writing a word in its own
    /// manifest, so the operator names the exact identifiers here and an undeclared one is refused before
    /// its code is loaded.
    /// </remarks>
    public IList<string> TrustedSinks { get; } = [];

    /// <summary>
    /// Gets or sets the folder holding one subfolder per installed extension.
    /// </summary>
    [Required]
    public string RootFolder { get; set; } = "plugins";

    /// <summary>
    /// Gets or sets the folder beneath which each extension's own data, cache and scratch folders are laid
    /// out.
    /// </summary>
    [Required]
    public string StateFolder { get; set; } = "plugin-state";

    /// <summary>
    /// Gets the identifiers of extensions that are installed but must not be loaded.
    /// </summary>
    /// <remarks>
    /// A disabled extension is still discovered and still reported, so that an operator can see it is
    /// present and see why it is not running. Silently ignoring it would make "is it installed?" and "is it
    /// running?" indistinguishable.
    /// </remarks>
    public IList<string> Disabled { get; } = [];

    /// <summary>
    /// Gets what each extension has been granted, keyed by extension identifier.
    /// </summary>
    public IDictionary<string, PluginAccessOptions> Access { get; } =
        new Dictionary<string, PluginAccessOptions>(StringComparer.Ordinal);

    /// <summary>
    /// Gets what one extension has been granted, or an empty grant when the operator configured none.
    /// </summary>
    /// <param name="pluginId">The extension identifier.</param>
    /// <returns>The grant.</returns>
    public PluginAccessOptions AccessFor(string pluginId)
        => Access.TryGetValue(pluginId, out var access) ? access : new PluginAccessOptions();
}
