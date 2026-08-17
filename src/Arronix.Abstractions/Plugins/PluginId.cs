using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Plugins;

/// <summary>
/// Identifies one installed extension.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is the root of every namespace an extension owns: its data folder, its cache
/// partitions, its event namespace, its provider identifiers and its telemetry attribution all derive
/// from it. That is why it is branded and why its form is constrained rather than free — an identifier
/// that can contain a path separator is an identifier that can escape its own folder.
/// </para>
/// <para>
/// The permitted form is lower-case alphanumeric segments separated by dots, starting with a letter,
/// which is reverse-domain friendly without requiring it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Plugins, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct PluginId
{
    private PluginId(string value) => Value = value;

    /// <summary>
    /// Gets the identifier text.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates an extension identifier.
    /// </summary>
    /// <param name="value">The identifier text.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a well-formed identifier.</exception>
    public static PluginId FromString(string value)
    {
        if (!TryParse(value, out var id))
        {
            throw new ArgumentException(
                "An extension identifier must be lower-case alphanumeric segments separated by dots, starting with a letter.",
                nameof(value));
        }

        return id;
    }

    /// <summary>
    /// Attempts to create an extension identifier.
    /// </summary>
    /// <param name="value">The identifier text.</param>
    /// <param name="id">The identifier when the text was well-formed; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the text was well-formed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out PluginId id)
    {
        id = default;

        if (string.IsNullOrEmpty(value) || !IsLower(value[0]))
        {
            return false;
        }

        var previousWasSeparator = false;
        foreach (var character in value)
        {
            if (character == '.')
            {
                if (previousWasSeparator)
                {
                    return false;
                }

                previousWasSeparator = true;
                continue;
            }

            if (!IsLower(character) && !IsDigit(character))
            {
                return false;
            }

            previousWasSeparator = false;
        }

        if (previousWasSeparator)
        {
            return false;
        }

        id = new PluginId(value);
        return true;
    }

    /// <summary>
    /// Gets the identifier text, or an empty string for the default value.
    /// </summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => Value ?? string.Empty;

    private static bool IsLower(char character) => character is >= 'a' and <= 'z';

    private static bool IsDigit(char character) => character is >= '0' and <= '9';
}
