using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Reads the leaf cases in NUnit v3 XML result documents.</summary>
public static class NUnitResultReader
{
    /// <summary>Reads all NUnit XML files below one or more files or directories.</summary>
    public static NUnitTestRun ReadPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var files = paths
            .SelectMany(ExpandPath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new CompatibilityDocumentException("No NUnit XML result documents were found.");
        }

        return new NUnitTestRun(files.Select(ReadFile).ToArray());
    }

    /// <summary>Reads one NUnit XML result file.</summary>
    public static NUnitProjectResult ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var reader = XmlReader.Create(path, SafeXmlSettings());
            return Parse(XDocument.Load(reader, LoadOptions.SetLineInfo), path);
        }
        catch (XmlException exception)
        {
            throw new CompatibilityDocumentException(
                $"'{path}' is not valid NUnit XML: {exception.Message}",
                exception);
        }
    }

    /// <summary>Reads one NUnit XML result document from text.</summary>
    public static NUnitProjectResult Parse(string xml, string sourceName = "in-memory NUnit XML")
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, SafeXmlSettings());

        try
        {
            return Parse(XDocument.Load(reader, LoadOptions.SetLineInfo), sourceName);
        }
        catch (XmlException exception)
        {
            throw new CompatibilityDocumentException(
                $"'{sourceName}' is not valid NUnit XML: {exception.Message}",
                exception);
        }
    }

    private static NUnitProjectResult Parse(XDocument document, string sourceName)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "test-run")
        {
            throw new CompatibilityDocumentException($"'{sourceName}' has no NUnit <test-run> root.");
        }

        var assembly = root
            .Elements("test-suite")
            .FirstOrDefault(static element => (string?)element.Attribute("type") == "Assembly")
            ?? throw new CompatibilityDocumentException(
                $"'{sourceName}' has no top-level NUnit assembly test suite.");

        var project = RequiredAttribute(assembly, "name", sourceName);
        var assemblyPath = RequiredAttribute(assembly, "fullname", sourceName);
        var tests = root
            .Descendants("test-case")
            .Select(test => ParseTestCase(project, test, sourceName))
            .ToArray();

        var declared = new NUnitCounts(
            ReadCount(root, "total", sourceName),
            ReadCount(root, "passed", sourceName),
            ReadCount(root, "failed", sourceName),
            ReadCount(root, "skipped", sourceName),
            ReadCount(root, "inconclusive", sourceName));
        var observed = NUnitCounts.From(tests);

        if (declared != observed)
        {
            throw new CompatibilityDocumentException(
                $"'{sourceName}' declares {declared} but its leaf <test-case> elements contain {observed}.");
        }

        return new NUnitProjectResult(project, sourceName, tests)
        {
            AssemblyPath = assemblyPath
        };
    }

    private static NUnitTestCaseResult ParseTestCase(
        string project,
        XElement element,
        string sourceName)
    {
        var name = RequiredAttribute(element, "name", sourceName);
        var fullName = RequiredAttribute(element, "fullname", sourceName);
        var result = RequiredAttribute(element, "result", sourceName);
        var outcome = result switch
        {
            "Passed" => NUnitTestOutcome.Passed,
            "Failed" => NUnitTestOutcome.Failed,
            "Skipped" => NUnitTestOutcome.Skipped,
            "Inconclusive" => NUnitTestOutcome.Inconclusive,
            _ => throw new CompatibilityDocumentException(
                $"'{sourceName}' gives test '{fullName}' the unknown NUnit result '{result}'.")
        };

        return new NUnitTestCaseResult(project, name, fullName, outcome);
    }

    private static IEnumerable<string> ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A result path cannot be empty.", nameof(path));
        }

        if (File.Exists(path))
        {
            return [Path.GetFullPath(path)];
        }

        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.xml", SearchOption.AllDirectories)
                .Select(Path.GetFullPath);
        }

        throw new FileNotFoundException($"The result path '{path}' does not exist.", path);
    }

    private static int ReadCount(XElement root, string name, string sourceName)
    {
        var text = RequiredAttribute(root, name, sourceName);
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new CompatibilityDocumentException(
                $"'{sourceName}' has invalid NUnit count {name}='{text}'.");
        }

        return value;
    }

    private static string RequiredAttribute(XElement element, string name, string sourceName)
    {
        var value = (string?)element.Attribute(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CompatibilityDocumentException(
                $"'{sourceName}' has a <{element.Name.LocalName}> without a non-empty '{name}' attribute.");
    }

    private static XmlReaderSettings SafeXmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };
}

/// <summary>The outcome NUnit assigned to one executed test case.</summary>
public enum NUnitTestOutcome
{
    /// <summary>The test executed successfully.</summary>
    Passed,

    /// <summary>The test executed and failed.</summary>
    Failed,

    /// <summary>The test did not execute.</summary>
    Skipped,

    /// <summary>The test executed without reaching a conclusive outcome.</summary>
    Inconclusive
}

/// <summary>One leaf NUnit test result.</summary>
public sealed record NUnitTestCaseResult(
    string Project,
    string Name,
    string FullName,
    NUnitTestOutcome Outcome);

/// <summary>The results emitted for one test assembly.</summary>
public sealed record NUnitProjectResult(
    string Project,
    string Source,
    IReadOnlyList<NUnitTestCaseResult> Tests)
{
    /// <summary>Gets the assembly path reported by NUnit, when the result came from an execution artifact.</summary>
    public string? AssemblyPath { get; init; }

    /// <summary>Gets the aggregate leaf-case counts.</summary>
    public NUnitCounts Counts => NUnitCounts.From(Tests);
}

/// <summary>The complete execution evidence aggregated across test assemblies.</summary>
public sealed record NUnitTestRun(IReadOnlyList<NUnitProjectResult> Projects)
{
    /// <summary>Gets all leaf test cases in the run.</summary>
    public IReadOnlyList<NUnitTestCaseResult> Tests => Projects.SelectMany(static project => project.Tests).ToArray();

    /// <summary>Gets the aggregate leaf-case counts.</summary>
    public NUnitCounts Counts => NUnitCounts.From(Tests);
}

/// <summary>Explicit test-result counts reported by the ratchet.</summary>
public readonly record struct NUnitCounts(int Total, int Passed, int Failed, int Skipped, int Inconclusive)
{
    /// <summary>Gets the number of tests which were enabled for execution.</summary>
    public int Enabled => Total - Skipped;

    /// <summary>Counts a set of leaf test cases.</summary>
    public static NUnitCounts From(IEnumerable<NUnitTestCaseResult> tests)
    {
        ArgumentNullException.ThrowIfNull(tests);
        var values = tests.ToArray();
        return new NUnitCounts(
            values.Length,
            values.Count(static test => test.Outcome == NUnitTestOutcome.Passed),
            values.Count(static test => test.Outcome == NUnitTestOutcome.Failed),
            values.Count(static test => test.Outcome == NUnitTestOutcome.Skipped),
            values.Count(static test => test.Outcome == NUnitTestOutcome.Inconclusive));
    }

    /// <inheritdoc />
    public override string ToString()
        => $"total={Total}, passed={Passed}, failed={Failed}, skipped={Skipped}, inconclusive={Inconclusive}";
}
