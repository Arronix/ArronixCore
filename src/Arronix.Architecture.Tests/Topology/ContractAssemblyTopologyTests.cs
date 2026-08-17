using System.Linq;
using System.Reflection;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Rule 2 - the contract assembly takes no package, and therefore runs anywhere.
/// </summary>
/// <remarks>
/// <para>
/// Two consequences hang off the empty package set, and both are load-bearing. The first is isolation:
/// an extension and the host must agree on exactly one copy of the contract types, and a contract
/// assembly that dragged a versioned package behind it would make that agreement a matter of luck. The
/// second is reach: the same assembly is the only thing the browser client may reference, so anything it
/// pulled in would have to survive being compiled to WebAssembly.
/// </para>
/// <para>
/// The rule is asserted on the declaration and again on the binary, because a package that arrives
/// through a target rather than through an author still ends up in the reference graph.
/// </para>
/// </remarks>
[TestFixture]
public class ContractAssemblyTopologyTests
{
    private static readonly string[] ForbiddenReferencePrefixes =
    [
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore.",
        "Newtonsoft.",
        "NLog",
        "Serilog",
        "Sentry"
    ];

    private static Assembly ContractAssembly => typeof(Arronix.Abstractions.Health.HealthCheck).Assembly;

    [Test]
    public void ContractProjectDeclaresNoPackageReference()
    {
        var project = ProjectFile.Load(RepositoryLayout.Abstractions);

        Assert.That(
            project.PackageReferences,
            Is.Empty,
            "The contract assembly must stay package-free: it is the one assembly both the host and every "
            + "isolated consumer load, and the only assembly the browser client may reference.");
    }

    [Test]
    public void ContractProjectDeclaresNoProjectReference()
    {
        var project = ProjectFile.Load(RepositoryLayout.Abstractions);

        Assert.That(project.ProjectReferences, Is.Empty, "The contract assembly is the bottom of the graph.");
    }

    [Test]
    public void ContractAssemblyLinksNothingOutsideTheSharedFramework()
    {
        var offenders = ContractAssembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(name => ForbiddenReferencePrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "A dependency reached the contract assembly despite the empty package set.");
    }

    [Test]
    public void ContractAssemblyLinksNoOtherArronixAssembly()
    {
        var linked = ContractAssembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(linked, Is.Empty);
    }
}
