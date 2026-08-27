using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
    /// Reading uses the metadata, which both accepted modes generate; the mode only decides whether a
    /// generated write fast path exists beside it, and that path is emitted from the same member model. So
    /// the two accepted modes cannot read or write a payload differently, which is why the digest does not
    /// carry the mode. What pins the code behind either path is the assembly's content hash, not the digest.
    /// </remarks>
    [Test]
    public void TheAcceptedGenerationModesReadAndWriteAlike()
    {
        var withFastPath = WireBehaviorContext.Default.WireLenient!;
        var metadataOnly = MetadataOnlyContext.Default.WireLenient!;
        var value = new WireLenient("note") { GrandOne = 1, GrandTwo = 2 };

        var written = JsonSerializer.Serialize(value, withFastPath);
        var writtenWithout = JsonSerializer.Serialize(value, metadataOnly);
        var read = JsonSerializer.Deserialize(written, withFastPath)!;
        var readWithout = JsonSerializer.Deserialize(written, metadataOnly)!;

        Assert.Multiple(() =>
        {
            Assert.That(withFastPath.SerializeHandler, Is.Not.Null, "the premise: one mode has the fast path");
            Assert.That(metadataOnly.SerializeHandler, Is.Null, "and the other does not");

            Assert.That(writtenWithout, Is.EqualTo(written));
            Assert.That(readWithout.Note, Is.EqualTo(read.Note));
            Assert.That(readWithout.GrandOne, Is.EqualTo(read.GrandOne));
            Assert.That(readWithout.GrandTwo, Is.EqualTo(read.GrandTwo));
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
internal sealed partial class WireBehaviorContext : JsonSerializerContext;

/// <summary>The same graph without the generated write fast path.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WireLenient), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class MetadataOnlyContext : JsonSerializerContext;
