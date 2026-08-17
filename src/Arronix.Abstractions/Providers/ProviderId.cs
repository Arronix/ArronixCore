using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Plugins;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Identifies one provider implementation, qualified by the extension that supplies it.
/// </summary>
/// <remarks>
/// <para>
/// The surveyed aggregator resolves provider implementations by their type name, case-insensitively — a
/// fragile identity that survives only because one process hosts one application. A unified host will see
/// name collisions across extensions that no surveyed application ever faced, so the identity is
/// qualified from the start.
/// </para>
/// <para>
/// Minted by the registry from the declaring extension's identifier and the local identifier the
/// extension chose, never by the extension itself: an extension cannot claim another's namespace if it
/// never writes the qualified form.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct ProviderId
{
    private const char Separator = ':';

    private ProviderId(PluginId plugin, string local)
    {
        Plugin = plugin;
        Local = local;
    }

    /// <summary>
    /// Gets the qualified form, <c>plugin:local</c>.
    /// </summary>
    public string Value => $"{Plugin}{Separator}{Local}";

    /// <summary>
    /// Gets the extension that supplies the provider.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the identifier the extension gave the provider, unique within that extension.
    /// </summary>
    public string Local { get; }

    /// <summary>
    /// Creates a provider identifier.
    /// </summary>
    /// <param name="plugin">The extension supplying the provider.</param>
    /// <param name="local">The identifier within that extension.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="local"/> is <see langword="null"/>, empty or white space.</exception>
    public static ProviderId Create(PluginId plugin, string local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(local);
        return new ProviderId(plugin, local);
    }

    /// <summary>
    /// Attempts to read the qualified form.
    /// </summary>
    /// <param name="value">The text to read, for example <c>"example:acme"</c>.</param>
    /// <param name="id">The identifier when the text was well-formed; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the text was well-formed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out ProviderId id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf(Separator, StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        if (!PluginId.TryParse(value[..separator], out var plugin))
        {
            return false;
        }

        var local = value[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(local))
        {
            return false;
        }

        id = new ProviderId(plugin, local);
        return true;
    }

    /// <summary>
    /// Gets the qualified form, which <see cref="TryParse(string?, out ProviderId)"/> reads back.
    /// </summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => Value;
}
