using FluentAssertions.Execution;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;


namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// The two rules that make the isolation boundary real, and the order between them.
/// </summary>
/// <remarks>
/// Both of these are silent failures when they are wrong. A deny list that returns rather than throws
/// <i>grants</i> the host's implementation assemblies, because the default context resolves them happily. A
/// contract assembly that does not unify makes every cast fail, so the extension presents as having
/// registered nothing at all rather than as having failed to load. Neither is discoverable by reading the
/// code; both are trivial to catch here.
/// </remarks>
[TestFixture]
public sealed class PluginLoadContextTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.isolation");

    private static PluginLoadContext CreateContext()
        => new(
            Plugin,
            typeof(PluginLoadContextTests).Assembly.Location,
            nativeLibraryResolver: null,
            PackageContractScope.Empty(Plugin));

    /// <summary>
    /// Exact shared-framework names are matched exactly, and lookalikes are not.
    /// </summary>
    /// <remarks>
    /// <c>Microsoft.CSharp</c> and <c>Microsoft.VisualBasic</c> are whole assembly names rather than
    /// prefixes. Matching them as prefixes would hand <c>Microsoft.CSharpEvil</c> to the default context on
    /// the strength of its first fourteen characters, which is the whole of the isolation this rule exists
    /// to keep.
    /// </remarks>
    /// <param name="name">The assembly name.</param>
    /// <param name="shared">Whether it belongs to the shared framework.</param>
    [TestCase("Microsoft.CSharp", true)]
    [TestCase("Microsoft.VisualBasic", true)]
    [TestCase("System.Text.Json", true)]
    [TestCase("Microsoft.Win32.Registry", true)]
    [TestCase("Microsoft.CSharpEvil", false)]
    [TestCase("Microsoft.VisualBasicHijack", false)]
    [TestCase("Systematic.Deception", false)]
    public void SharedFrameworkClassificationDistinguishesExactNamesFromPrefixes(string name, bool shared)
        => PluginLoadContext.IsSharedFramework(name).Should().Be(shared);

    /// <summary>
    /// The host contract assembly is classified the way the runtime binds simple names.
    /// </summary>
    /// <param name="name">The assembly name.</param>
    /// <param name="host">Whether it is the host contract assembly.</param>
    [TestCase("Arronix.Abstractions", true)]
    [TestCase("arronix.abstractions", true)]
    [TestCase("ARRONIX.ABSTRACTIONS", true)]
    [TestCase("Arronix.AbstractionsEvil", false)]
    public void TheHostContractAssemblyIsRecognizedCaseInsensitively(string name, bool host)
        => PluginLoadContext.IsHostContract(name).Should().Be(host);

    /// <summary>
    /// The blocked list cannot be edited by a caller that reaches for it.
    /// </summary>
    [Test]
    public void TheBlockedPrefixListCannotBeEditedByACaller()
    {
        var prefixes = PluginLoadContext.BlockedAssemblyPrefixes;

        using var assertions = new AssertionScope();
        prefixes.Should().NotBeEmpty();
        ((object)prefixes).Should().NotBeAssignableTo<string[]>(
            "an exported array is an exported mutable deny list");
        PluginLoadContext.IsBlocked("arronix.host.runtime").Should().BeTrue(
            "the runtime binds simple names case-insensitively, so the deny list must too");
    }

    [TestCase("Arronix.Common")]
    [TestCase("Arronix.Plugins")]
    [TestCase("Arronix.Host")]
    [TestCase("Arronix.Api")]
    [TestCase("Arronix.Client")]
    [TestCase("NzbDrone.Core")]
    [TestCase("Sonarr.Http")]
    public void AHostImplementationAssemblyThrowsRatherThanFallingBack(string assemblyName)
    {
        var context = CreateContext();

        var isolation = CatchIsolationFailure(() => context.LoadFromAssemblyName(new AssemblyName(assemblyName)));

        isolation.Should().NotBeNull(
            "returning null here would fall back to the default context and succeed, silently granting the extension exactly what the deny list exists to withhold");
        isolation!.ErrorCode.Should().Be(CoreErrorCode.PluginIsolationViolation);
        isolation.BlockedAssembly.Should().Be(assemblyName);
        isolation.RequestedBy.Should().Be(Plugin.ToString());
    }

    [Test]
    public void TheContractAssemblyUnifiesWithTheHostInstance()
    {
        var context = CreateContext();
        var hostContracts = typeof(IPluginModule).Assembly;

        var resolved = context.LoadFromAssemblyName(new AssemblyName("Arronix.Abstractions"));

        resolved.Should().BeSameAs(
            hostContracts,
            "an extension loading its own copy of the contract assembly would implement a different runtime type than the host asks for, and no cast would ever succeed");
    }

    [Test]
    public void TheEntryModuleTypeIsTheSameTypeOnBothSidesOfTheBoundary()
    {
        var context = CreateContext();

        var resolved = context.LoadFromAssemblyName(new AssemblyName("Arronix.Abstractions"));
        var moduleType = resolved.GetType(typeof(IPluginModule).FullName!);

        moduleType.Should().BeSameAs(typeof(IPluginModule));
    }

    [TestCase("System.Text.Json")]
    [TestCase("System.Collections")]
    public void TheSharedFrameworkYieldsToTheDefaultContext(string assemblyName)
    {
        var context = CreateContext();

        var resolved = context.LoadFromAssemblyName(new AssemblyName(assemblyName));

        AssemblyLoadContext.GetLoadContext(resolved).Should().BeSameAs(
            AssemblyLoadContext.Default,
            "a second copy of a framework assembly is the same silent type-identity failure as a second copy of the contract assembly");
    }

    [Test]
    public void TheContextIsCollectibleFromTheFirstDay()
    {
        var context = CreateContext();

        context.IsCollectible.Should().BeTrue(
            "unload is not exercised in this milestone, but collectibility costs nothing now and forecloses nothing later");
    }

    [Test]
    public void TheContextIsNamedAfterTheExtension()
        => CreateContext().Name.Should().Be($"arronix-plugin:{Plugin}");

    [TestCase("Arronix.Common", true)]
    [TestCase("Arronix.Common.Tests", true)]
    [TestCase("Arronix.Abstractions", false)]
    [TestCase("Arronix.Plugin.Example", false)]
    [TestCase("System.Text.Json", false)]
    [TestCase(null, false)]
    public void TheDenyListIsPrefixMatchedAndDoesNotCatchTheContractAssembly(string? name, bool expected)
        => PluginLoadContext.IsBlocked(name).Should().Be(expected);

    [Test]
    public void AnEmptyEntryPathIsRefused()
    {
        var construct = () => new PluginLoadContext(Plugin, "  ", nativeLibraryResolver: null, PackageContractScope.Empty(Plugin));

        construct.Should().Throw<ArgumentException>();
    }

    private static PluginIsolationException? CatchIsolationFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception failure)
        {
            for (var current = failure; current is not null; current = current.InnerException)
            {
                if (current is PluginIsolationException isolation)
                {
                    return isolation;
                }
            }
        }

        return null;
    }
}
