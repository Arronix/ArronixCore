using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Common.Serialization.Converters;

namespace Arronix.Common.Serialization;

/// <summary>
/// The one JSON configuration the platform reads and writes with.
/// </summary>
/// <remarks>
/// <para>
/// Wire shape is a compatibility surface: the host, its subsystems and every extension have to agree on
/// casing, on how an enumeration is spelled, on what happens to a null and on what a timestamp looks like.
/// One configuration, in one place, is what makes that agreement checkable. A component that builds its own
/// options is producing a dialect, and a dialect is a bug that only shows up at an integration boundary.
/// </para>
/// <para>
/// Output is compact. The legacy configuration indented every payload globally and then cloned itself in two
/// places to undo that for the paths where the bytes actually mattered, which is the wrong default twice
/// over: indentation is a reading aid, and reading is what <see cref="Indented"/> is for.
/// </para>
/// <para>
/// Polymorphism is declared, never inferred. The legacy serializer cast every value to <see cref="object"/>
/// so that the runtime type's members were written; that writes a payload with no type discriminator, so it
/// cannot be read back, and it silently changes the shape of every payload rather than the few that are
/// polymorphic. A payload hierarchy that needs polymorphic treatment says so with
/// <see cref="JsonPolymorphicAttribute"/> and <see cref="JsonDerivedTypeAttribute"/>, which round-trips.
/// </para>
/// </remarks>
public static class JsonDefaults
{
    /// <summary>
    /// Gets the canonical options: the shape every payload the platform sends or stores is written in.
    /// </summary>
    /// <remarks>
    /// The instance is frozen. It is shared by every caller, and a serializer options instance that is
    /// mutated after first use throws anyway — freezing turns that latent failure into an immediate,
    /// obvious one at the point of the mistaken write.
    /// </remarks>
    public static JsonSerializerOptions Compact { get; } = Create(writeIndented: false);

    /// <summary>
    /// Gets the canonical options with indentation added, for payloads a person is expected to read —
    /// a configuration dump, a support bundle, a diagnostic file.
    /// </summary>
    /// <remarks>
    /// Identical to <see cref="Compact"/> in every respect that affects meaning, so a value written by one
    /// and read by the other survives unchanged.
    /// </remarks>
    public static JsonSerializerOptions Indented { get; } = Create(writeIndented: true);

    /// <summary>
    /// Applies the platform's conventions to a caller-owned options instance.
    /// </summary>
    /// <param name="options">The instance to configure. It must not have been used yet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This exists for the composition root, which has to hand the same conventions to a framework that
    /// insists on owning its own options instance. It adds converters and resolvers rather than replacing
    /// them, so it is meant for a freshly constructed instance and must not be applied twice.
    /// </para>
    /// <para>
    /// Indentation is deliberately not set here: it is the only convention a caller is free to choose, and
    /// the two prepared instances already cover both answers.
    /// </para>
    /// </remarks>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Human-edited payloads reach the platform through configuration files and pasted request bodies,
        // so reading is lenient about the two things people get wrong most often: a trailing comma and the
        // casing of a property name.
        options.AllowTrailingCommas = true;
        options.PropertyNameCaseInsensitive = true;

        // Writing is not lenient. Names are camel-cased on the way out, and a null is left out entirely
        // rather than written as an explicit null, so an absent value and a null value stay the same thing
        // across a round trip.
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        // An enumeration travels as its name, not as its ordinal: an ordinal is a number whose meaning
        // changes the day a member is inserted. Integers are still accepted on the way in, because a payload
        // written by an older component predates that decision.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        options.Converters.Add(new UtcDateTimeJsonConverter());

        // The generated metadata answers for the payload shapes the platform itself owns; reflection answers
        // for everything else, and comes last so it never shadows generated metadata. A host publishing
        // trimmed removes that last entry and is then told, at the point of use, about every shape the
        // generated context does not cover — which is the whole reason the context exists.
        options.TypeInfoResolverChain.Add(ArronixJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
    }

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
        };

        Apply(options);
        options.MakeReadOnly();

        return options;
    }
}
