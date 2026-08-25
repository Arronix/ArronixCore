using System;
using System.Collections.Generic;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Provider.Tmdb.Settings;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Settings;

[TestFixture]
public sealed class TmdbProviderSettingsTests
{
    [Test]
    public void Read_uses_trailing_slash_terminated_defaults_when_nothing_is_configured()
    {
        var settings = TmdbProviderSettings.Read(Definition());

        settings.BaseUrl.ToString().Should().EndWith("/");
        settings.ImageBaseUrl.ToString().Should().EndWith("/");
        settings.Region.Should().Be("US");
    }

    [Test]
    public void Read_normalizes_a_valid_region_to_uppercase()
    {
        var settings = TmdbProviderSettings.Read(Definition((TmdbProviderSettings.RegionField, "au")));

        settings.Region.Should().Be("AU");
    }

    [TestCase("A")]
    [TestCase("AUS")]
    [TestCase("A1")]
    [TestCase(" A")]
    public void Read_rejects_a_noncanonical_region(string region)
    {
        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.RegionField, region))))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TmdbProviderSettings.RegionField}*");
    }

    [Test]
    public void Read_appends_a_trailing_slash_to_a_configured_base_url_that_is_missing_one()
    {
        var settings = TmdbProviderSettings.Read(Definition(
            (TmdbProviderSettings.BaseUrlField, "https://api.example.test/3")));

        // Without normalization, System.Uri's combining rule would drop "3" entirely: relative combination
        // against a base lacking a trailing slash discards its last path segment.
        settings.BaseUrl.Should().Be(new Uri("https://api.example.test/3/"));
        new Uri(settings.BaseUrl, "movie/603").Should().Be(new Uri("https://api.example.test/3/movie/603"));
    }

    [Test]
    public void Read_appends_a_trailing_slash_to_a_configured_image_base_url_that_is_missing_one()
    {
        var settings = TmdbProviderSettings.Read(Definition(
            (TmdbProviderSettings.ImageBaseUrlField, "https://images.example.test/t/p/original")));

        settings.ImageBaseUrl.Should().Be(new Uri("https://images.example.test/t/p/original/"));
        new Uri(settings.ImageBaseUrl, "poster.jpg").Should()
            .Be(new Uri("https://images.example.test/t/p/original/poster.jpg"));
    }

    [Test]
    public void Read_leaves_an_already_trailing_slash_terminated_url_unchanged()
    {
        var settings = TmdbProviderSettings.Read(Definition(
            (TmdbProviderSettings.BaseUrlField, "https://api.example.test/3/")));

        settings.BaseUrl.Should().Be(new Uri("https://api.example.test/3/"));
    }

    [TestCase("not-a-url")]
    [TestCase("ftp://api.example.test/3")]
    [TestCase("//api.example.test/3")]
    public void Read_rejects_a_base_url_that_is_not_an_absolute_http_or_https_url(string invalidUrl)
    {
        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.BaseUrlField, invalidUrl))))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TmdbProviderSettings.BaseUrlField}*");
    }

    [TestCase("not-a-url")]
    [TestCase("ftp://images.example.test/t/p/original")]
    public void Read_rejects_an_image_base_url_that_is_not_an_absolute_http_or_https_url(string invalidUrl)
    {
        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.ImageBaseUrlField, invalidUrl))))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TmdbProviderSettings.ImageBaseUrlField}*");
    }

    [TestCase("https://api.example.test/3?foo=bar", TestName = "{m}(query)")]
    [TestCase("https://api.example.test/3#section", TestName = "{m}(fragment)")]
    [TestCase("https://user:pass@api.example.test/3/", TestName = "{m}(user_info)")]
    public void Read_rejects_a_base_url_carrying_user_info_query_or_fragment(string invalidUrl)
    {
        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.BaseUrlField, invalidUrl))))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TmdbProviderSettings.BaseUrlField}*");
    }

    [TestCase("https://images.example.test/t/p/original?foo=bar", TestName = "{m}(query)")]
    [TestCase("https://images.example.test/t/p/original#section", TestName = "{m}(fragment)")]
    [TestCase("https://user:pass@images.example.test/t/p/original/", TestName = "{m}(user_info)")]
    public void Read_rejects_an_image_base_url_carrying_user_info_query_or_fragment(string invalidUrl)
    {
        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.ImageBaseUrlField, invalidUrl))))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TmdbProviderSettings.ImageBaseUrlField}*");
    }

    [Test]
    public void Read_rejects_a_base_url_query_secret_and_never_echoes_it_in_the_exception()
    {
        const string secret = "leaked-tracking-token-9f3ac72e";

        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.BaseUrlField, $"https://api.example.test/3?ref={secret}"))))
            .Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain(secret);
    }

    [Test]
    public void Read_rejects_an_image_base_url_query_secret_and_never_echoes_it_in_the_exception()
    {
        const string secret = "leaked-tracking-token-b81de904";

        FluentActions.Invoking(() => TmdbProviderSettings.Read(Definition(
                (TmdbProviderSettings.ImageBaseUrlField, $"https://images.example.test/t/p/original?ref={secret}"))))
            .Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain(secret);
    }

    [Test]
    public void Read_rejects_a_definition_with_no_configured_token()
    {
        var definition = new ProviderDefinition
        {
            Id = 1,
            Provider = ProviderId.Create(PluginId.FromString("tmdb"), "tmdb-movies"),
            Family = ProviderFamily.Cataloger,
            Name = "No token",
            Settings = new Dictionary<string, string>(),
        };

        FluentActions.Invoking(() => TmdbProviderSettings.Read(definition))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TmdbProviderSettings.ReadAccessTokenField}*");
    }

    private static ProviderDefinition Definition(params (string Field, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string> { [TmdbProviderSettings.ReadAccessTokenField] = "token" };

        foreach (var (field, value) in overrides)
        {
            settings[field] = value;
        }

        return new ProviderDefinition
        {
            Id = 1,
            Provider = ProviderId.Create(PluginId.FromString("tmdb"), "tmdb-movies"),
            Family = ProviderFamily.Cataloger,
            Name = "TMDb test definition",
            Settings = settings,
        };
    }
}
