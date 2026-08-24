using System.IO;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// Proves the payload rule fails when a stale assembly is present.
/// </summary>
/// <remarks>
/// <para>
/// A rule that only ever runs against a correct payload cannot be distinguished from a rule that does not
/// look. That is not hypothetical here: the defect this guards against - a recursive copy of a project's
/// <c>bin</c> carrying an assembly a removed <c>ProjectReference</c> stopped producing - is invisible
/// precisely because everything still builds and every assertion still passes.
/// </para>
/// <para>
/// So the detector is exercised against a payload with the fault deliberately introduced. The copy is made
/// in a temporary directory; nothing here touches a staged payload or a build output.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class StagedPayloadDetectorTests
{
    private const string StaleAssembly = "Arronix.Format.Video.Contributions.dll";

    private string _root = string.Empty;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("arronix-payload-detector").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void TheRuleAcceptsTheStagedPayloadAsItIsBuilt()
    {
        var payload = CopyStagedMoviesPayload();

        var staged = PackagedMoviesLayoutTests.ManagedFileNamesIn(payload);
        var declared = PackagedMoviesLayoutTests.RuntimeAssembliesOf(
            Path.Combine(payload, "Arronix.Plugin.Movies.deps.json"));

        using var assertions = new AssertionScope();
        staged.Should().NotBeEmpty();
        declared.Should().NotBeEmpty();
        staged.Should().BeSubsetOf(declared, "the control: an untouched payload must satisfy the rule");
    }

    /// <remarks>
    /// The fault reproduced exactly as it would arrive. The stale file is a real assembly the movies package
    /// once carried and no longer references, planted the way a recursive copy of a source directory would
    /// have planted it.
    /// </remarks>
    [Test]
    public void TheRuleRejectsAPayloadCarryingAnAssemblyTheManifestDoesNotName()
    {
        var payload = CopyStagedMoviesPayload();
        File.WriteAllBytes(Path.Combine(payload, StaleAssembly), [0x4D, 0x5A]);

        var staged = PackagedMoviesLayoutTests.ManagedFileNamesIn(payload);
        var declared = PackagedMoviesLayoutTests.RuntimeAssembliesOf(
            Path.Combine(payload, "Arronix.Plugin.Movies.deps.json"));

        using var assertions = new AssertionScope();
        staged.Should().Contain(StaleAssembly, "the fault must actually be present for the rule to catch it");
        declared.Should().NotContain(StaleAssembly);
        staged.Except(declared, StringComparer.Ordinal).Should().Equal(
            [StaleAssembly],
            "the rule names the stale assembly and nothing else");
    }

    /// <remarks>
    /// The other half of the same fault. A payload staged from a directory listing carries whatever is in
    /// that directory, so the rule has to catch a stale file whatever its name; a rule keyed to one known
    /// offender would pass the next one.
    /// </remarks>
    [Test]
    public void TheRuleRejectsAnyAssemblyTheManifestDoesNotName()
    {
        var payload = CopyStagedMoviesPayload();
        File.WriteAllBytes(Path.Combine(payload, "Some.Forgotten.Dependency.dll"), [0x4D, 0x5A]);

        var staged = PackagedMoviesLayoutTests.ManagedFileNamesIn(payload);
        var declared = PackagedMoviesLayoutTests.RuntimeAssembliesOf(
            Path.Combine(payload, "Arronix.Plugin.Movies.deps.json"));

        staged.Except(declared, StringComparer.Ordinal).Should().Equal(["Some.Forgotten.Dependency.dll"]);
    }

    private string CopyStagedMoviesPayload()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "movies");
        var destination = Path.Combine(_root, "movies");
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        return destination;
    }
}
