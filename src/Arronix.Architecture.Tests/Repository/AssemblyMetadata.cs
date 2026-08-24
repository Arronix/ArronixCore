using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Arronix.Architecture.Tests.Repository;

/// <summary>
/// Reads a built assembly's metadata without loading it.
/// </summary>
/// <remarks>
/// <para>
/// Most rules in this delivery read the working tree, and a few need the compiled form because the
/// declaration and the binary can disagree. Those few used <see cref="Assembly.Load(AssemblyName)"/>, which
/// works only for assemblies this test project already references - and that is exactly the wrong
/// requirement for a rule which says an assembly must <i>not</i> be referenced. Taking the reference in
/// order to assert that nobody takes it would make the fixture the first offender.
/// </para>
/// <para>
/// So these read the file. Metadata is enough for every question asked of it - what a package declares,
/// what it references, what it ships - and reading rather than loading also means a rule keeps working when
/// the assembly under test could not be resolved in this process at all.
/// </para>
/// </remarks>
internal static class AssemblyMetadata
{
    /// <summary>One public type, as its metadata spells it.</summary>
    /// <param name="Namespace">The declared namespace, empty for the global namespace.</param>
    /// <param name="Name">The type name.</param>
    /// <param name="Assembly">The assembly that declares it.</param>
    public readonly record struct PublicType(string Namespace, string Name, string Assembly)
    {
        /// <summary>Gets the namespace-qualified name.</summary>
        public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;
    }

    /// <summary>
    /// Lists the public top-level types a project's built assembly declares.
    /// </summary>
    /// <param name="projectName">The project, which is also its assembly name.</param>
    /// <returns>The public types, or an empty list when the project has not been built.</returns>
    public static IReadOnlyList<PublicType> PublicTypes(string projectName)
    {
        var path = AssemblyPath(projectName);
        if (path is null)
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        return metadata
            .TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Where(static definition =>
                (definition.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
            .Select(definition => new PublicType(
                metadata.GetString(definition.Namespace),
                metadata.GetString(definition.Name),
                projectName))
            .Where(static type => !type.Name.StartsWith('<'))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Lists the assembly simple names a project's built assembly references.
    /// </summary>
    /// <param name="projectName">The project, which is also its assembly name.</param>
    /// <returns>The referenced simple names, or an empty list when the project has not been built.</returns>
    public static IReadOnlyList<string> ReferencedAssemblyNames(string projectName)
    {
        var path = AssemblyPath(projectName);
        if (path is null)
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        return metadata
            .AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? AssemblyPath(string projectName)
    {
        var candidate = RepositoryLayout.BuildOutputFile(projectName, projectName + ".dll");

        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }
}
