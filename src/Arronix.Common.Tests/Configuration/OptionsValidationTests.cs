using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Arronix.Common.Composition;
using Arronix.Common.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Tests.Configuration;

/// <summary>
/// Covers the platform's options types: that they bind, that an invalid value is refused as the host starts
/// rather than when the value is first read, and that every name the platform presents to the world is
/// derived from the one place the product name enters it.
/// </summary>
[TestFixture]
public class OptionsValidationTests
{
    private const string ApplicationNameKey = "Arronix:Identity:ApplicationName";

    [Test]
    public void AddArronixCommon_RejectsAMissingCollection()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.That(
            () => ArronixCommonServiceCollectionExtensions.AddArronixCommon(null!, configuration),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void AddArronixCommon_RejectsAMissingConfiguration()
    {
        Assert.That(
            () => new ServiceCollection().AddArronixCommon(null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void AddArronixCommon_RegistersTheSharedClock()
    {
        using var provider = BuildProvider(ValidIdentity());

        Assert.That(provider.GetService<TimeProvider>(), Is.SameAs(TimeProvider.System));
    }

    [Test]
    public void StartupValidation_RefusesAnIdentityWithNoApplicationName()
    {
        using var provider = BuildProvider();

        var validator = provider.GetRequiredService<IStartupValidator>();

        Assert.That(validator.Validate, Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public void StartupValidation_AcceptsACompleteConfiguration()
    {
        using var provider = BuildProvider(ValidIdentity());

        var validator = provider.GetRequiredService<IStartupValidator>();

        Assert.That(validator.Validate, Throws.Nothing);
    }

    [TestCase("", TestName = "Identity rejects an empty name")]
    [TestCase("4", TestName = "Identity rejects a name that does not start with a letter")]
    [TestCase("bad name!", TestName = "Identity rejects a name with a character no path can hold")]
    [TestCase("trailing ", TestName = "Identity rejects a name with a trailing separator")]
    [TestCase("double  space", TestName = "Identity rejects a name with a repeated separator")]
    public void Identity_RefusesANameThatCannotBecomeAPathOrAHeaderToken(string applicationName)
    {
        using var provider = BuildProvider(Setting(ApplicationNameKey, applicationName));

        Assert.That(
            () => provider.GetRequiredService<IOptions<HostIdentityOptions>>().Value,
            Throws.TypeOf<OptionsValidationException>());
    }

    [TestCase("Northwind", "northwind")]
    [TestCase("Aurora Deck", "aurora-deck")]
    [TestCase("Deck_9", "deck-9")]
    public void Identity_DerivesEveryNameFromTheApplicationName(string applicationName, string expectedToken)
    {
        using var provider = BuildProvider(Setting(ApplicationNameKey, applicationName));

        var identity = provider.GetRequiredService<IOptions<HostIdentityOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(identity.FileNameToken, Is.EqualTo(expectedToken));
            Assert.That(identity.PidFileName, Is.EqualTo(expectedToken + ".pid"));
            Assert.That(identity.LogFileNameStem, Is.EqualTo(expectedToken));
            Assert.That(identity.ProcessName, Is.EqualTo(expectedToken));
            Assert.That(identity.ConsoleProcessName, Is.EqualTo(expectedToken + ".console"));
            Assert.That(identity.DataFolderName, Is.EqualTo(applicationName));
            Assert.That(identity.ServiceName, Is.EqualTo(applicationName));
            Assert.That(identity.DisplayName, Is.EqualTo(applicationName));
        });
    }

    [Test]
    public void Identity_ProducesAUserAgentTokenWithNoSpaceInIt()
    {
        using var provider = BuildProvider(Setting(ApplicationNameKey, "Aurora Deck"));

        var identity = provider.GetRequiredService<IOptions<HostIdentityOptions>>().Value;

        Assert.That(identity.UserAgentProductToken, Is.EqualTo("AuroraDeck"));
    }

    [Test]
    public void Identity_PrefersTheInstanceNameForDisplayOnly()
    {
        using var provider = BuildProvider(
            Setting(ApplicationNameKey, "Northwind"),
            Setting("Arronix:Identity:InstanceName", "Second Floor"));

        var identity = provider.GetRequiredService<IOptions<HostIdentityOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(identity.DisplayName, Is.EqualTo("Second Floor"));
            Assert.That(identity.PidFileName, Is.EqualTo("northwind.pid"));
        });
    }

    [Test]
    public void Logging_RefusesMoreRotatedFilesThanTheCeiling()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:Logging:RotatedFileCount", "5000"));

        Assert.That(
            () => provider.GetRequiredService<IOptions<LoggingOptions>>().Value,
            Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public void Logging_BindsLevelsAndFormatFromConfiguration()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:Logging:Level", "Debug"),
            Setting("Arronix:Logging:ConsoleFormat", "Clef"));

        var logging = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(logging.Level, Is.EqualTo(LogLevel.Debug));
            Assert.That(logging.ConsoleFormat, Is.EqualTo(ConsoleLogFormat.Clef));
        });
    }

    [Test]
    public void FileSystem_RefusesAWriteProbeNameThatCouldEscapeTheFolderItProbes()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:FileSystem:WriteProbeFileName", "../probe.tmp"));

        Assert.That(
            () => provider.GetRequiredService<IOptions<FileSystemOptions>>().Value,
            Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public void FileSystem_CarriesNoVendorFolderNameByDefault()
    {
        using var provider = BuildProvider(ValidIdentity());

        var fileSystem = provider.GetRequiredService<IOptions<FileSystemOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(fileSystem.IgnoredFileNames, Is.Empty);
            Assert.That(fileSystem.IgnoredFileNameSuffixes, Is.Empty);
            Assert.That(fileSystem.ExcludedFolderNames, Does.Not.Contain("@eadir"));
        });
    }

    [Test]
    public void Http_RefusesMoreRedirectsThanTheCeiling()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:Http:MaxAutomaticRedirects", "40"));

        Assert.That(
            () => provider.GetRequiredService<IOptions<HttpClientOptions>>().Value,
            Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public void Http_BindsATimeoutWrittenInTheInvariantFormat()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:Http:RequestTimeout", "00:00:45"));

        var http = provider.GetRequiredService<IOptions<HttpClientOptions>>().Value;

        Assert.That(http.RequestTimeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void Proxy_RefusesToBeEnabledWithoutAHost()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:Http:Proxy:Enabled", "true"));

        Assert.That(
            () => provider.GetRequiredService<IOptions<ProxyOptions>>().Value,
            Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public void Proxy_RefusesAPasswordWithNoUserNameToPresentItWith()
    {
        using var provider = BuildProvider(
            ValidIdentity(),
            Setting("Arronix:Http:Proxy:Enabled", "true"),
            Setting("Arronix:Http:Proxy:Host", "proxy.internal"),
            Setting("Arronix:Http:Proxy:Password", "secret"));

        Assert.That(
            () => provider.GetRequiredService<IOptions<ProxyOptions>>().Value,
            Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public void Proxy_IsDisabledUntilConfigured()
    {
        using var provider = BuildProvider(ValidIdentity());

        var proxy = provider.GetRequiredService<IOptions<ProxyOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(proxy.Enabled, Is.False);
            Assert.That(proxy.BypassList, Is.Empty);
        });
    }

    [Test]
    public void Redaction_IsOnWithoutAnyConfiguration()
    {
        using var provider = BuildProvider(ValidIdentity());

        var redaction = provider.GetRequiredService<IOptions<RedactionOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(redaction.Enabled, Is.True);
            Assert.That(redaction.MaskNetworkAddresses, Is.True);
            Assert.That(redaction.Replacement, Is.Not.Empty);
        });
    }

    [Test]
    public void EveryOptionsType_IsBoundAndValidatedByTheEntryPoint()
    {
        using var provider = BuildProvider(ValidIdentity());

        Assert.Multiple(() =>
        {
            foreach (var optionsType in OptionsTypes())
            {
                var accessor = provider.GetService(typeof(IOptions<>).MakeGenericType(optionsType));

                Assert.That(
                    accessor,
                    Is.Not.Null,
                    $"{optionsType.Name} is not registered by AddArronixCommon.");
            }
        });
    }

    [Test]
    public void NoOptionsProperty_IsNullable()
    {
        var nullabilityContext = new NullabilityInfoContext();

        Assert.Multiple(() =>
        {
            foreach (var property in OptionsTypes().SelectMany(static type => type.GetProperties()))
            {
                var nullability = nullabilityContext.Create(property);

                Assert.That(
                    nullability.ReadState,
                    Is.EqualTo(NullabilityState.NotNull),
                    $"{property.DeclaringType?.Name}.{property.Name} is nullable; an option carries a value, not the absence of one.");
            }
        });
    }

    [Test]
    public void EveryOptionsType_NamesItsOwnConfigurationSection()
    {
        Assert.Multiple(() =>
        {
            foreach (var optionsType in OptionsTypes())
            {
                var section = optionsType.GetField("SectionName", BindingFlags.Public | BindingFlags.Static);

                Assert.That(
                    section?.GetRawConstantValue() as string,
                    Is.Not.Null.And.Not.Empty,
                    $"{optionsType.Name} does not declare the section it binds from.");
            }
        });
    }

    private static IEnumerable<Type> OptionsTypes() =>
        typeof(HostIdentityOptions).Assembly
            .GetExportedTypes()
            .Where(static type => type.IsClass
                && type.Namespace == typeof(HostIdentityOptions).Namespace
                && type.Name.EndsWith("Options", StringComparison.Ordinal))
            .OrderBy(static type => type.Name, StringComparer.Ordinal);

    private static KeyValuePair<string, string?> Setting(string key, string value) => new(key, value);

    private static KeyValuePair<string, string?> ValidIdentity() => Setting(ApplicationNameKey, "Northwind");

    private static ServiceProvider BuildProvider(params KeyValuePair<string, string?>[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new ServiceCollection()
            .AddArronixCommon(configuration)
            .BuildServiceProvider();
    }
}
