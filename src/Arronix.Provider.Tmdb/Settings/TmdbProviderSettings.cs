using System;
using System.Collections.Generic;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Provider.Tmdb.Settings;

/// <summary>Declares this provider's configurable settings and reads them from a configured definition.</summary>
/// <remarks>
/// Provider-owned configuration: nothing here is a media, format, or Host concern. No credential ships
/// with this package or its tests; a real TMDb API key is supplied by whoever configures a definition.
/// </remarks>
public static class TmdbProviderSettings
{
    /// <summary>The field identifier for the TMDb v3 API key.</summary>
    public const string ApiKeyField = "apiKey";

    /// <summary>The field identifier for the API base URL.</summary>
    public const string BaseUrlField = "baseUrl";

    /// <summary>The field identifier for the artwork image base URL.</summary>
    public const string ImageBaseUrlField = "imageBaseUrl";

    /// <summary>The field identifier for the release region.</summary>
    public const string RegionField = "region";

    private const string DefaultBaseUrl = "https://api.themoviedb.org/3/";
    private const string DefaultImageBaseUrl = "https://image.tmdb.org/t/p/original/";
    private const string DefaultRegion = "US";

    /// <summary>Gets the settings declared for definitions of this provider.</summary>
    public static IReadOnlyList<SettingsField> Fields { get; } =
    [
        new SettingsField
        {
            FieldId = ApiKeyField,
            Name = "API key",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            Sensitivity = SettingSensitivity.Secret,
            Required = true,
            HelpText = "The TMDb v3 API key used to authenticate outbound requests.",
        },
        new SettingsField
        {
            FieldId = BaseUrlField,
            Name = "API base URL",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Endpoint,
            DefaultValue = DefaultBaseUrl,
            Advanced = true,
        },
        new SettingsField
        {
            FieldId = ImageBaseUrlField,
            Name = "Image base URL",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Endpoint,
            DefaultValue = DefaultImageBaseUrl,
            Advanced = true,
        },
        new SettingsField
        {
            FieldId = RegionField,
            Name = "Release region",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            DefaultValue = DefaultRegion,
            HelpText = "The ISO 3166-1 country code used to select release dates and certification.",
        },
    ];

    /// <summary>Reads and validates the settings of one configured definition.</summary>
    /// <param name="definition">The configured definition.</param>
    /// <returns>The resolved settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured.</exception>
    public static TmdbSettingsValues Read(ProviderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.Settings.TryGetValue(ApiKeyField, out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Provider definition '{definition.Name}' has no '{ApiKeyField}' configured.");
        }

        return new TmdbSettingsValues(
            apiKey,
            ReadUri(definition, BaseUrlField, DefaultBaseUrl),
            ReadUri(definition, ImageBaseUrlField, DefaultImageBaseUrl),
            ReadText(definition, RegionField, DefaultRegion));
    }

    private static Uri ReadUri(ProviderDefinition definition, string field, string fallback) =>
        new(ReadText(definition, field, fallback), UriKind.Absolute);

    private static string ReadText(ProviderDefinition definition, string field, string fallback) =>
        definition.Settings.TryGetValue(field, out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : fallback;
}

/// <summary>The resolved settings of one configured TMDb definition.</summary>
/// <param name="ApiKey">The TMDb v3 API key.</param>
/// <param name="BaseUrl">The API base URL, trailing-slash terminated.</param>
/// <param name="ImageBaseUrl">The artwork image base URL, trailing-slash terminated.</param>
/// <param name="Region">The ISO 3166-1 region used to select release dates and certification.</param>
public sealed record TmdbSettingsValues(string ApiKey, Uri BaseUrl, Uri ImageBaseUrl, string Region);
