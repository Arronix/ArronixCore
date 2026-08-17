using System.Text.Json;

namespace Arronix.Api.Serialization;

/// <summary>
/// Renders an enumeration member as the same text the serializer would have written for it.
/// </summary>
/// <remarks>
/// Several wire contracts type a field as text where the host's own value is an enumeration —
/// <c>HealthSnapshotView.Status</c>, <c>PluginStatusView.State</c>, <c>EventEnvelope.State</c>. Those fields
/// are text on purpose, because the set of states is the host's business and freezing it into the contract
/// would make adding one a breaking change. But calling <c>ToString</c> on the way out would write
/// <c>"Healthy"</c> beside a genuine enumeration serialized as <c>"healthy"</c> in the very same payload, and
/// a client comparing the two would be right to be confused. One conversion, used everywhere such a field is
/// filled in, keeps the whole payload in one convention.
/// </remarks>
internal static class WireText
{
    /// <summary>
    /// Renders an enumeration member.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type.</typeparam>
    /// <param name="value">The member.</param>
    /// <returns>The member's name in the serializer's convention.</returns>
    internal static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
