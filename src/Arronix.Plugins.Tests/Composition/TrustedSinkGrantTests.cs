using System.Linq;
using Arronix.Plugins.Composition;
using Arronix.Plugins.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arronix.Plugins.Tests.Composition;

/// <summary>
/// The one privilege an operator grants rather than a package: reading the whole diagnostic stream.
/// </summary>
/// <remarks>
/// Checked when the host starts rather than when a package happens to ask. A misspelled identifier is a
/// grant that silently never applies, and the operator who wrote it believes they made one — so these run
/// the startup validator, which is what a generic host runs, and deliberately do not read the options
/// first.
/// </remarks>
[TestFixture]
public sealed class TrustedSinkGrantTests
{
    [Test]
    public void AWellFormedGrantSurvivesStartup()
    {
        using var provider = Compose("arronix.sink", "another.sink");

        var start = Startup(provider);

        start.Should().NotThrow();
        provider.GetRequiredService<IOptions<PluginRuntimeOptions>>().Value.TrustedSinks
            .Should().Equal(["arronix.sink", "another.sink"]);
    }

    [Test]
    public void AMisspelledIdentifierFailsStartupRatherThanSittingInert()
    {
        using var provider = Compose("Not A Plugin Id");

        Startup(provider).Should().Throw<OptionsValidationException>()
            .WithMessage("*well-formed extension identifier*");
    }

    [Test]
    public void ARepeatedEntryFailsStartup()
    {
        using var provider = Compose("arronix.sink", "arronix.sink");

        Startup(provider).Should().Throw<OptionsValidationException>()
            .WithMessage("*written twice*");
    }

    /// <summary>
    /// What a host does before anything reads a setting: run the validators registered to run at startup.
    /// Resolving the options instead would prove only that they are validated on first read, which an
    /// installation that never reads them would never do.
    /// </summary>
    private static Action Startup(IServiceProvider provider)
        => () => provider.GetRequiredService<IStartupValidator>().Validate();

    private static ServiceProvider Compose(params string[] trusted)
    {
        var settings = trusted
            .Select((id, index) => new KeyValuePair<string, string?>(
                $"{PluginRuntimeOptions.SectionName}:{nameof(PluginRuntimeOptions.TrustedSinks)}:{index}",
                id))
            .ToList();

        settings.Add(new KeyValuePair<string, string?>($"{PluginRuntimeOptions.SectionName}:RootFolder", "plugins"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixPlugins(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services.BuildServiceProvider();
    }
}
