using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Arronix.Architecture.Tests.Repository;

/// <summary>
/// One project file, read as a declaration rather than evaluated as a build.
/// </summary>
/// <remarks>
/// The declared reference set is what a reviewer reads and what a rule is written against, so it is what
/// is asserted. Evaluating the project instead would fold in transitive packages and framework
/// references and turn a one-line rule into a judgment call.
/// </remarks>
internal sealed class ProjectFile
{
    private ProjectFile(string name, string path, XDocument document, string text)
    {
        Name = name;
        Path = path;
        Document = document;
        Text = text;
    }

    /// <summary>Gets the project name.</summary>
    public string Name { get; }

    /// <summary>Gets the absolute path of the project file.</summary>
    public string Path { get; }

    /// <summary>Gets the parsed project file.</summary>
    public XDocument Document { get; }

    /// <summary>Gets the raw text of the project file.</summary>
    public string Text { get; }

    /// <summary>Gets the SDK the project is built with.</summary>
    public string Sdk => (string?)Document.Root?.Attribute("Sdk") ?? string.Empty;

    /// <summary>Gets the packages the project declares, ordered and de-duplicated.</summary>
    public IReadOnlyList<string> PackageReferences => Includes("PackageReference");

    /// <summary>Gets the projects the project declares, by project name, ordered and de-duplicated.</summary>
    public IReadOnlyList<string> ProjectReferences =>
        ProjectReferenceNames(Document.Descendants("ProjectReference"));

    /// <summary>Gets analyzer-only project references, which do not become runtime dependencies.</summary>
    public IReadOnlyList<string> AnalyzerProjectReferences =>
        ProjectReferenceNames(Document
            .Descendants("ProjectReference")
            .Where(static element =>
                string.Equals(Metadata(element, "OutputItemType"), "Analyzer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Metadata(element, "ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Gets project references whose assemblies may enter the runtime dependency graph.</summary>
    public IReadOnlyList<string> RuntimeProjectReferences =>
        ProjectReferenceNames(Document
            .Descendants("ProjectReference")
            .Where(static element =>
                !string.Equals(Metadata(element, "ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)));

    private static string? Metadata(XElement element, string name) =>
        (string?)element.Attribute(name) ?? (string?)element.Element(name);

    private static IReadOnlyList<string> ProjectReferenceNames(IEnumerable<XElement> references) =>
        references
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => System.IO.Path.GetFileNameWithoutExtension(
                include!.Replace('\\', System.IO.Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Reads a project file.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <returns>The project file.</returns>
    /// <exception cref="FileNotFoundException">The project file is not in the working tree.</exception>
    public static ProjectFile Load(string projectName)
    {
        var path = RepositoryLayout.ProjectFilePath(projectName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Project '{projectName}' is not in the working tree. A governance rule about a project that "
                + "does not exist would silently pass, so this is a failure rather than a skip.",
                path);
        }

        var text = File.ReadAllText(path);

        return new ProjectFile(projectName, path, XDocument.Parse(text), text);
    }

    /// <summary>Gets the Arronix projects the project declares.</summary>
    /// <returns>The referenced Arronix project names.</returns>
    public IReadOnlyList<string> ArronixProjectReferences =>
        ProjectReferences
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .ToArray();

    private IReadOnlyList<string> Includes(string elementName) =>
        Document
            .Descendants(elementName)
            .Select(element => (string?)element.Attribute("Include"))
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
