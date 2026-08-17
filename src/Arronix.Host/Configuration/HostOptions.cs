using System.ComponentModel.DataAnnotations;

namespace Arronix.Host.Configuration;

/// <summary>
/// Operator control over how the host discovers and admits extensions.
/// </summary>
/// <remarks>
/// <para>
/// Zero extensions loading is a valid state, so nothing here is required: a host started against an empty
/// or absent extension folder serves an empty catalog rather than failing. That is deliberate — the
/// failure mode of a platform whose startup depends on third-party code is that one bad extension takes the
/// whole installation offline, and the surveyed applications each needed a "retry with extensions disabled"
/// escape hatch because of it.
/// </para>
/// <para>
/// <see cref="Disabled"/> exists so an operator can quarantine a specific extension by configuration
/// without deleting it, which keeps the extension's stored configuration intact while it is investigated.
/// </para>
/// </remarks>
public sealed class HostOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Host";

    /// <summary>
    /// Gets or sets the folder searched for extensions. Each immediate child folder that contains a
    /// manifest is one candidate.
    /// </summary>
    public string ExtensionFolder { get; set; } = "plugins";

    /// <summary>
    /// Gets or sets a value indicating whether extensions are discovered at all. Setting it to
    /// <see langword="false"/> starts the host with an empty catalog, which is the supported way to
    /// establish whether a fault is the platform's or an extension's.
    /// </summary>
    public bool LoadExtensions { get; set; } = true;

    /// <summary>
    /// Gets or sets how long the whole activation pass may take before it is abandoned. An extension that
    /// blocks in its configuration call cannot then hold the host's startup open indefinitely.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan ActivationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets the identifiers of extensions the operator has switched off. A disabled extension is reported
    /// as such rather than silently omitted, so an operator reading the extension list can tell the
    /// difference between "not installed" and "installed and switched off".
    /// </summary>
    public IList<string> Disabled { get; } = [];
}
