using System;
using System.Collections.Generic;
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
/// A shape the model cannot reproduce is refused rather than described: a hash that disagrees with the wire
/// while looking like agreement is worse than no contract.
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

        {{ENTITY_ATTRIBUTE}}
        public sealed class SampleItem : MediaItem<SampleItem, SampleTimeline, SampleStage>
        {
            {{ITEM}}
        }
        {{EXTRA}}
        {{CONTEXT}}
        """;

    private const string Context = """

        [JsonSourceGenerationOptions({{OPTIONS}})]
        [JsonSerializable(typeof(SampleItem){{TARGET}})]
        internal sealed partial class SampleContext : JsonSerializerContext;
        """;

    /// <summary>
    /// The half the framework's own generator would write, written by hand.
    /// </summary>
    /// <remarks>
    /// That generator is not in this driver, so without this every case would compile with the context's
    /// abstract members unimplemented — and a case allowed to have errors is a case that can pass over
    /// source which never compiled. It exists to compile; nothing here runs it.
    /// </remarks>
    private const string Half = """

        partial class {{NAME}}
        {
            public {{NAME}}() : base(new global::System.Text.Json.JsonSerializerOptions())
            {
            }

            public static {{NAME}} Default { get; } = new();

            protected override global::System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

            public override global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
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
            Assert.That(generated, Does.Contain("SampleContext.Default.GetTypeInfo(typeof("));
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
    /// A computed member's name is refused wherever it appears, so it must not be a name another type
    /// carries. Subtracting the collisions before looking for them empties the set they were in.
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

    /// <remarks>
    /// Two contexts can declare different options, so picking either would publish a hash describing the
    /// wire the other one writes.
    /// </remarks>
    [Test]
    public void TwoContextsClaimingOneEntityAreRefused()
    {
        const string Second = """
            [JsonSourceGenerationOptions(JsonSerializerDefaults.Strict, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
            [JsonSerializable(typeof(SampleItem))]
            internal sealed partial class SecondContext : JsonSerializerContext;
            """;

        var refusals = Refusals(Build(extra: Second, halves: "SecondContext"));

        Assert.That(refusals.Single(), Does.Contain("more than one serialization context"));
    }

    [Test]
    public void AnUntypedValueIsRefused()
    {
        var refusals = Refusals(Build(item: "public object? Anything { get; init; }"));

        Assert.That(refusals.Single(), Does.Contain("untyped value"));
    }

    [Test]
    public void AnInterfaceValueIsRefused()
    {
        const string Contract = """

            public interface ISampleFacet { string? Note { get; } }
            """;

        var refusals = Refusals(Build(item: "public ISampleFacet? Facet { get; init; }", extra: Contract));

        Assert.That(refusals.Single(), Does.Contain("is an interface"));
    }

    [Test]
    public void AnAbstractValueIsRefused()
    {
        const string Abstract = """

            public abstract class SampleFacet { public string? Note { get; init; } }
            """;

        var refusals = Refusals(Build(item: "public SampleFacet? Facet { get; init; }", extra: Abstract));

        Assert.That(refusals.Single(), Does.Contain("is abstract"));
    }

    /// <remarks>
    /// A compiler lists a nested generic's own arguments; a runtime lists its containing type's as well.
    /// The two renderings would disagree while describing the same type.
    /// </remarks>
    [Test]
    public void AGenericNestedInsideAnotherTypeIsRefused()
    {
        const string Nested = """

            public static class Outer
            {
                public sealed record Inner<T>(T Value);
            }
            """;

        var refusals = Refusals(Build(item: "public Outer.Inner<string>? Nested { get; init; }", extra: Nested));

        Assert.That(refusals.Single(), Does.Contain("nested inside another type"));
    }

    [Test]
    public void AMultidimensionalArrayIsRefused()
    {
        var refusals = Refusals(Build(item: "public int[,]? Grid { get; init; }"));

        Assert.That(refusals.Single(), Does.Contain("multidimensional array"));
    }

    /// <remarks>
    /// The framework's attributes are matched by symbol identity. An author's own type with the same name
    /// is a different type, so it neither exempts a member nor is mistaken for a shape this model refuses.
    /// </remarks>
    [Test]
    public void AnAuthorsOwnAttributeWithAFrameworkNameIsNotTheFrameworksAttribute()
    {
        const string Decoy = """
            using System;

            namespace Sample.Decoy;

            [AttributeUsage(AttributeTargets.Property)]
            public sealed class JsonIgnoreAttribute : Attribute;
            """;

        var decoyed = Build(
            timeline: "[Sample.Decoy.JsonIgnore] public string Note => string.Empty;",
            outside: Decoy);
        var genuine = Build(timeline: "[JsonIgnore] public string Note => string.Empty;");

        Assert.Multiple(() =>
        {
            Assert.That(Refusals(decoyed), Is.Empty);
            Assert.That(Computed(Generated(decoyed)), Does.Not.Contain("note"),
                "the decoy exempts nothing, so that member is an ordinary one");

            Assert.That(Refusals(genuine), Is.Empty);
            Assert.That(Computed(Generated(genuine)), Does.Contain("note"),
                "the framework's attribute does, and its name is then refused in a payload");
        });
    }

    /// <remarks>
    /// The harder case, and the one a metadata-name lookup gets wrong: the impostor is in the framework's
    /// exact namespace with the framework's exact name, so a use site in this compilation binds to it and
    /// a compilation-first lookup hands it back as the framework's own. Reading it as the instruction that
    /// keeps a member off the wire, while the real serializer writes that member, is the failure — and it
    /// would look like a working contract until a payload arrived.
    /// </remarks>
    [Test]
    public void AnImpostorInTheFrameworksOwnNamespaceIsNotTheFrameworksAttribute()
    {
        const string Impostor = """
            namespace System.Text.Json.Serialization;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class JsonIgnoreAttribute : System.Attribute;
            """;

        var impersonated = Build(
            timeline: "[JsonIgnore] public string Note => string.Empty;",
            outside: Impostor);

        Assert.Multiple(() =>
        {
            Assert.That(Refusals(impersonated), Is.Empty);
            Assert.That(Computed(Generated(impersonated)), Does.Not.Contain("note"),
                "a package's own attribute keeps nothing off the wire, whatever it is called");
            Assert.That(Computed(Generated(impersonated)), Does.Contain("status"),
                "and the framework's own is still found, from the assembly that declares it");
        });
    }

    /// <remarks>
    /// The same lookup, for the declaration that says a context exists at all. An impostor
    /// <c>JsonSerializable</c> in the framework's namespace declares nothing this generator can read, so the
    /// item type has no context and is reported rather than published against a context that is not one.
    /// </remarks>
    [Test]
    public void AnImpostorSerializableAttributeDeclaresNoContext()
    {
        const string Impostor = """
            namespace System.Text.Json.Serialization;

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class JsonSerializableAttribute(System.Type type) : System.Attribute
            {
                public System.Type Type { get; } = type;
            }
            """;

        const string ImpostorContext = """

            [JsonSerializable(typeof(SampleItem))]
            internal sealed partial class ImpostorContext : JsonSerializerContext;
            """;

        var diagnostics = Run(Body(
            string.Empty,
            string.Empty,
            extra: ImpostorContext,
            outside: Impostor,
            halves: "ImpostorContext"));

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1010" }));
    }

    /// <remarks>
    /// The same rule for the context itself: a class deriving something else called
    /// <c>JsonSerializerContext</c>, or carrying something else called <c>JsonSerializable</c>, is not a
    /// declared serialization context and the item type has none.
    /// </remarks>
    [Test]
    public void AnAuthorsOwnContextWithAFrameworkNameIsNotADeclaredContext()
    {
        const string Decoy = """
            using System;

            namespace Sample.Decoy;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class JsonSerializableAttribute(Type type) : Attribute
            {
                public Type Type { get; } = type;
            }
            """;

        const string DecoyContext = """

            [Sample.Decoy.JsonSerializable(typeof(SampleItem))]
            internal sealed partial class DecoyContext;
            """;

        var diagnostics = Run(Body(string.Empty, string.Empty, extra: DecoyContext, outside: Decoy));

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1010" }));
    }

    /// <remarks>
    /// An enumeration reaches the wire as a number in its underlying type, so widening one changes what a
    /// payload carries while nothing about the member moves.
    /// </remarks>
    [Test]
    public void AnEnumerationsUnderlyingTypeIsPartOfTheDescribedShape()
    {
        const string Carries = "public SampleStage Second { get; init; }";

        var narrow = Build(item: Carries);
        var wide = narrow with
        {
            Contract = narrow.Contract.Replace(
                "public enum SampleStage { Unknown }",
                "public enum SampleStage : long { Unknown }",
                StringComparison.Ordinal),
        };

        Assert.Multiple(() =>
        {
            Assert.That(wide.Contract, Does.Contain(": long"), "the widening actually happened");
            Assert.That(Declaration(Generated(wide)), Is.Not.EqualTo(Declaration(Generated(narrow))));
        });
    }

    /// <summary>Reads the member names the generated contract refuses to accept from a payload.</summary>
    private static string[] Computed(string generated)
    {
        const string Marker = "Derived =";
        var start = generated.IndexOf(Marker, StringComparison.Ordinal);

        if (start < 0)
        {
            return [];
        }

        var end = generated.IndexOf("});", start, StringComparison.Ordinal);

        return generated[start..end]
            .Split('"')
            .Where(static (part, index) => index % 2 == 1)
            .ToArray();
    }

    /// <summary>Reads the emitted declaration line, which carries both hashes.</summary>
    private static string Declaration(string generated) =>
        generated.Split('\n').First(line => line.StartsWith("[assembly:", StringComparison.Ordinal));

    /// <remarks>
    /// The root is reached by nothing, so a rule applied only where a member declares a type never sees it.
    /// </remarks>
    [Test]
    public void AnUnmodeledAttributeOnTheEntityItselfIsRefused()
    {
        var refusals = Refusals(Build(
            item: "public string? Note { get; init; }",
            entityAttribute: "[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("JsonNumberHandling"));
            Assert.That(refusals[0], Does.Contain("the entity itself"));
        });
    }

    /// <remarks>
    /// The element of a recognized sequence is reached without any member declaring it, so it is described
    /// where it is dequeued rather than where a property named the list.
    /// </remarks>
    [Test]
    public void AnUnrecognizedSequenceInsideARecognizedOneIsRefused()
    {
        var refusals = Refusals(Build(
            item: "public IReadOnlyList<HashSet<string>> Groups { get; init; } = [];"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("collection this model does not recognize"));
            Assert.That(refusals[0], Does.Contain("the elements of"));
        });
    }

    /// <remarks>
    /// Measured on the pinned SDK: a public field carrying <c>[JsonInclude]</c> is serialized even with
    /// <c>IncludeFields</c> off, so it would reach the wire without appearing in the digest.
    /// </remarks>
    [Test]
    public void AFieldTheFrameworkSerializesIsRefused()
    {
        var refusals = Refusals(Build(item: "[JsonInclude] public string? OpenField;"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("is a field the framework serializes"));
        });
    }

    [Test]
    public void ANonPublicMemberTheFrameworkSerializesIsRefused()
    {
        var refusals = Refusals(Build(item: "[JsonInclude] internal string? Hidden { get; set; }"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("without being publicly readable"));
        });
    }

    /// <remarks>
    /// The framework honours a named constructor whatever its accessibility, so the model reads it rather
    /// than looking at the public ones and silently choosing something else.
    /// </remarks>
    [Test]
    public void ANonPublicNamedConstructorIsTheOneModeled()
    {
        const string Facet = """

            public sealed class SampleFacet
            {
                public SampleFacet() => Note = "parameterless";

                [JsonConstructor]
                private SampleFacet(string note) => Note = note;

                public string Note { get; }
            }
            """;

        var generated = Generated(Build(item: "public SampleFacet? Facet { get; init; }", extra: Facet));

        Assert.That(generated, Is.Not.Empty, "the shape is published rather than refused");
    }

    [Test]
    public void MoreThanOneNamedConstructorIsRefused()
    {
        const string Facet = """

            public sealed class SampleFacet
            {
                [JsonConstructor]
                public SampleFacet(string note) => Note = note;

                [JsonConstructor]
                public SampleFacet(string note, int _) => Note = note;

                public string Note { get; }
            }
            """;

        var refusals = Refusals(Build(item: "public SampleFacet? Facet { get; init; }", extra: Facet));

        Assert.That(refusals.Single(), Does.Contain("more than one constructor"));
    }

    /// <remarks>
    /// A serialization-only mode generates the write fast path and no metadata, so a reader has nothing.
    /// Metadata alone is enough, the default inherits both, and a combined value has no named field at all
    /// — which is why the flag is read rather than the member name.
    /// </remarks>
    [Test]
    public void AGenerationModeWithoutMetadataIsRefused()
    {
        var refusals = Refusals(Build(target: ", GenerationMode = JsonSourceGenerationMode.Serialization"));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain("no metadata to read with"));
        });
    }

    [TestCase("JsonSourceGenerationMode.Default")]
    [TestCase("JsonSourceGenerationMode.Metadata")]
    [TestCase("JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization")]
    public void AGenerationModeCarryingMetadataIsPublished(string mode)
    {
        Assert.That(Refusals(Build(target: ", GenerationMode = " + mode)), Is.Empty);
    }

    /// <remarks>
    /// The property the framework's generator names after the type is never read — the root is asked for by
    /// type — so renaming it changes nothing, including the hashes. Admitted deliberately, not by omission.
    /// </remarks>
    [Test]
    public void RenamingTheGeneratedPropertyChangesNothing()
    {
        var renamed = Build(target: ", TypeInfoPropertyName = \"SomethingElse\"");

        Assert.Multiple(() =>
        {
            Assert.That(Refusals(renamed), Is.Empty);
            Assert.That(Generated(renamed), Does.Contain("GetTypeInfo(typeof("));
            Assert.That(Generated(renamed), Does.Not.Contain("Default.SomethingElse"));
            Assert.That(Declaration(Generated(renamed)), Is.EqualTo(Declaration(Generated(Build()))),
                "both hashes are unchanged");
        });
    }

    /// <remarks>
    /// The framework runs these against the value on the way in or out, so a graph carrying one does
    /// something no rendering of its members describes. Refused before load rather than at run time.
    /// </remarks>
    [TestCase("IJsonOnSerializing", "OnSerializing")]
    [TestCase("IJsonOnSerialized", "OnSerialized")]
    [TestCase("IJsonOnDeserializing", "OnDeserializing")]
    [TestCase("IJsonOnDeserialized", "OnDeserialized")]
    public void ATypeTheFrameworkRunsCodeForIsRefused(string contract, string member)
    {
        var facet = $$"""

            public sealed class SampleFacet : {{contract}}
            {
                public string? Note { get; init; }
                public void {{member}}()
                {
                }
            }
            """;

        var refusals = Refusals(Build(item: "public SampleFacet? Facet { get; init; }", extra: facet));

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Has.Length.EqualTo(1));
            Assert.That(refusals[0], Does.Contain(contract));
            Assert.That(refusals[0], Does.Contain("runs code against the value"));
        });
    }

    /// <remarks>
    /// A constructor parameter's nullability is its own. Reading the member's instead leaves two contracts
    /// that read a missing or null value differently hashing alike; both types below expose a member that
    /// is not nullable.
    /// </remarks>
    [Test]
    public void AParameterNullabilityDifferentFromItsMembersChangesTheHash()
    {
        const string Strict = """

            public sealed class SampleFacet
            {
                public SampleFacet(string note) => Note = note;

                public string Note { get; }
            }
            """;

        const string Lenient = """

            public sealed class SampleFacet
            {
                public SampleFacet(string? note) => Note = note ?? string.Empty;

                public string Note { get; }
            }
            """;

        var strict = Generated(Build(item: "public SampleFacet? Facet { get; init; }", extra: Strict));
        var lenient = Generated(Build(item: "public SampleFacet? Facet { get; init; }", extra: Lenient));

        Assert.That(
            Declaration(lenient),
            Is.Not.EqualTo(Declaration(strict)),
            "the member is not nullable in either, so only the parameter can have moved the hash");
    }

    /// <remarks>
    /// A default decides what a member becomes when a payload omits it, so two contracts differing only in
    /// one are different contracts.
    /// </remarks>
    [Test]
    public void AConstructorDefaultChangesTheHash()
    {
        const string Without = """

            public sealed class SampleFacet
            {
                public SampleFacet(int count) => Count = count;

                public int Count { get; }
            }
            """;

        var with = Without.Replace("int count)", "int count = 3)", StringComparison.Ordinal);

        var absent = Generated(Build(item: "public SampleFacet? Facet { get; init; }", extra: Without));
        var present = Generated(Build(item: "public SampleFacet? Facet { get; init; }", extra: with));

        Assert.That(Declaration(present), Is.Not.EqualTo(Declaration(absent)));
    }

    [Test]
    public void ADefaultThisModelCannotRenderIsRefused()
    {
        const string Facet = """

            public sealed class SampleFacet
            {
                public SampleFacet(SampleStage stage = SampleStage.Unknown) => Stage = stage;

                public SampleStage Stage { get; }
            }
            """;

        var refusals = Refusals(Build(item: "public SampleFacet? Facet { get; init; }", extra: Facet));

        Assert.That(refusals.Single(), Does.Contain("default value this model does not render"));
    }

    [Test]
    public void ADictionaryIsRefused()
    {
        var refusals = Refusals(Build(
            item: "public IReadOnlyDictionary<string, string> Notes { get; init; } = null!;"));

        Assert.That(refusals.Single(), Does.Contain("dictionary"));
    }

    private static Sources Build(
        string timeline = "",
        string item = "",
        string options = SupportedOptions,
        string extra = "",
        string outside = "",
        string entityAttribute = "",
        string target = "",
        params string[] halves) =>
        new(
            Compose(
                timeline,
                item,
                extra,
                entityAttribute,
                Context
                    .Replace("{{OPTIONS}}", options, StringComparison.Ordinal)
                    .Replace("{{TARGET}}", target, StringComparison.Ordinal),
                ["SampleContext", .. halves]),
            outside);

    private static Sources Body(
        string timeline,
        string item,
        string extra = "",
        string outside = "",
        params string[] halves) =>
        new(Compose(timeline, item, extra, string.Empty, string.Empty, halves), outside);

    private const string ContextTargetPlaceholder = "{{TARGET}}";

    private static string Compose(
        string timeline,
        string item,
        string extra,
        string entityAttribute,
        string context,
        IReadOnlyList<string> halves)
    {
        var source = Preamble
            .Replace("{{TIMELINE}}", timeline, StringComparison.Ordinal)
            .Replace("{{ITEM}}", item, StringComparison.Ordinal)
            .Replace("{{EXTRA}}", extra, StringComparison.Ordinal)
            .Replace("{{ENTITY_ATTRIBUTE}}", entityAttribute, StringComparison.Ordinal)
            .Replace("{{CONTEXT}}", context, StringComparison.Ordinal)
            .Replace(ContextTargetPlaceholder, string.Empty, StringComparison.Ordinal);

        foreach (var name in halves)
        {
            source += Half.Replace("{{NAME}}", name, StringComparison.Ordinal);
        }

        return source;
    }

    /// <summary>One case's sources: the contract itself, and anything it shares a compilation with.</summary>
    /// <remarks>
    /// A second file rather than more text in the first. A namespace appended to a file-scoped one is a
    /// compile error, and a type that does not compile shadows nothing — which is how a case meant to prove
    /// shadowing passes without testing it.
    /// </remarks>
    private sealed record Sources(string Contract, string Companion);

    /// <summary>The refusals a case produced, having first required that it produced nothing else.</summary>
    private static string[] Refusals(Sources source)
    {
        var diagnostics = Run(source);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id).Distinct(StringComparer.Ordinal),
            Is.SubsetOf(new[] { "ARX1011" }),
            "a refusal case reports refusals and nothing else");

        return diagnostics.Select(diagnostic => diagnostic.GetMessage()).ToArray();
    }

    private static string Generated(Sources source)
    {
        var trees = Compile(source, out _).SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".ClientContract.g.cs", StringComparison.Ordinal))
            .ToArray();

        return trees.Length == 1
            ? trees[0].ToString()
            : throw new InvalidOperationException($"Expected one generated contract, found {trees.Length}.");
    }

    private static ImmutableArray<Diagnostic> Run(Sources source)
    {
        Compile(source, out var diagnostics);
        return diagnostics;
    }

    private static Compilation Compile(Sources source, out ImmutableArray<Diagnostic> diagnostics)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source.Contract, ParseOptions, "Contract.cs") };

        if (!string.IsNullOrWhiteSpace(source.Companion))
        {
            trees.Add(CSharpSyntaxTree.ParseText(source.Companion, ParseOptions, "Companion.cs"));
        }

        var compilation = CSharpCompilation.Create(
            "ClientContract_" + Guid.NewGuid().ToString("N"),
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ClientContractGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out diagnostics);

        // Every case, including the generated output, compiles. A regression that reasons over source
        // which did not compile proves nothing about source that does.
        var errors = updated.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id + " " + diagnostic.Location.GetLineSpan() + ": " + diagnostic.GetMessage())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(errors, Is.Empty, "the source this case reasons over did not compile");

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
