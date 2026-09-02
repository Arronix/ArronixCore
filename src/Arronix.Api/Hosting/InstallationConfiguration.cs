using Arronix.Api.Configuration;
using Arronix.Common.Installation;
using Arronix.Host.Configuration;
using Arronix.Plugins.Configuration;
using Microsoft.Extensions.Configuration;

namespace Arronix.Api.Hosting;

/// <summary>
/// Teaches the server that it is running inside an installation.
/// </summary>
/// <remarks>
/// <para>
/// Four settings decide where a running Arronix keeps everything that outlives the process: the folder its
/// packages are installed in, the folder each package's own state is laid out under, the database file, and
/// the static root the client is served from. Each is independently useful and each stays independently
/// configurable. What was missing was the fact that binds them: in a real deployment they are four parts of
/// one installation, and nothing in the product said so. The gap was filled from outside every time — by a
/// script exporting four environment variables that only agreed with each other because one author wrote
/// them together.
/// </para>
/// <para>
/// One setting closes it. <c>Arronix:Installation:Root</c> names the installation this server belongs to,
/// and the layout derives the rest. It is read from ordinary configuration, so it can arrive from the
/// installed server's own <c>appsettings.json</c>, from an environment variable, or from the command line,
/// and a relative value is resolved against the content root — which is what lets an installation be moved
/// or copied without editing anything inside it.
/// </para>
/// <para>
/// The derived values are added last and therefore win. That is the rule, and it is the safe direction:
/// declaring an installation root is a deliberate deployment decision, and an installation whose database
/// silently stayed in the previous working directory because some other source also mentioned it would be
/// the exact failure this type exists to remove. A deployment that wants the paths apart leaves the root
/// unset, and then nothing here changes anything at all.
/// </para>
/// </remarks>
public static class InstallationConfiguration
{
    /// <summary>The configuration key naming the installation this server belongs to.</summary>
    public const string RootKey = "Arronix:Installation:Root";

    /// <summary>
    /// Derives this server's installation-owned paths from its installation root, when it has one.
    /// </summary>
    /// <param name="configuration">The configuration being built.</param>
    /// <param name="contentRootPath">The directory a relative installation root is resolved against.</param>
    /// <returns>
    /// The layout the server will use, or <see langword="null"/> when no installation root is configured and
    /// every path therefore stays exactly as it was.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static InstallationLayout? AddArronixInstallation(
        this IConfigurationManager configuration,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var root = configuration[RootKey];

        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var layout = InstallationLayout.At(root, contentRootPath);

        configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RootKey] = layout.Root,
            [PluginRuntimeOptions.SectionName + ":" + nameof(PluginRuntimeOptions.RootFolder)] = layout.PackagesFolder,
            [PluginRuntimeOptions.SectionName + ":" + nameof(PluginRuntimeOptions.StateFolder)] = layout.PackageStateFolder,
            [StoreOptions.SectionName + ":" + nameof(StoreOptions.DataSource)] = layout.StoreDataSource,
            [ApiOptions.SectionName + ":" + nameof(ApiOptions.ClientRoot)] = layout.ClientStaticRoot,
        });

        return layout;
    }
}
