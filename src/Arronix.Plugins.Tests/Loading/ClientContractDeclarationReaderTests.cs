using System.IO;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Tests.Support;
using FluentAssertions.Execution;

namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// What an assembly's bytes are allowed to say about the client contracts it declares.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is well-formed metadata. That is the point: a reader written against the generator's
/// output accepts all of them, because the generator never produces any of them. What decides whether a
/// declaration may be published is whether it is the shape the platform defined, and each case below is a
/// different way of not being it while looking exactly like it.
/// </para>
/// <para>
/// A defect never fails staging. Whether a file is an admissible assembly and whether it may also be
/// offered to a browser are different questions, and answering the first with the second would cost an
/// installation a working media kind over a browser-only defect.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ClientContractDeclarationReaderTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("arronix-declarations").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Test]
    public void AWellFormedDeclarationIsRead()
    {
        var staged = Stage("Fixture.Good", new CompiledDeclaration.Declared("Entry"));

        using var assertions = new AssertionScope();
        staged.ClientContracts.Defects.Should().BeEmpty();
        staged.ClientContracts.Declarations.Should().HaveCount(1);

        var declaration = staged.ClientContracts.Declarations[0];
        declaration.EntryPointType.Should().Be("Fixture.Declared.Entry");
        declaration.EntityTypeName.Should().Be(CompiledDeclaration.EntityTypeName);
        declaration.GeneratedMetadataHash.Should().Be(CompiledDeclaration.ValidHash);
    }

    /// <remarks>
    /// The video format's shape, and the one that must not be mistaken for a defect: an assembly owning no
    /// item declares nothing, and declaring nothing is valid.
    /// </remarks>
    [Test]
    public void AnAssemblyDeclaringNothingIsRead()
    {
        var staged = Stage("Fixture.Silent");

        using var assertions = new AssertionScope();
        staged.ClientContracts.Declarations.Should().BeEmpty();
        staged.ClientContracts.Defects.Should().BeEmpty();
    }

    /// <remarks>
    /// Three strings decode cleanly. Without reading the signature, the first would be published as an
    /// entity type that nothing had type-checked and that no compiler had ever resolved.
    /// </remarks>
    [Test]
    public void ADeclarationWhoseConstructorIsNotTheDeclaredShapeIsADefect()
    {
        var staged = Stage(
            "Fixture.Signature",
            new CompiledDeclaration.Declared(
                "Entry",
                Takes: CompiledDeclaration.Signature.ThreeStrings));

        Defects(staged).Should().ContainSingle()
            .Which.Should().Contain("(System.Type, string, string)");
    }

    [Test]
    public void ADeclarationNamingAnEntityThisAssemblyDoesNotDefineIsADefect()
    {
        var staged = Stage(
            "Fixture.Foreign",
            new CompiledDeclaration.Declared("Entry", Entity: CompiledDeclaration.ForeignEntity));

        Defects(staged).Should().ContainSingle()
            .Which.Should().Contain("this assembly does not define");
    }

    /// <remarks>
    /// Case included in the rule, deliberately. Two spellings of one hash compare unequal unless every
    /// reader remembers to fold them, and the one that forgets is the one that lets a mismatch through.
    /// </remarks>
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", TestName = "lower case")]
    [TestCase("0123456789ABCDEF", TestName = "too short")]
    [TestCase("Z123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", TestName = "not hexadecimal")]
    public void ADeclarationWhoseHashIsNotRenderedThisWayIsADefect(string hash)
    {
        var staged = Stage(
            "Fixture.Hash",
            new CompiledDeclaration.Declared("Entry", MetadataHash: hash));

        Defects(staged).Should().ContainSingle()
            .Which.Should().Contain("64 upper-case hexadecimal");
    }

    /// <remarks>
    /// A consumer resolves a contract by the name a declaration carries, so two answering to one name leave
    /// it no way to choose. Publishing either would be publishing a coin toss.
    /// </remarks>
    [Test]
    public void TwoDeclarationsForOneEntityAreADefect()
    {
        var staged = Stage(
            "Fixture.Ambiguous",
            new CompiledDeclaration.Declared("First"),
            new CompiledDeclaration.Declared("Second"));

        Defects(staged).Should().ContainSingle()
            .Which.Should().Contain("2 client contracts for entity type");
    }

    /// <remarks>
    /// The identity rule, from the outside. This assembly defines its own attribute under the platform's
    /// exact namespace and name, so a reader matching on those would publish whatever it says. Deriving
    /// from a type the assembly defines produces a base type definition rather than a reference, and only
    /// a reference resolved through this host's own contract assembly is a declaration.
    /// </remarks>
    [Test]
    public void AnAttributeMerelyNamedLikeTheDeclarationIsNotOne()
    {
        var staged = Stage(
            "Fixture.Impostor",
            CompiledDeclaration.Base.LocallyDeclared,
            new CompiledDeclaration.Declared("Entry"));

        using var assertions = new AssertionScope();
        staged.ClientContracts.Declarations.Should().BeEmpty();
        staged.ClientContracts.Defects.Should().BeEmpty(
            "an attribute that is not this platform's declaration is not a malformed one");
    }

    /// <remarks>
    /// The rule every case above depends on. Staging decides whether a file is an admissible assembly; a
    /// declaration decides whether that assembly may also be offered to a browser. A host that conflated
    /// them would quarantine a package, and every dependant of its shared contract with it, over a defect
    /// no dependant can observe.
    /// </remarks>
    [Test]
    public void ADefectiveDeclarationStillStages()
    {
        var path = CompiledDeclaration.Write(
            _folder,
            "Fixture.Staged",
            CompiledDeclaration.Base.Platform,
            new CompiledDeclaration.Declared("Entry", MetadataHash: "not-a-hash"));

        var staged = StagedAssembly.TryStage(path, out var assembly, out var error);

        using var assertions = new AssertionScope();
        staged.Should().BeTrue(error ?? "the file is a readable managed assembly");
        assembly!.Identity.Name.Should().Be("Fixture.Staged");
        assembly.ClientContracts.Defects.Should().NotBeEmpty();
    }

    private static IReadOnlyList<string> Defects(StagedAssembly staged)
        => [.. staged.ClientContracts.Defects];

    private StagedAssembly Stage(string assemblyName, params CompiledDeclaration.Declared[] declarations)
        => Stage(assemblyName, CompiledDeclaration.Base.Platform, declarations);

    private StagedAssembly Stage(
        string assemblyName,
        CompiledDeclaration.Base from,
        params CompiledDeclaration.Declared[] declarations)
    {
        var path = CompiledDeclaration.Write(_folder, assemblyName, from, declarations);

        StagedAssembly.TryStage(path, out var staged, out var error).Should().BeTrue(error ?? assemblyName);
        return staged!;
    }
}
