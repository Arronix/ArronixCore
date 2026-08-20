
using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Host.Media;
using Arronix.Plugin.Movies.Definition;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Declaration;

/// <summary>
/// The real declaration, through the host's real gate.
/// </summary>
/// <remarks>
/// <para>
/// This is the assertion that makes the whole conversion checkable from here. <c>ValidatedDefinition</c>
/// resolves every cross-reference a definition makes over its own shape — a title-pattern capture naming a
/// coordinate component, a query tier naming a search kind, or a strategy binding naming a strategy this
/// host build carries — and refuses the
/// definition with every defect it found rather than with the first. A fixture written beside a validator
/// agrees with it by construction; this declaration was not.
/// </para>
/// <para>
/// This test exercises the public admission boundary. Parser behavior is verified separately through the
/// admitted kind's public parser seam.
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
            Assert.That(validated!.Kind, Is.EqualTo(new Movies().Kind));
        });
    }

    [Test]
    public void CarriesTheStaticParserTypeWithoutAParseDeclaration()
    {
        ValidatedDefinition.TryValidate(MoviesDeclaration.Derived, out var validated, out _);

        Assert.Multiple(() =>
        {
            Assert.That(validated, Is.Not.Null);
            Assert.That(MoviesDeclaration.Derived.ParserType, Is.EqualTo(typeof(MovieReleaseParser)));
            Assert.That(MoviesDeclaration.Carried.Parsing, Is.Null);
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
