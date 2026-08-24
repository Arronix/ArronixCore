using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Tests.Support;


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

    [Test]
    public void InspectionReadsMetadataRatherThanLoadingTheAssembly()
    {
        var folder = Directory.CreateTempSubdirectory("arronix-inspection-probe").FullName;
        var assemblyName = $"Inspection.Probe.{Guid.NewGuid():N}";
        var assemblyPath = EmittedPlugin.Write(folder, "inspection-probe", assemblyName: assemblyName);
        var observedLoads = 0;

        void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
        {
            if (string.Equals(args.LoadedAssembly.GetName().Name, assemblyName, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref observedLoads);
            }
        }

        try
        {
            AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Should().NotContain(
                assembly => assembly.GetName().Name == assemblyName);

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            var report = PluginReferenceInspector.Inspect(assemblyPath);

            report.References.Should().Contain("Arronix.Abstractions");
            observedLoads.Should().Be(0, "metadata inspection must not load or execute the candidate");
            AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Should().NotContain(
                assembly => assembly.GetName().Name == assemblyName,
                "inspection happens before isolation and must not load the candidate into any load context");
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            Directory.Delete(folder, recursive: true);
        }
    }
}
