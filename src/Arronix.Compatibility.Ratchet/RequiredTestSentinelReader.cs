namespace Arronix.Compatibility.Ratchet;

/// <summary>One permanent test whose successful execution protects a critical proof rail.</summary>
public sealed record RequiredTestSentinel(
    string Id,
    string Project,
    string FullName,
    string Fixture,
    string Method,
    string SourceFile,
    string SourceFileDigest)
{
    /// <summary>Gets additional compiled documents which supply the sentinel's proof inputs or helpers.</summary>
    public IReadOnlyList<RequiredTestSupportDocument> SupportDocuments { get; init; } = [];
}

/// <summary>One additional compiled source document required by a permanent proof sentinel.</summary>
public sealed record RequiredTestSupportDocument(string SourceFile, string SourceFileDigest);

/// <summary>Reads the canonical required-test registry consumed by repository verification.</summary>
public static class RequiredTestSentinelReader
{
    public const string Header =
        "# id\tproject\tfull name\tfixture\tmethod\tsource file\tSHA-256 source digest\tsupport source files";

    /// <summary>Reads and validates a non-empty canonical sentinel registry.</summary>
    public static IReadOnlyList<RequiredTestSentinel> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required proof sentinel registry '{path}' does not exist.", path);
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
        {
            throw new CompatibilityDocumentException(
                $"Required proof sentinel registry '{path}' has no canonical eight-column header.");
        }

        var result = new List<RequiredTestSentinel>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        string? previousId = null;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != 8 || fields.Any(string.IsNullOrWhiteSpace)
                || fields.Any(static value => !string.Equals(value, value.Trim(), StringComparison.Ordinal)))
            {
                throw InvalidLine(path, index, "expected eight non-empty canonical tab-separated fields");
            }

            var sentinel = new RequiredTestSentinel(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                fields[4],
                fields[5],
                fields[6])
            {
                SupportDocuments = ParseSupportDocuments(path, index, fields[7])
            };
            if (!IsStableId(sentinel.Id))
            {
                throw InvalidLine(path, index, $"'{sentinel.Id}' is not a stable identifier");
            }

            if (previousId is not null && string.CompareOrdinal(previousId, sentinel.Id) >= 0)
            {
                throw InvalidLine(path, index, "sentinel identifiers are not strictly sorted");
            }

            if (!ids.Add(sentinel.Id))
            {
                throw InvalidLine(path, index, $"sentinel identifier '{sentinel.Id}' is duplicated");
            }

            var executionIdentity = sentinel.Project + "\n" + sentinel.FullName;
            if (!identities.Add(executionIdentity))
            {
                throw InvalidLine(path, index, "the project and full-name identity is duplicated");
            }

            if (!IsRelativePath(sentinel.Project)
                || !sentinel.Project.StartsWith("src/", StringComparison.Ordinal)
                || !sentinel.Project.EndsWith(".Tests.csproj", StringComparison.Ordinal)
                || !IsRelativePath(sentinel.SourceFile)
                || !sentinel.SourceFile.StartsWith("src/", StringComparison.Ordinal)
                || !IsClrTypeName(sentinel.Fixture)
                || !IsIdentifier(sentinel.Method)
                || !string.Equals(
                    sentinel.FullName,
                    sentinel.Fixture + "." + sentinel.Method,
                    StringComparison.Ordinal)
                || !IsDigest(sentinel.SourceFileDigest)
                || sentinel.SupportDocuments.Any(document => string.Equals(
                    document.SourceFile,
                    sentinel.SourceFile,
                    StringComparison.Ordinal)))
            {
                throw InvalidLine(path, index, $"sentinel '{sentinel.Id}' has an invalid binding");
            }

            result.Add(sentinel);
            previousId = sentinel.Id;
        }

        if (result.Count == 0)
        {
            throw new CompatibilityDocumentException(
                $"Required proof sentinel registry '{path}' contains no stable rows.");
        }

        return result;
    }

    private static IReadOnlyList<RequiredTestSupportDocument> ParseSupportDocuments(
        string path,
        int zeroBasedLine,
        string value)
    {
        if (string.Equals(value, "-", StringComparison.Ordinal))
        {
            return [];
        }

        var result = new List<RequiredTestSupportDocument>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        string? previousPath = null;
        foreach (var entry in value.Split(';'))
        {
            var separator = entry.LastIndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                throw InvalidLine(path, zeroBasedLine, "support sources must use path=sha256:digest entries");
            }

            var sourceFile = entry[..separator];
            var sourceFileDigest = entry[(separator + 1)..];
            if (!IsRelativePath(sourceFile)
                || !sourceFile.StartsWith("src/", StringComparison.Ordinal)
                || sourceFile.Contains(';')
                || sourceFile.Contains('=')
                || !IsDigest(sourceFileDigest))
            {
                throw InvalidLine(path, zeroBasedLine, $"support source '{entry}' is not canonical");
            }

            if (previousPath is not null && string.CompareOrdinal(previousPath, sourceFile) >= 0)
            {
                throw InvalidLine(path, zeroBasedLine, "support source paths are not strictly sorted");
            }

            if (!paths.Add(sourceFile))
            {
                throw InvalidLine(path, zeroBasedLine, $"support source '{sourceFile}' is duplicated");
            }

            result.Add(new RequiredTestSupportDocument(sourceFile, sourceFileDigest));
            previousPath = sourceFile;
        }

        return result;
    }

    private static CompatibilityDocumentException InvalidLine(string path, int zeroBasedLine, string message)
        => new($"Required proof sentinel registry '{path}' line {zeroBasedLine + 1}: {message}.");

    private static bool IsStableId(string value)
    {
        if (value.Length < 3)
        {
            return false;
        }

        var separator = true;
        foreach (var character in value)
        {
            var currentSeparator = character is '.' or '-';
            if (currentSeparator && separator)
            {
                return false;
            }

            if (!currentSeparator && !(character is >= 'a' and <= 'z') && !char.IsAsciiDigit(character))
            {
                return false;
            }

            separator = currentSeparator;
        }

        return !separator;
    }

    private static bool IsRelativePath(string value)
        => !Path.IsPathRooted(value)
            && !value.Contains('\\')
            && value.Split('/').All(static segment => segment is not "" and not "." and not "..");

    private static bool IsClrTypeName(string value)
        => value.Split('.', '+').All(IsIdentifier);

    private static bool IsIdentifier(string value)
        => value.Length > 0
            && (char.IsAsciiLetter(value[0]) || value[0] == '_')
            && value[1..].All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool IsDigest(string value)
        => value.Length == 71
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value[7..].All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
