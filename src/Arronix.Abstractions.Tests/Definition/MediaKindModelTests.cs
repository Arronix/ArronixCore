// Exercises the per-kind engine inputs a typed media kind carries.

using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;

namespace Arronix.Abstractions.Tests.Definition;

[TestFixture]
public class MediaKindModelTests
{
    /// <summary>
    /// A kind that says nothing about a defaulted section gets the host's behavior for it, by reference.
    /// </summary>
    /// <remarks>
    /// Identity rather than equality is the assertion that matters: the capability rules decide whether a
    /// section is a contribution by comparing it against these defaults, so a default that were merely equal
    /// would still be a default, but one that were a fresh instance each time would make the comparison a
    /// structural one by accident rather than by design.
    /// </remarks>
    [Test]
    public void MinimalModelAppliesTheDeclaredSectionDefaults()
    {
        var model = DefinitionFixtures.Model();

        Assert.Multiple(() =>
        {
            Assert.That(model.Naming, Is.SameAs(NamingDeclaration.Default));
            Assert.That(model.Notifications, Is.SameAs(NotificationDeclaration.Default));
        });
    }

    /// <summary>
    /// The model carries no structure, no intent, no strategy table and no required vocabulary.
    /// </summary>
    /// <remarks>
    /// The absences are the design. Structure and intent are derived from the item type, so carrying them
    /// here would be a second source of truth for them; a strategy is a method on the kind's own type; and a
    /// typed kind's vocabulary is whatever it compiled against, which the compiler already checked. This
    /// case fails if any of the four is ever added back.
    /// </remarks>
    [Test]
    public void TheModelCarriesNoStructureIntentStrategyOrVocabularySection()
    {
        var members = typeof(MediaKindModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.That(
            members,
            Has.No.Member("Shape")
                .And.No.Member("Intent")
                .And.No.Member("Strategies")
                .And.No.Member("RequiredVocabulary")
                .And.No.Member("Quality"));
    }
}
