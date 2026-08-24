using System.IO;

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
/// This is the metadata half of the implementation-boundary rule: an extension may bring its own libraries
/// and supported format or language contracts, but it may not reference Arronix implementation assemblies
/// or the legacy applications. It runs at discovery, before a load context exists, and reads metadata rather
/// than executing anything: no type initializer or module initializer runs before the entry assembly is
/// refused.
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

        if (!StagedAssembly.TryStage(assemblyPath, out var staged, out var error))
        {
            throw new BadImageFormatException(error, assemblyPath);
        }

        return Report(staged!);
    }

    /// <summary>
    /// Projects an already-staged assembly's reference table into a report.
    /// </summary>
    /// <param name="staged">The staged assembly.</param>
    /// <returns>The report.</returns>
    /// <remarks>
    /// The loader takes this overload, because it has already read the candidate's bytes once and must not
    /// read the path a second time: the file it decided about and the file it loads have to be the same
    /// file.
    /// </remarks>
    internal static AssemblyReferenceReport Report(StagedAssembly staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        var references = new List<string>(staged.References.Count);
        var violations = new List<string>();

        foreach (var reference in staged.References)
        {
            var name = reference.Name ?? string.Empty;
            references.Add(name);

            // List<string>.Contains compares with the default string comparer, which is ordinal.
            if (PluginLoadContext.IsBlocked(name) && !violations.Contains(name))
            {
                violations.Add(name);
            }
        }

        return new AssemblyReferenceReport(staged.Path, references.AsReadOnly(), violations.AsReadOnly());
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
            error = failure.Message;
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
