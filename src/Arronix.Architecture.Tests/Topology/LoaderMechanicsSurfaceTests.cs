using System.Linq;
using System.Reflection;
using Arronix.Plugins.Loading;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Loader mechanics stay inside the loader.
/// </summary>
/// <remarks>
/// <para>
/// <c>Arronix.Plugins</c> is Host infrastructure, and some of it is public because Host and the API genuinely
/// call it across an assembly boundary. That is a narrow, deliberate list. Assembly staging, the shared
/// contract store, its collectible context, package receipts, the resolved graph and the installed-package
/// model are none of those things: they are how isolation and resolution are implemented, and an extension
/// author writes a manifest rather than naming any of them.
/// </para>
/// <para>
/// The rule is asserted in both directions. Exporting one of these would widen a surface nobody asked for;
/// deleting or renaming one would let the first half pass while checking nothing, so each type is also
/// required to exist. The repository already has the tool for the sharing this needs — the loader declares
/// <c>InternalsVisibleTo</c> for Host and both test assemblies — so visibility never has to be bought with
/// <c>public</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LoaderMechanicsSurfaceTests
{
    /// <summary>
    /// Types which implement package resolution and shared-contract loading, and must not be exported.
    /// </summary>
    private static readonly string[] InternalMechanics =
    [
        "Arronix.Plugins.Loading.StagedAssembly",
        "Arronix.Plugins.Loading.SharedContractStore",
        "Arronix.Plugins.Loading.PackageContractScope",
        "Arronix.Plugins.Loading.AdmittedContract",
        "Arronix.Plugins.Loading.SharedContractAdmission",
        "Arronix.Plugins.Loading.SharedContractRefusal",
        "Arronix.Plugins.Loading.SharedContractState",
        "Arronix.Plugins.Loading.SharedContractIdentityException",
        "Arronix.Plugins.Loading.AssemblyIdentity",
        "Arronix.Plugins.Loading.LoadFailurePolicy",
        "Arronix.Plugins.Dependencies.InstalledPackage",
        "Arronix.Plugins.Dependencies.ResolvedPackageGraph",
        "Arronix.Plugins.Dependencies.PackageDependencyResolver",
        "Arronix.Plugins.Dependencies.IPackageGraphSource",
        "Arronix.Plugins.Dependencies.PackageAdmissionReceipt",
        "Arronix.Plugins.Dependencies.PackageAdmissionLease",
        "Arronix.Plugins.Registry.PackageDependencyRegistry",
    ];

    private static Assembly Loader => typeof(PluginLoadContext).Assembly;

    public static IEnumerable<string> Mechanics => InternalMechanics;

    [TestCaseSource(nameof(Mechanics))]
    public void ALoaderMechanismExistsAndIsNotExported(string typeName)
    {
        var type = Loader.GetType(typeName, throwOnError: false);

        Assert.That(
            type,
            Is.Not.Null,
            $"'{typeName}' is one of the types this rule is about. If it was renamed, rename it here too; "
            + "a rule whose subject has vanished passes without checking anything.");

        Assert.That(
            Loader.GetExportedTypes(),
            Has.None.EqualTo(type),
            $"'{typeName}' implements isolation rather than describing a contract. Host and the test "
            + "assemblies reach it through InternalsVisibleTo, which the loader already declares.");
    }

    /// <summary>
    /// The same rule stated as a shape, so a mechanism added tomorrow is governed on the day it appears.
    /// </summary>
    [Test]
    public void NoStagingOrSharedContractTypeIsExportedAtAll()
    {
        var exported = Loader
            .GetExportedTypes()
            // The manifest namespace is deliberately exempt. A dependency declaration is the one shape in
            // this area an extension author writes, so it is authoring contract rather than mechanics.
            .Where(type => !string.Equals(
                type.Namespace,
                "Arronix.Plugins.Manifest",
                StringComparison.Ordinal))
            .Where(type => type.Name.Contains("SharedContract", StringComparison.Ordinal)
                || type.Name.Contains("PackageContract", StringComparison.Ordinal)
                || type.Name.StartsWith("Staged", StringComparison.Ordinal)
                || type.Name.StartsWith("Installed", StringComparison.Ordinal)
                || type.Name.StartsWith("Resolved", StringComparison.Ordinal)
                || type.Name.StartsWith("PackageAdmission", StringComparison.Ordinal)
                || type.Name.StartsWith("PackageDependency", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        Assert.That(
            exported,
            Is.Empty,
            "Shared-contract and assembly-staging types are loader internals. The published loader surface is "
            + "the pipeline, its results and its isolation boundary, not the mechanics underneath them.");
    }
}
