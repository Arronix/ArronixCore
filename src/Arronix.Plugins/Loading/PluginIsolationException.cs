using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;


namespace Arronix.Plugins.Loading;

/// <summary>
/// Thrown when an extension reaches for an assembly it may not reference.
/// </summary>
/// <remarks>
/// <para>
/// Raised in two places, deliberately: by the static reference inspector before any context exists, and by
/// the load context itself if a reference somehow survives inspection. The second is not redundant — a
/// reference emitted by a reflection call rather than by the compiler never appears in the assembly
/// reference table, and only the load context sees it.
/// </para>
/// <para>
/// It is a throw rather than a null return, and that distinction is the whole of the isolation guarantee.
/// Returning <see langword="null"/> from a load context's resolver falls back to the default context, which
/// would <i>succeed</i> — silently handing the extension the host's implementation assemblies.
/// </para>
/// </remarks>
public sealed class PluginIsolationException : ArronixException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginIsolationException"/> class.
    /// </summary>
    public PluginIsolationException()
        : base(CoreErrorCode.PluginIsolationViolation, "The extension referenced an assembly it may not reference.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginIsolationException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public PluginIsolationException(string message)
        : base(CoreErrorCode.PluginIsolationViolation, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginIsolationException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public PluginIsolationException(string message, Exception innerException)
        : base(CoreErrorCode.PluginIsolationViolation, message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginIsolationException"/> class naming the assembly
    /// that was refused.
    /// </summary>
    /// <param name="blockedAssembly">The assembly the extension asked for.</param>
    /// <param name="requestedBy">The extension or assembly that asked for it.</param>
    public PluginIsolationException(string blockedAssembly, string requestedBy)
        : base(
            CoreErrorCode.PluginIsolationViolation,
            $"'{requestedBy}' references forbidden implementation assembly '{blockedAssembly}'. Extensions may reference supported contracts and their own private dependency closure, never Host, platform, or legacy implementation assemblies.")
    {
        BlockedAssembly = blockedAssembly;
        RequestedBy = requestedBy;
    }

    /// <summary>
    /// Gets the assembly that was refused.
    /// </summary>
    public string? BlockedAssembly { get; }

    /// <summary>
    /// Gets what asked for it.
    /// </summary>
    public string? RequestedBy { get; }
}
