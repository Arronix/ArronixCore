using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Client;

namespace Arronix.Abstractions.Tests.Client;

/// <summary>
/// Live serializer behavior the digest either carries or refuses.
/// </summary>
/// <remarks>
/// Each case changes something a reader or a typed writer does and previously left the digest identical.
/// Baselined against real generated metadata first, so nothing honest is refused; see
/// <c>docs/research/g07/client-contract-declaration.md</c>.
/// </remarks>
[TestFixture]
public sealed class ClientContractDigestRefusalTests
{
    /// <summary>A context standing in for a generated one, so a case can change one thing about it.</summary>
    private sealed class Probe : JsonSerializerContext
    {
        private readonly JsonTypeInfo _root;
        private readonly JsonTypeInfo _text;
        private readonly JsonTypeInfo _bag;

        internal Probe(
            Action<JsonTypeInfo>? arrange = null,
            Type? memberType = null,
            JsonSerializerOptions? options = null,
            bool ownResolver = true,
            bool foreignOptions = false)
            : base(options ?? Honest())
        {
            var built = foreignOptions ? Honest() : Options;
            _text = JsonTypeInfo.CreateJsonTypeInfo(typeof(string), built);
            _bag = JsonTypeInfo.CreateJsonTypeInfo(typeof(Dictionary<string, object>), built);
            _root = JsonTypeInfo.CreateJsonTypeInfo<Sample>(built);
            _root.CreateObject = static () => new Sample();

            var property = _root.CreateJsonPropertyInfo(memberType ?? typeof(string), "note");
            property.Get = static value => ((Sample)value).Note;
            property.Set = static (value, note) => ((Sample)value).Note = note as string;
            _root.Properties.Add(property);

            foreach (var info in new[] { _root, _text, _bag })
            {
                info.OriginatingResolver = ownResolver ? this : NullResolver.Instance;
            }

            arrange?.Invoke(_root);

            foreach (var info in new[] { _root, _text, _bag })
            {
                info.MakeReadOnly();
            }
        }

        internal JsonTypeInfo Root => _root;

        protected override JsonSerializerOptions? GeneratedSerializerOptions => Options;

        public override JsonTypeInfo? GetTypeInfo(Type type)
        {
            if (type == typeof(Sample)) return _root;
            if (type == typeof(string)) return _text;
            return type == typeof(Dictionary<string, object>) ? _bag : null;
        }

        /// <summary>The options a generated context on strict defaults actually carries.</summary>
        internal static JsonSerializerOptions Honest() =>
            new(JsonSerializerDefaults.Strict) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    private sealed class NullResolver : IJsonTypeInfoResolver
    {
        internal static NullResolver Instance { get; } = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }

    private sealed class Sample
    {
        public string? Note { get; set; }
    }

    private static string Digest(Probe probe) => ClientContractDigest.OfSerialization(probe, probe.Root);

    private static void Refuses(Probe probe, string because) =>
        Assert.That(
            () => Digest(probe),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains(because));

    [Test]
    public void AnHonestGraphIsRendered()
    {
        Assert.That(() => Digest(new Probe()), Throws.Nothing);
    }

    // ---------------------------------------------------------------- options

    /// <remarks>Reference handling puts <c>$id</c> and <c>$ref</c> in a payload; nothing renders that.</remarks>
    [Test]
    public void PreservingReferencesIsRefusedAndWouldOtherwiseHashTheSame()
    {
        var options = Probe.Honest();
        options.ReferenceHandler = ReferenceHandler.Preserve;

        Refuses(new Probe(options: options), "preserve references");
    }

    /// <remarks>
    /// The obsolete flag is independent of <c>DefaultIgnoreCondition</c> — measured, setting it leaves that
    /// at <c>Never</c> — so the rendering does not cover it by proxy and it is refused on its own.
    /// </remarks>
    [Test]
    public void DroppingNullValuesIsRefused()
    {
        var options = Probe.Honest();
#pragma warning disable SYSLIB0020
        options.IgnoreNullValues = true;
        Assert.That(options.DefaultIgnoreCondition, Is.EqualTo(JsonIgnoreCondition.Never), "the premise");
#pragma warning restore SYSLIB0020

        Refuses(new Probe(options: options), "drop null values");
    }

    [Test]
    public void InferringPolymorphismIsRefused()
    {
        var options = Probe.Honest();
        options.InferClosedTypePolymorphism = true;

        Refuses(new Probe(options: options), "infer polymorphism");
    }

    [Test]
    public void OptionsCarryingTheirOwnConvertersAreRefused()
    {
        var options = Probe.Honest();
        options.Converters.Add(new JsonStringEnumConverter());

        Refuses(new Probe(options: options), "converters of their own");
    }

    [Test]
    public void ADictionaryKeyPolicyIsRefused()
    {
        var options = Probe.Honest();
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;

        Refuses(new Probe(options: options), "dictionary key policy");
    }

    [Test]
    public void ANamingPolicyOtherThanCamelCaseIsRefused()
    {
        var options = Probe.Honest();
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;

        Refuses(new Probe(options: options), "other than camel case");
    }

    [TestCase("MaxDepth")]
    [TestCase("PreferredObjectCreationHandling")]
    [TestCase("UnknownTypeHandling")]
    [TestCase("AllowOutOfOrderMetadataProperties")]
    [TestCase("IgnoreReadOnlyProperties")]
    [TestCase("IgnoreReadOnlyFields")]
    public void AReaderSettingChangesTheDigest(string setting)
    {
        var options = Probe.Honest();

        switch (setting)
        {
            case "MaxDepth": options.MaxDepth = 3; break;
            case "PreferredObjectCreationHandling":
                options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate; break;
            case "UnknownTypeHandling":
                options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode; break;
            case "AllowOutOfOrderMetadataProperties":
                options.AllowOutOfOrderMetadataProperties = true; break;
            case "IgnoreReadOnlyProperties": options.IgnoreReadOnlyProperties = true; break;
            default: options.IgnoreReadOnlyFields = true; break;
        }

        Assert.That(Digest(new Probe(options: options)), Is.Not.EqualTo(Digest(new Probe())));
    }

    /// <remarks>
    /// The stated boundary. Indentation, line endings, buffer size and the encoder change the bytes a
    /// payload is written as; every conforming reader recovers the same values, so they are left out of the
    /// rendering on purpose rather than by oversight.
    /// </remarks>
    [Test]
    public void FormattingSettingsDoNotChangeTheDigest()
    {
        var formatted = Probe.Honest();
        formatted.WriteIndented = true;
        formatted.IndentSize = 4;
        formatted.NewLine = "\n";
        formatted.DefaultBufferSize = 4096;
        formatted.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        Assert.That(Digest(new Probe(options: formatted)), Is.EqualTo(Digest(new Probe())));
    }

    // ------------------------------------------------------------- type level

    /// <remarks>
    /// The check that separates a contract's own generated graph from an arbitrary hand-built one.
    /// </remarks>
    [Test]
    public void MetadataResolvedBySomethingElseIsRefused()
    {
        Refuses(new Probe(ownResolver: false), "other than this contract's own context");
    }

    /// <remarks>
    /// A callback runs against the value on the way in or out and can change it, so no rendering of a
    /// type's members describes what a graph carrying one does.
    /// </remarks>
    [TestCase("OnSerializing", "before it is written")]
    [TestCase("OnSerialized", "after it is written")]
    [TestCase("OnDeserializing", "before it is read")]
    [TestCase("OnDeserialized", "after it is read")]
    public void ACallbackIsRefused(string callback, string because)
    {
        Refuses(
            new Probe(info =>
            {
                switch (callback)
                {
                    case "OnSerializing": info.OnSerializing = static _ => { }; break;
                    case "OnSerialized": info.OnSerialized = static _ => { }; break;
                    case "OnDeserializing": info.OnDeserializing = static _ => { }; break;
                    default: info.OnDeserialized = static _ => { }; break;
                }
            }),
            because);
    }

    [Test]
    public void MetadataBuiltForOtherOptionsIsRefused()
    {
        var probe = new Probe(foreignOptions: true);

        Assert.That(() => Digest(probe), Throws.InstanceOf<NotSupportedException>()
            .With.Message.Contains("built for other options"));
    }

    [Test]
    public void ATypeStatingItsOwnNumberHandlingIsRefused() =>
        Refuses(new Probe(static info => info.NumberHandling = JsonNumberHandling.AllowReadingFromString), "number handling");

    [Test]
    public void ATypeStatingItsOwnUnmappedMemberHandlingIsRefused() =>
        Refuses(new Probe(static info => info.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip), "unmapped-member handling");

    [Test]
    public void ATypeStatingItsOwnObjectCreationHandlingIsRefused() =>
        Refuses(
            new Probe(static info => info.PreferredPropertyObjectCreationHandling = JsonObjectCreationHandling.Populate),
            "object creation handling");

    [Test]
    public void APolymorphicTypeIsRefused() =>
        Refuses(new Probe(static info => info.PolymorphismOptions = new JsonPolymorphismOptions()), "polymorphic");

    /// <remarks>
    /// A converter replaces a type's whole reading and writing. Generated metadata always has one, so what
    /// is refused is a converter declared outside the framework.
    /// </remarks>
    [Test]
    public void ATypeWithAConverterOfItsOwnIsRefused()
    {
        var probe = new Converted();

        Assert.That(
            () => ClientContractDigest.OfSerialization(probe, probe.Root),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("converter of its own"));
    }

    private sealed class Converted : JsonSerializerContext
    {
        private readonly JsonTypeInfo _root;

        internal Converted()
            : base(Probe.Honest())
        {
            // Built through the reflecting resolver because that is what honours a [JsonConverter] on a
            // type; created by hand the attribute is not read, and the case would prove nothing.
            _root = new DefaultJsonTypeInfoResolver().GetTypeInfo(typeof(Custom), Options)!;
            _root.OriginatingResolver = this;
            _root.MakeReadOnly();
        }

        internal JsonTypeInfo Root => _root;

        protected override JsonSerializerOptions? GeneratedSerializerOptions => Options;

        public override JsonTypeInfo? GetTypeInfo(Type type) => type == typeof(Custom) ? _root : null;
    }

    [JsonConverter(typeof(CustomConverter))]
    private sealed class Custom
    {
        public string? Note { get; set; }
    }

    private sealed class CustomConverter : JsonConverter<Custom>
    {
        public override Custom Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new();

        public override void Write(Utf8JsonWriter writer, Custom value, JsonSerializerOptions options)
            => writer.WriteNullValue();
    }

    // ----------------------------------------------------------- member level

    [Test]
    public void AMemberWithAConverterOfItsOwnIsRefused() =>
        Refuses(new Probe(static info => info.Properties[0].CustomConverter = new PassThrough()), "converter of its own");

    private sealed class PassThrough : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    [Test]
    public void AMemberWithItsOwnOrderIsRefused() =>
        Refuses(new Probe(static info => info.Properties[0].Order = 3), "its own order");

    [Test]
    public void AMemberWithItsOwnNumberHandlingIsRefused() =>
        Refuses(new Probe(static info => info.Properties[0].NumberHandling = JsonNumberHandling.WriteAsString), "number handling");

    [Test]
    public void AMemberWithItsOwnObjectCreationHandlingIsRefused() =>
        Refuses(
            new Probe(static info => info.Properties[0].ObjectCreationHandling = JsonObjectCreationHandling.Populate),
            "object creation handling");

    [Test]
    public void AMemberThatIsExtensionDataIsRefused() =>
        Refuses(
            new Probe(static info => info.Properties[0].IsExtensionData = true, typeof(Dictionary<string, object>)),
            "extension data");

    /// <remarks>
    /// Generated metadata gives an ignored member one, so it is refused only on a member that is actually
    /// read or written, where it decides at run time whether the member appears at all.
    /// </remarks>
    [Test]
    public void AMemberDecidingForItselfWhetherToBeWrittenIsRefused() =>
        Refuses(
            new Probe(static info => info.Properties[0].ShouldSerialize = static (_, _) => false),
            "decides for itself whether to be written");

    /// <remarks>
    /// A default decides what a member becomes when a payload omits it, which is part of what a payload
    /// means. Before it was rendered, two contracts differing only in a default hashed alike.
    /// </remarks>
    [Test]
    public void AConstructorParameterIsPartOfTheRendering()
    {
        var probe = new Probe();
        var rendering = ClientContractDigest.RenderSerialization(probe, probe.Root);

        Assert.That(rendering, Does.Contain("|parameter=~"), "a member no parameter fills says so");
    }

    [Test]
    public void AnIgnoredMemberMayCarryOne()
    {
        var probe = new Probe(static info =>
        {
            var property = info.Properties[0];
            property.Get = null;
            property.Set = null;
            property.ShouldSerialize = static (_, _) => false;
        });

        Assert.That(() => Digest(probe), Throws.Nothing);
    }
}
