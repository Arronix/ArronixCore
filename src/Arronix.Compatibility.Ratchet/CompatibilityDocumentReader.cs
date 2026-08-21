using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Reads the five canonical compatibility documents without accepting schema drift.</summary>
public static class CompatibilityDocumentReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    /// <summary>Reads a canonical compatibility ledger directory.</summary>
    public static CompatibilityLedger ReadLedger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"The compatibility ledger directory '{directory}' does not exist.");
        }

        var baseline = ParseBaseline(ReadUtf8(Path.Combine(fullDirectory, "baseline.json")));
        var sources = ParseSourcesJsonLines(ReadUtf8(Path.Combine(fullDirectory, "sources.jsonl")));
        var requirements = ParseRequirementsJsonLines(ReadUtf8(Path.Combine(fullDirectory, "requirements.jsonl")));
        var cases = ParseCasesJsonLines(ReadUtf8(Path.Combine(fullDirectory, "cases.jsonl")));
        var replacements = ParseReplacementsJsonLines(ReadUtf8(Path.Combine(fullDirectory, "replacements.jsonl")));
        return new CompatibilityLedger(baseline, sources, requirements, cases, replacements);
    }

    public static CompatibilityBaselineDocument ParseBaseline(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Deserialize<CompatibilityBaselineDocument>(json, "compatibility baseline");
    }

    public static IReadOnlyList<CompatibilitySource> ParseSourcesJsonLines(string jsonLines)
        => ParseJsonLines<CompatibilitySource>(jsonLines, "sources.jsonl", static value => value.SourceId, false);

    public static IReadOnlyList<CompatibilityRequirement> ParseRequirementsJsonLines(string jsonLines)
        => ParseJsonLines<CompatibilityRequirement>(
            jsonLines,
            "requirements.jsonl",
            static value => value.RequirementId,
            false);

    public static IReadOnlyList<CompatibilityCase> ParseCasesJsonLines(string jsonLines)
        => ParseJsonLines<CompatibilityCase>(jsonLines, "cases.jsonl", static value => value.CaseId, false);

    public static IReadOnlyList<CompatibilityReplacement> ParseReplacementsJsonLines(string jsonLines)
        => ParseJsonLines<CompatibilityReplacement>(
            jsonLines,
            "replacements.jsonl",
            static value => value.ReplacementId,
            true);

    internal static T Deserialize<T>(string json, string documentName)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new CompatibilityDocumentException($"The {documentName} contains JSON null.");
        }
        catch (JsonException exception)
        {
            throw new CompatibilityDocumentException(
                $"The {documentName} does not match schema version 1: {exception.Message}",
                exception);
        }
    }

    internal static JsonSerializerOptions StrictJsonOptions => SerializerOptions;

    private static IReadOnlyList<T> ParseJsonLines<T>(
        string jsonLines,
        string documentName,
        Func<T, string> id,
        bool allowEmpty)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(jsonLines);
        var normalized = jsonLines.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Contains('\r', StringComparison.Ordinal))
        {
            throw new CompatibilityDocumentException($"The {documentName} contains a bare carriage return.");
        }

        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        if (lines.Any(static line => string.IsNullOrWhiteSpace(line)))
        {
            throw new CompatibilityDocumentException($"The {documentName} contains a blank line.");
        }

        if (!allowEmpty && lines.Length == 0)
        {
            throw new CompatibilityDocumentException($"The {documentName} contains no records.");
        }

        var records = new List<T>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            records.Add(Deserialize<T>(lines[index], $"{documentName} line {index + 1}"));
        }

        string? previous = null;
        foreach (var record in records)
        {
            var current = id(record);
            if (string.IsNullOrWhiteSpace(current))
            {
                throw new CompatibilityDocumentException($"The {documentName} contains an empty primary identifier.");
            }

            if (previous is not null && string.CompareOrdinal(previous, current) >= 0)
            {
                throw new CompatibilityDocumentException(
                    $"The {documentName} primary identifiers are not strictly sorted: '{previous}', '{current}'.");
            }

            previous = current;
        }

        return records;
    }

    private static string ReadUtf8(string path)
    {
        try
        {
            return StrictUtf8.GetString(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException exception)
        {
            throw new CompatibilityDocumentException($"'{path}' is not valid UTF-8.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>Reports a malformed compatibility or execution document.</summary>
public sealed class CompatibilityDocumentException : Exception
{
    public CompatibilityDocumentException(string message)
        : base(message)
    {
    }

    public CompatibilityDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
