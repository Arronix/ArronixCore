using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Contracts;

/// <summary>
/// Below 1.0.0 a wrong contract is deleted, not deprecated - and this fixture is what keeps that true.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/contracts/stability.md</c> once granted 1.0-grade guarantees to a 0.x contract library with no
/// external consumers, and then forbade deleting an unused contract. The consequences were mechanical and
/// they were everywhere: a stringly-typed key bag shipped beside a record that "could not change", a wrapper
/// DTO beside an unused one, a sentinel value chosen because reshaping would be "breaking", and a
/// host-side bridge written so a superseded provider contract need not be removed. Every one of those was
/// argued for correctly from a premise that was false. The policy has been rewritten; this fixture exists
/// because a rewritten document persuades the next contributor only if they read it.
/// </para>
/// <para>
/// Two rules, both chosen for having no plausible innocent instance rather than for breadth. A governance
/// rule that fires on a legitimate design is worse than one that misses a bad one, because the first thing
/// a false positive teaches is how to suppress the rule.
/// </para>
/// </remarks>
[TestFixture]
public partial class PreReleaseStabilityTests
{
    /// <summary>
    /// Words that cannot appear in a contract-assembly type name, whatever the type does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scoped to <c>Arronix.Abstractions</c> deliberately. This is the assembly extensions compile against,
    /// so it is the only one where a second shape can outlive the decision that created it - the host may
    /// legitimately own a translation between two of its own internal representations, and calling that a
    /// shim is a naming question, not an architectural one.
    /// </para>
    /// <para>
    /// "Bridge" is on the list and "Adapter" is not. <c>StableProviderBridge</c> was the archetype of this
    /// defect and the word carries no other meaning here, whereas an adapter is an ordinary structural
    /// pattern that an honest contract may well need.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenContractWords =
    [
        "Backport",
        "Bridge",
        "Compat",
        "Compatibility",
        "Deprecated",
        "Legacy",
        "Obsolete",
        "Shim"
    ];

    /// <summary>Gets every project this delivery owns, test projects included.</summary>
    public static IEnumerable<string> AllProjects => RepositoryLayout.AllProjects;

    /// <summary>
    /// Nothing in this delivery marks anything obsolete.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is nobody to give notice to. Nothing has ever shipped against an Arronix contract, so a
    /// deprecation marker cannot be doing the job it exists for; what it does instead is keep a shape that
    /// is known to be wrong compiling, and buy that with a promise to remove it at a major version which,
    /// below 1.0.0, means 1.0.0 - the release that is supposed to be the first one worth committing to.
    /// </para>
    /// <para>
    /// The check reads applications, not prose: a comment recording that this marker is not used must not
    /// fail the rule that says so. It covers test projects too, because a fixture asserting the old ladder
    /// still works is how the ladder comes back.
    /// </para>
    /// </remarks>
    /// <param name="projectName">The project.</param>
    [Test]
    [TestCaseSource(nameof(AllProjects))]
    public void ProjectMarksNothingObsolete(string projectName)
    {
        var offenders = SourceScanner
            .CodeLines(projectName, "*.cs", "*.razor")
            .Where(static entry => ObsoleteAttributePattern().IsMatch(entry.Text))
            .Select(static entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' marks something obsolete. Below 1.0.0 a contract with no implementer outside "
                + "this repository is deleted, not deprecated - see docs/contracts/stability.md.");
    }

    /// <summary>
    /// The contract assembly declares no type whose name is an admission that a second shape exists.
    /// </summary>
    /// <remarks>
    /// Word splitting rather than substring matching, so <c>ForwardCompatibilityTests</c> would fail on the
    /// word "Compatibility" while a member documented as aiding forward compatibility is untouched - this
    /// rule reads declared names only, never prose.
    /// </remarks>
    [Test]
    public void ContractAssemblyDeclaresNoCompatibilityShape()
    {
        var declarations = SourceScanner.DeclaredTypes(RepositoryLayout.Abstractions);

        Assert.That(declarations, Is.Not.Empty, "No type declaration was found in the contract assembly.");

        var offenders = declarations
            .Where(static declaration => NamesACompatibilityShape(declaration.Name))
            .Select(static declaration => declaration.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "The contract assembly declares a type whose name says it exists to keep an older shape "
                + "reachable. Below 1.0.0 the older shape is deleted instead - see docs/contracts/stability.md.");
    }

    /// <summary>
    /// Both detectors find what they are for, and neither finds what only looks like it.
    /// </summary>
    /// <remarks>
    /// Without this, a typo in either rule turns the whole fixture into a green run that checks nothing -
    /// and a governance rule nobody can tell is dead is worse than no rule, because it is trusted.
    /// </remarks>
    [Test]
    public void BothDetectorsAreControlled()
    {
        var marker = "[" + "Obsolete";

        Assert.Multiple(() =>
        {
            Assert.That(ObsoleteAttributePattern().IsMatch(marker + "]"), Is.True);
            Assert.That(ObsoleteAttributePattern().IsMatch(marker + @"(""Use IIndexer instead."")]"), Is.True);
            Assert.That(ObsoleteAttributePattern().IsMatch("[System." + "Obsolete]"), Is.True);

            // A member whose name merely contains the word is not an application of the attribute.
            Assert.That(ObsoleteAttributePattern().IsMatch("var isObsolete = false;"), Is.False);
            Assert.That(ObsoleteAttributePattern().IsMatch("results[ObsoleteCount] = 0;"), Is.False);

            Assert.That(NamesACompatibilityShape("StableProviderBridge"), Is.True);
            Assert.That(NamesACompatibilityShape("TagsToParsedReleaseBridge"), Is.True);
            Assert.That(NamesACompatibilityShape("CompatibilityLayer"), Is.True);
            Assert.That(NamesACompatibilityShape("LegacyImportShim"), Is.True);

            // Word splitting, not substring matching: none of these contains a forbidden word.
            Assert.That(NamesACompatibilityShape("IProvider"), Is.False);
            Assert.That(NamesACompatibilityShape("HealthSnapshotView"), Is.False);
            Assert.That(NamesACompatibilityShape("JobExecutionContext"), Is.False);
        });
    }

    private static bool NamesACompatibilityShape(string typeName) =>
        SourceScanner
            .Words(typeName)
            .Any(static word => ForbiddenContractWords.Contains(word, StringComparer.OrdinalIgnoreCase));

    [GeneratedRegex(@"\[\s*(?:System\s*\.\s*)?Obsolete\s*[\](]", RegexOptions.CultureInvariant)]
    private static partial Regex ObsoleteAttributePattern();
}
