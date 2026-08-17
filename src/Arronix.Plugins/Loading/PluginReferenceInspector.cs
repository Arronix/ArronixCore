using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Arronix.Plugins.Loading;

/// <summary>
/// What an assembly's reference table says it depends on.
/// </summary>
/// <param name="AssemblyPath">The assembly that was inspected.</param>
/// <param name="References">Every simple assembly name in its reference table, in table order.</param>
/// <param name="Violations">The subset of those names an extension may not reference.</param>
public sealed record AssemblyReferenceReport(
    string AssemblyPath,
    IReadOnlyList<string> References,
    IReadOnlyList<string> Violations)
{
    /// <summary>
    /// Gets a value indicating whether the assembly is admissible on reference grounds alone.
    /// </summary>
    public bool IsAdmissible => Violations.Count == 0;
}

/// <summary>
/// Reads an assembly's reference table without loading it.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanical form of the rule that an extension references the contract assembly and nothing
/// else. It runs at discovery, before a load context exists, and it reads metadata rather than executing
/// anything: no type initializer runs, no module initializer runs, and a hostile assembly gets no
/// opportunity to act before it is refused.
/// </para>
/// <para>
/// The check is deliberately redundant with the load context's deny list. Static inspection catches the
/// compiler-emitted reference and gives a good diagnostic; the load context catches a reference conjured at
/// runtime, which never appears in a reference table at all. Neither subsumes the other.
/// </para>
/// <para>
/// The metadata readers used here are part of the shared framework, so this costs no package.
/// </para>
/// </remarks>
public static class PluginReferenceInspector
{
    /// <summary>
    /// Inspects an assembly's reference table.
    /// </summary>
    /// <param name="assemblyPath">The assembly to inspect.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblyPath"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">The assembly is not there.</exception>
    /// <exception cref="BadImageFormatException">The file is not a managed assembly.</exception>
    public static AssemblyReferenceReport Inspect(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("The assembly to inspect was not found.", assemblyPath);
        }

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException("The file carries no managed metadata.", assemblyPath);
        }

        var metadata = peReader.GetMetadataReader();
        var references = new List<string>(metadata.AssemblyReferences.Count);
        var violations = new List<string>();

        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);
            var name = metadata.GetString(reference.Name);

            references.Add(name);

            // List<string>.Contains compares with the default string comparer, which is ordinal.
            if (PluginLoadContext.IsBlocked(name) && !violations.Contains(name))
            {
                violations.Add(name);
            }
        }

        return new AssemblyReferenceReport(assemblyPath, references, violations);
    }

    /// <summary>
    /// Inspects an assembly's reference table, treating an unreadable file as a failure rather than an
    /// exception.
    /// </summary>
    /// <param name="assemblyPath">The assembly to inspect.</param>
    /// <param name="report">The report on success; otherwise <see langword="null"/>.</param>
    /// <param name="error">Why the file could not be read, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the assembly could be read.</returns>
    public static bool TryInspect(string assemblyPath, out AssemblyReferenceReport? report, out string? error)
    {
        report = null;
        error = null;

        try
        {
            report = Inspect(assemblyPath);
            return true;
        }
        catch (FileNotFoundException failure)
        {
            error = failure.Message;
        }
        catch (BadImageFormatException failure)
        {
            error = $"'{assemblyPath}' is not a managed assembly: {failure.Message}";
        }
        catch (IOException failure)
        {
            error = $"'{assemblyPath}' could not be read: {failure.Message}";
        }
        catch (UnauthorizedAccessException failure)
        {
            error = $"'{assemblyPath}' could not be read: {failure.Message}";
        }

        return false;
    }
}
