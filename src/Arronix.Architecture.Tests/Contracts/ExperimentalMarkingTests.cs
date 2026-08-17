using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Contracts;

/// <summary>
/// Rule 9 - everything added to the contract assembly since 0.1.0 is marked experimental.
/// </summary>
/// <remarks>
/// <para>
/// The marker is not decoration. Consuming an experimental contract is a compile error until the consumer
/// writes a file-scoped opt-in with a reason, which means every use of an unsettled contract is visible
/// in review and grep-able afterwards. An unmarked addition silently promotes itself to a
/// semantic-versioning guarantee nobody agreed to give.
/// </para>
/// <para>
/// "Newly added" has to be defined against something, and the something is the 0.1.0 surface written out
/// below. Promotion is therefore a deliberate act: a type is promoted by removing its attribute and
/// adding it to this list in the same change, which forces the decision through review rather than
/// letting it happen by omission.
/// </para>
/// </remarks>
[TestFixture]
public partial class ExperimentalMarkingTests
{
    /// <summary>
    /// The public surface that shipped in 0.1.0. Stable, and therefore correctly unmarked.
    /// </summary>
    private static readonly string[] StableSurface =
    [
        "Arronix.Abstractions.DTOs.CutoffPolicy",
        "Arronix.Abstractions.DTOs.ImportDecision",
        "Arronix.Abstractions.DTOs.ImportReason",
        "Arronix.Abstractions.DTOs.Language",
        "Arronix.Abstractions.DTOs.LibraryPathSpec",
        "Arronix.Abstractions.DTOs.MatchConfidence",
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

    private static Assembly ContractAssembly => typeof(Arronix.Abstractions.Health.HealthCheck).Assembly;

    private static IReadOnlyList<Type> ExportedTypes => ContractAssembly.GetExportedTypes();

    [Test]
    public void EveryTypeAddedSinceTheInitialReleaseIsMarkedExperimental()
    {
        var unmarked = ExportedTypes
            .Where(static type => type.GetCustomAttribute<ExperimentalAttribute>() is null)
            .Select(static type => type.FullName ?? type.Name)
            .Where(static name => !StableSurface.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            unmarked,
            Is.Empty,
            "A contract type carries no experimental marker and is not part of the 0.1.0 surface. Until it "
            + "is promoted by an explicit version-history entry, consuming it must remain a compile error "
            + "that a consumer has to opt out of in writing.");
    }

    [Test]
    public void NoTypeFromTheInitialReleaseIsMarkedExperimental()
    {
        var regressions = ExportedTypes
            .Where(static type => type.GetCustomAttribute<ExperimentalAttribute>() is not null)
            .Select(static type => type.FullName ?? type.Name)
            .Where(static name => StableSurface.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(regressions, Is.Empty, "Marking a stable type experimental is itself a breaking change.");
    }

    [Test]
    public void TheStableSurfaceListDescribesTypesThatStillExist()
    {
        var exported = ExportedTypes
            .Select(static type => type.FullName ?? type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = StableSurface
            .Where(name => !exported.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            missing,
            Is.Empty,
            "A type on the stable list has been removed or renamed. Both are breaking changes at this "
            + "version, and neither should be discoverable only through this fixture going quiet.");
    }

    [Test]
    public void EveryExperimentalMarkerCarriesAnAreaIdentifierAndAPolicyLink()
    {
        var defects = ExportedTypes
            .Select(static type => (type, marker: type.GetCustomAttribute<ExperimentalAttribute>()))
            .Where(static pair => pair.marker is not null)
            .Where(static pair =>
                !DiagnosticIdPattern().IsMatch(pair.marker!.DiagnosticId)
                || pair.marker.UrlFormat is null
                || !pair.marker.UrlFormat.Contains("stability.md", StringComparison.Ordinal))
            .Select(static pair => $"{pair.type.FullName} => {pair.marker!.DiagnosticId}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            defects,
            Is.Empty,
            "An experimental marker must name a contract area and link to the stability policy, or the "
            + "consumer who has to suppress it has nothing to read before deciding whether to.");
    }

    [Test]
    public void EveryContractAreaInUseIsDocumentedInTheStabilityPolicy()
    {
        var policy = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "docs", "contracts", "stability.md"));

        var undocumented = ExportedTypes
            .Select(static type => type.GetCustomAttribute<ExperimentalAttribute>())
            .Where(static marker => marker is not null)
            .Select(static marker => marker!.DiagnosticId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !policy.Contains(id, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            undocumented,
            Is.Empty,
            "A diagnostic identifier is emitted by the contract assembly but appears nowhere in the "
            + "stability policy, so a consumer who hits it has no way to find out what they opted into.");
    }

    [Test]
    public void EveryTypeTheStabilityPolicyCallsStableIsOnTheStableSurfaceList()
    {
        // Keeps the prose and the assertion honest in the direction that matters: the document must not
        // promise stability for something this fixture would let go unmarked.
        var policy = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "docs", "contracts", "stability.md"));
        var section = InitialReleaseSection(policy);

        Assert.That(section, Is.Not.Empty, "The 0.1.0 section of the stability policy could not be located.");

        var simpleNames = StableSurface
            .Select(static name => name[(name.LastIndexOf('.') + 1)..])
            .ToHashSet(StringComparer.Ordinal);

        var claimed = BacktickedNamePattern()
            .Matches(section)
            .Select(static match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !simpleNames.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            claimed,
            Is.Empty,
            "The stability policy names a type in its initial-release section that this fixture does not "
            + "hold to be stable.");
    }

    private static string InitialReleaseSection(string policy)
    {
        const string Heading = "### 0.1.0";

        var start = policy.IndexOf(Heading, StringComparison.Ordinal);

        if (start < 0)
        {
            return string.Empty;
        }

        var end = policy.IndexOf("\n## ", start, StringComparison.Ordinal);

        return end < 0 ? policy[start..] : policy[start..end];
    }

    [GeneratedRegex("^ARX[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticIdPattern();

    [GeneratedRegex(@"`(?<name>[A-Z][A-Za-z0-9]*)`", RegexOptions.CultureInvariant)]
    private static partial Regex BacktickedNamePattern();
}
