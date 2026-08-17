using System.Linq;
using System.Reflection;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Naming;

/// <summary>
/// Rule 6 - nothing new carries a legacy product name.
/// </summary>
/// <remarks>
/// <para>
/// The legacy tree still lives in this repository and still builds; that is deliberate and is not what
/// this rule is about. What it is about is the boundary: a new assembly that names the old product has
/// stopped being a clean-room platform and has started being a rename, and the difference between those
/// two outcomes is the whole reason for this delivery.
/// </para>
/// <para>
/// Only declared names are checked, not every occurrence. The loader's assembly deny list has to spell
/// both legacy prefixes out as string literals in order to refuse them - that is the rule being
/// enforced, not broken.
/// </para>
/// </remarks>
[TestFixture]
public class LegacyNameTests
{
    private static readonly string[] LegacyProductNames = ["NzbDrone", "Sonarr"];

    /// <summary>Gets every project this delivery adds.</summary>
    public static IEnumerable<string> NewProjects =>
        RepositoryLayout.MediaNeutralProjects.Concat(RepositoryLayout.MediaExtensionProjects);

    [Test]
    [TestCaseSource(nameof(NewProjects))]
    public void ProjectDeclaresNoTypeNamedAfterALegacyProduct(string projectName)
    {
        var declarations = SourceScanner.DeclaredTypes(projectName);

        Assert.That(declarations, Is.Not.Empty, $"No type declaration was found in '{projectName}'.");

        var offenders = declarations
            .Where(declaration => NamesALegacyProduct(declaration.Name))
            .Select(static declaration => declaration.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, $"'{projectName}' declares a type carrying a legacy product name.");
    }

    [Test]
    [TestCaseSource(nameof(NewProjects))]
    public void ProjectDeclaresNoNamespaceNamedAfterALegacyProduct(string projectName)
    {
        var offenders = SourceScanner
            .Lines(projectName, "*.cs", "*.razor")
            .Where(static entry => entry.Text.TrimStart().StartsWith("namespace ", StringComparison.Ordinal))
            .Where(entry => NamesALegacyProduct(entry.Text))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    [TestCaseSource(nameof(NewProjects))]
    public void ProjectAssemblyIdentityCarriesNoLegacyProductName(string projectName)
    {
        var project = ProjectFile.Load(projectName);

        var identity = project
            .Document
            .Descendants()
            .Where(static element =>
                element.Name.LocalName is "AssemblyName" or "RootNamespace" or "ProjectReference")
            .Select(static element => (string?)element.Attribute("Include") ?? element.Value)
            .Where(NamesALegacyProduct)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(identity, Is.Empty);
    }

    [Test]
    public void ContractSurfaceCarriesNoLegacyProductName()
    {
        var offenders = typeof(Arronix.Abstractions.Health.HealthCheck)
            .Assembly
            .GetExportedTypes()
            .SelectMany(static type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => $"{type.FullName}.{member.Name}")
                .Prepend(type.FullName ?? type.Name))
            .Where(NamesALegacyProduct)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    private static bool NamesALegacyProduct(string? value) =>
        value is not null
        && LegacyProductNames.Any(legacy => value.Contains(legacy, StringComparison.OrdinalIgnoreCase));
}
