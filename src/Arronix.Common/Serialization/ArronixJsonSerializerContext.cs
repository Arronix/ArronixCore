using System.Text.Json.Serialization;
using Arronix.Abstractions.Health;

namespace Arronix.Common.Serialization;

/// <summary>
/// Compile-time serialization metadata for the payload shapes the platform itself writes.
/// </summary>
/// <remarks>
/// <para>
/// Reflection-based serialization discovers a type's members at run time, which is exactly what a trimmer
/// cannot see and therefore exactly what it removes. Generating the metadata instead lets a host be
/// published trimmed — and, as a side effect, moves the cost of describing a type from the first
/// serialization to build time.
/// </para>
/// <para>
/// This context is registered ahead of the reflection resolver in <see cref="JsonDefaults"/>, so a type
/// listed here is served from generated metadata and everything else still works. It lists only shapes this
/// assembly owns or serializes on someone's behalf; a host or a subsystem in another assembly declares its
/// own context and adds it to <see cref="System.Text.Json.JsonSerializerOptions.TypeInfoResolverChain"/>,
/// because the attributes below cannot be contributed to from outside this compilation.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(HealthCheck))]
[JsonSerializable(typeof(IReadOnlyList<HealthCheck>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
public partial class ArronixJsonSerializerContext : JsonSerializerContext
{
}
