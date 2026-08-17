#pragma warning disable ARX0013 // Shape contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0019
#pragma warning disable ARX0020 // Definition contracts are experimental; these tests exercise the declaration.

using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Host.Media;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Declaration;

/// <summary>
/// The real declaration, through the host's real gate.
/// </summary>
/// <remarks>
/// <para>
/// This is the assertion that makes the whole conversion checkable from here. <c>ValidatedDefinition</c>
/// resolves every cross-reference a definition makes over its own shape — a title-pattern capture naming a
/// coordinate component, a rung row naming a ladder tier, a query tier naming a search kind, a strategy
/// binding naming a strategy this host build carries, a corpus case naming a pattern — and refuses the
/// definition with every defect it found rather than with the first. A fixture written beside a validator
/// agrees with it by construction; this declaration was not.
/// </para>
/// <para>
/// The gate is public. The engines behind it are not: they are internal to <c>Arronix.Host</c> and shared
/// only with that assembly's own tests, which is why the release corpus cannot be executed from this
/// project and ships on the definition instead.
/// </para>
/// </remarks>
[TestFixture]
public class HostGateTests
{
    [Test]
    public void PassesTheHostsDefinitionGateWithNoDefects()
    {
        var accepted = ValidatedDefinition.TryValidate(
            MoviesDeclaration.Derived,
            out var validated,
            out var defects);

        Assert.Multiple(() =>
        {
            Assert.That(
                accepted,
                Is.True,
                string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));

            Assert.That(validated, Is.Not.Null);
            Assert.That(validated!.Kind, Is.EqualTo(Movies.Kind));
        });
    }

    [Test]
    public void ResolvesEveryDeclaredGuardAndPatternThroughTheGate()
    {
        ValidatedDefinition.TryValidate(MoviesDeclaration.Derived, out var validated, out _);

        Assert.Multiple(() =>
        {
            foreach (var guard in MoviesDeclaration.Parsing.Guards)
            {
                Assert.That(validated!.GuardOf(guard.GuardId).Regex, Is.EqualTo(guard.Regex));
            }

            foreach (var pattern in MoviesDeclaration.Parsing.TitlePatterns)
            {
                Assert.That(
                    validated!.PatternOf(pattern.PatternId).Regex,
                    Is.EqualTo(pattern.Regex));
            }
        });
    }

    /// <summary>
    /// The kind asks this host build for no strategy and no vocabulary, because it cannot.
    /// </summary>
    /// <remarks>
    /// Two cases used to sit here: one walked the declaration's strategy bindings and checked each against
    /// the host's inventory, the other walked its required enum ordinals and checked each against the
    /// host's maxima. Both were checks on a negotiation a typed kind does not have — it names no strategy
    /// by string and states no required vocabulary, because whatever it compiled against is what the
    /// compiler already checked. What is left to assert is that the negotiation is genuinely absent rather
    /// than merely unused, which is what this case does.
    /// </remarks>
    [Test]
    public void NeedsNoStrategyOrVocabularyNegotiationWithTheHost()
    {
        var members = typeof(MediaKindModel).GetProperties().Select(property => property.Name).ToList();

        Assert.That(
            members,
            Has.No.Member("Strategies").And.No.Member("RequiredVocabulary"),
            "a typed kind's strategies are methods and its vocabulary is what it compiled against");
    }
}
