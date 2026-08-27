using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Arronix.Abstractions.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitDoes = global::NUnit.Framework.Does;
using NUnitIs = global::NUnit.Framework.Is;
using NUnitTestCaseAttribute = global::NUnit.Framework.TestCaseAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;
using PinnedMediaShapeGenerator = global::Arronix.Generators.MediaShapeGenerator;

namespace Arronix.Generators.Tests;

/// <summary>
/// The generated item codec travels from its independently compiled domain assembly to the media shape.
/// </summary>
[NUnitTestFixtureAttribute]
internal sealed class GeneratedItemCodecTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

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

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));
}
