using System.IO;
using System.Linq;
using Arronix.Abstractions.Serialization;
using Arronix.Plugins.Composition;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable ARX0010 // Serialization contracts are experimental; the composition needs one registered.

namespace Arronix.Plugins.Tests.Composition;

/// <summary>
/// Registration composes, and composing it loads nothing.
/// </summary>
[TestFixture]
public sealed class CompositionTests
{
    private static ServiceProvider Build(params KeyValuePair<string, string?>[] settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        // Stands in for the host: the loader takes the platform's serializer and the framework's logging,
        // both of which a real host has registered long before extensions are composed.
        services.TryAddSingleton<IJsonSerializer, StubJsonSerializer>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddArronixPlugins(configuration);

        return services.BuildServiceProvider();
    }

    [Test]
    public void TheLoaderResolvesEvenThoughTheHostHasBuiltAlmostNothingElse()
    {
        using var provider = Build();

        var resolve = () => provider.GetRequiredService<PluginLoader>();

        resolve.Should().NotThrow(
            "the gated platform services are optional so that a host that has not built a subsystem yet still composes");
    }

    [Test]
    public void TheRuntimeRegistryIsOneInstanceHoweverItIsAskedFor()
    {
        using var provider = Build();

        provider.GetRequiredService<IPluginRuntimeRegistry>()
            .Should().BeSameAs(provider.GetRequiredService<PluginRuntimeRegistry>());
    }

    [Test]
    public void TheTokenRegistryIsShared()
    {
        using var provider = Build();

        provider.GetRequiredService<TokenRegistry>()
            .Should().BeSameAs(provider.GetRequiredService<TokenRegistry>());
    }

    [Test]
    public void OptionsBindFromTheSectionTheTypeNames()
    {
        using var provider = Build(
            new KeyValuePair<string, string?>("Arronix:Plugins:RootFolder", "/somewhere/extensions"),
            new KeyValuePair<string, string?>("Arronix:Plugins:Enabled", "false"),
            new KeyValuePair<string, string?>("Arronix:Plugins:Disabled:0", "example"),
            new KeyValuePair<string, string?>("Arronix:Plugins:Access:example:GrantedRoots:0", "/media"),
            new KeyValuePair<string, string?>("Arronix:Plugins:Access:example:DeniedHosts:0", "denied.example"));

        var options = provider.GetRequiredService<IOptions<PluginRuntimeOptions>>().Value;

        options.RootFolder.Should().Be("/somewhere/extensions");
        options.Enabled.Should().BeFalse();
        options.Disabled.Should().Equal("example");
        options.AccessFor("example").GrantedRoots.Should().Equal("/media");
        options.AccessFor("example").DeniedHosts.Should().Equal("denied.example");
    }

    [Test]
    public void AnExtensionWithNoConfiguredGrantGetsAnEmptyOneRatherThanNull()
    {
        using var provider = Build();

        var access = provider.GetRequiredService<IOptions<PluginRuntimeOptions>>().Value.AccessFor("unconfigured");

        access.GrantedRoots.Should().BeEmpty();
        access.AllowedHosts.Should().BeEmpty();
        access.DeniedHosts.Should().BeEmpty();
    }

    [Test]
    public void ComposingLoadsNothing()
    {
        using var provider = Build();

        provider.GetRequiredService<PluginLoader>();

        provider.GetRequiredService<IPluginRuntimeRegistry>().All.Should().BeEmpty(
            "an extension must never be able to observe a half-built host, so loading is the host's decision and not a side effect of registration");
    }

    [Test]
    public void RegistrationIsRefusedWithoutAServiceCollectionOrAConfiguration()
    {
        var withoutServices = () => ArronixPluginsServiceCollectionExtensions.AddArronixPlugins(null!, new ConfigurationBuilder().Build());
        var withoutConfiguration = () => new ServiceCollection().AddArronixPlugins(null!);

        withoutServices.Should().Throw<ArgumentNullException>();
        withoutConfiguration.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TheEditorSchemaShipsBesideTheAssembly()
    {
        var schema = Path.Combine(
            Path.GetDirectoryName(typeof(PluginManifestReader).Assembly.Location)!,
            "Manifest",
            "plugin.schema.json");

        File.Exists(schema).Should().BeTrue();
        File.ReadAllText(schema).Should().Contain(
            "EDITOR AID ONLY",
            "the schema document is never the runtime gate and must say so where someone will read it");
    }

    [Test]
    public void TheLoaderTakesThePlatformServicesBundleRatherThanReachingIntoTheContainer()
    {
        typeof(PluginLoader)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should().NotContain(
                typeof(IServiceProvider),
                "resolving from the container at load time is exactly the pattern the closed registration surface exists to replace");
    }
}
