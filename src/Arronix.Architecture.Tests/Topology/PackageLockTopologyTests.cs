using System.IO;
using System.Linq;
using System.Text.Json;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// The checked-in lock files describe the graph that actually exists.
/// </summary>
/// <remarks>
/// <para>
/// Lock files are checked in and the rail restores with <c>--locked-mode</c>, which is what makes the
/// dependency graph a reviewable fact rather than a property of whoever built last. That validation covers
/// packages. It does not check the project entries: a lock file naming a project that was renamed or
/// deleted restores cleanly and stays wrong, quietly describing a graph the repository no longer has.
/// </para>
/// <para>
/// This rule exists because that happened. Splitting the video package left
/// <c>arronix.format.video.contracts</c> in a lock file after the project was gone, through a locked
/// restore that reported success. Nothing broke, which is the problem: the record is evidence, and evidence
/// that can rot without failing is not evidence.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PackageLockTopologyTests
{
    private const string LockFileName = "packages.lock.json";

    /// <summary>Gets every project this delivery owns, for the parameterized cases below.</summary>
    public static IEnumerable<string> AllProjects => RepositoryLayout.AllProjects;

    [Test]
    public void EveryProjectChecksInALockFile()
    {
        var missing = RepositoryLayout.AllProjects
            .Where(static project => !File.Exists(LockFilePath(project)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(RepositoryLayout.AllProjects, Is.Not.Empty);
            Assert.That(
                missing,
                Is.Empty,
                "the rail restores with --locked-mode, which needs every project's lock file present.");
        });
    }

    /// <remarks>
    /// <para>
    /// Both directions against the same evaluated set, because each direction catches a different mistake
    /// and neither implies the other. A name in the lock file that the graph no longer contains is a record
    /// of a project that was renamed or deleted - the defect that started this fixture. A project in the
    /// graph that the lock file does not name is a dependency nobody locked, which is the case
    /// <c>--locked-mode</c> exists for and would not see either.
    /// </para>
    /// <para>
    /// The evaluated set is the transitive closure of runtime project references, which is what NuGet
    /// records: a lock file lists every project reachable through references, not only the direct ones.
    /// Analyzer references are excluded, matching how they are declared - <c>OutputItemType="Analyzer"</c>
    /// with <c>ReferenceOutputAssembly="false"</c> makes a project a build input rather than a dependency,
    /// and NuGet does not record it.
    /// </para>
    /// </remarks>
    /// <param name="projectName">The project under test.</param>
    [Test]
    [TestCaseSource(nameof(AllProjects))]
    public void LockFileRecordsExactlyTheEvaluatedProjectClosure(string projectName)
    {
        var locked = ProjectDependenciesOf(projectName).ToArray();
        var evaluated = EvaluatedProjectClosureOf(projectName);

        Assert.Multiple(() =>
        {
            Assert.That(
                locked.Except(evaluated, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal),
                Is.Empty,
                $"'{projectName}' locks a project its reference graph no longer reaches. Regenerate the lock "
                + "files with a restore that re-evaluates the graph; --locked-mode will not tell you.");

            Assert.That(
                evaluated.Except(locked, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal),
                Is.Empty,
                $"'{projectName}' reaches a project its lock file does not record.");
        });
    }

    /// <remarks>
    /// Guards the case above: a closure computed from a project file nobody can read would be empty, and an
    /// empty set matches an empty set. Every project that declares a runtime project reference must produce
    /// a non-empty closure.
    /// </remarks>
    [Test]
    public void TheEvaluatedClosureIsActuallyComputed()
    {
        var withReferences = RepositoryLayout.AllProjects
            .Where(static project => ProjectFile.Load(project).RuntimeProjectReferences.Count > 0)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(withReferences, Is.Not.Empty, "no project declares a runtime project reference");

            foreach (var project in withReferences)
            {
                Assert.That(
                    EvaluatedProjectClosureOf(project),
                    Is.Not.Empty,
                    $"the closure computed for '{project}' is empty, so its rule would pass by comparing "
                    + "nothing with nothing.");
            }
        });
    }

    /// <summary>
    /// Walks a project's runtime project references transitively.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <returns>Every project reachable through runtime references, excluding the project itself.</returns>
    private static IReadOnlyCollection<string> EvaluatedProjectClosureOf(string projectName)
    {
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(ProjectFile.Load(projectName).RuntimeProjectReferences);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!reached.Add(current) || !RepositoryLayout.ProjectExists(current))
            {
                continue;
            }

            foreach (var reference in ProjectFile.Load(current).RuntimeProjectReferences)
            {
                pending.Push(reference);
            }
        }

        return reached;
    }

    private static string LockFilePath(string projectName) =>
        Path.Combine(RepositoryLayout.ProjectDirectory(projectName), LockFileName);

    private static IEnumerable<string> ProjectDependenciesOf(string projectName)
    {
        var path = LockFilePath(projectName);
        if (!File.Exists(path))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("dependencies", out var frameworks))
        {
            return [];
        }

        return frameworks
            .EnumerateObject()
            .SelectMany(framework => framework.Value.EnumerateObject())
            .Where(static dependency =>
                dependency.Value.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "Project", StringComparison.Ordinal))
            .Select(static dependency => dependency.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
