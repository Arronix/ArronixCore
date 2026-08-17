using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;

namespace Arronix.Abstractions.Plugins;

/// <summary>
/// Thrown when an extension reaches for something its granted capabilities do not cover.
/// </summary>
/// <remarks>
/// <para>
/// A dedicated type rather than a bare platform failure so that the capability and the contract are
/// carried as data. Both ends of the gate raise it: the registry when an extension registers something it
/// did not declare, and the context when it reads a dependency it did not declare. Reporting them the same
/// way is what makes the least-privilege model legible in a support bundle.
/// </para>
/// <para>
/// It always carries <see cref="CoreErrorCode.PluginCapabilityMissing"/>, so a caller that only cares
/// about the code need not know this type exists.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Plugins, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class PluginCapabilityException : ArronixException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginCapabilityException"/> class.
    /// </summary>
    public PluginCapabilityException()
        : base(CoreErrorCode.PluginCapabilityMissing, "The extension does not hold the required capability.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginCapabilityException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public PluginCapabilityException(string message)
        : base(CoreErrorCode.PluginCapabilityMissing, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginCapabilityException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public PluginCapabilityException(string message, Exception innerException)
        : base(CoreErrorCode.PluginCapabilityMissing, message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginCapabilityException"/> class naming what was
    /// missing and what it was needed for.
    /// </summary>
    /// <param name="plugin">The extension that reached for something it may not have.</param>
    /// <param name="required">The capability it would have needed.</param>
    /// <param name="contractName">The contract it reached for.</param>
    public PluginCapabilityException(PluginId plugin, Capability required, string contractName)
        : base(
            CoreErrorCode.PluginCapabilityMissing,
            $"Extension '{plugin}' requires the '{CapabilityNames.ToWireName(required)}' capability to use '{contractName}'. Declare it in the extension manifest.")
    {
        Plugin = plugin;
        Required = required;
        ContractName = contractName;
    }

    /// <summary>
    /// Gets the extension that reached for something it may not have.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the capability it would have needed.
    /// </summary>
    public Capability Required { get; }

    /// <summary>
    /// Gets the contract it reached for.
    /// </summary>
    public string? ContractName { get; }
}
