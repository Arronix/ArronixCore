using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// One configured instance of a provider.
/// </summary>
/// <remarks>
/// <para>
/// Owned by the host and handed to the provider on every call. Settings are a string map keyed by the
/// descriptor's field identifiers rather than a typed object, because the platform stores, redacts,
/// validates and serializes them without knowing what any of them mean.
/// </para>
/// <para>
/// A definition whose implementation has gone away is retained and marked, never deleted. One surveyed
/// application deletes such rows on start-up, which under an extension model means uninstalling an
/// extension destroys the user's configuration.
/// </para>
/// </remarks>
public sealed record ProviderDefinition
{
    /// <summary>
    /// Gets the host-assigned identifier of this definition.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the provider implementation this definition configures.
    /// </summary>
    public required ProviderId Provider { get; init; }

    /// <summary>
    /// Gets the kind of external service, carried so that a definition can be routed without resolving
    /// its implementation.
    /// </summary>
    public required ProviderFamily Family { get; init; }

    /// <summary>
    /// Gets the name the user gave this definition.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether the definition is in use.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the setting values, keyed by <see cref="SettingsField.FieldId"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }

    /// <summary>
    /// Gets the platform tags applied to this definition.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets the media kinds the user has narrowed this definition to. Empty means every kind the
    /// provider can serve.
    /// </summary>
    public IReadOnlyList<MediaKindId> MediaKinds { get; init; } = [];

    /// <summary>
    /// Gets the definition's rank among its peers, where a lower number is preferred.
    /// </summary>
    public int Priority { get; init; } = 25;

    /// <summary>
    /// Gets whether the definition's implementation is still present.
    /// </summary>
    public DefinitionState State { get; init; } = DefinitionState.Active;

    /// <summary>
    /// Gets the message an operator should see about this definition, when there is one.
    /// </summary>
    public ProviderMessage? Message { get; init; }
}

/// <summary>
/// Whether a definition's implementation is still present.
/// </summary>
public enum DefinitionState
{
    /// <summary>The implementation is loaded and the definition is usable.</summary>
    Active = 0,

    /// <summary>The implementation is absent. The definition is retained and quarantined.</summary>
    Orphaned = 1
}

/// <summary>
/// Something an operator should know about a provider definition.
/// </summary>
/// <param name="Text">The message.</param>
/// <param name="Severity">How much it matters.</param>
public sealed record ProviderMessage(string Text, ProviderMessageSeverity Severity);

/// <summary>
/// How much a provider message matters.
/// </summary>
public enum ProviderMessageSeverity
{
    /// <summary>Worth knowing.</summary>
    Info = 0,

    /// <summary>Worth acting on eventually.</summary>
    Warning = 1,

    /// <summary>The definition is not working.</summary>
    Error = 2
}
