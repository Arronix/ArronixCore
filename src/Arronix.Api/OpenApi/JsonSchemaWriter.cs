using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

#pragma warning disable ARX0013 // Shape contracts are experimental; identity types are described as text.
#pragma warning disable ARX0014 // Extension contracts are experimental; identity types are described as text.
#pragma warning disable ARX0015 // Provider contracts are experimental; identity types are described as text.

namespace Arronix.Api.OpenApi;

/// <summary>
/// Describes the shape of a payload, by reading the payload's own type.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a machine-readable description of an API is worth having and the packages that
/// normally produce one are not available to this assembly — deliberately, since taking any of them would
/// end the guarantee that this project references no package at all. Reflecting over the wire types is the
/// honest alternative: it produces a description derived from the same types the endpoints actually
/// serialize, so it cannot drift from them the way a hand-written specification would.
/// </para>
/// <para>
/// It describes what this platform sends, not everything the specification can express. There is no
/// polymorphism to model, because none of the wire contracts uses any; enumerations are written as their
/// names, matching how they are serialized; and a type that has already been described is referenced
/// rather than repeated, which is also what stops a self-referencing type from recursing forever.
/// </para>
/// </remarks>
internal sealed class JsonSchemaWriter
{
    /// <summary>Types that serialize as plain text because a converter gives them a textual form.</summary>
    private static readonly HashSet<Type> TextualIdentifiers =
    [
        typeof(MediaKindId),
        typeof(MediaLevelId),
        typeof(PluginId),
        typeof(ProviderId),
        typeof(OrdinalPath),
    ];

    private readonly Dictionary<string, JsonObject> _components = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _inProgress = [];

    /// <summary>
    /// Gets every named schema produced so far, for the document's component section.
    /// </summary>
    internal IReadOnlyDictionary<string, JsonObject> Components => _components;

    /// <summary>
    /// Describes one type.
    /// </summary>
    /// <param name="type">The type to describe.</param>
    /// <returns>A schema, which may be a reference to a named component.</returns>
    internal JsonObject Describe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            var inner = Describe(underlying);
            inner["nullable"] = true;
            return inner;
        }

        if (TextualIdentifiers.Contains(type))
        {
            return new JsonObject { ["type"] = "string" };
        }

        if (type == typeof(MediaItemId))
        {
            return new JsonObject { ["type"] = "integer", ["format"] = "int32" };
        }

        if (type.IsEnum)
        {
            var names = new JsonArray();
            foreach (var name in Enum.GetNames(type))
            {
                names.Add(JsonNamingPolicy.CamelCase.ConvertName(name));
            }

            return new JsonObject { ["type"] = "string", ["enum"] = names };
        }

        if (TryDescribePrimitive(type, out var primitive))
        {
            return primitive;
        }

        if (TryDescribeDictionary(type, out var dictionary))
        {
            return dictionary;
        }

        if (TryDescribeSequence(type, out var sequence))
        {
            return sequence;
        }

        return DescribeObject(type);
    }

    private static bool TryDescribePrimitive(Type type, [NotNullWhen(true)] out JsonObject? schema)
    {
        schema = type switch
        {
            _ when type == typeof(string) || type == typeof(char) => new JsonObject { ["type"] = "string" },
            _ when type == typeof(bool) => new JsonObject { ["type"] = "boolean" },
            _ when type == typeof(byte) || type == typeof(short) || type == typeof(int)
                => new JsonObject { ["type"] = "integer", ["format"] = "int32" },
            _ when type == typeof(long) => new JsonObject { ["type"] = "integer", ["format"] = "int64" },
            _ when type == typeof(float) || type == typeof(double) || type == typeof(decimal)
                => new JsonObject { ["type"] = "number" },
            _ when type == typeof(Guid) => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            _ when type == typeof(Uri) => new JsonObject { ["type"] = "string", ["format"] = "uri" },
            _ when type == typeof(DateTimeOffset) || type == typeof(DateTime)
                => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            _ when type == typeof(DateOnly) => new JsonObject { ["type"] = "string", ["format"] = "date" },
            _ when type == typeof(TimeSpan) => new JsonObject { ["type"] = "string", ["format"] = "duration" },
            _ => null,
        };

        return schema is not null;
    }

    private bool TryDescribeDictionary(Type type, [NotNullWhen(true)] out JsonObject? schema)
    {
        var mapping = type.GetInterfaces().Prepend(type).FirstOrDefault(candidate =>
            candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                || candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)));

        if (mapping is null)
        {
            schema = null;
            return false;
        }

        schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = Describe(mapping.GetGenericArguments()[1]),
        };

        return true;
    }

    private bool TryDescribeSequence(Type type, [NotNullWhen(true)] out JsonObject? schema)
    {
        if (type.IsArray)
        {
            schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Describe(type.GetElementType()!),
            };

            return true;
        }

        var sequence = type.GetInterfaces().Prepend(type).FirstOrDefault(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (sequence is null || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            schema = null;
            return false;
        }

        schema = new JsonObject
        {
            ["type"] = "array",
            ["items"] = Describe(sequence.GetGenericArguments()[0]),
        };

        return true;
    }

    private JsonObject DescribeObject(Type type)
    {
        var name = SchemaName(type);
        var reference = new JsonObject { ["$ref"] = "#/components/schemas/" + name };

        if (_components.ContainsKey(name) || !_inProgress.Add(type))
        {
            return reference;
        }

        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
            {
                continue;
            }

            var propertyName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            properties[propertyName] = Describe(property.PropertyType);

            // A member the language insists on being given a value is a member the payload must carry.
            // Everything else is genuinely optional, and saying so is what lets a client tell the difference
            // between "absent" and "wrong".
            if (property.GetCustomAttributes().Any(static attribute =>
                    string.Equals(attribute.GetType().Name, "RequiredMemberAttribute", StringComparison.Ordinal)))
            {
                required.Add(propertyName);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["title"] = type.Name,
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        _inProgress.Remove(type);
        _components[name] = schema;

        return reference;
    }

    private static string SchemaName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var stem = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        var arguments = string.Concat(type.GetGenericArguments().Select(SchemaName));
        return string.Create(CultureInfo.InvariantCulture, $"{stem}Of{arguments}");
    }
}
