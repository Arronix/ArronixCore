using System.Text.Json;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using FluentAssertions;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// The document a host answers with is untrusted, and this is where that is taken seriously.
/// </summary>
/// <remarks>
/// Each case deserializes cleanly and describes something that is not an installation — a plausible bug in
/// a host, a proxy, or a second implementation of this protocol. Each has a quietly wrong outcome if it
/// reaches the loader: an entry replaced, a closure member skipped, a verification result overwritten.
/// </remarks>
[TestFixture]
public sealed class ContractManifestValidatorTests
{
    private static readonly string Sha = new('A', 64);

    [Test]
    public void AWellFormedManifestIsAccepted()
        => ContractManifestValidator.Describe(Manifest()).Should().BeNull();

    [TestCase("no-contract-identity", "universal contract identity")]
    [TestCase("installation-hash", "installation hash")]
    [TestCase("duplicate-package", "appears more than once")]
    [TestCase("closure-hash", "closure hash")]
    [TestCase("no-assemblies", "offers no assembly")]
    [TestCase("duplicate-assembly-name", "binds to one assembly")]
    [TestCase("unsafe-file-name", "not a bare file name")]
    [TestCase("zero-length", "length of 0")]
    [TestCase("bad-content-hash", "no readable content hash")]
    [TestCase("empty-module", "no module identifier")]
    [TestCase("identity-name-disagrees", "whose simple name is")]
    [TestCase("unreadable-identity", "not a readable assembly name")]
    [TestCase("closure-missing-self", "not the final member")]
    [TestCase("closure-self-not-last", "not the final member")]
    [TestCase("closure-duplicate", "more than once")]
    [TestCase("closure-unknown", "no such package")]
    [TestCase("refusal-duplicate", "refused more than once")]
    [TestCase("refusal-overlaps-published", "both published to clients and withheld")]
    [TestCase("refusal-blames-nobody", "did not withhold")]
    [TestCase("refusal-blames-itself", "blames itself")]
    [TestCase("refusal-blames-a-published-package", "did not withhold")]
    [TestCase("refusal-blank-assembly", "names a blank assembly")]
    [TestCase("refusal-duplicate-assembly", "more than once")]
    [TestCase("refusal-blank-file", "names a blank file")]
    [TestCase("refusal-unbare-file", "not a bare file name")]
    [TestCase("refusal-duplicate-cause", "more than once")]
    [TestCase("refusal-empty-cause", "names an empty cause")]
    [TestCase("null-package-list", "absent rather than empty")]
    [TestCase("null-refused-list", "absent rather than empty")]
    [TestCase("null-package-entry", "one of its packages is null")]
    [TestCase("null-assembly-entry", "null assembly entry")]
    [TestCase("null-assembly-list", "absent list rather than an empty one")]
    [TestCase("null-refusal-entry", "refusals is null")]
    [TestCase("duplicate-file-name", "more than once")]
    public void AManifestThatDoesNotDescribeAnInstallationIsNamed(string defect, string expected)
        => ContractManifestValidator.Describe(Malformed(defect))
            .Should().NotBeNull().And.Subject.Should().Contain(expected);

    /// <summary>
    /// An identifier that is not one is a document this client cannot read, not a package called nothing.
    /// </summary>
    /// <remarks>
    /// The converter refuses rather than defaulting. A default identifier compares equal to every other
    /// unreadable one, so defaulting would quietly merge packages the host never merged.
    /// </remarks>
    [TestCase("\"Not A Package\"")]
    [TestCase("\"\"")]
    [TestCase("\"9starts.with.a.digit\"")]
    [TestCase("null")]
    [TestCase("7")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("true")]
    public void AMalformedPackageIdentifierMakesTheWholeDocumentUnreadable(string wireText)
    {
        var json = $$"""
        {
          "contractIdentity": "Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null",
          "installationHash": "{{new string('A', 64)}}",
          "packages": [{ "id": {{wireText}}, "version": "1.0.0", "name": "P", "assemblies": [], "closure": [], "closureHash": "x" }],
          "refused": []
        }
        """;

        var read = () => JsonSerializer.Deserialize<ClientContractManifest>(json, ApiJsonOptions.Default);

        read.Should().Throw<JsonException>();
    }

    private static ClientContractManifest Manifest(
        IReadOnlyList<ClientContractPackage>? packages = null,
        IReadOnlyList<ClientContractRefusal>? refused = null)
        => new(
            "Arronix.Abstractions, Version=0.9.0.0, Culture=neutral, PublicKeyToken=null",
            Sha,
            packages ?? [Package("one.package", Assembly("One"))],
            refused ?? []);

    private static ClientContractPackage Package(
        string id,
        ClientContractAssembly assembly,
        IReadOnlyList<PluginId>? closure = null)
        => new(
            PluginId.FromString(id),
            "1.0.0",
            id,
            [assembly],
            closure ?? [PluginId.FromString(id)],
            Sha);

    private static ClientContractAssembly Assembly(string simpleName)
        => new(
            simpleName,
            simpleName + ".dll",
            simpleName + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            Sha,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            1024);

    private static ClientContractManifest Malformed(string defect) => defect switch
    {
        "no-contract-identity" => Manifest() with { ContractIdentity = "  " },
        "installation-hash" => Manifest() with { InstallationHash = "not-a-hash" },
        "duplicate-package" => Manifest([Package("one.package", Assembly("One")), Package("one.package", Assembly("Two"))]),
        "closure-hash" => Manifest([Package("one.package", Assembly("One")) with { ClosureHash = "short" }]),
        "no-assemblies" => Manifest([Package("one.package", Assembly("One")) with { Assemblies = [] }]),
        "duplicate-assembly-name" => Manifest([Package("one.package", Assembly("One")), Package("two.package", Assembly("One"))]),
        "unsafe-file-name" => Manifest([Package("one.package", Assembly("One") with { FileName = "../escape.dll" })]),
        "zero-length" => Manifest([Package("one.package", Assembly("One") with { Length = 0 })]),
        "bad-content-hash" => Manifest([Package("one.package", Assembly("One") with { ContentHash = "zz" })]),
        "empty-module" => Manifest([Package("one.package", Assembly("One") with { ModuleVersionId = Guid.Empty })]),
        "identity-name-disagrees" => Manifest(
            [Package("one.package", Assembly("One") with { Identity = "Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" })]),
        "unreadable-identity" => Manifest([Package("one.package", Assembly("One") with { Identity = "  ,,, " })]),
        "closure-missing-self" => Manifest(
            [
                Package("one.package", Assembly("One"), [PluginId.FromString("two.package")]),
                Package("two.package", Assembly("Two")),
            ]),
        "closure-self-not-last" => Manifest(
            [
                Package("one.package", Assembly("One"), [PluginId.FromString("one.package"), PluginId.FromString("two.package")]),
                Package("two.package", Assembly("Two")),
            ]),
        "closure-duplicate" => Manifest(
            [Package("one.package", Assembly("One"), [PluginId.FromString("one.package"), PluginId.FromString("one.package")])]),
        "closure-unknown" => Manifest(
            [Package("one.package", Assembly("One"), [PluginId.FromString("ghost.package"), PluginId.FromString("one.package")])]),
        "refusal-duplicate" => Manifest(null, [Refusal("bad.package"), Refusal("bad.package")]),
        "refusal-overlaps-published" => Manifest(null, [Refusal("one.package")]),
        "refusal-blames-nobody" => Manifest(null, [Refusal("bad.package") with { CausedBy = [PluginId.FromString("ghost.package")] }]),
        "refusal-blames-itself" => Manifest(null, [Refusal("bad.package") with { CausedBy = [PluginId.FromString("bad.package")] }]),

        // A package a client can see in the published list cannot be the reason another one is missing.
        "refusal-blames-a-published-package" => Manifest(
            null,
            [Refusal("bad.package") with { CausedBy = [PluginId.FromString("one.package")] }]),
        "refusal-blank-assembly" => Manifest(null, [Refusal("bad.package") with { MissingAssemblies = ["  "] }]),
        "refusal-blank-file" => Manifest(null, [Refusal("bad.package") with { UnadmittedFiles = [" "] }]),

        // Rendered to an operator, so a path here is a path this client would print as a declaration.
        "refusal-unbare-file" => Manifest(
            null,
            [Refusal("bad.package") with { UnadmittedFiles = ["../escape.dll"] }]),
        "refusal-duplicate-cause" => Manifest(
            null,
            [
                Refusal("bad.package") with { CausedBy = [PluginId.FromString("other.bad"), PluginId.FromString("other.bad")] },
                Refusal("other.bad"),
            ]),
        "refusal-empty-cause" => Manifest(null, [Refusal("bad.package") with { CausedBy = [default] }]),
        "null-package-list" => Manifest() with { Packages = null! },
        "null-refused-list" => Manifest() with { Refused = null! },
        "null-package-entry" => Manifest([null!]),
        "null-assembly-entry" => Manifest([Package("one.package", Assembly("One")) with { Assemblies = [null!] }]),
        "null-assembly-list" => Manifest([Package("one.package", Assembly("One")) with { Assemblies = null! }]),
        "null-refusal-entry" => Manifest(null, [null!]),

        // Two addresses inside one package that differ only in the hash a client is asked to trust.
        "duplicate-file-name" => Manifest(
            [
                Package("one.package", Assembly("One")) with
                {
                    Assemblies = [Assembly("One"), Assembly("Two") with { FileName = "One.dll" }],
                },
            ]),
        "refusal-duplicate-assembly" => Manifest(null, [Refusal("bad.package") with { MissingAssemblies = ["One", "one"] }]),
        _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, "Unknown defect."),
    };

    /// <summary>A default identifier names no extension, so it is never written.</summary>
    [Test]
    public void ADefaultPackageIdentifierIsNeverWritten()
    {
        var write = () => JsonSerializer.Serialize(
            new ClientContractRefusal(default, "withheld", [], [], []),
            ApiJsonOptions.Default);

        write.Should().Throw<JsonException>();
    }

    private static ClientContractRefusal Refusal(string id)
        => new(PluginId.FromString(id), "withheld", [], [], []);
}
