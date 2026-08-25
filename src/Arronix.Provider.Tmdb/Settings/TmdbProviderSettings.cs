using System;
using System.Collections.Generic;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Provider.Tmdb.Settings;

/// <summary>Declares this provider's configurable settings and reads them from a configured definition.</summary>
/// <remarks>
/// Credentials are operator supplied and travel only as a Bearer header. Endpoint overrides reject URI
/// components which could carry credentials or tracking data into request diagnostics.
/// </remarks>
public static class TmdbProviderSettings
{
    /// <summary>The field identifier for the TMDb API Read Access Token.</summary>
    public const string ReadAccessTokenField = "readAccessToken";

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
            FieldId = ReadAccessTokenField,
            Name = "API Read Access Token",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            Sensitivity = SettingSensitivity.Secret,
            Required = true,
            HelpText = "The TMDb API Read Access Token, sent as an Authorization: Bearer header.",
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
            HelpText = "The ISO 3166-1 country code used to select release dates.",
        },
    ];

    /// <summary>Reads and validates the settings of one configured definition.</summary>
    /// <param name="definition">The configured definition.</param>
    /// <returns>The resolved settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No Read Access Token is configured, or a configured endpoint is not an absolute <c>http</c>/<c>https</c>
    /// URL with a host, or it carries user info, a query string, or a fragment.
    /// </exception>
    public static TmdbSettingsValues Read(ProviderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.Settings.TryGetValue(ReadAccessTokenField, out var token) || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"Provider definition '{definition.Name}' has no '{ReadAccessTokenField}' configured.");
        }

        return new TmdbSettingsValues(
            token,
            ReadEndpointUri(definition, BaseUrlField, DefaultBaseUrl),
            ReadEndpointUri(definition, ImageBaseUrlField, DefaultImageBaseUrl),
            ReadRegion(definition));
    }

    /// <summary>Reads an absolute HTTP(S) endpoint and normalizes its path to end in a slash.</summary>
    private static Uri ReadEndpointUri(ProviderDefinition definition, string field, string fallback)
    {
        var text = ReadText(definition, field, fallback);

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host))
        {
            throw new InvalidOperationException(
                $"Provider definition '{definition.Name}' has an invalid '{field}': it must be an absolute http or https URL with a host.");
        }

        // Do not echo rejected text; its query or fragment may contain a secret.
        if (!string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new InvalidOperationException(
                $"Provider definition '{definition.Name}' has an invalid '{field}': it must not carry user info, a query string, or a fragment.");
        }

        var path = parsed.AbsolutePath.EndsWith('/') ? parsed.AbsolutePath : parsed.AbsolutePath + "/";

        return new UriBuilder(parsed) { Path = path }.Uri;
    }

    private static string ReadRegion(ProviderDefinition definition)
    {
        var region = ReadText(definition, RegionField, DefaultRegion);

        if (region.Length != 2 || !IsAsciiLetter(region[0]) || !IsAsciiLetter(region[1]))
        {
            throw new InvalidOperationException(
                $"Provider definition '{definition.Name}' has an invalid '{RegionField}': it must be a two-letter ISO 3166-1 country code.");
        }

        return region.ToUpperInvariant();
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static string ReadText(ProviderDefinition definition, string field, string fallback) =>
        definition.Settings.TryGetValue(field, out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : fallback;
}

/// <summary>The resolved settings of one configured TMDb definition.</summary>
/// <param name="ReadAccessToken">The TMDb API Read Access Token, sent as an <c>Authorization: Bearer</c> header.</param>
/// <param name="BaseUrl">
/// The API base URL: absolute, <c>http</c>/<c>https</c>, a non-empty host, no user info/query/fragment,
/// trailing-slash terminated.
/// </param>
/// <param name="ImageBaseUrl">
/// The artwork image base URL: absolute, <c>http</c>/<c>https</c>, a non-empty host, no user
/// info/query/fragment, trailing-slash terminated.
/// </param>
/// <param name="Region">The ISO 3166-1 region used to select release dates.</param>
public sealed record TmdbSettingsValues(string ReadAccessToken, Uri BaseUrl, Uri ImageBaseUrl, string Region);
