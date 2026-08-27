using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Arronix.Abstractions.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Arronix.Generators.Tests;

/// <summary>
/// What the client contract generator refuses to publish.
/// </summary>
/// <remarks>
/// The generator emits a hash it computed from a compile-time model of the framework's serializer. A shape
/// the model does not reproduce must be refused rather than described, because a hash that disagrees with
/// the wire while looking like agreement is worse than no contract at all. Each case here is a shape that
/// would otherwise be published under a wrong hash.
/// </remarks>
[TestFixture]
internal sealed class ClientContractGeneratorTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Arronix.Abstractions.Media;

        namespace Sample;

        public enum SampleStage { Unknown }

        public sealed record SampleTimeline : IReleaseTimeline<SampleStage>
        {
            public SampleStage Stage { get; init; }
            {{TIMELINE}}
        }

        public sealed class SampleItem : MediaItem<SampleItem, SampleTimeline, SampleStage>
        {
            {{ITEM}}
        }
        """;

    private const string Context = """

        [JsonSourceGenerationOptions({{OPTIONS}})]
        [JsonSerializable(typeof(SampleItem))]
        internal sealed partial class SampleContext : JsonSerializerContext;
        """;

    private const string SupportedOptions =
        "JsonSerializerDefaults.Strict, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase";

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    /// <remarks>
    /// Guards every refusal case below: an empty refusal list also describes a generator that found no
    /// entry point at all, so the supported shape has to be shown actually producing one.
    /// </remarks>
    [Test]
    public void TheSupportedShapeIsPublished()
    {
        var generated = Generated(Build());

        Assert.Multiple(() =>
        {
            Assert.That(Refusals(Build()), Is.Empty);
            Assert.That(generated, Does.Contain("SampleItemClientContractEntryPointAttribute(typeof("));
            Assert.That(generated, Does.Contain("SampleContext.Default.SampleItem"));
        });
    }

    /// <remarks>
    /// The declaration is what a browser reads to find the entry point at all, so its absence is reported
    /// where an author can act on it rather than by publishing nothing.
    /// </remarks>
    [Test]
    public void AnItemTypeWithNoDeclaredSerializationContextIsReported()
    {
        var diagnostics = Run(Body(string.Empty, string.Empty));

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1010" }));
    }

    /// <remarks>
    /// A computed member's name is refused wherever it appears in a payload, so it must not also be a name
    /// some other type legitimately carries — the guard would then reject valid payloads. The bug this
    /// pins is subtractive: removing the colliding names from the computed set before looking for a
    /// collision empties the set the collision was in, and the check passes every time.
    /// </remarks>
    [Test]
    public void AComputedMemberSharingALiveMembersNameIsRefused()
    {
        var refusals = Refusals(Build(timeline: "[JsonIgnore] public string Title => string.Empty;"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("title"));
            Assert.That(refusals[0], Does.Contain("computed member"));
        });
    }

    [Test]
    public void ADeclaredOptionThisModelDoesNotDescribeIsRefused()
    {
        var refusals = Refusals(Build(options: SupportedOptions + ", WriteIndented = true"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("WriteIndented"));
        });
    }

    [Test]
    public void DefaultsOtherThanStrictAreRefused()
    {
        var refusals = Refusals(Build(
            options: "JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase"));

        Assert.That(refusals.Single(), Does.Contain("Strict"));
    }

    [Test]
    public void ANamingPolicyOtherThanCamelCaseIsRefused()
    {
        var refusals = Refusals(Build(
            options: "JsonSerializerDefaults.Strict, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower"));

        Assert.That(refusals.Single(), Does.Contain("camel-case"));
    }

    /// <remarks>
    /// A collection the model does not recognize would be described as an object carrying the collection's
    /// own members, which is not what the framework writes.
    /// </remarks>
    [Test]
    public void ACollectionThisModelDoesNotRecognizeIsRefused()
    {
        var refusals = Refusals(Build(item: "public HashSet<string> Tags { get; init; } = new();"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("collection this model does not recognize"));
        });
    }

    [Test]
    public void AMemberCarryingAnUnmodeledSerializationAttributeIsRefused()
    {
        var refusals = Refusals(Build(
            item: "[JsonPropertyName(\"renamed\")] public string? Alias { get; init; }"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("JsonPropertyName"));
        });
    }

    [Test]
    public void ATypeCarryingAnUnmodeledSerializationAttributeIsRefused()
    {
        const string Polymorphic = """

            [JsonPolymorphic]
            public class SampleFacet
            {
                public string? Note { get; init; }
            }
            """;

        var refusals = Refusals(Build(
            item: "public SampleFacet? Facet { get; init; }",
            extra: Polymorphic));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("JsonPolymorphic"));
        });
    }

    [Test]
    public void ADictionaryIsRefused()
    {
        var refusals = Refusals(Build(
            item: "public IReadOnlyDictionary<string, string> Notes { get; init; } = null!;"));

        Assert.That(refusals.Single(), Does.Contain("dictionary"));
    }

    private static string Build(
        string timeline = "",
        string item = "",
        string options = SupportedOptions,
        string extra = "") =>
        Body(timeline, item, extra) + Context.Replace("{{OPTIONS}}", options, StringComparison.Ordinal);

    private static string Body(string timeline, string item, string extra = "") =>
        Preamble
            .Replace("{{TIMELINE}}", timeline, StringComparison.Ordinal)
            .Replace("{{ITEM}}", item, StringComparison.Ordinal)
        + extra;

    private static string[] Refusals(string source) =>
        Run(source)
            .Where(diagnostic => diagnostic.Id == "ARX1011")
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();

    private static string Generated(string source)
    {
        var trees = Compile(source, out _).SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".ClientContract.g.cs", StringComparison.Ordinal))
            .ToArray();

        return trees.Length == 1
            ? trees[0].ToString()
            : throw new InvalidOperationException($"Expected one generated contract, found {trees.Length}.");
    }

    private static ImmutableArray<Diagnostic> Run(string source)
    {
        Compile(source, out var diagnostics);
        return diagnostics;
    }

    private static Compilation Compile(string source, out ImmutableArray<Diagnostic> diagnostics)
    {
        var compilation = CSharpCompilation.Create(
            "ClientContract_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Contract.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ClientContractGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out diagnostics);

        return updated;
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(MediaItem<,,>).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
