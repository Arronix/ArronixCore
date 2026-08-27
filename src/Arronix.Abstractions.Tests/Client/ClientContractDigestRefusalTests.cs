using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Client;

namespace Arronix.Abstractions.Tests.Client;

/// <summary>
/// Live metadata the digest refuses rather than hashing.
/// </summary>
/// <remarks>
/// A hash over a graph that is only partly described says two contracts agree when what differs is the
/// part nobody looked at. Each case gives the running metadata a feature the rendering does not carry, and
/// requires a refusal rather than a number.
/// </remarks>
[TestFixture]
public sealed class ClientContractDigestRefusalTests
{
    private sealed class Probe : JsonSerializerContext
    {
        private readonly JsonTypeInfo _info;

        private readonly JsonTypeInfo _text;
        private readonly JsonTypeInfo _bag;

        internal Probe(Action<JsonTypeInfo> arrange, Type memberType)
            : base(new JsonSerializerOptions(JsonSerializerDefaults.Strict))
        {
            _text = JsonTypeInfo.CreateJsonTypeInfo(typeof(string), Options);
            _bag = JsonTypeInfo.CreateJsonTypeInfo(typeof(Dictionary<string, object>), Options);
            _info = JsonTypeInfo.CreateJsonTypeInfo<Sample>(Options);
            _info.CreateObject = static () => new Sample();

            var property = _info.CreateJsonPropertyInfo(memberType, "note");
            property.Get = static value => ((Sample)value).Note;
            property.Set = static (value, note) => ((Sample)value).Note = note as string;
            _info.Properties.Add(property);

            arrange(_info);
            _info.MakeReadOnly();
        }

        protected override JsonSerializerOptions? GeneratedSerializerOptions => Options;

        public override JsonTypeInfo? GetTypeInfo(Type type)
        {
            if (type == typeof(Sample)) return _info;
            if (type == typeof(string)) return _text;
            return type == typeof(Dictionary<string, object>) ? _bag : null;
        }

        internal JsonTypeInfo Root => _info;
    }

    private sealed class Sample
    {
        public string? Note { get; set; }
    }

    private static Probe Arranged(Action<JsonTypeInfo> arrange, Type? memberType = null) =>
        new(arrange, memberType ?? typeof(string));

    [Test]
    public void AGraphWithNothingUnusualIsRendered()
    {
        var probe = Arranged(static _ => { });

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.Nothing);
    }

    [Test]
    public void ATypeStatingItsOwnNumberHandlingIsRefused()
    {
        var probe = Arranged(static info => info.NumberHandling = JsonNumberHandling.AllowReadingFromString);

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("number handling"));
    }

    [Test]
    public void ATypeStatingItsOwnUnmappedMemberHandlingIsRefused()
    {
        var probe = Arranged(static info => info.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip);

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("unmapped-member handling"));
    }

    [Test]
    public void APolymorphicTypeIsRefused()
    {
        var probe = Arranged(static info => info.PolymorphismOptions = new JsonPolymorphismOptions());

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("polymorphic"));
    }

    [Test]
    public void AMemberWithItsOwnOrderIsRefused()
    {
        var probe = Arranged(static info => info.Properties[0].Order = 3);

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("its own order"));
    }

    [Test]
    public void AMemberWithItsOwnNumberHandlingIsRefused()
    {
        var probe = Arranged(static info =>
            info.Properties[0].NumberHandling = JsonNumberHandling.WriteAsString);

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("number handling"));
    }

    [Test]
    public void AMemberThatIsExtensionDataIsRefused()
    {
        var probe = Arranged(
            static info => info.Properties[0].IsExtensionData = true,
            typeof(Dictionary<string, object>));

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("extension data"));
    }
}
