using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Architecture.Tests.Repository;
using Arronix.Plugin.Movies;

namespace Arronix.Architecture.Tests.Capabilities;

/// <summary>Keeps generated and Host binding mechanics out of the extension-authoring experience.</summary>
[TestFixture]
public sealed class AuthoringSdkSurfaceTests
{
    private static readonly Type[] HiddenBindingTypes =
    [
        typeof(IMediaTypeDefinition),
        typeof(IMediaTypeRegistration),
        typeof(IMediaTypeBinder<>),
        typeof(CompiledField),
        typeof(CompiledEntityShape),
        typeof(CompiledShapeCatalog),
        typeof(IFormatUseVisitor),
        typeof(IGroupDefinitionVisitor<>),
        typeof(ISelectionDefinitionVisitor<>),
        typeof(IMatchAgreementVisitor<>),
        typeof(IDerivationDefinitionVisitor<>),
        typeof(IClosedCataloger),
        typeof(IClosedCurator),
        typeof(ProviderTypeRegistration),
        typeof(LanguageDefinitionRegistration),
    ];

    private static readonly Type[] ConcreteSemanticValues =
    [
        typeof(FormatUse<>),
        typeof(GroupDefinition<,>),
        typeof(OrderedSelectionDefinition<,>),
        typeof(ThresholdSelectionDefinition<>),
        typeof(MatchAgreement<,>),
        typeof(DerivationDefinition<,>),
        typeof(GroupNamingDefinition<,>),
        typeof(GroupNamingSelection<,>),
        typeof(GroupSummaryDefinition<,>),
        typeof(WorkbenchDefinition<,>),
    ];

    private static readonly MemberInfo[] HiddenErasedMembers =
    [
        typeof(IFormatUse).GetMethod("Accept")!,
        typeof(IGroupDefinition<>).GetMethod("Accept")!,
        typeof(ISelectionDefinition<>).GetMethod("Accept")!,
        typeof(IMatchAgreement<>).GetMethod("Accept")!,
        typeof(IDerivationDefinition<>).GetMethod("Accept")!,
        typeof(IQueryArgumentDefinition<>).GetProperty("Property")!,
        typeof(IGroupNamingDefinition<>).GetProperty("GroupType")!,
        typeof(IGroupNamingSelection<>).GetProperty("GroupType")!,
        typeof(ITokenFallbackDefinition<>).GetProperty("Property")!,
        typeof(IGroupSummaryDefinition<>).GetProperty("GroupType")!,
        typeof(IGroupSummaryDefinition<>).GetProperty("Headline")!,
        typeof(IGroupSummaryFieldDefinition).GetProperty("Value")!,
        typeof(ISortDefinition<>).GetProperty("Property")!,
        typeof(IItemPropertyDefinition<>).GetProperty("Property")!,
        typeof(IWorkbenchDefinition<>).GetProperty("RowType")!,
    ];

    private static readonly string[] BindingVocabulary =
    [
        "CompiledShape",
        "CompiledEntity",
        "CompiledField",
        "IMediaTypeDefinition",
        "IMediaTypeRegistration",
        "IMediaTypeBinder",
        "IFormatUseVisitor",
        "IGroupDefinitionVisitor",
        "ISelectionDefinitionVisitor",
        "IMatchAgreementVisitor",
        "IDerivationDefinitionVisitor",
        "LambdaExpression",
        ".Capture(",
    ];

    [Test]
    public void GeneratedAndErasedBridgeTypesAreMarkedAsNonAuthoring()
    {
        var visible = HiddenBindingTypes
            .Where(static type => type.GetCustomAttribute<EditorBrowsableAttribute>()?.State
                != EditorBrowsableState.Never)
            .Select(static type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            visible,
            Is.Empty,
            "cross-assembly visibility is an implementation necessity, not an invitation to author against the bridge");
    }

    [Test]
    public void VisitorDispatchAndErasedCarriersAreMarkedAsNonAuthoring()
    {
        var visible = HiddenErasedMembers
            .Where(static member => member.GetCustomAttribute<EditorBrowsableAttribute>()?.State
                != EditorBrowsableState.Never)
            .Select(static member => $"{member.DeclaringType!.Name}.{member.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            visible,
            Is.Empty,
            "heterogeneous collections retain typed author values while their Host-only erased view stays advanced");
    }

    [Test]
    public void AConcreteMediaTypeHidesItsGeneratedShapeAndExposesNoCaptureMember()
    {
        var publicMembers = typeof(Movies)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(static member => member.Name)
            .ToArray();
        var compiledShapes = typeof(Movies).GetProperty(
            "CompiledShapes",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(compiledShapes, Is.Not.Null);
            Assert.That(
                compiledShapes!.GetCustomAttribute<EditorBrowsableAttribute>()?.State,
                Is.EqualTo(EditorBrowsableState.Never),
                "the immutable generator proof requires a public override, but it is not authoring vocabulary");
            Assert.That(publicMembers, Does.Not.Contain("Capture"));
            Assert.That(
                typeof(MediaType<,,,>).Assembly.GetType(
                    "Arronix.Abstractions.Media.MediaTypeRegistration",
                    throwOnError: true)!.IsPublic,
                Is.False,
                "the capture factory has no supported author use");
        });
    }

    [Test]
    public void ConcreteSemanticValuesExposeNoVisitorOrTypeCarrier()
    {
        var mechanics = ConcreteSemanticValues
            .SelectMany(static type => type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(static member => member.Name is "Accept" or "GroupType" or "RowType")
            .Select(static member => $"{member.DeclaringType!.Name}.{member.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            mechanics,
            Is.Empty,
            "an author constructs typed values; only the hidden erased interface dispatches them to Host");
    }

    [Test]
    public void TypedMediaExtensionSourceNamesNoBindingMechanic()
    {
        var offenders = TypedMediaExtensions()
            .SelectMany(static project => RepositoryLayout.Files(project, "*.cs"))
            .SelectMany(static path => File.ReadAllLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .SelectMany(entry => BindingVocabulary
                .Where(binding => entry.Line.Contains(binding, StringComparison.Ordinal))
                .Select(binding => $"{RepositoryLayout.Relative(entry.Path)}:{entry.Number}: {binding}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "generated getters, visitors, erasure and capture are SDK implementation details, never media declarations");
    }

    [Test]
    public void EachTypedMediaModulePerformsOneMediaRegistration()
    {
        foreach (var project in TypedMediaExtensions())
        {
            var registrations = RepositoryLayout.Files(project, "*.cs")
                .SelectMany(File.ReadAllLines)
                .Count(static line => line.Contains("AddMediaType<", StringComparison.Ordinal));

            Assert.That(
                registrations,
                Is.EqualTo(1),
                $"'{project}' should name its media type once and let generated capture do the rest");
        }
    }

    [Test]
    public void TheSdkPackageCarriesTheGeneratorAsAnAnalyzerAsset()
    {
        var sdkProject = ProjectFile.Load(RepositoryLayout.Sdk);
        var sdk = sdkProject.Document;
        var generatorReference = sdk.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Arronix.Generators", StringComparison.Ordinal) == true);
        var packagedGenerator = sdk.Descendants("TfmSpecificPackageFile").Single();
        var generator = ProjectFile.Load(RepositoryLayout.Generators).Document;

        Assert.Multiple(() =>
        {
            Assert.That(sdkProject.RuntimeProjectReferences, Is.EqualTo(new[] { RepositoryLayout.Abstractions }));
            Assert.That(sdkProject.AnalyzerProjectReferences, Is.EqualTo(new[] { RepositoryLayout.Generators }));
            Assert.That(Metadata(generatorReference, "OutputItemType"), Is.EqualTo("Analyzer"));
            Assert.That(Metadata(generatorReference, "ReferenceOutputAssembly"), Is.EqualTo("false"));
            Assert.That(Metadata(generatorReference, "PrivateAssets"), Is.EqualTo("all"));
            Assert.That(Metadata(packagedGenerator, "Include"), Is.EqualTo("@(Analyzer)"));
            Assert.That(
                Metadata(packagedGenerator, "PackagePath")?.Replace('\\', '/'),
                Is.EqualTo("analyzers/dotnet/cs"));
            Assert.That(sdk.Descendants("IncludeBuildOutput").Single().Value, Is.EqualTo("false"));
            Assert.That(generator.Descendants("IsPackable").Single().Value, Is.EqualTo("false"));
        });
    }

    private static IReadOnlyList<string> TypedMediaExtensions() =>
        RepositoryLayout.MediaExtensionProjects
            .Where(project => RepositoryLayout.Files(project, "*.cs")
                .Any(path => File.ReadAllText(path).Contains("MediaType<", StringComparison.Ordinal)))
            .ToArray();

    private static string? Metadata(XElement element, string name) =>
        (string?)element.Attribute(name) ?? (string?)element.Element(name);
}
