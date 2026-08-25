using System.Collections.ObjectModel;
using System.Linq;
using Arronix.Abstractions.Telemetry;

namespace Arronix.Common.Telemetry;

/// <summary>
/// The size an accepted event is cut down to before it is queued.
/// </summary>
/// <remarks>
/// A bounded queue bounds how many events wait, not how large they are. One event carrying a megabyte of
/// message and ten thousand tags is the same denial of service as ten thousand events, and the caller that
/// produced it is usually the code that is already going wrong.
/// </remarks>
internal static class TelemetryLimits
{
    /// <summary>The longest message, stack trace or exception message kept.</summary>
    internal const int MaxTextLength = 8_192;

    /// <summary>The longest tag key, tag value or fingerprint token kept.</summary>
    internal const int MaxTokenLength = 1_024;

    /// <summary>How many tags are kept, in the order the caller gave them.</summary>
    internal const int MaxTags = 64;

    /// <summary>How many fingerprint tokens are kept.</summary>
    internal const int MaxFingerprint = 16;

    /// <summary>Cuts one string to length when there is one, and leaves absence alone.</summary>
    internal static string? CutOrNull(string? text, int limit) => text is null ? null : Cut(text, limit);

    /// <summary>Cuts one string to length, marking that it was cut.</summary>
    internal static string Cut(string? text, int limit)
        => text is null ? string.Empty
        : text.Length <= limit ? text
        : string.Concat(text.AsSpan(0, limit), "… [truncated]");

    /// <summary>Copies a caller's tags into a host-owned, bounded dictionary.</summary>
    internal static IReadOnlyDictionary<string, string> Tags(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null or { Count: 0 })
        {
            return ReadOnlyDictionary<string, string>.Empty;
        }

        var kept = new Dictionary<string, string>(Math.Min(tags.Count, MaxTags), StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            if (kept.Count >= MaxTags)
            {
                break;
            }

            if (tag.Key is not null)
            {
                kept[Cut(tag.Key, MaxTokenLength)] = Cut(tag.Value, MaxTokenLength);
            }
        }

        return new ReadOnlyDictionary<string, string>(kept);
    }

    /// <summary>Copies a caller's fingerprint into a host-owned, bounded list.</summary>
    internal static IReadOnlyList<string> Fingerprint(IReadOnlyList<string>? fingerprint)
        => fingerprint is null or { Count: 0 }
            ? []
            : [.. fingerprint.Take(MaxFingerprint).Select(token => Cut(token, MaxTokenLength))];

    /// <summary>Cuts a rendered failure to size.</summary>
    internal static ExceptionSummary? Summary(ExceptionSummary? summary)
        => summary is null
            ? null
            : new ExceptionSummary(
                Cut(summary.TypeName, MaxTokenLength),
                Cut(summary.Message, MaxTextLength),
                summary.StackTrace is null ? null : Cut(summary.StackTrace, MaxTextLength));
}
