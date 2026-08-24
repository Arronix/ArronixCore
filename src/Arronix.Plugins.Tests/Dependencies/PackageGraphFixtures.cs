using System.Linq;
using System.Text;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// One resolution, as the fixtures read it.
/// </summary>
/// <remarks>
/// The engine hands its three answers back as pass-local values so no second durable resolved-graph model
/// exists in production. This record is the test's own convenience for holding them together; nothing in
/// the platform consumes it.
/// </remarks>
/// <param name="ActivationOrder">The eligible packages, dependencies before dependants.</param>
/// <param name="IneligiblePackages">The identifiers that may not be activated.</param>
/// <param name="Diagnostics">Every reason an identifier is ineligible.</param>
internal sealed record PackageResolution(
    IReadOnlyList<InstalledPackage> ActivationOrder,
    IReadOnlyList<PluginId> IneligiblePackages,
    IReadOnlyList<PackageDependencyDiagnostic> Diagnostics);

/// <summary>
/// The scenario vocabulary the dependency-graph fixtures are written in.
/// </summary>
/// <remarks>
/// A package is written as it would be read aloud - <c>Package("app", "1.0.0", "lib:&gt;=1.0 &lt;2.0")</c> -
/// so a fixture asserting something about a diamond looks like a diamond. Ranges go through
/// <see cref="VersionRangeParser"/> exactly as a manifest's would; the fixtures have no private notion of
/// what a range means.
/// </remarks>
internal static class PackageGraphFixtures
{
    /// <summary>Runs the production engine over installed packages.</summary>
    /// <param name="installed">The installed packages.</param>
    /// <returns>The resolution.</returns>
    public static PackageResolution Resolve(IEnumerable<InstalledPackage> installed)
    {
        PackageDependencyEngine.Resolve(installed, out var order, out var ineligible, out var diagnostics);
        return new PackageResolution(order, ineligible, diagnostics);
    }

    /// <summary>
    /// Builds one installed candidate.
    /// </summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="requirements">Its requirements, each written <c>id:range</c>.</param>
    /// <returns>The candidate.</returns>
    public static InstalledPackage Package(string id, string version, params string[] requirements)
        => PackageFrom(id, version, $"/packages/{id}", requirements);

    /// <summary>
    /// Builds one installed candidate that knows where it came from.
    /// </summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="origin">Where the copy was found.</param>
    /// <param name="requirements">Its requirements, each written <c>id:range</c>.</param>
    /// <returns>The candidate.</returns>
    public static InstalledPackage PackageFrom(string id, string version, string origin, params string[] requirements)
        => new(
            PluginId.FromString(id),
            SemanticVersion.Parse(version),
            $"{origin}/plugin.json",
            origin,
            requirements: [.. requirements.Select(Requirement)]);

    /// <summary>
    /// Builds one installed candidate an operator has switched off.
    /// </summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="requirements">Its requirements, each written <c>id:range</c>.</param>
    /// <returns>The candidate.</returns>
    /// <remarks>
    /// The one state a package can be in other than available, so the fixture names it rather than taking a
    /// reason. A fixture that could invent a state the platform cannot produce would be testing a
    /// generality nothing supplies.
    /// </remarks>
    public static InstalledPackage DisabledPackage(string id, string version, params string[] requirements)
        => new(
            PluginId.FromString(id),
            SemanticVersion.Parse(version),
            $"/packages/{id}/plugin.json",
            $"/packages/{id}",
            requirements: [.. requirements.Select(Requirement)],
            availability: PackageAvailability.DisabledByConfiguration);

    /// <summary>
    /// Attempts to build one installed package whose folder the caller states exactly, including blank.
    /// </summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="folder">Where the copy was found, which the caller may state as blank.</param>
    /// <returns>The package.</returns>
    /// <remarks>
    /// A copy's folder is the tie-break in a duplicate diagnostic and part of the text that renders it, so
    /// two spellings of "no folder" that sort equal and print differently would let discovery order decide
    /// an operator-facing message. The canonical model refuses a blank folder outright, which makes that
    /// state unrepresentable rather than merely normalized.
    /// </remarks>
    public static InstalledPackage PackageWithFolder(string id, string version, string? folder)
        => new(
            PluginId.FromString(id),
            SemanticVersion.Parse(version),
            $"{folder}/plugin.json",
            folder!);

    /// <summary>
    /// Reads one requirement written <c>id:range</c>.
    /// </summary>
    /// <param name="text">The requirement text.</param>
    /// <returns>The requirement.</returns>
    public static PackageRequirement Requirement(string text)
    {
        var separator = text.IndexOf(':');
        return new PackageRequirement(
            PluginId.FromString(text[..separator]),
            VersionRangeParser.Parse(text[(separator + 1)..]));
    }

    /// <summary>
    /// Gets the identifiers in the activation order.
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The identifiers.</returns>
    public static string[] Activated(this PackageResolution resolution)
        => [.. resolution.ActivationOrder.Select(static package => package.Id.Value)];

    /// <summary>
    /// Gets the ineligible identifiers.
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The identifiers.</returns>
    public static string[] Ineligible(this PackageResolution resolution)
        => [.. resolution.IneligiblePackages.Select(static id => id.Value)];

    /// <summary>
    /// Gets the diagnostics of one kind.
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <param name="kind">The kind wanted.</param>
    /// <returns>The diagnostics.</returns>
    public static PackageDependencyDiagnostic[] Of(
        this PackageResolution resolution,
        PackageDependencyDiagnosticKind kind)
        => [.. resolution.Diagnostics.Where(diagnostic => diagnostic.Kind == kind)];

    /// <summary>
    /// Renders a cycle path the way the diagnostic's own message renders it.
    /// </summary>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>The path text.</returns>
    public static string Path(this PackageDependencyDiagnostic diagnostic)
        => string.Join(" -> ", diagnostic.CyclePath.Select(static id => id.Value));

    /// <summary>
    /// Renders a resolution completely, so that two resolutions can be compared as one value.
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The rendering.</returns>
    /// <remarks>
    /// Everything observable is in here: the order with its versions, the ineligible identifiers, and every
    /// diagnostic including its kind, subject, dependency, cycle path and message. A permutation fixture
    /// comparing renderings is therefore asserting on the whole result rather than on the part somebody
    /// remembered to check.
    /// </remarks>
    public static string Render(this PackageResolution resolution)
    {
        var text = new StringBuilder();

        foreach (var package in resolution.ActivationOrder)
        {
            text.Append("activate ").Append(package.Id).Append(' ').Append(package.Version).Append('\n');
        }

        foreach (var id in resolution.IneligiblePackages)
        {
            text.Append("refuse ").Append(id).Append('\n');
        }

        foreach (var diagnostic in resolution.Diagnostics)
        {
            text.Append("diagnostic ")
                .Append(diagnostic.Kind)
                .Append(" | ")
                .Append(diagnostic.Package)
                .Append(" | ")
                .Append(diagnostic.Dependency?.Value ?? "-")
                .Append(" | ")
                .Append(diagnostic.Path())
                .Append(" | ")
                .Append(diagnostic.Message)
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Enumerates every permutation of a list.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The values.</param>
    /// <returns>Every ordering of them.</returns>
    /// <remarks>
    /// Exhaustive rather than sampled. "Invariant under every permutation" is the claim, and a fixture that
    /// checked a hundred random orderings would be evidence for a weaker one.
    /// </remarks>
    public static IEnumerable<T[]> Permutations<T>(IReadOnlyList<T> values)
    {
        var indexes = Enumerable.Range(0, values.Count).ToArray();
        var order = new int[values.Count];

        foreach (var permutation in Permute(indexes, 0))
        {
            Array.Copy(permutation, order, order.Length);
            yield return [.. order.Select(index => values[index])];
        }
    }

    private static IEnumerable<int[]> Permute(int[] values, int start)
    {
        if (start == values.Length - 1)
        {
            yield return values;
            yield break;
        }

        for (var index = start; index < values.Length; index++)
        {
            (values[start], values[index]) = (values[index], values[start]);

            foreach (var permutation in Permute(values, start + 1))
            {
                yield return permutation;
            }

            (values[start], values[index]) = (values[index], values[start]);
        }
    }
}
