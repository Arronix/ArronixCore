using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Abstractions.Health;
using Arronix.Common.Serialization;

namespace Arronix.Common.Tests.Serialization;

/// <summary>
/// Covers the canonical JSON configuration: the conventions every payload the platform writes obeys, and
/// the three legacy defaults that were corrected — global indentation, the parameterless-constructor
/// requirement, and polymorphism by cast.
/// </summary>
[TestFixture]
public class JsonDefaultsTests
{
    [Test]
    public void Compact_WritesCamelCasedPropertyNames()
    {
        var json = JsonSerializer.Serialize(new Sample("north", 3), JsonDefaults.Compact);

        Assert.That(json, Is.EqualTo("""{"name":"north","count":3}"""));
    }

    [Test]
    public void Compact_WritesNoIndentation()
    {
        var json = JsonSerializer.Serialize(new Sample("north", 3), JsonDefaults.Compact);

        Assert.That(json, Does.Not.Contain("\n"));
    }

    [Test]
    public void Indented_WritesForAReader()
    {
        var json = JsonSerializer.Serialize(new Sample("north", 3), JsonDefaults.Indented);

        Assert.That(json, Does.Contain("\n"));
    }

    [Test]
    public void Indented_AgreesWithCompactOnEveryConventionThatChangesMeaning()
    {
        var indented = JsonSerializer.Deserialize<Sample>(
            JsonSerializer.Serialize(new Sample("north", 3), JsonDefaults.Indented),
            JsonDefaults.Compact);

        Assert.That(indented, Is.EqualTo(new Sample("north", 3)));
    }

    [Test]
    public void Compact_LeavesANullOut()
    {
        var json = JsonSerializer.Serialize(new Optional("north", null), JsonDefaults.Compact);

        Assert.That(json, Is.EqualTo("""{"name":"north"}"""));
    }

    [Test]
    public void Compact_ReadsAPropertyNameWhateverItsCasing()
    {
        var value = JsonSerializer.Deserialize<Sample>("""{"Name":"north","COUNT":3}""", JsonDefaults.Compact);

        Assert.That(value, Is.EqualTo(new Sample("north", 3)));
    }

    [Test]
    public void Compact_ForgivesATrailingComma()
    {
        var value = JsonSerializer.Deserialize<Sample>("""{"name":"north","count":3,}""", JsonDefaults.Compact);

        Assert.That(value, Is.EqualTo(new Sample("north", 3)));
    }

    [Test]
    public void Compact_WritesAnEnumerationAsACamelCasedName()
    {
        var json = JsonSerializer.Serialize(new Flagged(Level.HighPriority), JsonDefaults.Compact);

        Assert.That(json, Is.EqualTo("""{"level":"highPriority"}"""));
    }

    [Test]
    public void Compact_StillReadsAnEnumerationWrittenAsANumber()
    {
        var value = JsonSerializer.Deserialize<Flagged>("""{"level":1}""", JsonDefaults.Compact);

        Assert.That(value?.Level, Is.EqualTo(Level.HighPriority));
    }

    [Test]
    public void Compact_CamelCasesDictionaryKeys()
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["FirstKey"] = "value" },
            JsonDefaults.Compact);

        Assert.That(json, Is.EqualTo("""{"firstKey":"value"}"""));
    }

    [Test]
    public void Compact_IsFrozenSoNoCallerCanChangeTheSharedShape()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsonDefaults.Compact.IsReadOnly, Is.True);
            Assert.That(JsonDefaults.Indented.IsReadOnly, Is.True);
        });
    }

    [Test]
    public void Compact_RoundTripsATypeWithNoParameterlessConstructor()
    {
        // The legacy serializer required `where T : new()`, which excludes every immutable record — that
        // is, everything the contract layer is made of. This test would not compile against it.
        var json = JsonSerializer.Serialize(new Sample("north", 3), JsonDefaults.Compact);

        Assert.That(JsonSerializer.Deserialize<Sample>(json, JsonDefaults.Compact), Is.EqualTo(new Sample("north", 3)));
    }

    [Test]
    public void Compact_RoundTripsAHierarchyThatDeclaresItselfPolymorphic()
    {
        var json = JsonSerializer.Serialize<Node>(new Leaf("north", 3), JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.StartWith("""{"kind":"leaf","""));
            Assert.That(JsonSerializer.Deserialize<Node>(json, JsonDefaults.Compact), Is.EqualTo(new Leaf("north", 3)));
        });
    }

    [Test]
    public void Compact_WritesOnlyTheDeclaredTypeOfAnUndeclaredHierarchy()
    {
        // Deliberate: the legacy serializer cast every value to object so that the runtime type's members
        // were written. That writes members no discriminator identifies, so the payload cannot be read back
        // — and it changed the shape of every payload to serve the few that are polymorphic.
        var json = JsonSerializer.Serialize<Plain>(new PlainWithMore("north", 3), JsonDefaults.Compact);

        Assert.That(json, Is.EqualTo("""{"name":"north"}"""));
    }

    [Test]
    public void Apply_RejectsAMissingOptionsInstance()
    {
        Assert.That(() => JsonDefaults.Apply(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Apply_GivesACallerOwnedInstanceTheSameShape()
    {
        var options = new JsonSerializerOptions();
        JsonDefaults.Apply(options);

        Assert.That(
            JsonSerializer.Serialize(new Optional("north", null), options),
            Is.EqualTo(JsonSerializer.Serialize(new Optional("north", null), JsonDefaults.Compact)));
    }

    [Test]
    public void GeneratedContext_DescribesThePlatformsOwnPayloadShapes()
    {
        var typeInfo = ArronixJsonSerializerContext.Default.GetTypeInfo(typeof(HealthCheck));

        Assert.That(typeInfo, Is.Not.Null, "The generated context is what lets a host be published trimmed.");
    }

    [Test]
    public void GeneratedContext_ServesAShapeThroughTheSharedOptionsWithoutChangingIt()
    {
        var json = JsonSerializer.Serialize(
            new HealthCheck("probe", "Probe", HealthStatus.Healthy, HealthSeverity.Info),
            JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("""checkId":"probe"""));
            Assert.That(json, Does.Contain("""status":"healthy"""));
            Assert.That(json, Does.Not.Contain("message"), "A null is left out, whoever resolved the shape.");
        });
    }

    private sealed record Sample(string Name, int Count);

    private sealed record Optional(string Name, string? Note);

    private sealed record Flagged(Level Level);

    private enum Level
    {
        Normal = 0,
        HighPriority = 1,
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Leaf), "leaf")]
    private abstract record Node(string Name);

    private sealed record Leaf(string Name, int Depth) : Node(Name);

    private record Plain(string Name);

    private sealed record PlainWithMore(string Name, int Extra) : Plain(Name);
}
