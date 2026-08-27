using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Client;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>
/// The framework behaviours the client contract generator's compile-time model depends on.
/// </summary>
/// <remarks>
/// Predictions the movie graph cannot exercise — inheritance with own members, a byte array, a parameter
/// whose nullability differs from its member's — pinned directly against the framework. These types exist
/// only to ask it a question.
/// </remarks>
[TestFixture]
public sealed class FrameworkWireBehaviorTests
{
    /// <remarks>Derived first, then each base. Host's compiled shapes use the other order.</remarks>
    [Test]
    public void MembersAreOrderedMostDerivedFirst()
    {
        var members = WireBehaviorContext.Default.WireChild!.Properties
            .Select(property => property.Name)
            .ToArray();

        Assert.That(members, Is.EqualTo(new[] { "childOne", "blob", "parentOne", "grandOne", "grandTwo" }));
    }

    /// <remarks>
    /// A byte array is a base64 string, not a sequence of numbers, so it has no element type and is not an
    /// enumerable. Every other array is.
    /// </remarks>
    [Test]
    public void AByteArrayIsAValueRatherThanASequence()
    {
        var blob = WireBehaviorContext.Default.WireChild!.Properties
            .Single(property => property.Name == "blob");
        var metadata = WireBehaviorContext.Default.GetTypeInfo(blob.PropertyType)!;

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Kind, Is.EqualTo(JsonTypeInfoKind.None));
            Assert.That(metadata.ElementType, Is.Null);
        });
    }

    /// <remarks>
    /// A parameter's nullability is its own: the member here is not nullable and the parameter that fills
    /// it is, so a model reading the member's answer would describe the wrong one.
    /// </remarks>
    [Test]
    public void AParametersNullabilityIsItsOwn()
    {
        var member = WireBehaviorContext.Default.WireLenient!.Properties.Single(p => p.Name == "note");

        Assert.Multiple(() =>
        {
            Assert.That(member.IsGetNullable, Is.False);
            Assert.That(member.AssociatedParameter, Is.Not.Null);
            Assert.That(member.AssociatedParameter!.IsNullable, Is.True);
        });
    }

    /// <remarks>
    /// The mode is what removes the fast path, and it removes it from every reachable type rather than only
    /// the one named. That is what lets the contract require metadata alone instead of describing a
    /// delegate no rendering can describe.
    /// </remarks>
    [Test]
    public void MetadataOnlyRemovesTheWriteFastPathFromEveryReachableType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Handlers(WireBehaviorContext.Default, typeof(WireLenient)),
                Has.Some.EqualTo("WireLenient"),
                "the premise: the default mode generates one");

            Assert.That(Handlers(MetadataOnlyContext.Default, typeof(WireLenient)), Is.Empty);
        });
    }

    /// <summary>Names every reachable type the context generated a write fast path for.</summary>
    internal static IReadOnlyList<string> Handlers(JsonSerializerContext context, Type root)
    {
        var found = new List<string>();
        var seen = new HashSet<Type> { root };
        var pending = new Queue<JsonTypeInfo>();
        pending.Enqueue(context.GetTypeInfo(root)!);

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            var generic = typeof(JsonTypeInfo<>).MakeGenericType(type.Type);

            if (generic.IsInstanceOfType(type)
                && generic.GetProperty("SerializeHandler")!.GetValue(type) is not null)
            {
                found.Add(type.Type.Name);
            }

            foreach (var property in type.Properties)
            {
                if ((property.Get is not null || property.Set is not null) && seen.Add(property.PropertyType))
                {
                    pending.Enqueue(context.GetTypeInfo(property.PropertyType)!);
                }
            }

            if (type.ElementType is { } element && seen.Add(element))
            {
                pending.Enqueue(context.GetTypeInfo(element)!);
            }
        }

        return found;
    }

    /// <remarks>
    /// The hole no metadata inspection can see. A string enum keeps the same converter type name, leaves
    /// the options' converter list empty, and changes what a payload carries. This enumeration has no zero
    /// member on purpose: a names mode writes an undefined zero as a number, so probing zero alone would
    /// read the same either way, and only a declared constant separates them.
    /// </remarks>
    [Test]
    public void AStringEnumIsInvisibleInMetadataAndVisibleInWhatItWrites()
    {
        var numeric = WireBehaviorContext.Default;
        var stringy = StringEnumContext.Default;

        Assert.Multiple(() =>
        {
            Assert.That(
                stringy.GetTypeInfo(typeof(WireStage))!.Converter.GetType().Name,
                Is.EqualTo(numeric.GetTypeInfo(typeof(WireStage))!.Converter.GetType().Name),
                "the converter type is the same");
            Assert.That(stringy.Options.Converters, Is.Empty, "and nothing is registered on the options");

            Assert.That(
                JsonSerializer.Serialize(default(WireStage), stringy.GetTypeInfo(typeof(WireStage))!),
                Is.EqualTo(JsonSerializer.Serialize(default(WireStage), numeric.GetTypeInfo(typeof(WireStage))!)),
                "an undefined zero reads the same in both modes");

            Assert.That(
                JsonSerializer.Serialize(WireStage.Done, stringy.GetTypeInfo(typeof(WireStage))!),
                Is.Not.EqualTo(JsonSerializer.Serialize(WireStage.Done, numeric.GetTypeInfo(typeof(WireStage))!)),
                "and a declared constant does not");

            Assert.That(
                ClientContractDigest.RenderSerialization(stringy, stringy.GetTypeInfo(typeof(WireHolder))!),
                Is.Not.EqualTo(ClientContractDigest.RenderSerialization(numeric, numeric.GetTypeInfo(typeof(WireHolder))!)),
                "and so does the rendering");
        });
    }

    /// <remarks>
    /// The premise of resolving every type through the context rather than through its options: a context
    /// answers for the graph a compiler generated, and for nothing else.
    /// </remarks>
    [Test]
    public void AContextAnswersOnlyForTheGraphItWasGeneratedFor()
    {
        Assert.That(WireBehaviorContext.Default.GetTypeInfo(typeof(Guid)), Is.Null);
    }
}

/// <summary>A base with two members.</summary>
public class WireGrandparent
{
    /// <summary>Gets the first.</summary>
    public int GrandOne { get; init; }

    /// <summary>Gets the second.</summary>
    public int GrandTwo { get; init; }
}

/// <summary>A middle level with one member of its own.</summary>
public class WireParent : WireGrandparent
{
    /// <summary>Gets its own member.</summary>
    public int ParentOne { get; init; }
}

/// <summary>A leaf with two members of its own, one of them a byte array.</summary>
public class WireChild : WireParent
{
    /// <summary>Gets its own member.</summary>
    public int ChildOne { get; init; }

    /// <summary>Gets a byte array, which the framework writes as a base64 string.</summary>
    public byte[]? Blob { get; init; }
}

/// <summary>An enumeration with no zero member, so a zero probe alone would read the same either way.</summary>
public enum WireStage
{
    /// <summary>The first value, deliberately not zero.</summary>
    Done = 1,

    /// <summary>The second.</summary>
    Pending = 2,
}

/// <summary>A holder for the enumeration above.</summary>
public class WireHolder
{
    /// <summary>Gets the stage.</summary>
    public WireStage Stage { get; init; }
}

/// <summary>A member that is not nullable, filled by a parameter that is.</summary>
public class WireLenient : WireGrandparent
{
    /// <summary>Creates one.</summary>
    /// <param name="note">The note, which may be absent.</param>
    public WireLenient(string? note) => Note = note ?? string.Empty;

    /// <summary>Gets the note, which never is.</summary>
    public string Note { get; }
}

/// <summary>Metadata for the hierarchy above, declared exactly as a contract assembly declares its own.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WireChild))]
[JsonSerializable(typeof(WireLenient))]
[JsonSerializable(typeof(WireHolder))]
internal sealed partial class WireBehaviorContext : JsonSerializerContext;

/// <summary>The same holder with enumerations written as names.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WireHolder))]
internal sealed partial class StringEnumContext : JsonSerializerContext;

/// <summary>The same graph without the generated write fast path.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WireLenient), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class MetadataOnlyContext : JsonSerializerContext;
