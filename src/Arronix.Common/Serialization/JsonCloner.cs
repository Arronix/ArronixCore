using System.Text.Json;

namespace Arronix.Common.Serialization;

/// <summary>
/// Deep-copies a value by serializing and reading it back.
/// </summary>
/// <remarks>
/// <para>
/// A round trip through JSON is a legitimate way to detach a graph from whatever else holds a reference to
/// it, and it is the only one that needs no per-type support. It is also expensive out of all proportion to
/// how it reads at the call site, which is why it is a named static call rather than an extension method:
/// the legacy form hung off every expression in the codebase, one dot away, and looked free.
/// </para>
/// <para>
/// The copy is only as faithful as the payload's own serialization. Members the canonical options omit —
/// anything ignored, anything with no accessible setter, a value whose runtime type is more derived than
/// its declared type and is not declared polymorphic — are absent from the copy. When that matters, write
/// a copy constructor.
/// </para>
/// </remarks>
public static class JsonCloner
{
    /// <summary>
    /// Returns a deep copy of a value.
    /// </summary>
    /// <typeparam name="TValue">The value's type. Nothing is required of it — in particular it need not have
    /// a parameterless constructor, which the legacy form demanded and which excludes immutable records.</typeparam>
    /// <param name="value">The value to copy. May be <see langword="null"/>.</param>
    /// <returns>A copy sharing no references with the original, or <see langword="null"/> if the original was null.</returns>
    /// <exception cref="JsonException">The value cannot be written or read back.</exception>
    public static TValue? Clone<TValue>(TValue? value)
    {
        if (value is null)
        {
            return default;
        }

        // Straight to UTF-8 and back: the intermediate string the legacy form built is pure overhead, and
        // this path is already the expensive way to copy something.
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Compact);

        return JsonSerializer.Deserialize<TValue>(payload, JsonDefaults.Compact);
    }
}
