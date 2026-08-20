using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arronix.Abstractions.Serialization;
using Arronix.Common.Composition;
using Arronix.Common.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace Arronix.Common.Tests.Serialization;

/// <summary>
/// Covers the serializer the platform resolves: that it round-trips immutable payloads, that its
/// try-shaped method really does not throw, and that a payload carrying no value is reported rather than
/// substituted.
/// </summary>
[TestFixture]
public class SystemTextJsonSerializerTests
{
    private readonly IJsonSerializer _serializer = new SystemTextJsonSerializer();

    [Test]
    public void Constructor_RejectsAMissingOptionsInstance()
    {
        Assert.That(() => new SystemTextJsonSerializer(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Serialize_UsesTheCanonicalShape()
    {
        Assert.That(_serializer.Serialize(new Payload("north", 3)), Is.EqualTo("""{"name":"north","count":3}"""));
    }

    [Test]
    public void Deserialize_ReadsATypeWithNoParameterlessConstructor()
    {
        var value = _serializer.Deserialize<Payload>("""{"name":"north","count":3}""");

        Assert.That(value, Is.EqualTo(new Payload("north", 3)));
    }

    [Test]
    public void Deserialize_RejectsAMissingPayload()
    {
        Assert.That(() => _serializer.Deserialize<Payload>(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Deserialize_ReportsAMalformedPayload()
    {
        Assert.That(() => _serializer.Deserialize<Payload>("{not json"), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TryDeserialize_ReportsSuccessWithTheValue()
    {
        var read = _serializer.TryDeserialize<Payload>("""{"name":"north","count":3}""", out var value);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.True);
            Assert.That(value, Is.EqualTo(new Payload("north", 3)));
        });
    }

    [TestCase("{not json")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TryDeserialize_ReportsFailureWithoutThrowing(string? json)
    {
        var read = _serializer.TryDeserialize<Payload>(json!, out var value);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public async Task SerializeAsync_WritesToTheStreamAndLeavesItOpen()
    {
        using var destination = new MemoryStream();

        await _serializer.SerializeAsync(new Payload("north", 3), destination);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetString(destination.ToArray()), Is.EqualTo("""{"name":"north","count":3}"""));
            Assert.That(destination.CanWrite, Is.True, "The caller owns the stream and may keep writing to it.");
        });
    }

    [Test]
    public async Task DeserializeAsync_ReadsFromTheStreamAndLeavesItOpen()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("""{"name":"north","count":3}"""));

        var value = await _serializer.DeserializeAsync<Payload>(source);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(new Payload("north", 3)));
            Assert.That(source.CanRead, Is.True);
        });
    }

    [Test]
    public void DeserializeRequired_ReportsAPayloadThatCarriesNoValue()
    {
        // The legacy code substituted `new T()` here, turning an empty response into an object whose every
        // member sat at its default — a failure that then surfaced somewhere else entirely.
        Assert.That(() => _serializer.DeserializeRequired<Payload>("null"), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void DeserializeRequired_ReturnsTheValueWhenThereIsOne()
    {
        Assert.That(
            _serializer.DeserializeRequired<Payload>("""{"name":"north","count":3}"""),
            Is.EqualTo(new Payload("north", 3)));
    }

    [Test]
    public void DeserializeRequired_RejectsAMissingSerializer()
    {
        Assert.That(
            () => JsonSerializationExtensions.DeserializeRequired<Payload>(null!, "{}"),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void DeserializeRequiredAsync_ReportsAPayloadThatCarriesNoValue()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("null"));

        Assert.That(
            async () => await _serializer.DeserializeRequiredAsync<Payload>(source),
            Throws.InstanceOf<JsonException>());
    }

    [Test]
    public async Task DeserializeRequiredAsync_ReturnsTheValueWhenThereIsOne()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("""{"name":"north","count":3}"""));

        Assert.That(await _serializer.DeserializeRequiredAsync<Payload>(source), Is.EqualTo(new Payload("north", 3)));
    }

    [Test]
    public void TheEntryPoint_RegistersTheSerializer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Arronix:Identity:ApplicationName"] = "Northwind" })
            .Build();

        using var provider = new ServiceCollection()
            .AddArronixCommon(configuration)
            .BuildServiceProvider();

        Assert.That(provider.GetService<IJsonSerializer>(), Is.TypeOf<SystemTextJsonSerializer>());
    }

    private sealed record Payload(string Name, int Count);
}
