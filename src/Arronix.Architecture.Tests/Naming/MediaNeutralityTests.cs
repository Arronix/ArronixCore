using System.Linq;
using System.Reflection;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Naming;

/// <summary>
/// Rule 5 - invariant 1. No media noun outside a media extension.
/// </summary>
/// <remarks>
/// <para>
/// The platform is a unified host for many media kinds, and the whole design rests on the host knowing
/// none of them. The moment a runtime type is called an episode, that runtime has a favorite media kind
/// and every other kind becomes a special case of it. The four *arr code bases this delivery replaces are
/// each an existence proof of exactly that.
/// </para>
/// <para>
/// Matching is by word, not by substring, and the distinction is what makes this rule usable. The nouns
/// below appear inside perfectly innocent English identifiers - authorization, tracking, a bookmark - and
/// a rule that failed on those would be turned off within a week. Words are extracted the way a reader
/// sees them, so <c>AuthorName</c> fails and <c>Authorization</c> passes.
/// </para>
/// </remarks>
[TestFixture]
public class MediaNeutralityTests
{
    /// <summary>Gets the assemblies that must know no media kind.</summary>
    public static IEnumerable<string> MediaNeutralProjects => RepositoryLayout.MediaNeutralProjects;

    [Test]
    [TestCaseSource(nameof(MediaNeutralProjects))]
    public void ProjectDeclaresNoMediaSpecificType(string projectName)
    {
        var declarations = SourceScanner.DeclaredTypes(projectName);

        Assert.That(
            declarations,
            Is.Not.Empty,
            $"No type declaration was found in '{projectName}'. Either the project moved or the reader "
            + "stopped working; both make this rule vacuous, so both fail.");

        var offenders = declarations
            .Where(declaration => MediaVocabulary.Names(declaration.Name))
            .Select(static declaration => declaration.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' declares a type named after one media kind. Invariant 1: a media noun lives "
            + "in an Arronix.Plugin.* assembly and nowhere else.");
    }

    [Test]
    public void ContractSurfaceDeclaresNoMediaSpecificMember()
    {
        // Depth where it matters most. A neutral type with a member called SeasonNumber is exactly as
        // corrosive as a type called Season, and the contract assembly is the one surface every extension,
        // every front end and the host all read.
        var offenders = new List<string>();

        foreach (var type in ContractTypes())
        {
            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // Accessors are the compiler's spelling of a property that is already in this scan, and a
                // constructor is named after its type, which is scanned separately.
                if (member.MemberType == MemberTypes.Constructor || member is MethodInfo { IsSpecialName: true })
                {
                    continue;
                }

                if (MediaVocabulary.Names(member.Name))
                {
                    offenders.Add($"{type.FullName}.{member.Name}");
                }
            }
        }

        Assert.That(
            offenders.Order(StringComparer.Ordinal).ToArray(),
            Is.Empty,
            "A contract member is named after one media kind.");
    }

    [Test]
    public void ContractSurfaceDeclaresNoMediaSpecificEnumValue()
    {
        var offenders = ContractTypes()
            .Where(static type => type.IsEnum)
            .SelectMany(static type => Enum.GetNames(type).Select(name => $"{type.FullName}.{name}"))
            .Where(entry => MediaVocabulary.Names(entry[(entry.LastIndexOf('.') + 1)..]))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "A closed vocabulary in the contract assembly names one media kind. A vocabulary is the "
            + "hardest thing in the platform to widen later, so this is the worst place for it to happen.");
    }

    private static IReadOnlyList<Type> ContractTypes() =>
        typeof(Arronix.Abstractions.Health.HealthCheck).Assembly.GetExportedTypes();
}
