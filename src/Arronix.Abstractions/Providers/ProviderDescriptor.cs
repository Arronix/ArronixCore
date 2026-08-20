
namespace Arronix.Abstractions.Providers;

/// <summary>
/// Declares what one provider implementation is and how it is configured.
/// </summary>
/// <remarks>
/// The descriptor is the implementation's declaration; a <see cref="ProviderDefinition"/> is one
/// configured instance of it. Keeping them apart is what makes a provider stateless: the implementation
/// holds no configuration, so two definitions of the same provider cannot interfere with each other.
/// </remarks>
public sealed record ProviderDescriptor
{
    /// <summary>
    /// Gets the identifier within the declaring extension. The registry qualifies it.
    /// </summary>
    public required string LocalId { get; init; }

    /// <summary>
    /// Gets the kind of external service this provider integrates with.
    /// </summary>
    public required ProviderFamily Family { get; init; }

    /// <summary>
    /// Gets the provider's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a sentence describing what the provider is.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the settings a definition of this provider carries.
    /// </summary>
    public required IReadOnlyList<SettingsField> Settings { get; init; }

    /// <summary>
    /// Gets the transfer protocols the provider deals in, where that is meaningful for its family.
    /// </summary>
    public IReadOnlyList<DownloadProtocol> Protocols { get; init; } = [];

    /// <summary>
    /// Gets the ready-made configurations offered as a starting point.
    /// </summary>
    public IReadOnlyList<ProviderPreset> Presets { get; init; } = [];

    /// <summary>
    /// Gets further reading about the service being integrated with.
    /// </summary>
    public Uri? InfoLink { get; init; }
}

/// <summary>
/// A ready-made set of setting values offered as a starting point for a definition.
/// </summary>
/// <param name="PresetId">The identifier of the preset.</param>
/// <param name="Name">The preset's display name.</param>
/// <param name="Settings">The setting values, keyed by <see cref="SettingsField.FieldId"/>.</param>
public sealed record ProviderPreset(
    string PresetId,
    string Name,
    IReadOnlyDictionary<string, string> Settings);
