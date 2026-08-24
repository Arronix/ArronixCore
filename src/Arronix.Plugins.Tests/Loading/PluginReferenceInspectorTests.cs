using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;


namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// Invariant two, made mechanical: an extension references the contract assembly and nothing else.
/// </summary>
/// <remarks>
/// The fixture for the failing case is this test assembly itself. It references the loader, which is on the
/// deny list, so it is exactly the shape of an assembly that must be refused — and using it means the check
/// is proved against a real compiled reference table rather than against one the test constructed to pass.
/// </remarks>
[TestFixture]
public sealed class PluginReferenceInspectorTests
{
    private static string ThisAssembly => typeof(PluginReferenceInspectorTests).Assembly.Location;

    private static string ContractAssembly => typeof(IPluginModule).Assembly.Location;

    [Test]
    public void AnAssemblyReferencingAHostAssemblyIsRefusedBeforeAnyContextExists()
    {
        var report = PluginReferenceInspector.Inspect(ThisAssembly);

        report.IsAdmissible.Should().BeFalse();
        report.Violations.Should().Contain("Arronix.Plugins");
    }

    [Test]
    public void TheContractAssemblyItselfIsAdmissible()
    {
        var report = PluginReferenceInspector.Inspect(ContractAssembly);

        report.IsAdmissible.Should().BeTrue();
        report.Violations.Should().BeEmpty();
    }

    [Test]
    public void EveryReferenceIsReportedNotOnlyTheViolations()
    {
        var report = PluginReferenceInspector.Inspect(ThisAssembly);

        report.AssemblyPath.Should().Be(ThisAssembly);
        report.References.Should().Contain("Arronix.Abstractions");
        report.References.Should().Contain(name => name.StartsWith("System.", StringComparison.Ordinal));
    }

    [Test]
    public void AViolationIsReportedOnceHoweverOftenItIsReferenced()
    {
        var report = PluginReferenceInspector.Inspect(ThisAssembly);

        report.Violations.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void AMissingAssemblyIsAFailureRatherThanAnException()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"arronix-absent-{Guid.NewGuid():N}.dll");

        PluginReferenceInspector.TryInspect(missing, out var report, out var error).Should().BeFalse();

        report.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void AFileThatIsNotAManagedAssemblyIsAFailureRatherThanAnException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arronix-notanassembly-{Guid.NewGuid():N}.dll");
        File.WriteAllText(path, "this is not a portable executable");

        try
        {
            PluginReferenceInspector.TryInspect(path, out var report, out var error).Should().BeFalse();

            report.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <remarks>
    /// Counted in the default context, which is the context inspection could load into. Counting every
    /// context instead would make the assertion depend on whether the runtime had finished collecting the
    /// collectible plugin contexts other fixtures create — one of which deliberately loads the contract
    /// assembly as an extension's own entry assembly — so it measured test scheduling rather than the
    /// behavior under test.
    /// </remarks>
    [Test]
    public void InspectionReadsMetadataRatherThanLoadingTheAssembly()
    {
        PluginReferenceInspector.Inspect(ContractAssembly);
        PluginReferenceInspector.Inspect(ThisAssembly);

        AssemblyLoadContext.Default.Assemblies
            .Count(assembly => assembly.GetName().Name == "Arronix.Abstractions")
            .Should().Be(
                1,
                "inspection happens at discovery, before any isolation decision has been taken, so it must not load anything");
    }
}
