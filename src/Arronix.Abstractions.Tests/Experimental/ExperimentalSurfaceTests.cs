using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Health;

namespace Arronix.Abstractions.Tests.Experimental;

/// <summary>
/// Guards the stability policy: everything added after 0.1.0 must be marked experimental, and the 0.1.0
/// surface must stay stable.
/// </summary>
/// <remarks>
/// The assertions are reflection-driven on purpose. Naming an experimental type in source would make
/// this fixture itself a consumer of the unstable surface and force a suppression into a test project.
/// </remarks>
[TestFixture]
public class ExperimentalSurfaceTests
{
    /// <summary>
    /// The public surface that shipped in 0.1.0. These carry the full semantic-versioning guarantee and
    /// must never gain an experimental marker.
    /// </summary>
    private static readonly string[] StableTypeNames =
    [
        "Arronix.Abstractions.DTOs.CutoffPolicy",
        "Arronix.Abstractions.DTOs.ImportDecision",
        "Arronix.Abstractions.DTOs.ImportReason",
        "Arronix.Abstractions.DTOs.MatchConfidence",
        "Arronix.Abstractions.DTOs.Language",
        "Arronix.Abstractions.DTOs.LibraryPathSpec",
        "Arronix.Abstractions.DTOs.MatchDecision",
        "Arronix.Abstractions.DTOs.NamingToken",
        "Arronix.Abstractions.DTOs.ParsedRelease",
        "Arronix.Abstractions.DTOs.QualityTier",
        "Arronix.Abstractions.DTOs.ReleaseCandidate",
        "Arronix.Abstractions.Health.CoreErrorCode",
        "Arronix.Abstractions.Health.HealthCheck",
        "Arronix.Abstractions.Health.HealthSeverity",
        "Arronix.Abstractions.Health.HealthStatus",
        "Arronix.Abstractions.Identity.MediaItemId",
        "Arronix.Abstractions.Identity.MediaKindId",
        "Arronix.Abstractions.Identity.ReleaseId",
        "Arronix.Abstractions.Import.IImportPipeline",
        "Arronix.Abstractions.Media.IMediaIdResolver",
        "Arronix.Abstractions.Media.IMediaKind",
        "Arronix.Abstractions.Naming.ILibraryLayout",
        "Arronix.Abstractions.Naming.IRenamePolicy",
        "Arronix.Abstractions.Parsing.IReleaseParser",
        "Arronix.Abstractions.Quality.IQualityModel",
        "Arronix.Abstractions.Scheduling.IBackgroundTaskRegistry",
        "Arronix.Abstractions.Scheduling.IScheduledJob",
        "Arronix.Abstractions.Scheduling.JobExecutionContext",
        "Arronix.Abstractions.Scheduling.JobExecutionResult"
    ];

    private static Assembly ContractAssembly => typeof(HealthCheck).Assembly;

    private static IEnumerable<Type> ExportedTypes =>
        ContractAssembly.GetExportedTypes().Where(type => !type.IsNested);

    [Test]
    public void EverySurfaceTypeIsEitherKnownStableOrMarkedExperimental()
    {
        var unmarked = ExportedTypes
            .Where(type => type.GetCustomAttribute<ExperimentalAttribute>() is null)
            .Select(type => type.FullName!)
            .Where(name => !StableTypeNames.Contains(name, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            unmarked,
            Is.Empty,
            "New contract types must carry [Experimental] until they are promoted by an explicit version-history entry.");
    }

    [Test]
    public void NoTypeFromTheInitialReleaseIsMarkedExperimental()
    {
        var regressions = ExportedTypes
            .Where(type => type.GetCustomAttribute<ExperimentalAttribute>() is not null)
            .Select(type => type.FullName!)
            .Where(name => StableTypeNames.Contains(name, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.That(regressions, Is.Empty, "Marking a stable type experimental is a breaking change.");
    }

    [Test]
    public void EveryExperimentalDiagnosticIdFollowsTheContractAreaConvention()
    {
        var ids = ExportedTypes
            .Select(type => type.GetCustomAttribute<ExperimentalAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.DiagnosticId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.That(ids, Is.Not.Empty);
        Assert.That(
            ids.All(id => Regex.IsMatch(id, "^ARX[0-9]{4}$", RegexOptions.CultureInvariant)),
            Is.True,
            $"Unexpected diagnostic ids: {string.Join(", ", ids)}");
    }

    [Test]
    public void EveryExperimentalTypeLinksToTheStabilityPolicy()
    {
        var missingLink = ExportedTypes
            .Select(type => (type, attribute: type.GetCustomAttribute<ExperimentalAttribute>()))
            .Where(pair => pair.attribute is not null)
            .Where(pair => pair.attribute!.UrlFormat is null
                || !pair.attribute.UrlFormat.Contains("stability.md", StringComparison.Ordinal))
            .Select(pair => pair.type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.That(missingLink, Is.Empty);
    }

    [Test]
    public void EveryExperimentalTypeUsesTheDiagnosticIdOfItsOwnNamespace()
    {
        var expectedByNamespace = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Arronix.Abstractions.Caching"] = "ARX0001",
            ["Arronix.Abstractions.Diagnostics"] = "ARX0002",
            ["Arronix.Abstractions.Errors"] = "ARX0003",
            ["Arronix.Abstractions.Events"] = "ARX0004",
            ["Arronix.Abstractions.FileSystem"] = "ARX0005",
            ["Arronix.Abstractions.Health"] = "ARX0006",
            ["Arronix.Abstractions.Hosting"] = "ARX0007",
            ["Arronix.Abstractions.Http"] = "ARX0008",
            ["Arronix.Abstractions.Naming"] = "ARX0009",
            ["Arronix.Abstractions.Serialization"] = "ARX0010",
            ["Arronix.Abstractions.Telemetry"] = "ARX0011",
            ["Arronix.Abstractions.Throttling"] = "ARX0012",
            ["Arronix.Abstractions.Shape"] = "ARX0013",
            ["Arronix.Abstractions.Plugins"] = "ARX0014",
            ["Arronix.Abstractions.Providers"] = "ARX0015",
            ["Arronix.Abstractions.Intent"] = "ARX0016",
            ["Arronix.Abstractions.Wire"] = "ARX0017",
            ["Arronix.Abstractions.Definition"] = "ARX0019",
            ["Arronix.Abstractions.Media"] = "ARX0020",
            ["Arronix.Abstractions.Quality"] = "ARX0021",

            // A family's own types are the same contract area as the framework that reads them: a consumer
            // that opts in to the axes model opts in to the families it ships with, and splitting the
            // identifier would make one opt-in into two for no boundary anybody can point at.
            ["Arronix.Abstractions.Quality.Families"] = "ARX0021"
        };

        var mismatched = ExportedTypes
            .Select(type => (type, attribute: type.GetCustomAttribute<ExperimentalAttribute>()))
            .Where(pair => pair.attribute is not null)
            .Where(pair => !expectedByNamespace.TryGetValue(pair.type.Namespace ?? string.Empty, out var expected)
                || !string.Equals(expected, pair.attribute!.DiagnosticId, StringComparison.Ordinal))
            .Select(pair => $"{pair.type.FullName} => {pair.attribute!.DiagnosticId}")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            mismatched,
            Is.Empty,
            "A consumer opts in per area, so an area's types must all share one diagnostic id.");
    }
}
