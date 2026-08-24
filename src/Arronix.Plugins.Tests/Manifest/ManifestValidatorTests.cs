using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;


namespace Arronix.Plugins.Tests.Manifest;

/// <summary>
/// Everything that can be proved about a declaration without loading any code.
/// </summary>
[TestFixture]
public sealed class ManifestValidatorTests
{
    /// <summary>
    /// Proves a declaration the way the loader does, from a candidate found in a folder.
    /// </summary>
    /// <remarks>
    /// Validation builds the installation's canonical package snapshot, so it needs the folder the
    /// declaration was found in and the typed availability the operator's configuration produced. The tests
    /// supply a stable synthetic location; nothing is read from it.
    /// </remarks>
    private static bool TryValidate(
        PluginManifest manifest,
        out ValidatedManifest? validated,
        out IReadOnlyList<ManifestDefect> defects,
        PackageAvailability availability = PackageAvailability.Available)
        => PluginManifestValidator.TryValidate(
            new PluginCandidate(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arronix-manifest", "plugin.json"),
                manifest),
            availability,
            out validated,
            out defects);

    private static PluginManifest Valid(Action<Builder>? adjust = null)
    {
        var builder = new Builder();
        adjust?.Invoke(builder);
        return builder.Build();
    }

    [Test]
    public void AWellFormedDeclarationYieldsProvedValues()
    {
        TryValidate(Valid(), out var validated, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        validated!.Id.Should().Be(PluginId.FromString("example"));
        validated.Version.ToString().Should().Be("0.1.0");
        validated.ContractRange.Text.Should().Be(">=0.3 <0.4");
        validated.EntryAssembly.Should().Be("Arronix.Plugin.Example.dll");
        validated.ContractAssemblies.Should().BeEmpty();
        validated.Dependencies.Should().BeEmpty();
        validated.DeclaredCapabilities.Has(Capability.Parsing).Should().BeTrue();
        validated.Policies.Should().NotBeNull();
    }

    [Test]
    public void TheGrantedSetAddsTheImpliedPrivilegeAndTheDeclaredSetDoesNot()
    {
        var manifest = Valid(builder => builder.Capabilities = ["indexing"]);

        TryValidate(manifest, out var validated, out _).Should().BeTrue();

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

        TryValidate(manifest, out var validated, out var defects).Should().BeFalse();

        validated.Should().BeNull();
        defects.Select(defect => defect.Path).Should().Contain(["id", "version", "contracts.arronix"]);
    }

    [TestCase(0)]
    [TestCase(2)]
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

    /// <summary>
    /// The two package shapes: zero or one entry assembly, zero or more shared contract assemblies, and at
    /// least one of the two.
    /// </summary>
    [Test]
    public void APackageWithNoEntryAssemblyPublishesContractsAndHoldsNoPrivilege()
    {
        var manifest = Valid(builder =>
        {
            builder.Entry = null;
            builder.Capabilities = [];
            builder.ContractAssemblies = ["Example.Contracts.dll"];
        });

        TryValidate(manifest, out var validated, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        validated!.EntryAssembly.Should().BeNull();
        validated.ContractAssemblies.Should().Equal("Example.Contracts.dll");
    }

    [Test]
    public void APackageThatNeitherRunsCodeNorPublishesContractsIsRefused()
        => ShouldHaveDefect(
            Valid(builder =>
            {
                builder.Entry = null;
                builder.Capabilities = [];
            }),
            "entryAssembly",
            CoreErrorCode.PluginManifestInvalid);

    /// <remarks>
    /// A privilege is a statement about what code will be allowed to do, so a package that runs none can
    /// hold none. The forward capability check would refuse the same manifest one step later without a
    /// member to point at.
    /// </remarks>
    [Test]
    public void APackageWithNoEntryAssemblyMayNotHoldAPrivilege()
        => ShouldHaveDefect(
            Valid(builder =>
            {
                builder.Entry = null;
                builder.ContractAssemblies = ["Example.Contracts.dll"];
                builder.Capabilities = ["parsing"];
            }),
            "capabilities",
            CoreErrorCode.PluginManifestInvalid);

    [TestCase("../escape.dll")]
    [TestCase("sub/dir.dll")]
    [TestCase("sub\\dir.dll")]
    [TestCase("contracts.exe")]
    [TestCase("")]
    public void AContractAssemblyThatCouldLeaveThePackageFolderIsRefused(string assembly)
        => ShouldHaveDefect(
            Valid(builder => builder.ContractAssemblies = [assembly]),
            "contractAssemblies[0]",
            CoreErrorCode.PluginManifestInvalid);

    /// <remarks>
    /// Case-insensitively, because a manifest is written once and read wherever the host runs: a list
    /// naming one file on Windows and two on Linux is not a portable declaration.
    /// </remarks>
    [TestCase("Example.Contracts.dll")]
    [TestCase("EXAMPLE.contracts.DLL")]
    public void ADuplicateContractAssemblyIsRefused(string second)
        => ShouldHaveDefect(
            Valid(builder => builder.ContractAssemblies = ["Example.Contracts.dll", second]),
            "contractAssemblies[1]",
            CoreErrorCode.PluginManifestInvalid);

    /// <summary>
    /// Sharing an assembly says its types are one identity everywhere. The entry assembly carries the
    /// module, the parser and the provider implementations, whose isolation, update and unload lifetime is
    /// the thing a package boundary exists to keep separate.
    /// </summary>
    [TestCase("Arronix.Plugin.Example.dll")]
    [TestCase("arronix.plugin.example.DLL")]
    public void TheEntryAssemblyMayNotAlsoBeSharedAsAContractAssembly(string published)
        => ShouldHaveDefect(
            Valid(builder => builder.ContractAssemblies = [published]),
            "contractAssemblies[0]",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void ADependencyIsProvedIntoAnExactPackageAndOneRange()
    {
        var manifest = Valid(builder => builder.Dependencies =
            [new PackageDependencyDeclaration { Package = "example.contracts", Range = ">=0.1 <0.2" }]);

        TryValidate(manifest, out var validated, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        var dependency = validated!.Dependencies.Should().ContainSingle().Which;
        dependency.PackageId.Should().Be(PluginId.FromString("example.contracts"));
        dependency.Range.Text.Should().Be(">=0.1 <0.2");
    }

    [TestCase("Example.Contracts")]
    [TestCase("example..contracts")]
    [TestCase("")]
    public void AMalformedDependencyIdentifierIsRefused(string package)
        => ShouldHaveDefect(
            Valid(builder => builder.Dependencies = [new PackageDependencyDeclaration { Package = package, Range = ">=0.1" }]),
            "dependencies[0].package",
            CoreErrorCode.PluginManifestInvalid);

    /// <remarks>
    /// The one range grammar, reused verbatim. A second reader would be a second meaning for a range the
    /// first time either of them was corrected.
    /// </remarks>
    [TestCase("^0.1.0")]
    [TestCase("0.1.*")]
    [TestCase("")]
    public void AMalformedDependencyRangeIsRefused(string range)
        => ShouldHaveDefect(
            Valid(builder => builder.Dependencies = [new PackageDependencyDeclaration { Package = "example.contracts", Range = range }]),
            "dependencies[0].range",
            CoreErrorCode.PluginManifestInvalid);

    [Test]
    public void APackageMayNotDependOnItself()
        => ShouldHaveDefect(
            Valid(builder => builder.Dependencies =
                [new PackageDependencyDeclaration { Package = "example", Range = ">=0.1" }]),
            "dependencies[0].package",
            CoreErrorCode.PluginManifestInvalid);

    /// <summary>
    /// Two statements about one package are two things the author wrote, at least one of which is not what
    /// they meant. An intersection is a third range neither of them said and taking either is
    /// last-writer-wins, so the manifest is refused with the entry to fix named.
    /// </summary>
    [Test]
    public void OneDependencyStatedTwiceIsRefusedRatherThanIntersected()
        => ShouldHaveDefect(
            Valid(builder => builder.Dependencies =
            [
                new PackageDependencyDeclaration { Package = "example.contracts", Range = ">=0.1 <0.2" },
                new PackageDependencyDeclaration { Package = "example.contracts", Range = ">=0.3 <0.4" }
            ]),
            "dependencies[1].package",
            CoreErrorCode.PluginManifestInvalid);

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
    public void HoldingTheMediaPrivilegeWithoutClaimingAKindIsAccepted()
    {
        var manifest = Valid(builder => builder.Capabilities = ["media-kind"]);

        TryValidate(manifest, out var validated, out var defects).Should().BeTrue(
            "which kinds an extension supplies is derived from the types it registers, so a manifest that "
            + "does not restate them is complete rather than incomplete");

        defects.Should().BeEmpty();
        validated!.MediaKinds.Should().BeEmpty();
    }

    [Test]
    public void AMediaExtensionDeclaringBothIsAccepted()
    {
        var manifest = Valid(builder =>
        {
            builder.Capabilities = ["media-kind"];
            builder.MediaKinds = ["example"];
        });

        TryValidate(manifest, out var validated, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        validated!.MediaKinds.Should().ContainSingle();
        validated.MediaKinds[0].Value.Should().Be("example");
    }

    [TestCase("Title")]
    [TestCase("{Title")]
    [TestCase("Title}")]
    [TestCase("{}")]
    [TestCase("{A}{B}")]
    [TestCase("{---}")]
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
    public void TokensEquivalentUnderTheNamingGrammarAreDuplicates()
    {
        var manifest = Valid(builder => builder.Tokens =
        [
            new NamingToken("{Series Title}", string.Empty, string.Empty),
            new NamingToken("{series.title}", string.Empty, string.Empty)
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

        TryValidate(manifest, out _, out var defects).Should().BeTrue();
        defects.Should().BeEmpty();
    }

    [Test]
    public void ADuplicateIdentifierSchemeIsRefused()
        => ShouldHaveDefect(
            Valid(builder => builder.Identifiers = ["exdb", "exdb"]),
            "identifiers[1]",
            CoreErrorCode.PluginManifestInvalid);

    /// <summary>
    /// A list member written as an explicit JSON null is the same statement as omitting it.
    /// </summary>
    /// <remarks>
    /// The reader refuses malformed JSON and unknown members, and neither describes <c>"tokens": null</c>.
    /// Without this the null reaches the validator as a null reference and takes the whole loader down,
    /// which is one malformed file quarantining every other package with it.
    /// </remarks>
    [Test]
    public void AListMemberWrittenAsNullIsReadAsTheEmptyListItDefaultsTo()
    {
        var manifest = Valid(builder =>
        {
            builder.ContractAssemblies = null!;
            builder.Dependencies = null!;
            builder.MediaKinds = null!;
            builder.Identifiers = null!;
            builder.Tokens = null!;
        });

        TryValidate(manifest, out var validated, out var defects).Should().BeTrue(
            string.Join("; ", defects));

        validated!.ContractAssemblies.Should().BeEmpty();
        validated.Dependencies.Should().BeEmpty();
        validated.MediaKinds.Should().BeEmpty();
        validated.Tokens.Should().BeEmpty();
    }

    [Test]
    public void AnAbsentPolicyGraphBecomesAnEmptyOneRatherThanNull()
    {
        TryValidate(Valid(builder => builder.Policies = null), out var validated, out _)
            .Should().BeTrue();

        validated!.Policies.TotalCount().Should().Be(0);
    }

    private static void ShouldHaveDefect(PluginManifest manifest, string path, CoreErrorCode code)
    {
        TryValidate(manifest, out var validated, out var defects).Should().BeFalse();

        validated.Should().BeNull();
        defects.Should().Contain(defect => defect.Path == path && defect.Code == code);
    }

    private sealed class Builder
    {
        public int SchemaVersion { get; set; } = PluginManifestValidator.SupportedSchemaVersion;

        public string Id { get; set; } = "example";

        public string Name { get; set; } = "Example";

        public string Version { get; set; } = "0.1.0";

        public string Range { get; set; } = ">=0.3 <0.4";

        public string? Entry { get; set; } = "Arronix.Plugin.Example.dll";

        public IReadOnlyList<string> ContractAssemblies { get; set; } = [];

        public IReadOnlyList<PackageDependencyDeclaration> Dependencies { get; set; } = [];

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
            ContractAssemblies = ContractAssemblies,
            Dependencies = Dependencies,
            Capabilities = Capabilities,
            MediaKinds = MediaKinds,
            Identifiers = Identifiers,
            Tokens = Tokens,
            Policies = Policies
        };
    }
}
