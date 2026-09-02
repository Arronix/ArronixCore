using System.IO;
using Arronix.Api.Configuration;
using Arronix.Api.Hosting;
using Arronix.Common.Installation;
using Arronix.Host.Configuration;
using Arronix.Plugins.Configuration;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;

namespace Arronix.Api.Tests.Installation;

/// <summary>
/// One installation root decides every path an installation owns.
/// </summary>
/// <remarks>
/// These cases are the contract between the composer that writes an installation and the server that runs
/// inside one. They are asserted against the real configuration manager rather than a stand-in, because the
/// property that matters is a precedence one and precedence belongs to the configuration system.
/// </remarks>
[TestFixture]
internal sealed class InstallationConfigurationTests
{
    private const string PackagesRootKey = PluginRuntimeOptions.SectionName + ":RootFolder";
    private const string PackageStateKey = PluginRuntimeOptions.SectionName + ":StateFolder";
    private const string StoreKey = StoreOptions.SectionName + ":DataSource";
    private const string ClientRootKey = ApiOptions.SectionName + ":ClientRoot";

    [Test]
    public void WithNoInstallationRootNothingIsDerived()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ClientRootKey] = "wwwroot",
        });

        var layout = configuration.AddArronixInstallation(Path.GetTempPath());

        using var assertions = new AssertionScope();
        layout.Should().BeNull();
        configuration[ClientRootKey].Should().Be("wwwroot");
        configuration[PackagesRootKey].Should().BeNull();
        configuration[StoreKey].Should().BeNull();
    }

    [Test]
    public void AnInstallationRootDerivesEveryPathTheInstallationOwns()
    {
        var root = Path.Combine(Path.GetTempPath(), "arronix-installation-configuration");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InstallationConfiguration.RootKey] = root,
        });

        var layout = configuration.AddArronixInstallation(Path.GetTempPath());

        using var assertions = new AssertionScope();
        layout.Should().NotBeNull();
        configuration[PackagesRootKey].Should().Be(layout!.PackagesFolder);
        configuration[PackageStateKey].Should().Be(layout.PackageStateFolder);
        configuration[StoreKey].Should().Be(layout.StoreDataSource);
        configuration[ClientRootKey].Should().Be(layout.ClientStaticRoot);
        configuration[InstallationConfiguration.RootKey].Should().Be(layout.Root);
    }

    /// <remarks>
    /// The installed server carries a relative root, so that an installation stays correct when it is moved
    /// or copied. The content root is the server's own folder, which is what makes <c>..</c> mean the
    /// installation it belongs to and nothing else.
    /// </remarks>
    [Test]
    public void ARelativeRootIsResolvedAgainstTheContentRoot()
    {
        var installation = Path.Combine(Path.GetTempPath(), "arronix-relative-installation");
        var contentRoot = Path.Combine(installation, InstallationLayout.ServerFolderName);
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InstallationConfiguration.RootKey] = "..",
        });

        var layout = configuration.AddArronixInstallation(contentRoot);

        layout!.Root.Should().Be(Path.GetFullPath(installation));
    }

    /// <remarks>
    /// The shipped <c>appsettings.json</c> states a client root of its own, and an installation whose client
    /// was served from somewhere else because a default outranked it would be the exact defect this seam
    /// removes. Declaring an installation root is a deliberate act and the paths it owns follow it.
    /// </remarks>
    [Test]
    public void TheInstallationOutranksAnEarlierSettingForThePathsItOwns()
    {
        var root = Path.Combine(Path.GetTempPath(), "arronix-precedence");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ClientRootKey] = "wwwroot",
            [StoreKey] = "arronix.db",
            [InstallationConfiguration.RootKey] = root,
        });

        var layout = configuration.AddArronixInstallation(Path.GetTempPath());

        using var assertions = new AssertionScope();
        configuration[ClientRootKey].Should().Be(layout!.ClientStaticRoot);
        configuration[StoreKey].Should().Be(layout.StoreDataSource);
    }

    /// <remarks>
    /// Nothing outside the four paths is touched. An installation says where its own packages, state and
    /// client live; it does not decide where an operator keeps their media.
    /// </remarks>
    [Test]
    public void NothingOutsideThoseFourPathsIsChanged()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LibraryOptions.SectionName + ":RootFolders:0"] = "/media/movies",
            [ApiOptions.SectionName + ":MaxPageSize"] = "25",
            [InstallationConfiguration.RootKey] = Path.Combine(Path.GetTempPath(), "arronix-untouched"),
        });

        configuration.AddArronixInstallation(Path.GetTempPath());

        using var assertions = new AssertionScope();
        configuration[LibraryOptions.SectionName + ":RootFolders:0"].Should().Be("/media/movies");
        configuration[ApiOptions.SectionName + ":MaxPageSize"].Should().Be("25");
    }
}
