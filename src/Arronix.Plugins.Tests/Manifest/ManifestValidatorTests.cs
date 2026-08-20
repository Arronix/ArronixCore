using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Manifest;


namespace Arronix.Plugins.Tests.Manifest;

/// <summary>
/// Everything that can be proved about a declaration without loading any code.
/// </summary>
[TestFixture]
public sealed class ManifestValidatorTests
{
    private static PluginManifest Valid(Action<Builder>? adjust = null)
    {
        var builder = new Builder();
        adjust?.Invoke(builder);
        return builder.Build();
    }

    [Test]
    public void AWellFormedDeclarationYieldsProvedValues()
    {
        PluginManifestValidator.TryValidate(Valid(), out var validated, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        validated!.Id.Should().Be(PluginId.FromString("example"));
        validated.Version.ToString().Should().Be("0.1.0");
        validated.ContractRange.Text.Should().Be(">=0.3 <0.4");
        validated.EntryAssembly.Should().Be("Arronix.Plugin.Example.dll");
        validated.DeclaredCapabilities.Has(Capability.Parsing).Should().BeTrue();
        validated.Policies.Should().NotBeNull();
    }

    [Test]
    public void TheGrantedSetAddsTheImpliedPrivilegeAndTheDeclaredSetDoesNot()
    {
        var manifest = Valid(builder => builder.Capabilities = ["indexing"]);

        PluginManifestValidator.TryValidate(manifest, out var validated, out _).Should().BeTrue();

        validated!.DeclaredCapabilities.Has(Capability.Network).Should().BeFalse(
            "the forward check runs against the declaration, so an implied privilege must never look like an undeclared one");
        validated.GrantedCapabilities.Has(Capability.Network).Should().BeTrue();
    }

    [Test]
    public void EveryDefectIsReportedNotOnlyTheFirst()
    {
        var manifest = Valid(builder =>
        {
            builder.Id = "Not An Id";
            builder.Version = "not-a-version";
            builder.Range = "^0.3.0";
        });

        PluginManifestValidator.TryValidate(manifest, out var validated, out var defects).Should().BeFalse();

        validated.Should().BeNull();
        defects.Select(defect => defect.Path).Should().Contain(["id", "version", "contracts.arronix"]);
    }

    [TestCase(1)]
    [TestCase(-1)]
    public void AnUnknownManifestFormatVersionIsRefused(int schemaVersion)
    {
        var manifest = Valid(builder => builder.SchemaVersion = schemaVersion);

        ShouldHaveDefect(manifest, "schemaVersion", CoreErrorCode.PluginManifestInvalid);
    }

    [TestCase("Example")]
    [TestCase("1example")]
    [TestCase("example.")]
    [TestCase("example..tv")]
    [TestCase("example/tv")]
    [TestCase("")]
    public void AMalformedIdentifierIsRefused(string id)
        => ShouldHaveDefect(Valid(builder => builder.Id = id), "id", CoreErrorCode.PluginManifestInvalid);

    [TestCase("../escape.dll")]
    [TestCase("sub/dir.dll")]
    [TestCase("sub\\dir.dll")]
    [TestCase("plugin.exe")]
    [TestCase("")]
    public void AnEntryAssemblyThatCouldLeaveTheExtensionFolderIsRefused(string entry)
        => ShouldHaveDefect(Valid(builder => builder.Entry = entry), "entryAssembly", CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void AnExtensionMustDeclareAtLeastOneCapability()
        => ShouldHaveDefect(Valid(builder => builder.Capabilities = []), "capabilities", CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void AnUnknownCapabilityIsRefusedRatherThanIgnored()
        => ShouldHaveDefect(
            Valid(builder => builder.Capabilities = ["parsing", "process"]),
            "capabilities[1]",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void ADuplicateCapabilityIsRefused()
        => ShouldHaveDefect(
            Valid(builder => builder.Capabilities = ["parsing", "parsing"]),
            "capabilities[1]",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void ClaimingAMediaKindWithoutThePrivilegeIsRefused()
        => ShouldHaveDefect(
            Valid(builder => builder.MediaKinds = ["example"]),
            "mediaKinds",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void HoldingTheMediaPrivilegeWithoutClaimingAKindIsRefused()
        => ShouldHaveDefect(
            Valid(builder => builder.Capabilities = ["media-kind"]),
            "mediaKinds",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void AMediaExtensionDeclaringBothIsAccepted()
    {
        var manifest = Valid(builder =>
        {
            builder.Capabilities = ["media-kind"];
            builder.MediaKinds = ["example"];
        });

        PluginManifestValidator.TryValidate(manifest, out var validated, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        validated!.MediaKinds.Should().ContainSingle();
        validated.MediaKinds[0].Value.Should().Be("example");
    }

    [TestCase("Title")]
    [TestCase("{Title")]
    [TestCase("Title}")]
    [TestCase("{}")]
    [TestCase("{A}{B}")]
    [TestCase("   ")]
    public void AMalformedTokenIsRefused(string name)
        => ShouldHaveDefect(
            Valid(builder => builder.Tokens = [new NamingToken(name, string.Empty, string.Empty)]),
            "tokens[0].name",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void ADuplicateTokenIsRefused()
    {
        var manifest = Valid(builder => builder.Tokens =
        [
            new NamingToken("{Title}", string.Empty, string.Empty),
            new NamingToken("{Title}", string.Empty, string.Empty)
        ]);

        ShouldHaveDefect(manifest, "tokens[1].name", CoreErrorCode.PluginManifestInvalid);
    }

    [Test]
    public void ADuplicatePolicyIdentifierWithinACategoryIsRefused()
    {
        var manifest = Valid(builder => builder.Policies = new PolicyGraph { Parsing = ["Alpha", "Alpha"] });

        ShouldHaveDefect(manifest, "policies.parsing[1]", CoreErrorCode.PluginPolicyDeclarationInvalid);
    }

    [Test]
    public void TheSamePolicyIdentifierInTwoCategoriesIsAllowed()
    {
        var manifest = Valid(builder => builder.Policies = new PolicyGraph
        {
            Parsing = ["Alpha"],
            Naming = ["Alpha"]
        });

        PluginManifestValidator.TryValidate(manifest, out _, out var defects).Should().BeTrue();
        defects.Should().BeEmpty();
    }

    [Test]
    public void ADuplicateIdentifierSchemeIsRefused()
        => ShouldHaveDefect(
            Valid(builder => builder.Identifiers = ["exdb", "exdb"]),
            "identifiers[1]",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void AnAbsentPolicyGraphBecomesAnEmptyOneRatherThanNull()
    {
        PluginManifestValidator.TryValidate(Valid(builder => builder.Policies = null), out var validated, out _)
            .Should().BeTrue();

        validated!.Policies.TotalCount().Should().Be(0);
    }

    private static void ShouldHaveDefect(PluginManifest manifest, string path, CoreErrorCode code)
    {
        PluginManifestValidator.TryValidate(manifest, out var validated, out var defects).Should().BeFalse();

        validated.Should().BeNull();
        defects.Should().Contain(defect => defect.Path == path && defect.Code == code);
    }

    private sealed class Builder
    {
        public int SchemaVersion { get; set; }

        public string Id { get; set; } = "example";

        public string Name { get; set; } = "Example";

        public string Version { get; set; } = "0.1.0";

        public string Range { get; set; } = ">=0.3 <0.4";

        public string Entry { get; set; } = "Arronix.Plugin.Example.dll";

        public IReadOnlyList<string> Capabilities { get; set; } = ["parsing"];

        public IReadOnlyList<string> MediaKinds { get; set; } = [];

        public IReadOnlyList<string> Identifiers { get; set; } = [];

        public IReadOnlyList<NamingToken> Tokens { get; set; } = [];

        public PolicyGraph? Policies { get; set; } = new();

        public PluginManifest Build() => new()
        {
            SchemaVersion = SchemaVersion,
            Id = Id,
            Name = Name,
            Version = Version,
            Contracts = new ContractRequirements { Arronix = Range },
            EntryAssembly = Entry,
            Capabilities = Capabilities,
            MediaKinds = MediaKinds,
            Identifiers = Identifiers,
            Tokens = Tokens,
            Policies = Policies
        };
    }
}
