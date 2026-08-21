using System.Linq;
using Arronix.Architecture.Tests.Repository;
using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitIs = global::NUnit.Framework.Is;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;

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
[NUnitTestFixtureAttribute]
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

    private static global::System.Reflection.Assembly ContractAssembly =>
        typeof(global::Arronix.Abstractions.Health.HealthCheck).Assembly;

    [NUnitTestAttribute]
    public void ContractProjectDeclaresNoPackageReference()
    {
        var project = ProjectFile.Load(RepositoryLayout.Abstractions);

        NUnitAssert.That(
            project.PackageReferences,
            NUnitIs.Empty,
            "The contract assembly must stay package-free: it is the one assembly both the host and every "
            + "isolated consumer load, and the only assembly the browser client may reference.");
    }

    [NUnitTestAttribute]
    public void ContractProjectDeclaresNoProjectReference()
    {
        var project = ProjectFile.Load(RepositoryLayout.Abstractions);

        NUnitAssert.That(project.ProjectReferences, NUnitIs.Empty, "The contract assembly is the bottom of the graph.");
    }

    [NUnitTestAttribute]
    public void ContractAssemblyLinksNothingOutsideTheSharedFramework()
    {
        var offenders = ContractAssembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(name => ForbiddenReferencePrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(
            offenders,
            NUnitIs.Empty,
            "A dependency reached the contract assembly despite the empty package set.");
    }

    [NUnitTestAttribute]
    public void ContractAssemblyLinksNoOtherArronixAssembly()
    {
        RequireAssembly(typeof(NUnitAssert), "nunit.framework");
        RequireAssembly(typeof(global::Arronix.Abstractions.Health.HealthCheck), "Arronix.Abstractions");

        var linked = ContractAssembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(linked, NUnitIs.Empty);
    }

    private static void RequireAssembly(global::System.Type type, string expectedName)
    {
        if (!string.Equals(type.Assembly.GetName().Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected '{type.FullName}' from '{expectedName}', but resolved it from '{type.Assembly.FullName}'.");
        }
    }
}
