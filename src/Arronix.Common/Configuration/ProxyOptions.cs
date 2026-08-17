using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Configuration;

/// <summary>
/// The forward proxy, if any, that outbound requests are routed through.
/// </summary>
/// <remarks>
/// <para>
/// The credentials on this type are the only secret the platform's own configuration holds. They are never
/// projected into an identity, a cache key or a log line; anything that needs to tell two proxy
/// configurations apart compares the configuration by value instead of building a string from its fields.
/// </para>
/// <para>
/// This type is the bound configuration shape. The resolved, immutable form the outbound stack works with
/// is a separate type owned by that stack, and it gains a protocol selector when it lands.
/// </para>
/// </remarks>
public sealed class ProxyOptions : IValidatableObject
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Http:Proxy";

    /// <summary>
    /// Gets or sets a value indicating whether outbound requests are routed through the proxy.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the host name or address of the proxy. Required once <see cref="Enabled"/> is set.
    /// </summary>
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port the proxy listens on.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Gets or sets the user name presented to a proxy that requires authentication. Empty means the proxy
    /// is used anonymously.
    /// </summary>
    [StringLength(255)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password presented alongside <see cref="Username"/>.
    /// </summary>
    [StringLength(255)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether requests to addresses on the local network bypass the proxy.
    /// </summary>
    public bool BypassLocalAddresses { get; set; } = true;

    /// <summary>
    /// Gets the host patterns that bypass the proxy. A leading wildcard matches any number of leading
    /// labels, so one entry covers a whole domain.
    /// </summary>
    public IList<string> BypassList { get; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && string.IsNullOrWhiteSpace(Host))
        {
            yield return new ValidationResult(
                "A proxy host is required when the proxy is enabled.",
                [nameof(Host)]);
        }

        if (Password.Length > 0 && Username.Length == 0)
        {
            yield return new ValidationResult(
                "A proxy password without a user name cannot be presented to a proxy.",
                [nameof(Username)]);
        }
    }
}
