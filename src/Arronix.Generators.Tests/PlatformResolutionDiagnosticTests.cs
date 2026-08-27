using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Arronix.Generators.Tests;

/// <summary>What <c>ARX1004</c> says when the referenced contract is incomplete or duplicated.</summary>
/// <remarks>
/// The defect is a property of a reference set, so each case compiles its own <c>Arronix.Abstractions</c>
/// — complete, missing one declaration, or duplicated — and references that image instead of the real one.
/// The control asserts a positive: <c>ARX1003</c> is reachable only through a resolved reading, so a
/// generator that stopped recognizing anything cannot pass the negatives on its own.
/// </remarks>
[TestFixture]
internal sealed class PlatformResolutionDiagnosticTests
{
    /// <summary>The attribute names the platform reads, without their <c>Attribute</c> suffix.</summary>
    /// <remarks>
    /// Spelled out rather than read from <see cref="PlatformSymbols"/>: a symbol added there and not here
    /// fails the control case loudly instead of narrowing what these cases cover.
    /// </remarks>
    private static readonly string[] Annotations =
    [
        "Ignore", "Identity", "Title", "Sortable", "Filterable", "Groupable", "Searchable", "Progress",
        "Status", "Timestamp", "Size", "Artwork", "Disambiguation", "Count", "Ratio", "Multiline",
        "Editable", "Derived", "Display", "Unit", "Prominence"
    ];

    /// <summary>The platform's non-attribute declarations, by namespace and declaration text.</summary>
    private static readonly (string Namespace, string Declaration)[] Contracts =
    [
        ("Arronix.Abstractions.Media", "public interface IMediaEntity { }"),
        ("Arronix.Abstractions.Media", "public interface IMediaItem : IMediaEntity { }"),
        ("Arronix.Abstractions.Media", "public class MediaType<TItem, TTarget, TRelease, TParser> { }"),
        ("Arronix.Abstractions.Media", "public class MediaItem<TItem, TTimeline, TStage> : IMediaItem { }"),
        ("Arronix.Abstractions.Media", "public sealed class GroupDefinition<TItem, TGroup> { }"),
        ("Arronix.Abstractions.Media", "public sealed class WorkbenchDefinition<TItem, TRow> { }"),
        ("Arronix.Abstractions.Media", "public sealed class ArtworkSet { }"),
        ("Arronix.Abstractions.Media", "public sealed class ArtworkImage { }"),
        ("Arronix.Abstractions.Media", "public sealed class ExternalIdSet { }"),
        ("Arronix.Abstractions.Shape", "public sealed class ExternalId { }"),
        ("Arronix.Abstractions.Shape", "public sealed class OrdinalPath { }"),
        ("Arronix.Abstractions.Identity", "public sealed class MediaItemId { }"),
        ("Arronix.Abstractions.FileSystem", "public sealed class PlatformPath { }"),
        ("Arronix.Abstractions.DTOs", "public sealed class Language { }"),
        ("Arronix.Abstractions.DTOs", "public sealed class QualityTier { }")
    ];

    /// <summary>A media declaration, closed over the contract's own base.</summary>
    private const string MediaSource = """
        namespace Sample;

        public sealed class SampleStage { }

        public sealed class SampleTimeline { }

        public sealed class SampleItem :
            global::Arronix.Abstractions.Media.MediaItem<SampleItem, SampleTimeline, SampleStage>
        {
        }

        public {{MODIFIERS}} class SampleMedia :
            global::Arronix.Abstractions.Media.MediaType<SampleItem, SampleItem, SampleItem, SampleItem>
        {
        }
        """;

    /// <summary>A declaration with a base of its own, in a compilation that references the contract.</summary>
    private const string UnrelatedSource = """
        namespace Sample;

        public class Ledger { }

        public sealed class AuditedLedger : Ledger { }
        """;

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> Framework = CreateFrameworkReferences();

    /// <remarks>What a package built against an older contract than the SDK looks like.</remarks>
    [Test]
    public void AContractMissingOneDeclarationIsReportedAtTheDeclarationThatNeededIt()
    {
        var diagnostics = Diagnostics(
            Compose(MediaSource, "sealed partial"),
            [Contract("Arronix.Abstractions", omit: "Prominence")]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(new[] { "ARX1004", "ARX1004" }),
                "the item and the media declaration each need what the reference set does not supply");
            Assert.That(diagnostics[0].GetMessage(),
                Does.Contain("Arronix.Abstractions.Media.ProminenceAttribute"));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("Arronix.Abstractions, Version="));
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Location.SourceSpan.Start),
                Is.Unique,
                "each report is located at its own declaration rather than at the compilation");
        });
    }

    /// <remarks>The control: <c>ARX1003</c> is reachable only through a resolved reading.</remarks>
    [Test]
    public void ACompleteContractIsNotReportedAndIsStillRead()
    {
        var diagnostics = Diagnostics(Compose(MediaSource, "sealed"), [Contract("Arronix.Abstractions")]);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1003" }));
    }

    /// <remarks>An ordinary author's build, which a rule that over-reports would break.</remarks>
    [Test]
    public void ACompleteContractAndAPartialDeclarationReportNothing()
    {
        Assert.That(
            Diagnostics(Compose(MediaSource, "sealed partial"), [Contract("Arronix.Abstractions")]),
            Is.Empty);
    }

    /// <remarks>Both restore and both compile, so the identities are named: the remedy is to drop one.</remarks>
    [Test]
    public void TwoReferencedContractsAreReportedAsTwo()
    {
        var diagnostics = Diagnostics(
            Compose(MediaSource, "sealed partial"),
            [Contract("Arronix.Abstractions"), SecondAnchor("Arronix.Abstractions.Legacy")]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(new[] { "ARX1004", "ARX1004" }));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("references 2 assemblies"));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("Arronix.Abstractions, Version="));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("Arronix.Abstractions.Legacy, Version="));
        });
    }

    /// <remarks>The reference set holds the type and the contract is the wrong one: a different fix.</remarks>
    [Test]
    public void ADeclarationSuppliedByAnotherAssemblyIsNamedAsComingFromIt()
    {
        var diagnostics = Diagnostics(
            Compose(MediaSource, "sealed partial"),
            [Contract("Arronix.Abstractions", omit: "Prominence"), Annotation("Arronix.Abstractions.Extra")]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics[0].GetMessage(),
                Does.Contain("is declared by Arronix.Abstractions.Extra, Version="));
            Assert.That(diagnostics[0].GetMessage(),
                Does.Contain("rather than by the referenced contract Arronix.Abstractions, Version="));
        });
    }

    /// <remarks>Host, Common and every test project reference the contract without authoring media.</remarks>
    [Test]
    public void ACompilationThatReferencesTheContractWithoutAuthoringMediaIsNotReported()
    {
        Assert.That(
            Diagnostics(UnrelatedSource, [Contract("Arronix.Abstractions", omit: "Prominence")]),
            Is.Empty);
    }

    /// <remarks>No contract at all is not an incomplete one.</remarks>
    [Test]
    public void ACompilationWithNoArronixContractIsNotReported()
    {
        Assert.That(Diagnostics(UnrelatedSource, []), Is.Empty);
    }

    /// <remarks>
    /// A shared contract assembly declares the item and no media type, and it is the one a browser is
    /// offered, so it is reported on its own account.
    /// </remarks>
    [Test]
    public void AnItemDeclarationAloneIsReported()
    {
        const string ItemOnly = """
            namespace Sample;

            public sealed class SampleStage { }

            public sealed class SampleTimeline { }

            public sealed class SampleItem :
                global::Arronix.Abstractions.Media.MediaItem<SampleItem, SampleTimeline, SampleStage>
            {
            }
            """;

        var diagnostics = Diagnostics(ItemOnly, [Contract("Arronix.Abstractions", omit: "Artwork")]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1004" }));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("Sample.SampleItem"));
            Assert.That(diagnostics[0].GetMessage(),
                Does.Contain("Arronix.Abstractions.Media.ArtworkAttribute"));
        });
    }

    /// <remarks>A rule that fired while generation continued would describe a failure that did not happen.</remarks>
    [Test]
    public void TheGeneratorsProduceNothingWhileTheReportIsTheOnlyThingSaid()
    {
        var source = Compose(MediaSource, "sealed partial");
        var references = new[] { Contract("Arronix.Abstractions", omit: "Prominence") };

        Assert.Multiple(() =>
        {
            Assert.That(Generated(source, references, new MediaShapeGenerator()), Is.Empty);
            Assert.That(Diagnostics(source, references, new MediaShapeGenerator()), Is.Empty);
            Assert.That(Diagnostics(source, references, new ClientContractGenerator()), Is.Empty);
            Assert.That(
                Diagnostics(source, references).Select(static diagnostic => diagnostic.Id).Distinct(),
                Is.EqualTo(new[] { "ARX1004" }));
        });
    }

    private static string Compose(string template, string modifiers) =>
        template.Replace("{{MODIFIERS}}", modifiers, StringComparison.Ordinal);

    /// <summary>Compiles a contract assembly holding every platform declaration but the named one.</summary>
    private static MetadataReference Contract(string assemblyName, string? omit = null)
    {
        var source = new StringBuilder();

        foreach (var group in Contracts.GroupBy(static declared => declared.Namespace))
        {
            source.Append("namespace ").Append(group.Key).AppendLine();
            source.AppendLine("{");

            foreach (var declared in group)
            {
                source.Append("    ").AppendLine(declared.Declaration);
            }

            source.AppendLine("}");
        }

        source.AppendLine("namespace Arronix.Abstractions.Media");
        source.AppendLine("{");

        foreach (var annotation in Annotations.Where(name => name != omit))
        {
            source.Append("    public sealed class ").Append(annotation)
                .AppendLine("Attribute : global::System.Attribute { }");
        }

        source.AppendLine("}");

        return Emit(assemblyName, source.ToString());
    }

    /// <summary>Compiles a second assembly that also declares the anchor and nothing else.</summary>
    private static MetadataReference SecondAnchor(string assemblyName) =>
        Emit(assemblyName, "namespace Arronix.Abstractions.Media { public interface IMediaEntity { } }");

    /// <summary>Compiles an assembly declaring one platform annotation the contract is missing.</summary>
    private static MetadataReference Annotation(string assemblyName) =>
        Emit(
            assemblyName,
            "namespace Arronix.Abstractions.Media { public sealed class ProminenceAttribute : "
            + "global::System.Attribute { } }");

    private static MetadataReference Emit(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, assemblyName + ".cs")],
            Framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.That(result.Success, Is.True, Report(result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static ImmutableArray<Diagnostic> Diagnostics(
        string source,
        IReadOnlyList<MetadataReference> contracts,
        IIncrementalGenerator? generator = null)
    {
        Run(source, contracts, generator ?? new MediaTypeAuthoringDiagnosticsGenerator(), out var diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<string> Generated(
        string source,
        IReadOnlyList<MetadataReference> contracts,
        IIncrementalGenerator generator) =>
        Run(source, contracts, generator, out _).SyntaxTrees.Skip(1).Select(static tree => tree.ToString()).ToArray();

    private static Compilation Run(
        string source,
        IReadOnlyList<MetadataReference> contracts,
        IIncrementalGenerator generator,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var compilation = CSharpCompilation.Create(
            "PlatformResolution_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Declaration.cs")],
            Framework.AddRange(contracts),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out diagnostics);

        var errors = updated.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Is.Empty, "the source this case reasons over did not compile: " + Report(errors));

        return updated;
    }

    private static string Report(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));

    /// <summary>The framework alone; the real contract would be a second declaration of every type.</summary>
    private static ImmutableArray<MetadataReference> CreateFrameworkReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(static path => !Path.GetFileName(path).StartsWith("Arronix.", StringComparison.Ordinal))
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
