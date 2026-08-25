using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace Arronix.Generators.Tests;

[TestFixture]
internal sealed class MediaTypeAuthoringDiagnosticsGeneratorTests
{
    private const string Source = """
        using System;
        using Arronix.Abstractions.Identity;
        using Arronix.Abstractions.Media;
        using Arronix.Abstractions.Parsing;
        using Arronix.Abstractions.Shape;

        namespace Representative;

        public enum Stage
        {
            Unknown
        }

        public sealed record Timeline : IReleaseTimeline<Stage>
        {
            public Stage Stage { get; init; }
            public DateOnly? PublicationDate { get; init; }
        }

        public sealed class Target : IReleaseTarget;
        public sealed class Publication : IRelease;
        public sealed record Representation : IRepresentation;

        public sealed class Parser : IReleaseParser<Publication>
        {
            public static ReleaseParseResult<Publication> Parse(ReleaseParseContext context) =>
                throw new NotSupportedException();
        }

        public sealed class SampleMedia() :
            MediaType<MediaItem<Timeline, Stage>, Target, Publication, Parser>(
                MediaKindId.FromString("samples"),
                "Sample",
                "Samples",
                formats:
                [
                    new FormatUse<Representation>(
                        new FormatFamilyDefinition<Representation>
                        {
                            Id = "sample",
                            Name = "Sample",
                            FileExtensions = [".sample"]
                        })
                ],
                availability: new ThresholdSelectionDefinition<MediaItem<Timeline, Stage>>(
                    "availability",
                    "Availability",
                    "days",
                    ThresholdDirection.AtLeast,
                    0));
        """;

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    [Test]
    public void AConcreteMediaTypeWithoutPartialIsReportedAtItsDeclaration()
    {
        var diagnostics = Analyze(Source);
        var diagnostic = diagnostics.Single();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Id, Is.EqualTo("ARX1003"));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.Location.GetLineSpan().Path, Is.EqualTo("Authoring.cs"));
            Assert.That(
                diagnostic.GetMessage(),
                Is.EqualTo("Media type 'SampleMedia' must be declared partial so Arronix can generate its compiled shape"));
        });
    }

    [Test]
    public void AGeneratedCompanionDeclarationDoesNotHideTheAuthoringDiagnostic()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(Source, ParseOptions, "Authoring.cs");
        var compilation = CSharpCompilation.Create(
            "Authoring_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [
                new MediaShapeGenerator().AsSourceGenerator(),
                new MediaTypeAuthoringDiagnosticsGenerator().AsSourceGenerator()
            ],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("ARX1003"));
    }

    [Test]
    public void TheAnalyzerAssemblyExposesBothComponentsToRoslynDiscovery()
    {
        var reference = new AnalyzerFileReference(
            typeof(MediaShapeGenerator).Assembly.Location,
            new TestAnalyzerAssemblyLoader());

        Assert.Multiple(() =>
        {
            Assert.That(
                reference.GetAnalyzers(LanguageNames.CSharp),
                Is.Empty);
            Assert.That(
                reference.GetGenerators(LanguageNames.CSharp).Length,
                Is.EqualTo(2));
        });
    }

    [Test]
    public void APartialMediaTypeNeedsNoAuthoringDiagnostic()
    {
        var source = Source.Replace(
            "public sealed class SampleMedia",
            "public sealed partial class SampleMedia",
            StringComparison.Ordinal);

        Assert.That(Analyze(source), Is.Empty);
    }

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Authoring.cs");
        var compilation = CSharpCompilation.Create(
            "Authoring_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new MediaTypeAuthoringDiagnosticsGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        return diagnostics;
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

    private sealed class TestAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) =>
            AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
