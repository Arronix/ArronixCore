using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Arronix.Generators.Tests;

/// <summary>
/// What a generator recognizes a platform type by, when the compilation declares one spelled like it.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on the System.Text.Json impostor cases in <see cref="ClientContractGeneratorTests"/>, and for
/// the same reason: a namespace and a name are not an identity. Anybody may declare
/// <c>Sample.Csv.IgnoreAttribute</c>, or <c>Arronix.Abstractions.Media.MediaType&lt;,,,&gt;</c>, or
/// <c>System.DateOnly</c>. Every one of those was accepted by a short-name, display-string or
/// metadata-name reading, and each acceptance is silent: a real field vanishes from Host's compiled shapes
/// and from the client projection while JSON still carries it, an unrelated attribute turns an ordinary
/// property into an identity, and an impostor base gets a generated projection it never declared.
/// </para>
/// <para>
/// Each case is a pair. The negative shows the lookalike deciding nothing, and the positive shows the
/// genuine type still deciding everything, because a generator that stopped recognizing anything would
/// pass every negative on its own. Every compilation — the case's own source and whatever the generator
/// added to it — is required to produce no errors, so no case can pass over source that never compiled.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class PlatformSymbolTests
{
    /// <summary>A complete media declaration, with the item body and the definition body supplied.</summary>
    private const string MediaSource = """
        using System;
        using System.Collections.Generic;
        using Arronix.Abstractions.DTOs;
        using Arronix.Abstractions.Identity;
        using Arronix.Abstractions.Media;
        using Arronix.Abstractions.Parsing;
        using Arronix.Abstractions.Shape;

        namespace Sample;

        public enum SampleStage
        {
            Announced,
            Released
        }

        public sealed record SampleTimeline : IReleaseTimeline<SampleStage>
        {
            public SampleStage Stage { get; init; }
        }

        public sealed record SampleRepresentation : IRepresentation;

        public sealed class SampleParser : IReleaseParser<Release<SampleRepresentation>>
        {
            public static ReleaseParseResult<Release<SampleRepresentation>> Parse(ReleaseParseContext context) =>
                ReleaseParseResult<Release<SampleRepresentation>>.Accepted(
                    new Release<SampleRepresentation>(context.Text, null));
        }

        public record DecisionRow(string Reason);

        public sealed record DecoyGroup(string Name);

        public sealed class SampleGroup : IMediaEntity
        {
            public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

            public required string Title { get; init; }

            public Language? TitleLanguage { get; init; }

            public string? Overview { get; init; }

            public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

            public int Status { get; init; }

            public int Collections { get; init; }

            public int CatalogState { get; init; }

            public int Ordinary { get; init; }
        }

        public sealed class SampleItem : MediaItem<SampleItem, SampleTimeline, SampleStage>
        {
        {{ITEM}}
        }

        public {{MODIFIERS}} class SampleMedia() :
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
        {{MEDIA}}
        }
        """;

    /// <summary>An item type and the serialization context its client contract needs.</summary>
    private const string ClientSource = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Arronix.Abstractions.Media;

        namespace Sample;

        public enum SampleStage
        {
            Unknown
        }

        public sealed record SampleTimeline : IReleaseTimeline<SampleStage>
        {
            public SampleStage Stage { get; init; }
        }

        public record DecisionRow(string Reason);

        public sealed class SampleItem : MediaItem<SampleItem, SampleTimeline, SampleStage>
        {
        {{ITEM}}
        }

        [JsonSourceGenerationOptions(JsonSerializerDefaults.Strict, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        [JsonSerializable(typeof(SampleItem))]
        internal sealed partial class SampleContext : JsonSerializerContext;

        partial class SampleContext
        {
            public SampleContext() : base(new global::System.Text.Json.JsonSerializerOptions())
            {
            }

            public static SampleContext Default { get; } = new();

            protected override global::System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

            public override global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    /// <summary>An item property that pulls the group into the catalog as a related shape.</summary>
    private const string GroupMember = """
            public IReadOnlyList<SampleGroup> Sections { get; init; } = [];
        """;

    /// <summary>Two annotations of a package's own, spelled exactly like two of the platform's.</summary>
    private const string UnrelatedAnnotations = """
        namespace Sample.Csv;

        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class IgnoreAttribute : System.Attribute;

        [System.AttributeUsage(System.AttributeTargets.Property)]
        public sealed class IdentityAttribute : System.Attribute;
        """;

    /// <summary>
    /// A media base of the package's own, in the platform's namespace under the platform's name.
    /// </summary>
    /// <remarks>
    /// Its constructor takes the same arguments under the same names, so the declaration that closes it is
    /// the same source either way and only what the name binds to differs. It declares no
    /// <c>CompiledShapes</c>, so a compilation that closes it needs no generated projection and reports no
    /// error for the absence of one.
    /// </remarks>
    private const string MediaTypeImpostor = """
        namespace Arronix.Abstractions.Media;

        public abstract class MediaType<TItem, TTarget, TRelease, TParser>(
            global::Arronix.Abstractions.Identity.MediaKindId kind,
            string singularName,
            string pluralName,
            global::System.Collections.Generic.IReadOnlyList<IFormatUse> formats,
            ISelectionDefinition<TItem> availability)
            where TItem : class, IMediaItem
        {
        }
        """;

    /// <summary>An item base of the package's own, under the platform's exact name.</summary>
    private const string MediaItemImpostor = """
        namespace Arronix.Abstractions.Media;

        public class MediaItem<TItem, TReleaseTimeline, TReleaseStage>
        {
        }
        """;

    /// <summary>
    /// Group and workbench declarations of the package's own, under the platform's exact names.
    /// </summary>
    /// <remarks>
    /// Only the namespace, the name and the type-parameter names have to match: those are what the display
    /// string the generator compared was made of. Neither carries the platform contract, so nothing but the
    /// generator's own reading can mistake one for a real declaration.
    /// </remarks>
    private const string DefinitionImpostors = """
        namespace Arronix.Abstractions.Media;

        public sealed record GroupDefinition<TItem, TGroup>(string SingularName, string PluralName);

        public sealed record WorkbenchDefinition<TItem, TRow>(string Id, string Name);
        """;

    /// <summary>A calendar date of the package's own, under the framework's exact name.</summary>
    private const string DateOnlyImpostor = """
        namespace System;

        public readonly record struct DateOnly(int DayNumber);
        """;

    /// <summary>A value of the package's own, under the exact name of a framework scalar.</summary>
    private const string GuidImpostor = """
        namespace System;

        public readonly record struct Guid(int Marker);
        """;

    /// <summary>A collection of the package's own, under the exact name of a framework sequence.</summary>
    private const string ListImpostor = """
        namespace System.Collections.Generic;

        public sealed class List<T>;
        """;

    /// <summary>A keyed collection of the package's own, under the framework's exact name.</summary>
    private const string DictionaryImpostor = """
        namespace System.Collections.Generic;

        public interface IReadOnlyDictionary<TKey, TValue>;
        """;

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    /// <remarks>
    /// The defect this pins is silent in both directions at once: <c>Sample.Csv.Ignore</c> removed a real
    /// field from Host's compiled shapes while the serializer went on writing it, and
    /// <c>Sample.Csv.Identity</c> gave an ordinary property the platform's identity semantics.
    /// </remarks>
    [Test]
    public void AnUnrelatedIgnoreOrIdentityDecidesNothingAboutACompiledShape()
    {
        const string Item = """
                [Sample.Csv.Ignore]
                public string Note { get; init; } = string.Empty;

                [Sample.Csv.Identity]
                public string Ledger { get; init; } = string.Empty;
            """;

        var item = ItemShape(MediaSource, Item, companion: UnrelatedAnnotations);
        var note = Field(item, "Note");
        var ledger = Field(item, "Ledger");

        Assert.Multiple(() =>
        {
            Assert.That(note.Descriptor.ValueKind, Is.EqualTo(FieldValueKind.Text),
                "an unrelated [Ignore] removes no field a payload still carries");
            Assert.That(ledger.ExplicitIdentity, Is.False);
            Assert.That(ledger.Descriptor.Semantics & FieldSemantics.Identity, Is.EqualTo(FieldSemantics.None),
                "an unrelated [Identity] confers no identity semantics");
        });
    }

    [Test]
    public void ThePlatformsOwnIgnoreAndIdentityStillDecideACompiledShape()
    {
        const string Item = """
                [Ignore]
                public string Note { get; init; } = string.Empty;

                [Identity]
                public string Ledger { get; init; } = string.Empty;
            """;

        var item = ItemShape(MediaSource, Item);
        var ledger = Field(item, "Ledger");

        Assert.Multiple(() =>
        {
            Assert.That(item.Fields.Select(static field => field.PropertyName), Does.Not.Contain("Note"));
            Assert.That(ledger.ExplicitIdentity, Is.True);
            Assert.That(ledger.Descriptor.Semantics & FieldSemantics.Identity, Is.EqualTo(FieldSemantics.Identity));
        });
    }

    /// <remarks>
    /// The same two annotations, on the other output generated from the same reading. Host's shapes and a
    /// browser's projection have to agree about which fields exist, so both are asked.
    /// </remarks>
    [Test]
    public void AnUnrelatedIgnoreOrIdentityDecidesNothingAboutAClientProjection()
    {
        const string Item = """
                [Sample.Csv.Ignore]
                public string Note { get; init; } = string.Empty;

                [Sample.Csv.Identity]
                public string Ledger { get; init; } = string.Empty;
            """;

        var unrelated = ClientContract(ClientSource, Item, companion: UnrelatedAnnotations);
        var genuine = ClientContract(ClientSource, """
                [Ignore]
                public string Note { get; init; } = string.Empty;

                [Identity]
                public string Ledger { get; init; } = string.Empty;
            """);

        Assert.Multiple(() =>
        {
            Assert.That(unrelated, Does.Contain("FieldId = \"note\""));
            Assert.That(Semantics(unrelated, "ledger"), Is.EqualTo(0));
            Assert.That(genuine, Does.Not.Contain("FieldId = \"note\""));
            Assert.That(Semantics(genuine, "ledger") & 1, Is.EqualTo(1));
        });
    }

    /// <remarks>
    /// The base a display-string comparison gets wrong. A package declaring the platform's media base binds
    /// its own declaration to it, and the generator then hands that declaration a Host-binding projection
    /// over a base which never asked for one.
    /// </remarks>
    [Test]
    public void AnImpostorMediaBaseReceivesNoCompiledShape()
    {
        var generated = Generated(
            Compose(MediaSource, string.Empty, string.Empty),
            MediaTypeImpostor,
            new MediaShapeGenerator());

        Assert.That(generated, Is.Empty);
    }

    [Test]
    public void ThePlatformsOwnMediaBaseStillReceivesACompiledShape()
    {
        var generated = Generated(
            Compose(MediaSource, string.Empty, string.Empty),
            companion: null,
            new MediaShapeGenerator());

        Assert.That(generated.Single(), Does.Contain("CompiledShapes"));
    }

    /// <remarks>
    /// The same base, read by the diagnostic that tells an author their declaration is not partial. Against
    /// an impostor it must say nothing, because there is no generated projection for the missing modifier
    /// to block.
    /// </remarks>
    [Test]
    public void AnImpostorMediaBaseIsNotHeldToTheAuthoringDiagnostic()
    {
        var diagnostics = Diagnostics(
            Compose(MediaSource, string.Empty, string.Empty, modifiers: "sealed"),
            MediaTypeImpostor,
            new MediaTypeAuthoringDiagnosticsGenerator());

        Assert.That(diagnostics, Is.Empty);
    }

    /// <remarks>
    /// The control implements <c>CompiledShapes</c> by hand, which an author never does, so that the case
    /// compiles cleanly while its declaration is deliberately not partial. Without it the missing member is
    /// an error, and the case would be asserting over source that did not compile.
    /// </remarks>
    [Test]
    public void ThePlatformsOwnMediaBaseIsStillHeldToTheAuthoringDiagnostic()
    {
        const string Media = """
                public override CompiledShapeCatalog CompiledShapes { get; } =
                    new(
                        new CompiledEntityShape
                        {
                            EntityType = typeof(SampleItem),
                            Fields = Array.Empty<CompiledField>()
                        },
                        Array.Empty<CompiledEntityShape>());
            """;

        var diagnostics = Diagnostics(
            Compose(MediaSource, string.Empty, Media, modifiers: "sealed"),
            companion: null,
            new MediaTypeAuthoringDiagnosticsGenerator());

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1003" }));
    }

    /// <remarks>
    /// The item base the client contract generator finds an entry point by. An impostor gets no entry
    /// point, and no diagnostic either: a type that is not a media item is not an author's mistake.
    /// </remarks>
    [Test]
    public void AnImpostorItemBaseReceivesNoClientContract()
    {
        var source = Compose(ClientSource, string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(Generated(source, MediaItemImpostor, new ClientContractGenerator()), Is.Empty);
            Assert.That(Diagnostics(source, MediaItemImpostor, new ClientContractGenerator()), Is.Empty);
        });
    }

    [Test]
    public void ThePlatformsOwnItemBaseStillReceivesAClientContract()
    {
        var generated = Generated(
            Compose(ClientSource, string.Empty),
            companion: null,
            new ClientContractGenerator());

        Assert.That(generated.Single(), Does.Contain("SampleItemClientContractEntryPointAttribute(typeof("));
    }

    /// <remarks>
    /// Group and workbench declarations are matched where they are constructed, so an impostor puts its
    /// second type argument into the catalog as a shape the media type never declared. Host would then hold
    /// a projection of a type no group or workbench uses.
    /// </remarks>
    [Test]
    public void ImpostorGroupAndWorkbenchDeclarationsContributeNoShape()
    {
        const string Media = """
                private static readonly object Group =
                    new GroupDefinition<SampleItem, DecoyGroup>("Decoy", "Decoys");

                private static readonly object Workbench =
                    new WorkbenchDefinition<SampleItem, DecisionRow>("review", "Review");
            """;

        var loaded = Shapes(Compose(MediaSource, string.Empty, Media), DefinitionImpostors);

        Assert.Multiple(() =>
        {
            Assert.That(() => loaded.Catalog.Get(loaded.Type("Sample.DecoyGroup")),
                Throws.ArgumentException);
            Assert.That(() => loaded.Catalog.Get(loaded.Type("Sample.DecisionRow")),
                Throws.ArgumentException);
        });
    }

    [Test]
    public void ThePlatformsOwnGroupAndWorkbenchDeclarationsStillContributeShapes()
    {
        const string Media = """
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
            """;

        var loaded = Shapes(Compose(MediaSource, string.Empty, Media), companion: null);
        var row = loaded.Catalog.Get(loaded.Type("Sample.DecisionRow"));

        Assert.That(row.Fields.Select(static field => field.PropertyName), Is.EqualTo(new[] { "Reason" }));
    }

    /// <remarks>
    /// Framework shapes were classified the same way, so a package's own <c>System.DateOnly</c> was read as
    /// a date. What it actually is, to everything downstream, is an ordinary value with one component.
    /// </remarks>
    [Test]
    public void AnImpostorFrameworkShapeIsNotClassifiedAsThatShape()
    {
        const string Item = """
                public DateOnly Released { get; init; }
            """;

        var released = Field(ItemShape(MediaSource, Item, DateOnlyImpostor), "Released");

        Assert.That(released.Descriptor.ValueKind, Is.EqualTo(FieldValueKind.Composite));
    }

    [Test]
    public void TheFrameworksOwnShapeIsStillClassifiedAsThatShape()
    {
        const string Item = """
                public DateOnly Released { get; init; }
            """;

        var released = Field(ItemShape(MediaSource, Item), "Released");

        Assert.That(released.Descriptor.ValueKind, Is.EqualTo(FieldValueKind.Date));
    }

    /// <remarks>
    /// A record's equality contract is never publicly readable, so the ordinary public-property rule
    /// already omits it and nothing needs to read the name. Reading the name cost an entity that used the
    /// identifier a real field, in Host's shapes and in the client projection, while the serializer went on
    /// writing it.
    /// </remarks>
    [Test]
    public void AnOrdinaryPropertyNamedEqualityContractStaysVisible()
    {
        const string Item = """
                public string EqualityContract { get; init; } = string.Empty;
            """;

        var field = Field(ItemShape(MediaSource, Item), "EqualityContract");
        var contract = ClientContract(ClientSource, Item);

        Assert.Multiple(() =>
        {
            Assert.That(field.Descriptor.ValueKind, Is.EqualTo(FieldValueKind.Text));
            Assert.That(contract, Does.Contain("FieldId = \"equalityContract\""));
            Assert.That(contract, Does.Contain("value.EqualityContract"),
                "and the serialization model describes it, so the emitted hash covers it");
        });
    }

    /// <remarks>
    /// The other half of the same question, as observable output: a record contributes the members it
    /// declares and nothing the compiler added, which the ordinary public-property rule already decides.
    /// </remarks>
    [Test]
    public void ARecordContributesOnlyTheMembersItDeclares()
    {
        const string Item = """
                public DecisionRow Decision { get; init; } = new(string.Empty);
            """;

        var decision = Field(ItemShape(MediaSource, Item), "Decision");
        var contract = ClientContract(ClientSource, Item);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Components.Select(static field => field.PropertyName),
                Is.EqualTo(new[] { "Reason" }));
            Assert.That(contract, Does.Contain("FieldId = \"reason\""));
            Assert.That(contract, Does.Not.Contain("equalityContract"));
        });
    }

    /// <remarks>
    /// The serialization model classified framework shapes by the same rendered name it writes into the
    /// digest, so a package's own scalar and the framework's rendered identically and hashed alike. They
    /// are not the same wire: the framework writes one value for its own, and an object with members for
    /// anything else.
    /// </remarks>
    [Test]
    public void AnImpostorScalarIsNotDescribedAsThatScalarsWire()
    {
        const string Item = """
                public Guid Marker { get; init; }
            """;

        Assert.That(
            MetadataHash(ClientContract(ClientSource, Item, GuidImpostor)),
            Is.Not.EqualTo(MetadataHash(ClientContract(ClientSource, Item))),
            "a package's own value spelled like a framework scalar is a different wire, and the declared "
            + "hashes have to say so");
    }

    /// <remarks>The same reading for sequences: a recognized one carries elements, and an object does not.</remarks>
    [Test]
    public void AnImpostorSequenceIsNotDescribedAsASequence()
    {
        const string Item = """
                public List<string> Tags { get; init; } = new();
            """;

        Assert.That(
            MetadataHash(ClientContract(ClientSource, Item, ListImpostor)),
            Is.Not.EqualTo(MetadataHash(ClientContract(ClientSource, Item))),
            "a package's own collection spelled like a framework sequence is a different wire");
    }

    /// <remarks>
    /// Dictionaries are refused because key handling is not modeled. The impostor is refused too, but as
    /// what it actually is — an interface whose payload the declaration does not state — which is how the
    /// refusal shows that the reading is no longer the name.
    /// </remarks>
    [Test]
    public void AnImpostorKeyedCollectionIsNotRefusedAsADictionary()
    {
        const string Item = """
                public IReadOnlyDictionary<string, string> Notes { get; init; } = null!;
            """;

        var impostor = Refusal(Compose(ClientSource, Item), DictionaryImpostor);
        var genuine = Refusal(Compose(ClientSource, Item), companion: null);

        Assert.Multiple(() =>
        {
            Assert.That(genuine, Does.Contain("is a dictionary"));
            Assert.That(impostor, Does.Contain("is an interface"));
        });
    }

    /// <remarks>
    /// The symbols are the consumer's own, so the analyzer must not carry the contract it resolves them
    /// from. A runtime reference would also load a second Arronix.Abstractions into the compiler.
    /// </remarks>
    [Test]
    public void TheAnalyzerAssemblyReferencesNoArronixContract()
    {
        var referenced = typeof(MediaShapeGenerator).Assembly.GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name?.StartsWith("Arronix", StringComparison.Ordinal) == true);

        Assert.That(referenced, Is.Empty);
    }

    /// <remarks>
    /// <c>Status</c>, <c>Collections</c> and <c>CatalogState</c> are conventional defaults for the members
    /// of the common item shape, and every related shape — a group, a workbench row — is read at top level
    /// too. Ungated, the names alone made an unrelated group property Secondary or Diagnostic, and Host
    /// then held a prominence the author never declared for a type that has no such member.
    /// </remarks>
    [Test]
    public void ABareEntitysConventionallyNamedPropertiesTakeTheOrdinaryProminence()
    {
        var loaded = Shapes(Compose(MediaSource, GroupMember), companion: null);
        var group = loaded.Catalog.Get(loaded.Type("Sample.SampleGroup"));

        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "Status", "Collections", "CatalogState", "Ordinary" })
            {
                Assert.That(Field(group, name).Descriptor.Prominence, Is.EqualTo(Prominence.Detail), name);
            }
        });
    }

    [Test]
    public void AnItemsConventionallyNamedPropertiesStillTakeTheirDefaults()
    {
        var item = Shapes(Compose(MediaSource, GroupMember), companion: null).Catalog.Item;

        Assert.Multiple(() =>
        {
            Assert.That(Field(item, "Status").Descriptor.Prominence, Is.EqualTo(Prominence.Secondary));
            Assert.That(Field(item, "Collections").Descriptor.Prominence, Is.EqualTo(Prominence.Secondary));
            Assert.That(Field(item, "CatalogState").Descriptor.Prominence, Is.EqualTo(Prominence.Diagnostic));
        });
    }

    private static string Compose(
        string template,
        string item,
        string media = "",
        string modifiers = "sealed partial") =>
        template
            .Replace("{{ITEM}}", item, StringComparison.Ordinal)
            .Replace("{{MEDIA}}", media, StringComparison.Ordinal)
            .Replace("{{MODIFIERS}}", modifiers, StringComparison.Ordinal);

    private static CompiledEntityShape ItemShape(string template, string item, string? companion = null) =>
        Shapes(Compose(template, item), companion).Catalog.Item;

    private static CompiledField Field(CompiledEntityShape shape, string propertyName) =>
        shape.Fields.SingleOrDefault(field => field.PropertyName == propertyName)
        ?? throw new InvalidOperationException($"'{propertyName}' is not in the generated shape.");

    private static string ClientContract(string template, string item, string? companion = null) =>
        Generated(Compose(template, item), companion, new ClientContractGenerator()).Single();

    /// <summary>
    /// Reads the generated-metadata hash out of the declared entry point's constructor arguments.
    /// </summary>
    /// <remarks>
    /// The first of the two literals, and specifically not the projection hash beside it: the projection
    /// describes an unrecognized value by walking its members, so it differs for a lookalike whatever the
    /// serialization model decided, and a case comparing both would pass without asking that model
    /// anything.
    /// </remarks>
    private static string MetadataHash(string generated)
    {
        var start = generated.IndexOf("[assembly:", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "the contract declares no entry point");

        var open = generated.IndexOf('"', start) + 1;
        return generated.AsSpan(open, generated.IndexOf('"', open) - open).ToString();
    }

    private static string Refusal(string source, string? companion)
    {
        var diagnostics = Diagnostics(source, companion, new ClientContractGenerator());

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(new[] { "ARX1011" }));

        return diagnostics[0].GetMessage();
    }

    private static int Semantics(string generated, string fieldId)
    {
        const string Marker = "FieldSemantics)";
        var identifier = $"FieldId = \"{fieldId}\"";
        var field = generated.IndexOf(identifier, StringComparison.Ordinal);

        Assert.That(field, Is.GreaterThanOrEqualTo(0), $"'{fieldId}' is not in the generated schema");
        Assert.That(generated.IndexOf(identifier, field + 1, StringComparison.Ordinal), Is.LessThan(0),
            $"'{fieldId}' names more than one descriptor, so this reads an arbitrary one");

        var value = generated.IndexOf(Marker, field, StringComparison.Ordinal) + Marker.Length;
        return int.Parse(
            generated.AsSpan(value, generated.IndexOf(',', value) - value),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Runs one generator and returns what it added, having required the result to compile.</summary>
    private static IReadOnlyList<string> Generated(
        string source,
        string? companion,
        IIncrementalGenerator generator)
    {
        var original = Parse(source, companion).Length;

        return Run(source, companion, generator, out _).SyntaxTrees
            .Skip(original)
            .Select(static tree => tree.ToString())
            .ToArray();
    }

    /// <summary>Runs one generator and returns what it reported, having required the result to compile.</summary>
    private static ImmutableArray<Diagnostic> Diagnostics(
        string source,
        string? companion,
        IIncrementalGenerator generator)
    {
        Run(source, companion, generator, out var diagnostics);
        return diagnostics;
    }

    private static Loaded Shapes(string source, string? companion)
    {
        var compilation = Run(source, companion, new MediaShapeGenerator(), out _);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        Assert.That(emit.Success, Is.True, Report(emit.Diagnostics));

        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var definition = assembly.GetType("Sample.SampleMedia", throwOnError: true)!;
        var catalog = definition
            .GetProperty("CompiledShapes", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(Activator.CreateInstance(definition));

        return new Loaded(assembly, (CompiledShapeCatalog)catalog!);
    }

    private static Compilation Run(
        string source,
        string? companion,
        IIncrementalGenerator generator,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var compilation = CSharpCompilation.Create(
            "PlatformSymbols_" + Guid.NewGuid().ToString("N"),
            Parse(source, companion),
            References,
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

    private static SyntaxTree[] Parse(string source, string? companion) =>
        companion is null
            ? [CSharpSyntaxTree.ParseText(source, ParseOptions, "Declaration.cs")]
            :
            [
                CSharpSyntaxTree.ParseText(source, ParseOptions, "Declaration.cs"),

                // A second file, not more text in the first: a namespace appended to a file-scoped one is a
                // compile error, and a type that does not compile shadows nothing.
                CSharpSyntaxTree.ParseText(companion, ParseOptions, "Companion.cs"),
            ];

    private static string Report(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(MediaType<,,,>).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private sealed record Loaded(Assembly Assembly, CompiledShapeCatalog Catalog)
    {
        internal Type Type(string name) => Assembly.GetType(name, throwOnError: true)!;
    }
}
