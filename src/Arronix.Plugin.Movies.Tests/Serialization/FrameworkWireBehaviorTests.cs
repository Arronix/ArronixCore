using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>
/// The framework behaviours the client contract generator's compile-time model depends on.
/// </summary>
/// <remarks>
/// <para>
/// The generator predicts what the framework's serializer will do so that a hash computed while the
/// assembly compiled can be checked against the running metadata. Two of those predictions cannot be
/// exercised by the movie graph: it has no type that both inherits and declares members of its own, and no
/// byte array. A model that had them wrong would agree with the runtime here and disagree the day a media
/// kind introduced either.
/// </para>
/// <para>
/// So they are pinned directly. These types exist only to ask the framework a question; nothing serializes
/// a movie through this context.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FrameworkWireBehaviorTests
{
    /// <remarks>
    /// Derived members first, then each base in turn, each level in its own declaration order. The
    /// generator's serialization model orders members this way and Host's compiled shapes order them the
    /// other way, so this is what says which of the two is the wire.
    /// </remarks>
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

/// <summary>Metadata for the hierarchy above, declared exactly as a contract assembly declares its own.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Strict,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WireChild))]
internal sealed partial class WireBehaviorContext : JsonSerializerContext;
