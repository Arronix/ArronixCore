using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitDoes = global::NUnit.Framework.Does;
using NUnitHas = global::NUnit.Framework.Has;
using NUnitIs = global::NUnit.Framework.Is;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestCaseAttribute = global::NUnit.Framework.TestCaseAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;
using PinnedMediaShapeGenerator = global::Arronix.Generators.MediaShapeGenerator;

namespace Arronix.Generators.Tests;

[NUnitTestFixtureAttribute]
internal sealed class MediaShapeGeneratorTests
{
    private const string RepresentativeSource = """
        using System;
        using System.Collections.Generic;
        using Arronix.Abstractions.Identity;
        using Arronix.Abstractions.Media;
        using Arronix.Abstractions.Parsing;
        using Arronix.Abstractions.Shape;

        namespace Representative;

        public enum SampleStage
        {
            Announced,
            Released
        }

        public sealed record SampleTimeline : IReleaseTimeline<SampleStage>
        {
            public SampleStage Stage { get; init; }

            public DateOnly? PublicationDate { get; init; }
        }

        public sealed record PublicationDetails(string Binding, int PageCount);

        public sealed class SampleItem : MediaItem<SampleItem, SampleTimeline, SampleStage>
        {
            [Display(
                Name = "Publication details",
                Description = "The edition-specific physical presentation.")]
            public required PublicationDetails Details { get; init; }

            [Ignore]
            public string InternalNote { get; init; } = string.Empty;
        }

        public sealed record SampleRepresentation : IRepresentation;

        public sealed class SampleParser : IReleaseParser<Release<SampleRepresentation>>
        {
            public static ReleaseParseResult<Release<SampleRepresentation>> Parse(ReleaseParseContext context) =>
                ReleaseParseResult<Release<SampleRepresentation>>.Accepted(
                    new Release<SampleRepresentation>(context.Text, null));
        }

        public sealed record DecisionScore(int Value, string Reason);

        public sealed record DecisionRow
        {
            public required DecisionScore Score { get; init; }

            [Editable]
            public bool Accept { get; init; }
        }

        public sealed partial class SampleMedia() :
            MediaType<
                SampleItem,
                ReleaseTarget<SampleItem>,
                Release<SampleRepresentation>,
                SampleParser>(
                    MediaKindId.FromString("samples"),
                    "Sample",
                    "Samples",
                    formats:
                    [
                        new FormatUse<SampleRepresentation>(
                            new FormatFamilyDefinition<SampleRepresentation>
                            {
                                Id = "sample",
                                Name = "Sample",
                                FileExtensions = [".sample"]
                            })
                    ],
                    availability: new OrderedSelectionDefinition<SampleItem, SampleStage>(
                        item => item.Status,
                        "Minimum availability",
                        SampleStage.Released))
        {
            public override IReadOnlyList<IGroupDefinition<SampleItem>> Groups { get; } =
            [
                new GroupDefinition<SampleItem, MediaCollection<SampleItem>>(
                    item => item.Collections,
                    "Collection",
                    "Collections")
            ];

            public override IReadOnlyList<IWorkbenchDefinition<SampleItem>> Workbenches { get; } =
            [
                new WorkbenchDefinition<SampleItem, DecisionRow>("review", "Review")
            ];
        }
        """;

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    [NUnitTestAttribute]
    public void RepresentativeDefinitionProducesExecutableItemGroupAndWorkbenchShapes()
    {
        RequireAssembly(typeof(NUnitAssert), "nunit.framework");
        RequireAssembly(typeof(PinnedMediaShapeGenerator), "Arronix.Generators");

        var generation = Generate(RepresentativeSource);

        AssertNoErrors(generation);
        using var assemblyStream = new MemoryStream();
        var emit = generation.OutputCompilation.Emit(assemblyStream);
        NUnitAssert.That(emit.Success, NUnitIs.True, FormatDiagnostics(emit.Diagnostics));

        assemblyStream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
        var mediaType = RequiredType(assembly, "Representative.SampleMedia");
        var itemType = RequiredType(assembly, "Representative.SampleItem");
        var detailsType = RequiredType(assembly, "Representative.PublicationDetails");
        var rowType = RequiredType(assembly, "Representative.DecisionRow");
        var definition = Activator.CreateInstance(mediaType);
        var catalog = (CompiledShapeCatalog?)mediaType
            .GetProperty(nameof(MediaTypePlaceholder.CompiledShapes))?
            .GetValue(definition);

        NUnitAssert.That(catalog, NUnitIs.Not.Null);
        NUnitAssert.That(catalog!.Item.EntityType, NUnitIs.EqualTo(itemType));

        var title = RequiredField(catalog.Item, "Title");
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(title.Descriptor.ValueKind, NUnitIs.EqualTo(FieldValueKind.Text));
            NUnitAssert.That(title.Descriptor.Semantics & FieldSemantics.Title, NUnitIs.EqualTo(FieldSemantics.Title));
            NUnitAssert.That(title.Descriptor.Semantics & FieldSemantics.Searchable, NUnitIs.EqualTo(FieldSemantics.Searchable));
            NUnitAssert.That(title.Descriptor.Semantics & FieldSemantics.Sortable, NUnitIs.EqualTo(FieldSemantics.Sortable));
        });

        var details = RequiredField(catalog.Item, "Details");
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(details.PropertyType, NUnitIs.EqualTo(detailsType));
            NUnitAssert.That(details.Descriptor.Name, NUnitIs.EqualTo("Publication details"));
            NUnitAssert.That(details.Descriptor.ValueKind, NUnitIs.EqualTo(FieldValueKind.Composite));
            NUnitAssert.That(details.Components.Select(static field => field.PropertyName),
                NUnitIs.EqualTo(new[] { "Binding", "PageCount" }));
            NUnitAssert.That(catalog.Item.Fields.Select(static field => field.PropertyName),
                NUnitDoes.Not.Contain("InternalNote"));
        });

        var item = Activator.CreateInstance(itemType);
        var detailsValue = Activator.CreateInstance(detailsType, "Hardback", 640);
        itemType.GetProperty("Details")!.SetValue(item, detailsValue);
        NUnitAssert.That(details.Read(item!), NUnitIs.SameAs(detailsValue));

        var collectionType = typeof(MediaCollection<>).MakeGenericType(itemType);
        var collection = catalog.Get(collectionType);
        NUnitAssert.That(collection.Fields.Select(static field => field.PropertyName),
            NUnitDoes.Contain("MemberCount"));

        var row = catalog.Get(rowType);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(RequiredField(row, "Accept").Descriptor.Editable, NUnitIs.True);
            NUnitAssert.That(RequiredField(row, "Score").Components.Select(static field => field.PropertyName),
                NUnitIs.EqualTo(new[] { "Value", "Reason" }));
        });
    }

    private static void RequireAssembly(global::System.Type type, string expectedName)
    {
        if (!string.Equals(type.Assembly.GetName().Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected '{type.FullName}' from '{expectedName}', but resolved it from '{type.Assembly.FullName}'.");
        }
    }

    [NUnitTestAttribute]
    public void ValidDefinitionProducesNoGeneratorOrCompilationErrors()
    {
        var generation = Generate(RepresentativeSource);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(generation.RunResult.Diagnostics, NUnitIs.Empty,
                FormatDiagnostics(generation.RunResult.Diagnostics));
            NUnitAssert.That(generation.RunResult.Results, NUnitHas.Length.EqualTo(1));
            NUnitAssert.That(generation.RunResult.Results[0].Diagnostics, NUnitIs.Empty,
                FormatDiagnostics(generation.RunResult.Results[0].Diagnostics));
            NUnitAssert.That(generation.RunResult.Results[0].GeneratedSources, NUnitHas.Length.EqualTo(1));
            NUnitAssert.That(Errors(generation.OutputCompilation), NUnitIs.Empty,
                FormatDiagnostics(generation.OutputCompilation.GetDiagnostics()));
        });
    }

    [NUnitTestAttribute]
    public void GeneratedContractIsDeterministic()
    {
        var first = Generate(RepresentativeSource);
        var second = Generate(RepresentativeSource);

        AssertNoErrors(first);
        AssertNoErrors(second);
        NUnitAssert.That(GeneratedSources(second.RunResult), NUnitIs.EqualTo(GeneratedSources(first.RunResult)));
    }

    [NUnitTestAttribute]
    public void NonPartialDefinitionIsRejectedWithCompilerDiagnostic()
    {
        var source = RepresentativeSource.Replace(
            "public sealed partial class SampleMedia",
            "public sealed class SampleMedia",
            StringComparison.Ordinal);

        var generation = Generate(source);
        var errors = Errors(generation.OutputCompilation);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(generation.RunResult.Diagnostics, NUnitIs.Empty,
                FormatDiagnostics(generation.RunResult.Diagnostics));
            NUnitAssert.That(errors.Select(static diagnostic => diagnostic.Id), NUnitDoes.Contain("CS0260"),
                FormatDiagnostics(errors));
        });
    }


    /// <summary>
    /// The storage bridge reaches the host from any author's package layout, without either half of it
    /// friending the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two compilations, as a real media package is: a domain assembly publishing the item type and its
    /// generated bridge, and a separate assembly declaring the media type over it. Nothing here is Movies,
    /// nothing is in an Arronix namespace, and the second assembly names the first only through its public
    /// surface — no <c>InternalsVisibleTo</c>, no assembly attribute lookup, no type resolved by name at
    /// run time. The reference the generator emits is checked by the compiler.
    /// </para>
    /// <para>
    /// The control is the same declaration compiled against a domain assembly that publishes no bridge:
    /// the catalog is then built without one, which is what a media kind whose item cannot be stored looks
    /// like, rather than a compile error.
    /// </para>
    /// </remarks>
    [NUnitTestCaseAttribute(true, TestName = "AnyAuthorsGeneratedBridgeIsCarriedOntoTheCompiledShapes")]
    [NUnitTestCaseAttribute(false, TestName = "AnAuthorWithNoGeneratedBridgeCompilesWithoutOne")]
    public void AGeneratedBridgeIsCarriedFromTheDomainAssemblyThatPublishesIt(bool publishesBridge)
    {
        const string DomainSource = """
            using System;
            using Arronix.Abstractions.Media;

            namespace Chronicles.Domain;

            public enum ChronicleStage { Drafted, Issued }

            public sealed record ChronicleTimeline : IReleaseTimeline<ChronicleStage>
            {
                public ChronicleStage Stage { get; init; }
            }

            public sealed class Chronicle : MediaItem<Chronicle, ChronicleTimeline, ChronicleStage>;
            """;

        // Appended into the domain source's own file-scoped namespace: this is the shape the client
        // contract generator emits for any item type deriving from the common item.
        const string BridgeSource = """

            [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
            public static class ChronicleItemCodec
            {
                public static global::Arronix.Abstractions.Media.ICompiledItemCodec Declared { get; } = new Compiled();

                private sealed class Compiled : global::Arronix.Abstractions.Media.ICompiledItemCodec
                {
                    public global::System.Type ItemType => typeof(Chronicle);

                    public string MetadataHash => "CHRONICLE";

                    public byte[] Write(global::Arronix.Abstractions.Media.IMediaItem item) => [];

                    public global::Arronix.Abstractions.Media.IMediaItem Read(global::System.ReadOnlySpan<byte> payload) =>
                        new Chronicle { Title = "read", Lifecycle = new ChronicleTimeline() };
                }
            }
            """;

        const string ExtensionSource = """
            using System.Collections.Generic;
            using Arronix.Abstractions.Identity;
            using Arronix.Abstractions.Media;
            using Arronix.Abstractions.Parsing;
            using Arronix.Abstractions.Shape;
            using Chronicles.Domain;

            namespace Chronicles.Extension;

            public sealed record ChronicleRepresentation : IRepresentation;

            public sealed class ChronicleParser : IReleaseParser<Release<ChronicleRepresentation>>
            {
                public static ReleaseParseResult<Release<ChronicleRepresentation>> Parse(ReleaseParseContext context) =>
                    ReleaseParseResult<Release<ChronicleRepresentation>>.Accepted(
                        new Release<ChronicleRepresentation>(context.Text, null));
            }

            public sealed partial class Chronicles() :
                MediaType<
                    Chronicle,
                    ReleaseTarget<Chronicle>,
                    Release<ChronicleRepresentation>,
                    ChronicleParser>(
                        MediaKindId.FromString("chronicles"),
                        "Chronicle",
                        "Chronicles",
                        formats:
                        [
                            new FormatUse<ChronicleRepresentation>(
                                new FormatFamilyDefinition<ChronicleRepresentation>
                                {
                                    Id = "chronicle",
                                    Name = "Chronicle",
                                    FileExtensions = [".chronicle"]
                                })
                        ],
                        availability: new OrderedSelectionDefinition<Chronicle, ChronicleStage>(
                            item => item.Status,
                            "Minimum availability",
                            ChronicleStage.Issued));
            """;

        var domain = CSharpCompilation.Create(
            "Chronicles.Domain_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(publishesBridge ? DomainSource + BridgeSource : DomainSource, ParseOptions)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        NUnitAssert.That(Errors(domain), NUnitIs.Empty, FormatDiagnostics(Errors(domain)));

        using var image = new MemoryStream();
        NUnitAssert.That(domain.Emit(image).Success, NUnitIs.True, "the domain assembly must build");
        image.Position = 0;

        var extension = CSharpCompilation.Create(
            "Chronicles.Extension_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(ExtensionSource, ParseOptions)],
            References.Add(MetadataReference.CreateFromStream(image)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PinnedMediaShapeGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(extension, out var built, out var diagnostics);

        var generated = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Single(source => source.HintName.Contains("Chronicles", StringComparison.Ordinal))
            .SourceText.ToString();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(diagnostics, NUnitIs.Empty, FormatDiagnostics(diagnostics));
            NUnitAssert.That(Errors(built), NUnitIs.Empty, FormatDiagnostics(Errors(built)));

            if (publishesBridge)
            {
                NUnitAssert.That(
                    generated,
                    NUnitDoes.Contain("global::Chronicles.Domain.ChronicleItemCodec.Declared);"),
                    "the extension carries the bridge its own item type's assembly published");
            }
            else
            {
                NUnitAssert.That(
                    generated,
                    NUnitDoes.Not.Contain("ItemCodec"),
                    "and a domain assembly that published none leaves the catalog without one");
            }

            NUnitAssert.That(
                extension.Assembly.GetAttributes()
                    .Select(attribute => attribute.AttributeClass?.Name ?? string.Empty),
                NUnitDoes.Not.Contain("InternalsVisibleToAttribute"),
                "and neither assembly has to friend the other");
        });
    }

    private static Generation Generate(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "RepresentativeMedia.cs");
        var compilation = CSharpCompilation.Create(
            "RepresentativeMedia_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PinnedMediaShapeGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var driverDiagnostics);

        return new Generation(
            driver.GetRunResult(),
            (CSharpCompilation)outputCompilation,
            driverDiagnostics);
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");
        var paths = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(MediaType<,,,>).Assembly.Location)
            .Distinct(StringComparer.Ordinal);

        return paths
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private static IReadOnlyList<Diagnostic> Errors(Compilation compilation) =>
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

    private static void AssertNoErrors(Generation generation)
    {
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(generation.DriverDiagnostics, NUnitIs.Empty,
                FormatDiagnostics(generation.DriverDiagnostics));
            NUnitAssert.That(generation.RunResult.Diagnostics, NUnitIs.Empty,
                FormatDiagnostics(generation.RunResult.Diagnostics));
            NUnitAssert.That(Errors(generation.OutputCompilation), NUnitIs.Empty,
                FormatDiagnostics(generation.OutputCompilation.GetDiagnostics()));
        });
    }

    private static IReadOnlyList<string> GeneratedSources(GeneratorDriverRunResult result) =>
        result.Results
            .SelectMany(static generator => generator.GeneratedSources)
            .OrderBy(static generated => generated.HintName, StringComparer.Ordinal)
            .Select(static generated => generated.HintName + "\n" + generated.SourceText)
            .ToArray();

    private static CompiledField RequiredField(CompiledEntityShape shape, string propertyName) =>
        shape.Fields.Single(field => field.PropertyName == propertyName);

    private static Type RequiredType(Assembly assembly, string typeName) =>
        assembly.GetType(typeName, throwOnError: true)!;

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));

    private sealed record Generation(
        GeneratorDriverRunResult RunResult,
        CSharpCompilation OutputCompilation,
        ImmutableArray<Diagnostic> DriverDiagnostics);

    private abstract class MediaTypePlaceholder
    {
        public abstract CompiledShapeCatalog CompiledShapes { get; }
    }
}
